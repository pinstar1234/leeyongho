using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.Grid;
using InfraTown.Population;
using InfraTown.Power;
using InfraTown.Satisfaction;
using InfraTown.Save;
using InfraTown.Services;
using InfraTown.TimeSystem;
using InfraTown.UI;

namespace InfraTown.Tests.Playtest
{
    /// <summary>
    /// 실제 씬을 플레이 모드로 돌리며 기능 전체를 시나리오로 확인하는 자동 플레이 테스트.
    /// Tools/run-playtest.ps1 이 배치 모드에서 -executeMethod 로 실행한다 (사용 중인 에디터에서는 동작하지 않음).
    /// 씬에 없는 매니저·건물 목록은 플레이 직전에 메모리에서만 채우고(저장 안 함) 보고서에 적는다.
    /// 결과: -playtestOut 폴더의 report.md 와 스크린샷 PNG.
    /// </summary>
    [InitializeOnLoad]
    public static partial class PlaytestRunner
    {
        private const string ActiveKey = "InfraTown.Playtest.Active";
        private const string OutKey = "InfraTown.Playtest.Out";
        private const string SceneKey = "InfraTown.Playtest.Scene";
        private const string InjectedKey = "InfraTown.Playtest.Injected";
        private const string ModeKey = "InfraTown.Playtest.Mode";
        private const string DefaultScene = "Assets/Scenes/1park2.unity";

        // 밸런스 기준: 중간 단계 마을(주택 30채 + Tier 1~2 시설) 평균 만족도 목표 (사용자 결정 2026-11-12)
        private const float MiddleTargetSatisfaction = 84f;
        private const float MiddleTolerance = 1.5f;
        private const string SpecFolder = "Assets/Data/Build/Spec";
        private const float TimeoutSeconds = 900f;

        private static readonly Stack<IEnumerator> Steps = new Stack<IEnumerator>();
        private static readonly List<string> Report = new List<string>();
        private static readonly List<string> ConsoleErrors = new List<string>();
        private static int _passed, _failed;
        private static bool _started;
        private static double _startTime;

        static PlaytestRunner()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            EditorApplication.update += Tick;
        }

        /// <summary>-executeMethod InfraTown.Tests.Playtest.PlaytestRunner.Run [-playtestScene 경로] [-playtestOut 폴더]</summary>
        public static void Run()
        {
            if (!Application.isBatchMode)
            {
                Debug.LogWarning("[Playtest] 배치 모드 전용입니다. Tools/run-playtest.ps1 로 실행하세요.");
                return;
            }

            string scene = GetArg("-playtestScene") ?? DefaultScene;
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(OutKey, GetArg("-playtestOut") ?? Path.GetFullPath("Logs/Playtest"));
            SessionState.SetString(SceneKey, scene);
            SessionState.SetString(ModeKey, GetArg("-playtestMode") ?? "full");

            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            SessionState.SetString(InjectedKey, string.Join("\n", PrepareScene()));

            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode(); // 도메인 리로드 후 static 생성자에서 Tick 재등록
        }

        /// <summary>
        /// 플레이에 필요한데 씬에 없는 것을 메모리에서만 채운다 (씬 파일은 저장하지 않음)
        /// </summary>
        private static List<string> PrepareScene()
        {
            var notes = new List<string>();
            var required = new[]
            {
                typeof(GameLoopManager), typeof(EconomyManager), typeof(PopulationManager), typeof(PowerManager),
                typeof(SatisfactionManager), typeof(ServiceAccessibilityManager), typeof(TimeManager),
                typeof(BuildingManager), typeof(LightweightGridManager)
            };

            GameObject host = null;
            foreach (var type in required)
            {
                if (UnityEngine.Object.FindObjectOfType(type) != null) continue;
                if (host == null) host = new GameObject("[Playtest] 씬에 없던 매니저");
                host.AddComponent(type);
                notes.Add($"씬에 `{type.Name}` 없음 → 임시 추가");
            }

            var spec = AssetDatabase.FindAssets("t:BuildingData", new[] { SpecFolder })
                .Select(g => AssetDatabase.LoadAssetAtPath<BuildingData>(AssetDatabase.GUIDToAssetPath(g)))
                .OrderBy(d => d.unlockTier).ThenBy(d => d.name).ToList();

            foreach (var manager in UnityEngine.Object.FindObjectsOfType<BuildingManager>())
            {
                var so = new SerializedObject(manager);
                var list = so.FindProperty("buildingDatabase");
                var current = Enumerable.Range(0, list.arraySize)
                    .Select(i => list.GetArrayElementAtIndex(i).objectReferenceValue as BuildingData).ToList();
                if (spec.All(current.Contains)) continue;

                string before = string.Join(", ", current.Select(d => d != null ? d.name : "(빈 칸)"));
                list.arraySize = spec.Count;
                for (int i = 0; i < spec.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = spec[i];
                so.ApplyModifiedPropertiesWithoutUndo();
                notes.Add($"`{manager.gameObject.name}`의 건물 목록 [{before}] → 기획서 13종으로 임시 교체");
            }

            return notes;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) return;

            if (!_started)
            {
                _started = true;
                _startTime = EditorApplication.timeSinceStartup;
                Application.logMessageReceived += OnLog;
                Application.targetFrameRate = -1;
                QualitySettings.vSyncCount = 0;
                Steps.Push(Scenario());
            }

            if (EditorApplication.timeSinceStartup - _startTime > TimeoutSeconds)
            {
                Fail("전체 시간 초과", $"{TimeoutSeconds}초 안에 끝나지 않음");
                Finish(1);
                return;
            }

            try
            {
                while (Steps.Count > 0)
                {
                    var top = Steps.Peek();
                    if (!top.MoveNext()) { Steps.Pop(); continue; }
                    if (top.Current is IEnumerator nested) { Steps.Push(nested); continue; }
                    return; // null: 다음 프레임까지 대기
                }
                Finish(_failed > 0 ? 2 : 0);
            }
            catch (Exception e)
            {
                Fail("시나리오 예외", e.ToString());
                Finish(1);
            }
        }

        // ───────────────────────── 시나리오 ─────────────────────────

        private static IEnumerator Scenario()
        {
            // 자동 저장(1주마다)이 실제 세이브 폴더(persistentDataPath)를 덮어쓰지 않도록 테스트 폴더로 (save 모드도 같은 폴더를 비우고 씀)
            SaveSystem.FolderOverrideForTests = Path.Combine(SessionState.GetString(OutKey, "Logs/Playtest"), "Saves");
            Section("0. 씬 시작");
            string injected = SessionState.GetString(InjectedKey, "");
            if (string.IsNullOrEmpty(injected)) Note("씬에 필요한 매니저·건물 목록이 모두 있음");
            else foreach (var line in injected.Split('\n')) Note("⚠ " + line);
            yield return Frames(3);
            yield return BootChecks();

            if (SessionState.GetString(ModeKey, "full") == "save")
            {
                yield return SaveScenario(); // PlaytestSave.cs
                yield break;
            }

            if (SessionState.GetString(ModeKey, "full") == "dialogue")
            {
                yield return DialogueScenario(); // PlaytestDialogue.cs
                yield break;
            }

            if (SessionState.GetString(ModeKey, "full") == "rotation")
            {
                yield return RotationScenario();
                yield break;
            }

            Section("1-1. 중간 단계 마을 — 주택 30채 + Tier 1~2 시설 (실제 배치 경로)");
            yield return PlaceHouses();
            yield return PlaceMiddle(withNimby: true);
            yield return EnsurePower();
            float middle = SatisfactionSummary("중간 단계 마을");
            Check($"중간 단계 평균 만족도 ≈ {MiddleTargetSatisfaction}% (±{MiddleTolerance})",
                Mathf.Abs(middle - MiddleTargetSatisfaction) <= MiddleTolerance, $"{middle:F1}%");
            Shot("01_middle_village", null);

            Section("1-2. 완성된 마을 — 필수 시설 4종 추가");
            var dialogueTriggers = UnityEngine.Object.FindObjectOfType<InfraTown.EventSystem.DialogueTriggers>();
            var firstInstalled = new List<ServiceType>();
            if (dialogueTriggers != null) dialogueTriggers.OnFacilityFirstInstalled += t => firstInstalled.Add(t);

            yield return PlaceEssentials(withNimby: true);
            yield return EnsurePower();

            if (dialogueTriggers != null)
            {
                var dialogue = InfraTown.EventSystem.DialogueEventManager.Instance;
                Check("[대화] 씬에 대화 매니저 있음", dialogue != null);
                Check("[대화] 필수 시설 4종을 처음 지을 때 설치 대사", firstInstalled.Count == 4,
                    string.Join(", ", firstInstalled));
                Check("[대화] 대화창 UI가 없으면 대사는 콘솔에만, 게임은 멈추지 않음",
                    dialogue != null && !dialogue.IsPlaying && !TimeManager.Instance.IsEventPaused);
                Note($"[대화] 지금까지 재생된 대화 {dialogue?.PlayedDialogueIds.Count}개: {string.Join(", ", dialogue?.PlayedDialogueIds ?? new string[0])}");
            }
            else Note("[대화] 이 씬에는 DialogueTriggers 없음");
            float full = SatisfactionSummary("완성된 마을");
            Check("완성된 마을이 중간 단계보다 만족도가 높다", full > middle, $"{middle:F1}% → {full:F1}%");
            yield return VillageChecks();

            Section("2. UI — 종합 현황 / 만족도 / 건물 정보");
            yield return OverviewChecks();
            yield return BuildingInfoUiCheck();

            Section("3. 월간 정산");
            yield return MonthlySettlementCheck();

            Section("3-1. 한 달 스킵 버튼 (한 주씩 빠르게, 이벤트가 나면 멈춤)");
            yield return MonthSkipCheck();

            Section("3-2. 계절 — 전력·가스 사용량 (1년 진행)");
            yield return SeasonCheck();

            Section("4. 전신주(전력망) 설치");
            yield return PowerPoleCheck();

            Section("5. 철거 — 코드 경로와 UI [철거] 버튼");
            yield return DemolishCheck();

            Section("6. 배드엔딩 (나쁨 12주 = 3달)");
            yield return DefeatCheck();

            Section("7. 해피엔딩 (같은 판에서 엔딩 판정을 새로 시작해 10년 진행)");
            yield return VictoryCheck();
        }

        private static IEnumerator BootChecks()
        {
            Check("GameLoopManager", GameLoopManager.Instance != null);
            Check("EconomyManager", EconomyManager.Instance != null);
            Check("PopulationManager", PopulationManager.Instance != null);
            Check("PowerManager", PowerManager.Instance != null);
            Check("SatisfactionManager", SatisfactionManager.Instance != null);
            Check("ServiceAccessibilityManager", ServiceAccessibilityManager.Instance != null);
            Check("TimeManager", TimeManager.Instance != null);
            Check("BuildingManager", BuildingManager.Instance != null);
            Check("GameOutcomeManager 자동 추가", GameOutcomeManager.Instance != null);
            Check("GasManager (씬에 없으면 자동 추가)", InfraTown.Gas.GasManager.Instance != null);

            int managers = UnityEngine.Object.FindObjectsOfType<BuildingManager>().Length;
            Check("BuildingManager 1개", managers == 1, $"{managers}개");
            Check("LightweightGridManager 살아 있음", LightweightGridManager.Instance != null);
            Check("건물 데이터 13종", BuildingManager.Instance.AllBuildingData.Count == 13, $"{BuildingManager.Instance.AllBuildingData.Count}종");

            var eco = EconomyManager.Instance;
            var power = PowerManager.Instance;
            var gas = InfraTown.Gas.GasManager.Instance;
            yield return Frames(1); // 자동 추가된 가스 매니저의 Start (첫 달 구매)
            Check("시작 시 첫 달 전력·가스 구매 (전력 200kW + 가스 100, 10,000 → 7,000원)",
                power.CurrentAvailablePower == 200 && gas != null && gas.CurrentAvailableGas == 100 && eco.CurrentMoney == 7000,
                $"전력 {power.CurrentAvailablePower}kW, 가스 {gas?.CurrentAvailableGas}, 잔액 {eco.CurrentMoney:N0}원");
            Check("1년 1월 1주차, 겨울에 시작", TimeManager.Instance.CurrentMonth == 1 && TimeManager.Instance.CurrentWeek == 1 && TimeManager.Instance.CurrentSeason == Season.Winter,
                $"{TimeManager.Instance.CurrentMonth}월 {TimeManager.Instance.CurrentWeek}주차 {TimeManager.Instance.CurrentSeason}");
        }

        private static readonly Dictionary<string, BuildingInstance> Placed = new Dictionary<string, BuildingInstance>();

        private static IEnumerator PlaceHouses()
        {
            EconomyManager.Instance.AddMoney(1_000_000); // 테스트 자금

            // 사용자 기획 2026-11-24: 건물은 도로에 맞닿아야 설치 → 도로 없이 지으면 실패, 마을 도로를 먼저 깖
            Check("도로에 닿지 않은 주택은 지어지지 않음", Place("house", 45, 35, null) == null && (BuildingManager.Instance.LastPlacementError ?? "").Contains("도로"),
                BuildingManager.Instance.LastPlacementError);
            int roads = LayTownRoads();
            Check("마을 도로 60칸 (y=1, y=4 줄)", roads == 60, $"{roads}칸");

            int placedHouses = 0;
            for (int x = 0; x < 30; x++)
            {
                if (Place("house", x, 0, $"house{x}") != null) placedHouses++;
            }
            Check("주택 30채 배치 (y=0 줄)", placedHouses == 30, $"{placedHouses}채");

            var pop = PopulationManager.Instance;
            Check("수용 인구 = 30채 × 10", pop.MaxPopulationCapacity == 300, $"{pop.MaxPopulationCapacity}명");
            Check("인구 0명일 때 Tier 4 병원 잠김", !BuildingManager.Instance.IsBuildingUnlocked(Data("hospital")));
            Check("잠긴 건물은 지어지지 않음", Place("hospital", 2, 2, null) == null);

            int before = pop.CurrentPopulation;
            yield return WaitWeeks(1);
            Note($"주간 유입: {before}명 → {pop.CurrentPopulation}명 (만족도 {SatisfactionManager.Instance.CurrentAverageSatisfaction:F1})");
            Check("만족도 50 이상이면 주간 인구 유입", pop.CurrentPopulation > before);

            pop.AddPopulation(300);
            Check("인구 300명 → Tier 4까지 해금", BuildingManager.Instance.IsBuildingUnlocked(Data("hospital")));
        }

        /// <summary>
        /// 밸런스 기준 배치. 주택 y=0 줄(x 0~29), Tier 1~2 시설 y=2 줄에 7칸 간격, 필수 시설 y=5 줄, 불쾌 시설은 멀리.
        /// (Tools 쪽 오프라인 계산과 같은 배치)
        /// </summary>
        private static IEnumerator PlaceMiddle(bool withNimby)
        {
            var keys = new List<string>();
            void P(string id, int x, int y, string key) { Place(id, x, y, key); keys.Add(key); }

            LayTownRoads(); // 배드엔딩 뒤 재건할 때는 도로도 다시 — 시설보다 먼저 깔아야 도로 접근 조건을 통과
            P("village_hall", 2, 2, "hall");
            P("convenience_store", 9, 2, "store");
            P("bus_stop", 16, 2, "bus");
            P("senior_center", 23, 2, "senior");
            // 사용자 결정 2026-11-24: 전봇대 범위 밖 건물은 전기가 꺼지므로 기준 마을 전체를 덮는 전봇대 4개 (반경 4) — 도로 위에 세움
            P("power_pole", 4, 1, "pole_a");
            P("power_pole", 13, 1, "pole_b");
            P("power_pole", 22, 1, "pole_c");
            P("power_pole", 29, 1, "pole_d");
            if (withNimby) { PlaceWithRoad("livestock_farm", 50, 30, "farm"); keys.Add("farm"); }

            int ok = keys.Count(k => Placed.TryGetValue(k, out var b) && b != null);
            Check("Tier 1~2 시설 배치", ok == keys.Count, $"{ok}/{keys.Count}");
            yield break;
        }

        private static IEnumerator PlaceEssentials(bool withNimby)
        {
            var keys = new List<string>();
            void P(string id, int x, int y, string key) { Place(id, x, y, key); keys.Add(key); }

            P("hospital", 2, 5, "hospital");
            P("school", 9, 5, "school");
            P("fire_station", 16, 5, "fire");
            P("police_station", 20, 5, "police");
            if (withNimby) { PlaceWithRoad("landfill", 54, 34, "landfill"); keys.Add("landfill"); }

            int ok = keys.Count(k => Placed.TryGetValue(k, out var b) && b != null);
            Check("Tier 3~4 필수 시설 배치", ok == keys.Count, $"{ok}/{keys.Count}");
            yield break;
        }

        private static float SatisfactionSummary(string label)
        {
            var sat = SatisfactionManager.Instance;
            var values = Placed.Where(kv => kv.Key.StartsWith("house") && kv.Value != null)
                .Select(kv => kv.Value.GetComponent<HousingInstance>().CurrentSatisfaction).ToList();
            var bonus = Placed.Where(kv => kv.Key.StartsWith("house") && kv.Value != null)
                .Select(kv => kv.Value.GetComponent<HousingInstance>().AccessBonus).ToList();
            Note($"{label}: 평균 **{sat.CurrentAverageSatisfaction:F1}%** (최저 {values.Min():F1} / 최고 {values.Max():F1}), 접근 보너스 평균 +{bonus.Average():F1}, 좋음 {sat.GoodHousingRatio:P0}, 상태 {sat.CurrentState}");
            return sat.CurrentAverageSatisfaction;
        }

        private static IEnumerator EnsurePower()
        {
            var power = PowerManager.Instance;
            if (power.IsPowerShortage)
            {
                Note($"⚠ 기본 구매량 {power.TargetPurchasedPower}kW로는 이 마을({power.CurrentUsedPower}kW)을 감당 못 함 (정전 감점 {power.GetPowerBlackoutSatisfactionPenalty():F1}) → 목표 1,000kW로 올려 즉시 구매");
                power.SetTargetPurchasedPower(1000);
                power.ProcessMonthlyPowerPurchase();
            }
            var gas = InfraTown.Gas.GasManager.Instance;
            if (gas != null && gas.IsGasShortage)
            {
                Note($"⚠ 기본 가스 구매량 {gas.TargetPurchasedGas}로는 이 마을({gas.CurrentUsedGas})을 감당 못 함 → 목표 500으로 올려 즉시 구매");
                gas.SetTargetPurchasedGas(500);
                gas.ProcessMonthlyGasPurchase();
            }
            yield return Frames(1);
            Check("전력 충분 → 정전 감점 0", power.GetPowerBlackoutSatisfactionPenalty() == 0f,
                $"{power.CurrentUsedPower} / {power.CurrentAvailablePower} kW (건물 {power.BuildingPowerUsage} + 인구·계절 {power.SeasonalPowerUsage})");
            Check("가스 충분 → 가스 부족 감점 0", gas == null || gas.GetGasShortageSatisfactionPenalty() == 0f,
                $"{gas?.CurrentUsedGas} / {gas?.CurrentAvailableGas}");
        }

        private static IEnumerator VillageChecks()
        {
            var grid = GetGrid();
            Check("4x4 병원이 (2..5, 5..8) 16칸 점유",
                Enumerable.Range(2, 4).All(x => Enumerable.Range(5, 4).All(y => grid[x, y] == Placed["hospital"])));

            var service = ServiceAccessibilityManager.Instance;
            Check("필수 시설 4종 + 편의점 인식",
                service.HasHospital && service.HasSchool && service.HasFireStation && service.HasPoliceStation && service.HasConvenienceStore);
            Note($"평균 접근 시간: 병원 {service.HospitalAccessTime:F1}분 / 학교 {service.SchoolAccessTime:F1}분 / 소방 {service.FireStationAccessTime:F1}분 / 경찰 {service.PoliceAccessTime:F1}분 / 편의점 {service.ConvenienceAccessTime:F1}분");
            float near = service.GetAccessTime(Placed["house2"], ServiceType.Hospital);
            float far = service.GetAccessTime(Placed["house29"], ServiceType.Hospital);
            Check("먼 주택일수록 병원 접근 시간이 길다", far > near, $"x=2: {near:F1}분, x=29: {far:F1}분");

            var sat = SatisfactionManager.Instance;
            var h2 = Placed["house2"].GetComponent<HousingInstance>();
            var h29 = Placed["house29"].GetComponent<HousingInstance>();
            Check("접근 보너스 합계 ≤ 10", h2.AccessBonus <= 10.001f && h29.AccessBonus <= 10.001f,
                $"x=2: +{h2.AccessBonus:F1}, x=29: +{h29.AccessBonus:F1}");
            Note($"주택 만족도: x=2 {h2.CurrentSatisfaction:F1}({h2.State}), x=29 {h29.CurrentSatisfaction:F1}({h29.State})");
            Note($"마을: 평균 {sat.CurrentAverageSatisfaction:F1} ({sat.CurrentTier}), 좋음 비율 {sat.GoodHousingRatio:P0}, 상태 {sat.CurrentState}");

            var dist = Placed.Where(kv => kv.Key.StartsWith("house")).Select(kv => kv.Value.GetComponent<HousingInstance>().CurrentSatisfaction).ToList();
            Note($"주택 30채 만족도 분포: 최저 {dist.Min():F1} / 평균 {dist.Average():F1} / 최고 {dist.Max():F1}, 100점인 집 {dist.Count(v => v >= 99.99f)}채");

            Shot("01_village", null);
            yield break;
        }

        private static IEnumerator OverviewChecks()
        {
            var overview = UnityEngine.Object.FindObjectOfType<OverviewUI>(true);
            if (overview == null) { Note("OverviewUI 없음 — 종합 현황 확인 생략"); yield break; }

            var time = TimeManager.Instance;
            overview.ShowOverview();
            yield return RealSeconds(0.6f);

            var panel = TestWorld.GetField<GameObject>(overview, "overviewPanel");
            Check("종합 현황 열기 → 패널 표시", panel != null && panel.activeInHierarchy);
            Check("종합 현황이 열려 있는 동안 시간 정지", time.IsEventPaused);

            var eco = EconomyManager.Instance;
            var pop = PopulationManager.Instance;
            var power = PowerManager.Instance;
            var sat = SatisfactionManager.Instance;
            var service = ServiceAccessibilityManager.Instance;

            UiText(overview, "powerText", $"{power.CurrentUsedPower} / {power.CurrentAvailablePower} kW");
            UiText(overview, "populationText", $"{pop.CurrentPopulation}명 / 한계: {pop.MaxPopulationCapacity}명");
            UiText(overview, "satisfactionText", $"{Mathf.RoundToInt(sat.CurrentAverageSatisfaction)}%");
            UiText(overview, "subsidyText", $"{eco.GetRequestedSubsidy():N0} 원 / 최대: {eco.GetMaxEligibleSubsidy():N0} 원");
            UiText(overview, "patrolCountText", $"{service.DailyPolicePatrolCount} 회");
            UiText(overview, "hospitalTimeText", $"{service.HospitalAccessTime:F1} 분");
            UiText(overview, "fireStationTimeText", $"{service.FireStationAccessTime:F1} 분");
            UiText(overview, "convenienceTimeText", $"{service.ConvenienceAccessTime:F1} 분");
            UiText(overview, "complaintsText", service.GetServiceComplaintSummary());
            Note("종합 현황 UI에 학교 접근 시간 칸은 없음 (매니저에는 SchoolAccessTime 있음)");

            var slider = TestWorld.GetField<Slider>(overview, "subsidySlider");
            if (slider != null)
            {
                Check("지원금 슬라이더 최대값 = 최대 지원금", Mathf.Approximately(slider.maxValue, eco.GetMaxEligibleSubsidy()),
                    $"{slider.maxValue:N0} / {eco.GetMaxEligibleSubsidy():N0}");
                slider.value = slider.maxValue;
                yield return Frames(1);
                Check("슬라이더를 끝까지 올리면 신청 지원금 = 최대", eco.GetRequestedSubsidy() == eco.GetMaxEligibleSubsidy(),
                    $"{eco.GetRequestedSubsidy():N0}원");
                UiText(overview, "subsidyText", $"{eco.GetRequestedSubsidy():N0} 원");
            }
            else Note("subsidySlider 미연결");

            var powerSlider = TestWorld.GetField<Slider>(overview, "powerSlider");
            if (powerSlider != null)
            {
                int availableBefore = power.CurrentAvailablePower;
                int moneyBefore = eco.CurrentMoney;
                Check("전력 슬라이더 최대값 = 최대 구매량", Mathf.Approximately(powerSlider.maxValue, power.MaxPurchasablePower),
                    $"{powerSlider.maxValue:N0} kW");
                Check("전력 슬라이더 = 현재 다음 달 구매량", Mathf.RoundToInt(powerSlider.value) == power.TargetPurchasedPower,
                    $"{powerSlider.value} / {power.TargetPurchasedPower}");

                powerSlider.value = 803; // 10kW 단위로 맞춰짐
                yield return Frames(1);
                Check("슬라이더 → 다음 달 구매량 800kW (10kW 단위)", power.TargetPurchasedPower == 800, $"{power.TargetPurchasedPower} kW");
                Check("이번 달 보유 전력·잔액은 그대로 (다음 달부터 적용)",
                    power.CurrentAvailablePower == availableBefore && eco.CurrentMoney == moneyBefore,
                    $"보유 {power.CurrentAvailablePower} kW, 잔액 {eco.CurrentMoney:N0}");
                UiText(overview, "powerPurchaseText", "800 kW (월 8,000 원)");

                powerSlider.value = 300; // 사용량(505kW)보다 적게 → 부족 경고
                yield return Frames(1);
                UiText(overview, "powerPurchaseText", $"{power.NextMonthShortage} kW 부족");
                powerSlider.value = 800;
                yield return Frames(1);
            }
            else Note("powerSlider 미연결 — 이 씬의 종합 현황에는 전력 구매 슬라이더가 없음");

            Shot("02_overview_ui", panel != null ? panel.GetComponentInParent<Canvas>() : null);

            overview.HideOverview();
            yield return RealSeconds(1.0f);
            Check("종합 현황 닫기 → 패널 숨김", panel == null || !panel.activeInHierarchy);
            Check("닫으면 시간 다시 흐름", !time.IsEventPaused);

            var satUi = UnityEngine.Object.FindObjectOfType<SatisfactionUI>(true);
            if (satUi != null)
            {
                var t = TestWorld.GetField<TextMeshProUGUI>(satUi, "satisfactionText");
                Check("만족도 UI(SatisfactionUI) 값 일치", t == null || t.text == $"{Mathf.RoundToInt(sat.CurrentAverageSatisfaction)}%", t?.text);
            }
        }

        private static IEnumerator BuildingInfoUiCheck()
        {
            var ui = BuildingUIManager.Instance;
            if (ui == null) { Note("BuildingUIManager 없음 — 건물 정보 UI 확인 생략"); yield break; }

            ui.OnBuildingClicked(Placed["hospital"]);
            yield return Frames(2);
            Check("건물 클릭 → 행동 패널(정보/철거) 표시", ui.actionPanel == null || ui.actionPanel.activeSelf);
            if (ui.btnInfo != null) ui.btnInfo.onClick.Invoke();
            yield return Frames(2);
            string shown = ui.txtBuildingName != null ? ui.txtBuildingName.text : "(txtBuildingName 미연결)";
            Note($"정보 패널: '{shown}' / {ui.txtCategory?.text} / {ui.txtPower?.text} / {ui.txtCost?.text?.Replace("\n", " ")}");
            Check("[정보] 버튼 → 정보 패널에 건물 데이터 표시", ui.txtBuildingName == null || shown.Contains("병원"), shown);
            ui.CloseAllUI();
            yield return RealSeconds(0.5f);

            yield return InfrastructureButtonCheck(ui);
        }

        /// <summary>
        /// 인프라(도로·전신주) 버튼 (2026-11-25): 건물은 도로에 맞닿아야 지어지므로 UI로 도로를 고를 수 있어야 한다.
        /// 씬에 버튼이 없으면 BuildingUIManager가 주택 버튼을 복제해 줄 끝에 만든다
        /// </summary>
        private static IEnumerator InfrastructureButtonCheck(BuildingUIManager ui)
        {
            var btn = ui.InfrastructureButton;
            Check("[인프라] 버튼이 있음 (씬에 없으면 자동 생성)", btn != null);
            if (btn == null) yield break;
            var label = btn.GetComponentInChildren<TMP_Text>(true);
            Note($"[인프라] 버튼 '{btn.name}', 글자 '{label?.text}', 위치 {btn.GetComponent<RectTransform>()?.anchoredPosition}");
            Check("[인프라] 버튼이 켜져 있고 누를 수 있음", btn.gameObject.activeInHierarchy && btn.interactable);
            Check("[인프라] 버튼 글자", label == null || label.text.Contains("인프라"), label?.text);

            // 다른 카테고리 버튼과 겹치지 않음
            var rect = btn.GetComponent<RectTransform>();
            bool overlaps = false;
            foreach (var field in new[] { "btnFacility", "btnEssential", "btnNimbys", "btnHousing" })
            {
                var other = TestWorld.GetField<Button>(ui, field);
                if (other == null || other == btn || rect == null) continue;
                var o = other.GetComponent<RectTransform>();
                if (o.parent == rect.parent && Mathf.Abs(o.anchoredPosition.y - rect.anchoredPosition.y) < 1f
                    && Mathf.Abs(o.anchoredPosition.x - rect.anchoredPosition.x) < rect.rect.width) overlaps = true;
            }
            Check("[인프라] 버튼이 다른 카테고리 버튼과 겹치지 않음", !overlaps);

            // 누르면 도로·전신주를 고를 수 있어야 함
            btn.onClick.Invoke();
            yield return Frames(2);
            if (!TestWorld.GetField<bool>(ui, "showCatalogOnCategoryClick"))
            {
                var small = TestWorld.GetField<Button>(ui, "smallButton");
                Check("[인프라] 크기 메뉴에서 소형(도로·전신주) 선택 가능", small == null || small.interactable);
                small?.onClick.Invoke();
                yield return Frames(2);
            }
            var container = TestWorld.GetField<Transform>(ui, "buildingSlotContainer");
            var ids = new List<string>();
            if (container != null)
            {
                foreach (var slot in container.GetComponentsInChildren<BuildingSlotUI>())
                {
                    var data = TestWorld.GetField<BuildingData>(slot, "buildingData");
                    if (data != null && slot.gameObject.activeSelf) ids.Add(data.buildingID);
                }
            }
            Note($"[인프라] 목록: {string.Join(", ", ids)}");
            var buildCanvas = btn.GetComponentInParent<Canvas>();
            if (buildCanvas != null)
            {
                yield return RealSeconds(0.8f); // 슬롯 팝업 연출이 끝난 뒤 촬영
                Shot("02b_build_ui_infrastructure", buildCanvas.rootCanvas);
            }
            Check("[인프라] 목록에 도로와 전신주가 있음", container == null || (ids.Contains("road") && ids.Contains("power_pole")), string.Join(", ", ids));
            ui.CloseAllUI();
            yield return RealSeconds(0.5f);
        }

        private static IEnumerator MonthlySettlementCheck()
        {
            var eco = EconomyManager.Instance;
            var loop = GameLoopManager.Instance;
            var power = PowerManager.Instance;

            int income = -1, paid = -1, unpaid = -1;
            Action<int, int, int> handler = (i, p, u) => { income = i; paid = p; unpaid = u; };
            eco.OnMonthlySettled += handler;

            int moneyBefore = eco.CurrentMoney;
            int maintenance = loop.GetTotalMaintenanceCost();
            int powerCost = power.TargetPurchasedPower * power.PricePerPowerUnit;
            var gas = InfraTown.Gas.GasManager.Instance;
            int gasCost = gas != null ? gas.MonthlyPurchaseCost : 0;
            int requested = eco.GetRequestedSubsidy();

            yield return WaitUntil(() => income >= 0, 40, "월간 정산");
            eco.OnMonthlySettled -= handler;

            Note($"정산: 지원금 +{income:N0}, 유지비 -{paid:N0} (미납 {unpaid:N0}), 전력 -{powerCost:N0}, 가스 -{gasCost:N0}");
            Check("지원금 = 슬라이더로 신청한 금액", income == requested, $"{income:N0} / {requested:N0}");
            Check("유지비 = 건물 유지비 합계", paid == maintenance, $"{paid:N0} / {maintenance:N0}");
            Check("잔액 = 이전 + 지원금 − 유지비 − 전력 구매 − 가스 구매",
                eco.CurrentMoney == moneyBefore + income - paid - powerCost - gasCost, $"{moneyBefore:N0} → {eco.CurrentMoney:N0}");
            Check("정산 후 보유 전력 = 슬라이더로 정한 구매량", power.CurrentAvailablePower == power.TargetPurchasedPower,
                $"{power.CurrentAvailablePower} kW");
            Check("정산 후 보유 가스 = 정한 구매량", gas == null || gas.CurrentAvailableGas == gas.TargetPurchasedGas,
                $"{gas?.CurrentAvailableGas}");
        }

        /// <summary>
        /// 계절에 따라 전력·가스 사용량이 바뀌는지 실제 시간 진행으로 확인 (전력 여름 최고·겨울 최저, 가스 반대, 최고 = 최저 × 2.5)
        /// </summary>
        private static IEnumerator SeasonCheck()
        {
            var time = TimeManager.Instance;
            var power = PowerManager.Instance;
            var gas = InfraTown.Gas.GasManager.Instance;
            int pop = PopulationManager.Instance.CurrentPopulation;
            Note($"인구 {pop}명 → 최저 소비량 {pop / 10f:F1} (인구 100명당 10), 최고 {pop / 10f * 2.5f:F1}");

            var seen = new Dictionary<Season, (int power, int gas)>();
            int seasonEvents = 0;
            Action<Season> onSeason = s => seasonEvents++;
            time.OnSeasonChanged += onSeason;
            // 1년(12달)을 진행하며 달마다 계절별 사용량 기록
            for (int i = 0; i < TimeManager.MonthsPerYear; i++)
            {
                yield return WaitWeeks(TimeManager.WeeksPerMonth);
                if (PopulationManager.Instance.CurrentPopulation != pop) PopulationManager.Instance.ApplySaveState(new InfraTown.Save.PopulationSaveState { currentPopulation = pop });
                yield return Frames(1);
                seen[time.CurrentSeason] = (power.SeasonalPowerUsage, gas != null ? gas.CurrentUsedGas : 0);
            }
            time.OnSeasonChanged -= onSeason;

            foreach (var pair in seen) Note($"{SeasonUtil.ToKorean(pair.Key)}: 전력(인구·계절) {pair.Value.power}kW, 가스 {pair.Value.gas}");
            int min = SeasonUtil.GetSeasonalDemand(pop, 10f, Season.Winter, Season.Summer, 2.5f);
            int max = SeasonUtil.GetSeasonalDemand(pop, 10f, Season.Summer, Season.Summer, 2.5f);
            int mid = SeasonUtil.GetSeasonalDemand(pop, 10f, Season.Spring, Season.Summer, 2.5f);
            Check("1년 동안 계절이 4번 바뀜", seasonEvents == 4, $"{seasonEvents}번");
            Check("전력: 여름 최고, 겨울 최저, 봄·가을 중간",
                seen.Count == 4 && seen[Season.Summer].power == max && seen[Season.Winter].power == min && seen[Season.Spring].power == mid && seen[Season.Autumn].power == mid,
                $"최저 {min} / 중간 {mid} / 최고 {max}");
            Check("가스: 겨울 최고, 여름 최저, 봄·가을 중간",
                seen.Count == 4 && seen[Season.Winter].gas == max && seen[Season.Summer].gas == min && seen[Season.Spring].gas == mid && seen[Season.Autumn].gas == mid,
                $"최저 {min} / 중간 {mid} / 최고 {max}");
            Check("최고 소비량 = 최저의 2.5배", Mathf.Abs(max - min * 2.5f) <= 1f, $"{min} × 2.5 ≈ {max}");
        }

        private static IEnumerator MonthSkipCheck()
        {
            var time = TimeManager.Instance;
            var pop = PopulationManager.Instance;
            int weeksBefore = WeekIndex(time);
            int popBefore = pop.CurrentPopulation;
            var progressFrames = new HashSet<int>();
            bool? interrupted = null;
            Action<int, int> onProgress = (done, total) => progressFrames.Add(Time.frameCount);
            Action<bool> onEnd = i => interrupted = i;
            time.OnSkipProgress += onProgress;
            time.OnSkipEnded += onEnd;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Check("스킵 시작", time.StartMonthSkip());
            double deadline = EditorApplication.timeSinceStartup + 15; // 실제 시간으로 대기 (배치 모드는 프레임이 매우 빠름)
            while (time.IsSkipping && EditorApplication.timeSinceStartup < deadline) yield return null;
            stopwatch.Stop();
            if (time.IsSkipping) time.CancelSkip(); // 시간 초과 시 다음 단계가 막히지 않도록

            int passed = WeekIndex(time) - weeksBefore;
            Check("스킵 한 번에 4주(한 달)가 지나감", passed == 4 && interrupted == false, $"{passed}주, 중단={interrupted}");
            Check("한 주씩 여러 프레임에 걸쳐 진행 (값이 바뀌는 모습이 보임)", progressFrames.Count >= 4, $"{progressFrames.Count}프레임");
            Note($"스킵 소요 실제 시간 {stopwatch.Elapsed.TotalSeconds:F1}초 (한 주 {time.SkipSecondsPerWeek}초), 인구 {popBefore} → {pop.CurrentPopulation}");

            // 스킵 도중 이벤트(대화·컷씬 등의 일시정지)가 일어나면 그 시점에서 멈춤
            var source = new object();
            int stopAt = -1;
            Action<int, int, int> onDate = null;
            int weekCount = 0;
            onDate = (y, m, w) => { if (++weekCount == 3) { time.RequestEventPause(source); stopAt = WeekIndex(time); } };
            interrupted = null;
            weeksBefore = WeekIndex(time);
            time.OnDateChanged += onDate;
            time.StartMonthSkip();
            deadline = EditorApplication.timeSinceStartup + 15;
            while (time.IsSkipping && EditorApplication.timeSinceStartup < deadline) yield return null;
            if (time.IsSkipping) time.CancelSkip();
            yield return Frames(5);
            time.OnDateChanged -= onDate;

            Check("스킵 도중 이벤트가 나면 그 주에서 멈춤", interrupted == true && WeekIndex(time) == stopAt && WeekIndex(time) - weeksBefore == 3,
                $"{WeekIndex(time) - weeksBefore}주 진행 후 멈춤");
            time.ReleaseEventPause(source);

            time.OnSkipProgress -= onProgress;
            time.OnSkipEnded -= onEnd;
        }

        private static int WeekIndex(TimeManager t) => ((t.CurrentYear - 1) * TimeManager.MonthsPerYear + (t.CurrentMonth - 1)) * TimeManager.WeeksPerMonth + t.CurrentWeek;

        private static IEnumerator PowerPoleCheck()
        {
            var power = PowerManager.Instance;
            // 기준 마을은 전봇대 4개가 모두 덮음 (멀리 둔 불쾌 시설 제외)
            var unpowered = Placed.Where(kv => kv.Value != null && kv.Value.Data.category != BuildingType.Nimbys && !kv.Value.Data.IsRoad && !power.IsBuildingPowered(kv.Value))
                .Select(kv => kv.Key).ToList();
            Check("기준 마을 건물 전부 전봇대 범위 안 (전기 공급)", unpowered.Count == 0, string.Join(", ", unpowered));

            var house = PlaceWithRoad("house", 45, 25, "farHouse"); // 전봇대 없는 곳
            yield return Frames(1);
            Check("전봇대 범위 밖 주택은 전기 꺼짐", house != null && !power.IsBuildingPowered(house));
            float before = house != null ? house.GetComponent<HousingInstance>().CurrentSatisfaction : 0f;

            var pole = PlaceWithRoad("power_pole", 49, 25, "pole");
            yield return Frames(1);
            Check("전신주에 UtilityPoleInstance 자동 부착", pole != null && pole.GetComponent<UtilityPoleInstance>() != null);
            Check("반경 4 안이 되면 전기 공급", house != null && power.IsBuildingPowered(house));
            float after = house != null ? house.GetComponent<HousingInstance>().CurrentSatisfaction : 0f;
            Check("전기가 들어온 주택 만족도 상승", after > before, $"{before:F1} → {after:F1}");

            if (pole != null) pole.DestroyBuilding();
            Placed.Remove("pole");
            yield return Frames(2);
            Check("전신주 철거 → 다시 전기 꺼짐", house != null && !power.IsBuildingPowered(house));
            if (house != null) house.DestroyBuilding();
            Placed.Remove("farHouse");
            yield return Frames(2);
        }

        private static IEnumerator DemolishCheck()
        {
            var service = ServiceAccessibilityManager.Instance;
            var loop = GameLoopManager.Instance;

            Placed["hospital"].DestroyBuilding();
            Placed.Remove("hospital");
            yield return Frames(2);
            Check("[코드] 병원 철거 → 병원 미설치", !service.HasHospital);
            Check("[코드] 병원 점유 칸 해제", GetGrid()[3, 6] == null);
            Check("[코드] 철거 후 같은 자리에 다시 건설", Place("hospital", 2, 5, "hospital") != null);

            var ui = BuildingUIManager.Instance;
            if (ui == null || ui.btnDemolish == null) { Note("UI [철거] 버튼 없음 — 확인 생략"); yield break; }

            var store = Placed["store"];
            int maintenanceBefore = loop.GetTotalMaintenanceCost();
            ui.OnBuildingClicked(store);
            yield return Frames(1);
            ui.btnDemolish.onClick.Invoke();
            yield return Frames(2);
            Placed.Remove("store");

            Check("[UI 버튼] 오브젝트 삭제", store == null);
            Check("[UI 버튼] 편의점 미설치로 갱신", !service.HasConvenienceStore);
            Check("[UI 버튼] 게임 루프 목록에 삭제된 참조 없음", loop.PlacedBuildings.All(b => b != null),
                $"삭제된 참조 {loop.PlacedBuildings.Count(b => b == null)}개");
            Check("[UI 버튼] 유지비에서 편의점(120원) 빠짐", loop.GetTotalMaintenanceCost() == maintenanceBefore - Data("convenience_store").maintenanceCost,
                $"{maintenanceBefore:N0} → {loop.GetTotalMaintenanceCost():N0}");
            Check("[UI 버튼] 같은 자리에 다시 건설", Place("convenience_store", 9, 2, "store") != null);
        }

        private static IEnumerator DefeatCheck()
        {
            var outcome = GameOutcomeManager.Instance;
            var sat = SatisfactionManager.Instance;
            var power = PowerManager.Instance;

            GameResult ended = GameResult.None;
            outcome.OnGameEnded += r => ended = r;
            int lastBad = 0;
            outcome.OnBadStreakChanged += (d, limit) => lastBad = d;
            int weeks = 0;
            Action countWeek = () => weeks++;
            TimeManager.Instance.OnWeeklyTick += countWeek;

            // 시설 전부 철거 + 전력 0 + 매립지로 주택을 '나쁨'으로
            foreach (var key in Placed.Keys.Where(k => !k.StartsWith("house")).ToList())
            {
                Placed[key].DestroyBuilding();
                Placed.Remove(key);
            }
            yield return Frames(2);
            power.SetTargetPurchasedPower(0);
            power.ProcessMonthlyPowerPurchase();
            foreach (int x in new[] { 0, 8, 16, 24 }) PlaceWithRoad("landfill", x, 2, $"landfill{x}"); // 도로도 철거된 뒤라 도로부터
            yield return Frames(2);

            Note($"악화된 마을: 평균 {sat.CurrentAverageSatisfaction:F1}, 상태 {sat.CurrentState}, 나쁨 주택 {sat.BadHousingRatio:P0}");
            Check("마을 상태 '나쁨'", sat.CurrentState == SatisfactionState.Bad);
            Shot("03_bad_town", null);

            // 배드엔딩 순간에 이미 열려 있던 건물 정보창 (사용자 2026-11-25: 팀원 1이 게임 오버·클리어 창을 만드는 중 — 붙은 뒤에도 이 창으로 철거되는지 확인)
            var uiForDefeat = BuildingUIManager.Instance;
            var openedHouse = Placed.Where(kv => kv.Key.StartsWith("house") && kv.Value != null).Select(kv => kv.Value).Skip(1).FirstOrDefault();
            if (uiForDefeat != null && uiForDefeat.btnDemolish != null && openedHouse != null) uiForDefeat.OnBuildingClicked(openedHouse);

            yield return WaitUntil(() => ended != GameResult.None, 40, "배드엔딩");
            Check("나쁨 12주(3달) 연속 → 배드엔딩", ended == GameResult.Defeat && lastBad == 12, $"{ended}, 나쁨 {lastBad}주");
            Check("엔딩 후 시간 정지", TimeManager.Instance.IsEventPaused);
            // 사용자 기획 2026-11-24: 배드엔딩 = 게임 오버 → 건설·철거 불가
            Check("배드엔딩 뒤 건설 불가 (게임 오버)", outcome.IsGameOver && Place("road", 45, 35, null) == null
                && BuildingManager.Instance.LastPlacementError == BuildingManager.GameOverPlacementError, BuildingManager.Instance.LastPlacementError);
            var someHouse = Placed.FirstOrDefault(kv => kv.Key.StartsWith("house") && kv.Value != null).Value;
            int placedCount = GameLoopManager.Instance.PlacedBuildings.Count;
            someHouse?.DestroyBuilding();
            yield return Frames(1);
            Check("배드엔딩 뒤 철거 불가", someHouse != null && GameLoopManager.Instance.PlacedBuildings.Count == placedCount);

            // 게임 오버 전에 열어 둔 정보창의 [철거] 버튼 (BuildingUIManager가 Destroy(gameObject)를 직접 부르면 기능 코드로 못 막음)
            if (uiForDefeat == null || uiForDefeat.btnDemolish == null || openedHouse == null)
                Note("UI [철거] 버튼 없음 — 게임 오버 전에 열어 둔 정보창 철거 확인 생략");
            else
            {
                bool panelOpen = uiForDefeat.actionPanel != null && uiForDefeat.actionPanel.activeInHierarchy;
                bool buttonUsable = uiForDefeat.btnDemolish.interactable && uiForDefeat.btnDemolish.gameObject.activeInHierarchy;
                if (!panelOpen || !buttonUsable)
                {
                    Check("게임 오버 전에 열어 둔 정보창: UI가 엔딩 때 닫거나 [철거]를 끔", true, panelOpen ? "철거 버튼 꺼짐" : "정보창 닫힘");
                }
                else
                {
                    int countBefore = GameLoopManager.Instance.PlacedBuildings.Count;
                    uiForDefeat.btnDemolish.onClick.Invoke();
                    yield return Frames(2);
                    bool survived = openedHouse != null && GameLoopManager.Instance.PlacedBuildings.Count == countBefore;
                    if (survived) Check("게임 오버 전에 열어 둔 정보창의 [철거]로도 철거 안 됨", true);
                    else Note("⚠ 알려진 문제 (팀원 1 UI, 게임 오버·클리어 창 제작 중): 게임 오버 전에 열어 둔 건물 정보창의 [철거] 버튼으로 철거됨 — " +
                              "BuildingUIManager가 OnGameEnded에서 정보창을 닫거나, 철거를 Destroy(gameObject) 대신 BuildingInstance.TryDemolish()로 바꾸면 막힘");
                }
            }
            uiForDefeat?.CloseAllUI();
            TimeManager.Instance.OnWeeklyTick -= countWeek;
            // 12주 동안 매주 주간 틱 → 지나간 주 수만큼 2%씩, 시설당 최대 10%
            float expectedPerFacility = Mathf.Min(weeks * 2f, 10f);
            Check($"필수 시설이 없던 {weeks}주 동안 미설치 감점이 시설마다 주 2%씩 쌓임 (최대 10%)",
                Mathf.Approximately(sat.GetMissingFacilityPenalty(ServiceType.Hospital), expectedPerFacility) &&
                Mathf.Approximately(sat.TotalMissingFacilityPenalty, expectedPerFacility * 4f),
                $"병원 {sat.GetMissingFacilityPenalty(ServiceType.Hospital):F0}%, 합계 {sat.TotalMissingFacilityPenalty:F0}% (예상 {expectedPerFacility:F0}% × 4)");
        }

        private static IEnumerator VictoryCheck()
        {
            // 엔딩 판정 새로 시작 (씬을 다시 불러오면 임시로 넣은 매니저가 사라지므로 같은 판에서 진행)
            var old = GameOutcomeManager.Instance;
            TimeManager.Instance.ReleaseEventPause(old);
            UnityEngine.Object.Destroy(old);
            yield return Frames(1);
            GameLoopManager.Instance.gameObject.AddComponent<GameOutcomeManager>();
            yield return Frames(2);
            var outcome = GameOutcomeManager.Instance;
            Check("새 엔딩 판정 시작", outcome != null && outcome != old && !outcome.IsGameOver && !TimeManager.Instance.IsEventPaused);

            foreach (var key in Placed.Keys.Where(k => k.StartsWith("landfill")).ToList())
            {
                Placed[key].DestroyBuilding();
                Placed.Remove(key);
            }
            yield return Frames(1);
            yield return PlaceMiddle(withNimby: false);
            yield return PlaceEssentials(withNimby: false);
            yield return EnsurePower();
            EconomyManager.Instance.AddMoney(10_000_000);
            yield return Frames(1);

            var sat = SatisfactionManager.Instance;
            Note($"좋은 마을 재건: 평균 {sat.CurrentAverageSatisfaction:F1}, 좋음 비율 {sat.GoodHousingRatio:P0}, 누락 필수 시설 [{string.Join(", ", outcome.GetMissingRequiredServices())}]");
            Check("필수 시설 4종 → 인프라 정상화", outcome.IsInfrastructureNormalized());

            // 배드엔딩 단계에서 필수 시설이 없던 동안 쌓인 미설치 감점은 다시 지어도 주마다 2%씩 천천히 회복된다
            float leftover = sat.TotalMissingFacilityPenalty;
            if (leftover > 0f)
            {
                Note($"필수 시설 미설치 감점이 {leftover:F0}% 남아 있음 → 주마다 회복되는지 확인");
                int startWeek = outcome.WeeksElapsed;
                yield return WaitUntil(() => sat.TotalMissingFacilityPenalty <= 0f, 70, "미설치 감점 회복");
                int weeks = outcome.WeeksElapsed - startWeek;
                Check("다시 지으면 미설치 감점이 주마다 회복되어 0이 됨 (시설당 최대 10% → 5주 이내)",
                    sat.TotalMissingFacilityPenalty <= 0f && weeks <= 5, $"{weeks}주 걸림");
                Note($"회복 후: 평균 {sat.CurrentAverageSatisfaction:F1}, 좋음 비율 {sat.GoodHousingRatio:P0}");
            }

            if (sat.GoodHousingRatio < outcome.RequiredGoodRatio)
            {
                Note($"좋음 비율이 기준({outcome.RequiredGoodRatio:P0})에 못 미침 → 10년이 지나도 승리하지 않아야 함");
                yield return WaitWeeks(outcome.VictoryWeeks + 30);
                Check("좋음 비율 미달 → 10년이 지나도 계속 플레이", !outcome.IsGameOver, $"{outcome.WeeksElapsed}주, 결과 {outcome.Result}");
                yield break;
            }

            GameResult ended = GameResult.None;
            outcome.OnGameEnded += r => ended = r;
            double t0 = EditorApplication.timeSinceStartup;
            yield return WaitUntil(() => ended != GameResult.None, outcome.VictoryWeeks + 60, "해피엔딩");
            Note($"10년 진행: 실제 {EditorApplication.timeSinceStartup - t0:F0}초, 엔딩 판정 {outcome.WeeksElapsed}주 ({TimeManager.Instance.CurrentYear}년 {TimeManager.Instance.CurrentMonth}월 {TimeManager.Instance.CurrentWeek}주차)");
            Check("10년 경과 + 조건 충족 → 해피엔딩", ended == GameResult.Victory, $"{ended}, {outcome.WeeksElapsed}주째");
            Check("정확히 480주(10년)째에 판정", outcome.WeeksElapsed == outcome.VictoryWeeks && outcome.VictoryWeeks == 480, $"{outcome.WeeksElapsed}주");
            Note($"10년 뒤: 인구 {PopulationManager.Instance.CurrentPopulation}명, 잔액 {EconomyManager.Instance.CurrentMoney:N0}원, 만족도 {sat.CurrentAverageSatisfaction:F1}, 좋음 {sat.GoodHousingRatio:P0}");
            Shot("04_victory_town", null);

            // 사용자 기획 2026-11-24: 해피엔딩은 게임 오버가 아니고 끝내기/계속하기 선택 → 계속하기면 배드엔딩 조건 없이 이어서
            Check("해피엔딩 = 게임 오버 아님, 선택 대기 (시간 정지)", !outcome.IsGameOver && outcome.IsVictoryChoicePending && TimeManager.Instance.IsEventPaused);
            Check("계속하기 → 시간이 다시 흐름", outcome.ContinueAfterVictory() && outcome.IsFreePlay && !TimeManager.Instance.IsEventPaused);
            int freeStart = outcome.WeeksElapsed;
            yield return WaitWeeks(8);
            Check("계속하기 뒤 8주: 엔딩 판정 없이 계속", outcome.Result == GameResult.Victory && outcome.BadStreakWeeks == 0 && outcome.WeeksElapsed >= freeStart + 8,
                $"결과 {outcome.Result}, 경과 {outcome.WeeksElapsed - freeStart}주");
        }

        // ───────────────────────── 회전 모드 (-playtestMode rotation) ─────────────────────────

        private const int RotationTestCount = 100;
        private const int RotationTestCycles = 3;

        /// <summary>
        /// 건물 100개를 Q/E로 무작위 회전해 설치 → 미리보기와 설치 모습 일치 확인 → 전부 철거, 를 반복
        /// </summary>
        private static IEnumerator RotationScenario()
        {
            var manager = BuildingManager.Instance;
            var loop = GameLoopManager.Instance;
            EconomyManager.Instance.AddMoney(100_000_000);
            manager.RequireRoadAccess = false; // 회전만 검사 (격자 위치에 100개를 지으므로 도로 접근 조건은 끔 — 도로 규칙은 RoadAccessTests·기본 시나리오에서)

            // 원래 회전이 들어간 길쭉한 테스트 프리팹 (에셋에서 X -90° 등으로 들여오는 경우를 흉내)
            var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
            template.name = "[Playtest] 회전 테스트 프리팹";
            template.transform.position = new Vector3(0f, -500f, 0f);
            template.transform.rotation = Quaternion.Euler(-90f, 30f, 0f);
            template.transform.localScale = new Vector3(1.2f, 3.2f, 0.6f);
            UnityEngine.Object.Destroy(template.GetComponent<Collider>());

            var testData = new List<BuildingData>();
            foreach (var size in new[] { BuildingSizeType.Small, BuildingSizeType.Medium, BuildingSizeType.Large })
            {
                var d = ScriptableObject.CreateInstance<BuildingData>();
                d.buildingID = $"rotation_test_{size}";
                d.buildingName = $"회전 테스트 {size}";
                d.category = BuildingType.Facility;
                d.sizeType = size;
                d.buildingPrefab = template;
                testData.Add(d);
            }
            var database = TestWorld.GetField<List<BuildingData>>(manager, "buildingDatabase");
            database.AddRange(testData);
            // 인구 0명에서 지을 수 있는 기획서 Tier 1 건물(원래 회전 0)도 섞는다
            var mixed = testData.Concat(new[] { Data("village_hall"), Data("road"), Data("house") }).ToList();

            Note($"테스트 프리팹 원래 회전 {template.transform.rotation.eulerAngles} (X -90°, Y 30°), 배치 {RotationTestCount}개 × {RotationTestCycles}회 반복");
            var rng = new System.Random(20261112);

            for (int cycle = 1; cycle <= RotationTestCycles; cycle++)
            {
                Section($"회전 테스트 {cycle}회차 — {RotationTestCount}개 설치");
                int placedCount = 0, previewMismatch = 0, expectedMismatch = 0, positionMismatch = 0, rotationValueMismatch = 0, cellMismatch = 0;
                var placed = new List<BuildingInstance>();
                var rotationsUsed = new HashSet<float>();

                for (int i = 0; i < RotationTestCount; i++)
                {
                    var data = mixed[i % mixed.Count];
                    var cell = new Vector2Int((i % 15) * 4, (i / 15) * 4);

                    manager.SelectBuildingToBuild(data);
                    // Q(+90) / E(-90)를 무작위 횟수만큼 누른 것과 같은 호출
                    int presses = rng.Next(0, 6);
                    bool useQ = rng.Next(2) == 0;
                    for (int p = 0; p < presses; p++) manager.RotatePreview(useQ ? 90f : -90f);
                    float expectedY = Mathf.Repeat((useQ ? 90f : -90f) * presses, 360f);
                    if (!Mathf.Approximately(manager.CurrentRotationY, expectedY)) rotationValueMismatch++;

                    TestWorld.Invoke(manager, "UpdateBuildingPreview", cell); // 마우스가 그 칸 위에 있는 상태
                    var preview = TestWorld.GetField<GameObject>(manager, "_previewInstance");
                    Quaternion previewRot = preview.transform.rotation;
                    Vector3 previewPos = preview.transform.position;

                    TestWorld.Invoke(manager, "TryPlaceBuilding", cell);
                    var b = GetGrid()[cell.x, cell.y];
                    if (b == null || b.Data != data) { Fail($"{i}번 설치", $"{data.buildingID} @ {cell}"); continue; }
                    placedCount++;
                    placed.Add(b);
                    rotationsUsed.Add(b.RotationY);

                    Quaternion expected = Quaternion.Euler(0f, b.RotationY, 0f) * data.buildingPrefab.transform.rotation;
                    if (Quaternion.Angle(previewRot, b.transform.rotation) > 0.01f) previewMismatch++;
                    if (Quaternion.Angle(expected, b.transform.rotation) > 0.01f) expectedMismatch++;
                    if (Vector3.Distance(previewPos, b.transform.position) > 0.001f) positionMismatch++;
                    if (!Mathf.Approximately(b.RotationY, manager.CurrentRotationY)) rotationValueMismatch++;

                    var size = data.GridSize;
                    var grid = GetGrid();
                    for (int x = cell.x; x < cell.x + size.x; x++)
                        for (int y = cell.y; y < cell.y + size.y; y++)
                            if (grid[x, y] != b) cellMismatch++;
                }
                manager.CancelBuildingMode();
                yield return Frames(2);

                Check($"{RotationTestCount}개 모두 설치", placedCount == RotationTestCount, $"{placedCount}개");
                Check("설치된 회전 = 설치 직전 미리보기 회전 (전부)", previewMismatch == 0, $"불일치 {previewMismatch}개");
                Check("설치된 회전 = 플레이어 Y 회전 × 프리팹 원래 회전", expectedMismatch == 0, $"불일치 {expectedMismatch}개");
                Check("설치 위치 = 미리보기 위치", positionMismatch == 0, $"불일치 {positionMismatch}개");
                Check("Q/E 누른 만큼 회전 + 건물에 기록된 RotationY = 설치 당시 회전", rotationValueMismatch == 0, $"불일치 {rotationValueMismatch}개");
                Check("회전해도 점유 칸(1x1/2x2/4x4)이 정확", cellMismatch == 0, $"어긋난 칸 {cellMismatch}개");
                Check("0/90/180/270 네 방향이 모두 쓰임", rotationsUsed.SetEquals(new[] { 0f, 90f, 180f, 270f }),
                    string.Join(", ", rotationsUsed.OrderBy(r => r)));
                Check("게임 루프에 100개 등록", loop.PlacedBuildings.Count == RotationTestCount, $"{loop.PlacedBuildings.Count}개");
                if (cycle == 1) Shot("rotation_placed", null, new Vector3(20f, 0f, 14f));

                Section($"회전 테스트 {cycle}회차 — 전부 철거");
                for (int i = 0; i < placed.Count; i++)
                {
                    // 절반은 코드 경로(DestroyBuilding), 절반은 UI [철거] 버튼과 같은 Destroy(gameObject)
                    if (i % 2 == 0) placed[i].DestroyBuilding();
                    else UnityEngine.Object.Destroy(placed[i].gameObject);
                }
                yield return Frames(3);

                var gridAfter = GetGrid();
                int occupied = 0;
                foreach (var c in gridAfter) if (c != null) occupied++;
                int alive = UnityEngine.Object.FindObjectsOfType<BuildingInstance>().Length;
                Check("철거 후 남은 건물 오브젝트 0개", alive == 0, $"{alive}개");
                Check("철거 후 점유 칸 0개", occupied == 0, $"{occupied}칸");
                Check("철거 후 게임 루프 목록 비어 있음 (삭제된 참조 없음)", loop.PlacedBuildings.Count == 0,
                    $"{loop.PlacedBuildings.Count}개");
                Check("철거 후 유지비 합계 0", loop.GetTotalMaintenanceCost() == 0, $"{loop.GetTotalMaintenanceCost():N0}");
                Check("미리보기 오브젝트가 남지 않음", TestWorld.GetField<GameObject>(manager, "_previewInstance") == null);
            }

            database.RemoveAll(d => testData.Contains(d));
            UnityEngine.Object.Destroy(template);
            manager.RequireRoadAccess = true;
        }

        // ───────────────────────── 도우미 ─────────────────────────

        private static BuildingData Data(string id) =>
            BuildingManager.Instance.AllBuildingData.First(d => d != null && d.buildingID == id);

        private static BuildingInstance[,] GetGrid() =>
            TestWorld.GetField<BuildingInstance[,]>(BuildingManager.Instance, "_occupiedGrid");

        /// <summary>UI에서 건물을 고르고 칸을 클릭한 것과 같은 경로로 배치</summary>
        private static BuildingInstance Place(string id, int x, int y, string key)
        {
            var manager = BuildingManager.Instance;
            var data = Data(id);
            // 도로는 일반 칸, 나머지는 도로 위 전신주부터 (사용자 기획 2026-11-24: 전신주는 도로 위에도)
            BuildingInstance At() => data.IsRoad ? GetGrid()[x, y] : manager.GetBuildingAt(new Vector2Int(x, y));
            var before = At();
            manager.SelectBuildingToBuild(data);
            TestWorld.Invoke(manager, "TryPlaceBuilding", new Vector2Int(x, y));
            manager.CancelBuildingMode();

            var placed = At();
            if (placed != null && (placed == before || placed.Data.buildingID != id)) placed = null;
            if (key != null) Placed[key] = placed;
            return placed;
        }

        /// <summary>
        /// 도로에 맞닿아 있지 않으면 바로 옆(아래 → 왼쪽 → 위 → 오른쪽) 빈 칸에 도로를 하나 깐 뒤 설치 (사용자 기획 2026-11-24: 도로 접근 조건).
        /// 깐 도로는 key + "_road"로 Placed에 넣어 배드엔딩 단계의 시설 철거 때 같이 철거된다. 설치에 실패하면 그 도로는 다시 철거
        /// </summary>
        private static BuildingInstance PlaceWithRoad(string id, int x, int y, string key)
        {
            var manager = BuildingManager.Instance;
            var data = Data(id);
            BuildingInstance road = null;
            if (!data.IsRoad && !manager.HasRoadAccess(data, new Vector2Int(x, y)))
            {
                var size = data.GridSize;
                foreach (var c in new[] { new Vector2Int(x, y - 1), new Vector2Int(x - 1, y), new Vector2Int(x, y + size.y), new Vector2Int(x + size.x, y) })
                {
                    if (c.x < 0 || c.y < 0 || c.x >= 60 || c.y >= 40 || GetGrid()[c.x, c.y] != null || manager.GetRoadPoleAt(c) != null) continue;
                    road = Place("road", c.x, c.y, null);
                    if (road != null) break;
                }
            }
            var placed = Place(id, x, y, key);
            if (placed == null && road != null) road.DestroyBuilding();
            else if (road != null) Placed[(key ?? $"{id}_{x}_{y}") + "_road"] = road;
            return placed;
        }

        /// <summary>기준 마을 도로: y=1 줄(주택 y=0·시설 y=2~3), y=4 줄(시설·필수 시설 y=5), x 0~29. 이미 깔린 칸은 건너뜀</summary>
        private static int LayTownRoads()
        {
            int laid = 0;
            foreach (int y in new[] { 1, 4 })
            {
                for (int x = 0; x < 30; x++)
                {
                    string key = $"road_{x}_{y}";
                    if (Placed.TryGetValue(key, out var existing) && existing != null) { laid++; continue; }
                    var road = Place("road", x, y, null); // 실패한 칸은 Placed에 null로 남기지 않음 (철거 단계에서 null 참조 방지)
                    if (road != null) { Placed[key] = road; laid++; }
                }
            }
            return laid;
        }

        private static void UiText(object ui, string field, string expectedPart)
        {
            var text = TestWorld.GetField<TextMeshProUGUI>(ui, field);
            if (text == null) { Note($"{field} 미연결"); return; }
            Check($"종합 현황 {field}", text.text.Contains(expectedPart), $"'{text.text.Replace("\n", " ")}'");
        }

        private static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        private static IEnumerator RealSeconds(float seconds)
        {
            double end = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < end) yield return null;
        }

        private static float _normalSecondsPerWeek = -1f;

        /// <summary>게임 시간을 한 주 1프레임으로 빠르게 돌림 (대기가 끝나면 RestoreSpeed로 원래 속도 복원)</summary>
        private static void FastForward()
        {
            var time = TimeManager.Instance;
            float current = TestWorld.GetField<float>(time, "secondsPerWeek");
            if (current > 0.001f) _normalSecondsPerWeek = current;
            TestWorld.SetField(time, "secondsPerWeek", 0.0001f);
            if (time.CurrentSpeed == GameSpeed.Pause) time.SetGameSpeed(GameSpeed.Normal);
        }

        private static void RestoreSpeed()
        {
            if (TimeManager.Instance != null && _normalSecondsPerWeek > 0f)
                TestWorld.SetField(TimeManager.Instance, "secondsPerWeek", _normalSecondsPerWeek);
        }

        private static IEnumerator WaitWeeks(int weeks)
        {
            FastForward();
            int count = 0;
            Action<int, int, int> handler = (y, m, d) => count++;
            TimeManager.Instance.OnDateChanged += handler;
            int guard = weeks * 20 + 200;
            while (count < weeks && guard-- > 0) yield return null;
            TimeManager.Instance.OnDateChanged -= handler;
            RestoreSpeed();
            if (count < weeks) Fail($"{weeks}주 대기", $"{count}주만 진행됨");
        }

        private static IEnumerator WaitUntil(Func<bool> condition, int maxWeeks, string label)
        {
            FastForward();
            int weeks = 0;
            Action<int, int, int> handler = (y, m, w) => weeks++;
            TimeManager.Instance.OnDateChanged += handler;
            int guard = maxWeeks * 20 + 200;
            while (!condition() && weeks <= maxWeeks && guard-- > 0) yield return null;
            TimeManager.Instance.OnDateChanged -= handler;
            RestoreSpeed();
            if (!condition()) Fail(label, $"{weeks}주 진행했지만 조건 미충족");
        }

        /// <summary>3D 화면 촬영. overlayCanvas를 주면 그 UI도 함께 찍는다 (잠시 카메라 렌더 모드로 바꿨다 되돌림)</summary>
        private static void Shot(string name, Canvas overlayCanvas, Vector3? center = null)
        {
            var cam = Camera.main;
            if (cam == null) { Note($"스크린샷 {name}: Main Camera 없음"); return; }

            Vector3 lookAt = center ?? new Vector3(32f, 0f, 10f);
            var savedPos = cam.transform.position;
            var savedRot = cam.transform.rotation;
            cam.transform.position = lookAt + new Vector3(0f, 56f, -42f);
            cam.transform.LookAt(lookAt);

            RenderMode savedMode = RenderMode.ScreenSpaceOverlay;
            Camera savedCam = null;
            if (overlayCanvas != null)
            {
                overlayCanvas = overlayCanvas.rootCanvas;
                savedMode = overlayCanvas.renderMode;
                savedCam = overlayCanvas.worldCamera;
                overlayCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                overlayCanvas.worldCamera = cam;
                overlayCanvas.planeDistance = cam.nearClipPlane + 0.5f;
                Canvas.ForceUpdateCanvases();
            }

            const int w = 1920, h = 1080; // UI 기준 해상도(1920x1080)와 맞춰야 화면 가장자리 칸이 잘리지 않음
            var rt = new RenderTexture(w, h, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            cam.targetTexture = prev;
            RenderTexture.active = null;

            if (overlayCanvas != null)
            {
                overlayCanvas.renderMode = savedMode;
                overlayCanvas.worldCamera = savedCam;
            }
            cam.transform.SetPositionAndRotation(savedPos, savedRot);

            string dir = SessionState.GetString(OutKey, "Logs/Playtest");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            Note($"📷 스크린샷: {name}.png");
        }

        private static void Section(string title) => Report.Add($"\n## {title}\n");
        private static void Note(string text) => Report.Add($"- {text}");

        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok) _passed++; else _failed++;
            Report.Add($"- {(ok ? "✅" : "❌")} {name}{(detail != null ? $" — {detail}" : "")}");
        }

        private static void Fail(string name, string detail) => Check(name, false, detail);

        private static readonly HashSet<string> UiWarnings = new HashSet<string>();

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            // 화면에 바로 보이는 UI 문제(폰트에 없는 글자 등)는 경고로 따로 모은다
            if (type == LogType.Warning && condition.Contains("was not found in the"))
            {
                UiWarnings.Add(condition);
                return;
            }

            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                string firstFrame = stackTrace?.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
                ConsoleErrors.Add($"[{type}] {condition}{(firstFrame != null ? $" @ {firstFrame}" : "")}");
            }
        }

        private static void Finish(int exitCode)
        {
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Tick;
            SessionState.EraseBool(ActiveKey);

            if (ConsoleErrors.Count > 0) { _failed++; if (exitCode == 0) exitCode = 2; }

            var lines = new List<string>
            {
                "# InfraTown 자동 플레이 테스트 결과",
                "",
                $"- 씬: `{SessionState.GetString(SceneKey, DefaultScene)}` (플레이 모드, 배치 실행, 씬 저장 안 함)",
                $"- 결과: **{_passed} 통과 / {_failed} 실패**",
                $"- 실행 시간: {EditorApplication.timeSinceStartup - _startTime:F0}초",
            };
            lines.AddRange(Report);
            lines.Add("\n## UI 경고 (폰트에 없는 글자 등)\n");
            lines.AddRange(UiWarnings.Count == 0 ? new[] { "- 없음" } : UiWarnings.Select(w => $"- ⚠ {w}"));
            lines.Add("\n## 콘솔 에러/예외\n");
            lines.AddRange(ConsoleErrors.Count == 0 ? new[] { "- 없음" } : ConsoleErrors.Distinct().Select(e => $"- ❌ {e}"));

            string dir = SessionState.GetString(OutKey, "Logs/Playtest");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "report.md"), lines, new System.Text.UTF8Encoding(false));
            Debug.Log($"[Playtest] 완료: {_passed} 통과 / {_failed} 실패 → {dir}");

            EditorApplication.Exit(exitCode);
        }

        private static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}

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
    public static class PlaytestRunner
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
            Section("0. 씬 시작");
            string injected = SessionState.GetString(InjectedKey, "");
            if (string.IsNullOrEmpty(injected)) Note("씬에 필요한 매니저·건물 목록이 모두 있음");
            else foreach (var line in injected.Split('\n')) Note("⚠ " + line);
            yield return Frames(3);
            yield return BootChecks();

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
            yield return PlaceEssentials(withNimby: true);
            yield return EnsurePower();
            float full = SatisfactionSummary("완성된 마을");
            Check("완성된 마을이 중간 단계보다 만족도가 높다", full > middle, $"{middle:F1}% → {full:F1}%");
            yield return VillageChecks();

            Section("2. UI — 종합 현황 / 만족도 / 건물 정보");
            yield return OverviewChecks();
            yield return BuildingInfoUiCheck();

            Section("3. 월간 정산");
            yield return MonthlySettlementCheck();

            Section("4. 전신주(전력망) 설치");
            yield return PowerPoleCheck();

            Section("5. 철거 — 코드 경로와 UI [철거] 버튼");
            yield return DemolishCheck();

            Section("6. 배드엔딩 (나쁨 30일)");
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

            int managers = UnityEngine.Object.FindObjectsOfType<BuildingManager>().Length;
            Check("BuildingManager 1개", managers == 1, $"{managers}개");
            Check("LightweightGridManager 살아 있음", LightweightGridManager.Instance != null);
            Check("건물 데이터 13종", BuildingManager.Instance.AllBuildingData.Count == 13, $"{BuildingManager.Instance.AllBuildingData.Count}종");

            var eco = EconomyManager.Instance;
            var power = PowerManager.Instance;
            Check("시작 시 첫 달 전력 구매 (200kW, 10,000 → 8,000원)",
                power.CurrentAvailablePower == 200 && eco.CurrentMoney == 8000,
                $"전력 {power.CurrentAvailablePower}kW, 잔액 {eco.CurrentMoney:N0}원");
            yield break;
        }

        private static readonly Dictionary<string, BuildingInstance> Placed = new Dictionary<string, BuildingInstance>();

        private static IEnumerator PlaceHouses()
        {
            EconomyManager.Instance.AddMoney(1_000_000); // 테스트 자금

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
            yield return WaitDays(7);
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

            P("village_hall", 2, 2, "hall");
            P("convenience_store", 9, 2, "store");
            P("bus_stop", 16, 2, "bus");
            P("senior_center", 23, 2, "senior");
            P("road", 0, 1, "road");
            if (withNimby) P("livestock_farm", 50, 30, "farm");

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
            if (withNimby) P("landfill", 54, 34, "landfill");

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
            yield return Frames(1);
            Check("전력 충분 → 정전 감점 0", power.GetPowerBlackoutSatisfactionPenalty() == 0f, $"{power.CurrentUsedPower} / {power.CurrentAvailablePower} kW");
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
            int requested = eco.GetRequestedSubsidy();

            yield return WaitUntil(() => income >= 0, 40, "월간 정산");
            eco.OnMonthlySettled -= handler;

            Note($"정산: 지원금 +{income:N0}, 유지비 -{paid:N0} (미납 {unpaid:N0}), 전력 -{powerCost:N0}");
            Check("지원금 = 슬라이더로 신청한 금액", income == requested, $"{income:N0} / {requested:N0}");
            Check("유지비 = 건물 유지비 합계", paid == maintenance, $"{paid:N0} / {maintenance:N0}");
            Check("잔액 = 이전 + 지원금 − 유지비 − 전력 구매",
                eco.CurrentMoney == moneyBefore + income - paid - powerCost, $"{moneyBefore:N0} → {eco.CurrentMoney:N0}");
            Check("정산 후 보유 전력 = 슬라이더로 정한 구매량", power.CurrentAvailablePower == power.TargetPurchasedPower,
                $"{power.CurrentAvailablePower} kW");
        }

        private static IEnumerator PowerPoleCheck()
        {
            var power = PowerManager.Instance;
            float farBefore = Placed["house29"].GetComponent<HousingInstance>().CurrentSatisfaction;

            var pole = Place("power_pole", 1, 1, "pole");
            yield return Frames(1);
            Check("전신주에 UtilityPoleInstance 자동 부착", pole != null && pole.GetComponent<UtilityPoleInstance>() != null);
            Check("전력망 생김", power.HasPowerGrid);
            Check("반경 4 안 주택(x=0) 전기 공급", power.IsHousingPowered(Placed["house0"]));
            Check("반경 밖 주택(x=29) 전기 끊김", !power.IsHousingPowered(Placed["house29"]));
            float farAfter = Placed["house29"].GetComponent<HousingInstance>().CurrentSatisfaction;
            Check("전기 끊긴 주택 만족도 하락", farAfter < farBefore, $"{farBefore:F1} → {farAfter:F1}");

            pole.DestroyBuilding();
            Placed.Remove("pole");
            yield return Frames(2);
            Check("전신주 철거 → 전력망 해제", !power.HasPowerGrid);
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

            // 시설 전부 철거 + 전력 0 + 매립지로 주택을 '나쁨'으로
            foreach (var key in Placed.Keys.Where(k => !k.StartsWith("house")).ToList())
            {
                Placed[key].DestroyBuilding();
                Placed.Remove(key);
            }
            yield return Frames(2);
            power.SetTargetPurchasedPower(0);
            power.ProcessMonthlyPowerPurchase();
            foreach (int x in new[] { 0, 8, 16, 24 }) Place("landfill", x, 2, $"landfill{x}");
            yield return Frames(2);

            Note($"악화된 마을: 평균 {sat.CurrentAverageSatisfaction:F1}, 상태 {sat.CurrentState}, 나쁨 주택 {sat.BadHousingRatio:P0}");
            Check("마을 상태 '나쁨'", sat.CurrentState == SatisfactionState.Bad);
            Shot("03_bad_town", null);

            yield return WaitUntil(() => ended != GameResult.None, 40, "배드엔딩");
            Check("나쁨 30일 연속 → 배드엔딩", ended == GameResult.Defeat, $"{ended}, 나쁨 {lastBad}일");
            Check("엔딩 후 시간 정지", TimeManager.Instance.IsEventPaused);
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

            if (sat.GoodHousingRatio < outcome.RequiredGoodRatio)
            {
                Note($"좋음 비율이 기준({outcome.RequiredGoodRatio:P0})에 못 미침 → 10년이 지나도 승리하지 않아야 함");
                yield return WaitDays(outcome.VictoryDays + 30);
                Check("좋음 비율 미달 → 10년이 지나도 계속 플레이", !outcome.IsGameOver, $"{outcome.DaysElapsed}일, 결과 {outcome.Result}");
                yield break;
            }

            GameResult ended = GameResult.None;
            outcome.OnGameEnded += r => ended = r;
            double t0 = EditorApplication.timeSinceStartup;
            yield return WaitUntil(() => ended != GameResult.None, outcome.VictoryDays + 60, "해피엔딩");
            Note($"10년 진행: 실제 {EditorApplication.timeSinceStartup - t0:F0}초, 엔딩 판정 {outcome.DaysElapsed}일 ({TimeManager.Instance.CurrentYear}년 {TimeManager.Instance.CurrentMonth}월 {TimeManager.Instance.CurrentDay}일)");
            Check("10년 경과 + 조건 충족 → 해피엔딩", ended == GameResult.Victory, $"{ended}, {outcome.DaysElapsed}일째");
            Check("정확히 3,600일째에 판정", outcome.DaysElapsed == outcome.VictoryDays, $"{outcome.DaysElapsed}일");
            Note($"10년 뒤: 인구 {PopulationManager.Instance.CurrentPopulation}명, 잔액 {EconomyManager.Instance.CurrentMoney:N0}원, 만족도 {sat.CurrentAverageSatisfaction:F1}, 좋음 {sat.GoodHousingRatio:P0}");
            Shot("04_victory_town", null);
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
            manager.SelectBuildingToBuild(Data(id));
            TestWorld.Invoke(manager, "TryPlaceBuilding", new Vector2Int(x, y));
            manager.CancelBuildingMode();

            var placed = GetGrid()[x, y];
            if (placed != null && placed.Data.buildingID != id) placed = null;
            if (key != null) Placed[key] = placed;
            return placed;
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

        private static float _normalSecondsPerDay = -1f;

        /// <summary>게임 시간을 하루 1프레임으로 빠르게 돌림 (대기가 끝나면 RestoreSpeed로 원래 속도 복원)</summary>
        private static void FastForward()
        {
            var time = TimeManager.Instance;
            float current = TestWorld.GetField<float>(time, "secondsPerDay");
            if (current > 0.001f) _normalSecondsPerDay = current;
            TestWorld.SetField(time, "secondsPerDay", 0.0001f);
            if (time.CurrentSpeed == GameSpeed.Pause) time.SetGameSpeed(GameSpeed.Normal);
        }

        private static void RestoreSpeed()
        {
            if (TimeManager.Instance != null && _normalSecondsPerDay > 0f)
                TestWorld.SetField(TimeManager.Instance, "secondsPerDay", _normalSecondsPerDay);
        }

        private static IEnumerator WaitDays(int days)
        {
            FastForward();
            int count = 0;
            Action<int, int, int> handler = (y, m, d) => count++;
            TimeManager.Instance.OnDateChanged += handler;
            int guard = days * 20 + 200;
            while (count < days && guard-- > 0) yield return null;
            TimeManager.Instance.OnDateChanged -= handler;
            RestoreSpeed();
            if (count < days) Fail($"{days}일 대기", $"{count}일만 진행됨");
        }

        private static IEnumerator WaitUntil(Func<bool> condition, int maxDays, string label)
        {
            FastForward();
            int days = 0;
            Action<int, int, int> handler = (y, m, d) => days++;
            TimeManager.Instance.OnDateChanged += handler;
            int guard = maxDays * 20 + 200;
            while (!condition() && days <= maxDays && guard-- > 0) yield return null;
            TimeManager.Instance.OnDateChanged -= handler;
            RestoreSpeed();
            if (!condition()) Fail(label, $"{days}일 진행했지만 조건 미충족");
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

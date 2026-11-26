using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.Population;
using InfraTown.Power;
using InfraTown.Satisfaction;
using InfraTown.Save;
using InfraTown.Services;
using InfraTown.TimeSystem;
using Dlg = InfraTown.EventSystem;

namespace InfraTown.Tests.Playtest
{
    /// <summary>
    /// 세이브 확인 모드 (-Mode save): 실제 씬에서 마을을 만들고 며칠 진행한 뒤 저장 → 같은 씬을 새로 열어 이어하기 →
    /// 날짜·돈·인구·전력·만족도·건물·대화·튜토리얼이 그대로인지, 오프닝이 다시 나오지 않는지, 카메라는 저장되지 않는지 확인한다.
    /// 저장 파일은 Logs/Playtest/Saves에 만든다 (실제 저장 폴더를 건드리지 않음).
    /// </summary>
    public static partial class PlaytestRunner
    {
        private class GameSnapshot
        {
            public int year, month, week, totalWeeks, money, subsidy, population, capacity, powerTarget, powerAvailable, powerUsed, gasTarget, gasAvailable, gasUsed;
            public float satisfaction, missingPenalty;
            public int weeksElapsed, goodStreak, badStreak;
            public List<string> buildings;
            public int playedDialogues;
            public bool storyStarted, tutorialActive, tutorialCompleted;
            public int tutorialWeek;
            public string playerName;
            public GameSpeed speed;

            public override string ToString() =>
                $"{year}년 {month}월 {week}주차(누적 {totalWeeks}주), 돈 {money:N0}, 신청 지원금 {subsidy:N0}, 인구 {population}/{capacity}, " +
                $"전력 {powerUsed}/{powerAvailable}kW(다음 달 {powerTarget}), 가스 {gasUsed}/{gasAvailable}(다음 달 {gasTarget}), 만족도 {satisfaction:F2}(미설치 감점 {missingPenalty}), " +
                $"엔딩 판정 {weeksElapsed}주·좋음 {goodStreak}·나쁨 {badStreak}, 건물 {buildings.Count}개, 본 대화 {playedDialogues}개, " +
                $"튜토리얼 {(tutorialCompleted ? "끝남" : tutorialActive ? $"{tutorialWeek}주차" : "시작 전")}, 이름 {playerName}, 배속 {speed}";
        }

        private static GameSnapshot Snapshot()
        {
            var t = TimeManager.Instance;
            var sat = SatisfactionManager.Instance;
            var outcome = GameOutcomeManager.Instance;
            var tutorial = Dlg.TutorialDialogues.Instance;
            var triggers = Object.FindObjectOfType<Dlg.DialogueTriggers>();
            return new GameSnapshot
            {
                year = t.CurrentYear, month = t.CurrentMonth, week = t.CurrentWeek,
                totalWeeks = t.TotalWeeksPassed,
                speed = t.CurrentSpeed,
                money = EconomyManager.Instance.CurrentMoney,
                subsidy = EconomyManager.Instance.GetRequestedSubsidy(),
                population = PopulationManager.Instance.CurrentPopulation,
                capacity = PopulationManager.Instance.MaxPopulationCapacity,
                powerTarget = PowerManager.Instance.TargetPurchasedPower,
                powerAvailable = PowerManager.Instance.CurrentAvailablePower,
                powerUsed = PowerManager.Instance.CurrentUsedPower,                gasTarget = InfraTown.Gas.GasManager.Instance.TargetPurchasedGas,                gasAvailable = InfraTown.Gas.GasManager.Instance.CurrentAvailableGas,                gasUsed = InfraTown.Gas.GasManager.Instance.CurrentUsedGas,
                satisfaction = sat.CurrentAverageSatisfaction,
                missingPenalty = sat.TotalMissingFacilityPenalty,
                weeksElapsed = outcome.WeeksElapsed, goodStreak = outcome.GoodStreakWeeks, badStreak = outcome.BadStreakWeeks,
                buildings = GameLoopManager.Instance.PlacedBuildings
                    .Where(b => b != null)
                    .Select(b => $"{b.Data.buildingID}@{b.OriginGridPos.x},{b.OriginGridPos.y}/{b.RotationY}")
                    .OrderBy(s => s).ToList(),
                playedDialogues = Dlg.DialogueEventManager.Instance != null ? Dlg.DialogueEventManager.Instance.PlayedDialogueIds.Count : 0,
                storyStarted = triggers != null && triggers.StoryStarted,
                tutorialActive = tutorial != null && tutorial.IsActive,
                tutorialCompleted = tutorial != null && tutorial.IsCompleted,
                tutorialWeek = tutorial != null ? tutorial.CurrentWeek : 0,
                playerName = PlayerProfile.PlayerName,
            };
        }

        // ───── 저장 데이터 전체 비교 (2026-11-26: 대표 값만이 아니라 SaveData의 모든 구역) ─────

        /// <summary>지금 게임의 저장 데이터를 구역별 JSON으로 (순서가 의미 없는 목록은 정렬, 저장 시각·주 타이머는 빼고 따로 비교)</summary>
        private static Dictionary<string, string> FullState(SaveData d)
        {
            d.savedAt = null;
            d.resumeSlot = 0;
            d.buildings = d.buildings.OrderBy(b => b.buildingID).ThenBy(b => b.x).ThenBy(b => b.y).ToList();
            d.dialogue.playedDialogueIds.Sort();
            d.dialogue.playedCutsceneIds.Sort();
            d.dialogueTriggers.knownUnlockedBuildingIds.Sort();
            d.dialogueTriggers.essentials = d.dialogueTriggers.essentials.OrderBy(e => e.service).ToList();
            d.tutorial.triggeredStepIds.Sort();
            var time = JsonUtility.FromJson<TimeSaveState>(JsonUtility.ToJson(d.time));
            time.weekTimer = 0f;
            return new Dictionary<string, string>
            {
                ["씬·주인공 이름"] = $"{d.sceneName}/{d.playerName}",
                ["시간(주 타이머 제외)"] = JsonUtility.ToJson(time),
                ["돈·지원금 설정·최근 정산"] = JsonUtility.ToJson(d.economy),
                ["인구"] = JsonUtility.ToJson(d.population),
                ["전력"] = JsonUtility.ToJson(d.power),
                ["가스"] = JsonUtility.ToJson(d.gas),
                ["필수 시설 미설치 감점"] = JsonUtility.ToJson(d.satisfaction),
                ["엔딩 판정"] = JsonUtility.ToJson(d.outcome),
                ["건물(ID·칸·회전)"] = string.Join(";", d.buildings.Select(b => $"{b.buildingID}@{b.x},{b.y}/{b.rotationY}")),
                ["대화 기록·쉬운 문장·Auto"] = JsonUtility.ToJson(d.dialogue),
                ["대화 트리거(불만 주 수·해금 안내)"] = JsonUtility.ToJson(d.dialogueTriggers),
                ["튜토리얼"] = JsonUtility.ToJson(d.tutorial),
            };
        }

        /// <summary>저장하지 않고 불러온 뒤 다시 계산되는 상태 (칸 점유·도로 위 전신주·건물별 전기·접근성·만족도 단계·해금)</summary>
        private static Dictionary<string, string> DerivedState()
        {
            var bm = BuildingManager.Instance;
            var power = PowerManager.Instance;
            var sat = SatisfactionManager.Instance;
            var acc = ServiceAccessibilityManager.Instance;
            var cells = new System.Text.StringBuilder();
            var roadPoles = new List<string>();
            for (int x = 0; x < 60; x++)
                for (int y = 0; y < 40; y++)
                {
                    var c = new Vector2Int(x, y);
                    var b = bm.GetBuildingAt(c);
                    if (b == null) continue;
                    cells.Append($"{x},{y}={b.Data.buildingID}{(bm.IsRoadCell(x, y) ? "+road" : "")};");
                    if (bm.GetRoadPoleAt(c) != null) roadPoles.Add($"{x},{y}");
                }
            var powered = GameLoopManager.Instance.PlacedBuildings.Where(b => b != null)
                .Select(b => $"{b.Data.buildingID}@{b.OriginGridPos.x},{b.OriginGridPos.y}={(power.IsBuildingPowered(b) ? 1 : 0)}").OrderBy(s => s);
            return new Dictionary<string, string>
            {
                ["칸 점유(60x40 전체)"] = cells.ToString(),
                ["도로 위 전신주"] = string.Join(" ", roadPoles),
                ["건물별 전기 공급"] = string.Join(";", powered),
                ["꺼진 전봇대 수"] = power.BlackedOutPoles.Count.ToString(),
                ["필요 전력(건물+계절)"] = $"{power.CurrentUsedPower}",
                ["가스 사용량"] = $"{InfraTown.Gas.GasManager.Instance.CurrentUsedGas}",
                ["수용 인구"] = $"{PopulationManager.Instance.MaxPopulationCapacity}",
                ["접근 시간(병원·소방·경찰·학교·편의)"] = $"{acc.HospitalAccessTime:F2}/{acc.FireStationAccessTime:F2}/{acc.PoliceAccessTime:F2}/{acc.SchoolAccessTime:F2}/{acc.ConvenienceAccessTime:F2}",
                ["만족도(평균·단계·좋음 비율)"] = $"{sat.CurrentAverageSatisfaction:F2}/{sat.CurrentTier}/{sat.CurrentState}/{sat.GoodHousingRatio:F3}",
                ["해금된 건물"] = string.Join(",", bm.GetUnlockedBuildings().Select(b => b.buildingID).OrderBy(s => s)),
            };
        }

        private static void CompareStates(string label, Dictionary<string, string> before, Dictionary<string, string> after)
        {
            foreach (var k in before.Keys)
            {
                bool same = after.TryGetValue(k, out var v) && v == before[k];
                Check($"{label} — {k}", same, same ? Short(before[k]) : $"전: {Short(before[k])} / 후: {Short(v)}");
            }
        }

        private static string Short(string s) => s == null ? "(없음)" : s.Length <= 160 ? s : s.Substring(0, 160) + $"… ({s.Length}자)";

        /// <summary>
        /// 저장 파일을 불러와 씬을 다시 연다. 씬이 Build Settings에 있으면 실제 게임과 같은 함수(realLoad: ContinueGame/ContinueResume)로,
        /// 없으면 에디터 방식(PrepareLoad + LoadSceneInPlayMode)으로. 적용이 끝난 그 프레임에 돌아온다
        /// </summary>
        private static IEnumerator ReloadFromSave(string label, System.Func<bool> realLoad, SaveData fallback)
        {
            var oldLoop = GameLoopManager.Instance;
            Placed.Clear();
            string scenePath = SessionState.GetString(SceneKey, DefaultScene);
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            if (Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Note($"{label}: 씬 '{sceneName}'이 Build Settings에 있어 실제 게임과 같은 함수로 불러옴");
                Check($"{label}: 불러오기 함수 성공", realLoad(), SaveSystem.LastError);
            }
            else
            {
                Note($"{label}: 씬 '{sceneName}'이 Build Settings에 없어 에디터 방식으로 엶 (저장 파일 내용은 같음)");
                SaveSystem.PrepareLoad(fallback);
                EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
            }
            int guard = 0;
            while ((SaveSystem.HasPendingLoad || GameLoopManager.Instance == null || GameLoopManager.Instance == oldLoop) && guard++ < 600) yield return null;
            Check($"{label}: 새 씬에서 저장 데이터가 적용됨", !SaveSystem.HasPendingLoad && GameLoopManager.Instance != oldLoop);
        }

        private static SaveData ReadFile(string path) => File.Exists(path) ? JsonUtility.FromJson<SaveData>(File.ReadAllText(path)) : null;

        private static IEnumerator SaveScenario()
        {
            string folder = Path.Combine(SessionState.GetString(OutKey, "Logs/Playtest"), "Saves");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            SaveSystem.FolderOverrideForTests = folder;

            Section("S-1. 마을을 만들고 몇 주 진행");
            PlayerProfile.TrySetPlayerName("민수", out _);
            yield return PlaceHouses();
            yield return PlaceMiddle(withNimby: true);
            yield return EnsurePower();
            // 회전한 건물도 하나 (회전 저장 확인)
            var manager = BuildingManager.Instance;
            Place("road", 40, 19, "rotated_store_road"); // 도로에 맞닿아야 설치 (사용자 기획 2026-11-24). 도로를 먼저 깔아야 아래 회전이 유지됨
            var store = manager.FindBuildingData("convenience_store");
            manager.SelectBuildingToBuild(store);
            manager.RotatePreview(90f);
            Place("convenience_store", 40, 20, "rotated_store");
            manager.CancelBuildingMode();

            EconomyManager.Instance.SetGovernmentSubsidyAmount(15100);
            PowerManager.Instance.SetTargetPurchasedPower(1230);            InfraTown.Gas.GasManager.Instance.SetTargetPurchasedGas(470);
            yield return WaitWeeks(4); // 월 정산·주간 틱·튜토리얼 진행 (튜토리얼 7주 중간에 저장)
            TimeManager.Instance.SetGameSpeed(GameSpeed.Fast);
            // 기본값이 아닌 설정도 저장되는지 (쉬운 문장·Auto·지원금을 최대치에 맞춤)
            var dialogueManager = Dlg.DialogueEventManager.Instance;
            bool hasDialogue = dialogueManager != null;
            if (hasDialogue) { dialogueManager.UseEasyText = true; dialogueManager.SetAutoMode(true); }
            else Note("⚠ 이 씬에는 DialogueEventManager(대화 시스템)가 없음 → 대화·이야기·튜토리얼 저장은 확인할 수 없음 (값이 비어 있는 채로 비교)");
            yield return Frames(2);

            var fullBefore = FullState(SaveSystem.CaptureCurrentGame());
            var derivedBefore = DerivedState();
            float weekTimerBefore = TimeManager.Instance.GetSaveState().weekTimer;
            var before = Snapshot();
            Note($"저장 직전: {before}");
            Check("저장 전에 진행된 내용이 있음", before.buildings.Count > 30 && before.weeksElapsed >= 5 && (!hasDialogue || (before.storyStarted && before.tutorialActive)), $"건물 {before.buildings.Count}개, {before.weeksElapsed}주");

            // 카메라는 저장하지 않음 → 저장 직전에 옮겨 두고, 이어하기 뒤 씬 기본 위치인지 확인
            var cam = Camera.main;
            Vector3 sceneCamPos = cam != null ? cam.transform.position : Vector3.zero;
            if (cam != null) cam.transform.position = sceneCamPos + new Vector3(37f, 11f, -9f);

            Section("S-2. 저장");
            Check("2번 슬롯에 저장 성공", SaveSystem.SaveGame(2), SaveSystem.LastError);
            // 저장 전 4주 동안의 자동 저장은 자동 저장 전용 슬롯에만 (사용자 기획 2026-11-25) → 1~5번 중에는 2번만
            Check("2번 슬롯 저장 파일 생성, 현재 슬롯 2번 (1~5번 중 2번만, 자동 저장은 전용 슬롯)",
                SaveSystem.HasSave(2) && SaveSystem.CurrentSlot == 2 && SaveSystem.HasAutoSave() && Enumerable.Range(1, 5).Count(s => SaveSystem.HasSave(s)) == 1,
                string.Join(", ", Enumerable.Range(1, 5).Where(s => SaveSystem.HasSave(s)).Select(s => $"{s}번")) + (SaveSystem.HasAutoSave() ? ", 자동 저장" : ""));
            int savedWeekSlot2 = SaveSystem.LoadData(2).time.totalWeeksPassed;
            var summary = SaveSystem.GetSummary(2);
            Note($"저장 요약: {summary?.playerName}, {summary?.year}년 {summary?.month}월 {summary?.week}주차, 돈 {summary?.money:N0}, 인구 {summary?.population}, 건물 {summary?.buildingCount}개, {summary?.savedAt}");
            Check("저장 요약이 지금 상태와 같음", summary != null && summary.money == before.money && summary.population == before.population && summary.buildingCount == before.buildings.Count);
            Shot("s01_before_save", null);

            Section("S-3. 씬을 새로 열고 이어하기");
            var data = SaveSystem.LoadData(2);
            Check("저장 파일 읽기", data != null, SaveSystem.LastError);
            if (data == null) yield break;
            Note($"저장 파일 크기 {new FileInfo(SaveSystem.GetSavePath()).Length / 1024f:F1}KB, 씬 '{data.sceneName}'");
            CompareStates("저장 파일 = 저장 직전 상태", fullBefore, FullState(SaveSystem.LoadData(2)));

            // 씬이 Build Settings에 있으면 실제 게임처럼 SaveSystem.ContinueGame(2), 없으면 에디터 방식
            yield return ReloadFromSave("2번 슬롯 불러오기", () => SaveSystem.ContinueGame(2), data);
            var fullAfter = FullState(SaveSystem.CaptureCurrentGame());
            var derivedAfter = DerivedState();
            float weekTimerAfter = TimeManager.Instance.GetSaveState().weekTimer;
            CompareStates("불러온 상태 = 저장 직전", fullBefore, fullAfter);
            CompareStates("다시 계산된 상태 = 저장 직전", derivedBefore, derivedAfter);
            Check("주 안의 진행 시간(주 타이머)도 이어짐", weekTimerAfter >= weekTimerBefore && weekTimerAfter - weekTimerBefore < 2f,
                $"{weekTimerBefore:F2}초 → {weekTimerAfter:F2}초 (불러온 프레임만큼 흐름)");
            Check("불러온 뒤 현재 슬롯 = 2번", SaveSystem.CurrentSlot == 2, $"{SaveSystem.CurrentSlot}");
            yield return Frames(5);

            var after = Snapshot();
            Note($"이어하기 직후: {after}");
            Check("날짜·누적 주 수·배속", after.year == before.year && after.month == before.month && after.week == before.week && after.totalWeeks == before.totalWeeks && after.speed == before.speed,
                $"{before.year}/{before.month}/{before.week} → {after.year}/{after.month}/{after.week}, {before.speed} → {after.speed}");
            Check("돈·신청 지원금", after.money == before.money && after.subsidy == before.subsidy, $"{before.money:N0} → {after.money:N0}");
            Check("인구·수용 인구", after.population == before.population && after.capacity == before.capacity, $"{before.population}/{before.capacity} → {after.population}/{after.capacity}");
            Check("전력 (보유·다음 달 구매량·사용량)", after.powerAvailable == before.powerAvailable && after.powerTarget == before.powerTarget && after.powerUsed == before.powerUsed,
                $"{before.powerUsed}/{before.powerAvailable}({before.powerTarget}) → {after.powerUsed}/{after.powerAvailable}({after.powerTarget})");
            Check("가스 (보유·다음 달 구매량·사용량)", after.gasAvailable == before.gasAvailable && after.gasTarget == before.gasTarget && after.gasUsed == before.gasUsed && after.gasTarget == 470,
                $"{before.gasUsed}/{before.gasAvailable}({before.gasTarget}) → {after.gasUsed}/{after.gasAvailable}({after.gasTarget})");
            Check("만족도·미설치 감점", Mathf.Abs(after.satisfaction - before.satisfaction) < 0.01f && Mathf.Approximately(after.missingPenalty, before.missingPenalty),
                $"{before.satisfaction:F2} → {after.satisfaction:F2}");
            Check("엔딩 판정 주 수", after.weeksElapsed == before.weeksElapsed && after.goodStreak == before.goodStreak && after.badStreak == before.badStreak);
            Check("건물 종류·위치·회전 전부", after.buildings.SequenceEqual(before.buildings), $"{before.buildings.Count} → {after.buildings.Count}개");
            Check("회전한 건물의 회전도 그대로", after.buildings.Any(b => b.StartsWith("convenience_store@40,20/90")));
            Check("주인공 이름", after.playerName == before.playerName);
            Check("본 대화 기록", after.playedDialogues == before.playedDialogues, $"{before.playedDialogues} → {after.playedDialogues}");
            Check("튜토리얼 진행", after.tutorialActive == before.tutorialActive && after.tutorialCompleted == before.tutorialCompleted && after.tutorialWeek == before.tutorialWeek);
            if (Dlg.DialogueEventManager.Instance != null)
                Check("오프닝이 다시 시작되지 않음 (이야기 이미 시작된 상태)", after.storyStarted && !Dlg.DialogueEventManager.Instance.IsBusy);
            else Note("대화 시스템이 없어 오프닝 재시작 확인은 건너뜀");

            var camAfter = Camera.main;
            Check("카메라 위치는 저장되지 않음 (씬 기본 위치)", camAfter != null && Vector3.Distance(camAfter.transform.position, sceneCamPos) < 0.5f,
                camAfter != null ? $"{camAfter.transform.position}" : "카메라 없음");

            // 점유 칸도 복원됐는지: 저장된 건물 자리에 새로 지을 수 없어야 함
            var grid = TestWorld.GetField<BuildingInstance[,]>(BuildingManager.Instance, "_occupiedGrid");
            Check("건물 자리 점유 칸도 복원", grid[40, 20] != null && grid[0, 0] != null);
            Shot("s02_after_continue", null);

            Section("S-4. 이어서 플레이");
            int weekBefore = WeekIndex(TimeManager.Instance);
            int elapsedBefore = GameOutcomeManager.Instance.WeeksElapsed;
            yield return WaitWeeks(3);
            Check("시간이 이어서 흐르고 엔딩 판정도 이어서 셈", WeekIndex(TimeManager.Instance) - weekBefore == 3 && GameOutcomeManager.Instance.WeeksElapsed - elapsedBefore == 3,
                $"날짜 +{WeekIndex(TimeManager.Instance) - weekBefore}, 판정 +{GameOutcomeManager.Instance.WeeksElapsed - elapsedBefore}");
            yield return Frames(1); // 마지막 주의 LateUpdate(자동 저장)
            var autoSaved = SaveSystem.LoadData(SaveSystem.AutoSaveSlot);
            Check("이어서 하는 동안 1주마다 자동 저장 슬롯에 저장 (불러온 2번은 그대로)",
                autoSaved != null && autoSaved.time.totalWeeksPassed == TimeManager.Instance.TotalWeeksPassed && SaveSystem.GetLatestSlot() == SaveSystem.AutoSaveSlot
                && SaveSystem.LoadData(2).time.totalWeeksPassed == savedWeekSlot2,
                $"자동 저장 {autoSaved?.time.totalWeeksPassed}주 / 지금 {TimeManager.Instance.TotalWeeksPassed}주, 2번 {SaveSystem.LoadData(2)?.time.totalWeeksPassed}주(저장 때 {savedWeekSlot2}주)");
            int moneyBefore = EconomyManager.Instance.CurrentMoney;
            Check("불러온 건물(축사 50,30) 자리에는 겹쳐 지을 수 없음", Place("house", 50, 30, null) == null && EconomyManager.Instance.CurrentMoney == moneyBefore);
            var built = PlaceWithRoad("house", 55, 10, "after_load_house");
            Check("이어서 건물을 지을 수 있음 (건설비 차감)", built != null && EconomyManager.Instance.CurrentMoney < moneyBefore,
                $"건물 {(built != null ? "생김" : "없음")}, 돈 {moneyBefore:N0} → {EconomyManager.Instance.CurrentMoney:N0}, 배치 모드 {BuildingManager.Instance.IsPlacingBuilding}, 시간 정지 {TimeManager.Instance.IsEventPaused}");

            // ── S-5. 자동 저장 슬롯 불러오기 (불러오기 화면 맨 앞 칸) ──
            Section("S-5. 자동 저장 슬롯 불러오기");
            yield return Frames(2);
            Check("자동 저장 성공", SaveSystem.AutoSave(), SaveSystem.LastError);
            var autoBefore = FullState(SaveSystem.CaptureCurrentGame());
            var autoDerivedBefore = DerivedState();
            CompareStates("자동 저장 파일 = 저장 직전 상태", autoBefore, FullState(SaveSystem.LoadData(SaveSystem.AutoSaveSlot)));
            yield return ReloadFromSave("자동 저장 불러오기", () => SaveSystem.ContinueGame(SaveSystem.AutoSaveSlot), SaveSystem.LoadData(SaveSystem.AutoSaveSlot));
            CompareStates("자동 저장을 불러온 상태 = 저장 직전", autoBefore, FullState(SaveSystem.CaptureCurrentGame()));
            CompareStates("자동 저장을 불러온 뒤 다시 계산된 상태", autoDerivedBefore, DerivedState());
            Check("자동 저장을 불러와도 현재 슬롯은 그대로 2번", SaveSystem.CurrentSlot == 2, $"{SaveSystem.CurrentSlot}");
            Check("이어서 지은 집(55,10)도 자동 저장에 들어 있음", BuildingManager.Instance.GetBuildingAt(new Vector2Int(55, 10))?.Data.buildingID == "house");

            // ── S-6. 종료 직전 이어하기 저장 (팀원 1 Jinyu234: resume.json → 타이틀 '이어하기') ──
            Section("S-6. 종료 직전 이어하기 저장 (resume.json)");
            yield return WaitWeeks(1);
            Check("이어하기 저장 성공 (SaveResume)", SaveSystem.SaveResume(), SaveSystem.LastError);
            var resumeBefore = FullState(SaveSystem.CaptureCurrentGame());
            var resumeDerivedBefore = DerivedState();
            string resumePath = Path.Combine(SaveSystem.SaveFolder, "resume.json");
            var resumeData = ReadFile(resumePath);
            Check("resume.json 생성, HasResume", resumeData != null && SaveSystem.HasResume(), resumePath);
            Check("resume.json이 기억한 현재 슬롯 = 2번", resumeData != null && resumeData.resumeSlot == 2, $"{resumeData?.resumeSlot}");
            if (resumeData != null) CompareStates("resume.json = 저장 직전 상태", resumeBefore, FullState(ReadFile(resumePath)));
            Check("이어하기 저장은 슬롯 목록·최근 슬롯에 끼지 않음", SaveSystem.GetLatestSlot() == SaveSystem.AutoSaveSlot && SaveSystem.GetAllSlots().Count(s => !s.isEmpty) == 1,
                $"최근 {SaveSystem.GetLatestSlot()}, 1~5번 중 저장 {SaveSystem.GetAllSlots().Count(s => !s.isEmpty)}개");
            SaveSystem.SetCurrentSlot(4); // 이어하기가 저장 당시 슬롯(2번)을 되돌리는지 보려고 바꿔 둠
            yield return ReloadFromSave("이어하기(ContinueResume)", SaveSystem.ContinueResume, resumeData);
            if (!Application.CanStreamedLevelBeLoaded(Path.GetFileNameWithoutExtension(SessionState.GetString(SceneKey, DefaultScene))))
                SaveSystem.SetCurrentSlot(resumeData.resumeSlot); // 에디터 방식은 ContinueResume이 하는 슬롯 복원을 대신 함
            CompareStates("이어하기로 불러온 상태 = 저장 직전", resumeBefore, FullState(SaveSystem.CaptureCurrentGame()));
            CompareStates("이어하기로 불러온 뒤 다시 계산된 상태", resumeDerivedBefore, DerivedState());
            Check("이어하기 뒤 현재 슬롯 = 저장 당시 2번", SaveSystem.CurrentSlot == 2, $"{SaveSystem.CurrentSlot}");
            Shot("s03_after_resume", null);

            // 저장 폴더는 테스트 폴더 그대로 둠 (자동 저장이 실제 세이브 폴더를 덮어쓰지 않도록, 2026-11-25)
        }
    }
}

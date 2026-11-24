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
            public int year, month, day, totalDays, money, subsidy, population, capacity, powerTarget, powerAvailable, powerUsed;
            public float satisfaction, missingPenalty;
            public int daysElapsed, goodStreak, badStreak;
            public List<string> buildings;
            public int playedDialogues;
            public bool storyStarted, tutorialActive, tutorialCompleted;
            public int tutorialDay;
            public string playerName;
            public GameSpeed speed;

            public override string ToString() =>
                $"{year}년 {month}월 {day}일(누적 {totalDays}일), 돈 {money:N0}, 신청 지원금 {subsidy:N0}, 인구 {population}/{capacity}, " +
                $"전력 {powerUsed}/{powerAvailable}kW(다음 달 {powerTarget}), 만족도 {satisfaction:F2}(미설치 감점 {missingPenalty}), " +
                $"엔딩 판정 {daysElapsed}일·좋음 {goodStreak}·나쁨 {badStreak}, 건물 {buildings.Count}개, 본 대화 {playedDialogues}개, " +
                $"튜토리얼 {(tutorialCompleted ? "끝남" : tutorialActive ? $"{tutorialDay}일차" : "시작 전")}, 이름 {playerName}, 배속 {speed}";
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
                year = t.CurrentYear, month = t.CurrentMonth, day = t.CurrentDay,
                totalDays = TestWorld.GetField<int>(t, "_totalDaysPassed"),
                speed = t.CurrentSpeed,
                money = EconomyManager.Instance.CurrentMoney,
                subsidy = EconomyManager.Instance.GetRequestedSubsidy(),
                population = PopulationManager.Instance.CurrentPopulation,
                capacity = PopulationManager.Instance.MaxPopulationCapacity,
                powerTarget = PowerManager.Instance.TargetPurchasedPower,
                powerAvailable = PowerManager.Instance.CurrentAvailablePower,
                powerUsed = PowerManager.Instance.CurrentUsedPower,
                satisfaction = sat.CurrentAverageSatisfaction,
                missingPenalty = sat.TotalMissingFacilityPenalty,
                daysElapsed = outcome.DaysElapsed, goodStreak = outcome.GoodStreakDays, badStreak = outcome.BadStreakDays,
                buildings = GameLoopManager.Instance.PlacedBuildings
                    .Where(b => b != null)
                    .Select(b => $"{b.Data.buildingID}@{b.OriginGridPos.x},{b.OriginGridPos.y}/{b.RotationY}")
                    .OrderBy(s => s).ToList(),
                playedDialogues = Dlg.DialogueEventManager.Instance.PlayedDialogueIds.Count,
                storyStarted = triggers != null && triggers.StoryStarted,
                tutorialActive = tutorial != null && tutorial.IsActive,
                tutorialCompleted = tutorial != null && tutorial.IsCompleted,
                tutorialDay = tutorial != null ? tutorial.CurrentDay : 0,
                playerName = PlayerProfile.PlayerName,
            };
        }

        private static IEnumerator SaveScenario()
        {
            string folder = Path.Combine(SessionState.GetString(OutKey, "Logs/Playtest"), "Saves");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            SaveSystem.FolderOverrideForTests = folder;

            Section("S-1. 마을을 만들고 며칠 진행");
            PlayerProfile.TrySetPlayerName("민수", out _);
            yield return PlaceHouses();
            yield return PlaceMiddle(withNimby: true);
            yield return EnsurePower();
            // 회전한 건물도 하나 (회전 저장 확인)
            var manager = BuildingManager.Instance;
            var store = manager.FindBuildingData("convenience_store");
            manager.SelectBuildingToBuild(store);
            manager.RotatePreview(90f);
            Place("convenience_store", 40, 20, "rotated_store");
            manager.CancelBuildingMode();

            EconomyManager.Instance.SetGovernmentSubsidyAmount(15100);
            PowerManager.Instance.SetTargetPurchasedPower(1230);
            yield return WaitDays(40); // 월 정산·주간 틱·튜토리얼 진행
            TimeManager.Instance.SetGameSpeed(GameSpeed.Fast);
            yield return Frames(2);

            var before = Snapshot();
            Note($"저장 직전: {before}");
            Check("저장 전에 진행된 내용이 있음", before.buildings.Count > 30 && before.daysElapsed >= 40 && before.storyStarted, $"건물 {before.buildings.Count}개, {before.daysElapsed}일");

            // 카메라는 저장하지 않음 → 저장 직전에 옮겨 두고, 이어하기 뒤 씬 기본 위치인지 확인
            var cam = Camera.main;
            Vector3 sceneCamPos = cam != null ? cam.transform.position : Vector3.zero;
            if (cam != null) cam.transform.position = sceneCamPos + new Vector3(37f, 11f, -9f);

            Section("S-2. 저장");
            Check("저장 성공", SaveSystem.SaveGame(), SaveSystem.LastError);
            Check("저장 파일 생성", SaveSystem.HasSave(), SaveSystem.GetSavePath());
            var summary = SaveSystem.GetSummary();
            Note($"저장 요약: {summary?.playerName}, {summary?.year}년 {summary?.month}월 {summary?.day}일, 돈 {summary?.money:N0}, 인구 {summary?.population}, 건물 {summary?.buildingCount}개, {summary?.savedAt}");
            Check("저장 요약이 지금 상태와 같음", summary != null && summary.money == before.money && summary.population == before.population && summary.buildingCount == before.buildings.Count);
            Shot("s01_before_save", null);

            Section("S-3. 씬을 새로 열고 이어하기");
            var data = SaveSystem.LoadData();
            Check("저장 파일 읽기", data != null, SaveSystem.LastError);
            if (data == null) yield break;
            Note($"저장 파일 크기 {new FileInfo(SaveSystem.GetSavePath()).Length / 1024f:F1}KB, 씬 '{data.sceneName}'");

            // 실제 게임은 SaveSystem.ContinueGame()이 SceneManager.LoadScene을 부름. 테스트 씬은 Build Settings에 없어 에디터 방식으로 연다
            SaveSystem.PrepareLoad(data);
            Placed.Clear();
            string scenePath = SessionState.GetString(SceneKey, DefaultScene);
            EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
            int guard = 0;
            while ((SaveSystem.HasPendingLoad || GameLoopManager.Instance == null) && guard++ < 600) yield return null;
            yield return Frames(5);
            Check("새 씬에서 저장 데이터가 적용됨", !SaveSystem.HasPendingLoad);

            var after = Snapshot();
            Note($"이어하기 직후: {after}");
            Check("날짜·누적 일수·배속", after.year == before.year && after.month == before.month && after.day == before.day && after.totalDays == before.totalDays && after.speed == before.speed,
                $"{before.year}/{before.month}/{before.day} → {after.year}/{after.month}/{after.day}, {before.speed} → {after.speed}");
            Check("돈·신청 지원금", after.money == before.money && after.subsidy == before.subsidy, $"{before.money:N0} → {after.money:N0}");
            Check("인구·수용 인구", after.population == before.population && after.capacity == before.capacity, $"{before.population}/{before.capacity} → {after.population}/{after.capacity}");
            Check("전력 (보유·다음 달 구매량·사용량)", after.powerAvailable == before.powerAvailable && after.powerTarget == before.powerTarget && after.powerUsed == before.powerUsed,
                $"{before.powerUsed}/{before.powerAvailable}({before.powerTarget}) → {after.powerUsed}/{after.powerAvailable}({after.powerTarget})");
            Check("만족도·미설치 감점", Mathf.Abs(after.satisfaction - before.satisfaction) < 0.01f && Mathf.Approximately(after.missingPenalty, before.missingPenalty),
                $"{before.satisfaction:F2} → {after.satisfaction:F2}");
            Check("엔딩 판정 일수", after.daysElapsed == before.daysElapsed && after.goodStreak == before.goodStreak && after.badStreak == before.badStreak);
            Check("건물 종류·위치·회전 전부", after.buildings.SequenceEqual(before.buildings), $"{before.buildings.Count} → {after.buildings.Count}개");
            Check("회전한 건물의 회전도 그대로", after.buildings.Any(b => b.StartsWith("convenience_store@40,20/90")));
            Check("주인공 이름", after.playerName == before.playerName);
            Check("본 대화 기록", after.playedDialogues == before.playedDialogues, $"{before.playedDialogues} → {after.playedDialogues}");
            Check("튜토리얼 진행", after.tutorialActive == before.tutorialActive && after.tutorialCompleted == before.tutorialCompleted && after.tutorialDay == before.tutorialDay);
            Check("오프닝이 다시 시작되지 않음 (이야기 이미 시작된 상태)", after.storyStarted && !Dlg.DialogueEventManager.Instance.IsBusy);

            var camAfter = Camera.main;
            Check("카메라 위치는 저장되지 않음 (씬 기본 위치)", camAfter != null && Vector3.Distance(camAfter.transform.position, sceneCamPos) < 0.5f,
                camAfter != null ? $"{camAfter.transform.position}" : "카메라 없음");

            // 점유 칸도 복원됐는지: 저장된 건물 자리에 새로 지을 수 없어야 함
            var grid = TestWorld.GetField<BuildingInstance[,]>(BuildingManager.Instance, "_occupiedGrid");
            Check("건물 자리 점유 칸도 복원", grid[40, 20] != null && grid[0, 0] != null);
            Shot("s02_after_continue", null);

            Section("S-4. 이어서 플레이");
            int dayBefore = DayIndex(TimeManager.Instance);
            int elapsedBefore = GameOutcomeManager.Instance.DaysElapsed;
            yield return WaitDays(3);
            Check("시간이 이어서 흐르고 엔딩 판정도 이어서 셈", DayIndex(TimeManager.Instance) - dayBefore == 3 && GameOutcomeManager.Instance.DaysElapsed - elapsedBefore == 3,
                $"날짜 +{DayIndex(TimeManager.Instance) - dayBefore}, 판정 +{GameOutcomeManager.Instance.DaysElapsed - elapsedBefore}");
            int moneyBefore = EconomyManager.Instance.CurrentMoney;
            Check("불러온 건물(축사 50,30) 자리에는 겹쳐 지을 수 없음", Place("house", 50, 30, null) == null && EconomyManager.Instance.CurrentMoney == moneyBefore);
            var built = Place("house", 55, 10, "after_load_house");
            Check("이어서 건물을 지을 수 있음 (건설비 차감)", built != null && EconomyManager.Instance.CurrentMoney < moneyBefore,
                $"건물 {(built != null ? "생김" : "없음")}, 돈 {moneyBefore:N0} → {EconomyManager.Instance.CurrentMoney:N0}, 배치 모드 {BuildingManager.Instance.IsPlacingBuilding}, 시간 정지 {TimeManager.Instance.IsEventPaused}");

            SaveSystem.FolderOverrideForTests = null;
        }
    }
}

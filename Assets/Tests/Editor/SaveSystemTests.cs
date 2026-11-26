using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.EventSystem;
using InfraTown.Population;
using InfraTown.Power;
using InfraTown.Satisfaction;
using InfraTown.Save;
using InfraTown.Services;
using InfraTown.TimeSystem;

namespace InfraTown.Tests
{
    /// <summary>
    /// 세이브 (사용자 기획 2026-11-24): 카메라를 뺀 모든 상태를 저장하고, 새로 연 씬(새 매니저들)에 그대로 되돌린다
    /// </summary>
    public class SaveSystemTests
    {
        private TestWorld _world;
        private string _folder;
        private GameObject _prefab;

        private class Managers
        {
            public TimeManager time;
            public EconomyManager economy;
            public PopulationManager population;
            public PowerManager power;
            public InfraTown.Gas.GasManager gas;
            public SatisfactionManager satisfaction;
            public ServiceAccessibilityManager service;
            public GameLoopManager loop;
            public GameOutcomeManager outcome;
            public BuildingManager buildings;
            public DialogueEventManager dialogue;
            public CutsceneManager cutscene;
            public DialogueTriggers triggers;
            public TutorialDialogues tutorial;
            public BuildingData house, hospital, store;
        }

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "InfraTownSaveTests_" + System.Guid.NewGuid().ToString("N"));
            SaveSystem.FolderOverrideForTests = _folder;
            SaveSystem.SetCurrentSlot(1);
            SaveSystem.ClearPendingLoad();
            _prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _world = new TestWorld();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            CleanupWorld();
            Object.DestroyImmediate(_prefab);
            SaveSystem.FolderOverrideForTests = null;
            SaveSystem.ClearPendingLoad();
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        [Test]
        public void 종료_시점_저장은_수동_슬롯과_분리된다()
        {
            var managers = NewScene();
            Assert.IsFalse(SaveSystem.HasResume());
            Assert.IsTrue(SaveSystem.SaveGame(3), SaveSystem.LastError);
            string manual = File.ReadAllText(SaveSystem.GetSavePath(3));

            managers.time.SetGameSpeed(GameSpeed.Fast);
            Assert.IsTrue(SaveSystem.SaveResume(), SaveSystem.LastError);

            Assert.IsTrue(SaveSystem.HasResume());
            var resume = JsonUtility.FromJson<SaveData>(File.ReadAllText(Path.Combine(_folder, "resume.json")));
            Assert.AreEqual(3, resume.resumeSlot);
            Assert.AreEqual((int)GameSpeed.Fast, resume.time.speed);
            Assert.AreEqual(manual, File.ReadAllText(SaveSystem.GetSavePath(3)));
            CollectionAssert.AreEqual(new[] { 3 }, SaveSystem.GetAllSlots()
                .Where(slot => !slot.isEmpty).Select(slot => slot.slot));
        }

        private void CleanupWorld()
        {
            if (GameLoopManager.Instance != null)
            {
                foreach (var b in GameLoopManager.Instance.PlacedBuildings.ToList())
                    if (b != null) Object.DestroyImmediate(b.gameObject);
            }
            foreach (var m in Object.FindObjectsOfType<DialogueTriggers>()) TestWorld.Invoke(m, "OnDestroy");
            _world.Dispose();
        }

        /// <summary>씬 하나에 해당하는 매니저 세트 (이어하기 = 새 씬에 새 매니저)</summary>
        private Managers NewScene()
        {
            var m = new Managers
            {
                time = _world.CreateManager<TimeManager>(),
                economy = _world.CreateManager<EconomyManager>(),
                population = _world.CreateManager<PopulationManager>(),
                power = _world.CreateManager<PowerManager>(),
                gas = _world.CreateManager<InfraTown.Gas.GasManager>(),
                satisfaction = _world.CreateManager<SatisfactionManager>(),
                service = _world.CreateManager<ServiceAccessibilityManager>(),
                loop = _world.CreateManager<GameLoopManager>(),
                outcome = _world.CreateManager<GameOutcomeManager>(),
                buildings = _world.CreateManager<BuildingManager>(),
                dialogue = _world.CreateManager<DialogueEventManager>(),
                cutscene = _world.CreateManager<CutsceneManager>(),
                tutorial = _world.CreateManager<TutorialDialogues>(),
            };
            m.triggers = m.dialogue.gameObject.AddComponent<DialogueTriggers>();
            TestWorld.Invoke(m.triggers, "Awake");

            m.house = _world.CreateData(BuildingType.Housing, id: "house", name: "주택", capacity: 10, power: 5, maintenance: 10);
            m.hospital = _world.CreateData(BuildingType.Essential, id: "hospital", name: "병원", size: BuildingSizeType.Medium, power: 20);
            m.store = _world.CreateData(BuildingType.Facility, id: "convenience_store", name: "편의점", impact: 6f, radius: 7);
            foreach (var d in new[] { m.house, m.hospital, m.store }) d.buildingPrefab = _prefab;
            TestWorld.SetField(m.buildings, "buildingDatabase", new List<BuildingData> { m.house, m.hospital, m.store });
            return m;
        }

        /// <summary>몇 달 진행한 것 같은 상태를 만든다</summary>
        private void MakeProgress(Managers m)
        {
            PlayerProfile.TrySetPlayerName("민수", out _);
            m.time.ApplySaveState(new TimeSaveState { year = 2, month = 3, week = 3, totalWeeksPassed = 58, weekTimer = 41.5f, speed = 3 });
            m.buildings.RestoreBuilding(m.house, new Vector2Int(0, 0), 90f);
            m.buildings.RestoreBuilding(m.house, new Vector2Int(1, 0), 0f);
            m.buildings.RestoreBuilding(m.store, new Vector2Int(3, 0), 180f);
            m.buildings.RestoreBuilding(m.hospital, new Vector2Int(10, 10), 270f);
            m.population.AddPopulation(15);
            m.power.SetTargetPurchasedPower(650);
            m.power.ApplySaveState(new PowerSaveState { targetPurchasedPower = 650, currentAvailablePower = 400 });            m.gas.ApplySaveState(new GasSaveState { targetPurchasedGas = 120, currentAvailableGas = 80 });
            m.economy.ApplySaveState(new EconomySaveState { money = 123456, requestedSubsidy = 15500, lastMonthlyIncome = 15500, lastMaintenancePaid = 20, lastUnpaidMaintenance = 3 });
            m.satisfaction.ApplySaveState(new SatisfactionSaveState { missingPenaltyServices = { (int)ServiceType.School }, missingPenaltyValues = { 6f } });
            m.loop.RecalculateAll();
            m.outcome.ApplySaveState(new OutcomeSaveState { weeksElapsed = 58, goodStreakWeeks = 3, badStreakWeeks = 0 });

            m.dialogue.MarkPlayed("Start");
            m.dialogue.MarkPlayed("installed_Hospital");
            m.dialogue.UseEasyText = true;
            m.dialogue.SetAutoMode(true);
            m.cutscene.MarkPlayed("opening");

            m.triggers.ApplySaveState(new DialogueTriggerSaveState
            {
                storyStarted = true,
                knownUnlockedBuildingIds = { "house", "convenience_store", "hospital" },
                essentials = { new EssentialFacilitySaveState { service = (int)ServiceType.School, missingWeeks = 5, threshold = 9, firstInstallPlayed = false },
                               new EssentialFacilitySaveState { service = (int)ServiceType.Hospital, missingWeeks = 0, threshold = -1, firstInstallPlayed = true } }
            });
            m.tutorial.ApplySaveState(new TutorialSaveState { active = true, currentWeek = 4, triggeredStepIds = { "day1_welcome", "first_building", "day2_power", "day3_money", "day4_satisfaction" } });
        }

        private Managers SaveThenReopen()
        {
            Assert.IsTrue(SaveSystem.SaveGame(), SaveSystem.LastError);
            CleanupWorld();
            PlayerProfile.ClearPlayerName();
            _world = new TestWorld();
            var m = NewScene();
            var data = SaveSystem.LoadData();
            Assert.IsNotNull(data, SaveSystem.LastError);
            SaveSystem.ApplySaveData(data);
            return m;
        }

        [Test]
        public void 저장하고_새_씬에서_불러오면_모든_상태가_그대로다()
        {
            var before = NewScene();
            MakeProgress(before);
            float satisfactionBefore = before.satisfaction.CurrentAverageSatisfaction;
            int capacityBefore = before.population.MaxPopulationCapacity;

            var m = SaveThenReopen();

            // 이름·시간
            Assert.AreEqual("민수", PlayerProfile.PlayerName);
            Assert.AreEqual((2, 3, 3), (m.time.CurrentYear, m.time.CurrentMonth, m.time.CurrentWeek));
            Assert.AreEqual(58, m.time.TotalWeeksPassed);
            Assert.AreEqual(41.5f, TestWorld.GetField<float>(m.time, "_weekTimer"), 0.001f);
            Assert.AreEqual(GameSpeed.SuperFast, m.time.CurrentSpeed);

            // 건물 (위치·회전)
            var placed = m.loop.PlacedBuildings.OrderBy(b => b.OriginGridPos.x).ToList();
            Assert.AreEqual(4, placed.Count);
            Assert.AreEqual(("house", new Vector2Int(0, 0), 90f), (placed[0].Data.buildingID, placed[0].OriginGridPos, placed[0].RotationY));
            Assert.AreEqual(("convenience_store", new Vector2Int(3, 0), 180f), (placed[2].Data.buildingID, placed[2].OriginGridPos, placed[2].RotationY));
            Assert.AreEqual(("hospital", new Vector2Int(10, 10), 270f), (placed[3].Data.buildingID, placed[3].OriginGridPos, placed[3].RotationY));
            Assert.Less(Quaternion.Angle(Quaternion.Euler(0, 270f, 0) * _prefab.transform.rotation, placed[3].transform.rotation), 0.01f);
            var grid = TestWorld.GetField<BuildingInstance[,]>(m.buildings, "_occupiedGrid");
            Assert.AreEqual(placed[3], grid[11, 11], "2x2 병원이 점유 칸도 다시 차지");

            // 인구·전력·돈·만족도
            Assert.AreEqual(15, m.population.CurrentPopulation);
            Assert.AreEqual(capacityBefore, m.population.MaxPopulationCapacity);
            Assert.AreEqual(650, m.power.TargetPurchasedPower);
            Assert.AreEqual(400, m.power.CurrentAvailablePower);
            Assert.AreEqual(33, m.power.CurrentUsedPower, "사용량은 다시 계산: 건물(주택 5×2 + 병원 20) + 봄 인구 소비(15명 → 1.5 × 1.75 ≈ 3)");
            Assert.AreEqual(120, m.gas.TargetPurchasedGas);
            Assert.AreEqual(80, m.gas.CurrentAvailableGas);
            Assert.AreEqual(3, m.gas.CurrentUsedGas, "가스 사용량은 인구·계절로 다시 계산 (봄 = 중간)");
            Assert.AreEqual(123456, m.economy.CurrentMoney);
            Assert.AreEqual(15500, m.economy.GetRequestedSubsidy());
            Assert.AreEqual(3, m.economy.LastUnpaidMaintenance);
            Assert.AreEqual(6f, m.satisfaction.GetMissingFacilityPenalty(ServiceType.School));
            Assert.AreEqual(satisfactionBefore, m.satisfaction.CurrentAverageSatisfaction, 0.001f);

            // 엔딩 판정
            Assert.AreEqual(58, m.outcome.WeeksElapsed);
            Assert.AreEqual(3, m.outcome.GoodStreakWeeks);
            Assert.AreEqual(GameResult.None, m.outcome.Result);

            // 대화·컷씬·트리거·튜토리얼
            Assert.IsTrue(m.dialogue.HasPlayed("Start"));
            Assert.IsTrue(m.dialogue.HasPlayed("installed_Hospital"));
            Assert.IsTrue(m.dialogue.UseEasyText);
            Assert.IsTrue(m.dialogue.IsAutoMode);
            Assert.IsTrue(m.cutscene.HasPlayed("opening"));
            Assert.IsTrue(m.triggers.StoryStarted);
            Assert.AreEqual(5, m.triggers.GetMissingWeeks(ServiceType.School));
            Assert.AreEqual(9, m.triggers.GetComplaintThreshold(ServiceType.School));
            Assert.IsTrue(m.tutorial.IsActive);
            Assert.AreEqual(4, m.tutorial.CurrentWeek);
            Assert.IsTrue(m.tutorial.IsStepTriggered("day4_satisfaction"));
            Assert.IsFalse(m.tutorial.IsStepTriggered("day5_access"));
        }

        [Test]
        public void 불러온_직후_날짜_알림은_한_주로_세지_않고_다음_주부터_센다()
        {
            var before = NewScene();
            MakeProgress(before);
            var m = SaveThenReopen();

            // 불러오기 마지막의 날짜 알림(NotifyStateRestored)이 이미 지나감 → 아무것도 세지 않았어야 함
            Assert.AreEqual(58, m.outcome.WeeksElapsed);
            Assert.AreEqual(5, m.triggers.GetMissingWeeks(ServiceType.School));
            Assert.AreEqual(4, m.tutorial.CurrentWeek);

            // 다음 주
            TestWorld.Invoke(m.outcome, "HandleDateChanged", 2, 3, 4);
            TestWorld.Invoke(m.tutorial, "HandleDateChanged", 2, 3, 4);
            Assert.AreEqual(59, m.outcome.WeeksElapsed);
            Assert.AreEqual(5, m.tutorial.CurrentWeek);
        }

        [Test]
        public void 불러오기로_되살린_건물은_설치_대사나_비용이_없다()
        {
            var before = NewScene();
            MakeProgress(before);
            Assert.IsTrue(SaveSystem.SaveGame());
            CleanupWorld();
            _world = new TestWorld();
            var m = NewScene();
            int placedEvents = 0;
            m.buildings.OnBuildingPlaced += b => placedEvents++;
            m.economy.ApplySaveState(new EconomySaveState { money = 5 });

            SaveSystem.ApplySaveData(SaveSystem.LoadData());

            Assert.AreEqual(0, placedEvents, "설치 알림 없음 → 첫 설치 대사·튜토리얼이 다시 나오지 않음");
            Assert.AreEqual(123456, m.economy.CurrentMoney, "건설비를 내지 않음");
        }

        [Test]
        public void 건물_목록에_없는_건물은_건너뛴다()
        {
            var before = NewScene();
            MakeProgress(before);
            Assert.IsTrue(SaveSystem.SaveGame());
            var data = SaveSystem.LoadData();
            data.buildings.Add(new BuildingSaveState { buildingID = "removed_building", x = 30, y = 30 });
            CleanupWorld();
            _world = new TestWorld();
            var m = NewScene();

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("건물 1개를 되살리지 못했어요"));
            SaveSystem.ApplySaveData(data);
            Assert.AreEqual(4, m.loop.PlacedBuildings.Count);
        }

        [Test]
        public void 저장_요약을_볼_수_있다()
        {
            var before = NewScene();
            MakeProgress(before);
            Assert.IsFalse(SaveSystem.HasSave());
            Assert.IsNull(SaveSystem.GetSummary());

            Assert.IsTrue(SaveSystem.SaveGame());
            Assert.IsTrue(SaveSystem.HasSave());
            var s = SaveSystem.GetSummary();
            Assert.AreEqual("민수", s.playerName);
            Assert.AreEqual((2, 3, 3), (s.year, s.month, s.week));
            Assert.AreEqual(123456, s.money);
            Assert.AreEqual(15, s.population);
            Assert.AreEqual(4, s.buildingCount);
            Assert.IsNotEmpty(s.savedAt);
        }

        [Test]
        public void 두번_저장하면_덮어쓰고_지울_수_있다()
        {
            var m = NewScene();
            MakeProgress(m);
            Assert.IsTrue(SaveSystem.SaveGame());
            m.economy.AddMoney(1);
            Assert.IsTrue(SaveSystem.SaveGame());
            Assert.AreEqual(123457, SaveSystem.LoadData().economy.money);
            Assert.IsFalse(File.Exists(SaveSystem.GetSavePath() + ".tmp"));

            Assert.IsTrue(SaveSystem.DeleteSave(1));
            Assert.IsFalse(SaveSystem.HasSave());
        }

        [Test]
        public void 깨진_저장_파일은_읽지_않고_이유를_알려준다()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(SaveSystem.GetSavePath(), "{ 깨진 파일");
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("저장 파일을 읽지 못했어요"));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("저장 파일을 읽지 못했어요"));
            Assert.IsNull(SaveSystem.LoadData());
            Assert.IsNotEmpty(SaveSystem.LastError);
            Assert.IsFalse(SaveSystem.ContinueGame());
        }

        [Test]
        public void 일_단위_시절의_이전_버전_저장_파일은_불러오지_않는다()
        {
            var m = NewScene();
            MakeProgress(m);
            Assert.IsTrue(SaveSystem.SaveGame());
            var data = SaveSystem.LoadData();
            data.version = 1;
            File.WriteAllText(SaveSystem.GetSavePath(), JsonUtility.ToJson(data));

            Assert.IsNull(SaveSystem.LoadData());
            StringAssert.Contains("이전 버전", SaveSystem.LastError);
            Assert.IsFalse(SaveSystem.ContinueGame());
        }

        [Test]
        public void 빌드_설정에_없는_씬은_이어하기를_하지_않는다()
        {
            var m = NewScene();
            MakeProgress(m);
            Assert.IsTrue(SaveSystem.SaveGame());
            var data = SaveSystem.LoadData();
            data.sceneName = "없는씬_테스트";
            File.WriteAllText(SaveSystem.GetSavePath(), JsonUtility.ToJson(data));

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Build Settings"));
            Assert.IsFalse(SaveSystem.ContinueGame());
            StringAssert.Contains("Build Settings", SaveSystem.LastError);
            Assert.IsFalse(SaveSystem.HasPendingLoad);
        }

        [Test]
        public void 불러오기를_기다리는_동안에는_오프닝이_시작되지_않는다()
        {
            var m = NewScene();
            int started = 0;
            m.dialogue.OnDialogueStarted += d => started++;
            TestWorld.SetField(m.triggers, "introDialogue", DialogueData.Create("intro", true, true, new DialogueLine("주민", "어서 와요")));

            SaveSystem.PrepareLoad(new SaveData { sceneName = "x" });
            TestWorld.Invoke(m.triggers, "Start");
            TestWorld.Invoke(m.triggers, "Update");
            Assert.IsFalse(m.triggers.StoryStarted);
            Assert.AreEqual(0, started);

            SaveSystem.ClearPendingLoad(); // '새 게임'이면 그대로 시작
            TestWorld.Invoke(m.triggers, "Update");
            Assert.IsTrue(m.triggers.StoryStarted);
        }

        [Test]
        public void 이야기_시작_전에_저장했으면_불러온_뒤_처음처럼_오프닝이_나온다()
        {
            var m = NewScene();
            SaveSystem.ApplySaveData(new SaveData { sceneName = "x" }); // storyStarted = false
            Assert.IsFalse(m.triggers.StoryStarted);
            TestWorld.Invoke(m.triggers, "Start");
            TestWorld.Invoke(m.triggers, "Update");
            Assert.IsTrue(m.triggers.StoryStarted);
        }

        [Test]
        public void 끝난_판을_불러오면_엔딩_상태로_멈춰_있다()
        {
            var m = NewScene();
            MakeProgress(m);
            m.outcome.ApplySaveState(new OutcomeSaveState { weeksElapsed = 480, result = (int)GameResult.Victory });
            var reopened = SaveThenReopen();
            Assert.AreEqual(GameResult.Victory, reopened.outcome.Result);
            Assert.IsTrue(reopened.time.IsEventPaused);
        }

        // ── 슬롯 5개 (사용자 기획 2026-11-24) ──

        [Test]
        public void 슬롯은_1번부터_5번까지_5개다()
        {
            Assert.AreEqual(5, SaveSystem.SlotCount);
            Assert.IsFalse(SaveSystem.IsValidSlot(0));
            Assert.IsTrue(SaveSystem.IsValidSlot(1));
            Assert.IsTrue(SaveSystem.IsValidSlot(5));
            Assert.IsFalse(SaveSystem.IsValidSlot(6));
            Assert.AreEqual(1, SaveSystem.CurrentSlot, "처음 현재 슬롯은 1번");
        }

        [Test]
        public void 고른_슬롯에_따로따로_저장되고_고른_슬롯이_불러와진다()
        {
            var m = NewScene();
            MakeProgress(m);
            for (int slot = 1; slot <= 5; slot++)
            {
                m.economy.ApplySaveState(new EconomySaveState { money = slot * 1000 });
                Assert.IsTrue(SaveSystem.SaveGame(slot), SaveSystem.LastError);
            }
            for (int slot = 1; slot <= 5; slot++)
            {
                Assert.IsTrue(File.Exists(Path.Combine(_folder, $"save_{slot}.json")));
                Assert.AreEqual(slot * 1000, SaveSystem.LoadData(slot).economy.money, $"{slot}번 슬롯");
            }

            // 3번 슬롯을 골라 불러오면 3번 슬롯 내용
            CleanupWorld();
            _world = new TestWorld();
            var reopened = NewScene();
            SaveSystem.ApplySaveData(SaveSystem.LoadData(3));
            Assert.AreEqual(3000, reopened.economy.CurrentMoney);
        }

        [Test]
        public void 저장하거나_불러온_슬롯이_현재_슬롯이_되고_생략하면_현재_슬롯을_쓴다()
        {
            var m = NewScene();
            MakeProgress(m);
            int changed = -1;
            System.Action<int> onChanged = s => changed = s;
            SaveSystem.OnCurrentSlotChanged += onChanged;
            try
            {
                Assert.IsTrue(SaveSystem.SaveGame(4));
                Assert.AreEqual(4, SaveSystem.CurrentSlot);
                Assert.AreEqual(4, changed);

                m.economy.ApplySaveState(new EconomySaveState { money = 777 });
                Assert.IsTrue(SaveSystem.SaveGame(), "슬롯 생략 = 현재 슬롯(4번)");
                Assert.AreEqual(777, SaveSystem.LoadData(4).economy.money);
                Assert.IsFalse(SaveSystem.HasSave(1), "1번에는 저장하지 않음");
                Assert.AreEqual(4, SaveSystem.GetSummary().slot);
            }
            finally { SaveSystem.OnCurrentSlotChanged -= onChanged; }
        }

        [Test]
        public void 범위_밖_슬롯은_저장_불러오기_지우기를_하지_않는다()
        {
            var m = NewScene();
            MakeProgress(m);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("슬롯은 1~5번"));
            Assert.IsFalse(SaveSystem.SaveGame(6));
            StringAssert.Contains("1~5번", SaveSystem.LastError);
            Assert.IsNull(SaveSystem.LoadData(-1));
            Assert.IsFalse(SaveSystem.DeleteSave(0), "지우기는 슬롯 번호를 꼭 줘야 함");
            Assert.IsFalse(SaveSystem.ContinueGame(9));
            Assert.IsFalse(SaveSystem.SetCurrentSlot(6));
            Assert.AreEqual(1, SaveSystem.CurrentSlot);
            Assert.IsFalse(SaveSystem.HasAnySave());
        }

        [Test]
        public void 슬롯_목록은_빈_슬롯_깨진_파일_요약을_5칸으로_보여준다()
        {
            var m = NewScene();
            MakeProgress(m);
            Assert.IsTrue(SaveSystem.SaveGame(2));
            Assert.IsTrue(SaveSystem.SaveGame(5));
            File.WriteAllText(SaveSystem.GetSavePath(4), "{ 깨진 파일");
            File.SetLastWriteTimeUtc(SaveSystem.GetSavePath(2), System.DateTime.UtcNow.AddMinutes(-10));
            File.SetLastWriteTimeUtc(SaveSystem.GetSavePath(4), System.DateTime.UtcNow.AddMinutes(-20));
            File.SetLastWriteTimeUtc(SaveSystem.GetSavePath(5), System.DateTime.UtcNow.AddMinutes(-5));

            var slots = SaveSystem.GetAllSlots(); // 깨진 파일이 있어도 콘솔 에러 없음
            Assert.AreEqual(5, slots.Length);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, slots.Select(s => s.slot));
            Assert.IsTrue(slots[0].isEmpty && slots[2].isEmpty);
            Assert.IsTrue(slots[3].isBroken && !slots[3].isEmpty && slots[3].summary == null);
            Assert.IsNotEmpty(slots[3].error);
            Assert.AreEqual("민수", slots[1].summary.playerName);
            Assert.AreEqual((2, 2), (slots[1].slot, slots[1].summary.slot));
            Assert.AreEqual((2, 3, 3), (slots[4].summary.year, slots[4].summary.month, slots[4].summary.week));
            Assert.IsTrue(slots[4].isCurrent, "마지막으로 저장한 5번이 현재 슬롯");
            Assert.IsTrue(slots[4].isLatest && !slots[1].isLatest, "가장 최근에 저장한 슬롯");
            Assert.AreEqual(5, SaveSystem.GetLatestSlot());
            Assert.IsTrue(SaveSystem.HasAnySave());
        }

        [Test]
        public void 슬롯을_지우면_그_슬롯만_비고_저장이_없으면_이어하기를_하지_않는다()
        {
            var m = NewScene();
            MakeProgress(m);
            Assert.IsTrue(SaveSystem.SaveGame(1));
            Assert.IsTrue(SaveSystem.SaveGame(3));
            Assert.IsTrue(SaveSystem.DeleteSave(3));
            Assert.IsFalse(SaveSystem.HasSave(3));
            Assert.IsTrue(SaveSystem.HasSave(1));
            Assert.IsFalse(SaveSystem.DeleteSave(3), "이미 빈 슬롯");
            Assert.AreEqual(1, SaveSystem.GetLatestSlot());

            Assert.IsTrue(SaveSystem.DeleteSave(1));
            Assert.AreEqual(0, SaveSystem.GetLatestSlot());
            Assert.IsFalse(SaveSystem.ContinueLatest());
            Assert.IsNotEmpty(SaveSystem.LastError);
        }
    }
}

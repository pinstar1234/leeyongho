using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.Population;
using InfraTown.Satisfaction;
using InfraTown.Save;
using InfraTown.Services;
using InfraTown.TimeSystem;

namespace InfraTown.Tests
{
    /// <summary>
    /// 엔딩 뒤 흐름 (사용자 기획 2026-11-24):
    /// 배드엔딩 = 게임 오버 → 건설·철거 불가, 시간 정지 (타이틀로 돌아가는 화면은 UI).
    /// 해피엔딩 = 게임 오버 아님 → 끝내기(타이틀) / 계속하기(배드엔딩 조건 없이 원하는 만큼) 선택
    /// </summary>
    public class EndingFlowTests
    {
        private TestWorld _world;
        private TimeManager _time;
        private SatisfactionManager _satisfaction;
        private ServiceAccessibilityManager _service;
        private GameOutcomeManager _outcome;
        private BuildingManager _manager;
        private GameObject _prefab;
        private List<BuildingInstance> _buildings;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _time = _world.CreateManager<TimeManager>();
            _satisfaction = _world.CreateManager<SatisfactionManager>();
            _service = _world.CreateManager<ServiceAccessibilityManager>();
            _outcome = _world.CreateManager<GameOutcomeManager>();
            _outcome.OnGameEnded += FakeEndingUI; // 엔딩 화면 UI가 있는 상황 (없을 때 자동 계속하기는 따로 테스트, 2026-11-26)
            _manager = _world.CreateManager<BuildingManager>(callStart: true);
            _manager.RequireRoadAccess = false; // 엔딩 흐름만 확인
            _prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _buildings = new List<BuildingInstance>();
        }

        [TearDown]
        public void TearDown()
        {
            _manager.CancelBuildingMode();
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            Object.DestroyImmediate(_prefab);
            _world.Dispose();
        }

        private static void FakeEndingUI(GameResult result) { }

        private void Recalculate()
        {
            _service.RecalculateServiceMetrics(_buildings);
            _satisfaction.RecalculateSatisfaction(_buildings);
        }

        private void Weeks(int count)
        {
            for (int i = 0; i < count; i++) _outcome.ProcessWeek();
        }

        private void MakeBadTown()
        {
            _buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 10), 0, 0));
            _buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Nimbys, impact: -50f), 1, 0));
            Recalculate();
        }

        private void MakeGoodTown()
        {
            _buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 10), 5, 5));
            _buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Essential, id: "hospital"), 6, 5));
            _buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Essential, id: "school"), 4, 5));
            _buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Essential, id: "fire"), 5, 6));
            _buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Essential, id: "police"), 5, 4));
            Recalculate();
        }

        private void ReachDefeat()
        {
            MakeBadTown();
            Weeks(_outcome.DefeatBadStreakWeeks);
            Assert.AreEqual(GameResult.Defeat, _outcome.Result);
        }

        private void ReachVictory()
        {
            MakeGoodTown();
            Weeks(_outcome.VictoryWeeks);
            Assert.AreEqual(GameResult.Victory, _outcome.Result);
        }

        private BuildingData PlaceableData(string id = "house")
        {
            var data = _world.CreateData(BuildingType.Housing, id: id, name: id);
            data.buildingPrefab = _prefab;
            return data;
        }

        /// <summary>실제 설치 경로로 설치. 성공하면 그 건물, 실패하면 null</summary>
        private BuildingInstance Place(BuildingData data, int x, int y)
        {
            var cell = new Vector2Int(x, y);
            _manager.SelectBuildingToBuild(data);
            TestWorld.Invoke(_manager, "TryPlaceBuilding", cell);
            _manager.CancelBuildingMode();
            var placed = _manager.GetBuildingAt(cell);
            if (placed == null || placed.Data != data) return null;
            _spawned.Add(placed.gameObject);
            return placed;
        }

        // ─── 배드엔딩 = 게임 오버 ───

        [Test]
        public void 배드엔딩은_게임오버이고_시간이_멈춘다()
        {
            ReachDefeat();
            Assert.IsTrue(_outcome.IsGameOver);
            Assert.IsTrue(_outcome.HasEnded);
            Assert.IsFalse(_outcome.CanModifyTown);
            Assert.IsFalse(_outcome.IsVictoryChoicePending);
            Assert.IsTrue(_time.IsEventPaused);
        }

        [Test]
        public void 배드엔딩_뒤에는_건설할_수_없고_이유를_알려준다()
        {
            var data = PlaceableData();
            ReachDefeat();

            string rejected = null;
            _manager.OnPlacementRejected += (d, cell, reason) => rejected = reason;

            Assert.IsNull(Place(data, 20, 20));
            Assert.AreEqual(BuildingManager.GameOverPlacementError, _manager.LastPlacementError);
            Assert.AreEqual(BuildingManager.GameOverPlacementError, rejected);
            Assert.IsFalse(_manager.CanPlaceAt(data, new Vector2Int(20, 20), out var reason));
            Assert.AreEqual(BuildingManager.GameOverPlacementError, reason);
        }

        [Test]
        public void 배드엔딩_뒤에는_건설_모드에_들어갈_수_없다()
        {
            var data = PlaceableData();
            ReachDefeat();
            _manager.SelectBuildingToBuild(data);
            Assert.IsFalse(_manager.IsPlacingBuilding);
        }

        [Test]
        public void 건설_모드_중에_배드엔딩이_나면_건설_모드가_꺼진다()
        {
            var data = PlaceableData();
            MakeBadTown();
            _manager.SelectBuildingToBuild(data);
            Assert.IsTrue(_manager.IsPlacingBuilding);

            Weeks(_outcome.DefeatBadStreakWeeks);
            Assert.IsTrue(_outcome.IsGameOver);
            Assert.IsFalse(_manager.IsPlacingBuilding);
        }

        [Test]
        public void 배드엔딩_뒤에는_철거해도_건물이_남는다()
        {
            var house = Place(PlaceableData(), 20, 20);
            Assert.IsNotNull(house);
            ReachDefeat();

            int notified = 0;
            System.Action<BuildingInstance> onDestroyed = b => notified++;
            BuildingInstance.OnBuildingDestroyed += onDestroyed;
            try
            {
                ExpectBlockedWarning(() => house.DestroyBuilding());
            }
            finally
            {
                BuildingInstance.OnBuildingDestroyed -= onDestroyed;
            }

            Assert.AreEqual(0, notified, "철거 알림이 나가면 안 된다");
            Assert.IsTrue(house != null, "건물이 그대로 남아야 한다");
            Assert.AreEqual(house, _manager.GetBuildingAt(new Vector2Int(20, 20)), "점유 칸도 그대로");
        }

        private static void ExpectBlockedWarning(System.Action action)
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("철거할 수 없습니다"));
            action();
        }

        [Test]
        public void 게임오버_판정이_없는_씬에서는_건설할_수_있다()
        {
            Object.DestroyImmediate(_outcome);
            Assert.IsTrue(GameOutcomeManager.CanModifyTownNow);
            Assert.IsNotNull(Place(PlaceableData(), 20, 20));
        }

        // ─── 해피엔딩 = 끝내기/계속하기 선택 ───

        [Test]
        public void 해피엔딩은_게임오버가_아니고_선택을_기다리며_시간이_멈춘다()
        {
            GameResult ended = GameResult.None;
            _outcome.OnGameEnded += r => ended = r;
            ReachVictory();

            Assert.AreEqual(GameResult.Victory, ended);
            Assert.IsFalse(_outcome.IsGameOver);
            Assert.IsTrue(_outcome.HasEnded);
            Assert.IsTrue(_outcome.IsVictoryChoicePending);
            Assert.IsFalse(_outcome.IsFreePlay);
            Assert.IsTrue(_outcome.CanModifyTown);
            Assert.IsTrue(_time.IsEventPaused, "선택하기 전에는 시간이 멈춘다");
        }

        [Test]
        public void 해피엔딩_뒤에도_건설할_수_있다()
        {
            ReachVictory();
            Assert.IsNotNull(Place(PlaceableData(), 20, 20));
        }

        [Test]
        public void 계속하기를_고르면_시간이_다시_흐르고_이벤트가_나간다()
        {
            ReachVictory();
            bool started = false;
            _outcome.OnFreePlayStarted += () => started = true;

            Assert.IsTrue(_outcome.ContinueAfterVictory());
            Assert.IsTrue(started);
            Assert.IsTrue(_outcome.IsFreePlay);
            Assert.IsFalse(_outcome.IsVictoryChoicePending);
            Assert.IsFalse(_time.IsEventPaused);
            Assert.AreEqual(GameResult.Victory, _outcome.Result);
        }

        [Test]
        public void 계속하기_뒤에는_나쁨이_계속돼도_배드엔딩이_나지_않는다()
        {
            ReachVictory();
            _outcome.ContinueAfterVictory();
            int weeksBefore = _outcome.WeeksElapsed;

            // 마을을 '나쁨'으로 만들어 배드엔딩 기준의 몇 배를 보냄
            _buildings.Clear();
            MakeBadTown();
            Assert.AreEqual(SatisfactionState.Bad, _satisfaction.CurrentState);
            int raised = 0;
            _outcome.OnBadStreakChanged += (w, limit) => raised++;
            Weeks(_outcome.DefeatBadStreakWeeks * 5);

            Assert.AreEqual(GameResult.Victory, _outcome.Result);
            Assert.IsFalse(_outcome.IsGameOver);
            Assert.AreEqual(0, _outcome.BadStreakWeeks);
            Assert.AreEqual(0, raised, "나쁨 경고(OnBadStreakChanged)도 나가지 않는다");
            Assert.IsFalse(_time.IsEventPaused);
            Assert.AreEqual(weeksBefore + _outcome.DefeatBadStreakWeeks * 5, _outcome.WeeksElapsed, "경과 주 수는 계속 센다");
        }

        [Test]
        public void 해피엔딩_선택_전에는_주를_세지_않는다()
        {
            ReachVictory();
            int weeks = _outcome.WeeksElapsed;
            Weeks(10);
            Assert.AreEqual(weeks, _outcome.WeeksElapsed);
        }

        [Test]
        public void 해피엔딩이_아니면_계속하기는_아무것도_하지_않는다()
        {
            Assert.IsFalse(_outcome.ContinueAfterVictory(), "진행 중");
            Assert.IsFalse(_outcome.IsFreePlay);

            ReachDefeat();
            Assert.IsFalse(_outcome.ContinueAfterVictory(), "배드엔딩");
            Assert.IsFalse(_outcome.IsFreePlay);
            Assert.IsTrue(_time.IsEventPaused);
        }

        [Test]
        public void 계속하기를_두_번_눌러도_한_번만_처리된다()
        {
            ReachVictory();
            int started = 0;
            _outcome.OnFreePlayStarted += () => started++;
            Assert.IsTrue(_outcome.ContinueAfterVictory());
            Assert.IsFalse(_outcome.ContinueAfterVictory());
            Assert.AreEqual(1, started);
        }

        [Test]
        public void 타이틀_씬이_빌드_설정에_없으면_돌아가지_않고_false()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Build Settings"));
            Assert.IsFalse(_outcome.ReturnToTitle("__없는_씬__"));
        }

        // ─── 세이브 ───

        [Test]
        public void 계속하기_상태가_저장되고_불러오면_시간이_흐른다()
        {
            ReachVictory();
            _outcome.ContinueAfterVictory();
            var state = _outcome.GetSaveState();
            Assert.IsTrue(state.freePlay);

            var json = JsonUtility.ToJson(state);
            var restored = JsonUtility.FromJson<OutcomeSaveState>(json);

            var world = new TestWorld();
            try
            {
                var time = world.CreateManager<TimeManager>();
                var outcome = world.CreateManager<GameOutcomeManager>();
                outcome.ApplySaveState(restored);
                Assert.IsTrue(outcome.IsFreePlay);
                Assert.IsFalse(outcome.IsVictoryChoicePending);
                Assert.IsFalse(time.IsEventPaused);
            }
            finally
            {
                world.Dispose();
            }
        }

        // ─── 엔딩 화면 UI가 없을 때 (2026-11-26: 선택할 방법이 없어 시간이 영원히 멈추던 문제) ───

        [Test]
        public void 엔딩_UI가_없으면_해피엔딩_뒤_자동으로_계속하기()
        {
            _outcome.OnGameEnded -= FakeEndingUI;
            bool freePlay = false;
            _outcome.OnFreePlayStarted += () => freePlay = true;
            Assert.IsFalse(_outcome.HasEndingUI);

            ReachVictory();

            Assert.AreEqual(GameResult.Victory, _outcome.Result);
            Assert.IsTrue(_outcome.IsFreePlay, "자동 계속하기");
            Assert.IsFalse(_outcome.IsVictoryChoicePending);
            Assert.IsFalse(_time.IsEventPaused, "시간이 다시 흐름");
            Assert.IsTrue(freePlay);
        }

        [Test]
        public void 대화_트리거만_구독해도_엔딩_UI로_치지_않는다()
        {
            _outcome.OnGameEnded -= FakeEndingUI;
            var go = new GameObject("triggers");
            try
            {
                var triggers = go.AddComponent<InfraTown.EventSystem.DialogueTriggers>();
                var method = typeof(InfraTown.EventSystem.DialogueTriggers).GetMethod("HandleGameEnded",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                Assert.IsNotNull(method, "DialogueTriggers.HandleGameEnded");
                _outcome.OnGameEnded += (System.Action<GameResult>)System.Delegate.CreateDelegate(typeof(System.Action<GameResult>), triggers, method);
                Assert.IsFalse(_outcome.HasEndingUI, "DialogueTriggers의 엔딩 대화 구독은 UI가 아님");

                _outcome.OnGameEnded += FakeEndingUI;
                Assert.IsTrue(_outcome.HasEndingUI, "다른 구독자가 있으면 UI");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void 엔딩_UI가_없으면_선택_대기로_저장된_게임도_계속하기로_열린다()
        {
            ReachVictory();
            var state = _outcome.GetSaveState();

            var world = new TestWorld();
            try
            {
                var time = world.CreateManager<TimeManager>();
                var outcome = world.CreateManager<GameOutcomeManager>();
                outcome.ApplySaveState(state);
                Assert.IsTrue(outcome.IsFreePlay);
                Assert.IsFalse(time.IsEventPaused);
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void 해피엔딩_선택_전에_저장하면_불러와도_선택을_기다린다()
        {
            ReachVictory();
            var state = _outcome.GetSaveState();
            Assert.IsFalse(state.freePlay);

            var world = new TestWorld();
            try
            {
                var time = world.CreateManager<TimeManager>();
                var outcome = world.CreateManager<GameOutcomeManager>();
                outcome.OnGameEnded += FakeEndingUI;
                outcome.ApplySaveState(state);
                Assert.IsTrue(outcome.IsVictoryChoicePending);
                Assert.IsTrue(time.IsEventPaused);
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void 배드엔딩_상태를_불러오면_건설할_수_없다()
        {
            var data = PlaceableData();
            _outcome.ApplySaveState(new OutcomeSaveState { weeksElapsed = 30, badStreakWeeks = 12, result = (int)GameResult.Defeat });
            Assert.IsTrue(_outcome.IsGameOver);
            Assert.IsTrue(_time.IsEventPaused);
            Assert.IsNull(Place(data, 20, 20));
        }

        [Test]
        public void 패배_상태에_계속하기_표시가_있어도_무시한다()
        {
            _outcome.ApplySaveState(new OutcomeSaveState { result = (int)GameResult.Defeat, freePlay = true });
            Assert.IsTrue(_outcome.IsGameOver);
            Assert.IsFalse(_outcome.IsFreePlay);
        }

        [Test]
        public void 알_수_없는_결과값은_진행_중으로_불러온다()
        {
            _outcome.ApplySaveState(new OutcomeSaveState { result = 99 });
            Assert.AreEqual(GameResult.None, _outcome.Result);
            Assert.IsFalse(_time.IsEventPaused);
        }
    }

    /// <summary>
    /// 신청 지원금 회복 (사용자 기획 2026-11-24): 인구가 줄면 최대치와 함께 신청액도 줄지만(의도),
    /// 인구가 다시 늘면 플레이어가 정한 금액까지 되돌아오고, 최대치로 정해 뒀으면 계속 최대치를 따라간다
    /// </summary>
    public class SubsidyRecoveryTests
    {
        private TestWorld _world;
        private EconomyManager _economy;
        private PopulationManager _population;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _economy = _world.CreateManager<EconomyManager>();
            _population = _world.CreateManager<PopulationManager>();
            TestWorld.SetField(_population, "maxPopulationCapacity", 1000);
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        /// <summary>인구를 정확히 그 수로 (수용 인구 1000 안에서)</summary>
        private void SetPopulation(int count) => _population.AddPopulation(count - _population.CurrentPopulation);

        [Test]
        public void 인구가_줄면_신청액이_최대치로_줄고_다시_늘면_정한_금액까지_돌아온다()
        {
            SetPopulation(300); // 최대 30,000
            _economy.SetGovernmentSubsidyAmount(25000);

            SetPopulation(150); // 최대 22,500
            Assert.AreEqual(22500, _economy.GetRequestedSubsidy(), "인구가 줄면 최대치로 깎임 (의도)");
            Assert.AreEqual(25000, _economy.DesiredSubsidyAmount, "정한 금액은 기억");

            SetPopulation(300);
            Assert.AreEqual(25000, _economy.GetRequestedSubsidy(), "정한 금액까지 회복");

            SetPopulation(500); // 최대 40,000
            Assert.AreEqual(25000, _economy.GetRequestedSubsidy(), "최대치로 정하지 않았으면 정한 금액 이상 오르지 않음");
            Assert.IsFalse(_economy.IsSubsidyFollowingMax);
        }

        [Test]
        public void 최대치로_정했으면_인구가_늘어_최대치가_커질_때_계속_따라간다()
        {
            SetPopulation(300);
            _economy.SetGovernmentSubsidyAmount(_economy.GetMaxEligibleSubsidy()); // 30,000
            Assert.IsTrue(_economy.IsSubsidyFollowingMax);

            SetPopulation(290);
            Assert.AreEqual(29500, _economy.GetRequestedSubsidy());

            SetPopulation(300);
            Assert.AreEqual(30000, _economy.GetRequestedSubsidy());

            SetPopulation(400);
            Assert.AreEqual(35000, _economy.GetRequestedSubsidy(), "최대치를 따라감");
        }

        [Test]
        public void 최대치보다_큰_값을_넣어도_최대치로_정한_것으로_본다()
        {
            SetPopulation(100);
            _economy.SetGovernmentSubsidyAmount(999999);
            Assert.IsTrue(_economy.IsSubsidyFollowingMax);
            SetPopulation(200);
            Assert.AreEqual(25000, _economy.GetRequestedSubsidy());
        }

        [Test]
        public void 최대치에서_다시_낮추면_더_이상_따라가지_않는다()
        {
            SetPopulation(300);
            _economy.SetGovernmentSubsidyAmount(30000);
            _economy.SetGovernmentSubsidyAmount(20000);
            Assert.IsFalse(_economy.IsSubsidyFollowingMax);
            SetPopulation(500);
            Assert.AreEqual(20000, _economy.GetRequestedSubsidy());
        }

        [Test]
        public void 기본_신청액은_자동으로_오르지_않는다()
        {
            // 기본 15,000원 (CLAUDE.md: 신청 지원금은 슬라이더로만 바뀐다)
            SetPopulation(300);
            Assert.AreEqual(15000, _economy.GetRequestedSubsidy());
            Assert.IsFalse(_economy.IsSubsidyFollowingMax);
        }

        [Test]
        public void 알림을_받은_UI가_슬라이더_최대치를_줄이며_되부른_값은_정한_금액을_덮지_않는다()
        {
            // OverviewUI처럼 maxValue를 줄이면 Slider가 줄어든 값으로 onValueChanged → SetGovernmentSubsidyAmount를 되부르는 상황
            SetPopulation(300);
            _economy.SetGovernmentSubsidyAmount(25000);
            _economy.OnSubsidyAmountChanged += (requested, max) => _economy.SetGovernmentSubsidyAmount(requested);

            SetPopulation(150);
            Assert.AreEqual(25000, _economy.DesiredSubsidyAmount);
            Assert.IsFalse(_economy.IsSubsidyFollowingMax, "되부름 때문에 최대치를 따라가게 되면 안 된다");

            SetPopulation(300);
            Assert.AreEqual(25000, _economy.GetRequestedSubsidy());
        }

        [Test]
        public void 정산은_회복된_신청액으로_받는다()
        {
            SetPopulation(300);
            _economy.SetGovernmentSubsidyAmount(30000);
            SetPopulation(200);
            SetPopulation(300);

            int before = _economy.CurrentMoney;
            _economy.ProcessMonthlySettlement(300, 0);
            Assert.AreEqual(before + 30000, _economy.CurrentMoney);
        }

        [Test]
        public void 정한_금액과_최대치_따라가기가_저장되고_불러와진다()
        {
            SetPopulation(300);
            _economy.SetGovernmentSubsidyAmount(30000);
            SetPopulation(200);
            var state = _economy.GetSaveState();
            Assert.AreEqual(25000, state.requestedSubsidy);
            Assert.AreEqual(30000, state.desiredSubsidy);
            Assert.IsTrue(state.followMaxSubsidy);

            var restored = JsonUtility.FromJson<EconomySaveState>(JsonUtility.ToJson(state));
            var world = new TestWorld();
            try
            {
                var economy = world.CreateManager<EconomyManager>();
                var population = world.CreateManager<PopulationManager>();
                TestWorld.SetField(population, "maxPopulationCapacity", 1000);
                population.AddPopulation(200);
                economy.ApplySaveState(restored);
                Assert.AreEqual(25000, economy.GetRequestedSubsidy());
                population.AddPopulation(200); // 400명
                Assert.AreEqual(35000, economy.GetRequestedSubsidy());
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void 정한_금액이_없던_예전_저장_파일은_신청액을_정한_금액으로()
        {
            SetPopulation(300);
            _economy.ApplySaveState(new EconomySaveState { money = 0, requestedSubsidy = 20000 });
            Assert.AreEqual(20000, _economy.DesiredSubsidyAmount);
            Assert.IsFalse(_economy.IsSubsidyFollowingMax);
            Assert.AreEqual(20000, _economy.GetRequestedSubsidy());
        }
    }
}

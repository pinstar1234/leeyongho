using System.Collections.Generic;
using NUnit.Framework;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Population;
using InfraTown.Power;
using InfraTown.Satisfaction;
using InfraTown.Services;
using InfraTown.TimeSystem;

namespace InfraTown.Tests
{
    /// <summary>
    /// 기획서 6장 핵심 루프: 접근 시간 단축 보너스 → 주택별 좋음/나쁨 → 승리·패배
    /// </summary>
    public class AccessBonusTests
    {
        private TestWorld _world;
        private ServiceAccessibilityManager _service;
        private SatisfactionManager _satisfaction;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _service = _world.CreateManager<ServiceAccessibilityManager>();
            _satisfaction = _world.CreateManager<SatisfactionManager>();
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private BuildingInstance House(int x, int y) =>
            _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 10), x, y);

        private BuildingInstance Service(string id, int x, int y, float baseTime = 5f)
        {
            var data = _world.CreateData(BuildingType.Essential, id: id, name: id);
            data.fixedAccessTimeMinutes = baseTime;
            return _world.CreateBuilding(data, x, y);
        }

        private void Recalculate(List<BuildingInstance> buildings)
        {
            _service.RecalculateServiceMetrics(buildings);
            _satisfaction.RecalculateSatisfaction(buildings);
        }

        [Test]
        public void 읍내_60분_기준_대비_단축비율만큼_보너스()
        {
            // 맞닿은 병원 → 5분, 단축 비율 55/60 → 보너스 (10 ÷ 5종 = 2) × 0.9167 = 1.833
            var house = House(0, 0);
            Recalculate(new List<BuildingInstance> { house, Service("hospital", 1, 0) });

            Assert.AreEqual(2f * 55f / 60f, _service.GetAccessBonus(house, ServiceType.Hospital), 0.001f);
            Assert.AreEqual(70f + 2f * 55f / 60f, house.GetComponent<HousingInstance>().CurrentSatisfaction, 0.001f);
            Assert.AreEqual(2f * 55f / 60f, house.GetComponent<HousingInstance>().AccessBonus, 0.001f);
        }

        [Test]
        public void 여러_서비스의_보너스는_합산된다()
        {
            var house = House(0, 0);
            Recalculate(new List<BuildingInstance> { house, Service("hospital", 1, 0), Service("school", 0, 1) });
            Assert.AreEqual(2f * 2f * 55f / 60f, _service.GetTotalAccessBonus(house), 0.001f);
        }

        [Test]
        public void 접근_보너스_합계는_최대_10점()
        {
            Assert.AreEqual(10f, _service.MaxTotalAccessBonus);
            Assert.AreEqual(2f, _service.MaxBonusPerService, 0.001f);

            // 5종 시설이 모두 맞닿아 있어도 (기본 시간 0분이면) 합계 10을 넘지 않는다
            var house = House(5, 5);
            Recalculate(new List<BuildingInstance>
            {
                house,
                Service("hospital", 6, 5, baseTime: 0f), Service("school", 4, 5, baseTime: 0f),
                Service("fire", 5, 6, baseTime: 0f), Service("police", 5, 4, baseTime: 0f),
                Service("convenience_store", 6, 6, baseTime: 0f)
            });
            Assert.AreEqual(10f, _service.GetTotalAccessBonus(house), 0.001f);
        }

        [Test]
        public void 기준시간보다_먼_시설은_보너스가_없다()
        {
            // 빈 칸 58+38=96 → 20 + 48 = 68분 > 60분
            var house = House(0, 0);
            Recalculate(new List<BuildingInstance> { house, Service("hospital", 59, 39, baseTime: 20f) });
            Assert.AreEqual(0f, _service.GetAccessBonus(house, ServiceType.Hospital));
        }

        [Test]
        public void 시설이_없으면_보너스가_없다()
        {
            var house = House(0, 0);
            Recalculate(new List<BuildingInstance> { house });
            Assert.AreEqual(0f, _service.GetTotalAccessBonus(house));
            Assert.AreEqual(70f, _satisfaction.CurrentAverageSatisfaction);
        }

        [Test]
        public void 가까울수록_보너스가_크다()
        {
            var near = House(0, 0);
            var far = House(0, 30);
            Recalculate(new List<BuildingInstance> { near, far, Service("hospital", 1, 0) });
            Assert.Greater(_service.GetAccessBonus(near, ServiceType.Hospital), _service.GetAccessBonus(far, ServiceType.Hospital));
        }
    }

    public class SatisfactionStateTests
    {
        private TestWorld _world;
        private SatisfactionManager _satisfaction;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _satisfaction = _world.CreateManager<SatisfactionManager>();
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private BuildingInstance House(int x, int y) =>
            _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 10), x, y);

        private BuildingInstance Affector(float impact, int x, int y) =>
            _world.CreateBuilding(_world.CreateData(BuildingType.Nimbys, impact: impact, radius: 1), x, y);

        [TestCase(1f, SatisfactionState.Good)]    // 71
        [TestCase(0f, SatisfactionState.Normal)]  // 70
        [TestCase(-39f, SatisfactionState.Normal)] // 31
        [TestCase(-40f, SatisfactionState.Bad)]   // 30
        public void 좋음은_71이상_나쁨은_30이하(float impact, SatisfactionState expected)
        {
            var house = House(0, 0);
            _satisfaction.RecalculateSatisfaction(new List<BuildingInstance> { house, Affector(impact, 1, 0) });
            Assert.AreEqual(expected, house.GetComponent<HousingInstance>().State);
        }

        [Test]
        public void 좋음_주택_비율을_계산한다()
        {
            // 주택 2채는 +10 범위 안(좋음), 1채는 멀리(보통)
            var buildings = new List<BuildingInstance> { House(0, 0), House(2, 0), House(30, 30), Affector(10f, 1, 0) };
            _satisfaction.RecalculateSatisfaction(buildings);

            Assert.AreEqual(3, _satisfaction.HousingCount);
            Assert.AreEqual(2f / 3f, _satisfaction.GoodHousingRatio, 0.001f);
            Assert.AreEqual(0f, _satisfaction.BadHousingRatio);
        }

        [Test]
        public void 마을_전체_감점도_주택별_상태에_반영된다()
        {
            var power = _world.CreateManager<PowerManager>(); // 보유 전력 0
            var house = House(0, 0);
            var buildings = new List<BuildingInstance>
            {
                house, Affector(10f, 1, 0),
                _world.CreateBuilding(_world.CreateData(BuildingType.Facility, power: 50), 20, 20)
            };
            power.RecalculatePowerUsage(buildings);

            _satisfaction.RecalculateSatisfaction(buildings);

            // 80 - 전면 정전 30 = 50 → 보통
            Assert.AreEqual(50f, house.GetComponent<HousingInstance>().CurrentSatisfaction, 0.001f);
            Assert.AreEqual(SatisfactionState.Normal, house.GetComponent<HousingInstance>().State);
            Assert.AreEqual(0f, _satisfaction.GoodHousingRatio);
        }

        [Test]
        public void 마을_상태는_평균_만족도로_판정한다()
        {
            _satisfaction.RecalculateSatisfaction(new List<BuildingInstance> { House(0, 0), Affector(-50f, 1, 0) });
            Assert.AreEqual(SatisfactionState.Bad, _satisfaction.CurrentState);
        }
    }

    public class GameOutcomeTests
    {
        private TestWorld _world;
        private SatisfactionManager _satisfaction;
        private ServiceAccessibilityManager _service;
        private GameOutcomeManager _outcome;
        private List<BuildingInstance> _buildings;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _satisfaction = _world.CreateManager<SatisfactionManager>();
            _service = _world.CreateManager<ServiceAccessibilityManager>();
            _outcome = _world.CreateManager<GameOutcomeManager>();
            _buildings = new List<BuildingInstance>();
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private void Recalculate()
        {
            _service.RecalculateServiceMetrics(_buildings);
            _satisfaction.RecalculateSatisfaction(_buildings);
        }

        private void Days(int count)
        {
            for (int i = 0; i < count; i++) _outcome.ProcessDay();
        }

        /// <summary>주택 1채가 '나쁨'(20점)이 되는 마을</summary>
        private BuildingInstance MakeBadTown()
        {
            _buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 10), 0, 0));
            var nimby = _world.CreateBuilding(_world.CreateData(BuildingType.Nimbys, impact: -50f), 1, 0);
            _buildings.Add(nimby);
            Recalculate();
            return nimby;
        }

        /// <summary>필수 시설 4종이 붙어 있어 주택이 '좋음'인 마을 (policeToo=false면 경찰서 제외)</summary>
        private void MakeGoodTown(bool policeToo = true)
        {
            _buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 10), 5, 5));
            _buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Essential, id: "hospital"), 6, 5));
            _buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Essential, id: "school"), 4, 5));
            _buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Essential, id: "fire"), 5, 6));
            if (policeToo) AddPolice();
            Recalculate();
        }

        private void AddPolice()
        {
            _buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Essential, id: "police"), 5, 4));
            Recalculate();
        }

        [Test]
        public void 나쁨이_30일_연속이면_배드엔딩()
        {
            var time = _world.CreateManager<TimeManager>();
            MakeBadTown();

            Days(29);
            Assert.IsFalse(_outcome.IsGameOver);
            Assert.AreEqual(29, _outcome.BadStreakDays);

            Days(1);
            Assert.AreEqual(GameResult.Defeat, _outcome.Result);
            Assert.IsTrue(time.IsEventPaused, "게임이 끝나면 시간이 멈춰야 한다");
        }

        [Test]
        public void 나쁨에서_벗어나면_연속일수가_초기화된다()
        {
            var nimby = MakeBadTown();
            Days(20);

            _buildings.Remove(nimby);
            Recalculate();
            Days(1);

            Assert.AreEqual(0, _outcome.BadStreakDays);
            Assert.IsFalse(_outcome.IsGameOver);
        }

        [Test]
        public void 공사중_유예_요청이_있으면_나쁨_일수가_멈춘다()
        {
            MakeBadTown();
            Days(10);

            var construction = new object();
            _outcome.RequestDefeatGrace(construction);
            Days(100);
            Assert.AreEqual(10, _outcome.BadStreakDays);
            Assert.IsFalse(_outcome.IsGameOver);

            _outcome.ReleaseDefeatGrace(construction);
            Days(20);
            Assert.AreEqual(GameResult.Defeat, _outcome.Result);
        }

        [Test]
        public void 나쁨_일수_변경_이벤트가_발생한다()
        {
            int lastDays = -1, lastLimit = -1;
            _outcome.OnBadStreakChanged += (days, limit) => { lastDays = days; lastLimit = limit; };
            MakeBadTown();
            Days(3);
            Assert.AreEqual(3, lastDays);
            Assert.AreEqual(30, lastLimit);
        }

        [Test]
        public void 조건을_모두_갖추면_10년이_되는_날_해피엔딩()
        {
            MakeGoodTown();
            Assert.AreEqual(1f, _satisfaction.GoodHousingRatio);

            Days(_outcome.VictoryDays - 1);
            Assert.IsFalse(_outcome.IsGameOver, "10년 전에는 끝나지 않는다");

            Days(1);
            Assert.AreEqual(GameResult.Victory, _outcome.Result);
        }

        [Test]
        public void 필수시설이_빠지면_10년이_지나도_계속되고_갖추는_날_승리()
        {
            MakeGoodTown(policeToo: false);
            Days(_outcome.VictoryDays + 50);

            Assert.IsFalse(_outcome.IsGameOver);
            CollectionAssert.AreEqual(new[] { ServiceType.Police }, _outcome.GetMissingRequiredServices());

            AddPolice();
            Days(1);
            Assert.AreEqual(GameResult.Victory, _outcome.Result);
        }

        [Test]
        public void 좋음_70퍼센트를_30일_이상_유지해야_승리()
        {
            Days(_outcome.VictoryDays - 10); // 주택 없이 보내다가
            MakeGoodTown();                   // 10년 10일 전에 좋은 마을 완성

            Days(29);
            Assert.IsFalse(_outcome.IsGameOver, "좋음 유지 29일");

            Days(1);
            Assert.AreEqual(GameResult.Victory, _outcome.Result);
        }

        [Test]
        public void 인구_기준에_못미치면_승리하지_않는다()
        {
            var population = _world.CreateManager<PopulationManager>();
            TestWorld.SetField(_outcome, "requiredPopulation", 5);
            MakeGoodTown();

            Days(_outcome.VictoryDays + 30);
            Assert.IsFalse(_outcome.IsGameOver);

            population.RecalculateCapacity(_buildings);
            population.AddPopulation(5);
            Days(1);
            Assert.AreEqual(GameResult.Victory, _outcome.Result);
        }

        [Test]
        public void 게임이_끝나면_더이상_판정하지_않는다()
        {
            MakeBadTown();
            Days(40);
            Assert.AreEqual(30, _outcome.DaysElapsed);
        }

        [Test]
        public void TimeManager의_날짜가_넘어갈_때마다_하루를_센다()
        {
            var time = _world.CreateManager<TimeManager>();
            TestWorld.Invoke(_outcome, "Start");

            for (int i = 0; i < 3; i++) TestWorld.Invoke(time, "AdvanceOneDay");

            Assert.AreEqual(3, _outcome.DaysElapsed);
        }

        [Test]
        public void 게임루프가_엔딩_매니저를_자동으로_붙인다()
        {
            var loop = _world.CreateManager<GameLoopManager>();
            // 씬에 엔딩 매니저가 없는 상황 재현
            typeof(GameOutcomeManager).GetProperty("Instance").SetValue(null, null);

            TestWorld.Invoke(loop, "Start");

            Assert.IsNotNull(loop.GetComponent<GameOutcomeManager>());
        }
    }
}

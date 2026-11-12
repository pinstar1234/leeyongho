using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.Population;
using InfraTown.Power;
using InfraTown.Satisfaction;
using InfraTown.Services;
using InfraTown.TimeSystem;

namespace InfraTown.Tests
{
    public class ServiceAccessibilityTests
    {
        private TestWorld _world;
        private ServiceAccessibilityManager _service;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _service = _world.CreateManager<ServiceAccessibilityManager>();
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private BuildingInstance Facility(string id, string name, BuildingType type, float accessTime = 5f, int patrol = 3)
        {
            var data = _world.CreateData(type, id: id, name: name);
            data.fixedAccessTimeMinutes = accessTime;
            data.policePatrolCount = patrol;
            return _world.CreateBuilding(data, 0, 0);
        }

        [Test]
        public void 시설이_없으면_미설치_수치와_불만이_나온다()
        {
            _service.RecalculateServiceMetrics(new List<BuildingInstance>());

            Assert.IsFalse(_service.HasPoliceStation);
            Assert.AreEqual(0, _service.DailyPolicePatrolCount);
            Assert.AreEqual(99.9f, _service.HospitalAccessTime, 0.001f);
            StringAssert.Contains("경찰서가 없어", _service.GetServiceComplaintSummary());
            StringAssert.Contains("병원이 없어", _service.GetServiceComplaintSummary());
        }

        [Test]
        public void ID_또는_이름으로_시설을_인식하고_데이터의_수치를_쓴다()
        {
            _service.RecalculateServiceMetrics(new List<BuildingInstance>
            {
                Facility("police_01", "치안센터", BuildingType.Essential, patrol: 4),
                Facility("h1", "중앙병원", BuildingType.Essential, accessTime: 7f),
                Facility("FireStation", "x", BuildingType.Essential, accessTime: 3f),
                Facility("shop", "편의점", BuildingType.Facility, accessTime: 2f),
                Facility("s1", "초등학교", BuildingType.Essential, accessTime: 6f),
            });

            Assert.AreEqual(4, _service.DailyPolicePatrolCount);
            Assert.AreEqual(7f, _service.HospitalAccessTime);
            Assert.AreEqual(3f, _service.FireStationAccessTime);
            Assert.AreEqual(2f, _service.ConvenienceAccessTime);
            Assert.AreEqual(6f, _service.SchoolAccessTime);
            Assert.IsTrue(_service.HasSchool);
            StringAssert.Contains("갖추어져", _service.GetServiceComplaintSummary());
        }

        private BuildingInstance HouseAt(int x, int y) =>
            _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 10), x, y);

        private BuildingInstance HospitalAt(int x, int y, float baseTime = 5f, BuildingSizeType size = BuildingSizeType.Small)
        {
            var data = _world.CreateData(BuildingType.Essential, id: "hospital", name: "병원", size: size);
            data.fixedAccessTimeMinutes = baseTime;
            return _world.CreateBuilding(data, x, y);
        }

        [Test]
        public void 접근시간은_기본시간_더하기_거리x타일당_0점5분()
        {
            // 주택(0,0)과 병원(11,0) 사이 빈 칸 10개 → 5 + 10 × 0.5 = 10분
            var house = HouseAt(0, 0);
            _service.RecalculateServiceMetrics(new List<BuildingInstance> { house, HospitalAt(11, 0) });

            Assert.AreEqual(10f, _service.GetAccessTime(house, ServiceType.Hospital), 0.001f);
            Assert.AreEqual(10f, _service.HospitalAccessTime, 0.001f);
        }

        [Test]
        public void 맞닿은_시설은_기본시간만_걸린다()
        {
            var house = HouseAt(0, 0);
            _service.RecalculateServiceMetrics(new List<BuildingInstance> { house, HospitalAt(1, 1) });
            Assert.AreEqual(5f, _service.GetAccessTime(house, ServiceType.Hospital), 0.001f);
        }

        [Test]
        public void 큰_건물은_가장_가까운_칸부터_거리를_잰다()
        {
            // 4x4 병원(2..5, 0..3) 왼쪽 끝과 주택(0,0) 사이 빈 칸 1개
            var house = HouseAt(0, 0);
            _service.RecalculateServiceMetrics(new List<BuildingInstance> { house, HospitalAt(2, 0, size: BuildingSizeType.Large) });
            Assert.AreEqual(5.5f, _service.GetAccessTime(house, ServiceType.Hospital), 0.001f);
        }

        [Test]
        public void 여러_시설_중_가장_빠른_곳을_쓴다()
        {
            var house = HouseAt(20, 0);
            _service.RecalculateServiceMetrics(new List<BuildingInstance>
            {
                house,
                HospitalAt(0, 0),               // 빈 칸 19 → 14.5분
                HospitalAt(25, 0, baseTime: 8f) // 빈 칸 4  → 10분
            });
            Assert.AreEqual(10f, _service.GetAccessTime(house, ServiceType.Hospital), 0.001f);
        }

        [Test]
        public void 마을_접근시간은_주택_평균()
        {
            var near = HouseAt(0, 0);  // 빈 칸 0 → 5분
            var far = HouseAt(0, 22);  // 빈 칸 20 → 15분
            _service.RecalculateServiceMetrics(new List<BuildingInstance> { near, far, HospitalAt(0, 1) });
            Assert.AreEqual(10f, _service.HospitalAccessTime, 0.001f);
        }

        [Test]
        public void 기준보다_먼_주택이_있으면_불만이_나온다()
        {
            var far = HouseAt(50, 30); // 빈 칸 48 + 29 = 77 → 43.5분
            _service.RecalculateServiceMetrics(new List<BuildingInstance> { HouseAt(0, 0), far, HospitalAt(1, 0) });

            Assert.AreEqual(1, _service.GetLongAccessHousingCount(ServiceType.Hospital));
            StringAssert.Contains("주택 1채가 병원까지 15분 넘게", _service.GetServiceComplaintSummary());
        }

        [Test]
        public void 병원이_없으면_주택의_접근시간은_미설치값()
        {
            var house = HouseAt(0, 0);
            _service.RecalculateServiceMetrics(new List<BuildingInstance> { house });
            Assert.AreEqual(ServiceAccessibilityManager.DefaultUninstalledTime, _service.GetAccessTime(house, ServiceType.Hospital));
        }

        [Test]
        public void 갱신_이벤트가_발생한다()
        {
            bool fired = false;
            _service.OnServiceMetricsUpdated += () => fired = true;
            _service.RecalculateServiceMetrics(new List<BuildingInstance>());
            Assert.IsTrue(fired);
        }
    }

    public class TimeTests
    {
        private TestWorld _world;
        private TimeManager _time;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _time = _world.CreateManager<TimeManager>();
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private void AdvanceDays(int days)
        {
            for (int i = 0; i < days; i++) TestWorld.Invoke(_time, "AdvanceOneDay");
        }

        [Test]
        public void 일주일마다_주간틱()
        {
            int weekly = 0;
            _time.OnWeeklyTick += () => weekly++;
            AdvanceDays(14);
            Assert.AreEqual(2, weekly);
        }

        [Test]
        public void 한달이_지나면_월간틱과_다음달_1일()
        {
            int monthly = 0;
            _time.OnMonthlyTick += () => monthly++;
            AdvanceDays(30);
            Assert.AreEqual(1, monthly);
            Assert.AreEqual(2, _time.CurrentMonth);
            Assert.AreEqual(1, _time.CurrentDay);
        }

        [Test]
        public void 일년이_지나면_다음해()
        {
            AdvanceDays(360);
            Assert.AreEqual(2, _time.CurrentYear);
            Assert.AreEqual(1, _time.CurrentMonth);
        }

        [Test]
        public void 일시정지_잠금은_모든_요청이_풀려야_해제된다()
        {
            object a = new object(), b = new object();
            _time.RequestEventPause(a);
            _time.RequestEventPause(b);
            _time.ReleaseEventPause(a);
            Assert.IsTrue(_time.IsEventPaused);
            Assert.AreEqual(0f, Time.timeScale);

            _time.ReleaseEventPause(b);
            Assert.IsFalse(_time.IsEventPaused);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [Test]
        public void 배속은_timeScale에_반영된다()
        {
            _time.SetGameSpeed(GameSpeed.SuperFast);
            Assert.AreEqual(3f, Time.timeScale);
        }
    }

    public class BuildingManagerTests
    {
        private TestWorld _world;
        private BuildingManager _manager;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _manager = _world.CreateManager<BuildingManager>();
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private bool CanPlace(int x, int z, int sx, int sz) =>
            (bool)TestWorld.Invoke(_manager, "CanPlaceBuilding", x, z, sx, sz);

        [Test]
        public void 규격별_크기()
        {
            Assert.AreEqual(new Vector2Int(1, 1), _world.CreateData(BuildingType.Housing, size: BuildingSizeType.Small).GridSize);
            Assert.AreEqual(new Vector2Int(2, 2), _world.CreateData(BuildingType.Housing, size: BuildingSizeType.Medium).GridSize);
            Assert.AreEqual(new Vector2Int(4, 4), _world.CreateData(BuildingType.Housing, size: BuildingSizeType.Large).GridSize);
        }

        [Test]
        public void 카테고리와_규격으로_건물을_찾는다()
        {
            var small = _world.CreateData(BuildingType.Housing, id: "s", size: BuildingSizeType.Small);
            var medium = _world.CreateData(BuildingType.Housing, id: "m", size: BuildingSizeType.Medium);
            TestWorld.SetField(_manager, "buildingDatabase", new List<BuildingData> { small, medium });

            Assert.AreSame(medium, _manager.GetBuildingDataByCategoryAndSize(BuildingType.Housing, BuildingSizeType.Medium));
            Assert.IsNull(_manager.GetBuildingDataByCategoryAndSize(BuildingType.Nimbys, BuildingSizeType.Small));
        }

        [Test]
        public void 인구가_조건에_닿아야_해금된다()
        {
            var population = _world.CreateManager<PopulationManager>();
            var locked = _world.CreateData(BuildingType.Essential, requiredPopulation: 50);
            Assert.IsFalse(_manager.IsBuildingUnlocked(locked));

            var house = _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 100), 0, 0);
            population.RecalculateCapacity(new List<BuildingInstance> { house });
            population.AddPopulation(50);

            Assert.IsTrue(_manager.IsBuildingUnlocked(locked));
        }

        [Test]
        public void 맵_밖에는_지을_수_없다()
        {
            Assert.IsTrue(CanPlace(59, 39, 1, 1));
            Assert.IsFalse(CanPlace(59, 39, 2, 2));
            Assert.IsFalse(CanPlace(-1, 0, 1, 1));
        }

        [Test]
        public void 이미_점유된_타일에는_지을_수_없다()
        {
            var grid = TestWorld.GetField<BuildingInstance[,]>(_manager, "_occupiedGrid");
            grid[5, 5] = _world.CreateBuilding(_world.CreateData(BuildingType.Housing), 5, 5);

            Assert.IsFalse(CanPlace(4, 4, 2, 2));
            Assert.IsTrue(CanPlace(6, 6, 2, 2));
        }
    }

    public class GameLoopTests
    {
        private TestWorld _world;
        private GameLoopManager _loop;
        private EconomyManager _economy;
        private PopulationManager _population;
        private SatisfactionManager _satisfaction;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _economy = _world.CreateManager<EconomyManager>();
            _population = _world.CreateManager<PopulationManager>();
            _satisfaction = _world.CreateManager<SatisfactionManager>();
            _loop = _world.CreateManager<GameLoopManager>(callStart: true);
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private static void RaiseBuildingDestroyed(BuildingInstance building)
        {
            var field = typeof(BuildingInstance).GetField("OnBuildingDestroyed", BindingFlags.Static | BindingFlags.NonPublic);
            (field.GetValue(null) as Action<BuildingInstance>)?.Invoke(building);
        }

        [Test]
        public void 건물_등록시_수용인구와_만족도가_갱신된다()
        {
            var house = _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 20), 0, 0);
            var park = _world.CreateBuilding(_world.CreateData(BuildingType.Facility, impact: 10f), 1, 0);

            _loop.RegisterBuilding(house);
            _loop.RegisterBuilding(park);

            Assert.AreEqual(20, _population.MaxPopulationCapacity);
            Assert.AreEqual(80f, _satisfaction.CurrentAverageSatisfaction);
        }

        [Test]
        public void 같은_건물을_두번_등록해도_한번만_계산된다()
        {
            var house = _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 20), 0, 0);
            _loop.RegisterBuilding(house);
            _loop.RegisterBuilding(house);
            Assert.AreEqual(20, _population.MaxPopulationCapacity);
        }

        [Test]
        public void 건물_철거시_수용인구가_줄어든다()
        {
            var house = _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 20), 0, 0);
            _loop.RegisterBuilding(house);

            RaiseBuildingDestroyed(house);

            Assert.AreEqual(0, _population.MaxPopulationCapacity);
        }

        [Test]
        public void 주간틱에_인구가_유입된다()
        {
            _loop.RegisterBuilding(_world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 20), 0, 0));
            TestWorld.Invoke(_loop, "HandleWeeklyTick");
            Assert.AreEqual(3, _population.CurrentPopulation);
        }

        [Test]
        public void 월간틱에_유지비가_차감된다()
        {
            _economy.SetGovernmentSubsidyAmount(0);
            _loop.RegisterBuilding(_world.CreateBuilding(_world.CreateData(BuildingType.Facility, maintenance: 1000), 0, 0));

            TestWorld.Invoke(_loop, "HandleMonthlyTick");

            Assert.AreEqual(9000, _economy.CurrentMoney);
        }

        [Test]
        public void 건물_등록시_접근성_지표가_갱신된다()
        {
            var service = _world.CreateManager<ServiceAccessibilityManager>();
            _loop.RegisterBuilding(_world.CreateBuilding(_world.CreateData(BuildingType.Essential, id: "hospital"), 0, 0));
            Assert.IsTrue(service.HasHospital);
        }

        [Test]
        public void 건물_철거시_접근성_지표가_갱신된다()
        {
            var service = _world.CreateManager<ServiceAccessibilityManager>();
            var hospital = _world.CreateBuilding(_world.CreateData(BuildingType.Essential, id: "hospital"), 0, 0);
            _loop.RegisterBuilding(hospital);

            RaiseBuildingDestroyed(hospital);

            Assert.IsFalse(service.HasHospital);
        }

        [Test]
        public void 건물_등록시_전력_사용량이_갱신된다()
        {
            var power = _world.CreateManager<PowerManager>();
            _loop.RegisterBuilding(_world.CreateBuilding(_world.CreateData(BuildingType.Facility, power: 40), 0, 0));
            Assert.AreEqual(40, power.CurrentUsedPower);
        }

        [Test]
        public void 전력이_부족한_건물을_지으면_만족도가_떨어진다()
        {
            var power = _world.CreateManager<PowerManager>(); // 보유 전력 0
            _loop.RegisterBuilding(_world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 10), 0, 0));
            Assert.AreEqual(70f, _satisfaction.CurrentAverageSatisfaction);

            _loop.RegisterBuilding(_world.CreateBuilding(_world.CreateData(BuildingType.Facility, power: 40), 10, 10));

            Assert.AreEqual(40f, _satisfaction.CurrentAverageSatisfaction); // 전면 정전 -30
        }

        [Test]
        public void 월간틱은_지원금_유지비_전력구매_순서로_정산한다()
        {
            var power = _world.CreateManager<PowerManager>();
            _economy.TrySpendMoney(9000);                  // 잔액 1,000
            _economy.SetGovernmentSubsidyAmount(5000);
            _loop.RegisterBuilding(_world.CreateBuilding(_world.CreateData(BuildingType.Facility, maintenance: 1000), 0, 0));

            TestWorld.Invoke(_loop, "HandleMonthlyTick");

            // 1,000 + 5,000(지원금) - 1,000(유지비) - 2,000(전력 200kW) = 3,000
            Assert.AreEqual(3000, _economy.CurrentMoney);
            Assert.AreEqual(200, power.CurrentAvailablePower);
        }
    }
}

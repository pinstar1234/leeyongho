using System.Collections.Generic;
using NUnit.Framework;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Satisfaction;
using InfraTown.Services;

namespace InfraTown.Tests
{
    /// <summary>
    /// 밸런스: 해금됐는데 없는 필수 시설 1종당 1주일마다 마을 만족도 2% 감점, 시설당 최대 10%, 지으면 주마다 2%씩 회복
    /// </summary>
    public class MissingFacilityPenaltyTests
    {
        private TestWorld _world;
        private SatisfactionManager _satisfaction;
        private ServiceAccessibilityManager _service;
        private BuildingData _hospital, _school, _fire;
        private BuildingInstance _house;
        private readonly List<BuildingInstance> _buildings = new List<BuildingInstance>();

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            var manager = _world.CreateManager<BuildingManager>();
            _service = _world.CreateManager<ServiceAccessibilityManager>();
            _satisfaction = _world.CreateManager<SatisfactionManager>();

            _hospital = _world.CreateData(BuildingType.Essential, id: "hospital", name: "병원");
            _school = _world.CreateData(BuildingType.Essential, id: "school", name: "학교", requiredPopulation: 300); // 잠김
            _fire = _world.CreateData(BuildingType.Essential, id: "fire_station", name: "소방서", requiredPopulation: 300); // 잠김
            TestWorld.SetField(manager, "buildingDatabase", new List<BuildingData> { _hospital, _school, _fire });

            _buildings.Clear();
            _house = _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 10), 0, 0);
            _buildings.Add(_house);
            Refresh();
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private void Refresh()
        {
            _service.RecalculateServiceMetrics(_buildings);
            _satisfaction.RecalculateSatisfaction(_buildings);
        }

        private void Weeks(int n)
        {
            for (int i = 0; i < n; i++)
            {
                _satisfaction.ProcessWeeklyMissingFacilityPenalty();
                Refresh();
            }
        }

        [Test]
        public void 해금됐는데_없으면_1주일마다_2퍼센트씩_떨어진다()
        {
            Weeks(1);
            Assert.AreEqual(2f, _satisfaction.GetMissingFacilityPenalty(ServiceType.Hospital));
            Assert.AreEqual(68f, _satisfaction.CurrentAverageSatisfaction, 0.001f);

            Weeks(2);
            Assert.AreEqual(6f, _satisfaction.GetMissingFacilityPenalty(ServiceType.Hospital));
            Assert.AreEqual(64f, _satisfaction.CurrentAverageSatisfaction, 0.001f);
        }

        [Test]
        public void 시설당_최대_10퍼센트까지만_떨어진다()
        {
            Weeks(8);
            Assert.AreEqual(10f, _satisfaction.GetMissingFacilityPenalty(ServiceType.Hospital));
            Assert.AreEqual(60f, _satisfaction.CurrentAverageSatisfaction, 0.001f);
        }

        [Test]
        public void 아직_잠긴_시설은_감점이_없다()
        {
            Weeks(5);
            Assert.AreEqual(0f, _satisfaction.GetMissingFacilityPenalty(ServiceType.School));
            Assert.AreEqual(0f, _satisfaction.GetMissingFacilityPenalty(ServiceType.FireStation));
        }

        [Test]
        public void 없는_시설마다_따로_쌓인다()
        {
            _school.requiredPopulation = 0; // 학교도 해금
            Weeks(2);
            Assert.AreEqual(4f, _satisfaction.GetMissingFacilityPenalty(ServiceType.Hospital));
            Assert.AreEqual(4f, _satisfaction.GetMissingFacilityPenalty(ServiceType.School));
            Assert.AreEqual(8f, _satisfaction.TotalMissingFacilityPenalty);
            Assert.AreEqual(62f, _satisfaction.CurrentAverageSatisfaction, 0.001f);
        }

        [Test]
        public void 지으면_1주일마다_2퍼센트씩_천천히_회복된다()
        {
            Weeks(5); // 10%
            _buildings.Add(_world.CreateBuilding(_hospital, 30, 30)); // 멀리 지어 접근 보너스는 거의 없음
            Refresh();
            Assert.AreEqual(10f, _satisfaction.GetMissingFacilityPenalty(ServiceType.Hospital), "짓자마자 바로 사라지지 않음");

            Weeks(1);
            Assert.AreEqual(8f, _satisfaction.GetMissingFacilityPenalty(ServiceType.Hospital));
            Weeks(4);
            Assert.AreEqual(0f, _satisfaction.GetMissingFacilityPenalty(ServiceType.Hospital));
            Weeks(3);
            Assert.AreEqual(0f, _satisfaction.GetMissingFacilityPenalty(ServiceType.Hospital), "0 밑으로 내려가지 않음");
        }

        [Test]
        public void 감점이_바뀌면_이벤트가_발생한다()
        {
            float total = -1f;
            _satisfaction.OnMissingFacilityPenaltyChanged += t => total = t;
            Weeks(1);
            Assert.AreEqual(2f, total);
        }

        [Test]
        public void 주택별_좋음_나쁨에도_반영된다()
        {
            var park = _world.CreateBuilding(_world.CreateData(BuildingType.Facility, impact: 5f), 1, 0);
            _buildings.Add(park);
            Refresh();
            Assert.AreEqual(SatisfactionState.Good, _house.GetComponent<HousingInstance>().State, "75점 → 좋음");

            Weeks(3); // -6 → 69
            Assert.AreEqual(SatisfactionState.Normal, _house.GetComponent<HousingInstance>().State);
        }

        [Test]
        public void 게임루프_주간틱에서_감점이_쌓인다()
        {
            var loop = _world.CreateManager<GameLoopManager>();
            loop.RegisterBuilding(_house);

            TestWorld.Invoke(loop, "HandleWeeklyTick");

            Assert.AreEqual(2f, _satisfaction.GetMissingFacilityPenalty(ServiceType.Hospital));
            Assert.AreEqual(68f, _satisfaction.CurrentAverageSatisfaction, 0.001f);
        }
    }
}

using NUnit.Framework;
using System.Collections.Generic;
using InfraTown.Building;
using InfraTown.Economy;
using InfraTown.Power;
using InfraTown.Satisfaction;

namespace InfraTown.Tests
{
    public class SatisfactionTests
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

        private BuildingInstance Affector(float impact, int radius, int x, int y) =>
            _world.CreateBuilding(_world.CreateData(BuildingType.Facility, impact: impact, radius: radius), x, y);

        [Test]
        public void 주택이_없으면_기본_만족도_70_안정()
        {
            _satisfaction.RecalculateSatisfaction(new List<BuildingInstance>());
            Assert.AreEqual(70f, _satisfaction.CurrentAverageSatisfaction);
            Assert.AreEqual(SatisfactionTier.Stable, _satisfaction.CurrentTier);
        }

        [Test]
        public void 반경_안의_시설_영향이_더해진다()
        {
            _satisfaction.RecalculateSatisfaction(new List<BuildingInstance> { House(0, 0), Affector(15f, 3, 3, 3) });
            Assert.AreEqual(85f, _satisfaction.CurrentAverageSatisfaction);
            Assert.AreEqual(SatisfactionTier.Prosperous, _satisfaction.CurrentTier);
        }

        [Test]
        public void 반경_밖의_시설은_영향이_없다()
        {
            // 체비셰프 거리 4 > 반경 3
            _satisfaction.RecalculateSatisfaction(new List<BuildingInstance> { House(0, 0), Affector(15f, 3, 4, 0) });
            Assert.AreEqual(70f, _satisfaction.CurrentAverageSatisfaction);
        }

        [Test]
        public void 여러_주택의_만족도는_평균낸다()
        {
            // 주택 A만 +20 범위 안 → (90 + 70) / 2 = 80
            var buildings = new List<BuildingInstance> { House(0, 0), House(20, 20), Affector(20f, 2, 1, 1) };
            _satisfaction.RecalculateSatisfaction(buildings);
            Assert.AreEqual(80f, _satisfaction.CurrentAverageSatisfaction, 0.001f);
        }

        [Test]
        public void 만족도는_0에서_100_사이로_제한된다()
        {
            _satisfaction.RecalculateSatisfaction(new List<BuildingInstance> { House(0, 0), Affector(-500f, 3, 1, 0) });
            Assert.AreEqual(0f, _satisfaction.CurrentAverageSatisfaction);
            Assert.AreEqual(SatisfactionTier.Desolate, _satisfaction.CurrentTier);
        }

        [TestCase(-50f, SatisfactionTier.Desolate)]   // 20
        [TestCase(-30f, SatisfactionTier.Stagnant)]   // 40
        [TestCase(-10f, SatisfactionTier.Developing)] // 60
        [TestCase(10f, SatisfactionTier.Stable)]      // 80
        [TestCase(11f, SatisfactionTier.Prosperous)]  // 81
        public void 만족도_등급_경계값(float impact, SatisfactionTier expected)
        {
            _satisfaction.RecalculateSatisfaction(new List<BuildingInstance> { House(0, 0), Affector(impact, 3, 1, 0) });
            Assert.AreEqual(expected, _satisfaction.CurrentTier);
        }

        [Test]
        public void 전력이_전혀_없으면_정전_패널티_30()
        {
            var power = _world.CreateManager<PowerManager>();
            var buildings = new List<BuildingInstance>
            {
                House(0, 0),
                _world.CreateBuilding(_world.CreateData(BuildingType.Facility, power: 50), 10, 10)
            };
            power.RecalculatePowerUsage(buildings);

            _satisfaction.RecalculateSatisfaction(buildings);

            Assert.AreEqual(40f, _satisfaction.CurrentAverageSatisfaction);
        }

        [Test]
        public void 전봇대_반경_밖_주택만_만족도가_깎인다()
        {
            var economy = _world.CreateManager<EconomyManager>();
            var power = _world.CreateManager<PowerManager>();
            power.ProcessMonthlyPowerPurchase();

            var nearHouse = House(0, 0);
            var farHouse = House(30, 30);
            var pole = _world.CreateBuilding(_world.CreateData(BuildingType.Essential), 2, 2, withPole: true);
            var buildings = new List<BuildingInstance> { nearHouse, farHouse, pole };
            power.RecalculatePowerUsage(buildings);

            _satisfaction.RecalculateSatisfaction(buildings);

            Assert.AreEqual(70f, nearHouse.GetComponent<HousingInstance>().CurrentSatisfaction);
            Assert.AreEqual(55f, farHouse.GetComponent<HousingInstance>().CurrentSatisfaction);
            Assert.AreEqual(62.5f, _satisfaction.CurrentAverageSatisfaction, 0.001f);
        }

        [Test]
        public void 인구초과_패널티가_만족도에_반영된다()
        {
            var population = _world.CreateManager<InfraTown.Population.PopulationManager>();
            var house = House(0, 0);
            var house2 = House(10, 10);
            population.RecalculateCapacity(new List<BuildingInstance> { house, house2 }); // 수용 20
            population.AddPopulation(20);
            population.RecalculateCapacity(new List<BuildingInstance> { house });         // 수용 10, 100% 초과 → 50

            _satisfaction.RecalculateSatisfaction(new List<BuildingInstance> { house });

            Assert.AreEqual(20f, _satisfaction.CurrentAverageSatisfaction, 0.001f);
        }

        [Test]
        public void 만족도_변경_이벤트가_발생한다()
        {
            float received = -1f;
            _satisfaction.OnSatisfactionChanged += (score, tier) => received = score;
            _satisfaction.RecalculateSatisfaction(new List<BuildingInstance> { House(0, 0) });
            Assert.AreEqual(70f, received);
        }
    }

    public class PowerTests
    {
        private TestWorld _world;
        private PowerManager _power;
        private EconomyManager _economy;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _economy = _world.CreateManager<EconomyManager>();
            _power = _world.CreateManager<PowerManager>();
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private void MonthlyPurchase() => TestWorld.Invoke(_power, "ProcessMonthlyPowerPurchase");

        [Test]
        public void 사용전력은_건물_필요전력의_합()
        {
            _power.RecalculatePowerUsage(new List<BuildingInstance>
            {
                _world.CreateBuilding(_world.CreateData(BuildingType.Housing, power: 30), 0, 0),
                _world.CreateBuilding(_world.CreateData(BuildingType.Facility, power: 70), 5, 5)
            });
            Assert.AreEqual(100, _power.CurrentUsedPower);
        }

        [Test]
        public void 월_전력구매는_목표량x10원을_지불한다()
        {
            MonthlyPurchase(); // 기본 목표 200kW
            Assert.AreEqual(200, _power.CurrentAvailablePower);
            Assert.AreEqual(10000 - 2000, _economy.CurrentMoney);
        }

        [Test]
        public void 돈이_부족하면_전력을_구매하지_못한다()
        {
            _economy.TrySpendMoney(9000);
            MonthlyPurchase();
            Assert.AreEqual(0, _power.CurrentAvailablePower);
            Assert.AreEqual(1000, _economy.CurrentMoney);
        }

        [Test]
        public void 전력이_충분하면_패널티가_없다()
        {
            MonthlyPurchase();
            _power.RecalculatePowerUsage(new List<BuildingInstance>
            {
                _world.CreateBuilding(_world.CreateData(BuildingType.Facility, power: 100), 0, 0)
            });
            Assert.AreEqual(0f, _power.GetPowerBlackoutSatisfactionPenalty());
        }

        [Test]
        public void 전력이_부족하면_전봇대가_정전되고_근처_주택이_전기를_못받는다()
        {
            MonthlyPurchase(); // 보유 200
            var house = _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 10), 0, 0);
            var pole = _world.CreateBuilding(_world.CreateData(BuildingType.Essential, power: 300), 2, 2, withPole: true);

            _power.RecalculatePowerUsage(new List<BuildingInstance> { house, pole });

            Assert.IsFalse(pole.GetComponent<UtilityPoleInstance>().IsPowered);
            Assert.IsFalse(_power.IsHousingPowered(house));
            // 전봇대 망이 있으면 마을 전체 감점 대신 전기가 끊긴 주택만 감점
            Assert.AreEqual(0f, _power.GetPowerBlackoutSatisfactionPenalty());
            Assert.AreEqual(15f, _power.GetHousingPowerPenalty(house));
        }

        [Test]
        public void 전봇대가_없으면_전력_부족_비율만큼_마을_전체_감점()
        {
            MonthlyPurchase(); // 보유 200
            _power.RecalculatePowerUsage(new List<BuildingInstance>
            {
                _world.CreateBuilding(_world.CreateData(BuildingType.Facility, power: 400), 0, 0)
            });

            // 400 중 200 부족 → 50% × 30 = 15
            Assert.AreEqual(15f, _power.GetPowerBlackoutSatisfactionPenalty(), 0.001f);
        }

        [Test]
        public void 전봇대가_없으면_전력이_충분한지로_주택_전기공급을_판단한다()
        {
            MonthlyPurchase();
            var house = _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 10, power: 50), 0, 0);
            _power.RecalculatePowerUsage(new List<BuildingInstance> { house });

            Assert.IsTrue(_power.IsHousingPowered(house));
            Assert.AreEqual(0f, _power.GetHousingPowerPenalty(house));
        }

        [Test]
        public void 게임_시작시_첫달_전력을_바로_구매한다()
        {
            TestWorld.Invoke(_power, "Start");
            Assert.AreEqual(200, _power.CurrentAvailablePower);
            Assert.AreEqual(8000, _economy.CurrentMoney);
        }

        [Test]
        public void 정상_전봇대_반경_안의_주택은_전기를_받는다()
        {
            MonthlyPurchase();
            var house = _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 10), 0, 0);
            var pole = _world.CreateBuilding(_world.CreateData(BuildingType.Essential), 4, 4, withPole: true);
            var farHouse = _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 10), 30, 30);

            _power.RecalculatePowerUsage(new List<BuildingInstance> { house, pole, farHouse });

            Assert.IsTrue(_power.IsHousingPowered(house));
            Assert.IsFalse(_power.IsHousingPowered(farHouse));
        }
    }
}

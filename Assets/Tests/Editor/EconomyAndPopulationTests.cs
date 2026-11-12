using NUnit.Framework;
using System.Collections.Generic;
using InfraTown.Building;
using InfraTown.Economy;
using InfraTown.Population;
using InfraTown.Satisfaction;

namespace InfraTown.Tests
{
    public class EconomyTests
    {
        private TestWorld _world;
        private EconomyManager _economy;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _economy = _world.CreateManager<EconomyManager>();
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void 초기자금은_10000원()
        {
            Assert.AreEqual(10000, _economy.CurrentMoney);
        }

        [Test]
        public void 잔액이_충분하면_차감되고_이벤트가_발생한다()
        {
            int changed = 0;
            _economy.OnMoneyChanged += (money, delta) => changed = delta;

            Assert.IsTrue(_economy.TrySpendMoney(3000));
            Assert.AreEqual(7000, _economy.CurrentMoney);
            Assert.AreEqual(-3000, changed);
        }

        [Test]
        public void 잔액이_부족하면_차감되지_않는다()
        {
            Assert.IsFalse(_economy.TrySpendMoney(10001));
            Assert.AreEqual(10000, _economy.CurrentMoney);
        }

        [Test]
        public void 수입은_양수만_더해진다()
        {
            _economy.AddMoney(500);
            _economy.AddMoney(-500);
            Assert.AreEqual(10500, _economy.CurrentMoney);
        }

        [Test]
        public void 최대_지원금은_15000원_더하기_인구당_50원()
        {
            Assert.AreEqual(15000, _economy.GetMaxEligibleSubsidy());

            var population = _world.CreateManager<PopulationManager>();
            var house = _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 100), 0, 0);
            population.RecalculateCapacity(new List<BuildingInstance> { house });
            population.AddPopulation(40);

            Assert.AreEqual(15000 + 40 * 50, _economy.GetMaxEligibleSubsidy());
        }

        [Test]
        public void 신청_지원금은_0부터_최대치_사이로_제한된다()
        {
            _economy.SetGovernmentSubsidyAmount(999999);
            Assert.AreEqual(15000, _economy.GetRequestedSubsidy());

            _economy.SetGovernmentSubsidyAmount(-10);
            Assert.AreEqual(0, _economy.GetRequestedSubsidy());
        }

        [Test]
        public void 월_정산시_신청한_지원금이_들어온다()
        {
            _economy.SetGovernmentSubsidyAmount(5000);
            _economy.ProcessMonthlySettlement();
            Assert.AreEqual(15000, _economy.CurrentMoney);
        }

        [Test]
        public void 월_정산시_지원금을_받고_유지비를_낸다()
        {
            int income = -1, paid = -1, unpaid = -1;
            _economy.OnMonthlySettled += (i, p, u) => { income = i; paid = p; unpaid = u; };
            _economy.SetGovernmentSubsidyAmount(5000);

            _economy.ProcessMonthlySettlement(0, 3000);

            Assert.AreEqual(10000 + 5000 - 3000, _economy.CurrentMoney);
            Assert.AreEqual(5000, income);
            Assert.AreEqual(3000, paid);
            Assert.AreEqual(0, unpaid);
        }

        [Test]
        public void 잔액이_모자라면_낼수있는_만큼만_내고_미납을_기록한다()
        {
            _economy.SetGovernmentSubsidyAmount(0);

            _economy.ProcessMonthlySettlement(0, 12000);

            Assert.AreEqual(0, _economy.CurrentMoney);
            Assert.AreEqual(10000, _economy.LastMaintenancePaid);
            Assert.AreEqual(2000, _economy.LastUnpaidMaintenance);
        }

        [Test]
        public void 매개변수_없는_정산은_게임루프의_유지비_합계를_쓴다()
        {
            var loop = _world.CreateManager<InfraTown.Core.GameLoopManager>();
            loop.RegisterBuilding(_world.CreateBuilding(_world.CreateData(BuildingType.Facility, maintenance: 700), 0, 0));
            loop.RegisterBuilding(_world.CreateBuilding(_world.CreateData(BuildingType.Facility, maintenance: 300), 3, 3));
            _economy.SetGovernmentSubsidyAmount(0);

            _economy.ProcessMonthlySettlement();

            Assert.AreEqual(9000, _economy.CurrentMoney);
        }
    }

    public class PopulationTests
    {
        private TestWorld _world;
        private PopulationManager _population;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _population = _world.CreateManager<PopulationManager>();
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private List<BuildingInstance> Houses(params int[] capacities)
        {
            var list = new List<BuildingInstance>();
            for (int i = 0; i < capacities.Length; i++)
            {
                list.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: capacities[i]), i * 2, 0));
            }
            return list;
        }

        [Test]
        public void 수용인구는_주택_수용량의_합이고_주택이_아니면_무시된다()
        {
            var buildings = Houses(10, 20);
            buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Facility, capacity: 999), 10, 10));

            _population.RecalculateCapacity(buildings);

            Assert.AreEqual(30, _population.MaxPopulationCapacity);
        }

        [Test]
        public void 수용인구가_줄어도_주민은_초과인구로_남는다()
        {
            var buildings = Houses(10, 20);
            _population.RecalculateCapacity(buildings);
            _population.AddPopulation(25);

            buildings.RemoveAt(1);
            _population.RecalculateCapacity(buildings);

            Assert.AreEqual(25, _population.CurrentPopulation);
            Assert.AreEqual(15, _population.OverCapacityPopulation);
        }

        [Test]
        public void 인구초과_패널티는_초과비율1퍼센트당_0점5()
        {
            var buildings = Houses(20, 20);
            _population.RecalculateCapacity(buildings);
            _population.AddPopulation(24);
            Assert.AreEqual(0f, _population.GetOverCapacitySatisfactionPenalty());

            buildings.RemoveAt(1); // 수용 20, 인구 24 → 20% 초과 → 10점
            _population.RecalculateCapacity(buildings);

            Assert.AreEqual(10f, _population.GetOverCapacitySatisfactionPenalty(), 0.001f);
        }

        [Test]
        public void 인구초과_패널티는_최대_50()
        {
            var buildings = Houses(10);
            _population.RecalculateCapacity(buildings);
            _population.AddPopulation(10);

            _population.RecalculateCapacity(new List<BuildingInstance>()); // 주택 전부 철거

            Assert.AreEqual(50f, _population.GetOverCapacitySatisfactionPenalty());
        }

        [Test]
        public void 초과인구는_매주_절반씩_떠난다()
        {
            var buildings = Houses(10, 20);
            _population.RecalculateCapacity(buildings);
            _population.AddPopulation(30);
            buildings.RemoveAt(1); // 수용 10, 초과 20

            _population.RecalculateCapacity(buildings);
            _population.ProcessWeeklyPopulationUpdate(0);
            Assert.AreEqual(20, _population.CurrentPopulation); // 20명 초과 중 10명 떠남

            _population.ProcessWeeklyPopulationUpdate(0);
            _population.ProcessWeeklyPopulationUpdate(0);
            _population.ProcessWeeklyPopulationUpdate(0);
            _population.ProcessWeeklyPopulationUpdate(0);
            Assert.AreEqual(10, _population.CurrentPopulation); // 결국 수용 인구까지 줄어듦
        }

        [Test]
        public void 초과_상태에서는_인구가_늘지_않는다()
        {
            var buildings = Houses(10, 10);
            _population.RecalculateCapacity(buildings);
            _population.AddPopulation(20);
            buildings.RemoveAt(1);
            _population.RecalculateCapacity(buildings);

            _population.AddPopulation(5);

            Assert.AreEqual(20, _population.CurrentPopulation);
        }

        [Test]
        public void 인구는_수용인구를_넘지_않는다()
        {
            _population.RecalculateCapacity(Houses(5));
            _population.AddPopulation(100);
            Assert.AreEqual(5, _population.CurrentPopulation);
        }

        [Test]
        public void 만족도_70이면_주간_유입은_3명()
        {
            _world.CreateManager<SatisfactionManager>(); // 기본 만족도 70
            _population.RecalculateCapacity(Houses(100));

            _population.ProcessWeeklyPopulationUpdate(0);

            Assert.AreEqual(3, _population.CurrentPopulation);
        }

        [Test]
        public void 만족도가_50_미만이면_유입이_없다()
        {
            var satisfaction = _world.CreateManager<SatisfactionManager>();
            var buildings = Houses(100);
            // 주택 옆 혐오시설 -30 → 만족도 40
            buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Nimbys, impact: -30f), 1, 0));
            satisfaction.RecalculateSatisfaction(buildings);
            _population.RecalculateCapacity(buildings);

            _population.ProcessWeeklyPopulationUpdate(0);

            Assert.AreEqual(0, _population.CurrentPopulation);
        }

        [Test]
        public void 수용인구가_가득차면_유입이_없다()
        {
            _population.RecalculateCapacity(Houses(2));
            _population.AddPopulation(2);

            _population.ProcessWeeklyPopulationUpdate(0);

            Assert.AreEqual(2, _population.CurrentPopulation);
        }
    }
}

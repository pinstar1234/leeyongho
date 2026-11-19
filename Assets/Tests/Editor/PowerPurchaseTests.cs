using System.Collections.Generic;
using NUnit.Framework;
using InfraTown.Building;
using InfraTown.Economy;
using InfraTown.Power;

namespace InfraTown.Tests
{
    /// <summary>
    /// 종합 현황 전력 슬라이더: 다음 달 구매량 설정 → 다음 월간 정산에 적용
    /// </summary>
    public class PowerPurchaseTests
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

        [Test]
        public void 구매량은_0부터_최대치까지_10kW_단위()
        {
            _power.SetTargetPurchasedPower(347);
            Assert.AreEqual(350, _power.TargetPurchasedPower);

            _power.SetTargetPurchasedPower(99999);
            Assert.AreEqual(_power.MaxPurchasablePower, _power.TargetPurchasedPower);

            _power.SetTargetPurchasedPower(-50);
            Assert.AreEqual(0, _power.TargetPurchasedPower);
        }

        [Test]
        public void 구매량을_바꿔도_이번_달_보유_전력과_돈은_그대로()
        {
            _power.ProcessMonthlyPowerPurchase(); // 200kW, -2,000원
            int money = _economy.CurrentMoney;

            _power.SetTargetPurchasedPower(600);

            Assert.AreEqual(200, _power.CurrentAvailablePower);
            Assert.AreEqual(money, _economy.CurrentMoney);
        }

        [Test]
        public void 다음_월간_정산에_새_구매량이_적용된다()
        {
            _power.SetTargetPurchasedPower(600);

            _power.ProcessMonthlyPowerPurchase();

            Assert.AreEqual(600, _power.CurrentAvailablePower);
            Assert.AreEqual(10000 - 6000, _economy.CurrentMoney);
        }

        [Test]
        public void 월_비용과_다음_달_부족량을_알려준다()
        {
            _power.RecalculatePowerUsage(new List<BuildingInstance>
            {
                _world.CreateBuilding(_world.CreateData(BuildingType.Facility, power: 450), 0, 0)
            });
            _power.SetTargetPurchasedPower(300);

            Assert.AreEqual(3000, _power.MonthlyPurchaseCost);
            Assert.AreEqual(150, _power.NextMonthShortage);

            _power.SetTargetPurchasedPower(500);
            Assert.AreEqual(0, _power.NextMonthShortage);
        }

        [Test]
        public void 구매량_변경_이벤트가_발생한다()
        {
            int target = -1, cost = -1;
            _power.OnTargetPowerChanged += (t, c) => { target = t; cost = c; };

            _power.SetTargetPurchasedPower(420);

            Assert.AreEqual(420, target);
            Assert.AreEqual(4200, cost);
        }
    }
}

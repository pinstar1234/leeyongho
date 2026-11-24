using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.Gas;
using InfraTown.Power;
using InfraTown.Save;
using InfraTown.TimeSystem;

namespace InfraTown.Tests
{
    /// <summary>
    /// 사용자 기획 2026-11-24
    /// ① 전력·가스 부분 구매 (전부 아니면 0 → 살 수 있는 만큼, 전력 먼저 남은 돈으로 가스)
    /// ② 정산 2주 전 적자 경고 (지원금 &lt; 유지비 + 전력 + 가스)
    /// </summary>
    public class SettlementForecastTests
    {
        private TestWorld _world;
        private EconomyManager _economy;
        private PowerManager _power;
        private GasManager _gas;
        private TimeManager _time;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _time = _world.CreateManager<TimeManager>();
            _economy = _world.CreateManager<EconomyManager>();
            _power = _world.CreateManager<PowerManager>();
            _gas = _world.CreateManager<GasManager>();
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private void SetWeek(int week) => TestWorld.SetField(_time, "currentWeek", week);

        private void SetMoney(int money) =>
            _economy.ApplySaveState(new EconomySaveState { money = money, requestedSubsidy = 15000 });

        /// <summary>지원금 15,000원 &lt; 전력 10,000 + 가스 10,000 = 20,000원</summary>
        private void MakeDeficit()
        {
            _power.SetTargetPurchasedPower(1000);
            _gas.SetTargetPurchasedGas(1000);
        }

        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, true)]  // 정산 2주 전
        [TestCase(4, true)]  // 정산 1주 전
        public void 정산_2주_전부터_예보_기간(int week, bool expected)
        {
            var forecast = _world.CreateManager<SettlementForecaster>();
            SetWeek(week);
            Assert.AreEqual(expected, forecast.IsForecastPeriod);
            Assert.AreEqual(TimeManager.WeeksPerMonth - week + 1, forecast.WeeksUntilSettlement);
        }

        [Test]
        public void 나갈_돈은_유지비_전력_가스의_합()
        {
            var loop = _world.CreateManager<GameLoopManager>();
            loop.RegisterBuilding(_world.CreateBuilding(_world.CreateData(BuildingType.Facility, maintenance: 1234), 0, 0));
            var forecast = _world.CreateManager<SettlementForecaster>();
            MakeDeficit();

            Assert.AreEqual(15000, forecast.ExpectedSubsidy);
            Assert.AreEqual(1234, forecast.ExpectedMaintenance);
            Assert.AreEqual(10000, forecast.ExpectedPowerCost);
            Assert.AreEqual(10000, forecast.ExpectedGasCost);
            Assert.AreEqual(21234, forecast.ExpectedOutgoing);
            Assert.AreEqual(-6234, forecast.ExpectedMonthlyBalance);
            Assert.IsTrue(forecast.IsDeficit);
        }

        [Test]
        public void 적자면_정산_2주_전에_경고한다()
        {
            var forecast = _world.CreateManager<SettlementForecaster>();
            MakeDeficit();
            int warned = 0;
            forecast.OnWarningChanged += on => { if (on) warned++; };

            SetWeek(2);
            forecast.Refresh();
            Assert.IsFalse(forecast.IsWarning, "정산 3주 전에는 아직 경고하지 않음");

            SetWeek(3);
            forecast.Refresh();
            Assert.IsTrue(forecast.IsWarning);
            Assert.AreEqual(1, warned);

            forecast.Refresh();
            Assert.AreEqual(1, warned, "경고가 켜질 때 한 번만 알림");
        }

        [Test]
        public void 흑자면_정산_직전에도_경고하지_않는다()
        {
            var forecast = _world.CreateManager<SettlementForecaster>();
            SetWeek(4); // 기본 구매량: 전력 2,000 + 가스 1,000원 < 지원금 15,000원
            forecast.Refresh();
            Assert.IsFalse(forecast.IsDeficit);
            Assert.IsFalse(forecast.IsWarning);
        }

        [Test]
        public void 경고_중_슬라이더로_구매량을_줄이면_경고가_꺼진다()
        {
            MakeDeficit();
            SetWeek(3);
            var forecast = _world.CreateManager<SettlementForecaster>(callStart: true); // 슬라이더 이벤트 구독
            Assert.IsTrue(forecast.IsWarning);

            bool? last = null;
            forecast.OnWarningChanged += on => last = on;
            _power.SetTargetPurchasedPower(400); // 4,000 + 10,000 = 14,000 < 15,000

            Assert.IsFalse(forecast.IsWarning);
            Assert.AreEqual(false, last);
        }

        [Test]
        public void 지원금을_줄여_적자가_되면_경고가_켜진다()
        {
            SetWeek(4);
            var forecast = _world.CreateManager<SettlementForecaster>(callStart: true);
            Assert.IsFalse(forecast.IsWarning);

            _economy.SetGovernmentSubsidyAmount(1000); // 1,000 < 전력 2,000 + 가스 1,000
            Assert.IsTrue(forecast.IsWarning);
        }

        [Test]
        public void 고치지_않으면_전력을_먼저_사고_남은_돈으로_가스를_산다()
        {
            var loop = _world.CreateManager<GameLoopManager>();
            loop.RegisterBuilding(_world.CreateBuilding(_world.CreateData(BuildingType.Facility, maintenance: 10000), 0, 0));
            var forecast = _world.CreateManager<SettlementForecaster>();
            MakeDeficit();
            SetMoney(6000); // 6,000 + 지원금 15,000 − 유지비 10,000 = 11,000 → 전력 1,000kW(10,000원) → 남은 1,000원으로 가스 100

            Assert.AreEqual(1000, forecast.ExpectedPurchasedPower);
            Assert.AreEqual(100, forecast.ExpectedPurchasedGas);
            Assert.IsTrue(forecast.WillRunShort);

            TestWorld.Invoke(loop, "HandleMonthlyTick");

            Assert.AreEqual(1000, _power.CurrentAvailablePower);
            Assert.AreEqual(0, _power.LastPurchaseShortfall);
            Assert.AreEqual(100, _gas.CurrentAvailableGas);
            Assert.AreEqual(900, _gas.LastPurchaseShortfall);
            Assert.AreEqual(0, _economy.CurrentMoney);
        }

        [Test]
        public void 전력조차_다_못_사면_가스는_0()
        {
            var loop = _world.CreateManager<GameLoopManager>();
            loop.RegisterBuilding(_world.CreateBuilding(_world.CreateData(BuildingType.Facility, maintenance: 10000), 0, 0));
            var forecast = _world.CreateManager<SettlementForecaster>();
            MakeDeficit();
            SetMoney(2005); // 17,005 − 10,000 = 7,005 → 전력 700kW, 5원 남아 가스 0

            Assert.AreEqual(700, forecast.ExpectedPurchasedPower);
            Assert.AreEqual(0, forecast.ExpectedPurchasedGas);

            TestWorld.Invoke(loop, "HandleMonthlyTick");

            Assert.AreEqual(700, _power.CurrentAvailablePower);
            Assert.AreEqual(0, _gas.CurrentAvailableGas);
            Assert.AreEqual(5, _economy.CurrentMoney);
        }

        [Test]
        public void 예상_잔액은_지금_잔액_더하기_이번달_수지()
        {
            var forecast = _world.CreateManager<SettlementForecaster>();
            MakeDeficit();
            SetMoney(3000);
            Assert.AreEqual(3000 - 5000, forecast.ExpectedMoneyAfterSettlement);
        }

        [Test]
        public void 구매량을_최대치_대비_비율로_정할_수_있다()
        {
            _power.SetTargetPurchaseRatio(0.25f); // 2,000 × 25%
            Assert.AreEqual(500, _power.TargetPurchasedPower);
            Assert.AreEqual(0.25f, _power.TargetPurchaseRatio, 0.0001f);

            _gas.SetTargetPurchaseRatio(1.5f); // 1을 넘으면 최대치
            Assert.AreEqual(_gas.MaxPurchasableGas, _gas.TargetPurchasedGas);
            _gas.SetTargetPurchaseRatio(0.333f); // 666 → 10 단위 670
            Assert.AreEqual(670, _gas.TargetPurchasedGas);
        }

        [Test]
        public void 씬에_없으면_게임루프가_정산_예보를_추가한다()
        {
            var loop = _world.CreateManager<GameLoopManager>(callStart: true);
            // EditMode에서는 AddComponent해도 Awake가 자동으로 불리지 않으므로 컴포넌트가 붙었는지만 확인
            Assert.IsNotNull(loop.GetComponent<SettlementForecaster>());
        }
    }

    /// <summary>
    /// 사용자 기획 2026-11-24: 전력이 부족하면 무작위가 아니라 외곽 전봇대부터 중앙으로 꺼지고,
    /// 꺼지는 개수 = 부족 비율(필요 대비 %) ÷ 전봇대 1개당 % (올림, 최소 1)
    /// </summary>
    public class PoleBlackoutTests
    {
        private TestWorld _world;
        private PowerManager _power;
        private BuildingData _poleData;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _world.CreateManager<EconomyManager>();
            _power = _world.CreateManager<PowerManager>();
            _poleData = _world.CreateData(BuildingType.Infrastructure, id: "pole", name: "전신주");
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private BuildingInstance Pole(int x, int y) => _world.CreateBuilding(_poleData, x, y, withPole: true);

        /// <summary>필요 전력 1,000kW에 보유 available kW</summary>
        private void Evaluate(List<BuildingInstance> buildings, int available)
        {
            buildings.Add(_world.CreateBuilding(_world.CreateData(BuildingType.Facility, power: 1000), 50, 35));
            TestWorld.SetField(_power, "currentAvailablePower", available);
            _power.RecalculatePowerUsage(buildings);
        }

        private static bool On(BuildingInstance pole) => pole.GetComponent<UtilityPoleInstance>().IsPowered;

        [TestCase(2f, 5)]
        [TestCase(3f, 4)]
        [TestCase(4f, 3)]
        [TestCase(5f, 2)]
        public void 부족_10퍼센트면_전봇대_1개당_퍼센트만큼_꺼진다(float percentPerPole, int expectedOff)
        {
            _power.ShortagePercentPerPole = percentPerPole;
            var poles = Enumerable.Range(0, 10).Select(i => Pole(i * 5, 10)).ToList();
            Evaluate(new List<BuildingInstance>(poles), 900); // (1,000 − 900) / 1,000 = 10%

            Assert.AreEqual(10f, _power.ShortagePercent, 0.001f);
            Assert.AreEqual(expectedOff, _power.BlackoutPoleCount);
            Assert.AreEqual(expectedOff, poles.Count(p => !On(p)));
            Assert.AreEqual(expectedOff, _power.BlackedOutPoles.Count);
        }

        [Test]
        public void 조금이라도_부족하면_최소_1개는_꺼진다()
        {
            _power.ShortagePercentPerPole = 5f;
            var poles = new List<BuildingInstance> { Pole(0, 0), Pole(20, 0), Pole(40, 0) };
            Evaluate(new List<BuildingInstance>(poles), 999); // 0.1% 부족
            Assert.AreEqual(1, poles.Count(p => !On(p)));
        }

        [Test]
        public void 외곽부터_중앙_쪽으로_꺼진다()
        {
            _power.ShortagePercentPerPole = 5f;
            var left = Pole(0, 10);
            var midLeft = Pole(10, 10);
            var center = Pole(20, 10);
            var midRight = Pole(30, 10);
            var right = Pole(40, 10);
            var all = new List<BuildingInstance> { center, midRight, left, right, midLeft }; // 목록 순서와 무관해야 함

            Evaluate(new List<BuildingInstance>(all), 900); // 10% → 2개
            Assert.IsFalse(On(left));
            Assert.IsFalse(On(right));
            Assert.IsTrue(On(midLeft) && On(center) && On(midRight));

            Evaluate(new List<BuildingInstance>(all), 800); // 20% → 4개, 중앙만 남음
            Assert.IsFalse(On(midLeft) || On(midRight));
            Assert.IsTrue(On(center));
        }

        [Test]
        public void 정전_순서는_외곽부터이고_거리가_같으면_아래_왼쪽부터()
        {
            var a = Pole(0, 0);
            var b = Pole(20, 0);
            var c = Pole(0, 20);
            var d = Pole(20, 20);
            var mid = Pole(10, 10);
            Evaluate(new List<BuildingInstance> { mid, d, c, b, a }, 1000);

            var order = _power.GetBlackoutOrder().Select(p => p.GetComponent<BuildingInstance>()).ToList();
            CollectionAssert.AreEqual(new[] { a, b, c, d, mid }, order);
        }

        [Test]
        public void 같은_상태면_다시_계산해도_같은_전봇대가_꺼진다()
        {
            _power.ShortagePercentPerPole = 3f;
            var poles = Enumerable.Range(0, 8).Select(i => Pole(i * 4, i % 3)).ToList();
            Evaluate(new List<BuildingInstance>(poles), 880);
            var first = poles.Where(p => !On(p)).ToList();

            for (int i = 0; i < 5; i++)
            {
                var shuffled = new List<BuildingInstance>(poles);
                shuffled.Reverse();
                Evaluate(shuffled, 880);
                CollectionAssert.AreEquivalent(first, poles.Where(p => !On(p)).ToList());
            }
        }

        [Test]
        public void 전력이_0이면_전봇대가_모두_꺼진다()
        {
            _power.ShortagePercentPerPole = 5f;
            var poles = Enumerable.Range(0, 30).Select(i => Pole(i * 2, 0)).ToList();
            Evaluate(new List<BuildingInstance>(poles), 0);
            Assert.IsTrue(poles.All(p => !On(p)));
        }

        [Test]
        public void 전력이_충분하면_모두_켜진다()
        {
            var poles = new List<BuildingInstance> { Pole(0, 0), Pole(10, 0) };
            Evaluate(new List<BuildingInstance>(poles), 900);
            Evaluate(new List<BuildingInstance>(poles), 1000);
            Assert.IsTrue(poles.All(On));
            Assert.AreEqual(0, _power.BlackoutPoleCount);
        }

        /// <summary>
        /// 밸런스 확인용 (보고서의 표 A와 같은 배치): 중간 단계 마을 주택 30채(y=0 한 줄) + 전봇대 4개(반경 4), 부족 10%일 때 전기가 끊기는 주택 수
        /// </summary>
        [TestCase(2f, 4, 30)]
        [TestCase(3f, 4, 30)]
        [TestCase(4f, 3, 21)]
        [TestCase(5f, 2, 12)]
        public void 밸런스_중간_단계_마을_부족_10퍼센트(float percentPerPole, int poleOff, int darkHouses)
        {
            _power.ShortagePercentPerPole = percentPerPole;
            var houseData = _world.CreateData(BuildingType.Housing, id: "house", capacity: 10);
            var houses = Enumerable.Range(0, 30).Select(x => _world.CreateBuilding(houseData, x, 0)).ToList();
            var poles = new List<BuildingInstance> { Pole(4, 1), Pole(13, 1), Pole(22, 1), Pole(29, 1) };

            var all = new List<BuildingInstance>(houses);
            all.AddRange(poles);
            Evaluate(all, 900);

            Assert.AreEqual(poleOff, poles.Count(p => !On(p)));
            Assert.AreEqual(darkHouses, houses.Count(h => !_power.IsHousingPowered(h)));
        }
    }
}

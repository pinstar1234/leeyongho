using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.Gas;
using InfraTown.Power;
using InfraTown.Satisfaction;
using InfraTown.Save;
using InfraTown.Services;
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
        public void 경고_중_유지비가_큰_건물을_철거하면_바로_경고가_꺼진다()
        {
            // 2026-11-26 점검: 철거는 돈이 바뀌지 않지만 수용 인구 재계산 알림으로 예보가 바로 다시 계산되는지
            _world.CreateManager<InfraTown.Population.PopulationManager>();
            var forecast = _world.CreateManager<SettlementForecaster>(); // 씬에 있는 것처럼 (GameLoopManager가 따로 붙이지 않음)
            var loop = _world.CreateManager<InfraTown.Core.GameLoopManager>(callStart: true);
            var expensive = _world.CreateBuilding(_world.CreateData(InfraTown.Building.BuildingType.Facility, id: "landfill", maintenance: 20000), 10, 10);
            loop.RegisterBuilding(expensive);
            SetWeek(3);
            TestWorld.Invoke(forecast, "Start"); // 알림 구독
            Assert.IsTrue(forecast.IsWarning, $"유지비 {forecast.ExpectedMaintenance:N0}원 > 지원금");

            TestWorld.Invoke(expensive, "OnDestroy"); // 철거 알림 (DestroyBuilding·Destroy와 같은 경로)

            Assert.AreEqual(0, forecast.ExpectedMaintenance);
            Assert.IsFalse(forecast.IsWarning, "다음 주를 기다리지 않고 바로 꺼짐");
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
    /// 꺼지는 개수 = 부족해지면 1개 + 부족 비율이 1개당 %(기본 5%)만큼 늘 때마다 1개 = 1 + 내림(부족% ÷ 1개당 %)
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

        [TestCase(2f, 6)] // 1 + 10/2
        [TestCase(3f, 4)] // 1 + 3
        [TestCase(4f, 3)] // 1 + 2
        [TestCase(5f, 3)] // 1 + 2
        public void 부족_10퍼센트면_1개_더하기_퍼센트마다_1개씩_꺼진다(float percentPerPole, int expectedOff)
        {
            _power.ShortagePercentPerPole = percentPerPole;
            var poles = Enumerable.Range(0, 10).Select(i => Pole(i * 5, 10)).ToList();
            Evaluate(new List<BuildingInstance>(poles), 900); // (1,000 − 900) / 1,000 = 10%

            Assert.AreEqual(10f, _power.ShortagePercent, 0.001f);
            Assert.AreEqual(expectedOff, _power.BlackoutPoleCount);
            Assert.AreEqual(expectedOff, poles.Count(p => !On(p)));
            Assert.AreEqual(expectedOff, _power.BlackedOutPoles.Count);
        }

        [TestCase(951, 1)] // 4.9%
        [TestCase(950, 2)] // 5%
        [TestCase(901, 2)] // 9.9%
        [TestCase(900, 3)] // 10%
        [TestCase(851, 3)] // 14.9%
        [TestCase(850, 4)] // 15%
        public void 처음_부족하면_1개_그뒤_5퍼센트마다_1개씩_더_꺼진다(int available, int expectedOff)
        {
            _power.ShortagePercentPerPole = 5f;
            var poles = Enumerable.Range(0, 20).Select(i => Pole(i * 3, 10)).ToList();
            Evaluate(new List<BuildingInstance>(poles), available);
            Assert.AreEqual(expectedOff, _power.BlackoutPoleCount, $"부족 {_power.ShortagePercent:F2}%");
            Assert.AreEqual(expectedOff, poles.Count(p => !On(p)));
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

            Evaluate(new List<BuildingInstance>(all), 950); // 5% → 2개 (양 끝, 거리가 같으면 왼쪽부터)
            Assert.IsFalse(On(left));
            Assert.IsFalse(On(right));
            Assert.IsTrue(On(midLeft) && On(center) && On(midRight));

            Evaluate(new List<BuildingInstance>(all), 850); // 15% → 4개, 중앙만 남음
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
        [TestCase(5f, 3, 21)]
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

    /// <summary>
    /// 사용자 결정 2026-11-24: 모든 건물은 켜진 전봇대 공급 범위(한 칸이라도 닿으면) 안에 있어야 전기가 들어온다.
    /// 꺼진 주택은 −15, 꺼진 시설은 기능 정지(좋은 만족도 영향·접근성 제공 안 함, 설치로는 인정), 불쾌 시설의 나쁜 영향은 그대로
    /// </summary>
    public class BuildingPowerRangeTests
    {
        private TestWorld _world;
        private PowerManager _power;
        private SatisfactionManager _satisfaction;
        private BuildingData _poleData;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _power = _world.CreateManager<PowerManager>();
            _satisfaction = _world.CreateManager<SatisfactionManager>();
            _poleData = _world.CreateData(BuildingType.Infrastructure, id: "pole", name: "전신주");
            _poleData.powerSupplyRadius = 1; // 좁은 반경으로 범위 안/밖을 나눔
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private BuildingInstance Pole(int x, int y) => _world.CreateBuilding(_poleData, x, y, withPole: true);
        private BuildingInstance House(int x, int y) => _world.CreateBuilding(_world.CreateData(BuildingType.Housing, id: "house", capacity: 10), x, y);

        /// <summary>전력 넉넉히(1,000kW) 보유한 상태로 판정</summary>
        private void Evaluate(List<BuildingInstance> buildings)
        {
            TestWorld.SetField(_power, "currentAvailablePower", 1000);
            _power.RecalculatePowerUsage(buildings);
        }

        private float SatisfactionOf(BuildingInstance house, List<BuildingInstance> buildings)
        {
            _satisfaction.RecalculateSatisfaction(buildings);
            return house.GetComponent<HousingInstance>().CurrentSatisfaction;
        }

        [Test]
        public void 전봇대가_없으면_전력이_넉넉해도_모든_건물이_꺼진다()
        {
            var house = House(0, 0);
            var facility = _world.CreateBuilding(_world.CreateData(BuildingType.Facility), 3, 0);
            Evaluate(new List<BuildingInstance> { house, facility });

            Assert.IsFalse(_power.IsPowerShortage);
            Assert.IsFalse(_power.IsBuildingPowered(house));
            Assert.IsFalse(_power.IsBuildingPowered(facility));
        }

        [Test]
        public void 반경_안은_켜지고_밖은_꺼진다()
        {
            var near = House(1, 0);   // 전봇대(0,0)에서 1칸
            var far = House(2, 0);    // 2칸 → 반경 1 밖
            Evaluate(new List<BuildingInstance> { Pole(0, 0), near, far });

            Assert.IsTrue(_power.IsBuildingPowered(near));
            Assert.IsFalse(_power.IsBuildingPowered(far));
        }

        [Test]
        public void 큰_건물은_한_칸이라도_범위_안이면_켜진다()
        {
            _poleData.powerSupplyRadius = 4;
            var large = _world.CreateBuilding(_world.CreateData(BuildingType.Facility, size: BuildingSizeType.Large), 10, 10); // 10~13칸
            var touching = Pole(17, 10); // 오른쪽 끝 칸(13)에서 4칸
            Evaluate(new List<BuildingInstance> { large, touching });
            Assert.IsTrue(_power.IsBuildingPowered(large), "기준 칸(10)에서는 7칸이지만 13번 칸이 반경 안");

            var tooFar = Pole(18, 10);
            Evaluate(new List<BuildingInstance> { large, tooFar });
            Assert.IsFalse(_power.IsBuildingPowered(large));
        }

        [Test]
        public void 꺼진_전봇대의_범위는_전기를_주지_않는다()
        {
            _poleData.powerSupplyRadius = 4;
            var house = House(0, 0);
            var pole = Pole(1, 1);
            var buildings = new List<BuildingInstance> { house, pole, _world.CreateBuilding(_world.CreateData(BuildingType.Facility, power: 1000), 50, 35) };
            TestWorld.SetField(_power, "currentAvailablePower", 900); // 10% 부족 → 전봇대 1개뿐이라 그것이 꺼짐
            _power.RecalculatePowerUsage(buildings);

            Assert.IsFalse(pole.GetComponent<UtilityPoleInstance>().IsPowered);
            Assert.IsFalse(_power.IsBuildingPowered(pole));
            Assert.IsFalse(_power.IsBuildingPowered(house));
        }

        [Test]
        public void 전기가_꺼진_시설은_좋은_만족도_영향을_주지_않는다()
        {
            var house = House(0, 0);
            var pole = Pole(1, 0);   // 반경 1 → x 0~2
            var park = _world.CreateBuilding(_world.CreateData(BuildingType.Facility, id: "park", impact: 10f, radius: 3), 3, 0); // 전봇대에서 2칸 → 꺼짐
            var buildings = new List<BuildingInstance> { house, pole, park };
            Evaluate(buildings);

            Assert.IsFalse(_power.IsBuildingPowered(park));
            Assert.AreEqual(70f, SatisfactionOf(house, buildings), 0.001f, "꺼진 시설의 +10은 반영 안 됨");

            buildings.Add(Pole(4, 0)); // 시설에도 전기
            Evaluate(buildings);
            Assert.IsTrue(_power.IsBuildingPowered(park));
            Assert.AreEqual(80f, SatisfactionOf(house, buildings), 0.001f);
        }

        [Test]
        public void 불쾌_시설의_나쁜_영향은_전기가_꺼져도_그대로()
        {
            var house = House(0, 0);
            var pole = Pole(1, 0);
            var farm = _world.CreateBuilding(_world.CreateData(BuildingType.Nimbys, id: "farm", impact: -20f, radius: 3), 3, 0);
            var buildings = new List<BuildingInstance> { house, pole, farm };
            Evaluate(buildings);

            Assert.IsFalse(_power.IsBuildingPowered(farm));
            Assert.AreEqual(50f, SatisfactionOf(house, buildings), 0.001f);
        }

        [Test]
        public void 전봇대_범위_밖_주택은_15점_감점()
        {
            var near = House(0, 0);
            var far = House(10, 0);
            var buildings = new List<BuildingInstance> { near, far, Pole(1, 0) };
            Evaluate(buildings);

            Assert.AreEqual(70f, SatisfactionOf(near, buildings), 0.001f);
            Assert.AreEqual(55f, far.GetComponent<HousingInstance>().CurrentSatisfaction, 0.001f);
        }

        [Test]
        public void 전기가_꺼진_병원은_설치로는_인정되지만_접근성을_주지_않는다()
        {
            var service = _world.CreateManager<ServiceAccessibilityManager>();
            var house = House(0, 0);
            var hospital = _world.CreateBuilding(_world.CreateData(BuildingType.Essential, id: "hospital", name: "병원"), 5, 0);
            var buildings = new List<BuildingInstance> { house, Pole(1, 0), hospital };
            Evaluate(buildings);
            service.RecalculateServiceMetrics(buildings);

            Assert.IsTrue(service.HasService(ServiceType.Hospital), "필수 시설 미설치 감점·승리 조건에는 설치로 인정");
            Assert.IsFalse(service.HasPoweredService(ServiceType.Hospital));
            Assert.IsTrue(service.IsServiceUnpowered(ServiceType.Hospital));
            Assert.AreEqual(ServiceAccessibilityManager.DefaultUninstalledTime, service.GetAccessTime(house, ServiceType.Hospital));
            Assert.AreEqual(0f, service.GetAccessBonus(house, ServiceType.Hospital));
            StringAssert.Contains("병원에 전기가 들어오지 않아", service.GetServiceComplaintSummary());

            buildings.Add(Pole(4, 0)); // 병원에도 전기
            Evaluate(buildings);
            service.RecalculateServiceMetrics(buildings);
            Assert.IsTrue(service.HasPoweredService(ServiceType.Hospital));
            Assert.Greater(service.GetAccessBonus(house, ServiceType.Hospital), 0f);
        }

        [Test]
        public void 전력_변화로_전봇대가_꺼지면_게임루프가_접근성과_만족도를_다시_계산한다()
        {
            var service = _world.CreateManager<ServiceAccessibilityManager>();
            _world.CreateManager<EconomyManager>();
            var loop = _world.CreateManager<GameLoopManager>(callStart: true); // OnPowerDataChanged 구독
            _poleData.powerSupplyRadius = 10;
            loop.RegisterBuilding(House(0, 0));
            loop.RegisterBuilding(Pole(1, 0));
            loop.RegisterBuilding(_world.CreateBuilding(_world.CreateData(BuildingType.Essential, id: "hospital", name: "병원", power: 100), 3, 0));
            TestWorld.SetField(_power, "currentAvailablePower", 1000);
            _power.ShortagePercentPerPole = 5f; // 다시 판정 → OnPowerDataChanged
            Assert.IsTrue(service.HasPoweredService(ServiceType.Hospital));

            TestWorld.SetField(_power, "currentAvailablePower", 50); // 부족 → 하나뿐인 전봇대가 꺼짐
            _power.ShortagePercentPerPole = 5f;
            Assert.IsFalse(service.HasPoweredService(ServiceType.Hospital), "전봇대가 꺼졌는데 병원이 계속 운영 중");
            TestWorld.Invoke(loop, "OnDestroy");
        }
    }
}

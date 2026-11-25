using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.Gas;
using InfraTown.Population;
using InfraTown.Power;
using InfraTown.Satisfaction;
using InfraTown.Save;
using InfraTown.TimeSystem;

namespace InfraTown.Tests
{
    /// <summary>
    /// 계절 소비량과 가스 (사용자 기획 2026-11-24):
    /// 최저 소비량 = 인구 100명당 10, 최고 = 최저의 2.5배, 봄·가을 = 중간(1.75배).
    /// 전력은 여름 최고·겨울 최저(건물 소비에 더해짐), 가스는 겨울 최고·여름 최저. 가스도 매달 구매량을 정해 산다.
    /// </summary>
    public class SeasonalResourceTests
    {
        private TestWorld _world;
        private TimeManager _time;
        private PopulationManager _population;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _time = _world.CreateManager<TimeManager>();
            _population = _world.CreateManager<PopulationManager>();
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private void SetPopulation(int amount) =>
            _population.ApplySaveState(new PopulationSaveState { currentPopulation = amount });

        private void SetMonth(int month) => TestWorld.SetField(_time, "currentMonth", month);

        /// <summary>현재 달에서 month월 1주차까지 실제로 시간을 진행 (계절 변경 이벤트가 나가도록)</summary>
        private void AdvanceToMonth(int month)
        {
            while (_time.CurrentMonth != month || _time.CurrentWeek != 1)
                TestWorld.Invoke(_time, "AdvanceOneWeek");
        }

        // ── 공통 계산 ──

        [TestCase(Season.Summer, 100)]
        [TestCase(Season.Winter, 40)]
        [TestCase(Season.Spring, 70)]
        [TestCase(Season.Autumn, 70)]
        public void 전력_계절_소비량_인구400명(Season season, int expected)
        {
            Assert.AreEqual(expected, SeasonUtil.GetSeasonalDemand(400, 10f, season, Season.Summer, 2.5f));
        }

        [TestCase(Season.Winter, 100)]
        [TestCase(Season.Summer, 40)]
        [TestCase(Season.Spring, 70)]
        [TestCase(Season.Autumn, 70)]
        public void 가스_계절_소비량_인구400명(Season season, int expected)
        {
            Assert.AreEqual(expected, SeasonUtil.GetSeasonalDemand(400, 10f, season, Season.Winter, 2.5f));
        }

        [Test]
        public void 최저_소비량은_인구에_비례한다()
        {
            Assert.AreEqual(0, SeasonUtil.GetSeasonalDemand(0, 10f, Season.Winter, Season.Summer, 2.5f));
            Assert.AreEqual(10, SeasonUtil.GetSeasonalDemand(100, 10f, Season.Winter, Season.Summer, 2.5f));
            Assert.AreEqual(50, SeasonUtil.GetSeasonalDemand(500, 10f, Season.Winter, Season.Summer, 2.5f));
            Assert.AreEqual(125, SeasonUtil.GetSeasonalDemand(500, 10f, Season.Summer, Season.Summer, 2.5f), "최고 = 최저 × 2.5");
        }

        // ── 전력 ──

        [Test]
        public void 전력_필요량은_건물_소비에_인구_계절_소비가_더해진다()
        {
            var power = _world.CreateManager<PowerManager>(callStart: true);
            SetPopulation(400);
            power.RecalculatePowerUsage(new List<BuildingInstance>
            {
                _world.CreateBuilding(_world.CreateData(BuildingType.Housing, power: 25), 0, 0),
            });

            // 1월 = 겨울 = 전력 최저
            Assert.AreEqual(25, power.BuildingPowerUsage);
            Assert.AreEqual(40, power.SeasonalPowerUsage);
            Assert.AreEqual(65, power.CurrentUsedPower);
            Assert.AreEqual((40, 100), (power.MinSeasonalDemand, power.MaxSeasonalDemand));
        }

        [Test]
        public void 여름이_되면_전력_사용량이_최고가_되고_알림이_간다()
        {
            var power = _world.CreateManager<PowerManager>(callStart: true);
            SetPopulation(400);
            int lastUsed = -1;
            power.OnPowerDataChanged += (used, available) => lastUsed = used;

            AdvanceToMonth(3);
            Assert.AreEqual(70, power.CurrentUsedPower, "봄 = 중간");
            AdvanceToMonth(6);
            Assert.AreEqual(100, power.CurrentUsedPower, "여름 = 최고");
            Assert.AreEqual(100, lastUsed);
            AdvanceToMonth(12);
            Assert.AreEqual(40, power.CurrentUsedPower, "겨울 = 최저");
        }

        [Test]
        public void 인구가_늘면_전력_사용량도_는다()
        {
            var power = _world.CreateManager<PowerManager>(callStart: true);
            SetPopulation(100);
            Assert.AreEqual(10, power.CurrentUsedPower);
            SetPopulation(300);
            Assert.AreEqual(30, power.CurrentUsedPower);
        }

        [Test]
        public void 다음달_부족량은_다음달_계절로_계산한다()
        {
            var power = _world.CreateManager<PowerManager>(callStart: true);
            SetPopulation(400);
            power.SetTargetPurchasedPower(80);

            SetMonth(5); // 다음 달 6월 = 여름
            Assert.AreEqual(100, power.NextMonthExpectedUsage);
            Assert.AreEqual(20, power.NextMonthShortage);

            SetMonth(11); // 다음 달 12월 = 겨울
            Assert.AreEqual(40, power.NextMonthExpectedUsage);
            Assert.AreEqual(0, power.NextMonthShortage);
        }

        // ── 가스 ──

        [Test]
        public void 가스_사용량은_겨울_최고_여름_최저()
        {
            var gas = _world.CreateManager<GasManager>(callStart: true);
            SetPopulation(400);
            Assert.AreEqual(100, gas.CurrentUsedGas, "1월 = 겨울 = 최고");
            Assert.AreEqual((40, 100), (gas.MinDemand, gas.MaxDemand));

            AdvanceToMonth(6);
            Assert.AreEqual(40, gas.CurrentUsedGas, "여름 = 최저");
            AdvanceToMonth(9);
            Assert.AreEqual(70, gas.CurrentUsedGas, "가을 = 중간");
        }

        [Test]
        public void 가스_구매량은_10단위로_정하고_다음_정산부터_적용된다()
        {
            var economy = _world.CreateManager<EconomyManager>();
            var gas = _world.CreateManager<GasManager>(callStart: true);
            int afterStart = economy.CurrentMoney;
            int bought = gas.CurrentAvailableGas;
            Assert.AreEqual(gas.TargetPurchasedGas, bought, "시작할 때 첫 달 가스를 구매");

            int target = -1, cost = -1;
            gas.OnTargetGasChanged += (t, c) => { target = t; cost = c; };
            gas.SetTargetPurchasedGas(234);
            Assert.AreEqual(230, gas.TargetPurchasedGas);
            Assert.AreEqual((230, 2300), (target, cost));
            Assert.AreEqual(bought, gas.CurrentAvailableGas, "이번 달 보유량은 그대로");

            gas.ProcessMonthlyGasPurchase();
            Assert.AreEqual(230, gas.CurrentAvailableGas);
            Assert.AreEqual(afterStart - 2300, economy.CurrentMoney);

            gas.SetTargetPurchasedGas(99999);
            Assert.AreEqual(gas.MaxPurchasableGas, gas.TargetPurchasedGas);
        }

        [Test]
        public void 돈이_모자라면_가진_돈만큼만_가스를_산다()
        {
            var economy = _world.CreateManager<EconomyManager>();
            economy.ApplySaveState(new EconomySaveState { money = 105 }); // 단가 10원 → 목표 100 중 10만, 5원 남음
            var gas = _world.CreateManager<GasManager>(callStart: true);
            Assert.AreEqual(10, gas.CurrentAvailableGas);
            Assert.AreEqual(90, gas.LastPurchaseShortfall);
            Assert.AreEqual(5, economy.CurrentMoney);
        }

        [Test]
        public void 가스가_모자라면_부족_비율만큼_마을_만족도가_떨어진다()
        {
            var gas = _world.CreateManager<GasManager>(callStart: true);
            SetPopulation(400); // 겨울 100
            TestWorld.SetField(gas, "currentAvailableGas", 100);
            Assert.AreEqual(0f, gas.GetGasShortageSatisfactionPenalty());

            TestWorld.SetField(gas, "currentAvailableGas", 50);
            Assert.IsTrue(gas.IsGasShortage);
            Assert.AreEqual(15f, gas.GetGasShortageSatisfactionPenalty(), 0.001f);

            TestWorld.SetField(gas, "currentAvailableGas", 0);
            Assert.AreEqual(30f, gas.GetGasShortageSatisfactionPenalty(), 0.001f);

            // 만족도에 반영 (인구 초과 감점 등 다른 감점은 같으므로 차이만 비교)
            var satisfaction = _world.CreateManager<SatisfactionManager>();
            var house = _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 1000), 0, 0);
            var buildings = new List<BuildingInstance> { house };
            _population.RecalculateCapacity(buildings);
            TestWorld.SetField(gas, "currentAvailableGas", 100);
            satisfaction.RecalculateSatisfaction(buildings);
            float enough = satisfaction.CurrentAverageSatisfaction;
            TestWorld.SetField(gas, "currentAvailableGas", 0);
            satisfaction.RecalculateSatisfaction(buildings);
            Assert.AreEqual(30f, enough - satisfaction.CurrentAverageSatisfaction, 0.001f);
        }

        [Test]
        public void 다음달_가스_부족량은_다음달_계절로_계산한다()
        {
            var gas = _world.CreateManager<GasManager>(callStart: true);
            SetPopulation(400);
            gas.SetTargetPurchasedGas(80);
            SetMonth(11); // 다음 달 겨울
            Assert.AreEqual(100, gas.NextMonthExpectedUsage);
            Assert.AreEqual(20, gas.NextMonthShortage);
            SetMonth(5); // 다음 달 여름
            Assert.AreEqual(0, gas.NextMonthShortage);
        }

        // ── 게임 루프 ──

        [Test]
        public void 월간_정산에서_지원금_유지비_전력_다음에_가스를_산다()
        {
            var economy = _world.CreateManager<EconomyManager>();
            var power = _world.CreateManager<PowerManager>();
            var gas = _world.CreateManager<GasManager>();
            var loop = _world.CreateManager<GameLoopManager>(callStart: true);
            TestWorld.Invoke(power, "Start");
            TestWorld.Invoke(gas, "Start");

            gas.SetTargetPurchasedGas(300);
            economy.ApplySaveState(new EconomySaveState { money = 0, requestedSubsidy = 15000 });

            for (int i = 0; i < 4; i++) TestWorld.Invoke(_time, "AdvanceOneWeek");

            Assert.AreEqual(300, gas.CurrentAvailableGas, "지원금이 먼저 들어와서 가스 구매 성공");
            Assert.AreEqual(power.TargetPurchasedPower, power.CurrentAvailablePower);
            TestWorld.Invoke(loop, "OnDestroy");
        }

        [Test]
        public void 가스_매니저가_없으면_게임루프가_붙인다()
        {
            var loop = _world.CreateManager<GameLoopManager>(callStart: true);
            Assert.IsNotNull(loop.GetComponent<GasManager>());
            TestWorld.Invoke(loop, "OnDestroy");
        }

        [Test]
        public void 가스_상태를_저장하고_되돌린다()
        {
            var gas = _world.CreateManager<GasManager>(callStart: true);
            SetPopulation(200);
            gas.SetTargetPurchasedGas(150);
            TestWorld.SetField(gas, "currentAvailableGas", 77);
            var saved = gas.GetSaveState();
            Assert.AreEqual((150, 77), (saved.targetPurchasedGas, saved.currentAvailableGas));

            gas.ApplySaveState(new GasSaveState { targetPurchasedGas = 40, currentAvailableGas = 10 });
            Assert.AreEqual((40, 10), (gas.TargetPurchasedGas, gas.CurrentAvailableGas));
            Assert.AreEqual(50, gas.CurrentUsedGas, "사용량은 인구·계절로 다시 계산 (200명 겨울)");
        }
    }
}

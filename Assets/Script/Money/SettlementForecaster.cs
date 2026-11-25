using System;
using UnityEngine;
using InfraTown.Core;
using InfraTown.Gas;
using InfraTown.Population;
using InfraTown.Power;
using InfraTown.TimeSystem;

namespace InfraTown.Economy
{
    /// <summary>
    /// 월 정산 예보 (사용자 기획 2026-11-24).
    /// 정산 2주 전(warningWeeksBefore)부터 이번 정산의 지원금과 나갈 돈(유지비 + 전력 구매 + 가스 구매)을 계산해
    /// 지원금 &lt; 나갈 돈이면 경고(IsWarning, OnWarningChanged)를 낸다. 플레이어는 그동안 종합 현황의 지원금·전력·가스 슬라이더로 고칠 수 있다.
    /// 고치지 않으면 정산 때 정해 둔 구매량 안에서 전력을 먼저 살 수 있는 만큼 사고, 남은 돈으로 가스를 산다
    /// (PowerManager/GasManager의 부분 구매, 순서는 GameLoopManager.HandleMonthlyTick).
    ///
    /// UI(팀원 1) 연결: OnWarningChanged(true/false)로 경고 표시·숨김, OnForecastChanged 때 Expected* 값으로 내역 표시.
    /// 경고는 게임을 멈추지 않는다. UI가 없으면 경고가 켜질 때 콘솔에만 나온다.
    /// 씬에 없으면 GameLoopManager.Start가 자기 오브젝트에 추가한다.
    /// </summary>
    public class SettlementForecaster : MonoBehaviour
    {
        public static SettlementForecaster Instance { get; private set; }

        [Tooltip("정산 몇 주 전부터 예보·경고를 하는지 (사용자 기획: 2주 — 한 달 4주 중 3·4주차)")]
        [SerializeField] private int warningWeeksBefore = 2;

        public int WarningWeeksBefore => warningWeeksBefore;

        /// <summary>다음 월 정산까지 남은 주 수 (4주차 = 1, 3주차 = 2 … 정산은 4주차가 끝날 때)</summary>
        public int WeeksUntilSettlement => TimeManager.Instance != null
            ? TimeManager.WeeksPerMonth - TimeManager.Instance.CurrentWeek + 1
            : TimeManager.WeeksPerMonth;

        /// <summary>예보 기간인지 (정산 warningWeeksBefore주 전부터)</summary>
        public bool IsForecastPeriod => WeeksUntilSettlement <= warningWeeksBefore;

        // ─── 이번 정산 예상 (지금 설정 그대로 정산하면) ───

        /// <summary>들어올 지원금 (신청액, 인구 기준 최대치를 넘지 않음)</summary>
        public int ExpectedSubsidy
        {
            get
            {
                var eco = EconomyManager.Instance;
                return eco != null ? Mathf.Min(eco.GetRequestedSubsidy(), eco.GetMaxEligibleSubsidy()) : 0;
            }
        }

        /// <summary>나갈 유지비</summary>
        public int ExpectedMaintenance => GameLoopManager.Instance != null ? GameLoopManager.Instance.GetTotalMaintenanceCost() : 0;

        /// <summary>전력 구매비 (정해 둔 다음 달 구매량 × 단가)</summary>
        public int ExpectedPowerCost => PowerManager.Instance != null ? PowerManager.Instance.MonthlyPurchaseCost : 0;

        /// <summary>가스 구매비 (정해 둔 다음 달 구매량 × 단가)</summary>
        public int ExpectedGasCost => GasManager.Instance != null ? GasManager.Instance.MonthlyPurchaseCost : 0;

        /// <summary>나갈 돈 합계 (유지비 + 전력 + 가스)</summary>
        public int ExpectedOutgoing => ExpectedMaintenance + ExpectedPowerCost + ExpectedGasCost;

        /// <summary>지원금 − 나갈 돈 (음수면 이번 달 적자)</summary>
        public int ExpectedMonthlyBalance => ExpectedSubsidy - ExpectedOutgoing;

        /// <summary>지원금이 나갈 돈보다 적은지 (경고 조건)</summary>
        public bool IsDeficit => ExpectedSubsidy < ExpectedOutgoing;

        /// <summary>경고 중인지 (예보 기간 + 적자)</summary>
        public bool IsWarning { get; private set; }

        /// <summary>정산 뒤 예상 잔액 (지금 잔액 + 지원금 − 나갈 돈, 음수면 그만큼 모자라 일부를 못 삼)</summary>
        public int ExpectedMoneyAfterSettlement => CurrentMoney + ExpectedMonthlyBalance;

        /// <summary>잔액까지 합쳐도 모자라 전력·가스를 정한 만큼 다 사지 못하는지</summary>
        public bool WillRunShort => ExpectedPurchasedPower < TargetPower || ExpectedPurchasedGas < TargetGas;

        /// <summary>지금 설정대로 정산하면 실제로 살 수 있는 전력 (전력 먼저)</summary>
        public int ExpectedPurchasedPower => Simulate().power;

        /// <summary>지금 설정대로 정산하면 실제로 살 수 있는 가스 (전력을 사고 남은 돈으로)</summary>
        public int ExpectedPurchasedGas => Simulate().gas;

        /// <summary>예보 값이 다시 계산됨 (예보 기간이 아니어도 날짜·돈·슬라이더·건물이 바뀌면 발생)</summary>
        public event Action OnForecastChanged;

        /// <summary>경고가 켜지거나 꺼짐 (true = 정산 전 적자 경고)</summary>
        public event Action<bool> OnWarningChanged;

        private int CurrentMoney => EconomyManager.Instance != null ? EconomyManager.Instance.CurrentMoney : 0;
        private int TargetPower => PowerManager.Instance != null ? PowerManager.Instance.TargetPurchasedPower : 0;
        private int TargetGas => GasManager.Instance != null ? GasManager.Instance.TargetPurchasedGas : 0;

        // 구독한 매니저 (해제용)
        private TimeManager _time;
        private EconomyManager _economy;
        private PowerManager _power;
        private GasManager _gas;
        private PopulationManager _population;

        private void Awake()
        {
            if (Instance != null && Instance != this) { SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;
        }

        private void Start()
        {
            _time = TimeManager.Instance;
            if (_time != null) _time.OnDateChanged += HandleDateChanged;

            _economy = EconomyManager.Instance;
            if (_economy != null)
            {
                _economy.OnMoneyChanged += HandleTwoInts;
                _economy.OnSubsidyAmountChanged += HandleTwoInts;
            }

            _power = PowerManager.Instance;
            if (_power != null) _power.OnTargetPowerChanged += HandleTwoInts;

            _gas = GasManager.Instance;
            if (_gas != null) _gas.OnTargetGasChanged += HandleTwoInts;

            // 건물 설치·철거·복원은 모두 수용 인구 재계산(OnPopulationChanged)을 거치므로 유지비 변화도 여기서 잡힌다
            _population = PopulationManager.Instance;
            if (_population != null) _population.OnPopulationChanged += HandleTwoInts;

            Refresh();
        }

        private void OnDestroy()
        {
            if (_time != null) _time.OnDateChanged -= HandleDateChanged;
            if (_economy != null)
            {
                _economy.OnMoneyChanged -= HandleTwoInts;
                _economy.OnSubsidyAmountChanged -= HandleTwoInts;
            }
            if (_power != null) _power.OnTargetPowerChanged -= HandleTwoInts;
            if (_gas != null) _gas.OnTargetGasChanged -= HandleTwoInts;
            if (_population != null) _population.OnPopulationChanged -= HandleTwoInts;
            if (Instance == this) Instance = null;
        }

        private void HandleDateChanged(int year, int month, int week) => Refresh();

        private void HandleTwoInts(int a, int b) => Refresh();

        /// <summary>예보를 다시 계산하고 경고 상태가 바뀌었으면 알림 (이벤트로 자동 호출, 테스트에서 직접 호출 가능)</summary>
        public void Refresh()
        {
            bool warning = IsForecastPeriod && IsDeficit;
            if (warning != IsWarning)
            {
                IsWarning = warning;
                if (warning)
                {
                    Debug.LogWarning($"[SettlementForecaster] 정산 {WeeksUntilSettlement}주 전: 지원금 {ExpectedSubsidy:N0}원 < 나갈 돈 {ExpectedOutgoing:N0}원 " +
                                     $"(유지비 {ExpectedMaintenance:N0}, 전력 {ExpectedPowerCost:N0}, 가스 {ExpectedGasCost:N0}). " +
                                     $"그대로 두면 전력 {ExpectedPurchasedPower}/{TargetPower}kW, 가스 {ExpectedPurchasedGas}/{TargetGas}를 삽니다.");
                }
                OnWarningChanged?.Invoke(warning);
            }
            OnForecastChanged?.Invoke();
        }

        /// <summary>
        /// 정산을 흉내 내 실제로 살 수 있는 양 계산 (GameLoopManager.HandleMonthlyTick 순서와 같음):
        /// 지원금 수령 → 유지비(가진 만큼) → 전력(살 수 있는 만큼) → 남은 돈으로 가스
        /// </summary>
        private (int power, int gas) Simulate()
        {
            int money = CurrentMoney + ExpectedSubsidy;
            money -= Mathf.Min(ExpectedMaintenance, money);

            int power = BuyUnits(ref money, TargetPower, PowerManager.Instance != null ? PowerManager.Instance.PricePerPowerUnit : 0);
            int gas = BuyUnits(ref money, TargetGas, GasManager.Instance != null ? GasManager.Instance.PricePerGasUnit : 0);
            return (power, gas);
        }

        private static int BuyUnits(ref int money, int maxUnits, int price)
        {
            if (maxUnits <= 0) return 0;
            if (price <= 0) return maxUnits;
            int units = Mathf.Min(maxUnits, Mathf.Max(0, money) / price);
            money -= units * price;
            return units;
        }
    }
}

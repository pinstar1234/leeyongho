using System;
using UnityEngine;
using InfraTown.Core;
using InfraTown.Population;

namespace InfraTown.Economy
{
    public class EconomyManager : MonoBehaviour
    {
        public static EconomyManager Instance { get; private set; }

        [Header("재정 기본 설정")]
        [SerializeField] private int initialMoney = 10000; // 초기 자금 10,000원

        public int CurrentMoney { get; private set; }

        private int _requestedSubsidyAmount = 15000; // 이번 정산에 신청된 지원금 = 플레이어가 정한 금액을 지금 최대치로 자른 값 (기본 15,000원)

        // 신청 지원금 회복 (사용자 기획 2026-11-24): 인구가 줄어 최대치가 줄면 신청액도 줄지만(의도),
        // 플레이어가 정한 금액은 기억해 뒀다가 인구가 다시 늘면 그 금액까지 되돌린다.
        // 슬라이더를 최대치에 뒀으면(_followMaxSubsidy) 인구가 늘어 최대치가 커질 때 계속 최대치를 따라간다
        private int _desiredSubsidyAmount = 15000;
        private bool _followMaxSubsidy;
        private bool _notifyingSubsidy; // 알림을 받은 UI가 슬라이더 최대치를 줄이며 되부른 Set 호출은 플레이어 입력이 아니므로 무시

        /// <summary>플레이어가 슬라이더로 정한 신청 지원금 (최대치가 줄어 깎여도 기억하는 값)</summary>
        public int DesiredSubsidyAmount => _desiredSubsidyAmount;

        /// <summary>신청 지원금을 최대치로 정해 둬서 인구가 늘면 최대치를 따라가는지</summary>
        public bool IsSubsidyFollowingMax => _followMaxSubsidy;

        public event Action<int, int> OnMoneyChanged; // (현재 잔액, 변동액)
        public event Action<int, int> OnSubsidyAmountChanged; // (신청 지원금, 최대 지원 가능액)
        public event Action<int, int, int> OnMonthlySettled; // (지원금 수입, 유지비 실제 지출, 미납 유지비)

        /// <summary>최근 월 정산의 지원금 수입</summary>
        public int LastMonthlyIncome { get; private set; }

        /// <summary>최근 월 정산에서 실제로 지출된 유지비</summary>
        public int LastMaintenancePaid { get; private set; }

        /// <summary>최근 월 정산에서 잔액 부족으로 내지 못한 유지비 (0이면 정상)</summary>
        public int LastUnpaidMaintenance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { InfraTown.Core.SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;

            CurrentMoney = initialMoney;
        }

        private void Start()
        {
            OnMoneyChanged?.Invoke(CurrentMoney, 0);
            UpdateGovernmentSubsidyLimit();
        }

        /// <summary>
        /// 인수가 없는 경우: PopulationManager의 현재 인구수를 가져와 한도 갱신
        /// </summary>
        public void UpdateGovernmentSubsidyLimit()
        {
            int currentPop = PopulationManager.Instance != null ? PopulationManager.Instance.CurrentPopulation : 0;
            UpdateGovernmentSubsidyLimit(currentPop);
        }

        /// <summary>
        /// 인수가 1개인 경우: 전달받은 인구수를 기준으로 한도 갱신 (오버로딩)
        /// </summary>
        public void UpdateGovernmentSubsidyLimit(int currentPopulation)
        {
            int maxEligible = 15000 + (currentPopulation * 50);

            // 최대치를 따라가면 최대치, 아니면 정한 금액을 최대 한도로 자름 (인구가 다시 늘면 정한 금액까지 회복)
            _requestedSubsidyAmount = _followMaxSubsidy ? maxEligible : Mathf.Clamp(_desiredSubsidyAmount, 0, maxEligible);

            NotifySubsidy(maxEligible);
        }

        /// <summary>
        /// 플레이어가 UI Slider를 통해 직접 설정한 수령 지원금 지정.
        /// 최대치로 정하면 이후 인구가 늘어 최대치가 커질 때 계속 따라간다
        /// </summary>
        public void SetGovernmentSubsidyAmount(int amount)
        {
            if (_notifyingSubsidy) return; // 슬라이더 최대치가 줄며 생긴 되부름 (플레이어 입력 아님)

            int maxEligible = GetMaxEligibleSubsidy();
            _requestedSubsidyAmount = Mathf.Clamp(amount, 0, maxEligible);
            _desiredSubsidyAmount = _requestedSubsidyAmount;
            _followMaxSubsidy = _requestedSubsidyAmount >= maxEligible;

            NotifySubsidy(maxEligible);
        }

        private void NotifySubsidy(int maxEligible)
        {
            _notifyingSubsidy = true;
            try { OnSubsidyAmountChanged?.Invoke(_requestedSubsidyAmount, maxEligible); }
            finally { _notifyingSubsidy = false; }
        }

        public int GetRequestedSubsidy() => _requestedSubsidyAmount;

        // ─── 세이브 ───

        public InfraTown.Save.EconomySaveState GetSaveState() => new InfraTown.Save.EconomySaveState
        {
            money = CurrentMoney,
            requestedSubsidy = _requestedSubsidyAmount,
            desiredSubsidy = _desiredSubsidyAmount,
            followMaxSubsidy = _followMaxSubsidy,
            lastMonthlyIncome = LastMonthlyIncome,
            lastMaintenancePaid = LastMaintenancePaid,
            lastUnpaidMaintenance = LastUnpaidMaintenance,
        };

        /// <summary>저장된 잔액·신청 지원금으로 되돌림 (인구를 먼저 되돌린 뒤 호출해야 지원금 한도가 맞음)</summary>
        public void ApplySaveState(InfraTown.Save.EconomySaveState s)
        {
            if (s == null) return;
            CurrentMoney = Mathf.Max(0, s.money);
            _requestedSubsidyAmount = Mathf.Max(0, s.requestedSubsidy);
            // 신청액은 정한 금액을 자른 값이라 정한 금액 ≥ 신청액 (이 필드가 없던 저장 파일은 0 → 신청액으로)
            _desiredSubsidyAmount = Mathf.Max(_requestedSubsidyAmount, s.desiredSubsidy);
            _followMaxSubsidy = s.followMaxSubsidy;
            LastMonthlyIncome = s.lastMonthlyIncome;
            LastMaintenancePaid = s.lastMaintenancePaid;
            LastUnpaidMaintenance = s.lastUnpaidMaintenance;
            OnMoneyChanged?.Invoke(CurrentMoney, 0);
            UpdateGovernmentSubsidyLimit();
        }

        /// <summary>
        /// 인구수에 따라 자동으로 계산되는 최대 수령 가능 지원금 (기본 15,000원 + 인구당 50원)
        /// </summary>
        public int GetMaxEligibleSubsidy()
        {
            int currentPop = PopulationManager.Instance != null ? PopulationManager.Instance.CurrentPopulation : 0;
            return 15000 + (currentPop * 50);
        }

        /// <summary>
        /// 자금 차감 시도 (건설, 전기 구매 등)
        /// </summary>
        public bool TrySpendMoney(int amount)
        {
            if (amount <= 0) return true;

            if (CurrentMoney >= amount)
            {
                CurrentMoney -= amount;
                OnMoneyChanged?.Invoke(CurrentMoney, -amount);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 가진 돈 안에서 최대 maxUnits개까지 단가 pricePerUnit으로 구매하고 산 개수를 돌려준다 (전력·가스 부분 구매, 사용자 기획 2026-11-24).
        /// 돈이 모자라면 살 수 있는 만큼만 사고, 전혀 못 사면 0
        /// </summary>
        public int BuyUnits(int maxUnits, int pricePerUnit)
        {
            if (maxUnits <= 0) return 0;
            if (pricePerUnit <= 0) return maxUnits;
            int units = Mathf.Min(maxUnits, CurrentMoney / pricePerUnit);
            if (units > 0) TrySpendMoney(units * pricePerUnit);
            return units;
        }

        /// <summary>
        /// 자금 추가 (수입)
        /// </summary>
        public void AddMoney(int amount)
        {
            if (amount <= 0) return;
            CurrentMoney += amount;
            OnMoneyChanged?.Invoke(CurrentMoney, amount);
        }

        /// <summary>
        /// 매월 초일 정산 처리 (매개변수 없이 내부 자동 연산 처리)
        /// </summary>
        public void ProcessMonthlySettlement()
        {
            int currentPop = PopulationManager.Instance != null ? PopulationManager.Instance.CurrentPopulation : 0;
            int maintenance = GameLoopManager.Instance != null ? GameLoopManager.Instance.GetTotalMaintenanceCost() : 0;

            ProcessMonthlySettlement(currentPop, maintenance);
        }

        /// <summary>
        /// 매월 초일 정산 처리: 지원금 수령 후 건물 유지비 지출.
        /// 잔액이 모자라면 가진 만큼만 내고(잔액 0) 나머지는 LastUnpaidMaintenance로 기록.
        /// </summary>
        public void ProcessMonthlySettlement(int population, int totalMaintenanceCost)
        {
            int maxEligible = GetMaxEligibleSubsidy();
            int actualSubsidy = Mathf.Min(_requestedSubsidyAmount, maxEligible);

            AddMoney(actualSubsidy);

            int maintenance = Mathf.Max(0, totalMaintenanceCost);
            int paid = Mathf.Min(maintenance, CurrentMoney);
            int unpaid = maintenance - paid;

            if (paid > 0)
            {
                CurrentMoney -= paid;
                OnMoneyChanged?.Invoke(CurrentMoney, -paid);
            }

            LastMonthlyIncome = actualSubsidy;
            LastMaintenancePaid = paid;
            LastUnpaidMaintenance = unpaid;
            OnMonthlySettled?.Invoke(actualSubsidy, paid, unpaid);

            Debug.Log($"[EconomyManager] 월초 정산: 지원금 +{actualSubsidy:N0} 원, 유지비 -{paid:N0} 원 (현재 잔액: {CurrentMoney:N0} 원)");

            if (unpaid > 0)
            {
                Debug.LogWarning($"[EconomyManager] 잔액 부족으로 유지비 {unpaid:N0} 원을 내지 못했습니다.");
            }
        }
    }
}
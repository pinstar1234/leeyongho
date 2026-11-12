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

        private int _requestedSubsidyAmount = 15000; // 기본 신청 지원금 (기본 15,000원)

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
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
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

            // 현재 신청 지원금이 최대 한도를 넘지 않도록 조정
            _requestedSubsidyAmount = Mathf.Clamp(_requestedSubsidyAmount, 0, maxEligible);

            OnSubsidyAmountChanged?.Invoke(_requestedSubsidyAmount, maxEligible);
        }

        /// <summary>
        /// 플레이어가 UI Slider를 통해 직접 설정한 수령 지원금 지정
        /// </summary>
        public void SetGovernmentSubsidyAmount(int amount)
        {
            int maxEligible = GetMaxEligibleSubsidy();
            _requestedSubsidyAmount = Mathf.Clamp(amount, 0, maxEligible);

            OnSubsidyAmountChanged?.Invoke(_requestedSubsidyAmount, maxEligible);
        }

        public int GetRequestedSubsidy() => _requestedSubsidyAmount;

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
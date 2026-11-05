using System;
using UnityEngine;
using InfraTown.Population;
using InfraTown.Satisfaction;

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
            float currentSatisfaction = SatisfactionManager.Instance != null ? SatisfactionManager.Instance.CurrentAverageSatisfaction : 70f;

            ProcessMonthlySettlement(currentPop, Mathf.RoundToInt(currentSatisfaction));
        }

        /// <summary>
        /// 매월 초일 정산 처리 (인자 수신 버전)
        /// </summary>
        public void ProcessMonthlySettlement(int population, int satisfaction)
        {
            int maxEligible = GetMaxEligibleSubsidy();
            int actualSubsidy = Mathf.Min(_requestedSubsidyAmount, maxEligible);

            AddMoney(actualSubsidy);

            Debug.Log($"[EconomyManager] 월초 지원금 정산 완료: +{actualSubsidy:N0} 원 (현재 잔액: {CurrentMoney:N0} 원)");
        }
    }
}
using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Satisfaction;

namespace InfraTown.Economy
{
    public class EconomyManager : MonoBehaviour
    {
        public static EconomyManager Instance { get; private set; }

        [Header("기본 자금 설정")]
        [SerializeField] private int currentMoney = 10000; // 시작 자금 10,000원

        public int CurrentMoney => currentMoney;

        // 정부 지원금 전용 수입원 참조
        private GovernmentSubsidySource _subsidySource;
        private List<IFinancialSource> _incomeSources = new List<IFinancialSource>();

        // 잔액 변동 이벤트 (현재 잔액, 변동액)
        public event Action<int, int> OnMoneyChanged;

        // 월별 정산 이벤트 (총 수입, 총 유지비)
        public event Action<int, int> OnMonthlySettlement;

        // 지원금 설정 변경 이벤트 (현재 설정 지원금, 최대 지원 가능 지원금)
        public event Action<int, int> OnSubsidyAmountChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // 정부 지원금 수입원 기본 등록
            _subsidySource = new GovernmentSubsidySource();
            RegisterIncomeSource(_subsidySource);
        }

        /// <summary>
        /// 추가 수입원(세금, 인프라 이용료 등) 등록
        /// </summary>
        public void RegisterIncomeSource(IFinancialSource source)
        {
            if (!_incomeSources.Contains(source))
            {
                _incomeSources.Add(source);
            }
        }

        /// <summary>
        /// 인구수 변동 시 호출하여 최대 지원금 한계 수치를 재계산
        /// </summary>
        public void UpdateGovernmentSubsidyLimit(int population)
        {
            if (_subsidySource != null)
            {
                _subsidySource.RecalculateMaxSubsidy(population);
                OnSubsidyAmountChanged?.Invoke(_subsidySource.RequestedSubsidy, _subsidySource.MaxEligibleSubsidy);
            }
        }

        /// <summary>
        /// UI 등에서 플레이어가 직접 지원 수령액을 변경할 때 호출하는 함수
        /// </summary>
        public void SetGovernmentSubsidyAmount(int amount)
        {
            if (_subsidySource != null)
            {
                _subsidySource.SetRequestedSubsidy(amount);
                OnSubsidyAmountChanged?.Invoke(_subsidySource.RequestedSubsidy, _subsidySource.MaxEligibleSubsidy);
                Debug.Log($"[EconomyManager] 정부 지원금 수령액 조정됨: {GetRequestedSubsidy()}원 / 최대 한계: {GetMaxEligibleSubsidy()}원");
            }
        }

        public int GetRequestedSubsidy() => _subsidySource != null ? _subsidySource.RequestedSubsidy : 0;
        public int GetMaxEligibleSubsidy() => _subsidySource != null ? _subsidySource.MaxEligibleSubsidy : 0;

        /// <summary>
        /// 자금 차감 (건설 등)
        /// </summary>
        public bool TrySpendMoney(int amount)
        {
            if (currentMoney >= amount)
            {
                currentMoney -= amount;
                OnMoneyChanged?.Invoke(currentMoney, -amount);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 자금 추가
        /// </summary>
        public void AddMoney(int amount)
        {
            if (amount <= 0) return;
            currentMoney += amount;
            OnMoneyChanged?.Invoke(currentMoney, amount);
        }

        /// <summary>
        /// 매월(30일) 호출되는 재정 정산 함수
        /// </summary>
        public void ProcessMonthlySettlement(int population, int totalMaintenanceCost)
        {
            float avgSatisfaction = SatisfactionManager.Instance != null
                ? SatisfactionManager.Instance.CurrentAverageSatisfaction
                : 70f;

            // 한계치 재계산
            UpdateGovernmentSubsidyLimit(population);

            int totalIncome = 0;

            foreach (var source in _incomeSources)
            {
                totalIncome += source.CalculateMonthlyIncome(population, avgSatisfaction);
            }

            int netChange = totalIncome - totalMaintenanceCost;
            currentMoney += netChange;

            OnMoneyChanged?.Invoke(currentMoney, netChange);
            OnMonthlySettlement?.Invoke(totalIncome, totalMaintenanceCost);

            Debug.Log($"[월별 정산 완료] 수입: +{totalIncome}원 | 유지비: -{totalMaintenanceCost}원 | 최종 잔액: {currentMoney}원");
        }
    }
}
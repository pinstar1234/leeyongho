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

        // 돈 변경 시 UI 알림 이벤트 (현재 잔액, 변동 금액)
        public event Action<int, int> OnMoneyChanged;

        // 월별 정산 내역 전달 이벤트 (수입, 지출)
        public event Action<int, int> OnMonthlySettlement;

        private List<IFinancialSource> _incomeSources = new List<IFinancialSource>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // 기본 수입원(정부 지원금) 등록
            RegisterIncomeSource(new GovernmentSubsidySource());
        }

        /// <summary>
        /// 정부 지원금 외에 추가 수입원을 등록할 때 사용
        /// </summary>
        public void RegisterIncomeSource(IFinancialSource source)
        {
            if (!_incomeSources.Contains(source))
            {
                _incomeSources.Add(source);
            }
        }

        /// <summary>
        /// 자금 차감 (건설 등 비용 지출 시 사용)
        /// </summary>
        public bool TrySpendMoney(int amount)
        {
            if (currentMoney >= amount)
            {
                currentMoney -= amount;
                OnMoneyChanged?.Invoke(currentMoney, -amount);
                return true;
            }
            return false; // 잔액 부족
        }

        /// <summary>
        /// 자금 추가 (건물 파괴 환불 등)
        /// </summary>
        public void AddMoney(int amount)
        {
            if (amount <= 0) return;
            currentMoney += amount;
            OnMoneyChanged?.Invoke(currentMoney, amount);
        }

        /// <summary>
        /// 1달마다 호출되는 정산 함수 (TimeManager 또는 GameLoop에서 호출)
        /// </summary>
        /// <param name="population">현재 인구수</param>
        /// <param name="totalMaintenanceCost">건물들의 총 월별 유지비</param>
        public void ProcessMonthlySettlement(int population, int totalMaintenanceCost)
        {
            float avgSatisfaction = SatisfactionManager.Instance != null
                ? SatisfactionManager.Instance.CurrentAverageSatisfaction
                : 70f;

            int totalIncome = 0;

            // 등록된 모든 수입원 연산
            foreach (var source in _incomeSources)
            {
                totalIncome += source.CalculateMonthlyIncome(population, avgSatisfaction);
            }

            int netChange = totalIncome - totalMaintenanceCost;
            currentMoney += netChange;

            OnMoneyChanged?.Invoke(currentMoney, netChange);
            OnMonthlySettlement?.Invoke(totalIncome, totalMaintenanceCost);

            Debug.Log($"[월별 정산 완료] 수입: +{totalIncome}원 / 지출(유지비): -{totalMaintenanceCost}원 / 최종 잔액: {currentMoney}원");
        }
    }
}
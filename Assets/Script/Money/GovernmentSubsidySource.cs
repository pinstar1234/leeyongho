using UnityEngine;

namespace InfraTown.Economy
{
    public class GovernmentSubsidySource : IFinancialSource
    {
        public string SourceName => "정부 지원금";

        private const int BaseSubsidy = 15000;       // 기본 지원금 15,000원
        private const int SubsidyPerPopulation = 50;  // 인구 1명당 추가 지원금 50원

        /// <summary>
        /// 인구수에 따라 자동으로 계산되는 이번 달 정부 지원금 최대 한계값
        /// </summary>
        public int MaxEligibleSubsidy { get; private set; }

        /// <summary>
        /// 플레이어가 슬라이더/UI 등을 통해 직접 요청/설정한 실제 수령 지원금액
        /// </summary>
        public int RequestedSubsidy { get; private set; }

        public GovernmentSubsidySource()
        {
            RecalculateMaxSubsidy(0);
            RequestedSubsidy = MaxEligibleSubsidy; // 기본값은 한계치 100% 수령
        }

        /// <summary>
        /// 현재 인구 기반 최대 지원 한계금액 계산
        /// </summary>
        public void RecalculateMaxSubsidy(int population)
        {
            MaxEligibleSubsidy = BaseSubsidy + (population * SubsidyPerPopulation);

            // 플레이어가 기존 설정한 금액이 한계치를 초과하지 않도록 보정
            if (RequestedSubsidy > MaxEligibleSubsidy)
            {
                RequestedSubsidy = MaxEligibleSubsidy;
            }
        }

        /// <summary>
        /// UI를 통해 플레이어가 직접 수령받을 지원금액을 조정할 때 호출 (0 ~ MaxEligibleSubsidy)
        /// </summary>
        public void SetRequestedSubsidy(int amount)
        {
            RequestedSubsidy = Mathf.Clamp(amount, 0, MaxEligibleSubsidy);
        }

        /// <summary>
        /// 매월 정산 시 실제 지급될 수입액 반환
        /// </summary>
        public int CalculateMonthlyIncome(int population, float satisfaction)
        {
            RecalculateMaxSubsidy(population);
            return RequestedSubsidy;
        }
    }
}
using UnityEngine;

namespace InfraTown.Economy
{
    public class GovernmentSubsidySource : IFinancialSource
    {
        public string SourceName => "정부 기본 지원금";

        private const int BaseSubsidy = 15000;      // 기본 지원금 15,000원
        private const int SubsidyPerPopulation = 50; // 인구 1명당 추가 지원금 50원

        public int CalculateMonthlyIncome(int population, float satisfaction)
        {
            // 기본 지원금 + (인구수 * 명당 지원금)
            int totalSubsidy = BaseSubsidy + (population * SubsidyPerPopulation);
            return totalSubsidy;
        }
    }
}
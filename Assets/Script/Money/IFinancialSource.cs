namespace InfraTown.Economy
{
    public enum FinancialTransactionType
    {
        GovernmentSubsidy, // 정부 지원금
        Tax,               // 세금
        InfrastructureFee, // 인프라/시설 이용료
        MaintenanceCost,   // 건물 유지비
        ConstructionCost   // 건설 비용
    }

    /// <summary>
    /// 추후 다양한 수입원(세금, 관광 수입 등) 추가를 위한 인터페이스
    /// </summary>
    public interface IFinancialSource
    {
        string SourceName { get; }
        int CalculateMonthlyIncome(int population, float satisfaction);
    }
}
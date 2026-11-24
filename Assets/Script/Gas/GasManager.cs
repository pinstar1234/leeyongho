using System;
using UnityEngine;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.Population;
using InfraTown.TimeSystem;

namespace InfraTown.Gas
{
    /// <summary>
    /// 가스 (사용자 기획 2026-11-24). 전력과 같은 방식으로 매달 구매량을 정해 사 오고,
    /// 사용량은 인구와 계절로 정해진다: 최저 = 인구 100명당 10, 겨울 최고(최저의 2.5배), 여름 최저, 봄·가을 중간.
    /// 전봇대 같은 공급망은 없고, 모자라면 마을 전체 만족도가 부족 비율만큼 떨어진다.
    /// 다음 달 구매량은 종합 현황 슬라이더(UI 팀원) → SetTargetPurchasedGas, 다음 월간 정산부터 적용.
    /// 씬에 없으면 GameLoopManager가 자기 오브젝트에 추가한다.
    /// </summary>
    public class GasManager : MonoBehaviour
    {
        public static GasManager Instance { get; private set; }

        [Header("가스 구매 설정 (1단위당 가격, 임시값)")]
        [SerializeField] private int pricePerGasUnit = 10;
        [Tooltip("매달 자동 구매할 가스량 (임시값)")]
        [SerializeField] private int targetPurchasedGas = 100;

        [Tooltip("월 구매량 최대치 (슬라이더 최대값)")]
        [SerializeField] private int maxPurchasableGas = 2000;

        [Tooltip("월 구매량 조절 단위")]
        [SerializeField] private int purchaseStep = 10;

        [Tooltip("게임 시작 시 첫 달 가스를 바로 구매 (끄면 첫 월간 정산 전까지 가스 0)")]
        [SerializeField] private bool purchaseOnStart = true;

        [Header("계절 소비량 (사용자 기획 2026-11-24)")]
        [Tooltip("최저 소비량: 인구 100명당")]
        [SerializeField] private float demandPer100Population = 10f;

        [Tooltip("최고 소비량 = 최저 × 이 배율")]
        [SerializeField] private float peakMultiplier = 2.5f;

        [Tooltip("가장 많이 쓰는 계절 (정반대 계절이 최저)")]
        [SerializeField] private Season peakSeason = Season.Winter;

        [Header("가스 부족 만족도 감점 (임시값, 전력과 같은 방식)")]
        [Tooltip("가스가 전혀 없을 때 마을 전체 만족도 감점. 일부만 모자라면 부족 비율만큼")]
        [SerializeField] private float totalOutagePenalty = 30f;

        [Header("현재 가스 현황")]
        [SerializeField] private int currentAvailableGas = 0; // 이번 달 보유량
        [SerializeField] private int currentUsedGas = 0;      // 지금 필요량

        public int CurrentAvailableGas => currentAvailableGas;
        public int CurrentUsedGas => currentUsedGas;
        public int TargetPurchasedGas => targetPurchasedGas;
        public int PricePerGasUnit => pricePerGasUnit;
        public int MaxPurchasableGas => maxPurchasableGas;
        public int PurchaseStep => purchaseStep;
        public float DemandPer100Population => demandPer100Population;
        public float PeakMultiplier => peakMultiplier;
        public Season PeakSeason => peakSeason;

        /// <summary>다음 월간 정산에 드는 가스 구매 비용 (목표량 × 단가)</summary>
        public int MonthlyPurchaseCost => targetPurchasedGas * pricePerGasUnit;

        /// <summary>지금 인구의 최저 소비량 (여름)</summary>
        public int MinDemand => GetDemand(SeasonUtil.Opposite(peakSeason));

        /// <summary>지금 인구의 최고 소비량 (겨울)</summary>
        public int MaxDemand => GetDemand(peakSeason);

        /// <summary>다음 달 계절 기준 예상 사용량</summary>
        public int NextMonthExpectedUsage => GetDemand(TimeManager.Instance != null ? TimeManager.Instance.NextMonthSeason : CurrentSeason);

        /// <summary>다음 달 목표량으로 다음 달 예상 사용량을 감당하지 못하는 양 (0이면 충분)</summary>
        public int NextMonthShortage => Mathf.Max(0, NextMonthExpectedUsage - targetPurchasedGas);

        /// <summary>필요량이 보유량보다 많은 상태</summary>
        public bool IsGasShortage => currentUsedGas > currentAvailableGas;

        public event Action<int, int> OnGasDataChanged;   // (사용량, 보유량)
        public event Action<int, int> OnTargetGasChanged; // (다음 달 구매량, 월 구매 비용)

        private bool _subscribedToMonthlyTick;
        private TimeManager _time;
        private PopulationManager _population;

        private static Season CurrentSeason => TimeManager.Instance != null ? TimeManager.Instance.CurrentSeason : Season.Spring;

        private void Awake()
        {
            if (Instance != null && Instance != this) { SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;
        }

        private void Start()
        {
            _time = TimeManager.Instance;
            if (_time != null)
            {
                _time.OnSeasonChanged += HandleSeasonChanged;

                // GameLoopManager가 있으면 월 정산 순서(지원금 → 유지비 → 전력 → 가스)를 그쪽에서 관리
                if (GameLoopManager.Instance == null)
                {
                    _time.OnMonthlyTick += ProcessMonthlyGasPurchase;
                    _subscribedToMonthlyTick = true;
                }
            }

            _population = PopulationManager.Instance;
            if (_population != null) _population.OnPopulationChanged += HandlePopulationChanged;

            RecalculateGasUsage(false);

            if (purchaseOnStart)
            {
                ProcessMonthlyGasPurchase();
            }
            else
            {
                NotifyChanged();
            }
        }

        private void OnDestroy()
        {
            if (_time != null)
            {
                _time.OnSeasonChanged -= HandleSeasonChanged;
                if (_subscribedToMonthlyTick) _time.OnMonthlyTick -= ProcessMonthlyGasPurchase;
            }
            if (_population != null) _population.OnPopulationChanged -= HandlePopulationChanged;
            if (Instance == this) Instance = null;
        }

        /// <summary>그 계절의 소비량 (지금 인구 기준)</summary>
        public int GetDemand(Season season)
        {
            int population = PopulationManager.Instance != null ? PopulationManager.Instance.CurrentPopulation : 0;
            return SeasonUtil.GetSeasonalDemand(population, demandPer100Population, season, peakSeason, peakMultiplier);
        }

        /// <summary>
        /// 다음 월간 정산부터 매달 구매할 가스량 설정 (종합 현황 슬라이더).
        /// 0 ~ maxPurchasableGas, purchaseStep 단위로 맞춘다. 이번 달 보유량은 바뀌지 않는다.
        /// </summary>
        public void SetTargetPurchasedGas(int amount)
        {
            int step = Mathf.Max(1, purchaseStep);
            int clamped = Mathf.Clamp(amount, 0, Mathf.Max(0, maxPurchasableGas));
            targetPurchasedGas = Mathf.RoundToInt((float)clamped / step) * step;
            if (targetPurchasedGas > maxPurchasableGas) targetPurchasedGas -= step;

            OnTargetGasChanged?.Invoke(targetPurchasedGas, MonthlyPurchaseCost);
            NotifyChanged();
        }

        /// <summary>
        /// 이번 달 가스 구매 (목표량 × 단가). 자금이 부족하면 구매 실패 → 보유량 0
        /// </summary>
        public void ProcessMonthlyGasPurchase()
        {
            int totalCost = targetPurchasedGas * pricePerGasUnit;

            if (EconomyManager.Instance != null)
            {
                currentAvailableGas = EconomyManager.Instance.TrySpendMoney(totalCost) ? targetPurchasedGas : 0;
            }

            RecalculateGasUsage(false);
            NotifyChanged();
        }

        /// <summary>
        /// 지금 인구·계절로 사용량을 다시 계산. 값이 바뀌었으면(또는 notifyAlways) OnGasDataChanged
        /// </summary>
        public void RecalculateGasUsage(bool notifyAlways = true)
        {
            int before = currentUsedGas;
            currentUsedGas = GetDemand(CurrentSeason);
            if (notifyAlways || before != currentUsedGas) NotifyChanged();
        }

        /// <summary>
        /// 가스 부족 시 마을 전체 만족도 감점: 가스 0이면 totalOutagePenalty, 일부 부족이면 부족 비율만큼
        /// </summary>
        public float GetGasShortageSatisfactionPenalty()
        {
            if (!IsGasShortage) return 0f;
            if (currentAvailableGas <= 0) return totalOutagePenalty;
            float shortageRatio = 1f - (float)currentAvailableGas / currentUsedGas;
            return totalOutagePenalty * shortageRatio;
        }

        // ─── 세이브 ───

        public InfraTown.Save.GasSaveState GetSaveState() => new InfraTown.Save.GasSaveState
        {
            targetPurchasedGas = targetPurchasedGas,
            currentAvailableGas = currentAvailableGas,
        };

        /// <summary>저장된 이번 달 보유량·다음 달 구매량으로 되돌림 (사용량은 인구·계절로 다시 계산, 시작 시 구매는 덮어씀)</summary>
        public void ApplySaveState(InfraTown.Save.GasSaveState s)
        {
            if (s == null) return;
            targetPurchasedGas = Mathf.Clamp(s.targetPurchasedGas, 0, Mathf.Max(0, maxPurchasableGas));
            currentAvailableGas = Mathf.Max(0, s.currentAvailableGas);
            OnTargetGasChanged?.Invoke(targetPurchasedGas, MonthlyPurchaseCost);
            RecalculateGasUsage(true);
        }

        // ─── 내부 ───

        private void HandleSeasonChanged(Season season) => RecalculateGasUsage(false);

        private void HandlePopulationChanged(int current, int max) => RecalculateGasUsage(false);

        private void NotifyChanged() => OnGasDataChanged?.Invoke(currentUsedGas, currentAvailableGas);
    }
}

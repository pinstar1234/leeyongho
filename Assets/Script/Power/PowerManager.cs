using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.Population;
using InfraTown.TimeSystem;

namespace InfraTown.Power
{
    public class PowerManager : MonoBehaviour
    {
        public static PowerManager Instance { get; private set; }

        [Header("전력 구매 설정 (1kW당 가격)")]
        [SerializeField] private int pricePerPowerUnit = 10;     // 1kW당 10원
        [SerializeField] private int targetPurchasedPower = 200; // 매달 자동 구매할 전력량

        [Tooltip("플레이어가 종합 현황에서 정할 수 있는 월 구매량 최대치 (슬라이더 최대값)")]
        [SerializeField] private int maxPurchasablePower = 2000;

        [Tooltip("월 구매량 조절 단위 (kW)")]
        [SerializeField] private int purchaseStep = 10;

        [Tooltip("게임 시작 시 첫 달 전력을 바로 구매 (끄면 첫 월간 정산 전까지 전력 0)")]
        [SerializeField] private bool purchaseOnStart = true;

        [Header("계절 소비량 (사용자 기획 2026-11-24) — 건물 소비에 더해짐")]
        [Tooltip("인구 기준 최저 소비량: 인구 100명당 (kW)")]
        [SerializeField] private float demandPer100Population = 10f;

        [Tooltip("최고 소비량 = 최저 × 이 배율")]
        [SerializeField] private float peakMultiplier = 2.5f;

        [Tooltip("가장 많이 쓰는 계절 (정반대 계절이 최저)")]
        [SerializeField] private Season peakSeason = Season.Summer;

        [Header("정전 만족도 패널티")]
        [Tooltip("전력이 전혀 없을 때(전면 정전) 마을 전체 만족도 감점")]
        [SerializeField] private float totalBlackoutPenalty = 30f;

        [Tooltip("전봇대 망이 있을 때, 전기가 들어오지 않는 주택 1채의 만족도 감점")]
        [SerializeField] private float unpoweredHousingPenalty = 15f;

        [Header("전봇대 정전 (사용자 기획 2026-11-24) — 외곽부터 중앙으로 꺼짐")]
        [Tooltip("전력이 부족해지면 전봇대 1개가 꺼지고, 부족 비율이 이 %만큼 더 늘 때마다 1개씩 더 꺼진다 (사용자 결정 2026-11-24: 5%). 전력 0이면 전부")]
        [SerializeField] private float shortagePercentPerPole = 5f;

        [Header("현재 전력 현황")]
        [SerializeField] private int currentAvailablePower = 0; // 현재 보유 전력
        [SerializeField] private int currentUsedPower = 0;      // 현재 필요 전력 (건물 + 인구 계절 소비)
        [SerializeField] private int buildingPowerUsage = 0;    // 그중 건물 소비
        [SerializeField] private int seasonalPowerUsage = 0;    // 그중 인구·계절 소비

        public int CurrentAvailablePower => currentAvailablePower;
        public int CurrentUsedPower => currentUsedPower;
        public int TargetPurchasedPower => targetPurchasedPower;
        public int PricePerPowerUnit => pricePerPowerUnit;
        public int MaxPurchasablePower => maxPurchasablePower;
        public int PurchaseStep => purchaseStep;

        /// <summary>다음 월간 정산에 드는 전력 구매 비용 (목표량 × 단가)</summary>
        public int MonthlyPurchaseCost => targetPurchasedPower * pricePerPowerUnit;

        /// <summary>필요 전력 중 건물이 쓰는 양 (BuildingData.requiredPower 합)</summary>
        public int BuildingPowerUsage => buildingPowerUsage;

        /// <summary>필요 전력 중 인구·계절에 따른 양</summary>
        public int SeasonalPowerUsage => seasonalPowerUsage;

        public float DemandPer100Population => demandPer100Population;
        public float PeakMultiplier => peakMultiplier;
        public Season PeakSeason => peakSeason;

        /// <summary>그 계절의 인구 기준 소비량 (지금 인구 기준, 건물 소비 제외)</summary>
        public int GetSeasonalDemand(Season season)
        {
            int population = PopulationManager.Instance != null ? PopulationManager.Instance.CurrentPopulation : 0;
            return SeasonUtil.GetSeasonalDemand(population, demandPer100Population, season, peakSeason, peakMultiplier);
        }

        /// <summary>지금 인구의 계절 최저 소비량 (겨울)</summary>
        public int MinSeasonalDemand => GetSeasonalDemand(SeasonUtil.Opposite(peakSeason));

        /// <summary>지금 인구의 계절 최고 소비량 (여름)</summary>
        public int MaxSeasonalDemand => GetSeasonalDemand(peakSeason);

        /// <summary>다음 달 계절 기준 예상 필요 전력 (건물 + 인구 계절 소비)</summary>
        public int NextMonthExpectedUsage => buildingPowerUsage + GetSeasonalDemand(TimeManager.Instance != null ? TimeManager.Instance.NextMonthSeason : CurrentSeason);

        /// <summary>다음 달 목표량으로 다음 달 예상 필요 전력(계절 반영)을 감당하지 못하는 양 (0이면 충분)</summary>
        public int NextMonthShortage => Mathf.Max(0, NextMonthExpectedUsage - targetPurchasedPower);

        private static Season CurrentSeason => TimeManager.Instance != null ? TimeManager.Instance.CurrentSeason : Season.Spring;

        /// <summary>필요 전력이 보유 전력보다 많은 상태</summary>
        public bool IsPowerShortage => currentUsedPower > currentAvailablePower;

        /// <summary>전봇대가 하나 이상 있어 위치 기반 전력 공급 판정을 하는지 여부</summary>
        public bool HasPowerGrid => _allPoles.Count > 0;

        private List<UtilityPoleInstance> _allPoles = new List<UtilityPoleInstance>();
        private List<UtilityPoleInstance> _blackedOutPoles = new List<UtilityPoleInstance>();

        /// <summary>현재 정전된 전봇대 목록 (꺼진 순서 = 외곽부터, 연출용)</summary>
        public IReadOnlyList<UtilityPoleInstance> BlackedOutPoles => _blackedOutPoles;

        /// <summary>부족 비율 몇 %마다 전봇대 1개가 꺼지는지 (밸런스 조정·테스트용, 바꾸면 바로 다시 판정)</summary>
        public float ShortagePercentPerPole
        {
            get => shortagePercentPerPole;
            set { shortagePercentPerPole = Mathf.Max(0.01f, value); EvaluatePowerSupply(); }
        }

        /// <summary>필요 전력 대비 부족한 비율 (%) — (필요 − 보유) / 필요 × 100, 부족하지 않으면 0</summary>
        public float ShortagePercent => IsPowerShortage && currentUsedPower > 0
            ? (currentUsedPower - currentAvailablePower) * 100f / currentUsedPower
            : 0f;

        /// <summary>
        /// 지금 꺼져야 하는 전봇대 수 (사용자 결정 2026-11-24): 부족해지면 1개, 그 뒤 부족 비율이 shortagePercentPerPole(5%)만큼 늘 때마다 1개씩 더
        /// = 1 + 내림(부족% ÷ 5%) — 부족 0~5% 미만 1개, 5~10% 미만 2개, 10~15% 미만 3개 … 전력 0이면 전부, 부족하지 않으면 0
        /// </summary>
        public int BlackoutPoleCount
        {
            get
            {
                int poles = CountPoles();
                if (!IsPowerShortage || poles == 0) return 0;
                if (currentAvailablePower <= 0) return poles;
                float per = Mathf.Max(0.01f, shortagePercentPerPole);
                int count = 1 + Mathf.FloorToInt(ShortagePercent / per + 0.0001f); // 10% ÷ 5% = 2 → 3개 (부동소수 오차로 1.9999가 1이 되지 않게)
                return Mathf.Clamp(count, 1, poles);
            }
        }

        /// <summary>월 구매 때 돈이 모자라 사지 못한 전력 (kW, 0이면 다 삼)</summary>
        public int LastPurchaseShortfall { get; private set; }

        /// <summary>다음 달 구매량을 최대치 대비 비율 0~1로 (슬라이더를 비율로 쓸 때)</summary>
        public float TargetPurchaseRatio => maxPurchasablePower > 0 ? (float)targetPurchasedPower / maxPurchasablePower : 0f;

        /// <summary>다음 달 구매량을 최대치 대비 비율 0~1로 정함 (purchaseStep 단위로 맞춤)</summary>
        public void SetTargetPurchaseRatio(float ratio) =>
            SetTargetPurchasedPower(Mathf.RoundToInt(Mathf.Clamp01(ratio) * Mathf.Max(0, maxPurchasablePower)));

        public event Action<int, int> OnPowerDataChanged; // (사용 전력, 보유 전력)
        public event Action<int, int> OnTargetPowerChanged; // (다음 달 구매량, 월 구매 비용)

        private bool _subscribedToMonthlyTick;
        private TimeManager _time;
        private PopulationManager _population;

        private void Awake()
        {
            if (Instance != null && Instance != this) { InfraTown.Core.SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;
        }

        private void Start()
        {
            // GameLoopManager가 있으면 월 정산 순서(지원금 → 유지비 → 전력 구매)를 그쪽에서 관리
            _time = TimeManager.Instance;
            if (_time != null)
            {
                // 계절이 바뀌면 인구 계절 소비량이 바뀜
                _time.OnSeasonChanged += HandleSeasonChanged;

                if (GameLoopManager.Instance == null)
                {
                    _time.OnMonthlyTick += ProcessMonthlyPowerPurchase;
                    _subscribedToMonthlyTick = true;
                }
            }

            // 인구가 바뀌면 인구 계절 소비량이 바뀜
            _population = PopulationManager.Instance;
            if (_population != null) _population.OnPopulationChanged += HandlePopulationChanged;

            seasonalPowerUsage = GetSeasonalDemand(CurrentSeason);
            currentUsedPower = buildingPowerUsage + seasonalPowerUsage;

            if (purchaseOnStart)
            {
                ProcessMonthlyPowerPurchase();
            }
        }

        private void OnDestroy()
        {
            if (_time != null)
            {
                _time.OnSeasonChanged -= HandleSeasonChanged;
                if (_subscribedToMonthlyTick) _time.OnMonthlyTick -= ProcessMonthlyPowerPurchase;
            }
            if (_population != null) _population.OnPopulationChanged -= HandlePopulationChanged;
        }

        private void HandleSeasonChanged(Season season) => RefreshSeasonalUsage();

        private void HandlePopulationChanged(int current, int max) => RefreshSeasonalUsage();

        /// <summary>
        /// 지금 인구·계절로 인구 계절 소비량을 다시 계산. 바뀌었으면 정전 판정 + OnPowerDataChanged
        /// </summary>
        public void RefreshSeasonalUsage()
        {
            int seasonal = GetSeasonalDemand(CurrentSeason);
            if (seasonal == seasonalPowerUsage) return;
            seasonalPowerUsage = seasonal;
            currentUsedPower = buildingPowerUsage + seasonalPowerUsage;
            EvaluatePowerSupply();
        }

        /// <summary>
        /// 다음 월간 정산부터 매달 구매할 전력량 설정 (종합 현황 슬라이더).
        /// 0 ~ maxPurchasablePower, purchaseStep 단위로 맞춘다. 이번 달 보유 전력은 바뀌지 않는다.
        /// </summary>
        public void SetTargetPurchasedPower(int amount)
        {
            int step = Mathf.Max(1, purchaseStep);
            int clamped = Mathf.Clamp(amount, 0, Mathf.Max(0, maxPurchasablePower));
            targetPurchasedPower = Mathf.RoundToInt((float)clamped / step) * step;
            if (targetPurchasedPower > maxPurchasablePower) targetPurchasedPower -= step;

            OnTargetPowerChanged?.Invoke(targetPurchasedPower, MonthlyPurchaseCost);
            OnPowerDataChanged?.Invoke(currentUsedPower, currentAvailablePower);
        }

        /// <summary>
        /// 이번 달 전력 구매 (목표량 × 단가). 자금이 모자라면 가진 돈으로 살 수 있는 만큼만 산다 (사용자 기획 2026-11-24, 예전엔 전부 아니면 0).
        /// 월 정산 순서상 가스보다 먼저 사므로, 남은 돈이 가스 구매로 넘어간다
        /// </summary>
        public void ProcessMonthlyPowerPurchase()
        {
            if (EconomyManager.Instance != null)
            {
                currentAvailablePower = EconomyManager.Instance.BuyUnits(targetPurchasedPower, pricePerPowerUnit);
                LastPurchaseShortfall = targetPurchasedPower - currentAvailablePower;
                if (LastPurchaseShortfall > 0)
                {
                    Debug.LogWarning($"[PowerManager] 자금 부족으로 전력 {currentAvailablePower}/{targetPurchasedPower} kW만 구매했습니다.");
                }
            }

            EvaluatePowerSupply();
        }

        // ─── 세이브 ───

        public InfraTown.Save.PowerSaveState GetSaveState() => new InfraTown.Save.PowerSaveState
        {
            targetPurchasedPower = targetPurchasedPower,
            currentAvailablePower = currentAvailablePower,
        };

        /// <summary>저장된 이번 달 보유 전력·다음 달 구매량으로 되돌림 (사용량은 건물에서 다시 계산, 시작 시 구매는 덮어씀)</summary>
        public void ApplySaveState(InfraTown.Save.PowerSaveState s)
        {
            if (s == null) return;
            targetPurchasedPower = Mathf.Clamp(s.targetPurchasedPower, 0, Mathf.Max(0, maxPurchasablePower));
            currentAvailablePower = Mathf.Max(0, s.currentAvailablePower);
            seasonalPowerUsage = GetSeasonalDemand(CurrentSeason);
            currentUsedPower = buildingPowerUsage + seasonalPowerUsage;
            OnTargetPowerChanged?.Invoke(targetPurchasedPower, MonthlyPurchaseCost);
            EvaluatePowerSupply();
        }

        public void RecalculatePowerUsage(IReadOnlyList<BuildingInstance> allBuildings)
        {
            int totalRequired = 0;
            _allPoles.Clear();

            foreach (var building in allBuildings)
            {
                if (building == null || building.Data == null) continue;

                totalRequired += building.Data.requiredPower;

                if (building.TryGetComponent<UtilityPoleInstance>(out var pole))
                {
                    _allPoles.Add(pole);
                }
            }

            buildingPowerUsage = totalRequired;
            seasonalPowerUsage = GetSeasonalDemand(CurrentSeason);
            currentUsedPower = buildingPowerUsage + seasonalPowerUsage;
            EvaluatePowerSupply();
        }

        private void EvaluatePowerSupply()
        {
            RestoreAllPoles();

            // 무작위가 아니라 외곽(전봇대들의 중심에서 먼 것)부터 부족 비율만큼 끈다 (사용자 기획 2026-11-24).
            // 같은 상태면 항상 같은 전봇대가 꺼지므로 재계산·저장/불러오기에도 결과가 같다
            int blackoutCount = BlackoutPoleCount;
            if (blackoutCount > 0)
            {
                var order = GetBlackoutOrder();
                for (int i = 0; i < blackoutCount && i < order.Count; i++)
                {
                    order[i].SetPowerState(false);
                    _blackedOutPoles.Add(order[i]);
                }
            }

            OnPowerDataChanged?.Invoke(currentUsedPower, currentAvailablePower);
        }

        /// <summary>
        /// 정전될 순서의 전봇대 목록: 모든 전봇대 중심(평균 위치)에서 먼 외곽부터 → 중앙 순.
        /// 거리가 같으면 y가 작은 것, 그다음 x가 작은 것부터 (항상 같은 순서)
        /// </summary>
        public List<UtilityPoleInstance> GetBlackoutOrder()
        {
            var poles = new List<UtilityPoleInstance>();
            foreach (var pole in _allPoles) if (pole != null) poles.Add(pole);
            if (poles.Count <= 1) return poles;

            // 정수로 정확히 비교: 전봇대 중심을 반 칸 단위(2x + 크기)로 두고, 평균 대신 합계와 개수를 곱해 비교한다
            long n = poles.Count, sumX = 0, sumY = 0;
            foreach (var p in poles) { var c = DoubledCenter(p); sumX += c.x; sumY += c.y; }

            long DistanceSq(UtilityPoleInstance p)
            {
                var c = DoubledCenter(p);
                long dx = n * c.x - sumX, dy = n * c.y - sumY;
                return dx * dx + dy * dy;
            }

            poles.Sort((a, b) =>
            {
                int byDistance = DistanceSq(b).CompareTo(DistanceSq(a)); // 먼 것 먼저
                if (byDistance != 0) return byDistance;
                int byY = a.GridPos.y.CompareTo(b.GridPos.y);
                return byY != 0 ? byY : a.GridPos.x.CompareTo(b.GridPos.x);
            });
            return poles;
        }

        private static Vector2Int DoubledCenter(UtilityPoleInstance pole) => pole.GridPos * 2 + pole.GridSize;

        private int CountPoles()
        {
            int count = 0;
            foreach (var pole in _allPoles) if (pole != null) count++;
            return count;
        }

        private void RestoreAllPoles()
        {
            foreach (var pole in _blackedOutPoles)
            {
                if (pole != null) pole.SetPowerState(true);
            }
            _blackedOutPoles.Clear();
        }

        /// <summary>
        /// 건물에 전기가 들어오는지 (사용자 결정 2026-11-24): 모든 건물은 켜진 전봇대의 공급 범위 안에 있어야 전기가 들어온다.
        /// 전봇대가 하나도 없거나, 건물이 차지한 칸이 모두 범위 밖이면 꺼진 상태. 전봇대 자신은 자기 상태(IsPowered)를 따른다.
        /// 꺼진 주택은 만족도 감점, 꺼진 시설은 기능 정지(만족도 + 효과·접근성 제공 안 함 — SatisfactionManager·ServiceAccessibilityManager)
        /// </summary>
        public bool IsBuildingPowered(BuildingInstance building)
        {
            if (building == null || building.Data == null) return false;
            if (building.TryGetComponent<UtilityPoleInstance>(out var self)) return self.IsPowered;

            foreach (var pole in _allPoles)
            {
                if (pole == null || !pole.IsPowered) continue;
                if (IsInSupplyRange(pole, building)) return true;
            }
            return false;
        }

        /// <summary>
        /// 건물이 차지한 칸 중 한 칸이라도 전봇대 공급 반경(체비셰프 거리) 안에 들어오는지 (사용자 결정 2026-11-24: 한 칸이라도 닿으면)
        /// </summary>
        public static bool IsInSupplyRange(UtilityPoleInstance pole, BuildingInstance building)
        {
            if (pole == null || building == null || building.Data == null) return false;
            Vector2Int pMin = pole.GridPos, pMax = pMin + pole.GridSize - Vector2Int.one;
            Vector2Int bMin = building.OriginGridPos, bMax = bMin + building.Data.GridSize - Vector2Int.one;
            int dx = Mathf.Max(0, Mathf.Max(bMin.x - pMax.x, pMin.x - bMax.x));
            int dy = Mathf.Max(0, Mathf.Max(bMin.y - pMax.y, pMin.y - bMax.y));
            return Mathf.Max(dx, dy) <= pole.SupplyRadius;
        }

        /// <summary>
        /// 주택에 전기가 들어오는지 (주택이 아니면 true — 예전 API, 새 코드는 IsBuildingPowered)
        /// </summary>
        public bool IsHousingPowered(BuildingInstance housing)
        {
            if (housing == null || housing.Data == null || housing.Data.category != BuildingType.Housing) return true;
            return IsBuildingPowered(housing);
        }

        /// <summary>전면 정전인지 (필요 전력이 있는데 보유 전력이 0)</summary>
        public bool IsTotalBlackout => IsPowerShortage && currentAvailablePower <= 0;

        /// <summary>
        /// 마을 전체에 적용되는 정전 감점: 전면 정전(전력 0)일 때만 totalBlackoutPenalty.
        /// 그 밖의 부족은 전봇대가 외곽부터 꺼지고, 전기가 끊긴 주택만 GetHousingPowerPenalty로 감점된다
        /// (예전의 '전봇대가 없으면 부족 비율만큼 마을 전체 감점'은 2026-11-24에 없앰 — 이제 전봇대가 없으면 모든 건물이 꺼진 상태)
        /// </summary>
        public float GetPowerBlackoutSatisfactionPenalty() => IsTotalBlackout ? totalBlackoutPenalty : 0f;

        /// <summary>
        /// 주택 1채에 적용되는 정전 감점: 전기가 들어오지 않으면 unpoweredHousingPenalty (전면 정전 때는 마을 전체 감점만 적용해 겹치지 않게 0)
        /// </summary>
        public float GetHousingPowerPenalty(BuildingInstance housing)
        {
            if (IsTotalBlackout) return 0f;
            return IsHousingPowered(housing) ? 0f : unpoweredHousingPenalty;
        }
    }
}

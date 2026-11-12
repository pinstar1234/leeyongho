using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.TimeSystem;

namespace InfraTown.Power
{
    public class PowerManager : MonoBehaviour
    {
        public static PowerManager Instance { get; private set; }

        [Header("전력 구매 설정 (1kW당 가격)")]
        [SerializeField] private int pricePerPowerUnit = 10;     // 1kW당 10원
        [SerializeField] private int targetPurchasedPower = 200; // 매달 자동 구매할 전력량

        [Tooltip("게임 시작 시 첫 달 전력을 바로 구매 (끄면 첫 월간 정산 전까지 전력 0)")]
        [SerializeField] private bool purchaseOnStart = true;

        [Header("정전 만족도 패널티")]
        [Tooltip("전력이 전혀 없을 때(전면 정전) 마을 전체 만족도 감점")]
        [SerializeField] private float totalBlackoutPenalty = 30f;

        [Tooltip("전봇대 망이 있을 때, 전기가 들어오지 않는 주택 1채의 만족도 감점")]
        [SerializeField] private float unpoweredHousingPenalty = 15f;

        [Header("현재 전력 현황")]
        [SerializeField] private int currentAvailablePower = 0; // 현재 보유 전력
        [SerializeField] private int currentUsedPower = 0;      // 현재 필요 전력

        public int CurrentAvailablePower => currentAvailablePower;
        public int CurrentUsedPower => currentUsedPower;
        public int TargetPurchasedPower => targetPurchasedPower;
        public int PricePerPowerUnit => pricePerPowerUnit;

        /// <summary>필요 전력이 보유 전력보다 많은 상태</summary>
        public bool IsPowerShortage => currentUsedPower > currentAvailablePower;

        /// <summary>전봇대가 하나 이상 있어 위치 기반 전력 공급 판정을 하는지 여부</summary>
        public bool HasPowerGrid => _allPoles.Count > 0;

        private List<UtilityPoleInstance> _allPoles = new List<UtilityPoleInstance>();
        private List<UtilityPoleInstance> _blackedOutPoles = new List<UtilityPoleInstance>();

        /// <summary>현재 정전된 전봇대 목록 (연출용)</summary>
        public IReadOnlyList<UtilityPoleInstance> BlackedOutPoles => _blackedOutPoles;

        public event Action<int, int> OnPowerDataChanged; // (사용 전력, 보유 전력)

        private bool _subscribedToMonthlyTick;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            // GameLoopManager가 있으면 월 정산 순서(지원금 → 유지비 → 전력 구매)를 그쪽에서 관리
            if (GameLoopManager.Instance == null && TimeManager.Instance != null)
            {
                TimeManager.Instance.OnMonthlyTick += ProcessMonthlyPowerPurchase;
                _subscribedToMonthlyTick = true;
            }

            if (purchaseOnStart)
            {
                ProcessMonthlyPowerPurchase();
            }
        }

        private void OnDestroy()
        {
            if (_subscribedToMonthlyTick && TimeManager.Instance != null)
            {
                TimeManager.Instance.OnMonthlyTick -= ProcessMonthlyPowerPurchase;
            }
        }

        public void SetTargetPurchasedPower(int amount)
        {
            targetPurchasedPower = Mathf.Max(0, amount);
            OnPowerDataChanged?.Invoke(currentUsedPower, currentAvailablePower);
        }

        /// <summary>
        /// 이번 달 전력 구매 (목표량 × 단가). 자금이 부족하면 구매 실패 → 보유 전력 0
        /// </summary>
        public void ProcessMonthlyPowerPurchase()
        {
            int totalCost = targetPurchasedPower * pricePerPowerUnit;

            if (EconomyManager.Instance != null)
            {
                currentAvailablePower = EconomyManager.Instance.TrySpendMoney(totalCost) ? targetPurchasedPower : 0;
            }

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

            currentUsedPower = totalRequired;
            EvaluatePowerSupply();
        }

        private void EvaluatePowerSupply()
        {
            RestoreAllPoles();

            if (IsPowerShortage && _allPoles.Count > 0)
            {
                int blackoutCount = UnityEngine.Random.Range(1, Mathf.Min(3, _allPoles.Count + 1));
                List<UtilityPoleInstance> tempPool = new List<UtilityPoleInstance>(_allPoles);

                for (int i = 0; i < blackoutCount; i++)
                {
                    if (tempPool.Count == 0) break;
                    int randomIndex = UnityEngine.Random.Range(0, tempPool.Count);
                    UtilityPoleInstance targetPole = tempPool[randomIndex];

                    targetPole.SetPowerState(false);
                    _blackedOutPoles.Add(targetPole);
                    tempPool.RemoveAt(randomIndex);
                }
            }

            OnPowerDataChanged?.Invoke(currentUsedPower, currentAvailablePower);
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
        /// 주택에 전기가 들어오는지 여부.
        /// 전봇대 망이 없으면 보유 전력이 필요 전력을 감당하는지로 판단하고,
        /// 망이 있으면 정상 전봇대의 공급 반경 안에 있어야 한다.
        /// </summary>
        public bool IsHousingPowered(BuildingInstance housing)
        {
            if (housing == null || housing.Data == null || housing.Data.category != BuildingType.Housing) return true;

            if (!HasPowerGrid)
            {
                return !IsPowerShortage;
            }

            foreach (var pole in _allPoles)
            {
                if (pole == null || !pole.IsPowered) continue;

                if (GetTileDistance(housing.OriginGridPos, pole.GridPos) <= pole.SupplyRadius)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 마을 전체에 적용되는 정전 감점.
        /// 전력 0 → 전면 정전 감점, 전봇대 망이 없을 때 부족분 비율만큼 감점.
        /// (전봇대 망이 있으면 주택별 감점 GetHousingPowerPenalty로 처리)
        /// </summary>
        public float GetPowerBlackoutSatisfactionPenalty()
        {
            if (!IsPowerShortage) return 0f;

            if (currentAvailablePower <= 0) return totalBlackoutPenalty;

            if (!HasPowerGrid)
            {
                float shortageRatio = 1f - (float)currentAvailablePower / currentUsedPower;
                return totalBlackoutPenalty * shortageRatio;
            }

            return 0f;
        }

        /// <summary>
        /// 주택 1채에 적용되는 정전 감점 (전봇대 망이 있고 일부 전력이라도 있을 때만)
        /// </summary>
        public float GetHousingPowerPenalty(BuildingInstance housing)
        {
            if (!HasPowerGrid || currentAvailablePower <= 0) return 0f;
            return IsHousingPowered(housing) ? 0f : unpoweredHousingPenalty;
        }

        private int GetTileDistance(Vector2Int posA, Vector2Int posB)
        {
            return Mathf.Max(Mathf.Abs(posA.x - posB.x), Mathf.Abs(posA.y - posB.y));
        }
    }
}

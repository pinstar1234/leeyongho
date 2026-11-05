using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Building;
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

        [Header("현재 전력 현황")]
        [SerializeField] private int currentAvailablePower = 0; // 현재 보유 전력
        [SerializeField] private int currentUsedPower = 0;      // 현재 필요 전력

        public int CurrentAvailablePower => currentAvailablePower;
        public int CurrentUsedPower => currentUsedPower;
        public int TargetPurchasedPower => targetPurchasedPower;

        private List<UtilityPoleInstance> _allPoles = new List<UtilityPoleInstance>();
        private List<UtilityPoleInstance> _blackedOutPoles = new List<UtilityPoleInstance>();

        public event Action<int, int> OnPowerDataChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnMonthlyTick += ProcessMonthlyPowerPurchase;
            }
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnMonthlyTick -= ProcessMonthlyPowerPurchase;
            }
        }

        public void SetTargetPurchasedPower(int amount)
        {
            targetPurchasedPower = Mathf.Max(0, amount);
            OnPowerDataChanged?.Invoke(currentUsedPower, currentAvailablePower);
        }

        private void ProcessMonthlyPowerPurchase()
        {
            int totalCost = targetPurchasedPower * pricePerPowerUnit;

            if (EconomyManager.Instance != null)
            {
                if (EconomyManager.Instance.TrySpendMoney(totalCost))
                {
                    currentAvailablePower = targetPurchasedPower;
                }
                else
                {
                    currentAvailablePower = 0;
                }
            }

            EvaluatePowerSupply();
        }

        public void RecalculatePowerUsage(List<BuildingInstance> allBuildings)
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

            if (currentUsedPower > currentAvailablePower && _allPoles.Count > 0)
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

        public bool IsHousingPowered(BuildingInstance housing)
        {
            if (housing == null || housing.Data.category != BuildingType.Housing) return true;

            foreach (var pole in _allPoles)
            {
                if (pole == null) continue;

                if (pole.IsPowered)
                {
                    int distance = GetTileDistance(housing.OriginGridPos, pole.GridPos);

                    if (distance <= pole.SupplyRadius)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 정전 상황에 따른 만족도 감점 수치 반환 (GetPowerBlackoutSatisfactionPenalty 에러 해결)
        /// </summary>
        public float GetPowerBlackoutSatisfactionPenalty()
        {
            if (currentUsedPower <= currentAvailablePower && _blackedOutPoles.Count == 0)
            {
                return 0f;
            }

            if (currentAvailablePower <= 0 && currentUsedPower > 0)
            {
                return 30f;
            }

            return _blackedOutPoles.Count * 10f;
        }

        private int GetTileDistance(Vector2Int posA, Vector2Int posB)
        {
            return Mathf.Max(Mathf.Abs(posA.x - posB.x), Mathf.Abs(posA.y - posB.y));
        }
    }
}
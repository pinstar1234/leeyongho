using System.Collections.Generic;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Satisfaction;
using InfraTown.Population;
using InfraTown.Economy;
using InfraTown.Power;
using InfraTown.Services;
using InfraTown.TimeSystem;

namespace InfraTown.Core
{
    public class GameLoopManager : MonoBehaviour
    {
        public static GameLoopManager Instance { get; private set; }

        private List<BuildingInstance> _placedBuildings = new List<BuildingInstance>();

        /// <summary>현재 맵에 배치된 모든 건물 (읽기 전용)</summary>
        public IReadOnlyList<BuildingInstance> PlacedBuildings => _placedBuildings;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            // 엔딩 판정 매니저가 씬에 없으면 이 오브젝트에 추가 (씬 수정 없이 동작하도록)
            if (GameOutcomeManager.Instance == null)
            {
                gameObject.AddComponent<GameOutcomeManager>();
            }

            BuildingInstance.OnBuildingDestroyed += HandleBuildingDestroyed;

            // TimeManager 틱 이벤트 연동
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnWeeklyTick += HandleWeeklyTick;
                TimeManager.Instance.OnMonthlyTick += HandleMonthlyTick;
            }

            // 전력 보유량이 바뀌면(월 구매, 목표량 변경) 정전 상태가 달라지므로 만족도 재계산
            if (PowerManager.Instance != null)
            {
                PowerManager.Instance.OnPowerDataChanged += HandlePowerDataChanged;
            }
        }

        private void OnDestroy()
        {
            BuildingInstance.OnBuildingDestroyed -= HandleBuildingDestroyed;

            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnWeeklyTick -= HandleWeeklyTick;
                TimeManager.Instance.OnMonthlyTick -= HandleMonthlyTick;
            }

            if (PowerManager.Instance != null)
            {
                PowerManager.Instance.OnPowerDataChanged -= HandlePowerDataChanged;
            }
        }

        public void RegisterBuilding(BuildingInstance building)
        {
            if (!_placedBuildings.Contains(building))
            {
                _placedBuildings.Add(building);
                RecalculateAll();
            }
        }

        private void HandleBuildingDestroyed(BuildingInstance building)
        {
            if (_placedBuildings.Contains(building))
            {
                _placedBuildings.Remove(building);
                RecalculateAll();
            }
        }

        /// <summary>
        /// 건물 구성이 바뀌었을 때 모든 시스템 재계산.
        /// 순서 중요: 수용 인구 → 전력(정전 판정) → 접근성 → 만족도(앞의 결과를 사용)
        /// </summary>
        public void RecalculateAll()
        {
            PopulationManager.Instance?.RecalculateCapacity(_placedBuildings);
            PowerManager.Instance?.RecalculatePowerUsage(_placedBuildings);
            ServiceAccessibilityManager.Instance?.RecalculateServiceMetrics(_placedBuildings);
            TriggerSatisfactionUpdate();
        }

        public void TriggerSatisfactionUpdate()
        {
            if (SatisfactionManager.Instance != null)
            {
                SatisfactionManager.Instance.RecalculateSatisfaction(_placedBuildings);
            }
        }

        private void HandlePowerDataChanged(int used, int available)
        {
            TriggerSatisfactionUpdate();
        }

        /// <summary>
        /// 배치된 건물의 월 유지비 합계
        /// </summary>
        public int GetTotalMaintenanceCost()
        {
            int total = 0;
            foreach (var building in _placedBuildings)
            {
                if (building != null && building.Data != null)
                {
                    total += building.Data.maintenanceCost;
                }
            }
            return total;
        }

        /// <summary>
        /// 1주일(7일) 틱: 인구 갱신 및 만족도 패널티 산출
        /// </summary>
        private void HandleWeeklyTick()
        {
            float currentSatisfaction = SatisfactionManager.Instance != null
                ? SatisfactionManager.Instance.CurrentAverageSatisfaction
                : 70f;

            PopulationManager.Instance?.ProcessWeeklyPopulationUpdate(currentSatisfaction);
            TriggerSatisfactionUpdate();
        }

        /// <summary>
        /// 1달(30일) 틱: 지원금 수령 → 유지비 지출 → 다음 달 전력 구매 순서로 정산
        /// (지원금이 먼저 들어와야 전력 구매 자금이 확보되므로 순서를 여기서 보장)
        /// </summary>
        private void HandleMonthlyTick()
        {
            int currentPopulation = PopulationManager.Instance != null
                ? PopulationManager.Instance.CurrentPopulation
                : 0;

            EconomyManager.Instance?.ProcessMonthlySettlement(currentPopulation, GetTotalMaintenanceCost());
            PowerManager.Instance?.ProcessMonthlyPowerPurchase();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Satisfaction;
using InfraTown.Population;
using InfraTown.Economy;
using InfraTown.TimeSystem;

namespace InfraTown.Core
{
    public class GameLoopManager : MonoBehaviour
    {
        public static GameLoopManager Instance { get; private set; }

        private List<BuildingInstance> _placedBuildings = new List<BuildingInstance>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            BuildingInstance.OnBuildingDestroyed += HandleBuildingDestroyed;

            // TimeManager 틱 이벤트 연동
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnWeeklyTick += HandleWeeklyTick;
                TimeManager.Instance.OnMonthlyTick += HandleMonthlyTick;
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
        }

        public void RegisterBuilding(BuildingInstance building)
        {
            if (!_placedBuildings.Contains(building))
            {
                _placedBuildings.Add(building);
                PopulationManager.Instance?.RecalculateCapacity(_placedBuildings);
                TriggerSatisfactionUpdate();
            }
        }

        private void HandleBuildingDestroyed(BuildingInstance building)
        {
            if (_placedBuildings.Contains(building))
            {
                _placedBuildings.Remove(building);
                PopulationManager.Instance?.RecalculateCapacity(_placedBuildings);
                TriggerSatisfactionUpdate();
            }
        }

        public void TriggerSatisfactionUpdate()
        {
            if (SatisfactionManager.Instance != null)
            {
                SatisfactionManager.Instance.RecalculateSatisfaction(_placedBuildings);
            }
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
        /// 1달(30일) 틱: 건물 총 유지비 계산 및 정부 지원금 수령 정산
        /// </summary>
        private void HandleMonthlyTick()
        {
            int totalMaintenanceCost = 0;

            foreach (var building in _placedBuildings)
            {
                if (building != null && building.Data != null)
                {
                    totalMaintenanceCost += building.Data.maintenanceCost;
                }
            }

            int currentPopulation = PopulationManager.Instance != null
                ? PopulationManager.Instance.CurrentPopulation
                : 0;

            EconomyManager.Instance?.ProcessMonthlySettlement(currentPopulation, totalMaintenanceCost);
        }
    }
}
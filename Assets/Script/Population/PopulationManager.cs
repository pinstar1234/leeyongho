using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Economy;
using InfraTown.Satisfaction;

namespace InfraTown.Population
{
    public class PopulationManager : MonoBehaviour
    {
        public static PopulationManager Instance { get; private set; }

        [Header("인구 현황")]
        [SerializeField] private int currentPopulation = 0;
        [SerializeField] private int maxPopulationCapacity = 0;

        public int CurrentPopulation => currentPopulation;
        public int MaxPopulationCapacity => maxPopulationCapacity;

        // 인구 변동 이벤트 (현재 인구, 한계 인구)
        public event Action<int, int> OnPopulationChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            OnPopulationChanged?.Invoke(currentPopulation, maxPopulationCapacity);
        }

        /// <summary>
        /// 외부 호출용 인구 한계 수량 재계산
        /// </summary>
        public void RecalculateCapacity(List<BuildingInstance> allBuildings)
        {
            RecalculatePopulationCapacity(allBuildings);
        }

        /// <summary>
        /// 건물이 건설되거나 파괴되었을 때 한계 인구수를 재계산하고 인구 유입을 갱신
        /// </summary>
        public void RecalculatePopulationCapacity(List<BuildingInstance> allBuildings)
        {
            int totalCapacity = 0;

            foreach (var building in allBuildings)
            {
                if (building == null || building.Data == null) continue;

                if (building.Data.category == BuildingType.Housing)
                {
                    totalCapacity += building.Data.housingCapacity;
                }
            }

            maxPopulationCapacity = totalCapacity;

            if (currentPopulation > maxPopulationCapacity)
            {
                currentPopulation = maxPopulationCapacity;
            }

            OnPopulationChanged?.Invoke(currentPopulation, maxPopulationCapacity);
            EconomyManager.Instance?.UpdateGovernmentSubsidyLimit(currentPopulation);
        }

        /// <summary>
        /// 주간 단위 인구 유입/유출 업데이트 (매개변수 없는 버전)
        /// </summary>
        public void ProcessWeeklyPopulationUpdate()
        {
            ProcessWeeklyPopulationUpdate(0);
        }

        /// <summary>
        /// 주간 단위 인구 유입/유출 업데이트 (float 인자 수신 오버로드 - 에러 해결)
        /// </summary>
        public void ProcessWeeklyPopulationUpdate(float timeOrDelta)
        {
            ProcessWeeklyPopulationUpdate(Mathf.RoundToInt(timeOrDelta));
        }

        /// <summary>
        /// 주간 단위 인구 유입/유출 업데이트 (int 인자 수신 오버로드)
        /// </summary>
        public void ProcessWeeklyPopulationUpdate(int weekIndex)
        {
            // 수용 공간이 남아있고 만족도가 높을 때 인구 유입
            if (currentPopulation < maxPopulationCapacity)
            {
                float avgSatisfaction = SatisfactionManager.Instance != null
                    ? SatisfactionManager.Instance.CurrentAverageSatisfaction
                    : 70f;

                // 만족도가 50% 이상일 때 주간 자연 유입 발생
                if (avgSatisfaction >= 50f)
                {
                    int growthAmount = Mathf.Clamp(Mathf.RoundToInt((avgSatisfaction - 40f) * 0.1f), 1, 5);
                    AddPopulation(growthAmount);
                    Debug.Log($"[PopulationManager] 주간 인구 유입: +{growthAmount}명 (현재: {currentPopulation}/{maxPopulationCapacity})");
                }
            }
        }

        /// <summary>
        /// 주간 단위 인구 유입/유출 업데이트 (건물 리스트 수신 오버로드)
        /// </summary>
        public void ProcessWeeklyPopulationUpdate(List<BuildingInstance> allBuildings)
        {
            if (allBuildings != null)
            {
                RecalculatePopulationCapacity(allBuildings);
            }
            ProcessWeeklyPopulationUpdate(0);
        }

        /// <summary>
        /// 인구 수동 추가/변경 (테스트 및 이벤트용)
        /// </summary>
        public void AddPopulation(int amount)
        {
            currentPopulation = Mathf.Clamp(currentPopulation + amount, 0, maxPopulationCapacity);
            OnPopulationChanged?.Invoke(currentPopulation, maxPopulationCapacity);

            EconomyManager.Instance?.UpdateGovernmentSubsidyLimit(currentPopulation);
        }

        /// <summary>
        /// 한계 인구 초과율에 따른 만족도 감점 수치 반환
        /// </summary>
        public float GetOverCapacitySatisfactionPenalty()
        {
            if (maxPopulationCapacity <= 0) return 0f;

            if (currentPopulation > maxPopulationCapacity)
            {
                float overRatio = (float)(currentPopulation - maxPopulationCapacity) / maxPopulationCapacity;
                return overRatio * 100f * 0.5f;
            }

            return 0f;
        }
    }
}
using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Building;

namespace InfraTown.Population
{
    public class PopulationManager : MonoBehaviour
    {
        public static PopulationManager Instance { get; private set; }

        [Header("인구수 정보")]
        [SerializeField] private int currentPopulation = 0;     // 현재 실제 인구수
        [SerializeField] private int maxPopulationCapacity = 0; // 현재 총 인구 한계치

        public int CurrentPopulation => currentPopulation;
        public int MaxPopulationCapacity => maxPopulationCapacity;

        // 초과 인구 비율 (0.05 = 5% 초과 상태)
        public float OverCapacityRatio { get; private set; } = 0f;

        // 인구 변화 이벤트 (현재 인구수, 인구 한계치)
        public event Action<int, int> OnPopulationChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>
        /// 설치된 전체 주택의 데이터베이스(housingCapacity) 값을 합산하여 최대 인구 한계치를 재계산
        /// </summary>
        public void RecalculateCapacity(List<BuildingInstance> allBuildings)
        {
            int newCapacity = 0;

            foreach (var building in allBuildings)
            {
                if (building == null || building.Data == null) continue;

                // 주택(Housing)인 경우 ScriptableObject DB에 설정된 housingCapacity 사용
                if (building.Data.category == BuildingType.Housing)
                {
                    newCapacity += building.Data.housingCapacity;
                }
            }

            maxPopulationCapacity = newCapacity;
            OnPopulationChanged?.Invoke(currentPopulation, maxPopulationCapacity);
        }

        /// <summary>
        /// 1주일마다 호출되는 인구 갱신 로직 (주 단위 틱)
        /// </summary>
        public void ProcessWeeklyPopulationUpdate(float currentSatisfaction)
        {
            if (maxPopulationCapacity <= 0)
            {
                currentPopulation = 0;
                OverCapacityRatio = 0f;
                OnPopulationChanged?.Invoke(currentPopulation, maxPopulationCapacity);
                return;
            }

            // 최대 인구 한계치의 105% (5% 초과) 한계 지정
            int absoluteMaxLimit = Mathf.FloorToInt(maxPopulationCapacity * 1.05f);

            // 만족도가 40% 이상일 때 유입, 낮으면 유출
            if (currentSatisfaction >= 40f)
            {
                int growthAmount = Mathf.Max(1, Mathf.FloorToInt(maxPopulationCapacity * 0.1f));
                currentPopulation = Mathf.Min(currentPopulation + growthAmount, absoluteMaxLimit);
            }
            else
            {
                int decayAmount = Mathf.Max(1, Mathf.FloorToInt(currentPopulation * 0.1f));
                currentPopulation = Mathf.Max(0, currentPopulation - decayAmount);
            }

            // 한계 초과 비율 산출
            if (currentPopulation > maxPopulationCapacity && maxPopulationCapacity > 0)
            {
                OverCapacityRatio = (float)(currentPopulation - maxPopulationCapacity) / maxPopulationCapacity;
            }
            else
            {
                OverCapacityRatio = 0f;
            }

            OnPopulationChanged?.Invoke(currentPopulation, maxPopulationCapacity);
            Debug.Log($"[1주일 인구 갱신] 현재 인구: {currentPopulation} / 한계치: {maxPopulationCapacity} (초과율: {OverCapacityRatio * 100:F1}%)");
        }

        /// <summary>
        /// 인구 한계 초과로 인한 만족도 패널티 산출 (초과 퍼센트의 10배 차감)
        /// </summary>
        public float GetOverCapacitySatisfactionPenalty()
        {
            if (OverCapacityRatio <= 0f) return 0f;

            float overPercentage = OverCapacityRatio * 100f;
            return overPercentage * 10f;
        }
    }
}
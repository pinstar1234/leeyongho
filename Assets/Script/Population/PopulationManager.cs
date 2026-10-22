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
        [SerializeField] private int currentPopulation = 0; // 현재 실제 인구수
        [SerializeField] private int maxPopulationCapacity = 0; // 현재 인구 한계치

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
        /// 설치된 전체 주택을 검사하여 최대 인구 한계치를 재계산
        /// </summary>
        public void RecalculateCapacity(List<BuildingInstance> allBuildings)
        {
            int newCapacity = 0;

            foreach (var building in allBuildings)
            {
                if (building == null || building.Data == null) continue;

                // 주택(Housing)인 경우 데이터베이스에 설정된 용량만큼 한계치 증가
                if (building.Data.category == BuildingType.Housing)
                {
                    // 건물의 requiredPower 또는 별도 지정 필드를 용량으로 활용 (소형: 10명, 중형: 30명 등)
                    newCapacity += GetHousingCapacity(building.Data.sizeType);
                }
            }

            maxPopulationCapacity = newCapacity;
            OnPopulationChanged?.Invoke(currentPopulation, maxPopulationCapacity);
        }

        /// <summary>
        /// 건물 규격별 기본 인구 제공 한계치 (DB ScriptableObject 확장 시 대체 가능)
        /// </summary>
        private int GetHousingCapacity(BuildingSizeType sizeType)
        {
            return sizeType switch
            {
                BuildingSizeType.Small => 10,   // 소형 주택: 10명
                BuildingSizeType.Medium => 25,  // 중형 주택: 25명
                BuildingSizeType.Large => 60,   // 대형 주택: 60명
                _ => 10
            };
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
                // 주당 유입 인구 (한계치의 10% 단위 증가)
                int growthAmount = Mathf.Max(1, Mathf.FloorToInt(maxPopulationCapacity * 0.1f));
                currentPopulation = Mathf.Min(currentPopulation + growthAmount, absoluteMaxLimit);
            }
            else
            {
                // 만족도가 낮을 경우 인구 유출
                int decayAmount = Mathf.Max(1, Mathf.FloorToInt(currentPopulation * 0.1f));
                currentPopulation = Mathf.Max(0, currentPopulation - decayAmount);
            }

            // 한계 초과 비율 산출 (예: 한계치 100명, 현재 105명이면 0.05)
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
        /// 예: 3% 초과 시 -> 3 * 10 = -30% 차감
        /// </summary>
        public float GetOverCapacitySatisfactionPenalty()
        {
            if (OverCapacityRatio <= 0f) return 0f;

            float overPercentage = OverCapacityRatio * 100f; // 0.05 -> 5%
            return overPercentage * 10f; // 5% 초과 시 50% 차감 패널티
        }
    }
}
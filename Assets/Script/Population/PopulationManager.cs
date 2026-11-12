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

        [Header("인구 초과 (주택 철거 등으로 수용 인구가 줄었을 때)")]
        [Tooltip("초과 비율 1%당 만족도 감점 (0.5 → 초과 20%면 10점 감점)")]
        [SerializeField] private float overCapacityPenaltyPerPercent = 0.5f;

        [Tooltip("인구 초과 만족도 감점 최대치")]
        [SerializeField] private float maxOverCapacityPenalty = 50f;

        [Tooltip("매주 초과 인구 중 마을을 떠나는 비율 (최소 1명)")]
        [Range(0f, 1f)]
        [SerializeField] private float overCapacityLeaveRate = 0.5f;

        public int CurrentPopulation => currentPopulation;
        public int MaxPopulationCapacity => maxPopulationCapacity;

        /// <summary>수용 인구를 넘은 인구 수 (0이면 정상)</summary>
        public int OverCapacityPopulation => Mathf.Max(0, currentPopulation - maxPopulationCapacity);
        public bool IsOverCapacity => currentPopulation > maxPopulationCapacity;

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
        /// 건물이 건설되거나 파괴되었을 때 한계 인구수를 재계산.
        /// 수용 인구가 줄어도 주민은 바로 사라지지 않고 '초과 인구'로 남아 만족도 감점을 주며,
        /// 주간 갱신 때 조금씩 떠난다.
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
            // 초과 인구가 있으면 유입 없이 일부가 떠남
            if (IsOverCapacity)
            {
                int leaving = Mathf.Max(1, Mathf.CeilToInt(OverCapacityPopulation * overCapacityLeaveRate));
                AddPopulation(-leaving);
                Debug.Log($"[PopulationManager] 주간 인구 유출(수용 초과): -{leaving}명 (현재: {currentPopulation}/{maxPopulationCapacity})");
                return;
            }

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
        /// 인구 수동 추가/변경 (테스트 및 이벤트용).
        /// 늘릴 때는 수용 인구를 넘지 않고, 줄일 때는 0 아래로 내려가지 않는다.
        /// </summary>
        public void AddPopulation(int amount)
        {
            if (amount > 0)
            {
                // 이미 초과 상태라면 더 늘지 않음
                currentPopulation = Mathf.Max(currentPopulation, Mathf.Min(currentPopulation + amount, maxPopulationCapacity));
            }
            else
            {
                currentPopulation = Mathf.Max(0, currentPopulation + amount);
            }

            OnPopulationChanged?.Invoke(currentPopulation, maxPopulationCapacity);

            EconomyManager.Instance?.UpdateGovernmentSubsidyLimit(currentPopulation);
        }

        /// <summary>
        /// 한계 인구 초과율에 따른 만족도 감점 수치 반환
        /// </summary>
        public float GetOverCapacitySatisfactionPenalty()
        {
            if (!IsOverCapacity) return 0f;

            // 주택이 모두 사라졌는데 주민이 남은 경우
            if (maxPopulationCapacity <= 0) return maxOverCapacityPenalty;

            float overPercent = (float)OverCapacityPopulation / maxPopulationCapacity * 100f;
            return Mathf.Min(overPercent * overCapacityPenaltyPerPercent, maxOverCapacityPenalty);
        }
    }
}

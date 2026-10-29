using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Population;

namespace InfraTown.Satisfaction
{
    public class SatisfactionManager : MonoBehaviour
    {
        public static SatisfactionManager Instance { get; private set; }

        public const float DefaultSatisfaction = 70f; // 기본 시작 만족도 70%

        public float CurrentAverageSatisfaction { get; private set; } = DefaultSatisfaction;
        public SatisfactionTier CurrentTier { get; private set; } = SatisfactionTier.Stable;

        public event Action<float, SatisfactionTier> OnSatisfactionChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>
        /// 주택 단위 개별 만족도 계산 및 인구 과밀 패널티(초과 % x 10)를 최종 반영하여 마을 평균 산출
        /// </summary>
        public void RecalculateSatisfaction(List<BuildingInstance> allBuildings)
        {
            List<BuildingInstance> housingBuildings = new List<BuildingInstance>();
            List<BuildingInstance> affectorBuildings = new List<BuildingInstance>();

            foreach (var building in allBuildings)
            {
                if (building == null || building.Data == null) continue;

                if (building.Data.category == BuildingType.Housing)
                {
                    housingBuildings.Add(building);
                }

                if (Mathf.Abs(building.Data.satisfactionImpact) > 0.01f)
                {
                    affectorBuildings.Add(building);
                }
            }

            // 인구 초과 패널티 수치 가져오기 (초과 % x 10)
            float overCapacityPenalty = PopulationManager.Instance != null
                ? PopulationManager.Instance.GetOverCapacitySatisfactionPenalty()
                : 0f;

            if (housingBuildings.Count == 0)
            {
                CurrentAverageSatisfaction = Mathf.Clamp(DefaultSatisfaction - overCapacityPenalty, 0f, 100f);
                CurrentTier = CalculateTier(CurrentAverageSatisfaction);
                OnSatisfactionChanged?.Invoke(CurrentAverageSatisfaction, CurrentTier);
                return;
            }

            float totalHousingSatisfactionSum = 0f;

            foreach (var housing in housingBuildings)
            {
                HousingInstance housingComp = housing.GetComponent<HousingInstance>();
                if (housingComp == null)
                {
                    housingComp = housing.gameObject.AddComponent<HousingInstance>();
                }

                float satisfactionOffset = 0f;

                foreach (var affector in affectorBuildings)
                {
                    if (affector == housing) continue;

                    int distance = GetTileDistance(housing.OriginGridPos, affector.OriginGridPos);

                    if (distance <= affector.Data.satisfactionRadius)
                    {
                        satisfactionOffset += affector.Data.satisfactionImpact;
                    }
                }

                // 주택 개별 만족도 재계산
                housingComp.RecalculateSatisfaction(satisfactionOffset);
                totalHousingSatisfactionSum += housingComp.CurrentSatisfaction;
            }

            // 주택 평균 만족도 산출
            float rawAverage = totalHousingSatisfactionSum / housingBuildings.Count;

            // 최종 만족도 = 주택 평균 만족도 - (인구 한계 초과 % x 10)
            CurrentAverageSatisfaction = Mathf.Clamp(rawAverage - overCapacityPenalty, 0f, 100f);
            CurrentTier = CalculateTier(CurrentAverageSatisfaction);

            OnSatisfactionChanged?.Invoke(CurrentAverageSatisfaction, CurrentTier);
        }

        private int GetTileDistance(Vector2Int posA, Vector2Int posB)
        {
            return Mathf.Max(Mathf.Abs(posA.x - posB.x), Mathf.Abs(posA.y - posB.y));
        }

        private SatisfactionTier CalculateTier(float score)
        {
            return score switch
            {
                <= 20f => SatisfactionTier.Desolate,
                <= 40f => SatisfactionTier.Stagnant,
                <= 60f => SatisfactionTier.Developing,
                <= 80f => SatisfactionTier.Stable,
                _ => SatisfactionTier.Prosperous
            };
        }
    }
}
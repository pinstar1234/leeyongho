using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Population;
using InfraTown.Power;
using InfraTown.Services;

namespace InfraTown.Satisfaction
{
    public class SatisfactionManager : MonoBehaviour
    {
        public static SatisfactionManager Instance { get; private set; }

        public const float DefaultSatisfaction = 70f; // 기본 시작 만족도 70%

        [Header("좋음/나쁨 기준 (기획서 6장)")]
        [Tooltip("이 점수 이상이면 '좋음'")]
        [SerializeField] private float goodThreshold = 71f;

        [Tooltip("이 점수 이하면 '나쁨'")]
        [SerializeField] private float badThreshold = 30f;

        public float GoodThreshold => goodThreshold;
        public float BadThreshold => badThreshold;

        public float CurrentAverageSatisfaction { get; private set; } = DefaultSatisfaction;
        public SatisfactionTier CurrentTier { get; private set; } = SatisfactionTier.Stable;

        /// <summary>마을 평균 만족도 기준 상태 (패배 판정에 사용)</summary>
        public SatisfactionState CurrentState { get; private set; } = SatisfactionState.Normal;

        /// <summary>주택 수</summary>
        public int HousingCount { get; private set; }

        /// <summary>'좋음' 주택 비율 0~1 (승리 판정에 사용, 주택이 없으면 0)</summary>
        public float GoodHousingRatio { get; private set; }

        /// <summary>'나쁨' 주택 비율 0~1</summary>
        public float BadHousingRatio { get; private set; }

        public event Action<float, SatisfactionTier> OnSatisfactionChanged;

        /// <summary>(마을 상태, 좋음 주택 비율)</summary>
        public event Action<SatisfactionState, float> OnSatisfactionStateUpdated;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>
        /// 주택 단위 개별 만족도 계산 및 인구 과밀 / 정전 패널티를 최종 반영하여 마을 평균 산출
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

            // 1. 인구 초과 패널티 수치 가져오기 (초과 % x 10)
            float overCapacityPenalty = PopulationManager.Instance != null
                ? PopulationManager.Instance.GetOverCapacitySatisfactionPenalty()
                : 0f;

            // 2. 정전 패널티 수치 가져오기
            float blackoutPenalty = PowerManager.Instance != null
                ? PowerManager.Instance.GetPowerBlackoutSatisfactionPenalty()
                : 0f;

            if (housingBuildings.Count == 0)
            {
                HousingCount = 0;
                GoodHousingRatio = 0f;
                BadHousingRatio = 0f;
                ApplyAverage(DefaultSatisfaction - overCapacityPenalty - blackoutPenalty);
                return;
            }

            float totalHousingSatisfactionSum = 0f;
            int goodCount = 0, badCount = 0;
            var service = ServiceAccessibilityManager.Instance;

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

                // 전봇대 망에서 전기가 닿지 않는 주택 개별 감점
                if (PowerManager.Instance != null)
                {
                    satisfactionOffset -= PowerManager.Instance.GetHousingPowerPenalty(housing);
                }

                // 접근 시간 단축 보너스 (기획서 6장: 읍내 기준 시간 대비 단축 비율)
                float accessBonus = service != null ? service.GetTotalAccessBonus(housing) : 0f;
                housingComp.SetAccessBonus(accessBonus);
                satisfactionOffset += accessBonus;

                // 마을 전체 감점(인구 초과, 정전)도 주택마다 적용해야 주택별 좋음/나쁨 판정에 반영된다
                satisfactionOffset -= overCapacityPenalty + blackoutPenalty;

                housingComp.RecalculateSatisfaction(satisfactionOffset);

                SatisfactionState state = GetState(housingComp.CurrentSatisfaction);
                housingComp.SetState(state);
                if (state == SatisfactionState.Good) goodCount++;
                else if (state == SatisfactionState.Bad) badCount++;

                totalHousingSatisfactionSum += housingComp.CurrentSatisfaction;
            }

            HousingCount = housingBuildings.Count;
            GoodHousingRatio = (float)goodCount / HousingCount;
            BadHousingRatio = (float)badCount / HousingCount;

            // 최종 만족도 = 주택별 최종 만족도의 평균
            ApplyAverage(totalHousingSatisfactionSum / housingBuildings.Count);
        }

        private void ApplyAverage(float average)
        {
            CurrentAverageSatisfaction = Mathf.Clamp(average, 0f, 100f);
            CurrentTier = CalculateTier(CurrentAverageSatisfaction);
            CurrentState = GetState(CurrentAverageSatisfaction);

            OnSatisfactionChanged?.Invoke(CurrentAverageSatisfaction, CurrentTier);
            OnSatisfactionStateUpdated?.Invoke(CurrentState, GoodHousingRatio);
        }

        /// <summary>
        /// 만족도 점수 → 좋음/보통/나쁨
        /// </summary>
        public SatisfactionState GetState(float score)
        {
            if (score >= goodThreshold) return SatisfactionState.Good;
            if (score <= badThreshold) return SatisfactionState.Bad;
            return SatisfactionState.Normal;
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
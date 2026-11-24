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

        [Header("필수 시설 미설치 감점 (밸런스, 사용자 결정 2026-11-19)")]
        [Tooltip("해금됐는데 없으면 감점이 쌓이는 시설")]
        [SerializeField] private List<ServiceType> penalizedServices = new List<ServiceType>
        {
            ServiceType.Hospital, ServiceType.School, ServiceType.FireStation, ServiceType.Police
        };

        [Tooltip("없는 시설 1종당 1주일마다 쌓이는 감점 (%)")]
        [SerializeField] private float missingFacilityPenaltyPerWeek = 2f;

        [Tooltip("시설 1종당 감점 최대치 (%)")]
        [SerializeField] private float maxMissingFacilityPenalty = 10f;

        [Tooltip("시설을 지은 뒤(또는 다시 잠기면) 1주일마다 회복되는 양 (%)")]
        [SerializeField] private float missingFacilityRecoveryPerWeek = 2f;

        private readonly Dictionary<ServiceType, float> _missingFacilityPenalties = new Dictionary<ServiceType, float>();

        /// <summary>필수 시설 미설치로 지금 마을 만족도에서 빠지고 있는 합계 (%)</summary>
        public float TotalMissingFacilityPenalty
        {
            get
            {
                float total = 0f;
                foreach (var p in _missingFacilityPenalties.Values) total += p;
                return total;
            }
        }

        /// <summary>해당 시설 미설치로 쌓인 감점 (%)</summary>
        public float GetMissingFacilityPenalty(ServiceType type) =>
            _missingFacilityPenalties.TryGetValue(type, out var p) ? p : 0f;

        /// <summary>미설치 감점이 바뀌었을 때 (합계)</summary>
        public event Action<float> OnMissingFacilityPenaltyChanged;

        /// <summary>
        /// 1주일 경과 처리 (GameLoopManager 주간 틱에서 호출).
        /// 해금됐는데 없는 필수 시설은 감점이 쌓이고(시설당 최대치까지), 지어졌거나 잠긴 시설은 같은 비율로 회복된다.
        /// 반영은 다음 RecalculateSatisfaction 때.
        /// </summary>
        public void ProcessWeeklyMissingFacilityPenalty()
        {
            var service = ServiceAccessibilityManager.Instance;
            float before = TotalMissingFacilityPenalty;

            foreach (var type in penalizedServices)
            {
                float current = GetMissingFacilityPenalty(type);
                bool missing = service != null && service.IsServiceMissing(type);

                current = missing
                    ? Mathf.Min(current + missingFacilityPenaltyPerWeek, maxMissingFacilityPenalty)
                    : Mathf.Max(current - missingFacilityRecoveryPerWeek, 0f);

                if (current > 0f) _missingFacilityPenalties[type] = current;
                else _missingFacilityPenalties.Remove(type);
            }

            if (!Mathf.Approximately(before, TotalMissingFacilityPenalty))
            {
                OnMissingFacilityPenaltyChanged?.Invoke(TotalMissingFacilityPenalty);
            }
        }

        // ─── 세이브 ───

        public InfraTown.Save.SatisfactionSaveState GetSaveState()
        {
            var s = new InfraTown.Save.SatisfactionSaveState();
            foreach (var pair in _missingFacilityPenalties)
            {
                s.missingPenaltyServices.Add((int)pair.Key);
                s.missingPenaltyValues.Add(pair.Value);
            }
            return s;
        }

        /// <summary>필수 시설 미설치 누적 감점을 되돌림 (나머지 만족도는 건물에서 다시 계산)</summary>
        public void ApplySaveState(InfraTown.Save.SatisfactionSaveState s)
        {
            if (s == null) return;
            _missingFacilityPenalties.Clear();
            for (int i = 0; i < s.missingPenaltyServices.Count && i < s.missingPenaltyValues.Count; i++)
            {
                float value = Mathf.Clamp(s.missingPenaltyValues[i], 0f, maxMissingFacilityPenalty);
                if (value > 0f) _missingFacilityPenalties[(ServiceType)s.missingPenaltyServices[i]] = value;
            }
            OnMissingFacilityPenaltyChanged?.Invoke(TotalMissingFacilityPenalty);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { InfraTown.Core.SingletonGuard.RemoveDuplicate(this, Instance); return; }
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

            // 2-1. 가스 부족 감점 (마을 전체, 사용자 기획 2026-11-24) — 정전 감점과 같이 마을 감점에 합침
            blackoutPenalty += InfraTown.Gas.GasManager.Instance != null
                ? InfraTown.Gas.GasManager.Instance.GetGasShortageSatisfactionPenalty()
                : 0f;

            // 3. 필수 시설 미설치 누적 감점 (주간 틱에서 쌓이고 풀림)
            float missingFacilityPenalty = TotalMissingFacilityPenalty;

            if (housingBuildings.Count == 0)
            {
                HousingCount = 0;
                GoodHousingRatio = 0f;
                BadHousingRatio = 0f;
                ApplyAverage(DefaultSatisfaction - overCapacityPenalty - blackoutPenalty - missingFacilityPenalty);
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

                // 마을 전체 감점(인구 초과, 정전, 필수 시설 미설치)도 주택마다 적용해야 주택별 좋음/나쁨 판정에 반영된다
                satisfactionOffset -= overCapacityPenalty + blackoutPenalty + missingFacilityPenalty;

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
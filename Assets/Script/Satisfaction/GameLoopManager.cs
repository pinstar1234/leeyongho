using System.Collections.Generic;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Satisfaction;
using InfraTown.Population;
using InfraTown.Economy;
using InfraTown.Power;
using InfraTown.Gas;
using InfraTown.Services;
using InfraTown.TimeSystem;

namespace InfraTown.Core
{
    public class GameLoopManager : MonoBehaviour
    {
        public static GameLoopManager Instance { get; private set; }

        private List<BuildingInstance> _placedBuildings = new List<BuildingInstance>();
        private GasManager _gas;

        /// <summary>현재 맵에 배치된 모든 건물 (읽기 전용)</summary>
        public IReadOnlyList<BuildingInstance> PlacedBuildings => _placedBuildings;

        private void Awake()
        {
            if (Instance != null && Instance != this) { InfraTown.Core.SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;
        }

        private void Start()
        {
            // 엔딩 판정 매니저가 씬에 없으면 이 오브젝트에 추가 (씬 수정 없이 동작하도록)
            if (GameOutcomeManager.Instance == null)
            {
                gameObject.AddComponent<GameOutcomeManager>();
            }

            // 가스 매니저도 씬에 없으면 추가 (사용자 기획 2026-11-24)
            if (GasManager.Instance == null)
            {
                gameObject.AddComponent<GasManager>();
            }

            // 정산 2주 전 적자 경고도 씬에 없으면 추가 (사용자 기획 2026-11-24, 가스 매니저 뒤에 추가해야 가스 비용을 구독함)
            if (SettlementForecaster.Instance == null)
            {
                gameObject.AddComponent<SettlementForecaster>();
            }

            // 1주마다 자동 저장도 씬에 없으면 추가 (사용자 기획 2026-11-25)
            if (InfraTown.Save.AutoSaveManager.Instance == null)
            {
                gameObject.AddComponent<InfraTown.Save.AutoSaveManager>();
            }

            // 대화 관리자는 있는데 오프닝·튜토리얼·불만 대사를 띄우는 DialogueTriggers가 없으면 대화 관리자 오브젝트에 추가
            // (사용자 결정 2026-11-26 — 2Park 1에 대화창만 있어 튜토리얼이 시작되지 않던 문제, 씬 수정 없이. 오프닝 대화 에셋은 씬에서 연결해야 나옴)
            var dialogue = InfraTown.EventSystem.DialogueEventManager.Instance;
            if (dialogue != null && Application.isPlaying && FindObjectOfType<InfraTown.EventSystem.DialogueTriggers>() == null)
            {
                dialogue.gameObject.AddComponent<InfraTown.EventSystem.DialogueTriggers>();
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

            // 가스 보유량·사용량이 바뀌면 가스 부족 감점이 달라지므로 만족도 재계산
            _gas = GasManager.Instance;
            if (_gas != null) _gas.OnGasDataChanged += HandleGasDataChanged;
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


            if (_gas != null) _gas.OnGasDataChanged -= HandleGasDataChanged;
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
            // 전봇대가 켜지고 꺼지면 시설 운영 여부(접근성)도 바뀌므로 접근성 → 만족도 순서로 다시 계산 (사용자 결정 2026-11-24)
            ServiceAccessibilityManager.Instance?.RecalculateServiceMetrics(_placedBuildings);
            TriggerSatisfactionUpdate();
        }

        private void HandleGasDataChanged(int used, int available)
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
        /// 1주 틱: 인구 갱신 및 만족도 패널티 산출
        /// </summary>
        private void HandleWeeklyTick()
        {
            float currentSatisfaction = SatisfactionManager.Instance != null
                ? SatisfactionManager.Instance.CurrentAverageSatisfaction
                : 70f;

            PopulationManager.Instance?.ProcessWeeklyPopulationUpdate(currentSatisfaction);

            // 해금됐는데 없는 필수 시설 → 주마다 마을 만족도 감점 누적 (지으면 주마다 회복)
            SatisfactionManager.Instance?.ProcessWeeklyMissingFacilityPenalty();
            TriggerSatisfactionUpdate();
        }

        /// <summary>
        /// 1달(4주) 틱: 지원금 수령 → 유지비 지출 → 다음 달 전력 구매 → 가스 구매 순서로 정산
        /// (지원금이 먼저 들어와야 전력 구매 자금이 확보되므로 순서를 여기서 보장).
        /// 돈이 모자라면 전력을 정한 양까지 살 수 있는 만큼 먼저 사고, 남은 돈으로 가스를 산다 (사용자 기획 2026-11-24)
        /// </summary>
        private void HandleMonthlyTick()
        {
            int currentPopulation = PopulationManager.Instance != null
                ? PopulationManager.Instance.CurrentPopulation
                : 0;

            EconomyManager.Instance?.ProcessMonthlySettlement(currentPopulation, GetTotalMaintenanceCost());
            PowerManager.Instance?.ProcessMonthlyPowerPurchase();
            GasManager.Instance?.ProcessMonthlyGasPurchase();
        }
    }
}

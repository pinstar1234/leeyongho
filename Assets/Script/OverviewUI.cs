using UnityEngine;
using UnityEngine.UI;
using TMPro;
using InfraTown.Satisfaction;
using InfraTown.Population;
using InfraTown.Economy;
using InfraTown.Power;
using InfraTown.Services;
using InfraTown.TimeSystem;

namespace InfraTown.UI
{
    public class OverviewUI : MonoBehaviour
    {
        [Header("전체 UI 패널 제어")]
        [SerializeField] private GameObject overviewPanel;
        [SerializeField] private Button closeButton;

        [Header("1. 전기 현황")]
        [SerializeField] private TextMeshProUGUI powerText;

        [Header("2. 인구 현황")]
        [SerializeField] private TextMeshProUGUI populationText;

        [Header("3. 재정 및 지원금 조절 (Slider)")]
        [SerializeField] private TextMeshProUGUI subsidyText;
        [SerializeField] private Slider subsidySlider;

        [Header("4. 만족도 및 불만 사항")]
        [SerializeField] private TextMeshProUGUI satisfactionText;
        [SerializeField] private Image satisfactionFaceImage;
        [SerializeField] private TextMeshProUGUI complaintsText;

        [Header("5. 치안 및 필수/편의 시설 접근 시간")]
        [SerializeField] private TextMeshProUGUI patrolCountText;
        [SerializeField] private TextMeshProUGUI hospitalTimeText;
        [SerializeField] private TextMeshProUGUI fireStationTimeText;
        [SerializeField] private TextMeshProUGUI convenienceTimeText;

        private void Start()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(HideOverview);

            if (subsidySlider != null)
                subsidySlider.onValueChanged.AddListener(OnSubsidySliderChanged);

            RegisterEvents();

            if (overviewPanel != null)
                overviewPanel.SetActive(false);
        }

        private void OnDestroy()
        {
            UnregisterEvents();
        }

        private void RegisterEvents()
        {
            if (SatisfactionManager.Instance != null)
                SatisfactionManager.Instance.OnSatisfactionChanged += UpdateSatisfactionUI;

            if (PopulationManager.Instance != null)
                PopulationManager.Instance.OnPopulationChanged += UpdatePopulationUI;

            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnSubsidyAmountChanged += UpdateSubsidyUI;
                EconomyManager.Instance.OnMoneyChanged += (balance, change) => RefreshAllData();
            }

            if (PowerManager.Instance != null)
            {
                PowerManager.Instance.OnPowerDataChanged += (used, available) => UpdatePowerUI();
            }

            if (ServiceAccessibilityManager.Instance != null)
            {
                ServiceAccessibilityManager.Instance.OnServiceMetricsUpdated += UpdateServiceMetricsUI;
            }
        }

        private void UnregisterEvents()
        {
            if (SatisfactionManager.Instance != null)
                SatisfactionManager.Instance.OnSatisfactionChanged -= UpdateSatisfactionUI;

            if (PopulationManager.Instance != null)
                PopulationManager.Instance.OnPopulationChanged -= UpdatePopulationUI;

            if (EconomyManager.Instance != null)
                EconomyManager.Instance.OnSubsidyAmountChanged -= UpdateSubsidyUI;

            if (ServiceAccessibilityManager.Instance != null)
                ServiceAccessibilityManager.Instance.OnServiceMetricsUpdated -= UpdateServiceMetricsUI;
        }

        public void ShowOverview()
        {
            if (overviewPanel != null)
            {
                overviewPanel.SetActive(true);
                TimeManager.Instance?.RequestEventPause(this);
                RefreshAllData();
            }
        }

        public void HideOverview()
        {
            if (overviewPanel != null)
            {
                overviewPanel.SetActive(false);
                TimeManager.Instance?.ReleaseEventPause(this);
            }
        }

        public void RefreshAllData()
        {
            UpdatePowerUI();

            if (PopulationManager.Instance != null)
                UpdatePopulationUI(PopulationManager.Instance.CurrentPopulation, PopulationManager.Instance.MaxPopulationCapacity);

            if (SatisfactionManager.Instance != null)
                UpdateSatisfactionUI(SatisfactionManager.Instance.CurrentAverageSatisfaction, SatisfactionManager.Instance.CurrentTier);

            if (EconomyManager.Instance != null)
                UpdateSubsidyUI(EconomyManager.Instance.GetRequestedSubsidy(), EconomyManager.Instance.GetMaxEligibleSubsidy());

            UpdateServiceMetricsUI();
        }

        private void UpdatePowerUI()
        {
            if (powerText == null) return;

            if (PowerManager.Instance != null)
            {
                int usedPower = PowerManager.Instance.CurrentUsedPower;
                int availablePower = PowerManager.Instance.CurrentAvailablePower;
                powerText.text = $"전기 수급: {usedPower} / {availablePower} kW";
            }
        }

        private void UpdatePopulationUI(int current, int maxCapacity)
        {
            if (populationText != null)
            {
                populationText.text = $"인구 현황: {current}명 / 한계: {maxCapacity}명";
            }
        }

        private void UpdateSubsidyUI(int requested, int maxEligible)
        {
            if (subsidySlider != null)
            {
                subsidySlider.minValue = 0;
                subsidySlider.maxValue = maxEligible;
                subsidySlider.SetValueWithoutNotify(requested);
            }

            if (subsidyText != null)
            {
                subsidyText.text = $"신청 지원금: {requested:N0} 원 / 최대: {maxEligible:N0} 원";
            }
        }

        private void OnSubsidySliderChanged(float value)
        {
            int requestedAmount = Mathf.RoundToInt(value);
            EconomyManager.Instance?.SetGovernmentSubsidyAmount(requestedAmount);
        }

        private void UpdateSatisfactionUI(float score, SatisfactionTier tier)
        {
            if (satisfactionText != null)
            {
                satisfactionText.text = $"현재 만족도: {Mathf.RoundToInt(score)}%";
            }

            if (complaintsText != null)
            {
                if (ServiceAccessibilityManager.Instance != null)
                {
                    complaintsText.text = ServiceAccessibilityManager.Instance.GetServiceComplaintSummary();
                }
            }
        }

        // --- 5. 기획서 지정 고정 수치 / 미설치 여부 UI 표기 ---
        private void UpdateServiceMetricsUI()
        {
            if (ServiceAccessibilityManager.Instance == null) return;

            var mgr = ServiceAccessibilityManager.Instance;

            // 경찰 순찰 횟수
            if (patrolCountText != null)
            {
                patrolCountText.text = mgr.HasPoliceStation
                    ? $"경찰 하루 순찰: {mgr.DailyPolicePatrolCount} 회"
                    : "경찰 하루 순찰: 0 회 (경찰서 없음)";
            }

            // 병원 접근 시간
            if (hospitalTimeText != null)
            {
                hospitalTimeText.text = mgr.HasHospital
                    ? $"병원 접근 시간: {mgr.HospitalAccessTime:F1} 분"
                    : "병원 접근 시간: 미설치 (위험)";
            }

            // 소방서 접근 시간
            if (fireStationTimeText != null)
            {
                fireStationTimeText.text = mgr.HasFireStation
                    ? $"소방서 접근 시간: {fireStationTimeText:F1} 분"
                    : "소방서 접근 시간: 미설치 (위험)";
            }

            // 편의시설 접근 시간
            if (convenienceTimeText != null)
            {
                convenienceTimeText.text = mgr.HasConvenienceStore
                    ? $"편의시설 접근 시간: {mgr.ConvenienceAccessTime:F1} 분"
                    : "편의시설 접근 시간: 미설치";
            }
        }
    }
}
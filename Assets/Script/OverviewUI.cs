using UnityEngine;
using UnityEngine.UI;
using TMPro;
using InfraTown.Satisfaction;
using InfraTown.Population;
using InfraTown.Economy;
using InfraTown.Power;
using InfraTown.TimeSystem;

namespace InfraTown.UI
{
    public class OverviewUI : MonoBehaviour
    {
        [Header("전체 UI 패널 제어")]
        [SerializeField] private GameObject overviewPanel; // 전체 UI 창 패널
        [SerializeField] private Button closeButton;       // 닫기 버튼

        [Header("1. 전기 현황")]
        [SerializeField] private TextMeshProUGUI powerText; // 예: "사용 중 전기: 120 / 사용 가능: 200 kW"

        [Header("2. 인구 현황")]
        [SerializeField] private TextMeshProUGUI populationText; // 예: "현재 인구: 85명 / 한계: 100명"

        [Header("3. 재정 및 지원금 조절 (Slider)")]
        [SerializeField] private TextMeshProUGUI subsidyText; // 예: "신청 지원금: 12,000 / 최대 지원 가능: 15,000 원"
        [SerializeField] private Slider subsidySlider;       // 지원금 조절 슬라이더

        [Header("4. 만족도 및 불만 사항")]
        [SerializeField] private TextMeshProUGUI satisfactionText; // 예: "현재 만족도: 75%"
        [SerializeField] private Image satisfactionFaceImage;      // 만족도 표정 아이콘
        [SerializeField] private TextMeshProUGUI complaintsText;    // 예: "• 필수 시설(병원)이 부족합니다."

        [Header("5. 치안 및 필수/편의 시설 접근 시간")]
        [SerializeField] private TextMeshProUGUI patrolCountText;    // 예: "경찰 하루 순찰 횟수: 3회"
        [SerializeField] private TextMeshProUGUI hospitalTimeText;   // 예: "병원 도착 평균 시간: 4.2분"
        [SerializeField] private TextMeshProUGUI fireStationTimeText;// 예: "소방서 도착 평균 시간: 2.1분"
        [SerializeField] private TextMeshProUGUI convenienceTimeText;// 예: "편의시설 접근 평균 시간: 1.5분"

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
        }

        private void UnregisterEvents()
        {
            if (SatisfactionManager.Instance != null)
                SatisfactionManager.Instance.OnSatisfactionChanged -= UpdateSatisfactionUI;

            if (PopulationManager.Instance != null)
                PopulationManager.Instance.OnPopulationChanged -= UpdatePopulationUI;

            if (EconomyManager.Instance != null)
                EconomyManager.Instance.OnSubsidyAmountChanged -= UpdateSubsidyUI;
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

        // --- 1. 전기 UI 실제 연동 ---
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

        // --- 2. 인구 UI ---
        private void UpdatePopulationUI(int current, int maxCapacity)
        {
            if (populationText != null)
            {
                populationText.text = $"인구 현황: {current}명 / 한계: {maxCapacity}명";
            }
        }

        // --- 3. 정부 지원금 조절 UI ---
        private void UpdateSubsidyUI(int requested, int maxEligible)
        {
            if (subsidySlider != null)
            {
                subsidySlider.minValue = 0;
                subsidySlider.maxValue = maxEligible;
                subsidySlider.SetValueWithoutNotify(requested); // 대문자 S 수정 적용
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

        // --- 4. 만족도 및 불만 사항 UI ---
        private void UpdateSatisfactionUI(float score, SatisfactionTier tier)
        {
            if (satisfactionText != null)
            {
                satisfactionText.text = $"현재 만족도: {Mathf.RoundToInt(score)}%";
            }

            if (complaintsText != null)
            {
                if (score < 40f)
                    complaintsText.text = "• 필수 시설(병원/소방서)이 매우 부족합니다!\n• 인구 과밀 또는 정전으로 환경이 악화되었습니다.";
                else if (score < 60f)
                    complaintsText.text = "• 주변에 편의 시설이 더 필요합니다.";
                else
                    complaintsText.text = "• 특별한 불만 사항이 없습니다. 주민들이 만족하고 있습니다.";
            }
        }

        // --- 5. 필수/편의 시설 접근 시간 및 순찰 횟수 UI ---
        private void UpdateServiceMetricsUI()
        {
            if (patrolCountText != null)
                patrolCountText.text = "경찰 하루 순찰: 3 회";

            if (hospitalTimeText != null)
                hospitalTimeText.text = "병원 평균 접근 시간: 4.5 분";

            if (fireStationTimeText != null)
                fireStationTimeText.text = "소방서 평균 접근 시간: 2.1 분";

            if (convenienceTimeText != null)
                convenienceTimeText.text = "편의시설 접근 시간: 1.2 분";
        }
    }
}
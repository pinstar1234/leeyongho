using UnityEngine;
using UnityEngine.UI;
using TMPro;
using InfraTown.Satisfaction;
using InfraTown.Population;
using InfraTown.Economy;
using InfraTown.Power;
using InfraTown.Services;
using InfraTown.TimeSystem;
using DG.Tweening; // ★ DOTween 네임스페이스 추가

namespace InfraTown.UI
{
    public class OverviewUI : MonoBehaviour
    {
        [Header("전체 UI 패널 제어")]
        [SerializeField] private GameObject overviewPanel; // 전체 UI 창 패널
        [SerializeField] private Button closeButton;       // 닫기 버튼
        [SerializeField] private RectTransform windowRect; // ★ 슬라이드될 실제 팝업창(Window)의 RectTransform
        [SerializeField] private CanvasGroup dimCanvasGroup;// ★ 뒷배경 어두운 DimBackDrop의 CanvasGroup

        [Header("애니메이션 설정")]
        [SerializeField] private float slideDuration = 0.4f; // 슬라이드 속도
        [SerializeField] private Ease slideInEase = Ease.OutCubic; // 들어올 때 감속 효과
        [SerializeField] private Ease slideOutEase = Ease.InCubic; // 나갈 때 가속 효과

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

        private Vector2 _windowTargetPos; // 화면 중앙 원래 위치 (보통 Vector2.zero)
        private float _slideDistance = 1400f; // 오른쪽으로 빠져나갈 거리 (화면 너비 기준)
        private Tween _slideTween;
        private Tween _fadeTween;

        private void Awake()
        {
            // Window의 원래 목표 위치 기억 (화면 중앙 정렬일 경우 (0,0))
            if (windowRect != null)
            {
                _windowTargetPos = windowRect.anchoredPosition;
            }
        }

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
            _slideTween?.Kill();
            _fadeTween?.Kill();
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

        // ==========================================
        // ★ 오른쪽에서 왼쪽으로 부드럽게 슬라이드 등장
        // ==========================================
        public void ShowOverview()
        {
            if (overviewPanel == null || windowRect == null) return;

            overviewPanel.SetActive(true);
            TimeManager.Instance?.RequestEventPause(this);
            RefreshAllData();

            // 이전 트윈 제거
            _slideTween?.Kill();
            _fadeTween?.Kill();

            // 1. Window 위치를 화면 오른쪽 바깥으로 즉시 이동
            Vector2 startPos = _windowTargetPos + new Vector2(_slideDistance, 0f);
            windowRect.anchoredPosition = startPos;

            // 2. 오른쪽 바깥에서 원래 중앙 위치로 스르륵 이동 (SetUpdate(true)로 일시정지 중에도 작동)
            _slideTween = windowRect.DOAnchorPos(_windowTargetPos, slideDuration)
                .SetEase(slideInEase)
                .SetUpdate(true); // ★ 시간 정지 상태에서도 애니메이션 정상 재생

            // 3. 뒷배경 어두운 DimBackDrop 페이드 인
            if (dimCanvasGroup != null)
            {
                dimCanvasGroup.alpha = 0f;
                _fadeTween = dimCanvasGroup.DOFade(1f, slideDuration * 0.8f).SetUpdate(true);
            }
        }

        // ==========================================
        // ★ 오른쪽으로 스르륵 빠져나가며 닫히기
        // ==========================================
        public void HideOverview()
        {
            if (overviewPanel == null || windowRect == null) return;

            _slideTween?.Kill();
            _fadeTween?.Kill();

            Vector2 exitPos = _windowTargetPos + new Vector2(_slideDistance, 0f);

            // 1. 오른쪽 화면 밖으로 이동
            _slideTween = windowRect.DOAnchorPos(exitPos, slideDuration * 0.8f)
                .SetEase(slideOutEase)
                .SetUpdate(true)
                .OnComplete(() =>
                {
                    overviewPanel.SetActive(false);
                    TimeManager.Instance?.ReleaseEventPause(this);
                });

            // 2. 뒷배경 페이드 아웃
            if (dimCanvasGroup != null)
            {
                _fadeTween = dimCanvasGroup.DOFade(0f, slideDuration * 0.6f).SetUpdate(true);
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

        private void UpdateServiceMetricsUI()
        {
            if (ServiceAccessibilityManager.Instance == null) return;

            var mgr = ServiceAccessibilityManager.Instance;

            if (patrolCountText != null)
            {
                patrolCountText.text = mgr.HasPoliceStation
                    ? $"경찰 하루 순찰: {mgr.DailyPolicePatrolCount} 회"
                    : "경찰 하루 순찰: 0 회 (경찰서 없음)";
            }

            if (hospitalTimeText != null)
            {
                hospitalTimeText.text = mgr.HasHospital
                    ? $"병원 접근 시간: {mgr.HospitalAccessTime:F1} 분"
                    : "병원 접근 시간: 미설치 (위험)";
            }

            if (fireStationTimeText != null)
            {
                fireStationTimeText.text = mgr.HasFireStation
                    ? $"소방서 접근 시간: {mgr.FireStationAccessTime:F1} 분"
                    : "소방서 접근 시간: 미설치 (위험)";
            }

            if (convenienceTimeText != null)
            {
                convenienceTimeText.text = mgr.HasConvenienceStore
                    ? $"편의시설 접근 시간: {mgr.ConvenienceAccessTime:F1} 분"
                    : "편의시설 접근 시간: 미설치";
            }
        }
    }
}
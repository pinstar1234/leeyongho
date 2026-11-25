using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace InfraTown.TimeSystem
{
    public class TimeUI : MonoBehaviour
    {
        [Header("UI Text Component")]
        [SerializeField] private TextMeshProUGUI dateText; // 예: "1년차 1월 3주차"
        [SerializeField] private TextMeshProUGUI seasonText;

        [Header("Speed Buttons")]
        [SerializeField] private Button btnPause;
        [SerializeField] private Button btnSpeed1;
        [SerializeField] private Button btnSpeed2;
        [SerializeField] private Button btnSpeed3;
        [SerializeField] private Button btnWeekSkip;

        [Header("버튼 강조 색상 설정")]
        [SerializeField] private Color activeButtonColor = new Color(0.25f, 0.75f, 0.45f, 1f);   // 선택됨 (초록)
        [SerializeField] private Color pauseActiveColor = new Color(0.9f, 0.35f, 0.35f, 1f);    // 정지 선택됨 (빨강)
        [SerializeField] private Color inactiveButtonColor = new Color(0.25f, 0.28f, 0.35f, 1f);// 비활성 (어두운 회색)

        private void Start()
        {
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnDateChanged += UpdateDateUI; 
                TimeManager.Instance.OnSpeedChanged += UpdateSpeedButtonVisuals; 

                UpdateDateUI(TimeManager.Instance.CurrentYear, TimeManager.Instance.CurrentMonth, TimeManager.Instance.CurrentWeek); 
                UpdateSpeedButtonVisuals(TimeManager.Instance.CurrentSpeed); 
            }

            if (btnPause != null) btnPause.onClick.AddListener(() => OnClickSpeedButton(btnPause, GameSpeed.Pause)); 
            if (btnSpeed1 != null) btnSpeed1.onClick.AddListener(() => OnClickSpeedButton(btnSpeed1, GameSpeed.Normal));
            if (btnSpeed2 != null) btnSpeed2.onClick.AddListener(() => OnClickSpeedButton(btnSpeed2, GameSpeed.Fast));
            if (btnSpeed3 != null) btnSpeed3.onClick.AddListener(() => OnClickSpeedButton(btnSpeed3, GameSpeed.SuperFast)); 
            if (btnWeekSkip != null) btnWeekSkip.onClick.AddListener(OnClickWeekSkip);
            UpdateWeekSkipButton();
        }

        private void LateUpdate()
        {
            UpdateWeekSkipButton();
        }

        private void UpdateWeekSkipButton()
        {
            if (btnWeekSkip != null)
            {
                var timeManager = TimeManager.Instance;
                btnWeekSkip.interactable = timeManager != null && timeManager.CanSkip;
            }
        }

        private void OnClickWeekSkip()
        {
            TimeManager.Instance?.StartMonthSkip();
            UpdateWeekSkipButton();
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnDateChanged -= UpdateDateUI;
                TimeManager.Instance.OnSpeedChanged -= UpdateSpeedButtonVisuals;
            }

            if (btnWeekSkip != null) btnWeekSkip.onClick.RemoveListener(OnClickWeekSkip);
        }
        private void OnClickSpeedButton(Button clickedBtn, GameSpeed speed)
        {
            TimeManager.Instance?.SetGameSpeed(speed); 

            // 일시정지(timeScale = 0) 중에도 눌리는 느낌이 나도록 SetUpdate(true) 적용
            clickedBtn.transform.DOKill();
            clickedBtn.transform.localScale = Vector3.one;
            clickedBtn.transform.DOPunchScale(Vector3.one * 0.15f, 0.2f, 8, 1f).SetUpdate(true);
        }

        private void UpdateDateUI(int year, int month, int week)
        {
            if (dateText != null)
            {
                dateText.text = $"{year}년차 {month}월 {week}주차"; // 시간이 주 단위로 바뀜 (2026-11-24): 세 번째 값은 이번 달 몇 주차(1~4) 
            }
            if (seasonText != null) seasonText.text = SeasonUtil.ToKorean(SeasonUtil.FromMonth(month));
        }
        private void UpdateSpeedButtonVisuals(GameSpeed currentSpeed)
        {
            SetButtonColor(btnPause, currentSpeed == GameSpeed.Pause ? pauseActiveColor : inactiveButtonColor); 
            SetButtonColor(btnSpeed1, currentSpeed == GameSpeed.Normal ? activeButtonColor : inactiveButtonColor); 
            SetButtonColor(btnSpeed2, currentSpeed == GameSpeed.Fast ? activeButtonColor : inactiveButtonColor); 
            SetButtonColor(btnSpeed3, currentSpeed == GameSpeed.SuperFast ? activeButtonColor : inactiveButtonColor); 
        }

        private void SetButtonColor(Button btn, Color color)
        {
            if (btn != null && btn.targetGraphic != null)
            {
                btn.targetGraphic.color = color;
            }
        }
    }
}

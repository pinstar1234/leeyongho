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
        [SerializeField] private TextMeshProUGUI yearText;
        [SerializeField] private TextMeshProUGUI seasonText;
        [SerializeField] private Image[] weekIndicators;

        private static readonly Color CurrentWeekColor = new Color32(89, 112, 82, 255);
        private static readonly Color PastWeekColor = new Color32(150, 170, 131, 255);
        private static readonly Color UpcomingWeekColor = new Color32(232, 223, 202, 255);

        [Header("Season Badge")]
        [SerializeField] private Image seasonBadgeBackground;
        [SerializeField] private Image seasonIcon;
        [SerializeField] private Sprite springIcon;
        [SerializeField] private Sprite summerIcon;
        [SerializeField] private Sprite autumnIcon;
        [SerializeField] private Sprite winterIcon;

        [Header("Speed Buttons")]
        [SerializeField] private Button btnPause;
        [SerializeField] private Button btnSpeed1;
        [SerializeField] private Button btnSpeed2;
        [SerializeField] private Button btnSpeed3;
        [SerializeField] private Button btnWeekSkip;
        [SerializeField] private Sprite selectedSpeedButtonSprite;

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
            if (yearText != null)
                yearText.text = $"{year}년차";
            if (dateText != null)
                dateText.text = yearText != null ? $"{month}월 {week}주차" : $"{year}년차 {month}월 {week}주차";

            UpdateSeasonBadge(SeasonUtil.FromMonth(month));
            UpdateWeekIndicators(week);
        }

        private void UpdateWeekIndicators(int currentWeek)
        {
            if (weekIndicators == null) return;

            for (int i = 0; i < weekIndicators.Length; i++)
            {
                if (weekIndicators[i] == null) continue;

                int week = i + 1;
                weekIndicators[i].color = week == currentWeek
                    ? CurrentWeekColor
                    : week < currentWeek ? PastWeekColor : UpcomingWeekColor;
            }
        }

        private void UpdateSeasonBadge(Season season)
        {
            if (seasonText != null)
                seasonText.text = SeasonUtil.ToKorean(season);

            if (seasonBadgeBackground == null || seasonIcon == null) return;

            var (icon, foreground, background) = season switch
            {
                Season.Spring => (springIcon, new Color32(255, 187, 213, 255), new Color32(71, 42, 57, 255)),
                Season.Summer => (summerIcon, new Color32(255, 215, 112, 255), new Color32(61, 53, 30, 255)),
                Season.Autumn => (autumnIcon, new Color32(255, 170, 118, 255), new Color32(71, 45, 38, 255)),
                _ => (winterIcon, new Color32(184, 229, 255, 255), new Color32(36, 58, 82, 255))
            };

            seasonIcon.sprite = icon;
            seasonBadgeBackground.color = background;
            if (seasonText != null)
                seasonText.color = foreground;
        }
        private void UpdateSpeedButtonVisuals(GameSpeed currentSpeed)
        {
            SetSpeedButtonVisual(btnPause, currentSpeed == GameSpeed.Pause, pauseActiveColor);
            SetSpeedButtonVisual(btnSpeed1, currentSpeed == GameSpeed.Normal, activeButtonColor);
            SetSpeedButtonVisual(btnSpeed2, currentSpeed == GameSpeed.Fast, activeButtonColor);
            SetSpeedButtonVisual(btnSpeed3, currentSpeed == GameSpeed.SuperFast, activeButtonColor);
        }

        private void SetSpeedButtonVisual(Button button, bool selected, Color selectedColor)
        {
            if (button == null || button.targetGraphic == null) return;

            button.targetGraphic.color = selected ? selectedColor : inactiveButtonColor;
            if (selectedSpeedButtonSprite != null && button.image != null)
                button.image.overrideSprite = selected ? selectedSpeedButtonSprite : null;
        }
    }
}

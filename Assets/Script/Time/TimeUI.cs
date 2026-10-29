using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace InfraTown.TimeSystem
{
    public class TimeUI : MonoBehaviour
    {
        [Header("UI Text Component")]
        [SerializeField] private TextMeshProUGUI dateText; // 예: "1년차 1월 15일"

        [Header("Speed Buttons")]
        [SerializeField] private Button btnPause;
        [SerializeField] private Button btnSpeed1;
        [SerializeField] private Button btnSpeed2;
        [SerializeField] private Button btnSpeed3;

        private void Start()
        {
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnDateChanged += UpdateDateUI;
                UpdateDateUI(TimeManager.Instance.CurrentYear, TimeManager.Instance.CurrentMonth, TimeManager.Instance.CurrentDay);
            }

            if (btnPause != null) btnPause.onClick.AddListener(() => TimeManager.Instance?.SetGameSpeed(GameSpeed.Pause));
            if (btnSpeed1 != null) btnSpeed1.onClick.AddListener(() => TimeManager.Instance?.SetGameSpeed(GameSpeed.Normal));
            if (btnSpeed2 != null) btnSpeed2.onClick.AddListener(() => TimeManager.Instance?.SetGameSpeed(GameSpeed.Fast));
            if (btnSpeed3 != null) btnSpeed3.onClick.AddListener(() => TimeManager.Instance?.SetGameSpeed(GameSpeed.SuperFast));
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnDateChanged -= UpdateDateUI;
            }
        }

        private void UpdateDateUI(int year, int month, int day)
        {
            if (dateText != null)
            {
                dateText.text = $"{year}년차 {month}월 {day}일";
            }
        }
    }
}
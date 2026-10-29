using UnityEngine;
using TMPro;

namespace InfraTown.Economy
{
    public class EconomyUI : MonoBehaviour
    {
        [Header("UI Component")]
        [SerializeField] private TextMeshProUGUI moneyText; // 잔액 표시 (예: "10,000 원")

        private void Start()
        {
            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnMoneyChanged += UpdateMoneyUI;
                // 시작 자금 10,000원 표시
                UpdateMoneyUI(EconomyManager.Instance.CurrentMoney, 0);
            }
        }

        private void OnDestroy()
        {
            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnMoneyChanged -= UpdateMoneyUI;
            }
        }

        private void UpdateMoneyUI(int currentBalance, int changeAmount)
        {
            if (moneyText != null)
            {
                // 천 단위 쉼표 표기 (예: 10,000 원)
                moneyText.text = $"{currentBalance:N0} 원";
            }
        }
    }
}
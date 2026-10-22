using UnityEngine;
using TMPro;

namespace InfraTown.Population
{
    public class PopulationUI : MonoBehaviour
    {
        [Header("UI Component")]
        [SerializeField] private TextMeshProUGUI populationText; // 예: "45 / 50 명"

        private void Start()
        {
            if (PopulationManager.Instance != null)
            {
                PopulationManager.Instance.OnPopulationChanged += UpdateUI;
                UpdateUI(PopulationManager.Instance.CurrentPopulation, PopulationManager.Instance.MaxPopulationCapacity);
            }
        }

        private void OnDestroy()
        {
            if (PopulationManager.Instance != null)
            {
                PopulationManager.Instance.OnPopulationChanged -= UpdateUI;
            }
        }

        private void UpdateUI(int current, int maxCapacity)
        {
            if (populationText != null)
            {
                populationText.text = $"인구: {current} / {maxCapacity} 명";
            }
        }
    }
}
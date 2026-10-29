using UnityEngine;
using UnityEngine.UI;
using TMPro;
using InfraTown.Building;
using InfraTown.Population;

namespace InfraTown.UI
{
    public class BuildingSlotUI : MonoBehaviour
    {
        [Header("UI Data")]
        [SerializeField] private BuildingData buildingData;

        [Header("UI Components")]
        [SerializeField] private Button button;
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI lockInfoText; // 예: "잠김 (필요 인구: 50명)"
        [SerializeField] private GameObject lockOverlay;      // 자물쇠 어두운 패널

        private void Start()
        {
            if (button != null)
            {
                button.onClick.AddListener(OnClickSlot);
            }

            if (PopulationManager.Instance != null)
            {
                PopulationManager.Instance.OnPopulationChanged += HandlePopulationChanged;
            }

            InitUI();
        }

        private void OnDestroy()
        {
            if (PopulationManager.Instance != null)
            {
                PopulationManager.Instance.OnPopulationChanged -= HandlePopulationChanged;
            }
        }

        public void SetupSlot(BuildingData data)
        {
            buildingData = data;
            InitUI();
        }

        private void InitUI()
        {
            if (buildingData == null) return;

            if (nameText != null) nameText.text = buildingData.buildingName;
            if (iconImage != null && buildingData.buildingIcon != null) iconImage.sprite = buildingData.buildingIcon;

            int currentPop = PopulationManager.Instance != null ? PopulationManager.Instance.CurrentPopulation : 0;
            UpdateLockState(currentPop);
        }

        private void HandlePopulationChanged(int currentPop, int maxCapacity)
        {
            UpdateLockState(currentPop);
        }

        /// <summary>
        /// 현재 인구수에 따른 잠금/해금 비주얼 갱신
        /// </summary>
        private void UpdateLockState(int currentPop)
        {
            if (buildingData == null) return;

            bool isUnlocked = currentPop >= buildingData.requiredPopulation;

            if (button != null) button.interactable = isUnlocked;
            if (lockOverlay != null) lockOverlay.SetActive(!isUnlocked);

            if (lockInfoText != null)
            {
                if (isUnlocked)
                {
                    lockInfoText.text = $"{buildingData.constructionCost:N0} 원";
                }
                else
                {
                    lockInfoText.text = $"🔒 필요 인구 {buildingData.requiredPopulation}명";
                }
            }
        }

        private void OnClickSlot()
        {
            if (buildingData != null)
            {
                BuildingManager.Instance?.SelectBuildingToBuild(buildingData);
            }
        }
    }
}
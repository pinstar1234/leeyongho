using UnityEngine;
using UnityEngine.UI;
using InfraTown.Building;

namespace InfraTown.UI
{
    public class BuildingTestUI : MonoBehaviour
    {
        [Header("Building Data ScriptableObjects")]
        [SerializeField] private BuildingData smallBuildingData;
        [SerializeField] private BuildingData mediumBuildingData;
        [SerializeField] private BuildingData largeBuildingData;

        [Header("UI Buttons")]
        [SerializeField] private Button btnSmall;
        [SerializeField] private Button btnMedium;
        [SerializeField] private Button btnLarge;
        [SerializeField] private Button btnCancel;

        private void Start()
        {
            if (btnSmall != null) btnSmall.onClick.AddListener(() => OnSelectBuilding(smallBuildingData));
            if (btnMedium != null) btnMedium.onClick.AddListener(() => OnSelectBuilding(mediumBuildingData));
            if (btnLarge != null) btnLarge.onClick.AddListener(() => OnSelectBuilding(largeBuildingData));
            if (btnCancel != null) btnCancel.onClick.AddListener(OnCancel);
        }

        private void OnSelectBuilding(BuildingData data)
        {
            if (data != null && BuildingManager.Instance != null)
            {
                BuildingManager.Instance.SelectBuildingToBuild(data);
            }
        }

        private void OnCancel()
        {
            if (BuildingManager.Instance != null)
            {
                BuildingManager.Instance.CancelBuildingMode();
            }
        }
    }
}
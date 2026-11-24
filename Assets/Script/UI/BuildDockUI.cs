using InfraTown.Building;
using UnityEngine;
using UnityEngine.UI;

namespace InfraTown.UI
{
    public class BuildDockUI : MonoBehaviour
    {
        [SerializeField] private BuildingManager buildingManager;
        [SerializeField] private BuildingUIManager buildingUI;
        [SerializeField] private GameObject statusBar;
        [SerializeField] private GameObject timeControls;
        [SerializeField] private GameObject buildControls;
        [SerializeField] private GameObject topControls;
        [SerializeField] private GameObject buildingDetails;
        [SerializeField] private Button foldButton;
        [SerializeField] private RectTransform arrow;

        private RectTransform _foldRect;
        private Vector2 _expandedPosition;
        private bool _collapsed;
        private bool _placing;

        private void Start()
        {
            _foldRect = (RectTransform)foldButton.transform;
            _expandedPosition = _foldRect.anchoredPosition;
            foldButton.onClick.AddListener(ToggleDock);
            buildingManager.OnPlacementModeChanged += SetPlacementMode;
            SetPlacementMode(buildingManager.IsPlacingBuilding);
        }

        private void OnDestroy()
        {
            if (foldButton != null) foldButton.onClick.RemoveListener(ToggleDock);
            if (buildingManager != null) buildingManager.OnPlacementModeChanged -= SetPlacementMode;
        }

        private void ToggleDock()
        {
            _collapsed = !_collapsed;
            if (_collapsed) buildingUI.CloseBuildMenu();
            RefreshVisibility();
        }

        private void SetPlacementMode(bool placing)
        {
            _placing = placing;
            if (_placing) buildingUI.CloseBuildMenu();
            RefreshVisibility();
        }

        private void RefreshVisibility()
        {
            bool showDock = !_collapsed && !_placing;
            statusBar.SetActive(showDock);
            timeControls.SetActive(showDock);
            buildControls.SetActive(showDock);
            topControls.SetActive(!_placing);
            buildingDetails.SetActive(!_placing);
            foldButton.gameObject.SetActive(!_placing);

            _foldRect.anchoredPosition = _collapsed
                ? new Vector2(_expandedPosition.x, _foldRect.rect.height * 0.5f + 4f)
                : _expandedPosition;
            arrow.localRotation = Quaternion.Euler(0f, 0f, _collapsed ? 180f : 0f);
        }
    }
}

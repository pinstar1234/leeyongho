using UnityEngine;
using UnityEngine.UI;
using TMPro;
using InfraTown.Building;
using DG.Tweening; //  DOTween 네임스페이스 추가

namespace InfraTown.UI
{
    public class BuildingUIManager : MonoBehaviour
    {
        public static BuildingUIManager Instance { get; private set; }

        [Header("Action UI (좌/우 버튼 패널)")]
        public GameObject actionPanel;
        public Button btnInfo;
        public Button btnDemolish;

        [Header("Info UI (팝업 패널)")]
        public GameObject infoPanel;
        public CanvasGroup infoPanelCanvasGroup; //  투명도 조절을 위한 CanvasGroup

        public TextMeshProUGUI txtBuildingName;
        public Image imgBuildingIcon;
        public TextMeshProUGUI txtCategory;
        public TextMeshProUGUI txtPower;
        public TextMeshProUGUI txtCost;
        public TextMeshProUGUI txtDescription;
        public Button btnCloseInfo;

        private BuildingInstance _selectedBuilding;
        private Camera _mainCamera;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _mainCamera = Camera.main;
        }

        private void Start()
        {
            btnInfo.onClick.AddListener(ShowInfoPanel);
            btnDemolish.onClick.AddListener(DemolishSelectedBuilding);
            btnCloseInfo.onClick.AddListener(CloseAllUI);
            CloseAllUI();
        }

        public void OnBuildingClicked(BuildingInstance building)
        {
            _selectedBuilding = building;
            infoPanel.SetActive(false);

            //  Action Panel 애니메이션 연출 (통통 튀며 등장)
            actionPanel.SetActive(true);
            actionPanel.transform.position = _mainCamera.WorldToScreenPoint(building.transform.position);

            // 크기를 0으로 만들었다가 0.3초 동안 원래 크기(1)로 커지게 함
            actionPanel.transform.localScale = Vector3.zero;
            actionPanel.transform.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack);
        }

        private void ShowInfoPanel()
        {
            if (_selectedBuilding == null) return;

            actionPanel.SetActive(false);
            infoPanel.SetActive(true);

            // 데이터 업데이트 로직 (기존과 동일)
            BuildingData data = _selectedBuilding.Data;
            txtBuildingName.text = data.buildingName;

            if (data.buildingIcon != null)
            {
                imgBuildingIcon.sprite = data.buildingIcon;
                imgBuildingIcon.gameObject.SetActive(true);
            }
            else
            {
                imgBuildingIcon.gameObject.SetActive(false);
            }

            txtCategory.text = $"<b>분류:</b> {GetCategoryName(data.category)}";
            txtPower.text = $"<b>전력 소비:</b> {data.requiredPower} kW";
            txtCost.text = $"<b>유지비:</b> ${data.maintenanceCost}/월\n<b>건설비:</b> ${data.constructionCost}";
            txtDescription.text = data.description;

            //  Info Panel 애니메이션 연출 (페이드 인 + 살짝 커지며 등장)
            if (infoPanelCanvasGroup != null)
            {
                // 투명도 0 -> 1 페이드 인
                infoPanelCanvasGroup.alpha = 0f;
                infoPanelCanvasGroup.DOFade(1f, 0.25f);
            }

            // 크기 0.8 -> 1 통통 튀며 등장
            infoPanel.transform.localScale = Vector3.one * 0.8f;
            infoPanel.transform.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack);
        }

        private void DemolishSelectedBuilding()
        {
            if (_selectedBuilding != null)
            {
                _selectedBuilding.DestroyBuilding();
                _selectedBuilding = null;
            }
            CloseAllUI();
        }

        public void CloseAllUI()
        {
            // Action Panel이 켜져있다면 작아지면서 닫히도록 연출
            if (actionPanel.activeSelf)
            {
                actionPanel.transform.DOKill();
                actionPanel.transform.DOScale(Vector3.zero, 0.2f).SetEase(Ease.InBack)
                    .OnComplete(() => actionPanel.SetActive(false));
            }

            // Info Panel(팝업)이 켜져있다면 서서히 사라지면서 닫히도록 연출
            if (infoPanel.activeSelf)
            {
                infoPanel.transform.DOKill();

                // 투명도 1 -> 0 페이드 아웃
                if (infoPanelCanvasGroup != null)
                {
                    infoPanelCanvasGroup.DOKill();
                    infoPanelCanvasGroup.DOFade(0f, 0.2f);
                }

                // 크기가 1 -> 0으로 쪼그라들고 (InBack), 애니메이션이 끝나면 SetActive(false) 실행
                infoPanel.transform.DOScale(Vector3.zero, 0.25f).SetEase(Ease.InBack)
                    .OnComplete(() =>
                    {
                        infoPanel.SetActive(false);
                    });
            }
        }

        private string GetCategoryName(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.Essential: return "필수 시설";
                case BuildingType.Facility: return "편의 시설";
                case BuildingType.Nimbys: return "혐오 시설";
                default: return "알 수 없음";
            }
        }
    }
}
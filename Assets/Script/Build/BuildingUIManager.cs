using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using InfraTown.Building;
using DG.Tweening;

namespace InfraTown.UI
{
    public class BuildingUIManager : MonoBehaviour
    {
        public static BuildingUIManager Instance { get; private set; }

        [Header("Action UI (정보/철거 버튼 패널)")]
        public GameObject actionPanel;
        public Button btnInfo;
        public Button btnDemolish;

        [Header("UI Panels")]
        [SerializeField] private GameObject buildSubMenuPanel;
        [SerializeField] private CanvasGroup subMenuCanvasGroup;

        [Header("Info UI (상세 정보 팝업 패널)")]
        public GameObject infoPanel;
        public CanvasGroup infoPanelCanvasGroup;

        [Header("Category Main Buttons (메인 시설 카테고리 버튼)")]
        [SerializeField] private Button btnFacility;   // 편의시설
        [SerializeField] private Button btnEssential;  // 필수시설
        [SerializeField] private Button btnNimbys;     // 혐오시설
        [SerializeField] private Button btnHousing;    // 주택 (추가 연동)

        [Header("Sub Buttons (크기 선택 버튼)")]
        [SerializeField] private Button smallButton;   // Small (1x1)
        [SerializeField] private Button mediumButton;  // Medium (2x2)
        [SerializeField] private Button largeButton;   // Large (3x3)
        [SerializeField] private Button cancelButton;  // Cancel (취소)

        [Header("Building Info Elements")]
        public TextMeshProUGUI txtBuildingName;
        public Image imgBuildingIcon;
        public TextMeshProUGUI txtCategory;
        public TextMeshProUGUI txtPower;
        public TextMeshProUGUI txtCost;
        public TextMeshProUGUI txtDescription;
        public Button btnCloseInfo;

        private BuildingInstance _selectedBuilding;
        private Camera _mainCamera;

        // 애니메이션 및 현재 선택된 카테고리 상태 관리
        private BuildingType _currentSelectedCategory = BuildingType.Facility;
        private Button _lastClickedCategoryButton;
        private List<RectTransform> _subButtonRects = new List<RectTransform>();
        private List<Vector2> _subButtonOriginPositions = new List<Vector2>();
        private Sequence _buildMenuSequence;
        private bool _isBuildMenuOpen = false;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _mainCamera = Camera.main;

            // 서브 버튼 캐싱
            Button[] subBtns = { smallButton, mediumButton, largeButton, cancelButton };
            foreach (var btn in subBtns)
            {
                if (btn != null)
                {
                    RectTransform rt = btn.GetComponent<RectTransform>();
                    _subButtonRects.Add(rt);
                    _subButtonOriginPositions.Add(rt.anchoredPosition);
                }
            }

            // CanvasGroup 검사 및 설정
            if (buildSubMenuPanel != null && subMenuCanvasGroup == null)
            {
                subMenuCanvasGroup = buildSubMenuPanel.GetComponent<CanvasGroup>();
                if (subMenuCanvasGroup == null)
                    subMenuCanvasGroup = buildSubMenuPanel.AddComponent<CanvasGroup>();
            }

            if (buildSubMenuPanel != null)
                buildSubMenuPanel.SetActive(false);

            // 메인 카테고리 버튼 이벤트 바인딩
            if (btnFacility != null)
                btnFacility.onClick.AddListener(() => OnClickCategoryMainButton(btnFacility, BuildingType.Facility));

            if (btnEssential != null)
                btnEssential.onClick.AddListener(() => OnClickCategoryMainButton(btnEssential, BuildingType.Essential));

            if (btnNimbys != null)
                btnNimbys.onClick.AddListener(() => OnClickCategoryMainButton(btnNimbys, BuildingType.Nimbys));

            if (btnHousing != null)
                btnHousing.onClick.AddListener(() => OnClickCategoryMainButton(btnHousing, BuildingType.Housing));

            // 서브 버튼 이벤트 바인딩
            if (cancelButton != null)
                cancelButton.onClick.AddListener(CloseBuildMenu);

            if (smallButton != null)
                smallButton.onClick.AddListener(() => OnSelectBuildingSize(BuildingSizeType.Small));

            if (mediumButton != null)
                mediumButton.onClick.AddListener(() => OnSelectBuildingSize(BuildingSizeType.Medium));

            if (largeButton != null)
                largeButton.onClick.AddListener(() => OnSelectBuildingSize(BuildingSizeType.Large));
        }

        private void Start()
        {
            if (btnInfo != null) btnInfo.onClick.AddListener(ShowInfoPanel);
            if (btnDemolish != null) btnDemolish.onClick.AddListener(DemolishSelectedBuilding);
            if (btnCloseInfo != null) btnCloseInfo.onClick.AddListener(CloseAllUI);

            CloseAllUI();
        }

        // ==========================================
        // 카테고리 메인 버튼 클릭 처리
        // ==========================================
        private void OnClickCategoryMainButton(Button clickedBtn, BuildingType category)
        {
            clickedBtn.transform.DOKill();
            clickedBtn.transform.localScale = Vector3.one;
            clickedBtn.transform.DOPunchScale(Vector3.one * 0.2f, 0.25f, 10, 1f);

            // 열려있는 상태에서 동일 버튼 클릭 시 서브메뉴 닫기
            if (_isBuildMenuOpen && _lastClickedCategoryButton == clickedBtn)
            {
                CloseBuildMenu();
                _lastClickedCategoryButton = null;
                return;
            }

            _currentSelectedCategory = category;
            _lastClickedCategoryButton = clickedBtn;

            OpenBuildMenu();
        }

        public void OpenBuildMenu()
        {
            _isBuildMenuOpen = true;
            _buildMenuSequence?.Kill();
            _buildMenuSequence = DOTween.Sequence();

            buildSubMenuPanel.SetActive(true);

            if (subMenuCanvasGroup != null)
            {
                subMenuCanvasGroup.alpha = 0f;
                _buildMenuSequence.Join(subMenuCanvasGroup.DOFade(1f, 0.2f));
            }

            // 서브 버튼 순차 팝업 애니메이션
            for (int i = 0; i < _subButtonRects.Count; i++)
            {
                RectTransform rt = _subButtonRects[i];
                Vector2 targetPos = _subButtonOriginPositions[i];
                Vector2 startPos = targetPos - new Vector2(0, 45f);

                rt.DOKill();
                rt.anchoredPosition = startPos;
                rt.localScale = Vector3.zero;

                float delay = i * 0.05f;
                _buildMenuSequence.Insert(delay, rt.DOScale(Vector3.one, 0.35f).SetEase(Ease.OutBack, 2.5f));
                _buildMenuSequence.Insert(delay, rt.DOAnchorPos(targetPos, 0.35f).SetEase(Ease.OutCubic));
            }
        }

        public void CloseBuildMenu()
        {
            if (!_isBuildMenuOpen) return;
            _isBuildMenuOpen = false;
            _lastClickedCategoryButton = null;

            _buildMenuSequence?.Kill();
            _buildMenuSequence = DOTween.Sequence();

            for (int i = 0; i < _subButtonRects.Count; i++)
            {
                RectTransform rt = _subButtonRects[i];
                _buildMenuSequence.Join(rt.DOScale(Vector3.zero, 0.15f).SetEase(Ease.InBack));
            }

            if (subMenuCanvasGroup != null)
                _buildMenuSequence.Join(subMenuCanvasGroup.DOFade(0f, 0.15f));

            _buildMenuSequence.OnComplete(() =>
            {
                buildSubMenuPanel.SetActive(false);
                for (int i = 0; i < _subButtonRects.Count; i++)
                    _subButtonRects[i].anchoredPosition = _subButtonOriginPositions[i];
            });
        }

        // ==========================================
        // 크기 선택 시 BuildingManager 실제 데이터 호출 연동
        // ==========================================
        private void OnSelectBuildingSize(BuildingSizeType sizeType)
        {
            CloseBuildMenu();

            // BuildingManager와 연동하여 DB 조건에 맞는 건물 검색 후 건설 모드 진입
            if (BuildingManager.Instance != null)
            {
                BuildingData targetData = BuildingManager.Instance.GetBuildingDataByCategoryAndSize(_currentSelectedCategory, sizeType);

                if (targetData != null)
                {
                    BuildingManager.Instance.SelectBuildingToBuild(targetData);
                }
                else
                {
                    Debug.LogWarning($"[BuildingUIManager] 조건에 맞는 건물 DB를 찾을 수 없습니다. (카테고리: {_currentSelectedCategory}, 규격: {sizeType})");
                }
            }
        }

        // ==========================================
        // 건물 클릭, 정보창, 철거 제어
        // ==========================================
        public void OnBuildingClicked(BuildingInstance building)
        {
            _selectedBuilding = building;
            if (infoPanel != null) infoPanel.SetActive(false);

            if (actionPanel != null)
            {
                actionPanel.SetActive(true);
                actionPanel.transform.position = _mainCamera.WorldToScreenPoint(building.transform.position);
                actionPanel.transform.localScale = Vector3.zero;
                actionPanel.transform.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack);
            }
        }

        private void ShowInfoPanel()
        {
            if (_selectedBuilding == null || _selectedBuilding.Data == null) return;

            if (actionPanel != null) actionPanel.SetActive(false);
            if (infoPanel != null) infoPanel.SetActive(true);

            BuildingData data = _selectedBuilding.Data;

            if (txtBuildingName != null) txtBuildingName.text = data.buildingName;

            if (imgBuildingIcon != null)
            {
                if (data.buildingIcon != null)
                {
                    imgBuildingIcon.sprite = data.buildingIcon;
                    imgBuildingIcon.gameObject.SetActive(true);
                }
                else
                {
                    imgBuildingIcon.gameObject.SetActive(false);
                }
            }

            if (txtCategory != null) txtCategory.text = $"<b>분류:</b> {GetCategoryName(data.category)}";
            if (txtPower != null) txtPower.text = $"<b>전력 소비:</b> {data.requiredPower} kW";
            if (txtCost != null) txtCost.text = $"<b>유지비:</b> {data.maintenanceCost:N0}원/월\n<b>건설비:</b> {data.constructionCost:N0}원";
            if (txtDescription != null) txtDescription.text = data.description;

            if (infoPanelCanvasGroup != null)
            {
                infoPanelCanvasGroup.alpha = 0f;
                infoPanelCanvasGroup.DOFade(1f, 0.25f);
            }

            if (infoPanel != null)
            {
                infoPanel.transform.localScale = Vector3.one * 0.8f;
                infoPanel.transform.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack);
            }
        }

        private void DemolishSelectedBuilding()
        {
            if (_selectedBuilding != null)
            {
                // 주택 파괴 시 인구 및 건물 인스턴스 파괴
                Destroy(_selectedBuilding.gameObject);
                _selectedBuilding = null;
            }
            CloseAllUI();
        }

        public void CloseAllUI()
        {
            if (actionPanel != null && actionPanel.activeSelf)
            {
                actionPanel.transform.DOKill();
                actionPanel.transform.DOScale(Vector3.zero, 0.2f).SetEase(Ease.InBack)
                    .OnComplete(() => actionPanel.SetActive(false));
            }

            if (infoPanel != null && infoPanel.activeSelf)
            {
                infoPanel.transform.DOKill();
                if (infoPanelCanvasGroup != null)
                {
                    infoPanelCanvasGroup.DOKill();
                    infoPanelCanvasGroup.DOFade(0f, 0.2f);
                }
                infoPanel.transform.DOScale(Vector3.zero, 0.25f).SetEase(Ease.InBack)
                    .OnComplete(() => infoPanel.SetActive(false));
            }

            CloseBuildMenu();
        }

        private string GetCategoryName(BuildingType type)
        {
            return type switch
            {
                BuildingType.Essential => "필수 시설",
                BuildingType.Facility => "편의 시설",
                BuildingType.Nimbys => "불쾌 시설",
                BuildingType.Housing => "주택",
                _ => "미분류"
            };
        }
    }
}
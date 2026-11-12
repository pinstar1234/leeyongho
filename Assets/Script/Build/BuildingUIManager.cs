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
        public static BuildingUIManager Instance { get; private set; } //[cite: 18]

        [Header("Action UI (정보/철거 버튼 패널)")]
        public GameObject actionPanel; //[cite: 18]
        public Button btnInfo; //[cite: 18]
        public Button btnDemolish; //[cite: 18]

        [Header("1단계: 크기 선택 서브 메뉴 패널")]
        [SerializeField] private GameObject buildSubMenuPanel; //[cite: 18]
        [SerializeField] private CanvasGroup subMenuCanvasGroup; //[cite: 18]

        [Header("2단계: 건물 목록 선택 패널 (신규 연결)")]
        [SerializeField] private GameObject buildingListPanel;      // 건물 슬롯들이 들어갈 배경 패널
        [SerializeField] private Transform buildingSlotContainer;   // HorizontalLayoutGroup이 붙은 부모 Transform
        [SerializeField] private BuildingSlotUI buildingSlotPrefab; // BuildingSlotUI가 붙은 슬롯 프리팹

        [Header("Info UI (상세 정보 팝업 패널)")]
        public GameObject infoPanel; //[cite: 18]
        public CanvasGroup infoPanelCanvasGroup; //[cite: 18]

        [Header("Category Main Buttons (메인 시설 카테고리 버튼)")]
        [SerializeField] private Button btnFacility;   // 편의시설[cite: 18]
        [SerializeField] private Button btnEssential;  // 필수시설[cite: 18]
        [SerializeField] private Button btnNimbys;     // 혐오시설[cite: 18]
        [SerializeField] private Button btnHousing;    // 주택시설[cite: 18]

        [Header("Sub Buttons (크기 선택 버튼)")]
        [SerializeField] private Button smallButton;   // Small (1x1)[cite: 18]
        [SerializeField] private Button mediumButton;  // Medium (2x2)[cite: 18]
        [SerializeField] private Button largeButton;   // Large (3x3)[cite: 18]
        [SerializeField] private Button cancelButton;  // Cancel (취소)[cite: 18]

        [Header("Building Info Elements")]
        public TextMeshProUGUI txtBuildingName; //[cite: 18]
        public Image imgBuildingIcon; //[cite: 18]
        public TextMeshProUGUI txtCategory; //[cite: 18]
        public TextMeshProUGUI txtPower; //[cite: 18]
        public TextMeshProUGUI txtCost; //[cite: 18]
        public TextMeshProUGUI txtDescription; //[cite: 18]
        public Button btnCloseInfo; //[cite: 18]

        private BuildingInstance _selectedBuilding; //[cite: 18]
        private Camera _mainCamera; //[cite: 18]

        private BuildingType _currentSelectedCategory = BuildingType.Facility; //[cite: 18]
        private Button _lastClickedCategoryButton; //[cite: 18]
        private List<RectTransform> _subButtonRects = new List<RectTransform>(); //[cite: 18]
        private List<Vector2> _subButtonOriginPositions = new List<Vector2>(); //[cite: 18]
        private Sequence _buildMenuSequence; //[cite: 18]
        private bool _isBuildMenuOpen = false; //[cite: 18]

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; } //[cite: 18]
            Instance = this; //[cite: 18]
            _mainCamera = Camera.main; //[cite: 18]

            Button[] subBtns = { smallButton, mediumButton, largeButton, cancelButton }; //[cite: 18]
            foreach (var btn in subBtns)
            {
                if (btn != null)
                {
                    RectTransform rt = btn.GetComponent<RectTransform>();
                    _subButtonRects.Add(rt);
                    _subButtonOriginPositions.Add(rt.anchoredPosition);
                }
            }

            if (buildSubMenuPanel != null && subMenuCanvasGroup == null) //[cite: 18]
            {
                subMenuCanvasGroup = buildSubMenuPanel.GetComponent<CanvasGroup>();
                if (subMenuCanvasGroup == null)
                    subMenuCanvasGroup = buildSubMenuPanel.AddComponent<CanvasGroup>();
            }

            if (buildSubMenuPanel != null) buildSubMenuPanel.SetActive(false); //[cite: 18]
            if (buildingListPanel != null) buildingListPanel.SetActive(false);

            // 메인 카테고리 버튼 이벤트
            if (btnFacility != null) btnFacility.onClick.AddListener(() => OnClickCategoryMainButton(btnFacility, BuildingType.Facility)); //[cite: 18]
            if (btnEssential != null) btnEssential.onClick.AddListener(() => OnClickCategoryMainButton(btnEssential, BuildingType.Essential)); //[cite: 18]
            if (btnNimbys != null) btnNimbys.onClick.AddListener(() => OnClickCategoryMainButton(btnNimbys, BuildingType.Nimbys)); //[cite: 18]
            if (btnHousing != null) btnHousing.onClick.AddListener(() => OnClickCategoryMainButton(btnHousing, BuildingType.Housing)); //[cite: 18]

            // 크기 서브 버튼 이벤트
            if (cancelButton != null) cancelButton.onClick.AddListener(CloseBuildMenu); //[cite: 18]
            if (smallButton != null) smallButton.onClick.AddListener(() => OnSelectBuildingSize(BuildingSizeType.Small)); //[cite: 18]
            if (mediumButton != null) mediumButton.onClick.AddListener(() => OnSelectBuildingSize(BuildingSizeType.Medium)); //[cite: 18]
            if (largeButton != null) largeButton.onClick.AddListener(() => OnSelectBuildingSize(BuildingSizeType.Large)); //[cite: 18]
        }

        private void Start()
        {
            if (btnInfo != null) btnInfo.onClick.AddListener(ShowInfoPanel); //[cite: 18]
            if (btnDemolish != null) btnDemolish.onClick.AddListener(DemolishSelectedBuilding); //[cite: 18]
            if (btnCloseInfo != null) btnCloseInfo.onClick.AddListener(CloseAllUI); //[cite: 18]

            CloseAllUI(); //[cite: 18]
        }

        // ==========================================
        // 1단계: 카테고리 버튼 클릭 (편의/필수/혐오/주택)
        // ==========================================
        private void OnClickCategoryMainButton(Button clickedBtn, BuildingType category)
        {
            clickedBtn.transform.DOKill(); //[cite: 18]
            clickedBtn.transform.localScale = Vector3.one; //[cite: 18]
            clickedBtn.transform.DOPunchScale(Vector3.one * 0.2f, 0.25f, 10, 1f); //[cite: 18]

            CloseBuildingListPanel();

            if (_isBuildMenuOpen && _lastClickedCategoryButton == clickedBtn) //[cite: 18]
            {
                CloseBuildMenu(); //[cite: 18]
                _lastClickedCategoryButton = null; //[cite: 18]
                return;
            }

            _currentSelectedCategory = category; //[cite: 18]
            _lastClickedCategoryButton = clickedBtn; //[cite: 18]

            // 해당 카테고리에 존재하는 크기 버튼만 활성화
            UpdateSizeButtonAvailability(category);

            OpenBuildMenu(); //[cite: 18]
        }

        private void UpdateSizeButtonAvailability(BuildingType category)
        {
            if (BuildingManager.Instance == null) return;

            if (smallButton != null)
                smallButton.interactable = BuildingManager.Instance.GetBuildingsByCategoryAndSize(category, BuildingSizeType.Small).Count > 0;

            if (mediumButton != null)
                mediumButton.interactable = BuildingManager.Instance.GetBuildingsByCategoryAndSize(category, BuildingSizeType.Medium).Count > 0;

            if (largeButton != null)
                largeButton.interactable = BuildingManager.Instance.GetBuildingsByCategoryAndSize(category, BuildingSizeType.Large).Count > 0;
        }

        public void OpenBuildMenu()
        {
            _isBuildMenuOpen = true; //[cite: 18]
            _buildMenuSequence?.Kill(); //[cite: 18]
            _buildMenuSequence = DOTween.Sequence(); //[cite: 18]

            buildSubMenuPanel.SetActive(true); //[cite: 18]

            if (subMenuCanvasGroup != null) //[cite: 18]
            {
                subMenuCanvasGroup.alpha = 0f;
                _buildMenuSequence.Join(subMenuCanvasGroup.DOFade(1f, 0.2f));
            }

            for (int i = 0; i < _subButtonRects.Count; i++) //[cite: 18]
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
            CloseBuildingListPanel();

            if (!_isBuildMenuOpen) return; //[cite: 18]
            _isBuildMenuOpen = false; //[cite: 18]
            _lastClickedCategoryButton = null; //[cite: 18]

            _buildMenuSequence?.Kill(); //[cite: 18]
            _buildMenuSequence = DOTween.Sequence(); //[cite: 18]

            for (int i = 0; i < _subButtonRects.Count; i++) //[cite: 18]
            {
                RectTransform rt = _subButtonRects[i];
                _buildMenuSequence.Join(rt.DOScale(Vector3.zero, 0.15f).SetEase(Ease.InBack));
            }

            if (subMenuCanvasGroup != null) //[cite: 18]
                _buildMenuSequence.Join(subMenuCanvasGroup.DOFade(0f, 0.15f));

            _buildMenuSequence.OnComplete(() =>
            {
                buildSubMenuPanel.SetActive(false);
                for (int i = 0; i < _subButtonRects.Count; i++)
                    _subButtonRects[i].anchoredPosition = _subButtonOriginPositions[i];
            });
        }

        // ==========================================
        // 2단계: 크기 클릭 -> 필터링된 건물 슬롯 UI 생성
        // ==========================================
        private void OnSelectBuildingSize(BuildingSizeType sizeType)
        {
            if (BuildingManager.Instance == null) return;

            List<BuildingData> filteredBuildings = BuildingManager.Instance.GetBuildingsByCategoryAndSize(_currentSelectedCategory, sizeType);

            if (filteredBuildings.Count == 0)
            {
                Debug.LogWarning($"[BuildingUIManager] 조건에 맞는 건물 DB가 없습니다. ({_currentSelectedCategory} / {sizeType})");
                return;
            }

            ShowBuildingListPanel(filteredBuildings);
        }

        private void ShowBuildingListPanel(List<BuildingData> buildings)
        {
            if (buildingListPanel == null || buildingSlotContainer == null || buildingSlotPrefab == null) return;

            // 이전 슬롯 삭제
            foreach (Transform child in buildingSlotContainer)
            {
                Destroy(child.gameObject);
            }

            buildingListPanel.SetActive(true);

            // 작성하신 BuildingSlotUI.SetupSlot() 호출하여 슬롯 세팅
            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingSlotUI slot = Instantiate(buildingSlotPrefab, buildingSlotContainer);
                slot.SetupSlot(buildings[i]); //

                // DOTween 순차적 팝업 연출
                slot.transform.localScale = Vector3.zero;
                slot.transform.DOScale(Vector3.one, 0.3f)
                    .SetDelay(i * 0.05f)
                    .SetEase(Ease.OutBack);
            }
        }

        private void CloseBuildingListPanel()
        {
            if (buildingListPanel != null && buildingListPanel.activeSelf)
            {
                buildingListPanel.SetActive(false);
            }
        }

        // ==========================================
        // 기존 건물 클릭, 정보창, 철거 제어 유지
        // ==========================================
        public void OnBuildingClicked(BuildingInstance building)
        {
            _selectedBuilding = building; //[cite: 18]
            if (infoPanel != null) infoPanel.SetActive(false); //[cite: 18]

            if (actionPanel != null) //[cite: 18]
            {
                actionPanel.SetActive(true);
                actionPanel.transform.position = _mainCamera.WorldToScreenPoint(building.transform.position);
                actionPanel.transform.localScale = Vector3.zero;
                actionPanel.transform.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack);
            }
        }

        private void ShowInfoPanel()
        {
            if (_selectedBuilding == null || _selectedBuilding.Data == null) return; //[cite: 18]

            if (actionPanel != null) actionPanel.SetActive(false); //[cite: 18]
            if (infoPanel != null) infoPanel.SetActive(true); //[cite: 18]

            BuildingData data = _selectedBuilding.Data; //[cite: 18]

            if (txtBuildingName != null) txtBuildingName.text = data.buildingName; //[cite: 18]

            if (imgBuildingIcon != null) //[cite: 18]
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

            if (txtCategory != null) txtCategory.text = $"<b>분류:</b> {GetCategoryName(data.category)}"; //[cite: 18]
            if (txtPower != null) txtPower.text = $"<b>전력 소비:</b> {data.requiredPower} kW"; //[cite: 18]
            if (txtCost != null) txtCost.text = $"<b>유지비:</b> {data.maintenanceCost:N0}원/월\n<b>건설비:</b> {data.constructionCost:N0}원"; //[cite: 18]
            if (txtDescription != null) txtDescription.text = data.description; //[cite: 18]

            if (infoPanelCanvasGroup != null) //[cite: 18]
            {
                infoPanelCanvasGroup.alpha = 0f;
                infoPanelCanvasGroup.DOFade(1f, 0.25f);
            }

            if (infoPanel != null) //[cite: 18]
            {
                infoPanel.transform.localScale = Vector3.one * 0.8f;
                infoPanel.transform.DOScale(Vector3.one, 0.4f).SetEase(Ease.OutBack);
            }
        }

        private void DemolishSelectedBuilding()
        {
            if (_selectedBuilding != null) //[cite: 18]
            {
                Destroy(_selectedBuilding.gameObject);
                _selectedBuilding = null;
            }
            CloseAllUI(); //[cite: 18]
        }

        public void CloseAllUI()
        {
            if (actionPanel != null && actionPanel.activeSelf) //[cite: 18]
            {
                actionPanel.transform.DOKill();
                actionPanel.transform.DOScale(Vector3.zero, 0.2f).SetEase(Ease.InBack)
                    .OnComplete(() => actionPanel.SetActive(false));
            }

            if (infoPanel != null && infoPanel.activeSelf) //[cite: 18]
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

            CloseBuildMenu(); //[cite: 18]
        }

        private string GetCategoryName(BuildingType type)
        {
            return type switch //[cite: 18]
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
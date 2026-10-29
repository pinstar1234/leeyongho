using UnityEngine;
using UnityEngine.UI;
using TMPro;
using InfraTown.Building;
using DG.Tweening;
using System.Collections.Generic;

namespace InfraTown.UI
{
    public class BuildingUIManager : MonoBehaviour
    {
        public static BuildingUIManager Instance { get; private set; }

        [Header("Action UI (좌/우 버튼 패널)")]
        public GameObject actionPanel;
        public Button btnInfo;
        public Button btnDemolish;

        [Header("UI Panels")]
        [SerializeField] private GameObject buildSubMenuPanel;
        [SerializeField] private CanvasGroup subMenuCanvasGroup;

        [Header("Info UI (팝업 패널)")]
        public GameObject infoPanel;
        public CanvasGroup infoPanelCanvasGroup;

        // ★ 1. 기존 buildButton 대신 3개 시설 카테고리 버튼 선언
        [Header("Category Main Buttons (메인 시설 버튼들)")]
        [SerializeField] private Button btnFacility;   // 편의시설
        [SerializeField] private Button btnEssential;  // 필수시설
        [SerializeField] private Button btnNimbys;     // 혐오시설

        [Header("Sub Buttons (크기 선택 버튼들)")]
        [SerializeField] private Button smallButton;   // Small
        [SerializeField] private Button mediumButton;  // Medium
        [SerializeField] private Button largeButton;   // Large
        [SerializeField] private Button cancelButton;  // Cancel

        public TextMeshProUGUI txtBuildingName;
        public Image imgBuildingIcon;
        public TextMeshProUGUI txtCategory;
        public TextMeshProUGUI txtPower;
        public TextMeshProUGUI txtCost;
        public TextMeshProUGUI txtDescription;
        public Button btnCloseInfo;

        private BuildingInstance _selectedBuilding;
        private Camera _mainCamera;

        // 애니메이션 및 현재 선택 카테고리 상태 관리
        private BuildingType _currentSelectedCategory = BuildingType.Facility; // 현재 선택된 시설 종류
        private Button _lastClickedCategoryButton; // 마지막으로 누른 버튼 추적
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

            // CanvasGroup 세팅
            if (buildSubMenuPanel != null && subMenuCanvasGroup == null)
            {
                subMenuCanvasGroup = buildSubMenuPanel.GetComponent<CanvasGroup>();
                if (subMenuCanvasGroup == null)
                    subMenuCanvasGroup = buildSubMenuPanel.AddComponent<CanvasGroup>();
            }

            if (buildSubMenuPanel != null)
                buildSubMenuPanel.SetActive(false);

            // ★ 2. 메인 카테고리 버튼 3개 각각에 클릭 이벤트 연결
            if (btnFacility != null)
                btnFacility.onClick.AddListener(() => OnClickCategoryMainButton(btnFacility, BuildingType.Facility));

            if (btnEssential != null)
                btnEssential.onClick.AddListener(() => OnClickCategoryMainButton(btnEssential, BuildingType.Essential));

            if (btnNimbys != null)
                btnNimbys.onClick.AddListener(() => OnClickCategoryMainButton(btnNimbys, BuildingType.Nimbys));

            // 서브 버튼 이벤트 연결
            if (cancelButton != null)
                cancelButton.onClick.AddListener(CloseBuildMenu);

            if (smallButton != null)
                smallButton.onClick.AddListener(() => OnSelectBuildingSize("Small"));

            if (mediumButton != null)
                mediumButton.onClick.AddListener(() => OnSelectBuildingSize("Medium"));

            if (largeButton != null)
                largeButton.onClick.AddListener(() => OnSelectBuildingSize("Large"));
        }

        private void Start()
        {
            btnInfo.onClick.AddListener(ShowInfoPanel);
            btnDemolish.onClick.AddListener(DemolishSelectedBuilding);
            btnCloseInfo.onClick.AddListener(CloseAllUI);
            CloseAllUI();
        }

        // ==========================================
        // ★ 3. 카테고리 버튼 클릭 통합 제어 로직
        // ==========================================
        private void OnClickCategoryMainButton(Button clickedBtn, BuildingType category)
        {
            // 클릭된 버튼 쫀득한 Punch 효과
            clickedBtn.transform.DOKill();
            clickedBtn.transform.localScale = Vector3.one;
            clickedBtn.transform.DOPunchScale(Vector3.one * 0.2f, 0.25f, 10, 1f);

            // 이미 열려 있는 상태에서 같은 버튼을 다시 누르면 메뉴 닫기 (토글)
            if (_isBuildMenuOpen && _lastClickedCategoryButton == clickedBtn)
            {
                CloseBuildMenu();
                _lastClickedCategoryButton = null;
                return;
            }

            // 현재 카테고리 갱신
            _currentSelectedCategory = category;
            _lastClickedCategoryButton = clickedBtn;
            Debug.Log($"선택된 카테고리: {GetCategoryName(category)}");

            // 서브메뉴 열기
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

            // 4개 서브 버튼이 0.05초 간격으로 순차 팝업
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
        // ★ 4. 크기 선택 시 카테고리와 크기 정보 조합
        // ==========================================
        private void OnSelectBuildingSize(string size)
        {
            Debug.Log($"건설 요청 -> 카테고리: {_currentSelectedCategory} | 크기: {size}");
            CloseBuildMenu();

            // 추후 연결 예시:
            // BuildingManager.Instance.StartPlacingBuilding(_currentSelectedCategory, size);
        }

        // ==========================================
        // 기존 건물 클릭, 정보창, 철거 로직 유지
        // ==========================================
        public void OnBuildingClicked(BuildingInstance building)
        {
            _selectedBuilding = building;
            infoPanel.SetActive(false);

            actionPanel.SetActive(true);
            actionPanel.transform.position = _mainCamera.WorldToScreenPoint(building.transform.position);

            actionPanel.transform.localScale = Vector3.zero;
            actionPanel.transform.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack);
        }

        private void ShowInfoPanel()
        {
            if (_selectedBuilding == null) return;

            actionPanel.SetActive(false);
            infoPanel.SetActive(true);

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

            if (infoPanelCanvasGroup != null)
            {
                infoPanelCanvasGroup.alpha = 0f;
                infoPanelCanvasGroup.DOFade(1f, 0.25f);
            }

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
            if (actionPanel.activeSelf)
            {
                actionPanel.transform.DOKill();
                actionPanel.transform.DOScale(Vector3.zero, 0.2f).SetEase(Ease.InBack)
                    .OnComplete(() => actionPanel.SetActive(false));
            }

            if (infoPanel.activeSelf)
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
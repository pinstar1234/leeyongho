using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Grid;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.Population;
using InfraTown.UI;

namespace InfraTown.Building
{
#if UNITY_EDITOR
    internal sealed class BuildingCatalogSceneProcessor : UnityEditor.Build.IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(UnityEngine.SceneManagement.Scene scene, UnityEditor.Build.Reporting.BuildReport report)
        {
            if (report == null) return;

            foreach (var root in scene.GetRootGameObjects())
            foreach (var manager in root.GetComponentsInChildren<BuildingManager>(true))
                manager.IncludeProjectBuildingData();
        }
    }
#endif

    public class BuildingManager : MonoBehaviour
    {
        public static BuildingManager Instance { get; private set; }

        [Header("건물 DB (ScriptableObjects)")]
        [SerializeField] private List<BuildingData> buildingDatabase;
        [SerializeField] private bool discoverBuildingData;

        [Header("프리뷰 마테리얼 (반투명)")]
        [SerializeField] private Material validPreviewMaterial;   // 건설 가능 (초록 반투명)
        [SerializeField] private Material invalidPreviewMaterial; // 건설 불가능 (빨강 반투명)

        [Header("하이라이트 컨트롤러 참조")]
        [SerializeField] private GridHighlightController highlightController;

        [Tooltip("켜면 한 번 설치한 뒤 건설 모드가 끝난다 (Jinyu12)")]
        [SerializeField] private bool singlePlacement;

        [Header("드래그 건설 (사용자 기획 2026-11-25)")]
        [Tooltip("누른 채 끌어서 여러 개를 한 번에 짓는다. 도로·전신주는 가로 또는 세로 한 줄(더 많이 움직인 방향), 나머지는 끌어서 만든 사각형 안을 건물 크기 간격으로 채운다. 끄면 누르는 순간 한 개만 설치(예전 방식)")]
        [SerializeField] private bool dragPlacement = true;

        [Tooltip("드래그 한 번에 지을 수 있는 최대 개수 (임시값 — 넘는 칸은 미리보기·설치에서 빠짐)")]
        [SerializeField] private int maxDragBuildings = 100;

        [Header("도로 접근 (사용자 기획 2026-11-24)")]
        [Tooltip("건물은 한 면 이상(상하좌우, 대각선 제외) 도로에 맞닿아야 설치할 수 있다. 도로 자신은 어디든, 전신주는 도로 위에도 설치 가능. 끄면 예전처럼 어디든 (테스트·연출용)")]
        [SerializeField] private bool requireRoadAccess = true;

        [Header("건설 회전 (Q: +90°, E: -90°)")]
        [SerializeField] private KeyCode rotateKeyPositive = KeyCode.Q;
        [SerializeField] private KeyCode rotateKeyNegative = KeyCode.E;
        [SerializeField] private float rotationStep = 90f;

        /// <summary>건설 모드에서 플레이어가 돌린 Y축 회전 (0/90/180/270). 같은 건물을 연속 설치하는 동안 유지되고, 다른 건물을 고르면 0으로 돌아간다</summary>
        public float CurrentRotationY { get; private set; }

        /// <summary>건설 모드 중인지</summary>
        public bool IsBuildMode => _selectedBuildingData != null;

        /// <summary>건설 모드에서 고른 건물 (없으면 null)</summary>
        public BuildingData SelectedBuildingData => _selectedBuildingData;

        /// <summary>미리보기 회전이 바뀔 때 (새 Y 회전값)</summary>
        public event Action<float> OnPreviewRotationChanged;

        /// <summary>건물이 설치됐을 때 (연출·효과용)</summary>
        public event Action<BuildingInstance> OnBuildingPlaced;

        /// <summary>건물을 골라 배치 중인지 (미리보기가 마우스를 따라다니는 건설 모드)</summary>
        public bool IsPlacingBuilding => _selectedBuildingData != null;

        /// <summary>
        /// 건설 모드가 켜지거나 꺼질 때 (true = 배치 시작, false = 취소·종료). 건물을 바꿔 고를 때는 다시 알리지 않는다.
        /// 건설 독 UI(BuildDockUI) 등이 구독
        /// </summary>
        public event Action<bool> OnPlacementModeChanged;

        private bool _notifiedPlacing;

        /// <summary>배치 상태가 마지막으로 알린 값과 달라졌을 때만 OnPlacementModeChanged 발생</summary>
        private void NotifyPlacementMode()
        {
            if (_notifiedPlacing == IsPlacingBuilding) return;
            _notifiedPlacing = IsPlacingBuilding;
            OnPlacementModeChanged?.Invoke(_notifiedPlacing);
        }

        private BuildingData _selectedBuildingData;
        private BuildingData _lastSelectedBuildingData;
        private GameObject _previewInstance;
        private MeshRenderer[] _previewRenderers;

        // 60x40 타일별 점유중인 건물 인스턴스 저장 배열
        private BuildingInstance[,] _occupiedGrid = new BuildingInstance[60, 40];

        // 도로 위에 세운 전신주 (도로와 같은 칸을 쓰므로 따로 보관, 사용자 기획 2026-11-24). 도로가 철거돼도 전신주는 그 자리에 남는다
        private BuildingInstance[,] _roadPoleGrid = new BuildingInstance[60, 40];

        /// <summary>도로 접근 조건을 쓰는지 (끄면 어디든 설치 — 테스트·연출용)</summary>
        public bool RequireRoadAccess { get => requireRoadAccess; set => requireRoadAccess = value; }

        /// <summary>마지막으로 설치하지 못한 이유 (설치 성공하면 null) — UI 안내 문구용</summary>
        public string LastPlacementError { get; private set; }

        /// <summary>설치를 시도했지만 못 했을 때 (건물, 칸, 이유) — 토스트 등 안내용. 드래그 건설은 못 지은 칸마다 한 번씩</summary>
        public event Action<BuildingData, Vector2Int, string> OnPlacementRejected;

        /// <summary>돈이 모자라 못 지을 때의 이유 문구</summary>
        public const string MoneyShortPlacementError = "건설 자금이 부족합니다.";

        // ─── 드래그 건설 상태 (UI·연출용) ───

        /// <summary>드래그 건설을 쓰는지 (끄면 누르는 순간 한 개만 설치)</summary>
        public bool DragPlacementEnabled { get => dragPlacement; set => dragPlacement = value; }

        /// <summary>드래그 한 번에 지을 수 있는 최대 개수</summary>
        public int MaxDragBuildings { get => maxDragBuildings; set => maxDragBuildings = Mathf.Max(1, value); }

        /// <summary>지금 끌고 있는지 (건설 모드에서 누른 채)</summary>
        public bool IsDragPlacing { get; private set; }

        /// <summary>지금 드래그로 지으려는 칸들 (설치 순서대로, 각 건물의 원점 칸)</summary>
        public IReadOnlyList<Vector2Int> DragCells => _dragCells;

        /// <summary>그중 지을 수 있는 칸 수 (돈은 순서대로 쓴다고 보고 계산)</summary>
        public int DragValidCount { get; private set; }

        /// <summary>지을 수 있는 칸을 모두 지으면 드는 건설비</summary>
        public int DragTotalCost { get; private set; }

        /// <summary>드래그 미리보기 칸이 바뀜 (칸 목록) — 개수·비용 표시 등. 드래그가 끝나거나 취소되면 빈 목록</summary>
        public event Action<IReadOnlyList<Vector2Int>> OnDragCellsChanged;

        /// <summary>드래그 건설이 끝남 (지은 수, 못 지은 수)</summary>
        public event Action<int, int> OnAreaPlaced;

        private readonly List<Vector2Int> _dragCells = new List<Vector2Int>();
        private readonly List<bool> _dragValid = new List<bool>();
        private readonly List<GameObject> _dragPreviews = new List<GameObject>();
        private Vector2Int _dragStart;

        private void Awake()
        {
            if (Instance != null && Instance != this) { InfraTown.Core.SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;

#if UNITY_EDITOR
            IncludeProjectBuildingData();
#endif
            if (highlightController == null)
            {
                highlightController = GetComponent<GridHighlightController>();
            }
        }

#if UNITY_EDITOR
        public void IncludeProjectBuildingData()
        {
            if (!discoverBuildingData) return;

            buildingDatabase ??= new List<BuildingData>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var data in buildingDatabase)
                if (data != null) ids.Add(data.buildingID);

            var guids = UnityEditor.AssetDatabase.FindAssets("t:BuildingData", new[] { "Assets/Data/Build/Spec" });
            Array.Sort(guids, StringComparer.Ordinal);
            foreach (var guid in guids)
            {
                var data = UnityEditor.AssetDatabase.LoadAssetAtPath<BuildingData>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data.buildingPrefab == null || string.IsNullOrWhiteSpace(data.buildingID) ||
                    !ids.Add(data.buildingID)) continue;
                buildingDatabase.Add(data);
            }
        }
#endif

        private void Start()
        {
            if (LightweightGridManager.Instance != null)
            {
                var grid = LightweightGridManager.Instance;
                grid.OnGridHoverChanged += UpdateBuildingPreview;
                // 누르면 드래그 시작, 떼면 그 범위에 건설 (제자리 클릭은 한 칸) — 사용자 기획 2026-11-25
                grid.OnGridDragStarted += HandleGridDragStarted;
                grid.OnGridDragUpdated += HandleGridDragUpdated;
                grid.OnGridDragEnded += HandleGridDragEnded;
                grid.OnGridDragCanceled += CancelDragPlacement;
            }

            BuildingInstance.OnBuildingDestroyed += HandleBuildingDestroyed;

            if (highlightController != null)
            {
                highlightController.HideHighlight();
            }
        }

        private void Update()
        {
            if (_selectedBuildingData == null) return;

            if (Input.GetKeyDown(rotateKeyPositive)) RotatePreview(rotationStep);
            else if (Input.GetKeyDown(rotateKeyNegative)) RotatePreview(-rotationStep);
        }

        /// <summary>
        /// 설치할 건물의 Y축 회전을 바꾼다 (Q/E 키, UI 회전 버튼에서도 호출 가능). 미리보기에 바로 반영된다
        /// </summary>
        public void RotatePreview(float deltaDegrees)
        {
            CurrentRotationY = Mathf.Repeat(Mathf.Round(CurrentRotationY + deltaDegrees), 360f);
            ApplyPreviewRotation();
            OnPreviewRotationChanged?.Invoke(CurrentRotationY);
        }

        /// <summary>
        /// 설치 시 실제로 쓰일 회전 = 플레이어 Y 회전 × 프리팹에 설정된 원래 회전.
        /// 미리보기와 설치 모두 이 값을 쓰므로 보이는 모습 그대로 설치된다
        /// </summary>
        public Quaternion GetPlacementRotation(BuildingData data) => GetPlacementRotation(data, CurrentRotationY);

        /// <summary>지정한 Y 회전으로 설치할 때의 회전 (세이브 복원 등)</summary>
        public Quaternion GetPlacementRotation(BuildingData data, float rotationY)
        {
            Quaternion prefabRotation = data != null && data.buildingPrefab != null
                ? data.buildingPrefab.transform.rotation
                : Quaternion.identity;
            return Quaternion.Euler(0f, rotationY, 0f) * prefabRotation;
        }

        private void ApplyPreviewRotation()
        {
            if (_previewInstance != null && _selectedBuildingData != null)
            {
                _previewInstance.transform.rotation = GetPlacementRotation(_selectedBuildingData);
            }
            if (_selectedBuildingData != null)
            {
                foreach (var p in _dragPreviews) if (p != null) p.transform.rotation = GetPlacementRotation(_selectedBuildingData);
            }
        }

        private void OnDestroy()
        {
            if (LightweightGridManager.Instance != null)
            {
                var grid = LightweightGridManager.Instance;
                grid.OnGridHoverChanged -= UpdateBuildingPreview;
                grid.OnGridDragStarted -= HandleGridDragStarted;
                grid.OnGridDragUpdated -= HandleGridDragUpdated;
                grid.OnGridDragEnded -= HandleGridDragEnded;
                grid.OnGridDragCanceled -= CancelDragPlacement;
            }

            BuildingInstance.OnBuildingDestroyed -= HandleBuildingDestroyed;
        }

        /// <summary>
        /// 지정된 카테고리(필수/편의/불쾌/주택) 및 규격(소형/중형/대형)에 부합하는 건물 DB 데이터를 반환
        /// </summary>
        public BuildingData GetBuildingDataByCategoryAndSize(BuildingType category, BuildingSizeType sizeType)
        {
            if (buildingDatabase == null) return null;

            foreach (var data in buildingDatabase)
            {
                if (data != null && data.category == category && data.sizeType == sizeType)
                {
                    return data;
                }
            }

            return null;
        }

        /// <summary>
        /// 등록된 전체 건물 데이터 (읽기 전용)
        /// </summary>
        public IReadOnlyList<BuildingData> AllBuildingData => buildingDatabase ?? (IReadOnlyList<BuildingData>)new List<BuildingData>();

        /// <summary>
        /// 카테고리에 속한 건물 데이터 전체 (같은 분류·크기 건물이 여러 개일 때 UI 목록용)
        /// </summary>
        public List<BuildingData> GetBuildingsByCategory(BuildingType category)
        {
            var result = new List<BuildingData>();
            if (buildingDatabase == null) return result;

            foreach (var data in buildingDatabase)
            {
                if (data != null && data.category == category) result.Add(data);
            }
            return result;
        }
        public List<BuildingData> GetBuildingsByCategoryAndSize(BuildingType category, BuildingSizeType sizeType)
        {
            var result = new List<BuildingData>();
            if (buildingDatabase == null) return result; //[cite: 20]

            foreach (var data in buildingDatabase) //[cite: 20]
            {
                if (data != null && data.category == category && data.sizeType == sizeType) //[cite: 20]
                {
                    result.Add(data);
                }
            }
            return result;
        }

        /// <summary>
        /// 지금 인구로 해금된 건물 데이터 전체
        /// </summary>
        public List<BuildingData> GetUnlockedBuildings()
        {
            var result = new List<BuildingData>();
            if (buildingDatabase == null) return result;

            foreach (var data in buildingDatabase)
            {
                if (data != null && IsBuildingUnlocked(data)) result.Add(data);
            }
            return result;
        }

        /// <summary>
        /// 특정 건물이 현재 인구수 기준으로 해금되었는지 여부 확인
        /// </summary>
        public bool IsBuildingUnlocked(BuildingData data)
        {
            if (data == null) return false;

            int currentPop = PopulationManager.Instance != null
                ? PopulationManager.Instance.CurrentPopulation
                : 0;

            return currentPop >= data.requiredPopulation;
        }

        /// <summary>
        /// UI 버튼으로 건설할 건물을 선택 (해금 여부 검증)
        /// </summary>
        public void SelectBuildingToBuild(BuildingData data)
        {
            if (data == null) return;

            // 0. 배드엔딩(게임 오버) 뒤에는 건설 불가 (사용자 기획 2026-11-24)
            if (!GameOutcomeManager.CanModifyTownNow)
            {
                RejectPlacement(data, new Vector2Int(-1, -1), GameOverPlacementError);
                return;
            }

            // 1. 현재 인구수 기반 해금 검증
            if (!IsBuildingUnlocked(data))
            {
                Debug.LogWarning($"[BuildingManager] '{data.buildingName}'은(는) 해금되지 않았습니다. (필요 인구수: {data.requiredPopulation}명)");
                ToastUIManager.Instance?.ShowToast($"🔒 '{data.buildingName}' 해금에는 인구 {data.requiredPopulation}명이 필요합니다!");
                return;
            }

            ClearBuildingMode(); // 이전 미리보기 정리 (건물을 바꿔 고를 때 꺼짐→켜짐 알림이 나가지 않도록 알림 없이)

            // 다른 건물을 고르면 회전 초기화, 같은 건물을 다시 고르면 직전 회전 유지
            if (data != _lastSelectedBuildingData) CurrentRotationY = 0f;
            _lastSelectedBuildingData = data;
            _selectedBuildingData = data;

            // 미리보기도 설치와 같은 회전(플레이어 Y 회전 × 설치 프리팹의 원래 회전)으로 생성
            GameObject prefabToInstantiate = data.previewPrefab != null ? data.previewPrefab : data.buildingPrefab;
            _previewInstance = Instantiate(prefabToInstantiate, prefabToInstantiate.transform.position, GetPlacementRotation(data));

            foreach (var col in _previewInstance.GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }

            _previewRenderers = _previewInstance.GetComponentsInChildren<MeshRenderer>();
            SetPreviewMaterial(validPreviewMaterial);
            // 고르자마자 지금 마우스 칸에 미리보기 표시 (Jinyu12)
            var grid = LightweightGridManager.Instance;
            UpdateBuildingPreview(grid != null ? grid.HoveredGridPos : new Vector2Int(-1, -1));
            NotifyPlacementMode();
        }

        /// <summary>
        /// 마우스 위치에 따른 건물 프리뷰 모델 및 점유 타일 전체 하이라이트 갱신
        /// </summary>
        private void UpdateBuildingPreview(Vector2Int gridPos)
        {
            if (_selectedBuildingData == null)
            {
                if (highlightController != null) highlightController.HideHighlight();
                return;
            }

            if (IsDragPlacing) return; // 드래그 중에는 드래그 미리보기가 대신 보임

            if (_previewInstance == null) return;

            if (gridPos.x == -1 || gridPos.y == -1)
            {
                _previewInstance.SetActive(false);
                if (highlightController != null) highlightController.HideHighlight();
                return;
            }

            _previewInstance.SetActive(true);

            Vector2Int size = _selectedBuildingData.GridSize;

            Vector3 worldPos = GetBuildingWorldPosition(gridPos.x, gridPos.y, size.x, size.y);
            _previewInstance.transform.position = worldPos;

            bool canBuild = CanPlaceAt(_selectedBuildingData, gridPos, out _);
            SetPreviewMaterial(canBuild ? validPreviewMaterial : invalidPreviewMaterial);

            if (highlightController != null)
            {
                highlightController.ShowHighlightArea(gridPos.x, gridPos.y, size.x, size.y);
                highlightController.SetColor(canBuild ? Color.green : Color.red);
            }
        }

        /// <summary>
        /// 건물 실제 건설 처리 및 타일 점유 배열 전체 등록
        /// </summary>
        private void TryPlaceBuilding(Vector2Int gridPos)
        {
            if (_selectedBuildingData == null) return;

            if (!IsBuildingUnlocked(_selectedBuildingData))
            {
                Debug.LogWarning("[BuildingManager] 해금 조건 미달로 건설할 수 없습니다.");
                ToastUIManager.Instance?.ShowToast($"🔒 해금 조건(인구 {_selectedBuildingData.requiredPopulation}명)이 부족합니다!");
                CancelBuildingMode();
                return;
            }

            if (!CanPlaceAt(_selectedBuildingData, gridPos, out string reason))
            {
                RejectPlacement(_selectedBuildingData, gridPos, reason);
                return;
            }

            int cost = _selectedBuildingData.constructionCost;
            if (EconomyManager.Instance != null && !EconomyManager.Instance.TrySpendMoney(cost))
            {
                RejectPlacement(_selectedBuildingData, gridPos, MoneyShortPlacementError);
                return;
            }

            LastPlacementError = null;
            var placedData = _selectedBuildingData;
            BuildingInstance instance = SpawnBuilding(placedData, gridPos, CurrentRotationY);
            OnBuildingPlaced?.Invoke(instance);

            ToastUIManager.Instance?.ShowToast($"✅ '{placedData.buildingName}' 건설 완료!", false);
            if (singlePlacement) CancelBuildingMode();
        }

        private void RejectPlacement(BuildingData data, Vector2Int gridPos, string reason)
        {
            LastPlacementError = reason;
            Debug.LogWarning($"[BuildingManager] '{data.buildingName}' ({gridPos.x},{gridPos.y}) 설치 불가: {reason}");
            OnPlacementRejected?.Invoke(data, gridPos, reason);

            // 화면 안내 (Jinyu12의 토스트 문구, 자금 부족은 필요 금액까지)
            string toast = reason == MoneyShortPlacementError
                ? $"💰 건설 자금이 부족합니다! (필요 자금: {data.constructionCost:N0}원)"
                : $"⚠️ {reason}";
            ToastUIManager.Instance?.ShowToast(toast);
        }

        // ─── 드래그 건설 (사용자 기획 2026-11-25) ───
        // 누른 칸에서 시작해 뗀 칸까지: 도로·전신주는 가로/세로 한 줄, 나머지는 사각형을 건물 크기 간격으로 채움.
        // 지을 수 없는 칸은 건너뛰고 되는 칸만 짓는다. 돈이 떨어지면 그 뒤 칸은 짓지 않는다

        /// <summary>도로·전신주처럼 한 줄로 끌어서 짓는 건물인지 (나머지는 사각형 채우기)</summary>
        public static bool UsesLineDrag(BuildingData data) =>
            data != null && (data.IsRoad || data.isPowerPole || data.category == BuildingType.Infrastructure);

        /// <summary>
        /// 시작 칸 → 끝 칸으로 끌었을 때 지을 건물들의 원점 칸 (설치 순서 = 시작 칸에서 가까운 순).
        /// 한 줄: 더 많이 움직인 방향(같으면 가로)으로 시작 칸의 줄을 따라감. 사각형: 시작 칸 기준으로 끈 방향을 건물 크기 간격으로 채움.
        /// 맵 밖으로 나가는 건물은 빼고, maxDragBuildings개까지만
        /// </summary>
        public List<Vector2Int> GetDragCells(BuildingData data, Vector2Int start, Vector2Int end)
        {
            var cells = new List<Vector2Int>();
            if (data == null) return cells;
            Vector2Int size = data.GridSize;
            int sx = Mathf.Max(1, size.x), sy = Mathf.Max(1, size.y);
            int dx = end.x - start.x, dy = end.y - start.y;
            int dirX = dx < 0 ? -1 : 1, dirY = dy < 0 ? -1 : 1;
            int countX = Mathf.Abs(dx) / sx + 1, countY = Mathf.Abs(dy) / sy + 1;

            if (UsesLineDrag(data))
            {
                if (Mathf.Abs(dx) >= Mathf.Abs(dy)) countY = 1;
                else countX = 1;
            }

            int max = Mathf.Max(1, maxDragBuildings);
            for (int j = 0; j < countY && cells.Count < max; j++)
            {
                for (int i = 0; i < countX && cells.Count < max; i++)
                {
                    var cell = new Vector2Int(start.x + dirX * i * sx, start.y + dirY * j * sy);
                    if (cell.x < 0 || cell.y < 0 || cell.x + sx > 60 || cell.y + sy > 40) continue;
                    cells.Add(cell);
                }
            }
            return cells;
        }

        /// <summary>
        /// 고른 건물을 시작 칸 → 끝 칸 범위에 한 번에 설치 (마우스 드래그를 떼면 호출, UI·테스트가 직접 불러도 됨).
        /// 되는 칸만 짓고 못 지은 칸은 OnPlacementRejected로 하나씩 알린 뒤 토스트는 한 번만. 지은 개수를 돌려준다
        /// </summary>
        public int TryPlaceArea(Vector2Int start, Vector2Int end)
        {
            var data = _selectedBuildingData;
            if (data == null) return 0;

            if (!GameOutcomeManager.CanModifyTownNow)
            {
                RejectPlacement(data, start, GameOverPlacementError);
                return 0;
            }
            if (!IsBuildingUnlocked(data))
            {
                TryPlaceBuilding(start); // 해금 안내 + 건설 모드 종료 (한 칸 설치와 같은 처리)
                return 0;
            }

            var cells = GetDragCells(data, start, end);
            if (cells.Count == 0)
            {
                RejectPlacement(data, start, "맵 밖입니다.");
                return 0;
            }
            if (cells.Count == 1)
            {
                // 한 칸이면 예전 클릭 설치와 똑같이 (안내 문구 포함)
                TryPlaceBuilding(cells[0]);
                int ok = LastPlacementError == null ? 1 : 0;
                OnAreaPlaced?.Invoke(ok, 1 - ok);
                return ok;
            }

            int placed = 0, failed = 0;
            string firstReason = null;
            bool outOfMoney = false;
            var economy = EconomyManager.Instance;

            foreach (var cell in cells)
            {
                string reason;
                if (outOfMoney) reason = MoneyShortPlacementError;
                else if (!CanPlaceAt(data, cell, out reason)) { }
                else if (economy != null && !economy.TrySpendMoney(data.constructionCost)) reason = MoneyShortPlacementError;
                else
                {
                    BuildingInstance instance = SpawnBuilding(data, cell, CurrentRotationY);
                    OnBuildingPlaced?.Invoke(instance);
                    placed++;
                    continue;
                }

                if (reason == MoneyShortPlacementError) outOfMoney = true;
                failed++;
                if (firstReason == null) firstReason = reason;
                OnPlacementRejected?.Invoke(data, cell, reason);
            }

            LastPlacementError = failed == 0 ? null : firstReason;
            Debug.Log($"[BuildingManager] 드래그 건설 '{data.buildingName}': {placed}개 지음, {failed}개 못 지음{(failed > 0 ? $" ({firstReason})" : "")}");

            // 화면 안내는 한 번만
            string toast = failed == 0 ? $"'{data.buildingName}' {placed}개 건설 완료!"
                : placed == 0 ? (firstReason == MoneyShortPlacementError ? $"건설 자금이 부족합니다! (1개당 {data.constructionCost:N0}원)" : firstReason)
                : $"'{data.buildingName}' {placed}개 건설, {failed}개는 지을 수 없습니다. ({firstReason})";
            ToastUIManager.Instance?.ShowToast(toast, failed > 0);

            OnAreaPlaced?.Invoke(placed, failed);
            if (singlePlacement && placed > 0) CancelBuildingMode();
            return placed;
        }

        private void HandleGridDragStarted(Vector2Int cell)
        {
            if (_selectedBuildingData == null) return;
            if (!dragPlacement)
            {
                TryPlaceBuilding(cell); // 예전 방식: 누르는 순간 한 개
                return;
            }
            IsDragPlacing = true;
            _dragStart = cell;
            if (_previewInstance != null) _previewInstance.SetActive(false);
            RefreshDragPreview(cell);
        }

        private void HandleGridDragUpdated(Vector2Int start, Vector2Int current)
        {
            if (IsDragPlacing) RefreshDragPreview(current);
        }

        private void HandleGridDragEnded(Vector2Int start, Vector2Int end)
        {
            if (!IsDragPlacing) return;
            var from = _dragStart;
            ClearDragState();
            TryPlaceArea(from, end);

            // 건설 모드가 이어지면 지금 칸에 다시 한 개짜리 미리보기
            var grid = LightweightGridManager.Instance;
            if (_selectedBuildingData != null) UpdateBuildingPreview(grid != null ? grid.HoveredGridPos : end);
        }

        /// <summary>드래그 취소 (오른쪽 버튼) — 아무것도 짓지 않고 건설 모드는 그대로</summary>
        public void CancelDragPlacement()
        {
            if (!IsDragPlacing) return;
            ClearDragState();
            var grid = LightweightGridManager.Instance;
            if (_selectedBuildingData != null && grid != null) UpdateBuildingPreview(grid.HoveredGridPos);
        }

        /// <summary>드래그 범위 미리보기: 칸마다 반투명 건물(되는 칸 초록 / 못 짓는 칸 빨강) + 전체 범위 하이라이트</summary>
        private void RefreshDragPreview(Vector2Int current)
        {
            var data = _selectedBuildingData;
            if (data == null) return;

            _dragCells.Clear();
            _dragCells.AddRange(GetDragCells(data, _dragStart, current));
            EvaluateDragCells(data);

            GameObject prefab = data.previewPrefab != null ? data.previewPrefab : data.buildingPrefab;
            Vector2Int size = data.GridSize;
            for (int i = 0; i < _dragCells.Count; i++)
            {
                if (i >= _dragPreviews.Count)
                {
                    if (prefab == null) break;
                    var ghost = Instantiate(prefab, prefab.transform.position, GetPlacementRotation(data));
                    ghost.name = $"DragPreview_{i}";
                    foreach (var col in ghost.GetComponentsInChildren<Collider>()) col.enabled = false;
                    _dragPreviews.Add(ghost);
                }
                var preview = _dragPreviews[i];
                preview.SetActive(true);
                preview.transform.SetPositionAndRotation(GetBuildingWorldPosition(_dragCells[i].x, _dragCells[i].y, size.x, size.y), GetPlacementRotation(data));
                var mat = _dragValid[i] ? validPreviewMaterial : invalidPreviewMaterial;
                if (mat != null) foreach (var rend in preview.GetComponentsInChildren<MeshRenderer>()) rend.material = mat;
            }
            for (int i = _dragCells.Count; i < _dragPreviews.Count; i++) if (_dragPreviews[i] != null) _dragPreviews[i].SetActive(false);

            if (highlightController != null && _dragCells.Count > 0)
            {
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (var c in _dragCells)
                {
                    minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y);
                    maxX = Mathf.Max(maxX, c.x + size.x); maxY = Mathf.Max(maxY, c.y + size.y);
                }
                highlightController.ShowHighlightArea(minX, minY, maxX - minX, maxY - minY);
                highlightController.SetColor(DragValidCount == _dragCells.Count ? Color.green : DragValidCount > 0 ? Color.yellow : Color.red);
            }
            OnDragCellsChanged?.Invoke(_dragCells);
        }

        /// <summary>드래그 칸마다 지을 수 있는지 (돈은 앞 칸부터 순서대로 쓴다고 보고, 한 번 모자라면 그 뒤는 모두 못 지음)</summary>
        private void EvaluateDragCells(BuildingData data)
        {
            _dragValid.Clear();
            int money = EconomyManager.Instance != null ? EconomyManager.Instance.CurrentMoney : int.MaxValue;
            bool outOfMoney = false;
            int valid = 0, cost = 0;
            foreach (var cell in _dragCells)
            {
                bool ok = !outOfMoney && CanPlaceWithMoney(data, cell, money, out _);
                if (!ok && !outOfMoney && CanPlaceWithMoney(data, cell, int.MaxValue, out _)) outOfMoney = true; // 돈 때문에만 못 지음
                if (ok)
                {
                    valid++;
                    cost += data.constructionCost;
                    if (money != int.MaxValue) money -= data.constructionCost;
                }
                _dragValid.Add(ok);
            }
            DragValidCount = valid;
            DragTotalCost = cost;
        }

        /// <summary>그 드래그 칸을 지을 수 있는지 (미리보기 색과 같은 판정, 테스트·UI용)</summary>
        public bool IsDragCellValid(int index) => index >= 0 && index < _dragValid.Count && _dragValid[index];

        private void ClearDragState()
        {
            bool had = IsDragPlacing || _dragCells.Count > 0;
            IsDragPlacing = false;
            _dragCells.Clear();
            _dragValid.Clear();
            DragValidCount = 0;
            DragTotalCost = 0;
            foreach (var p in _dragPreviews)
            {
                if (p == null) continue;
                p.SetActive(false);
                if (Application.isPlaying) Destroy(p);
                else DestroyImmediate(p);
            }
            _dragPreviews.Clear();
            if (had)
            {
                if (highlightController != null) highlightController.HideHighlight();
                OnDragCellsChanged?.Invoke(_dragCells);
            }
        }

        // ─── 설치 조건 (사용자 기획 2026-11-24: 도로 접근, 도로 위 전신주) ───

        /// <summary>배드엔딩(게임 오버) 뒤 설치를 시도했을 때의 안내 문구</summary>
        public const string GameOverPlacementError = "게임이 끝나 더 이상 건설할 수 없습니다.";

        /// <summary>
        /// 그 칸에 이 건물을 지금 지을 수 있는지: 맵 안 + 겹치지 않음(전신주는 도로 위 허용) + 도로 접근 + 자금.
        /// 해금 여부는 따로(IsBuildingUnlocked). 못 지으면 reason에 이유
        /// </summary>
        public bool CanPlaceAt(BuildingData data, Vector2Int gridPos, out string reason) =>
            CanPlaceWithMoney(data, gridPos, EconomyManager.Instance != null ? EconomyManager.Instance.CurrentMoney : int.MaxValue, out reason);

        /// <summary>CanPlaceAt과 같고, 자금만 지금 잔액 대신 availableMoney로 본다 (드래그 미리보기에서 앞 칸이 쓴 돈을 뺀 값)</summary>
        private bool CanPlaceWithMoney(BuildingData data, Vector2Int gridPos, int availableMoney, out string reason)
        {
            if (!GameOutcomeManager.CanModifyTownNow)
            {
                reason = GameOverPlacementError;
                return false;
            }
            if (!IsAreaFree(data, gridPos, out reason)) return false;
            if (!HasRoadAccess(data, gridPos))
            {
                reason = data.isPowerPole ? "전신주는 도로 위나 도로에 맞닿은 곳에만 세울 수 있습니다." : "건물은 한 면 이상 도로에 맞닿아야 지을 수 있습니다.";
                return false;
            }
            if (availableMoney < data.constructionCost)
            {
                reason = MoneyShortPlacementError;
                return false;
            }
            reason = null;
            return true;
        }

        /// <summary>
        /// 자리만 검사 (맵 안 + 겹침). 전신주는 도로 칸 위에 세울 수 있고(도로 위 전신주가 이미 있으면 안 됨),
        /// 도로는 도로가 철거되고 남은 전신주 아래에 다시 깔 수 있다. 세이브 복원도 이 검사만 쓴다
        /// </summary>
        public bool IsAreaFree(BuildingData data, Vector2Int gridPos, out string reason)
        {
            reason = null;
            if (data == null) { reason = "건물 데이터가 없습니다."; return false; }
            Vector2Int size = data.GridSize;
            if (gridPos.x < 0 || gridPos.y < 0 || gridPos.x + size.x > 60 || gridPos.y + size.y > 40)
            {
                reason = "맵 밖입니다.";
                return false;
            }

            for (int x = gridPos.x; x < gridPos.x + size.x; x++)
            {
                for (int z = gridPos.y; z < gridPos.y + size.y; z++)
                {
                    var occupant = _occupiedGrid[x, z];
                    var roadPole = _roadPoleGrid[x, z];
                    bool blocked = data.isPowerPole ? roadPole != null || (occupant != null && !IsRoad(occupant))
                                 : data.IsRoad ? occupant != null
                                 : occupant != null || roadPole != null;
                    if (blocked)
                    {
                        reason = "다른 건물과 겹칩니다.";
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// 도로 접근 조건: 건물 영역의 바깥 둘레(상하좌우로 맞닿은 칸, 대각선 제외) 중 한 칸 이상이 도로면 true.
        /// 도로 자신은 항상 true(어디든), 전신주는 도로 위에 세우는 경우도 true. requireRoadAccess를 끄면 항상 true
        /// </summary>
        public bool HasRoadAccess(BuildingData data, Vector2Int gridPos)
        {
            if (data == null) return false;
            if (!requireRoadAccess || data.IsRoad) return true;

            Vector2Int size = data.GridSize;
            if (data.isPowerPole)
            {
                bool allRoad = true;
                for (int x = gridPos.x; x < gridPos.x + size.x; x++)
                    for (int z = gridPos.y; z < gridPos.y + size.y; z++)
                        if (!IsRoadCell(x, z)) allRoad = false;
                if (allRoad) return true;
            }

            for (int x = gridPos.x; x < gridPos.x + size.x; x++)
            {
                if (IsRoadCell(x, gridPos.y - 1) || IsRoadCell(x, gridPos.y + size.y)) return true;
            }
            for (int z = gridPos.y; z < gridPos.y + size.y; z++)
            {
                if (IsRoadCell(gridPos.x - 1, z) || IsRoadCell(gridPos.x + size.x, z)) return true;
            }
            return false;
        }

        /// <summary>그 칸에 도로가 깔려 있는지 (맵 밖이면 false)</summary>
        public bool IsRoadCell(int x, int z) =>
            x >= 0 && z >= 0 && x < 60 && z < 40 && IsRoad(_occupiedGrid[x, z]);

        private static bool IsRoad(BuildingInstance b) => b != null && b.Data != null && b.Data.IsRoad;

        /// <summary>그 칸의 건물 (도로 위 전신주가 있으면 전신주, 없으면 그 칸을 차지한 건물, 비었으면 null)</summary>
        public BuildingInstance GetBuildingAt(Vector2Int cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= 60 || cell.y >= 40) return null;
            return _roadPoleGrid[cell.x, cell.y] != null ? _roadPoleGrid[cell.x, cell.y] : _occupiedGrid[cell.x, cell.y];
        }

        /// <summary>그 칸에 도로 위 전신주가 있으면 그 전신주 (없으면 null)</summary>
        public BuildingInstance GetRoadPoleAt(Vector2Int cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= 60 || cell.y >= 40) return null;
            return _roadPoleGrid[cell.x, cell.y];
        }

        /// <summary>
        /// 건물 생성 + 점유 칸 등록 + 게임 루프 등록 (설치와 세이브 복원이 같이 씀). 검사·비용·OnBuildingPlaced는 부르는 쪽에서
        /// </summary>
        private BuildingInstance SpawnBuilding(BuildingData data, Vector2Int gridPos, float rotationY)
        {
            Vector2Int size = data.GridSize;
            Vector3 spawnPos = GetBuildingWorldPosition(gridPos.x, gridPos.y, size.x, size.y);
            // 미리보기와 같은 회전으로 생성 (예전에는 Quaternion.identity라 프리팹 회전이 사라졌음)
            GameObject buildingObj = Instantiate(data.buildingPrefab, spawnPos, GetPlacementRotation(data, rotationY));

            BuildingInstance instance = buildingObj.AddComponent<BuildingInstance>();
            instance.Init(data, gridPos, rotationY);

            // 전신주 데이터면 전력 연결 컴포넌트 부착 (프리팹 수정 없이 동작)
            if (data.isPowerPole && !buildingObj.TryGetComponent<InfraTown.Power.UtilityPoleInstance>(out _))
            {
                buildingObj.AddComponent<InfraTown.Power.UtilityPoleInstance>();
            }

            for (int x = gridPos.x; x < gridPos.x + size.x; x++)
            {
                for (int z = gridPos.y; z < gridPos.y + size.y; z++)
                {
                    // 전신주를 도로 위에 세우면 도로는 그대로 두고 전신주는 따로 보관
                    if (data.isPowerPole && IsRoadCell(x, z)) _roadPoleGrid[x, z] = instance;
                    else _occupiedGrid[x, z] = instance;
                }
            }

            GameLoopManager.Instance?.RegisterBuilding(instance);
            return instance;
        }

        // ─── 세이브 ───

        /// <summary>건물 ID(없으면 이름)로 등록된 건물 데이터 찾기</summary>
        public BuildingData FindBuildingData(string buildingID, string buildingName = null)
        {
            if (buildingDatabase == null) return null;
            if (!string.IsNullOrEmpty(buildingID))
            {
                foreach (var d in buildingDatabase) if (d != null && d.buildingID == buildingID) return d;
            }
            if (!string.IsNullOrEmpty(buildingName))
            {
                foreach (var d in buildingDatabase) if (d != null && d.buildingName == buildingName) return d;
            }
            return null;
        }

        /// <summary>
        /// 저장된 건물 되살리기: 돈·해금 검사 없이 그 자리에 그 회전으로 생성. 칸을 벗어나거나 겹치면 null.
        /// 설치 대사·튜토리얼이 나오지 않도록 OnBuildingPlaced는 보내지 않는다
        /// </summary>
        public BuildingInstance RestoreBuilding(BuildingData data, Vector2Int gridPos, float rotationY)
        {
            if (data == null || data.buildingPrefab == null) return null;
            // 도로 접근·자금은 검사하지 않음 (저장 당시 이미 지어진 건물 — 그 뒤 도로가 철거됐어도 그대로 되살림)
            if (!IsAreaFree(data, gridPos, out _)) return null;
            return SpawnBuilding(data, gridPos, Mathf.Repeat(rotationY, 360f));
        }

        /// <summary>예전 자리 검사 (맵 안 + 빈 칸 + 고른 건물의 자금). 도로 접근은 보지 않음 — 설치·미리보기는 CanPlaceAt을 쓴다</summary>
        private bool CanPlaceBuilding(int startX, int startZ, int sizeX, int sizeZ)
        {
            if (startX < 0 || startZ < 0 || startX + sizeX > 60 || startZ + sizeZ > 40)
                return false;

            for (int x = startX; x < startX + sizeX; x++)
            {
                for (int z = startZ; z < startZ + sizeZ; z++)
                {
                    if (_occupiedGrid[x, z] != null || _roadPoleGrid[x, z] != null) return false;
                }
            }

            if (_selectedBuildingData != null && EconomyManager.Instance != null)
            {
                if (EconomyManager.Instance.CurrentMoney < _selectedBuildingData.constructionCost)
                {
                    return false;
                }
            }

            return true;
        }

        private Vector3 GetBuildingWorldPosition(int startX, int startZ, int sizeX, int sizeZ)
        {
            float tileSize = 2.0f;
            float posX = (startX * tileSize) + (sizeX * tileSize * 0.5f);
            float posZ = (startZ * tileSize) + (sizeZ * tileSize * 0.5f);
            return new Vector3(posX, 0f, posZ);
        }

        private void SetPreviewMaterial(Material mat)
        {
            if (_previewRenderers == null || mat == null) return;
            foreach (var rend in _previewRenderers)
            {
                rend.material = mat;
            }
        }

        public void CancelBuildingMode()
        {
            ClearBuildingMode();
            NotifyPlacementMode();
        }

        private void ClearBuildingMode()
        {
            ClearDragState();
            _selectedBuildingData = null;
            if (_previewInstance != null)
            {
                _previewInstance.SetActive(false); // Destroy는 프레임 끝에 되므로 바로 숨김 (Jinyu12)
                if (Application.isPlaying) Destroy(_previewInstance);
                else DestroyImmediate(_previewInstance); // 에디터 테스트에서 호출될 때
                _previewInstance = null;
            }

            if (highlightController != null)
            {
                highlightController.HideHighlight();
            }
        }

        private void HandleBuildingDestroyed(BuildingInstance building)
        {
            Vector2Int size = building.Data.GridSize;
            Vector2Int pos = building.OriginGridPos;

            for (int x = pos.x; x < pos.x + size.x; x++)
            {
                for (int z = pos.y; z < pos.y + size.y; z++)
                {
                    if (x >= 0 && x < 60 && z >= 0 && z < 40)
                    {
                        if (_occupiedGrid[x, z] == building)
                        {
                            _occupiedGrid[x, z] = null;
                        }
                        if (_roadPoleGrid[x, z] == building)
                        {
                            _roadPoleGrid[x, z] = null;
                        }
                    }
                }
            }
        }
    }
}

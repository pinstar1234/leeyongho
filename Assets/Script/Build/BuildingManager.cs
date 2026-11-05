using System.Collections.Generic;
using UnityEngine;
using InfraTown.Grid;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.Population;

namespace InfraTown.Building
{
    public class BuildingManager : MonoBehaviour
    {
        public static BuildingManager Instance { get; private set; }

        [Header("건물 DB (ScriptableObjects)")]
        [SerializeField] private List<BuildingData> buildingDatabase;

        [Header("프리뷰 마테리얼 (반투명)")]
        [SerializeField] private Material validPreviewMaterial;   // 건설 가능 (초록 반투명)
        [SerializeField] private Material invalidPreviewMaterial; // 건설 불가능 (빨강 반투명)

        [Header("하이라이트 컨트롤러 참조")]
        [SerializeField] private GridHighlightController highlightController;

        private BuildingData _selectedBuildingData;
        private GameObject _previewInstance;
        private MeshRenderer[] _previewRenderers;

        // 60x40 타일별 점유중인 건물 인스턴스 저장 배열
        private BuildingInstance[,] _occupiedGrid = new BuildingInstance[60, 40];

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (highlightController == null)
            {
                highlightController = GetComponent<GridHighlightController>();
            }
        }

        private void Start()
        {
            if (LightweightGridManager.Instance != null)
            {
                LightweightGridManager.Instance.OnGridHoverChanged += UpdateBuildingPreview;
                LightweightGridManager.Instance.OnGridClicked += TryPlaceBuilding;
            }

            BuildingInstance.OnBuildingDestroyed += HandleBuildingDestroyed;

            if (highlightController != null)
            {
                highlightController.HideHighlight();
            }
        }

        private void OnDestroy()
        {
            if (LightweightGridManager.Instance != null)
            {
                LightweightGridManager.Instance.OnGridHoverChanged -= UpdateBuildingPreview;
                LightweightGridManager.Instance.OnGridClicked -= TryPlaceBuilding;
            }

            BuildingInstance.OnBuildingDestroyed -= HandleBuildingDestroyed;
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

            // 1. 현재 인구수 기반 해금 검증
            if (!IsBuildingUnlocked(data))
            {
                Debug.LogWarning($"[BuildingManager] '{data.buildingName}'은(는) 해금되지 않았습니다. (필요 인구수: {data.requiredPopulation}명)");
                return;
            }

            CancelBuildingMode();

            _selectedBuildingData = data;

            GameObject prefabToInstantiate = data.previewPrefab != null ? data.previewPrefab : data.buildingPrefab;
            _previewInstance = Instantiate(prefabToInstantiate);

            foreach (var col in _previewInstance.GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }

            _previewRenderers = _previewInstance.GetComponentsInChildren<MeshRenderer>();
            SetPreviewMaterial(validPreviewMaterial);
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

            bool canBuild = CanPlaceBuilding(gridPos.x, gridPos.y, size.x, size.y);
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

            // 해금 상태 다시 검증
            if (!IsBuildingUnlocked(_selectedBuildingData))
            {
                Debug.LogWarning("[BuildingManager] 해금 조건 미달로 건설할 수 없습니다.");
                CancelBuildingMode();
                return;
            }

            Vector2Int size = _selectedBuildingData.GridSize;

            if (CanPlaceBuilding(gridPos.x, gridPos.y, size.x, size.y))
            {
                int cost = _selectedBuildingData.constructionCost;

                if (EconomyManager.Instance != null && !EconomyManager.Instance.TrySpendMoney(cost))
                {
                    Debug.LogWarning("[BuildingManager] 건설 자금이 부족합니다!");
                    return;
                }

                Vector3 spawnPos = GetBuildingWorldPosition(gridPos.x, gridPos.y, size.x, size.y);
                GameObject buildingObj = Instantiate(_selectedBuildingData.buildingPrefab, spawnPos, Quaternion.identity);

                BuildingInstance instance = buildingObj.AddComponent<BuildingInstance>();
                instance.Init(_selectedBuildingData, gridPos);

                for (int x = gridPos.x; x < gridPos.x + size.x; x++)
                {
                    for (int z = gridPos.y; z < gridPos.y + size.y; z++)
                    {
                        _occupiedGrid[x, z] = instance;
                    }
                }

                GameLoopManager.Instance?.RegisterBuilding(instance);
            }
        }

        private bool CanPlaceBuilding(int startX, int startZ, int sizeX, int sizeZ)
        {
            if (startX < 0 || startZ < 0 || startX + sizeX > 60 || startZ + sizeZ > 40)
                return false;

            for (int x = startX; x < startX + sizeX; x++)
            {
                for (int z = startZ; z < startZ + sizeZ; z++)
                {
                    if (_occupiedGrid[x, z] != null) return false;
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
            _selectedBuildingData = null;
            if (_previewInstance != null)
            {
                Destroy(_previewInstance);
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
                    }
                }
            }
        }
    }
}
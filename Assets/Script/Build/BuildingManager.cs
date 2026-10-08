using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Grid;

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

        private BuildingData _selectedBuildingData;
        private GameObject _previewInstance;
        private MeshRenderer[] _previewRenderers;

        // 타일별 건물 점유 상태 관리 (60x40)
        private BuildingInstance[,] _occupiedGrid = new BuildingInstance[60, 40];

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (LightweightGridManager.Instance != null)
            {
                LightweightGridManager.Instance.OnGridHoverChanged += UpdateBuildingPreview;
                LightweightGridManager.Instance.OnGridClicked += TryPlaceBuilding;
            }

            BuildingInstance.OnBuildingDestroyed += HandleBuildingDestroyed;
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
        /// UI 버튼 클릭 시 건설할 건물을 선택하는 함수
        /// </summary>
        public void SelectBuildingToBuild(BuildingData data)
        {
            CancelBuildingMode();

            _selectedBuildingData = data;

            // 반투명 프리뷰 오브젝트 생성
            GameObject prefabToInstantiate = data.previewPrefab != null ? data.previewPrefab : data.buildingPrefab;
            _previewInstance = Instantiate(prefabToInstantiate);

            // 프리뷰에 Collider가 있다면 레이캐스트 방해 방지를 위해 비활성화
            foreach (var col in _previewInstance.GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }

            _previewRenderers = _previewInstance.GetComponentsInChildren<MeshRenderer>();
            SetPreviewMaterial(validPreviewMaterial);
        }

        /// <summary>
        /// 마우스 가리키는 타일에 맞춰 프리뷰를 Snap 및 위치 이동
        /// </summary>
        private void UpdateBuildingPreview(Vector2Int gridPos)
        {
            if (_selectedBuildingData == null || _previewInstance == null) return;

            if (gridPos.x == -1 || gridPos.y == -1)
            {
                _previewInstance.SetActive(false);
                return;
            }

            _previewInstance.SetActive(true);

            // 규격(1x1, 2x2, 3x3)에 따른 타일 피벗 위치 계산
            Vector2Int size = _selectedBuildingData.GridSize;
            Vector3 worldPos = GetBuildingWorldPosition(gridPos.x, gridPos.y, size.x, size.y);
            _previewInstance.transform.position = worldPos;

            // 건설 가능 여부 체크하여 프리뷰 마테리얼 색상 변경
            bool canBuild = CanPlaceBuilding(gridPos.x, gridPos.y, size.x, size.y);
            SetPreviewMaterial(canBuild ? validPreviewMaterial : invalidPreviewMaterial);
        }

        /// <summary>
        /// 클릭 시 실제 건물 건설 및 그리드 점유 등록
        /// </summary>
        private void TryPlaceBuilding(Vector2Int gridPos)
        {
            if (_selectedBuildingData == null) return;

            Vector2Int size = _selectedBuildingData.GridSize;

            if (CanPlaceBuilding(gridPos.x, gridPos.y, size.x, size.y))
            {
                Vector3 spawnPos = GetBuildingWorldPosition(gridPos.x, gridPos.y, size.x, size.y);
                GameObject buildingObj = Instantiate(_selectedBuildingData.buildingPrefab, spawnPos, Quaternion.identity);

                BuildingInstance instance = buildingObj.AddComponent<BuildingInstance>();
                instance.Init(_selectedBuildingData, gridPos);

                // 그리드 점유 배열 등록
                for (int x = gridPos.x; x < gridPos.x + size.x; x++)
                {
                    for (int z = gridPos.y; z < gridPos.y + size.y; z++)
                    {
                        _occupiedGrid[x, z] = instance;
                    }
                }

                // 취소 없이 연속 건설을 위해 프리뷰 유지
            }
        }

        /// <summary>
        /// 건물이 건설 가능한지 타일 범위를 검증
        /// </summary>
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
            return true;
        }

        /// <summary>
        /// 타일 크기에 따른 건물 월드 좌표 계산 (다중 타일 지원)
        /// </summary>
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
        }

        private void HandleBuildingDestroyed(BuildingInstance building)
        {
            // 점유되어 있던 타일 영역 해제
            Vector2Int size = building.Data.GridSize;
            Vector2Int pos = building.OriginGridPos;

            for (int x = pos.x; x < pos.x + size.x; x++)
            {
                for (int z = pos.y; z < pos.y + size.y; z++)
                {
                    if (_occupiedGrid[x, z] == building)
                    {
                        _occupiedGrid[x, z] = null;
                    }
                }
            }
        }

        public BuildingData GetBuildingDataByIndex(int index)
        {
            if (index >= 0 && index < buildingDatabase.Count)
                return buildingDatabase[index];
            return null;
        }
    }
}
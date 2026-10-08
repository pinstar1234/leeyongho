using System;
using UnityEngine;

namespace InfraTown.Grid
{
    public class GridManager : MonoBehaviour
    {
        public static GridManager Instance { get; private set; }

        [Header("Grid Configuration")]
        [SerializeField] private int width = 60;
        [SerializeField] private int height = 40;
        [SerializeField] private float tileSize = 2.0f; // 기획서 상 1타일 = 2m x 2m

        [Header("Tile Prefab & Visuals")]
        [SerializeField] private GameObject tilePrefab;
        [SerializeField] private LayerMask tileLayerMask;
        [SerializeField] private Color defaultHighlightColor = new Color(0.2f, 0.8f, 1.0f, 0.5f);
        [SerializeField] private Color buildValidColor = new Color(0.2f, 1.0f, 0.2f, 0.6f);

        private TileController[,] _gridArray;
        private TileController _currentHoveredTile;
        private Camera _mainCamera;

        public event Action<TileController> OnTileHoverChanged;
        public event Action<TileController> OnTileClicked;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _mainCamera = Camera.main;
            GenerateGrid();
        }

        private void Update()
        {
            HandleMouseInteraction();
        }

        /// <summary>
        /// 60 x 40 타일 생성 로직
        /// </summary>
        private void GenerateGrid()
        {
            _gridArray = new TileController[width, height];
            Vector3 originPos = transform.position;

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    // Center pivot 배치 (타일의 중심점 기준 위치)
                    Vector3 spawnPos = originPos + new Vector3(x * tileSize + tileSize * 0.5f, 0, z * tileSize + tileSize * 0.5f);
                    GameObject tileObj = Instantiate(tilePrefab, spawnPos, Quaternion.identity, transform);

                    // 타일 크기 2m x 2m 스케일 조정 (기본 Cube/Plane 1unit 기준)
                    tileObj.transform.localScale = new Vector3(tileSize, 0.1f, tileSize);

                    TileController tile = tileObj.GetComponent<TileController>();
                    if (tile == null)
                    {
                        tile = tileObj.AddComponent<TileController>();
                    }

                    tile.Init(x, z);
                    _gridArray[x, z] = tile;
                }
            }
        }

        /// <summary>
        /// 마우스 레이캐스트를 통한 타일 호버링 & 상호작용 검출
        /// </summary>
        private void HandleMouseInteraction()
        {
            Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit, 500f, tileLayerMask))
            {
                TileController hitTile = hit.collider.GetComponent<TileController>();

                if (hitTile != _currentHoveredTile)
                {
                    // 이전 타일 하이라이트 해제
                    if (_currentHoveredTile != null)
                    {
                        _currentHoveredTile.SetHighlight(false, Color.white);
                    }

                    _currentHoveredTile = hitTile;

                    // 새 타일 하이라이트 적용
                    if (_currentHoveredTile != null)
                    {
                        _currentHoveredTile.SetHighlight(true, defaultHighlightColor);
                    }

                    OnTileHoverChanged?.Invoke(_currentHoveredTile);
                }

                // 클릭 상호작용 (건물 건설 등 연동 시 사용)
                if (Input.GetMouseButtonDown(0) && _currentHoveredTile != null)
                {
                    OnTileClicked?.Invoke(_currentHoveredTile);
                }
            }
            else
            {
                if (_currentHoveredTile != null)
                {
                    _currentHoveredTile.SetHighlight(false, Color.white);
                    _currentHoveredTile = null;
                    OnTileHoverChanged?.Invoke(null);
                }
            }
        }

        /// <summary>
        /// 건물 건설 모드 시 여러 타일 범위를 한 번에 하이라이트할 때 사용하는 헬퍼 함수 (예: 2x2, 3x3 건물)
        /// </summary>
        public void HighlightBuildingArea(int startX, int startZ, int sizeX, int sizeZ, bool isValid)
        {
            Color targetColor = isValid ? buildValidColor : Color.red;

            for (int x = startX; x < startX + sizeX; x++)
            {
                for (int z = startZ; z < startZ + sizeZ; z++)
                {
                    if (x >= 0 && x < width && z >= 0 && z < height)
                    {
                        _gridArray[x, z].SetHighlight(true, targetColor);
                    }
                }
            }
        }

        public TileController GetTile(int x, int z)
        {
            if (x >= 0 && x < width && z >= 0 && z < height)
                return _gridArray[x, z];
            return null;
        }
    }
}
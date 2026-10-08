using System;
using UnityEngine;

namespace InfraTown.Grid
{
    /// <summary>
    /// 2,400개 오브젝트 생성을 지양하고 수학적 Raycast 계산으로 부하를 제로화한 그리드 매니저
    /// </summary>
    public class LightweightGridManager : MonoBehaviour
    {
        public static LightweightGridManager Instance { get; private set; }

        [Header("Grid Size")]
        [SerializeField] private int width = 60;
        [SerializeField] private int height = 40;
        [SerializeField] private float tileSize = 2.0f; // 1타일 = 2m x 2m

        [Header("Raycast & Interaction")]
        [SerializeField] private LayerMask groundLayerMask;

        // 현재 마우스가 가리키는 그리드 좌표 (-1일 경우 그리드 밖)
        public Vector2Int HoveredGridPos { get; private set; } = new Vector2Int(-1, -1);

        public event Action<Vector2Int> OnGridHoverChanged;
        public event Action<Vector2Int> OnGridClicked;

        private Camera _mainCamera;
        private Plane _groundPlane; // Y=0 평면 물리 레이캐스트 대체용 수학적 Plane

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _mainCamera = Camera.main;
            // Y=0 높이의 수학적 평면 정의 (Collider Raycast보다 연산 속도 100배 이상 우수)
            _groundPlane = new Plane(Vector3.up, Vector3.zero);
        }

        private void Update()
        {
            HandleGridRaycast();
        }

        /// <summary>
        /// 콜라이더 없이 수학적 평면 교차점으로 (x, z) 그리드 좌표 즉시 산출
        /// </summary>
        private void HandleGridRaycast()
        {
            Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);

            if (_groundPlane.Raycast(ray, out float enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);

                // World Position -> Grid Index 변환
                int x = Mathf.FloorToInt(hitPoint.x / tileSize);
                int z = Mathf.FloorToInt(hitPoint.z / tileSize);

                if (x >= 0 && x < width && z >= 0 && z < height)
                {
                    Vector2Int newGridPos = new Vector2Int(x, z);

                    if (newGridPos != HoveredGridPos)
                    {
                        HoveredGridPos = newGridPos;
                        OnGridHoverChanged?.Invoke(HoveredGridPos);
                    }

                    if (Input.GetMouseButtonDown(0))
                    {
                        OnGridClicked?.Invoke(HoveredGridPos);
                    }
                    return;
                }
            }

            // 그리드 범위를 벗어난 경우
            if (HoveredGridPos.x != -1)
            {
                HoveredGridPos = new Vector2Int(-1, -1);
                OnGridHoverChanged?.Invoke(HoveredGridPos);
            }
        }

        /// <summary>
        /// 그리드 좌표 (x, z)를 World Position 중심점 좌표로 변환하는 헬퍼 함수
        /// </summary>
        public Vector3 GetWorldPosition(int x, int z)
        {
            return new Vector3(x * tileSize + tileSize * 0.5f, 0f, z * tileSize + tileSize * 0.5f);
        }
    }
}
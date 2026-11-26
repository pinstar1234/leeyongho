using System;
using UnityEngine;
using UIEventSystem = UnityEngine.EventSystems.EventSystem;

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

        /// <summary>왼쪽 버튼을 누른 순간 (누른 칸). 드래그도 이 칸에서 시작된다</summary>
        public event Action<Vector2Int> OnGridClicked;

        // ─── 드래그 (사용자 기획 2026-11-25: 드래그로 여러 칸 한 번에 건설) ───

        /// <summary>왼쪽 버튼을 누른 채 끌고 있는지</summary>
        public bool IsDragging { get; private set; }

        /// <summary>드래그를 시작한 칸</summary>
        public Vector2Int DragStartCell { get; private set; } = new Vector2Int(-1, -1);

        /// <summary>드래그 중 마지막으로 가리킨 그리드 안 칸 (UI 위·그리드 밖으로 나가도 마지막 칸 유지)</summary>
        public Vector2Int DragCurrentCell { get; private set; } = new Vector2Int(-1, -1);

        /// <summary>드래그 시작 (시작 칸)</summary>
        public event Action<Vector2Int> OnGridDragStarted;

        /// <summary>드래그 중 가리킨 칸이 바뀜 (시작 칸, 지금 칸)</summary>
        public event Action<Vector2Int, Vector2Int> OnGridDragUpdated;

        /// <summary>버튼을 떼서 드래그 끝 (시작 칸, 끝 칸). 제자리 클릭도 시작 = 끝으로 한 번 나간다</summary>
        public event Action<Vector2Int, Vector2Int> OnGridDragEnded;

        /// <summary>드래그 취소 (오른쪽 버튼 또는 CancelDrag)</summary>
        public event Action OnGridDragCanceled;

        private Camera _mainCamera;
        private Plane _groundPlane; // Y=0 평면 물리 레이캐스트 대체용 수학적 Plane

        /// <summary>
        /// 현재 가리키는 칸 클릭 처리. 마우스가 UI 위에 있으면 무시한다
        /// (예전에는 UI 버튼을 눌러도 그 아래 칸에 건물이 지어졌음). 클릭이 받아들여지면 그 칸에서 드래그가 시작된다
        /// </summary>
        public bool TryRaiseClick()
        {
            if (HoveredGridPos.x < 0 || HoveredGridPos.y < 0) return false;
            if (InfraTown.Core.PointerGuard.IsPointerOverUI()) return false;
            OnGridClicked?.Invoke(HoveredGridPos);
            BeginDrag(HoveredGridPos);
            return true;
        }

        /// <summary>그 칸에서 드래그 시작 (마우스 입력 대신 테스트·UI가 직접 부를 수 있음)</summary>
        public void BeginDrag(Vector2Int cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= width || cell.y >= height) return;
            IsDragging = true;
            DragStartCell = cell;
            DragCurrentCell = cell;
            OnGridDragStarted?.Invoke(cell);
        }

        /// <summary>드래그 중 가리키는 칸 갱신 (그리드 밖 칸은 무시하고 마지막 칸 유지)</summary>
        public void UpdateDrag(Vector2Int cell)
        {
            if (!IsDragging || cell.x < 0 || cell.y < 0 || cell.x >= width || cell.y >= height || cell == DragCurrentCell) return;
            DragCurrentCell = cell;
            OnGridDragUpdated?.Invoke(DragStartCell, DragCurrentCell);
        }

        /// <summary>드래그 끝 (버튼을 뗌) → OnGridDragEnded(시작, 끝)</summary>
        public void EndDrag()
        {
            if (!IsDragging) return;
            IsDragging = false;
            OnGridDragEnded?.Invoke(DragStartCell, DragCurrentCell);
        }

        /// <summary>드래그 취소 (아무것도 짓지 않음)</summary>
        public void CancelDrag()
        {
            if (!IsDragging) return;
            IsDragging = false;
            OnGridDragCanceled?.Invoke();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { InfraTown.Core.SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;

            _mainCamera = Camera.main;
            // Y=0 높이의 수학적 평면 정의 (Collider Raycast보다 연산 속도 100배 이상 우수)
            _groundPlane = new Plane(Vector3.up, Vector3.zero);
        }

        private void LateUpdate()
        {
            HandleGridRaycast();
            HandleDragInput();
        }

        /// <summary>드래그 중 오른쪽 버튼 → 취소, 왼쪽 버튼을 떼면(UI 위에서 떼도) 끝</summary>
        private void HandleDragInput()
        {
            if (!IsDragging) return;
            if (Input.GetMouseButtonDown(1)) CancelDrag();
            else if (!Input.GetMouseButton(0)) EndDrag();
        }

        /// <summary>
        /// 콜라이더 없이 수학적 평면 교차점으로 (x, z) 그리드 좌표 즉시 산출
        /// </summary>
        private void HandleGridRaycast()
        {
            Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);

            bool pointerOverUI = UIEventSystem.current != null && UIEventSystem.current.IsPointerOverGameObject();
            if (!InfraTown.UI.GamePauseMenu.IsOpen && !pointerOverUI && _groundPlane.Raycast(ray, out float enter))
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
                    if (IsDragging) UpdateDrag(newGridPos);

                    if (Input.GetMouseButtonDown(0) && !IsDragging)
                    {
                        TryRaiseClick();
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

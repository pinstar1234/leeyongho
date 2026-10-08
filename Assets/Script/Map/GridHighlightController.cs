using UnityEngine;

namespace InfraTown.Grid
{
    public class GridHighlightController : MonoBehaviour
    {
        [Header("Highlight Prefab & Visuals")]
        [SerializeField] private GameObject highlightPrefab;
        [SerializeField] private float yOffset = 0.02f; // Z-fighting(깜빡임) 방지를 위한 Y축 미세 띄움
        [SerializeField] private float smoothSpeed = 25f; // 마우스 이동 시 하이라이트 이동 부드러움 정도

        private GameObject _highlightInstance;
        private Vector3 _targetPosition;
        private bool _isVisible = false;

        private void Start()
        {
            if (highlightPrefab != null)
            {
                // 단 1개의 하이라이트 인스턴스만 생성하여 재사용
                _highlightInstance = Instantiate(highlightPrefab, transform);
                _highlightInstance.SetActive(false);
            }

            // GridManager 호버 이벤트 구독
            if (LightweightGridManager.Instance != null)
            {
                LightweightGridManager.Instance.OnGridHoverChanged += HandleGridHoverChanged;
            }
        }

        private void OnDestroy()
        {
            if (LightweightGridManager.Instance != null)
            {
                LightweightGridManager.Instance.OnGridHoverChanged -= HandleGridHoverChanged;
            }
        }

        private void Update()
        {
            if (_isVisible && _highlightInstance != null)
            {
                // 목표 위치로 부드럽게 이동 (보간)
                _highlightInstance.transform.position = Vector3.Lerp(
                    _highlightInstance.transform.position,
                    _targetPosition,
                    Time.deltaTime * smoothSpeed
                );
            }
        }

        /// <summary>
        /// 마우스 가리키는 타일 좌표 변경 시 호출되는 이벤트 핸들러
        /// </summary>
        private void HandleGridHoverChanged(Vector2Int gridPos)
        {
            if (_highlightInstance == null) return;

            // -1, -1은 그리드 범위를 벗어난 상태
            if (gridPos.x == -1 || gridPos.y == -1)
            {
                _isVisible = false;
                _highlightInstance.SetActive(false);
                return;
            }

            // LightweightGridManager에서 해당 타일의 월드 중앙 위치 계산
            Vector3 worldPos = LightweightGridManager.Instance.GetWorldPosition(gridPos.x, gridPos.y);
            _targetPosition = new Vector3(worldPos.x, worldPos.y + yOffset, worldPos.z);

            if (!_isVisible)
            {
                _isVisible = true;
                _highlightInstance.transform.position = _targetPosition; // 최초 활성화 시 즉시 위치 세팅
                _highlightInstance.SetActive(true);
            }
        }

        /// <summary>
        /// 건물 설치 가이드 모드 등 상황에 따라 하이라이트 색상을 바꿀 수 있는 함수
        /// </summary>
        public void SetHighlightColor(Color color)
        {
            if (_highlightInstance != null && _highlightInstance.TryGetComponent<Renderer>(out var rend))
            {
                rend.material.SetColor("_BaseColor", color);
                rend.material.SetColor("_EmissionColor", color * 1.5f);
            }
        }
    }
}
using UnityEngine;

namespace InfraTown.Grid
{
    public class GridHighlightController : MonoBehaviour
    {
        [Header("Highlight Prefab & Visuals")]
        [SerializeField] private GameObject highlightPrefab;
        [SerializeField] private float yOffset = 0.05f; // Z-Fighting(깜빡임) 방지용 Y축 값

        private GameObject _highlightInstance;
        private Renderer _highlightRenderer;
        private const float TileSize = 2.0f; // 1타일 = 2m

        private void Awake()
        {
            InitHighlightObject();
        }

        private void InitHighlightObject()
        {
            if (_highlightInstance != null) return;

            if (highlightPrefab != null)
            {
                _highlightInstance = Instantiate(highlightPrefab, transform);
            }
            else
            {
                // 프리팹 미지정 시 기본 3D Quad 자동 생성 (안전장치)
                _highlightInstance = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _highlightInstance.transform.SetParent(transform);

                // 콜라이더 제거 (마우스 Raycast 방해 방지)
                if (_highlightInstance.TryGetComponent<Collider>(out var col))
                {
                    Destroy(col);
                }
            }

            _highlightInstance.name = "GridHighlight_Instance";
            _highlightRenderer = _highlightInstance.GetComponent<Renderer>();
            _highlightInstance.SetActive(false); // 시작 시 비활성화
        }

        /// <summary>
        /// 건물이 점유할 전체 타일 영역(sizeX x sizeZ) 하이라이트 켜기
        /// </summary>
        public void ShowHighlightArea(int startX, int startZ, int sizeX, int sizeZ)
        {
            if (_highlightInstance == null) InitHighlightObject();

            if (startX < 0 || startZ < 0)
            {
                HideHighlight();
                return;
            }

            // 1. 전체 점유 타일 범위의 중앙 월드 좌표 산출
            float posX = (startX * TileSize) + (sizeX * TileSize * 0.5f);
            float posZ = (startZ * TileSize) + (sizeZ * TileSize * 0.5f);

            // 2. 바닥 평면에 평평하게 배치 (Quad 회전 X: 90도)
            _highlightInstance.transform.position = new Vector3(posX, yOffset, posZ);
            _highlightInstance.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            // 3. 건물의 타일 규격(2m, 4m, 6m)에 맞는 스케일 적용
            _highlightInstance.transform.localScale = new Vector3(sizeX * TileSize, sizeZ * TileSize, 1f);

            if (!_highlightInstance.activeSelf)
            {
                _highlightInstance.SetActive(true);
            }
        }

        /// <summary>
        /// 하이라이트 비활성화
        /// </summary>
        public void HideHighlight()
        {
            if (_highlightInstance != null && _highlightInstance.activeSelf)
            {
                _highlightInstance.SetActive(false);
            }
        }

        public void SetColor(Color color)
        {
            if (_highlightRenderer != null)
            {
                _highlightRenderer.material.SetColor("_BaseColor", color);
                _highlightRenderer.material.SetColor("_Color", color);
            }
        }
    }
}
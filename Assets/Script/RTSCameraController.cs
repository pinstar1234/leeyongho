using UnityEngine;

namespace InfraTown.CameraSystem
{
    public class RTSCameraController : MonoBehaviour
    {
        [Header("Camera Height Zoom Settings (Y축 이동)")]
        [SerializeField] private float minHeight = 15f;    // 최소 높이 (최대 줌인)
        [SerializeField] private float maxHeight = 70f;    // 최대 높이 (최대 줌아웃)
        [SerializeField] private float zoomSpeed = 15f;    // 휠 감도
        [SerializeField] private float zoomSmoothness = 10f;

        [Header("Edge Scroll Settings")]
        [SerializeField] private float moveSpeed = 40f;     // 카메라 이동 속도
        [SerializeField] private float edgeThreshold = 20f; // 화면 가장자리 판정 범위(px)

        [Header("Map Boundaries (60x40 그리드 = 120m x 80m)")]
        [SerializeField] private Vector2 minBounds = new Vector2(0f, 0f);
        [SerializeField] private Vector2 maxBounds = new Vector2(120f, 80f);

        private Camera _cam;
        private float _targetY;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = Camera.main;

            _targetY = transform.position.y;
        }

        private void Update()
        {
            HandleHeightZoom();
            HandleEdgeMovement();
        }

        private void HandleHeightZoom()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");

            if (Mathf.Abs(scroll) > 0.01f)
            {
                _targetY -= scroll * zoomSpeed * 20f;
                _targetY = Mathf.Clamp(_targetY, minHeight, maxHeight);
            }

            Vector3 currentPos = transform.position;
            float newY = Mathf.Lerp(currentPos.y, _targetY, Time.unscaledDeltaTime * zoomSmoothness);

            // 높이 변경 후, 변경된 높이에 맞춰 즉시 위치 제한 보정
            Vector3 clampedPos = ClampPositionToDynamicBounds(new Vector3(currentPos.x, newY, currentPos.z));
            transform.position = clampedPos;
        }

        private void HandleEdgeMovement()
        {
            Vector3 moveDir = Vector3.zero;
            Vector3 mousePos = Input.mousePosition;

            if (mousePos.x >= Screen.width - edgeThreshold) moveDir += Vector3.right;
            if (mousePos.x <= edgeThreshold) moveDir += Vector3.left;
            if (mousePos.y >= Screen.height - edgeThreshold) moveDir += Vector3.forward;
            if (mousePos.y <= edgeThreshold) moveDir += Vector3.back;

            float hor = Input.GetAxisRaw("Horizontal");
            float ver = Input.GetAxisRaw("Vertical");
            if (hor != 0 || ver != 0)
            {
                moveDir = new Vector3(hor, 0f, ver).normalized;
            }

            if (moveDir != Vector3.zero)
            {
                Vector3 targetPos = transform.position + moveDir * (moveSpeed * Time.unscaledDeltaTime);

                // 현재 카메라 높이에 맞는 동적 경계 계산 적용
                transform.position = ClampPositionToDynamicBounds(targetPos);
            }
        }

        /// <summary>
        /// 현재 카메라의 높이(Y)와 FOV, 각도에 맞춰 화면 끝이 맵 밖으로 안 나가도록 동적으로 Clamp 경계 산출
        /// </summary>
        private Vector3 ClampPositionToDynamicBounds(Vector3 targetPos)
        {
            if (_cam == null) return targetPos;

            // 1. 현재 카메라 높이에서 바닥(Y=0)을 내려다보는 화면 시야(Frustum) 반폭/반높이 계산
            float halfFrustumHeight = targetPos.y * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfFrustumWidth = halfFrustumHeight * _cam.aspect;

            // 2. 카메라가 경사각(예: 60도)을 가지고 누워있는 경우 Z축 시야 오프셋 보정
            float angleRad = transform.eulerAngles.x * Mathf.Deg2Rad;
            float zOffset = targetPos.y / Mathf.Tan(angleRad); // 카메라 위치 기준 시선 중심 오프셋

            // 3. Y축 높이에 맞춘 동적 미니멈/맥시멈 경계값 계산
            float minX = minBounds.x + halfFrustumWidth;
            float maxX = maxBounds.x - halfFrustumWidth;

            // X축이 맵 크기보다 시야가 넓어진 경우 (과도한 줌아웃 시) 중앙 고정
            if (minX > maxX)
            {
                targetPos.x = (minBounds.x + maxBounds.x) * 0.5f;
            }
            else
            {
                targetPos.x = Mathf.Clamp(targetPos.x, minX, maxX);
            }

            float minZ = minBounds.y + halfFrustumHeight - zOffset;
            float maxZ = maxBounds.y - halfFrustumHeight - zOffset;

            if (minZ > maxZ)
            {
                targetPos.z = (minBounds.y + maxBounds.y) * 0.5f - zOffset;
            }
            else
            {
                targetPos.z = Mathf.Clamp(targetPos.z, minZ, maxZ);
            }

            return targetPos;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 center = new Vector3((minBounds.x + maxBounds.x) * 0.5f, 0f, (minBounds.y + maxBounds.y) * 0.5f);
            Vector3 size = new Vector3(maxBounds.x - minBounds.x, 1f, maxBounds.y - minBounds.y);
            Gizmos.DrawWireCube(center, size);
        }
    }
}
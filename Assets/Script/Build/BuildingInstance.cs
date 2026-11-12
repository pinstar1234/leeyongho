using UnityEngine;
using System;

namespace InfraTown.Building
{
    public class BuildingInstance : MonoBehaviour
    {
        public BuildingData Data { get; private set; }
        public Vector2Int OriginGridPos { get; private set; }

        /// <summary>설치 시 플레이어가 돌린 Y축 회전 (0/90/180/270, 프리팹 원래 회전 제외)</summary>
        public float RotationY { get; private set; }

        private float _lastRightClickTime = 0f;
        private const float DoubleClickThreshold = 0.3f; // 더블클릭 인정 시간 (0.3초)

        public static event Action<BuildingInstance> OnBuildingDestroyed;

        public void Init(BuildingData data, Vector2Int gridPos, float rotationY = 0f)
        {
            Data = data;
            OriginGridPos = gridPos;
            RotationY = rotationY;
        }


        private void OnMouseDown()
        {
            // 마우스 왼쪽 버튼 클릭 시 UI 호출 (UI 클릭 시 레이캐스트 방지 등은 상황에 맞게 추가)
            if (InfraTown.UI.BuildingUIManager.Instance != null)
            {
                InfraTown.UI.BuildingUIManager.Instance.OnBuildingClicked(this);
            }
        }

        //private void OnMouseOver()
        //{
        //    // 우클릭 더블클릭 검출
        //    if (Input.GetMouseButtonDown(1))
        //    {
        //        float timeSinceLastClick = Time.time - _lastRightClickTime;

        //        if (timeSinceLastClick <= DoubleClickThreshold)
        //        {
        //            DestroyBuilding();
        //        }

        //        _lastRightClickTime = Time.time;
        //    }
        //}

        public void DestroyBuilding()
        {
            NotifyDestroyed();
            Destroy(gameObject);
        }

        // ─── 철거 알림 보장 ───
        // UI 등에서 Destroy(gameObject)를 직접 불러도 점유 칸·인구·접근성·유지비가 갱신되도록
        // OnDestroy에서도 한 번만 알린다. 씬 전환/게임 종료로 사라지는 경우는 알리지 않는다.

        private bool _destroyNotified;
        private static bool _applicationQuitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetQuitFlag()
        {
            _applicationQuitting = false;
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        private static void OnApplicationQuitting() => _applicationQuitting = true;

        private void OnDestroy()
        {
            if (_applicationQuitting || !gameObject.scene.isLoaded) return;
            NotifyDestroyed();
        }

        private void NotifyDestroyed()
        {
            if (_destroyNotified || Data == null) return;
            _destroyNotified = true;
            OnBuildingDestroyed?.Invoke(this);
        }
    }
}
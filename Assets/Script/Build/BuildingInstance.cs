using UnityEngine;
using System;

namespace InfraTown.Building
{
    public class BuildingInstance : MonoBehaviour
    {
        public BuildingData Data { get; private set; }
        public Vector2Int OriginGridPos { get; private set; }

        private float _lastRightClickTime = 0f;
        private const float DoubleClickThreshold = 0.3f; // 더블클릭 인정 시간 (0.3초)

        public static event Action<BuildingInstance> OnBuildingDestroyed;

        public void Init(BuildingData data, Vector2Int gridPos)
        {
            Data = data;
            OriginGridPos = gridPos;
        }

        private void OnMouseOver()
        {
            // 우클릭 더블클릭 검출
            if (Input.GetMouseButtonDown(1))
            {
                float timeSinceLastClick = Time.time - _lastRightClickTime;

                if (timeSinceLastClick <= DoubleClickThreshold)
                {
                    DestroyBuilding();
                }

                _lastRightClickTime = Time.time;
            }
        }

        public void DestroyBuilding()
        {
            OnBuildingDestroyed?.Invoke(this);
            Destroy(gameObject);
        }
    }
}
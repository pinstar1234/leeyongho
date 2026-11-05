using UnityEngine;
using InfraTown.Building;

namespace InfraTown.Power
{
    public class UtilityPoleInstance : MonoBehaviour
    {
        [Header("정전 시 감점 수치")]
        [SerializeField] private float blackedOutSatisfactionPenalty = 15f; // 정전 시 만족도 감점

        private bool _isPowered = true;
        private BuildingInstance _buildingInstance;

        /// <summary>
        /// 전봇대 전력 공급 상태
        /// </summary>
        public bool IsPowered => _isPowered;

        /// <summary>
        /// DB에 설정된 전력 커버리지/공급 범위 타일 수
        /// </summary>
        public int SupplyRadius => (_buildingInstance != null && _buildingInstance.Data != null)
            ? _buildingInstance.Data.powerSupplyRadius
            : 4;

        /// <summary>
        /// 전봇대 그리드 원점 위치
        /// </summary>
        public Vector2Int GridPos => _buildingInstance != null ? _buildingInstance.OriginGridPos : Vector2Int.zero;

        private void Awake()
        {
            _buildingInstance = GetComponent<BuildingInstance>();
        }

        /// <summary>
        /// 전봇대 전력 공급 / 차단 설정
        /// </summary>
        public void SetPowerState(bool powered)
        {
            _isPowered = powered;

            // 시각적 연출 (정전 시 어둡게 표시 - Color.gray)
            if (TryGetComponent<MeshRenderer>(out var rend))
            {
                rend.material.color = powered ? Color.white : Color.gray;
            }
        }

        public float GetPenalty()
        {
            return _isPowered ? 0f : blackedOutSatisfactionPenalty;
        }
    }
}
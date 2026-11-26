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
        public int SupplyRadius => (Building != null && Building.Data != null)
            ? Building.Data.powerSupplyRadius
            : 4;

        /// <summary>
        /// 전봇대 그리드 원점 위치
        /// </summary>
        public Vector2Int GridPos => Building != null ? Building.OriginGridPos : Vector2Int.zero;

        /// <summary>전봇대 건물이 차지하는 칸 크기 (정전 순서를 정할 때 중심 계산용)</summary>
        public Vector2Int GridSize => Building != null && Building.Data != null ? Building.Data.GridSize : Vector2Int.one;

        // Awake 전에 조회되거나(에디터 테스트) 컴포넌트 추가 순서가 달라도 동작하도록 필요할 때 찾는다
        private BuildingInstance Building
        {
            get
            {
                if (_buildingInstance == null) _buildingInstance = GetComponent<BuildingInstance>();
                return _buildingInstance;
            }
        }

        private void Awake()
        {
            _buildingInstance = GetComponent<BuildingInstance>();
        }

        /// <summary>
        /// 전봇대 전력 공급 / 차단 설정
        /// </summary>
        public void SetPowerState(bool powered)
        {
            bool changed = _isPowered != powered || !_visualApplied;
            _isPowered = powered;
            if (changed) ApplyPowerVisual();
        }

        [Header("정전 표시")]
        [Tooltip("정전일 때 원래 색에 곱하는 색 (어둡게). 머티리얼 자체는 바꾸지 않는다")]
        [SerializeField] private Color blackoutTint = new Color(0.45f, 0.45f, 0.45f, 1f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP
        private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in
        private MaterialPropertyBlock _block;
        private bool _visualApplied;

        /// <summary>지금 정전 색(어둡게)을 보여 주는 중</summary>
        public bool IsShowingBlackout { get; private set; }

        /// <summary>
        /// 정전 시각 표시 (2026-11-26): 예전에는 맨 위 렌더러의 material.color를 흰색/회색으로 직접 바꿔서
        /// 팀원 머티리얼 색이 흰색으로 덮이고 바꿀 때마다 머티리얼 복사본이 생겼으며, 자식에 렌더러가 있는 모델은 표시가 안 됐다.
        /// 이제 자식 렌더러까지 MaterialPropertyBlock으로 '원래 색 × blackoutTint'만 덮어씌우고, 전기가 들어오면 덮어쓴 것을 지워 원래대로 돌린다
        /// </summary>
        private void ApplyPowerVisual()
        {
            _visualApplied = true;
            IsShowingBlackout = !_isPowered;
            if (_block == null) _block = new MaterialPropertyBlock();

            foreach (var rend in GetComponentsInChildren<Renderer>(true))
            {
                if (_isPowered)
                {
                    rend.SetPropertyBlock(null); // 원래 머티리얼 그대로
                    continue;
                }
                var mat = rend.sharedMaterial;
                if (mat == null) continue;
                _block.Clear();
                if (mat.HasProperty(BaseColorId)) _block.SetColor(BaseColorId, mat.GetColor(BaseColorId) * blackoutTint);
                if (mat.HasProperty(ColorId)) _block.SetColor(ColorId, mat.GetColor(ColorId) * blackoutTint);
                rend.SetPropertyBlock(_block);
            }
        }

        public float GetPenalty()
        {
            return _isPowered ? 0f : blackedOutSatisfactionPenalty;
        }
    }
}
using UnityEngine;

namespace InfraTown.Building
{
    public enum BuildingSizeType
    {
        Small,  // 소형 (1x1)
        Medium, // 중형 (2x2)
        Large   // 대형 (4x4)
    }

    public enum BuildingType
    {
        Essential, // 필수 시설 (소방서, 병원 등)
        Facility,  // 편의 시설 (마을회관, 편의점 등)
        Nimbys,    // 혐오/불쾌 시설 (축사, 쓰레기 매립지 등)
        Housing,   // 주택 (아파트, 단독주택 등)
        Infrastructure // 인프라 (도로, 전신주) — 기획서 4장, 즉시 건설
    }

    [CreateAssetMenu(fileName = "NewBuildingData", menuName = "InfraTown/Building Data")]
    public class BuildingData : ScriptableObject
    {
        [Header("기본 정보 및 해금 조건")]
        public string buildingID;
        public string buildingName;

        [TextArea(3, 5)]
        [Tooltip("UI 슬롯이나 안내 창에 표시할 건물 설명 문구")]
        public string description;

        [Tooltip("이 건물이 해금되기 위해 필요한 최소 '현재 인구수'")]
        public int requiredPopulation = 0; // 예: 0명(기본), 50명(티어1), 150명(티어2)

        [Tooltip("기획서 해금 티어 (1~4). 실제 해금 판정은 requiredPopulation으로 한다")]
        [Range(1, 4)]
        public int unlockTier = 1;

        [TextArea(2, 4)]
        [Tooltip("어르신·어린이 모드용 쉬운 설명 (기획서 4장)")]
        public string easyDescription;

        [TextArea(2, 4)]
        [Tooltip("해금 시 보여줄 스토리/메시지 (기획서 4장)")]
        public string unlockMessage;

        [Header("건설 / 철거 기간 (주 단위, 0 = 즉시. 사용자 결정 2026-11-24: 건설 소형 2·중형 4·대형 8주, 철거 1·2·3주)")]
        public int constructionWeeks = 0;
        public int demolitionWeeks = 0;

        [Header("5가지 핵심 규격 데이터")]
        public BuildingSizeType sizeType;   // 소형, 중형, 대형
        public BuildingType category;       // 필수, 편의, 혐오, 주택(Housing)
        public int requiredPower;           // 필요 전기량
        public int maintenanceCost;         // 유지비
        public int constructionCost;        // 건설 비용

        [Header("주택 전용 데이터")]
        [Tooltip("주택 건설 시 늘어나는 최대 인구 한계 수량")]
        public int housingCapacity = 0;

        [Header("만족도 및 영향 범위 (DB 설정)")]
        [Tooltip("주위 주택 만족도에 주는 영향 수치 (+: 상승, -: 하락)")]
        public float satisfactionImpact = 0f;

        [Tooltip("영향을 미치는 타일 범위 반지름")]
        public int satisfactionRadius = 3;

        [Header("전력 및 전봇대 범위 (DB 설정)")]
        [Tooltip("전봇대/발전소 등이 전기를 커버하거나 공급하는 타일 범위 반지름")]
        public int powerSupplyRadius = 4;

        [Tooltip("전신주처럼 주변 주택에 전기를 연결하는 건물이면 체크 (배치 시 UtilityPoleInstance가 자동으로 붙음)")]
        public bool isPowerPole = false;

        [Header("서비스 수치 (경찰/병원/소방/편의 전용)")]
        [Tooltip("경찰서일 경우 제공할 하루 순찰 횟수")]
        public int policePatrolCount = 3;

        [Tooltip("기본 접근 시간(분): 출동 준비·대기 등 거리와 무관한 시간.\n실제 접근 시간 = 이 값 + 주택과의 타일 거리 × ServiceAccessibilityManager의 '타일당 이동 시간'")]
        public float fixedAccessTimeMinutes = 5.0f;

        [Header("그래픽 및 프리팹")]
        public GameObject buildingPrefab;
        public GameObject previewPrefab;
        public Sprite buildingIcon;

        /// <summary>도로인지 (인프라 분류 중 전신주가 아닌 것). 다른 건물은 한 면 이상 도로에 닿아야 설치 가능 (사용자 기획 2026-11-24)</summary>
        public bool IsRoad => category == BuildingType.Infrastructure && !isPowerPole;

        public Vector2Int GridSize => sizeType switch
        {
            BuildingSizeType.Small => new Vector2Int(1, 1),
            BuildingSizeType.Medium => new Vector2Int(2, 2),
            BuildingSizeType.Large => new Vector2Int(4, 4),
            _ => new Vector2Int(1, 1)
        };

        /// <summary>
        /// 값 검사 (2026-11-26): 잘못 넣은 값(음수 비용·유지비·전력·수용 인구·반경, 빈 ID 등)이 조용히 게임을 망가뜨리지 않도록.
        /// 문제 목록을 돌려주고, fix면 음수를 0으로 고친다 (ID·이름·프리팹처럼 고칠 수 없는 건 알리기만).
        /// 인스펙터에서 값을 바꾸면 OnValidate가 고치고 콘솔에 경고, BuildingDataValidationTests가 모든 건물 에셋을 검사한다
        /// </summary>
        public System.Collections.Generic.List<string> Validate(bool fix)
        {
            var problems = new System.Collections.Generic.List<string>();
            void NonNegative(ref int value, string field)
            {
                if (value >= 0) return;
                problems.Add($"{field}가 음수({value})");
                if (fix) value = 0;
            }

            if (string.IsNullOrWhiteSpace(buildingID)) problems.Add("buildingID가 비어 있음 (저장·불러오기·시설 판별에 필요)");
            if (string.IsNullOrWhiteSpace(buildingName)) problems.Add("buildingName이 비어 있음");
            NonNegative(ref requiredPopulation, nameof(requiredPopulation));
            NonNegative(ref constructionWeeks, nameof(constructionWeeks));
            NonNegative(ref demolitionWeeks, nameof(demolitionWeeks));
            NonNegative(ref requiredPower, nameof(requiredPower));
            NonNegative(ref maintenanceCost, nameof(maintenanceCost));
            NonNegative(ref constructionCost, nameof(constructionCost));
            NonNegative(ref housingCapacity, nameof(housingCapacity));
            NonNegative(ref satisfactionRadius, nameof(satisfactionRadius));
            NonNegative(ref powerSupplyRadius, nameof(powerSupplyRadius));
            NonNegative(ref policePatrolCount, nameof(policePatrolCount));
            if (fixedAccessTimeMinutes < 0f)
            {
                problems.Add($"fixedAccessTimeMinutes가 음수({fixedAccessTimeMinutes})");
                if (fix) fixedAccessTimeMinutes = 0f;
            }

            if (isPowerPole && powerSupplyRadius <= 0) problems.Add("전신주인데 powerSupplyRadius가 0 (아무 건물에도 전기가 들어가지 않음)");
            if (isPowerPole && category != BuildingType.Infrastructure) problems.Add("전신주는 분류가 Infrastructure여야 함 (도로 위 설치·인프라 버튼)");
            if (category == BuildingType.Housing && housingCapacity <= 0) problems.Add("주택인데 housingCapacity가 0 (인구가 늘지 않음)");
            if (category != BuildingType.Housing && housingCapacity > 0) problems.Add("주택이 아닌데 housingCapacity가 있음 (수용 인구는 주택만 셈)");
            if (Mathf.Abs(satisfactionImpact) > 0.01f && satisfactionRadius <= 0) problems.Add("만족도 영향이 있는데 satisfactionRadius가 0 (자기 칸에만 영향)");
            return problems;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            var problems = Validate(fix: true);
            if (problems.Count > 0)
                Debug.LogWarning($"[BuildingData] '{name}' 값 확인 필요: {string.Join(" / ", problems)}", this);
        }
#endif
    }
}
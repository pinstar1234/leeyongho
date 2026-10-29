using UnityEngine;

namespace InfraTown.Building
{
    public enum BuildingSizeType
    {
        Small,  // 소형 (1x1)
        Medium, // 중형 (2x2)
        Large   // 대형 (3x3)
    }

    public enum BuildingType
    {
        Essential, // 필수 시설 (소방서, 병원 등)
        Facility,  // 편의 시설 (마을회관, 편의점 등)
        Nimbys,    // 혐오/불쾌 시설 (축사, 쓰레기 매립지 등)
        Housing    // 주택 (아파트, 단독주택 등)
    }

    [CreateAssetMenu(fileName = "NewBuildingData", menuName = "InfraTown/Building Data")]
    public class BuildingData : ScriptableObject
    {
        [Header("기본 정보 및 해금 조건")]
        public string buildingID;
        public string buildingName;

        [Tooltip("이 건물이 해금되기 위해 필요한 최소 '현재 인구수'")]
        public int requiredPopulation = 0; // 예: 0명(기본), 50명(티어1), 150명(티어2)

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

        [Header("그래픽 및 프리팹")]
        public GameObject buildingPrefab;
        public GameObject previewPrefab;
        public Sprite buildingIcon;

        public Vector2Int GridSize => sizeType switch
        {
            BuildingSizeType.Small => new Vector2Int(1, 1),
            BuildingSizeType.Medium => new Vector2Int(2, 2),
            BuildingSizeType.Large => new Vector2Int(3, 3),
            _ => new Vector2Int(1, 1)
        };
    }
}
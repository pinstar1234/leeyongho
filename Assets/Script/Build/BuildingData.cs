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
        Essential, // 필수 시설 (주택, 소방서, 병원 등)
        Facility,  // 편의 시설 (마을회관, 편의점 등)
        Nimbys,    // 혐오/불쾌 시설 (축사, 쓰레기 매립지 등)
        Housing    // 주택 (만족도를 개별 소유하는 주요 거주 건물)
    }

    [CreateAssetMenu(fileName = "NewBuildingData", menuName = "InfraTown/Building Data")]
    public class BuildingData : ScriptableObject
    {
        [Header("기본 정보")]
        public string buildingID;
        public string buildingName;

        [Header("5가지 핵심 규격 데이터")]
        public BuildingSizeType sizeType;   // 소형, 중형, 대형
        public BuildingType category;       // 필수, 편의, 혐오, 주택(Housing)
        public int requiredPower;           // 필요 전기량
        public int maintenanceCost;         // 유지비
        public int constructionCost;        // 건설 비용

        [Header("만족도 및 영향 범위 (DB 설정)")]
        [Tooltip("주위 주택 만족도에 주는 영향 수치 (+: 상승, -: 하락)")]
        public float satisfactionImpact = 0f;

        [Tooltip("영향을 미치는 타일 범위 반지름 (예: 3 = 주변 3타일 내 주택에 영향)")]
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
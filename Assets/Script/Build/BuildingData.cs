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
        Nimbys     // 혐오/불쾌 시설 (축사, 쓰레기 매립지 등)
    }

    [CreateAssetMenu(fileName = "NewBuildingData", menuName = "InfraTown/Building Data")]
    public class BuildingData : ScriptableObject
    {
        [Header("기본 정보")]
        public string buildingID;
        public string buildingName;

        [Header("5가지 핵심 규격 데이터")]
        public BuildingSizeType sizeType;   // 1. 소형, 중형, 대형
        public BuildingType category;       // 2. 필수, 편의, 혐오
        public int requiredPower;           // 3. 필요 전기량 (kW)
        public int maintenanceCost;         // 4. 유지비 ($/월)
        public int constructionCost;        // 5. 건설 비용 ($)

        [Header("그래픽 및 프리팹")]
        public GameObject buildingPrefab;   // 실제 설치될 건물 프리팹
        public GameObject previewPrefab;    // 반투명 배치 전 프리팹 (선택)
        public Sprite buildingIcon;         // UI용 아이콘

        /// <summary>
        /// 규격 크기에 따른 그리드 점유 칸 수 (소형:1, 중형:2, 대형:3)
        /// </summary>
        public Vector2Int GridSize => sizeType switch
        {
            BuildingSizeType.Small => new Vector2Int(1, 1),
            BuildingSizeType.Medium => new Vector2Int(2, 2),
            BuildingSizeType.Large => new Vector2Int(3, 3),
            _ => new Vector2Int(1, 1)
        };
    }
}
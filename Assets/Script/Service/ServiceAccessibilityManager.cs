using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Building;

namespace InfraTown.Services
{
    public class ServiceAccessibilityManager : MonoBehaviour
    {
        public static ServiceAccessibilityManager Instance { get; private set; }

        [field: Header("시설 설치 여부")]
        public bool HasPoliceStation { get; private set; } = false;

        [field: SerializeField]
        public bool HasHospital { get; private set; } = false;

        [field: SerializeField]
        public bool HasFireStation { get; private set; } = false;

        [field: SerializeField]
        public bool HasConvenienceStore { get; private set; } = false;

        [field: Header("기획서 고정 서비스 표기 값")]
        public int DailyPolicePatrolCount { get; private set; } = 0;
        public float HospitalAccessTime { get; private set; } = 0f;
        public float FireStationAccessTime { get; private set; } = 0f;
        public float ConvenienceAccessTime { get; private set; } = 0f;

        // 기획서 기준 미설치 시 기본 표기 수치 (예: 미설치 시 99.9분 또는 0회)
        private const float DefaultUninstalledTime = 99.9f;

        public event Action OnServiceMetricsUpdated;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>
        /// 건물이 건설/파괴될 때 마다 맵 상 시설 설치 여부를 체크하여 기획서 값 할당
        /// </summary>
        public void RecalculateServiceMetrics(List<BuildingInstance> allBuildings)
        {
            HasPoliceStation = false;
            HasHospital = false;
            HasFireStation = false;
            HasConvenienceStore = false;

            BuildingData policeData = null;
            BuildingData hospitalData = null;
            BuildingData fireData = null;
            BuildingData convenienceData = null;

            // 맵에 존재하는 건물 타입 검사
            foreach (var building in allBuildings)
            {
                if (building == null || building.Data == null) continue;

                string id = building.Data.buildingID.ToLower();
                string name = building.Data.buildingName;

                if (id.Contains("police") || name.Contains("경찰"))
                {
                    HasPoliceStation = true;
                    policeData = building.Data;
                }
                else if (id.Contains("hospital") || name.Contains("병원"))
                {
                    HasHospital = true;
                    hospitalData = building.Data;
                }
                else if (id.Contains("fire") || name.Contains("소방"))
                {
                    HasFireStation = true;
                    fireData = building.Data;
                }
                else if (building.Data.category == BuildingType.Facility)
                {
                    HasConvenienceStore = true;
                    convenienceData = building.Data;
                }
            }

            // 기획서 지정 수치 반영 (존재하면 DB의 지정값, 없으면 미설치용 수치)
            DailyPolicePatrolCount = HasPoliceStation && policeData != null ? policeData.policePatrolCount : 0;
            HospitalAccessTime = HasHospital && hospitalData != null ? hospitalData.fixedAccessTimeMinutes : DefaultUninstalledTime;
            FireStationAccessTime = HasFireStation && fireData != null ? fireData.fixedAccessTimeMinutes : DefaultUninstalledTime;
            ConvenienceAccessTime = HasConvenienceStore && convenienceData != null ? convenienceData.fixedAccessTimeMinutes : DefaultUninstalledTime;

            OnServiceMetricsUpdated?.Invoke();

            Debug.Log($"[시설 서비스 상태 갱신] 경찰: {(HasPoliceStation ? $"{DailyPolicePatrolCount}회" : "미설치")} | 병원: {(HasHospital ? $"{HospitalAccessTime}분" : "미설치")} | 소방서: {(HasFireStation ? $"{FireStationAccessTime}분" : "미설치")} | 편의시설: {(HasConvenienceStore ? $"{ConvenienceAccessTime}분" : "미설치")}");
        }

        /// <summary>
        /// 시설 미설치 여부에 따른 불만 사항 요약 문구 반환
        /// </summary>
        public string GetServiceComplaintSummary()
        {
            List<string> complaints = new List<string>();

            if (!HasPoliceStation)
                complaints.Add("• 경찰서가 없어 마을 치안 순찰이 이루어지지 않고 있습니다.");

            if (!HasHospital)
                complaints.Add("• 병원이 없어 응급 환자 발생 시 대처가 불가능합니다.");

            if (!HasFireStation)
                complaints.Add("• 소방서가 없어 화재 위험에 노출되어 있습니다.");

            if (!HasConvenienceStore)
                complaints.Add("• 편의 시설이 부족하여 주민 생활이 불편합니다.");

            if (complaints.Count == 0)
                return "• 주요 필수 및 편의 시설이 모두 갖추어져 주민 만족도가 높습니다.";

            return string.Join("\n", complaints);
        }
    }
}
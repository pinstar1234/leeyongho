using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Building;

namespace InfraTown.Services
{
    public enum ServiceType
    {
        Police,
        Hospital,
        FireStation,
        Convenience,
        School
    }

    /// <summary>
    /// 주택에서 각 서비스 시설까지의 접근 시간을 거리 기반으로 계산.
    /// 접근 시간(분) = 시설 데이터의 기본 접근 시간 + 가장 가까운 시설까지의 타일 거리 × 타일당 이동 시간
    /// </summary>
    public class ServiceAccessibilityManager : MonoBehaviour
    {
        public static ServiceAccessibilityManager Instance { get; private set; }

        [Header("접근 시간 계산")]
        [Tooltip("1타일(2m) 이동에 걸리는 게임 내 시간(분)")]
        [SerializeField] private float minutesPerTile = 0.5f;

        [Tooltip("이 시간(분)을 넘는 주택이 있으면 불만으로 표시")]
        [SerializeField] private float longAccessThresholdMinutes = 15f;

        [Header("접근 시간 단축 만족도 보너스 (기획서 6장)")]
        [Tooltip("시설이 없을 때 읍내까지 가는 기준 시간(분). 단축 비율 = (기준 - 현재) / 기준")]
        [SerializeField] private float townBaselineMinutes = 60f;

        [Tooltip("서비스 1종을 기준 시간에서 0분으로 줄였을 때 받는 최대 보너스 (단축 비율 × 이 값)")]
        [SerializeField] private float maxBonusPerService = 6f;

        [field: Header("시설 설치 여부")]
        public bool HasPoliceStation { get; private set; } = false;

        [field: SerializeField]
        public bool HasHospital { get; private set; } = false;

        [field: SerializeField]
        public bool HasFireStation { get; private set; } = false;

        [field: SerializeField]
        public bool HasConvenienceStore { get; private set; } = false;

        [field: Header("마을 평균 서비스 수치")]
        public int DailyPolicePatrolCount { get; private set; } = 0;
        public float HospitalAccessTime { get; private set; } = 0f;
        public float FireStationAccessTime { get; private set; } = 0f;
        public float ConvenienceAccessTime { get; private set; } = 0f;
        public float PoliceAccessTime { get; private set; } = 0f;
        public float SchoolAccessTime { get; private set; } = 0f;

        public bool HasSchool => _facilities[ServiceType.School].Count > 0;

        public float MinutesPerTile => minutesPerTile;
        public float LongAccessThresholdMinutes => longAccessThresholdMinutes;
        public float TownBaselineMinutes => townBaselineMinutes;
        public float MaxBonusPerService => maxBonusPerService;

        // 미설치 시 표기 수치
        public const float DefaultUninstalledTime = 99.9f;

        private static readonly ServiceType[] AllServiceTypes =
            { ServiceType.Police, ServiceType.Hospital, ServiceType.FireStation, ServiceType.Convenience, ServiceType.School };

        private readonly Dictionary<ServiceType, List<BuildingInstance>> _facilities = new Dictionary<ServiceType, List<BuildingInstance>>();
        private readonly Dictionary<BuildingInstance, Dictionary<ServiceType, float>> _housingAccessTimes = new Dictionary<BuildingInstance, Dictionary<ServiceType, float>>();
        private readonly Dictionary<ServiceType, float> _averageTimes = new Dictionary<ServiceType, float>();
        private readonly Dictionary<ServiceType, int> _longAccessHousingCounts = new Dictionary<ServiceType, int>();

        public event Action OnServiceMetricsUpdated;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            foreach (var type in AllServiceTypes)
            {
                _facilities[type] = new List<BuildingInstance>();
                _averageTimes[type] = DefaultUninstalledTime;
                _longAccessHousingCounts[type] = 0;
            }
        }

        /// <summary>
        /// 건물 이름/ID/카테고리로 서비스 종류 판별. 서비스 시설이 아니면 null
        /// </summary>
        public static ServiceType? GetServiceType(BuildingData data)
        {
            if (data == null) return null;

            string id = data.buildingID != null ? data.buildingID.ToLower() : string.Empty;
            string name = data.buildingName ?? string.Empty;

            if (id.Contains("police") || name.Contains("경찰")) return ServiceType.Police;
            if (id.Contains("hospital") || name.Contains("병원")) return ServiceType.Hospital;
            if (id.Contains("fire") || name.Contains("소방")) return ServiceType.FireStation;
            if (id.Contains("school") || name.Contains("학교")) return ServiceType.School;
            if (id.Contains("convenience") || id.Contains("store") || name.Contains("편의점")) return ServiceType.Convenience;
            return null;
        }

        /// <summary>
        /// 건물이 건설/파괴될 때마다 주택별 접근 시간과 마을 평균을 다시 계산
        /// </summary>
        public void RecalculateServiceMetrics(List<BuildingInstance> allBuildings)
        {
            foreach (var type in AllServiceTypes) _facilities[type].Clear();
            _housingAccessTimes.Clear();

            var housings = new List<BuildingInstance>();

            foreach (var building in allBuildings)
            {
                if (building == null || building.Data == null) continue;

                if (building.Data.category == BuildingType.Housing)
                {
                    housings.Add(building);
                    continue;
                }

                var type = GetServiceType(building.Data);
                if (type.HasValue) _facilities[type.Value].Add(building);
            }

            // 주택별 가장 빠른 시설까지의 시간
            foreach (var housing in housings)
            {
                var times = new Dictionary<ServiceType, float>();
                foreach (var type in AllServiceTypes)
                {
                    times[type] = CalculateAccessTime(housing, _facilities[type]);
                }
                _housingAccessTimes[housing] = times;
            }

            // 마을 평균 및 너무 먼 주택 수
            foreach (var type in AllServiceTypes)
            {
                var facilities = _facilities[type];
                int longCount = 0;
                float average;

                if (facilities.Count == 0)
                {
                    average = DefaultUninstalledTime;
                }
                else if (housings.Count == 0)
                {
                    // 주택이 없으면 거리 없이 가장 짧은 기본 접근 시간 표시
                    average = float.MaxValue;
                    foreach (var f in facilities) average = Mathf.Min(average, f.Data.fixedAccessTimeMinutes);
                }
                else
                {
                    float sum = 0f;
                    foreach (var housing in housings)
                    {
                        float t = _housingAccessTimes[housing][type];
                        sum += t;
                        if (t > longAccessThresholdMinutes) longCount++;
                    }
                    average = sum / housings.Count;
                }

                _averageTimes[type] = average;
                _longAccessHousingCounts[type] = longCount;
            }

            HasPoliceStation = _facilities[ServiceType.Police].Count > 0;
            HasHospital = _facilities[ServiceType.Hospital].Count > 0;
            HasFireStation = _facilities[ServiceType.FireStation].Count > 0;
            HasConvenienceStore = _facilities[ServiceType.Convenience].Count > 0;

            DailyPolicePatrolCount = 0;
            foreach (var police in _facilities[ServiceType.Police])
            {
                DailyPolicePatrolCount = Mathf.Max(DailyPolicePatrolCount, police.Data.policePatrolCount);
            }

            PoliceAccessTime = _averageTimes[ServiceType.Police];
            HospitalAccessTime = _averageTimes[ServiceType.Hospital];
            FireStationAccessTime = _averageTimes[ServiceType.FireStation];
            ConvenienceAccessTime = _averageTimes[ServiceType.Convenience];
            SchoolAccessTime = _averageTimes[ServiceType.School];

            OnServiceMetricsUpdated?.Invoke();

            Debug.Log($"[시설 서비스 상태 갱신] 주택 {housings.Count}채 평균 | 경찰: {FormatTime(PoliceAccessTime)} (순찰 {DailyPolicePatrolCount}회) | 병원: {FormatTime(HospitalAccessTime)} | 소방서: {FormatTime(FireStationAccessTime)} | 학교: {FormatTime(SchoolAccessTime)} | 편의시설: {FormatTime(ConvenienceAccessTime)}");
        }

        /// <summary>
        /// 해당 서비스가 마을에 하나 이상 설치되어 있는지
        /// </summary>
        public bool HasService(ServiceType type) => _facilities[type].Count > 0;

        /// <summary>
        /// 주택 1채가 서비스 1종에서 받는 접근 시간 단축 보너스.
        /// 단축 비율 = (읍내 기준 시간 - 현재 접근 시간) / 기준 시간 (0~1), 보너스 = 비율 × maxBonusPerService.
        /// 시설이 없거나 기준 시간보다 멀면 0.
        /// </summary>
        public float GetAccessBonus(BuildingInstance housing, ServiceType type)
        {
            if (townBaselineMinutes <= 0f || !HasService(type)) return 0f;

            float time = GetAccessTime(housing, type);
            float ratio = Mathf.Clamp01((townBaselineMinutes - time) / townBaselineMinutes);
            return ratio * maxBonusPerService;
        }

        /// <summary>
        /// 주택 1채가 모든 서비스에서 받는 접근 시간 단축 보너스 합계 (만족도에 더해짐)
        /// </summary>
        public float GetTotalAccessBonus(BuildingInstance housing)
        {
            float total = 0f;
            foreach (var type in AllServiceTypes)
            {
                total += GetAccessBonus(housing, type);
            }
            return total;
        }

        /// <summary>
        /// 특정 주택에서 해당 서비스까지의 접근 시간(분). 시설이 없거나 등록되지 않은 주택이면 99.9
        /// </summary>
        public float GetAccessTime(BuildingInstance housing, ServiceType type)
        {
            if (housing != null && _housingAccessTimes.TryGetValue(housing, out var times))
            {
                return times[type];
            }
            return DefaultUninstalledTime;
        }

        /// <summary>마을 주택 평균 접근 시간(분). 미설치면 99.9</summary>
        public float GetAverageAccessTime(ServiceType type) => _averageTimes[type];

        /// <summary>접근 시간이 기준(longAccessThresholdMinutes)을 넘는 주택 수</summary>
        public int GetLongAccessHousingCount(ServiceType type) => _longAccessHousingCounts[type];

        /// <summary>해당 서비스 시설 목록 (읽기 전용)</summary>
        public IReadOnlyList<BuildingInstance> GetFacilities(ServiceType type) => _facilities[type];

        /// <summary>
        /// 시설 미설치 및 먼 거리에 따른 불만 사항 요약 문구 반환
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

            if (!HasSchool)
                complaints.Add("• 학교가 없어 아이들 교육 때문에 젊은 가족이 마을을 떠나려 합니다.");

            if (!HasConvenienceStore)
                complaints.Add("• 편의 시설이 부족하여 주민 생활이 불편합니다.");

            AddDistanceComplaint(complaints, ServiceType.Police, "경찰서");
            AddDistanceComplaint(complaints, ServiceType.Hospital, "병원");
            AddDistanceComplaint(complaints, ServiceType.FireStation, "소방서");
            AddDistanceComplaint(complaints, ServiceType.School, "학교");
            AddDistanceComplaint(complaints, ServiceType.Convenience, "편의 시설");

            if (complaints.Count == 0)
                return "• 주요 필수 및 편의 시설이 가까이 갖추어져 주민 만족도가 높습니다.";

            return string.Join("\n", complaints);
        }

        private void AddDistanceComplaint(List<string> complaints, ServiceType type, string label)
        {
            int count = _longAccessHousingCounts[type];
            if (_facilities[type].Count > 0 && count > 0)
            {
                complaints.Add($"• 주택 {count}채가 {label}까지 {longAccessThresholdMinutes:F0}분 넘게 걸립니다. (평균 {_averageTimes[type]:F1}분)");
            }
        }

        private float CalculateAccessTime(BuildingInstance housing, List<BuildingInstance> facilities)
        {
            if (facilities.Count == 0) return DefaultUninstalledTime;

            float best = float.MaxValue;
            foreach (var facility in facilities)
            {
                int distance = GetFootprintDistance(housing, facility);
                float time = facility.Data.fixedAccessTimeMinutes + distance * minutesPerTile;
                best = Mathf.Min(best, time);
            }
            return best;
        }

        /// <summary>
        /// 두 건물 점유 영역 사이의 타일 거리 (맨해튼, 맞닿아 있으면 0)
        /// </summary>
        public static int GetFootprintDistance(BuildingInstance a, BuildingInstance b)
        {
            Vector2Int aMin = a.OriginGridPos, aMax = aMin + a.Data.GridSize - Vector2Int.one;
            Vector2Int bMin = b.OriginGridPos, bMax = bMin + b.Data.GridSize - Vector2Int.one;

            int dx = Mathf.Max(0, Mathf.Max(bMin.x - aMax.x, aMin.x - bMax.x) - 1);
            int dy = Mathf.Max(0, Mathf.Max(bMin.y - aMax.y, aMin.y - bMax.y) - 1);
            return dx + dy;
        }

        private static string FormatTime(float minutes) =>
            minutes >= DefaultUninstalledTime ? "미설치" : $"{minutes:F1}분";
    }
}

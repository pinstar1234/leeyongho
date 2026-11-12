using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Population;
using InfraTown.Satisfaction;
using InfraTown.Services;
using InfraTown.TimeSystem;

namespace InfraTown.Core
{
    public enum GameResult
    {
        None,    // 진행 중
        Victory, // 해피엔딩
        Defeat   // 배드엔딩
    }

    /// <summary>
    /// 기획서 6장 엔딩 판정.
    /// 승리: 10년 경과 + 필수 시설 설치 + '좋음' 주택 70% 이상을 30일 이상 유지 + 인구 기준 이상.
    ///      10년 시점에 미달이면 계속 플레이하고, 조건을 채우는 날 승리.
    /// 패배: 마을 평균 만족도 '나쁨'이 30일 연속 (공사 중 유예 요청이 있으면 그동안 카운트 정지).
    /// 씬에 없으면 GameLoopManager가 같은 오브젝트에 자동으로 추가한다.
    /// </summary>
    public class GameOutcomeManager : MonoBehaviour
    {
        public static GameOutcomeManager Instance { get; private set; }

        public const int DaysPerYear = 360; // 30일 × 12달

        [Header("승리 조건")]
        [Tooltip("최소 경과 연수")]
        [SerializeField] private int victoryYears = 10;

        [Tooltip("'좋음' 주택 비율 기준 (0~1)")]
        [Range(0f, 1f)]
        [SerializeField] private float requiredGoodRatio = 0.7f;

        [Tooltip("'좋음' 비율 기준을 연속으로 유지해야 하는 일수")]
        [SerializeField] private int requiredGoodStreakDays = 30;

        [Tooltip("필요 인구 수 (기획 확정 전 0)")]
        [SerializeField] private int requiredPopulation = 0;

        [Tooltip("인프라 정상화에 필요한 필수 시설")]
        [SerializeField] private List<ServiceType> requiredServices = new List<ServiceType>
        {
            ServiceType.Hospital, ServiceType.School, ServiceType.FireStation, ServiceType.Police
        };

        [Header("패배 조건")]
        [Tooltip("마을 만족도 '나쁨'이 이 일수만큼 연속되면 패배 (일반 모드 30일)")]
        [SerializeField] private int defeatBadStreakDays = 30;

        public int DaysElapsed { get; private set; }
        public int GoodStreakDays { get; private set; }
        public int BadStreakDays { get; private set; }
        public GameResult Result { get; private set; } = GameResult.None;
        public bool IsGameOver => Result != GameResult.None;

        public int VictoryDays => victoryYears * DaysPerYear;
        public int RequiredGoodStreakDays => requiredGoodStreakDays;
        public float RequiredGoodRatio => requiredGoodRatio;
        public int RequiredPopulation => requiredPopulation;
        public int DefeatBadStreakDays => defeatBadStreakDays;
        public IReadOnlyList<ServiceType> RequiredServices => requiredServices;

        /// <summary>패배 타이머 유예 중인지 (공사 중 '개선 기대감' 등)</summary>
        public bool IsDefeatGraceActive => _graceSources.Count > 0;

        public event Action<GameResult> OnGameEnded;

        /// <summary>(연속 '나쁨' 일수, 패배 기준 일수) — 경고 연출용</summary>
        public event Action<int, int> OnBadStreakChanged;

        /// <summary>하루 판정이 끝날 때마다 발생 — 진행 상황 UI용</summary>
        public event Action OnDayEvaluated;

        private readonly HashSet<object> _graceSources = new HashSet<object>();
        private (int year, int month, int day) _lastDate = (-1, -1, -1);
        private bool _subscribed;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start()
        {
            if (TimeManager.Instance != null)
            {
                _lastDate = (TimeManager.Instance.CurrentYear, TimeManager.Instance.CurrentMonth, TimeManager.Instance.CurrentDay);
                TimeManager.Instance.OnDateChanged += HandleDateChanged;
                _subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && TimeManager.Instance != null)
            {
                TimeManager.Instance.OnDateChanged -= HandleDateChanged;
            }
        }

        private void HandleDateChanged(int year, int month, int day)
        {
            // TimeManager.Start의 첫 알림처럼 날짜가 그대로면 하루로 세지 않음
            if (_lastDate == (year, month, day)) return;
            _lastDate = (year, month, day);
            ProcessDay();
        }

        /// <summary>
        /// 하루 경과 판정 (TimeManager가 하루를 넘길 때 호출됨)
        /// </summary>
        public void ProcessDay()
        {
            if (IsGameOver) return;

            DaysElapsed++;

            var satisfaction = SatisfactionManager.Instance;

            // 패배: 마을 평균 '나쁨' 연속 일수 (유예 중엔 증가도 초기화도 하지 않음)
            int previousBad = BadStreakDays;
            bool isBad = satisfaction != null && satisfaction.CurrentState == SatisfactionState.Bad;
            if (!isBad) BadStreakDays = 0;
            else if (!IsDefeatGraceActive) BadStreakDays++;

            if (BadStreakDays != previousBad)
            {
                OnBadStreakChanged?.Invoke(BadStreakDays, defeatBadStreakDays);
            }

            // 승리: '좋음' 비율 연속 유지 일수
            bool goodEnough = satisfaction != null && satisfaction.HousingCount > 0
                              && satisfaction.GoodHousingRatio >= requiredGoodRatio;
            GoodStreakDays = goodEnough ? GoodStreakDays + 1 : 0;

            OnDayEvaluated?.Invoke();

            if (BadStreakDays >= defeatBadStreakDays)
            {
                EndGame(GameResult.Defeat);
            }
            else if (IsVictoryConditionMet())
            {
                EndGame(GameResult.Victory);
            }
        }

        /// <summary>
        /// 현재 모든 승리 조건을 만족하는지
        /// </summary>
        public bool IsVictoryConditionMet()
        {
            return DaysElapsed >= VictoryDays
                   && GoodStreakDays >= requiredGoodStreakDays
                   && IsInfrastructureNormalized()
                   && CurrentPopulation >= requiredPopulation;
        }

        /// <summary>
        /// 인프라 정상화: 필수 시설이 모두 1개 이상 설치됨
        /// </summary>
        public bool IsInfrastructureNormalized() => GetMissingRequiredServices().Count == 0;

        /// <summary>
        /// 아직 설치되지 않은 필수 시설 목록
        /// </summary>
        public List<ServiceType> GetMissingRequiredServices()
        {
            var missing = new List<ServiceType>();
            var service = ServiceAccessibilityManager.Instance;
            foreach (var type in requiredServices)
            {
                if (service == null || !service.HasService(type)) missing.Add(type);
            }
            return missing;
        }

        /// <summary>
        /// 패배 타이머 유예 요청 (예: 필수 대형/중형 시설 공사 시작 시)
        /// </summary>
        public void RequestDefeatGrace(object source) => _graceSources.Add(source);

        /// <summary>
        /// 패배 타이머 유예 해제 (예: 공사 완료 시)
        /// </summary>
        public void ReleaseDefeatGrace(object source) => _graceSources.Remove(source);

        private static int CurrentPopulation =>
            PopulationManager.Instance != null ? PopulationManager.Instance.CurrentPopulation : 0;

        private void EndGame(GameResult result)
        {
            Result = result;
            TimeManager.Instance?.RequestEventPause(this);
            Debug.Log($"[GameOutcomeManager] 게임 종료: {(result == GameResult.Victory ? "해피엔딩" : "배드엔딩")} ({DaysElapsed}일째)");
            OnGameEnded?.Invoke(result);
        }
    }
}

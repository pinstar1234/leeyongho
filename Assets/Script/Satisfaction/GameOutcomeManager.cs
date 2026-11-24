using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Building;
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
    /// 승리: 10년(480주) 경과 + 필수 시설 설치 + '좋음' 주택 70% 이상을 4주(1달) 이상 유지 + 인구 기준 이상.
    ///      10년 시점에 미달이면 계속 플레이하고, 조건을 채우는 주에 승리.
    /// 패배: 마을 평균 만족도 '나쁨'이 12주(3달) 연속 (사용자 결정 2026-11-24, 예전 30일) (공사 중 유예 요청이 있으면 그동안 카운트 정지).
    /// 씬에 없으면 GameLoopManager가 같은 오브젝트에 자동으로 추가한다.
    /// </summary>
    public class GameOutcomeManager : MonoBehaviour
    {
        public static GameOutcomeManager Instance { get; private set; }

        public const int WeeksPerYear = TimeManager.WeeksPerYear; // 4주 × 12달 = 48주 (2026-11-24 주 단위로 변경)

        [Header("승리 조건")]
        [Tooltip("최소 경과 연수")]
        [SerializeField] private int victoryYears = 10;

        [Tooltip("'좋음' 주택 비율 기준 (0~1)")]
        [Range(0f, 1f)]
        [SerializeField] private float requiredGoodRatio = 0.7f;

        [Tooltip("'좋음' 비율 기준을 연속으로 유지해야 하는 주 수 (4주 = 1달)")]
        [SerializeField] private int requiredGoodStreakWeeks = TimeManager.WeeksPerMonth;

        [Tooltip("필요 인구 수 (기획 확정 전 0)")]
        [SerializeField] private int requiredPopulation = 0;

        [Tooltip("인프라 정상화에 필요한 필수 시설")]
        [SerializeField] private List<ServiceType> requiredServices = new List<ServiceType>
        {
            ServiceType.Hospital, ServiceType.School, ServiceType.FireStation, ServiceType.Police
        };

        [Header("패배 조건")]
        [Tooltip("마을 만족도 '나쁨'이 이 주 수만큼 연속되면 패배 (일반 모드 12주 = 3달)")]
        [SerializeField] private int defeatBadStreakWeeks = 3 * TimeManager.WeeksPerMonth;

        public int WeeksElapsed { get; private set; }
        public int GoodStreakWeeks { get; private set; }
        public int BadStreakWeeks { get; private set; }
        public GameResult Result { get; private set; } = GameResult.None;

        /// <summary>
        /// 게임 오버인지 — 배드엔딩만 게임 오버 (사용자 기획 2026-11-24). 게임 오버면 건설·철거가 막히고 시간이 멈춘다.
        /// 타이틀로 돌아가는 화면은 UI(팀원 1)가 OnGameEnded(Defeat)를 받아 띄우고 ReturnToTitle()을 부른다
        /// </summary>
        public bool IsGameOver => Result == GameResult.Defeat;

        /// <summary>엔딩(승리·패배)이 한 번이라도 났는지</summary>
        public bool HasEnded => Result != GameResult.None;

        /// <summary>
        /// 해피엔딩이 나서 플레이어의 선택을 기다리는 중 (시간 정지). UI는 '끝내기'(ReturnToTitle) / '계속하기'(ContinueAfterVictory)를 보여준다
        /// </summary>
        public bool IsVictoryChoicePending => Result == GameResult.Victory && !IsFreePlay;

        /// <summary>해피엔딩 뒤 '계속하기'를 골라 이어서 하는 중 — 배드엔딩 조건이 사라지고 엔딩 판정을 더 하지 않는다</summary>
        public bool IsFreePlay { get; private set; }

        /// <summary>건설·철거를 할 수 있는지 (게임 오버가 아니면 true). UI가 건설·철거 버튼을 끄는 데 쓴다</summary>
        public bool CanModifyTown => !IsGameOver;

        /// <summary>인스턴스가 없으면(엔딩 판정이 없는 씬) true</summary>
        public static bool CanModifyTownNow => Instance == null || Instance.CanModifyTown;

        public int VictoryWeeks => victoryYears * WeeksPerYear;
        public int RequiredGoodStreakWeeks => requiredGoodStreakWeeks;
        public float RequiredGoodRatio => requiredGoodRatio;
        public int RequiredPopulation => requiredPopulation;
        public int DefeatBadStreakWeeks => defeatBadStreakWeeks;
        public IReadOnlyList<ServiceType> RequiredServices => requiredServices;

        /// <summary>패배 타이머 유예 중인지 (공사 중 '개선 기대감' 등)</summary>
        public bool IsDefeatGraceActive => _graceSources.Count > 0;

        /// <summary>엔딩이 났을 때 (Defeat = 게임 오버 화면, Victory = 끝내기/계속하기 선택 화면)</summary>
        public event Action<GameResult> OnGameEnded;

        /// <summary>해피엔딩 뒤 '계속하기'를 골라 게임이 다시 흘러가기 시작함</summary>
        public event Action OnFreePlayStarted;

        /// <summary>(연속 '나쁨' 주 수, 패배 기준 주 수) — 경고 연출용</summary>
        public event Action<int, int> OnBadStreakChanged;

        /// <summary>한 주 판정이 끝날 때마다 발생 — 진행 상황 UI용</summary>
        public event Action OnWeekEvaluated;

        private readonly HashSet<object> _graceSources = new HashSet<object>();
        private (int year, int month, int day) _lastDate = (-1, -1, -1);
        private bool _subscribed;

        private void Awake()
        {
            if (Instance != null && Instance != this) { InfraTown.Core.SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;
        }

        private void Start()
        {
            if (TimeManager.Instance != null)
            {
                _lastDate = (TimeManager.Instance.CurrentYear, TimeManager.Instance.CurrentMonth, TimeManager.Instance.CurrentWeek);
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
            // TimeManager.Start의 첫 알림처럼 날짜가 그대로면 한 주로 세지 않음
            if (_lastDate == (year, month, day)) return;
            _lastDate = (year, month, day);
            ProcessWeek();
        }

        /// <summary>
        /// 한 주 경과 판정 (TimeManager가 한 주를 넘길 때 호출됨)
        /// </summary>
        public void ProcessWeek()
        {
            if (IsGameOver || IsVictoryChoicePending) return;

            WeeksElapsed++;

            // 해피엔딩 뒤 계속하기: 배드엔딩 조건이 사라지고 엔딩 판정도 더 하지 않는다 (경과 주 수만 셈)
            if (IsFreePlay)
            {
                OnWeekEvaluated?.Invoke();
                return;
            }

            // 유예를 요청한 공사 등이 해제 없이 사라졌으면 유예도 끝낸다 (패배 판정이 영원히 멈추지 않도록)
            InfraTown.Core.LockSourceUtil.RemoveDestroyed(_graceSources);

            var satisfaction = SatisfactionManager.Instance;

            // 패배: 마을 평균 '나쁨' 연속 주 수 (유예 중엔 증가도 초기화도 하지 않음)
            int previousBad = BadStreakWeeks;
            bool isBad = satisfaction != null && satisfaction.CurrentState == SatisfactionState.Bad;
            if (!isBad) BadStreakWeeks = 0;
            else if (!IsDefeatGraceActive) BadStreakWeeks++;

            if (BadStreakWeeks != previousBad)
            {
                OnBadStreakChanged?.Invoke(BadStreakWeeks, defeatBadStreakWeeks);
            }

            // 승리: '좋음' 비율 연속 유지 주 수
            bool goodEnough = satisfaction != null && satisfaction.HousingCount > 0
                              && satisfaction.GoodHousingRatio >= requiredGoodRatio;
            GoodStreakWeeks = goodEnough ? GoodStreakWeeks + 1 : 0;

            OnWeekEvaluated?.Invoke();

            if (BadStreakWeeks >= defeatBadStreakWeeks)
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
            return WeeksElapsed >= VictoryWeeks
                   && GoodStreakWeeks >= requiredGoodStreakWeeks
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
        public void RequestDefeatGrace(object source)
        {
            if (source != null) _graceSources.Add(source); // null은 해제할 방법이 없으므로 받지 않음
        }

        /// <summary>
        /// 패배 타이머 유예 해제 (예: 공사 완료 시)
        /// </summary>
        public void ReleaseDefeatGrace(object source)
        {
            if (source != null) _graceSources.Remove(source);
        }

        // ─── 세이브 ───

        public InfraTown.Save.OutcomeSaveState GetSaveState() => new InfraTown.Save.OutcomeSaveState
        {
            weeksElapsed = WeeksElapsed, goodStreakWeeks = GoodStreakWeeks, badStreakWeeks = BadStreakWeeks, result = (int)Result,
            freePlay = IsFreePlay,
        };

        /// <summary>경과 주 수·연속 주 수·엔딩 결과를 되돌림. 이번 주는 이미 센 것으로 둔다 (불러온 직후 한 주로 세지 않음)</summary>
        public void ApplySaveState(InfraTown.Save.OutcomeSaveState s)
        {
            if (s == null) return;
            WeeksElapsed = Mathf.Max(0, s.weeksElapsed);
            GoodStreakWeeks = Mathf.Max(0, s.goodStreakWeeks);
            BadStreakWeeks = Mathf.Max(0, s.badStreakWeeks);
            Result = Enum.IsDefined(typeof(GameResult), s.result) ? (GameResult)s.result : GameResult.None;
            IsFreePlay = s.freePlay && Result == GameResult.Victory;
            if (IsFreePlay) BadStreakWeeks = 0;
            if (TimeManager.Instance != null)
                _lastDate = (TimeManager.Instance.CurrentYear, TimeManager.Instance.CurrentMonth, TimeManager.Instance.CurrentWeek);
            // 게임 오버거나 해피엔딩 선택 전이면 계속 멈춤, 그 밖(진행 중·계속하기)은 멈춤 해제
            if (IsGameOver || IsVictoryChoicePending) TimeManager.Instance?.RequestEventPause(this);
            else TimeManager.Instance?.ReleaseEventPause(this);
            if (IsGameOver) BuildingManager.Instance?.CancelBuildingMode();
            OnBadStreakChanged?.Invoke(BadStreakWeeks, defeatBadStreakWeeks);
            OnWeekEvaluated?.Invoke();
        }

        private static int CurrentPopulation =>
            PopulationManager.Instance != null ? PopulationManager.Instance.CurrentPopulation : 0;

        /// <summary>
        /// 해피엔딩 선택 '계속하기': 시간 정지를 풀고 배드엔딩 조건 없이 원하는 만큼 이어서 한다 (사용자 기획 2026-11-24).
        /// 해피엔딩 선택을 기다리는 중이 아니면 아무것도 하지 않고 false
        /// </summary>
        public bool ContinueAfterVictory()
        {
            if (!IsVictoryChoicePending) return false;
            IsFreePlay = true;
            if (BadStreakWeeks != 0)
            {
                BadStreakWeeks = 0;
                OnBadStreakChanged?.Invoke(0, defeatBadStreakWeeks);
            }
            TimeManager.Instance?.ReleaseEventPause(this);
            Debug.Log($"[GameOutcomeManager] 해피엔딩 뒤 계속하기 ({WeeksElapsed}주째) — 배드엔딩 조건 없음");
            OnFreePlayStarted?.Invoke();
            return true;
        }

        /// <summary>
        /// 타이틀로 돌아가기 (배드엔딩 게임 오버 화면, 해피엔딩 '끝내기'). 저장하지 않는다.
        /// 타이틀 씬이 Build Settings에 없으면 false
        /// </summary>
        public bool ReturnToTitle(string titleScene = InfraTown.Save.SaveSystem.DefaultTitleScene)
        {
            if (string.IsNullOrEmpty(titleScene) || !Application.CanStreamedLevelBeLoaded(titleScene))
            {
                Debug.LogWarning($"[GameOutcomeManager] 타이틀 씬 '{titleScene}'이 Build Settings에 없어 돌아갈 수 없습니다.");
                return false;
            }
            InfraTown.Save.SaveSystem.ClearPendingLoad();
            Time.timeScale = 1f; // 엔딩으로 멈춘 상태가 타이틀까지 이어지지 않도록
            UnityEngine.SceneManagement.SceneManager.LoadScene(titleScene);
            return true;
        }

        private void EndGame(GameResult result)
        {
            Result = result;
            IsFreePlay = false;
            TimeManager.Instance?.RequestEventPause(this);
            // 게임 오버면 건설 모드도 끝냄 (미리보기가 남아 클릭으로 설치되지 않도록)
            if (result == GameResult.Defeat) BuildingManager.Instance?.CancelBuildingMode();
            Debug.Log($"[GameOutcomeManager] {(result == GameResult.Victory ? "해피엔딩 — 끝내기/계속하기 선택 대기" : "배드엔딩 — 게임 오버")} ({WeeksElapsed}주째)");
            OnGameEnded?.Invoke(result);
        }
    }
}

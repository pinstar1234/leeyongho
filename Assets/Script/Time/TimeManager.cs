using System;
using System.Collections.Generic;
using UnityEngine;

namespace InfraTown.TimeSystem
{
    public enum GameSpeed
    {
        Pause = 0,
        Normal = 1,   // 1배속
        Fast = 2,     // 2배속
        SuperFast = 3 // 3배속
    }

    /// <summary>
    /// 게임 시간 (사용자 기획 2026-11-24: 일 단위 → 주 단위).
    /// 한 칸 = 1주 (secondsPerWeek, 기본 180초). 1달 = 4주, 1년 = 12달 = 48주. 계절은 달로 정해진다(SeasonUtil).
    /// 매주 OnWeeklyTick(인구 등), 4주마다 OnMonthlyTick(정산), 계절이 바뀌면 OnSeasonChanged,
    /// 그리고 매주 OnDateChanged(년, 월, 주차).
    /// </summary>
    public class TimeManager : MonoBehaviour
    {
        public static TimeManager Instance { get; private set; }

        public const int WeeksPerMonth = 4;
        public const int MonthsPerYear = 12;
        public const int WeeksPerYear = WeeksPerMonth * MonthsPerYear; // 48

        [Header("Time Settings")]
        [Tooltip("게임 내 1주에 해당하는 현실 시간(초) (사용자 결정 2026-11-24: 180초)")]
        [SerializeField] private float secondsPerWeek = 180f;

        [Header("한 달 스킵 (사용자 기획 2026-11-19, 주 단위 변경 후 2026-11-24 한 달로)")]
        [Tooltip("스킵 버튼 한 번에 지나가는 주 수 (기본 4주 = 한 달)")]
        [SerializeField] private int skipWeeks = WeeksPerMonth;

        [Tooltip("스킵 중 1주가 지나가는 현실 시간(초) — 날짜와 인구·전력·가스·돈·만족도가 한 주씩 빠르게 바뀌는 모습을 보여줌. 0이면 한 번에")]
        [SerializeField] private float skipSecondsPerWeek = 0.3f;

        [Header("Current Date Info")]
        [SerializeField] private int currentYear = 1;
        [SerializeField] private int currentMonth = 1;
        [Tooltip("이번 달 몇 주차 (1~4)")]
        [SerializeField] private int currentWeek = 1;
        [SerializeField] private GameSpeed currentSpeed = GameSpeed.Normal;

        public int CurrentYear => currentYear;
        public int CurrentMonth => currentMonth;

        /// <summary>이번 달 몇 주차 (1~4)</summary>
        public int CurrentWeek => currentWeek;

        /// <summary>이전 이름 (일 단위 시절). 이제 주차(1~4)를 돌려준다 — 새 코드는 CurrentWeek를 쓸 것</summary>
        public int CurrentDay => currentWeek;

        public GameSpeed CurrentSpeed => currentSpeed;
        public float SecondsPerWeek => secondsPerWeek;

        /// <summary>지금 계절</summary>
        public Season CurrentSeason => SeasonUtil.FromMonth(currentMonth);

        /// <summary>다음 달의 계절 (다음 달 구매량을 정할 때 참고)</summary>
        public Season NextMonthSeason => SeasonUtil.FromMonth(currentMonth % MonthsPerYear + 1);

        /// <summary>게임 시작부터 지난 주 수</summary>
        public int TotalWeeksPassed => _totalWeeksPassed;

        /// <summary>이번 주가 얼마나 지났는지 0~1 (진행 바 등)</summary>
        public float WeekProgress => secondsPerWeek > 0f ? Mathf.Clamp01(_weekTimer / secondsPerWeek) : 0f;

        // 시간 진행 상태
        private float _weekTimer = 0f;
        private int _totalWeeksPassed = 0;

        // 이벤트로 인한 일시정지 잠금 스택 (여러 대화/팝업이 겹쳐도 안전하게 관리)
        private HashSet<object> _pauseLockSources = new HashSet<object>();
        public bool IsEventPaused => _pauseLockSources.Count > 0;

        /// <summary>날짜 변경 (년, 월, 주차 1~4) — 매주 발생</summary>
        public event Action<int, int, int> OnDateChanged;
        public event Action<GameSpeed> OnSpeedChanged;

        /// <summary>계절이 바뀜 (새 계절) — 해당 달의 OnMonthlyTick보다 먼저 발생</summary>
        public event Action<Season> OnSeasonChanged;

        // 주 단위 / 월 단위(4주) 정산 틱 이벤트
        public event Action OnWeeklyTick;
        public event Action OnMonthlyTick;

        // ─── 한 달 스킵 ───
        // UI: 스킵 버튼 → StartMonthSkip(), 버튼 활성 여부 → CanSkip, 진행 표시 → IsSkipping / SkipProgress / SkipWeeksRemaining
        // 날짜·인구·전력·가스·돈·만족도는 스킵 중에도 평소처럼 한 주씩 이벤트로 바뀌므로 기존 UI가 그대로 빠르게 갱신된다.
        // 대화·컷씬·엔딩 등으로 일시정지 잠금이 걸리면 그 자리에서 스킵이 멈춘다 (OnSkipEnded(true)).

        public int SkipWeeks { get => skipWeeks; set => skipWeeks = Mathf.Max(1, value); }
        public float SkipSecondsPerWeek { get => skipSecondsPerWeek; set => skipSecondsPerWeek = Mathf.Max(0f, value); }

        /// <summary>스킵 진행 중</summary>
        public bool IsSkipping { get; private set; }

        /// <summary>이번 스킵에서 남은 주 수</summary>
        public int SkipWeeksRemaining { get; private set; }

        /// <summary>이번 스킵에서 지나간 주 수</summary>
        public int SkipWeeksDone => IsSkipping ? _skipTotalWeeks - SkipWeeksRemaining : 0;

        /// <summary>스킵 진행률 0~1 (스킵 중이 아니면 0)</summary>
        public float SkipProgress => IsSkipping && _skipTotalWeeks > 0 ? (float)SkipWeeksDone / _skipTotalWeeks : 0f;

        /// <summary>지금 스킵을 시작할 수 있는지 (대화·컷씬·엔딩 등으로 멈춰 있거나 이미 스킵 중이면 false)</summary>
        public bool CanSkip => !IsSkipping && !IsEventPaused;

        /// <summary>스킵 시작 (지나갈 주 수)</summary>
        public event Action<int> OnSkipStarted;

        /// <summary>스킵 한 주 진행 (지나간 주 수, 전체 주 수)</summary>
        public event Action<int, int> OnSkipProgress;

        /// <summary>스킵 끝 (true = 이벤트 등으로 도중에 멈춤)</summary>
        public event Action<bool> OnSkipEnded;

        private int _skipTotalWeeks;
        private float _skipTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this) { InfraTown.Core.SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;
        }

        private void Start()
        {
            ApplyTimeScale();
            OnDateChanged?.Invoke(currentYear, currentMonth, currentWeek);
        }

        private void Update()
        {
            // 일시정지를 건 UI·대화 오브젝트가 해제 없이 삭제됐으면 그 잠금을 풀어 준다 (영구 정지 방지)
            if (_pauseLockSources.Count > 0 && InfraTown.Core.LockSourceUtil.RemoveDestroyed(_pauseLockSources) > 0)
            {
                ApplyTimeScale();
            }

            if (IsSkipping)
            {
                // 스킵은 배속·timeScale과 상관없이 실제 시간으로 진행
                TickSkip(Time.unscaledDeltaTime);
                return;
            }

            if (IsEventPaused || currentSpeed == GameSpeed.Pause) return;

            _weekTimer += Time.deltaTime;

            if (_weekTimer >= secondsPerWeek)
            {
                _weekTimer -= secondsPerWeek;
                AdvanceOneWeek();
            }
        }

        /// <summary>
        /// 게임 내 시간을 1주 진행시킴: 주간 틱 → (달이 바뀌면) 계절 변경 → 월간 틱 → 날짜 알림
        /// </summary>
        private void AdvanceOneWeek()
        {
            currentWeek++;
            _totalWeeksPassed++;

            // 매주 주간 틱 (인구 갱신 등)
            OnWeeklyTick?.Invoke();

            // 4주마다 다음 달 + 월간 틱 (재정 정산 등)
            if (currentWeek > WeeksPerMonth)
            {
                Season before = CurrentSeason;

                currentWeek = 1;
                currentMonth++;

                if (currentMonth > MonthsPerYear)
                {
                    currentMonth = 1;
                    currentYear++;
                }

                // 계절이 먼저 바뀌어야 월간 정산·소비량이 새 계절 기준이 된다
                if (CurrentSeason != before) OnSeasonChanged?.Invoke(CurrentSeason);

                OnMonthlyTick?.Invoke();
            }

            OnDateChanged?.Invoke(currentYear, currentMonth, currentWeek);
        }

        // ─── 세이브 ───

        public InfraTown.Save.TimeSaveState GetSaveState() => new InfraTown.Save.TimeSaveState
        {
            year = currentYear, month = currentMonth, week = currentWeek,
            totalWeeksPassed = _totalWeeksPassed, weekTimer = _weekTimer, speed = (int)currentSpeed,
        };

        /// <summary>저장된 날짜·배속으로 되돌림 (알림은 NotifyStateRestored에서 한 번에)</summary>
        public void ApplySaveState(InfraTown.Save.TimeSaveState s)
        {
            if (s == null) return;
            if (IsSkipping) CancelSkip();
            currentYear = Mathf.Max(1, s.year);
            currentMonth = Mathf.Clamp(s.month, 1, MonthsPerYear);
            currentWeek = Mathf.Clamp(s.week, 1, WeeksPerMonth);
            _totalWeeksPassed = Mathf.Max(0, s.totalWeeksPassed);
            _weekTimer = Mathf.Max(0f, s.weekTimer);
            currentSpeed = (GameSpeed)Mathf.Clamp(s.speed, 0, 3);
            ApplyTimeScale();
        }

        /// <summary>불러오기가 끝난 뒤 날짜·계절·배속 UI가 갱신되도록 알림</summary>
        public void NotifyStateRestored()
        {
            ApplyTimeScale();
            OnSeasonChanged?.Invoke(CurrentSeason);
            OnDateChanged?.Invoke(currentYear, currentMonth, currentWeek);
            OnSpeedChanged?.Invoke(currentSpeed);
        }

        /// <summary>
        /// 한 달 스킵 시작 (skipWeeks주, 기본 4주). 일시정지(배속 0) 중에도 가능. 시작하지 못하면 false
        /// </summary>
        public bool StartMonthSkip() => StartSkip(skipWeeks);

        /// <summary>이전 이름 (일주일 스킵 버튼에 연결된 경우용). StartMonthSkip과 같다</summary>
        public bool StartWeekSkip() => StartMonthSkip();

        /// <summary>
        /// weeks주 스킵 시작. 한 주씩 skipSecondsPerWeek 간격으로 빠르게 진행하며, 일시정지 잠금이 걸리면 멈춘다
        /// </summary>
        public bool StartSkip(int weeks)
        {
            if (weeks <= 0 || !CanSkip) return false;

            IsSkipping = true;
            _skipTotalWeeks = weeks;
            SkipWeeksRemaining = weeks;
            _skipTimer = 0f;
            OnSkipStarted?.Invoke(weeks);

            if (skipSecondsPerWeek <= 0f) TickSkip(0f); // 한 번에
            return true;
        }

        /// <summary>스킵을 지금 멈춤 (남은 주는 지나가지 않음)</summary>
        public void CancelSkip() => EndSkip(true);

        /// <summary>
        /// 스킵 시간 경과 처리 (Update에서 실제 시간으로 호출, 테스트에서 직접 호출 가능)
        /// </summary>
        public void TickSkip(float deltaSeconds)
        {
            if (!IsSkipping) return;
            if (IsEventPaused) { EndSkip(true); return; }

            _skipTimer += Mathf.Max(0f, deltaSeconds);
            while (IsSkipping && SkipWeeksRemaining > 0 && (skipSecondsPerWeek <= 0f || _skipTimer >= skipSecondsPerWeek))
            {
                if (skipSecondsPerWeek > 0f) _skipTimer -= skipSecondsPerWeek;

                SkipWeeksRemaining--;
                AdvanceOneWeek();
                if (!IsSkipping) return; // 이 주에 일어난 이벤트로 이미 멈춤
                OnSkipProgress?.Invoke(SkipWeeksDone, _skipTotalWeeks);

                // 그 주에 대화·컷씬·엔딩이 일시정지를 걸었으면 그 시점에서 멈춤
                if (IsEventPaused) { EndSkip(true); return; }
            }

            if (IsSkipping && SkipWeeksRemaining <= 0) EndSkip(false);
        }

        private void EndSkip(bool interrupted)
        {
            if (!IsSkipping) return;
            IsSkipping = false;
            SkipWeeksRemaining = 0;
            _skipTimer = 0f;
            OnSkipEnded?.Invoke(interrupted);
        }

        /// <summary>
        /// 배속 설정 (Pause, Normal=1, Fast=2, SuperFast=3)
        /// </summary>
        public void SetGameSpeed(GameSpeed speed)
        {
            currentSpeed = speed;
            ApplyTimeScale();
            OnSpeedChanged?.Invoke(currentSpeed);
        }

        private void ApplyTimeScale()
        {
            if (IsEventPaused)
            {
                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = (float)currentSpeed;
            }
        }

        /// <summary>
        /// 이벤트(대화창, 팝업 경고) 발생 시 시간을 일시정지 요청
        /// </summary>
        public void RequestEventPause(object source)
        {
            if (source == null) return; // null은 해제할 방법이 없으므로 받지 않음
            if (_pauseLockSources.Add(source))
            {
                ApplyTimeScale();
                // 스킵 도중 대화·컷씬·엔딩이 시작되면 그 시점에서 바로 멈춤
                if (IsSkipping) EndSkip(true);
            }
        }

        /// <summary>
        /// 이벤트 종료 시 일시정지 해제
        /// </summary>
        public void ReleaseEventPause(object source)
        {
            if (source == null) return;
            if (_pauseLockSources.Remove(source))
            {
                ApplyTimeScale();
            }
        }

        /// <summary>현재 걸려 있는 일시정지 잠금 수</summary>
        public int PauseLockCount => _pauseLockSources.Count;

        /// <summary>모든 일시정지 잠금 해제 (씬 전환·디버그용)</summary>
        public void ClearAllEventPauses()
        {
            _pauseLockSources.Clear();
            ApplyTimeScale();
        }
    }
}

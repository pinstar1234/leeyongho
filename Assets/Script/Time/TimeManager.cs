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

    public class TimeManager : MonoBehaviour
    {
        public static TimeManager Instance { get; private set; }

        [Header("Time Settings")]
        [Tooltip("게임 내 하루에 해당하는 현실 시간(초)")]
        [SerializeField] private float secondsPerDay = 90f; // 1분 30초 = 90초

        [Header("일주일 스킵 (사용자 기획 2026-11-19)")]
        [Tooltip("스킵 버튼 한 번에 지나가는 일수")]
        [SerializeField] private int skipDays = 7;

        [Tooltip("스킵 중 하루가 지나가는 현실 시간(초) — 날짜와 인구·전력·돈·만족도가 하루씩 빠르게 바뀌는 모습을 보여줌. 0이면 한 번에")]
        [SerializeField] private float skipSecondsPerDay = 0.3f;

        [Header("Current Date Info")]
        [SerializeField] private int currentYear = 1;
        [SerializeField] private int currentMonth = 1;
        [SerializeField] private int currentDay = 1;
        [SerializeField] private GameSpeed currentSpeed = GameSpeed.Normal;

        public int CurrentYear => currentYear;
        public int CurrentMonth => currentMonth;
        public int CurrentDay => currentDay;
        public GameSpeed CurrentSpeed => currentSpeed;

        // 시간 진행 상태
        private float _dayTimer = 0f;
        private int _totalDaysPassed = 0;

        // 이벤트로 인한 일시정지 잠금 스택 (여러 대화/팝업이 겹쳐도 안전하게 관리)
        private HashSet<object> _pauseLockSources = new HashSet<object>();
        public bool IsEventPaused => _pauseLockSources.Count > 0;

        // 시간 변경 이벤트 알림 (년, 월, 일, 배속)
        public event Action<int, int, int> OnDateChanged;
        public event Action<GameSpeed> OnSpeedChanged;

        // 주 단위(7일) / 월 단위(30일) 정산 틱 이벤트
        public event Action OnWeeklyTick;
        public event Action OnMonthlyTick;

        // ─── 일주일 스킵 ───
        // UI: 스킵 버튼 → StartWeekSkip(), 버튼 활성 여부 → CanSkip, 진행 표시 → IsSkipping / SkipProgress / SkipDaysRemaining
        // 날짜·인구·전력·돈·만족도는 스킵 중에도 평소처럼 하루씩 이벤트로 바뀌므로 기존 UI가 그대로 빠르게 갱신된다.
        // 대화·컷씬·엔딩 등으로 일시정지 잠금이 걸리면 그 자리에서 스킵이 멈춘다 (OnSkipEnded(true)).

        public int SkipDays { get => skipDays; set => skipDays = Mathf.Max(1, value); }
        public float SkipSecondsPerDay { get => skipSecondsPerDay; set => skipSecondsPerDay = Mathf.Max(0f, value); }

        /// <summary>스킵 진행 중</summary>
        public bool IsSkipping { get; private set; }

        /// <summary>이번 스킵에서 남은 일수</summary>
        public int SkipDaysRemaining { get; private set; }

        /// <summary>이번 스킵에서 지나간 일수</summary>
        public int SkipDaysDone => IsSkipping ? _skipTotalDays - SkipDaysRemaining : 0;

        /// <summary>스킵 진행률 0~1 (스킵 중이 아니면 0)</summary>
        public float SkipProgress => IsSkipping && _skipTotalDays > 0 ? (float)SkipDaysDone / _skipTotalDays : 0f;

        /// <summary>지금 스킵을 시작할 수 있는지 (대화·컷씬·엔딩 등으로 멈춰 있거나 이미 스킵 중이면 false)</summary>
        public bool CanSkip => !IsSkipping && !IsEventPaused;

        /// <summary>스킵 시작 (지나갈 일수)</summary>
        public event Action<int> OnSkipStarted;

        /// <summary>스킵 하루 진행 (지나간 일수, 전체 일수)</summary>
        public event Action<int, int> OnSkipProgress;

        /// <summary>스킵 끝 (true = 이벤트 등으로 도중에 멈춤)</summary>
        public event Action<bool> OnSkipEnded;

        private int _skipTotalDays;
        private float _skipTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this) { InfraTown.Core.SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;
        }

        private void Start()
        {
            ApplyTimeScale();
            OnDateChanged?.Invoke(currentYear, currentMonth, currentDay);
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

            _dayTimer += Time.deltaTime;

            if (_dayTimer >= secondsPerDay)
            {
                _dayTimer -= secondsPerDay;
                AdvanceOneDay();
            }
        }

        /// <summary>
        /// 게임 내 시간을 하루 진행시킴
        /// </summary>
        private void AdvanceOneDay()
        {
            currentDay++;
            _totalDaysPassed++;

            // 1주일(7일) 마다 주간 틱 발생 (인구 갱신 등)
            if (_totalDaysPassed % 7 == 0)
            {
                OnWeeklyTick?.Invoke();
            }

            // 1달(30일) 마다 월간 틱 발생 (재정 정산 등)
            if (currentDay > 30)
            {
                currentDay = 1;
                currentMonth++;

                if (currentMonth > 12)
                {
                    currentMonth = 1;
                    currentYear++;
                }

                OnMonthlyTick?.Invoke();
            }

            OnDateChanged?.Invoke(currentYear, currentMonth, currentDay);
        }

        // ─── 세이브 ───

        public InfraTown.Save.TimeSaveState GetSaveState() => new InfraTown.Save.TimeSaveState
        {
            year = currentYear, month = currentMonth, day = currentDay,
            totalDaysPassed = _totalDaysPassed, dayTimer = _dayTimer, speed = (int)currentSpeed,
        };

        /// <summary>저장된 날짜·배속으로 되돌림 (알림은 NotifyStateRestored에서 한 번에)</summary>
        public void ApplySaveState(InfraTown.Save.TimeSaveState s)
        {
            if (s == null) return;
            if (IsSkipping) CancelSkip();
            currentYear = Mathf.Max(1, s.year);
            currentMonth = Mathf.Clamp(s.month, 1, 12);
            currentDay = Mathf.Clamp(s.day, 1, 30);
            _totalDaysPassed = Mathf.Max(0, s.totalDaysPassed);
            _dayTimer = Mathf.Max(0f, s.dayTimer);
            currentSpeed = (GameSpeed)Mathf.Clamp(s.speed, 0, 3);
            ApplyTimeScale();
        }

        /// <summary>불러오기가 끝난 뒤 날짜·배속 UI가 갱신되도록 알림</summary>
        public void NotifyStateRestored()
        {
            ApplyTimeScale();
            OnDateChanged?.Invoke(currentYear, currentMonth, currentDay);
            OnSpeedChanged?.Invoke(currentSpeed);
        }

        /// <summary>
        /// 일주일 스킵 시작 (skipDays일). 일시정지(배속 0) 중에도 가능. 시작하지 못하면 false
        /// </summary>
        public bool StartWeekSkip() => StartSkip(skipDays);

        /// <summary>
        /// days일 스킵 시작. 하루씩 skipSecondsPerDay 간격으로 빠르게 진행하며, 일시정지 잠금이 걸리면 멈춘다
        /// </summary>
        public bool StartSkip(int days)
        {
            if (days <= 0 || !CanSkip) return false;

            IsSkipping = true;
            _skipTotalDays = days;
            SkipDaysRemaining = days;
            _skipTimer = 0f;
            OnSkipStarted?.Invoke(days);

            if (skipSecondsPerDay <= 0f) TickSkip(0f); // 한 번에
            return true;
        }

        /// <summary>스킵을 지금 멈춤 (남은 날은 지나가지 않음)</summary>
        public void CancelSkip() => EndSkip(true);

        /// <summary>
        /// 스킵 시간 경과 처리 (Update에서 실제 시간으로 호출, 테스트에서 직접 호출 가능)
        /// </summary>
        public void TickSkip(float deltaSeconds)
        {
            if (!IsSkipping) return;
            if (IsEventPaused) { EndSkip(true); return; }

            _skipTimer += Mathf.Max(0f, deltaSeconds);
            while (IsSkipping && SkipDaysRemaining > 0 && (skipSecondsPerDay <= 0f || _skipTimer >= skipSecondsPerDay))
            {
                if (skipSecondsPerDay > 0f) _skipTimer -= skipSecondsPerDay;

                SkipDaysRemaining--;
                AdvanceOneDay();
                if (!IsSkipping) return; // 이 날에 일어난 이벤트로 이미 멈춤
                OnSkipProgress?.Invoke(SkipDaysDone, _skipTotalDays);

                // 그날 대화·컷씬·엔딩이 일시정지를 걸었으면 그 시점에서 멈춤
                if (IsEventPaused) { EndSkip(true); return; }
            }

            if (IsSkipping && SkipDaysRemaining <= 0) EndSkip(false);
        }

        private void EndSkip(bool interrupted)
        {
            if (!IsSkipping) return;
            IsSkipping = false;
            SkipDaysRemaining = 0;
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
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

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            ApplyTimeScale();
            OnDateChanged?.Invoke(currentYear, currentMonth, currentDay);
        }

        private void Update()
        {
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
            if (_pauseLockSources.Add(source))
            {
                ApplyTimeScale();
            }
        }

        /// <summary>
        /// 이벤트 종료 시 일시정지 해제
        /// </summary>
        public void ReleaseEventPause(object source)
        {
            if (_pauseLockSources.Remove(source))
            {
                ApplyTimeScale();
            }
        }
    }
}
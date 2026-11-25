using System;
using UnityEngine;
using InfraTown.Core;
using InfraTown.TimeSystem;

namespace InfraTown.Save
{
    /// <summary>
    /// 자동 저장 (사용자 기획 2026-11-25): 게임이 예상치 못하게 꺼질 때를 대비해 1주가 지날 때마다
    /// 자동 저장 전용 슬롯(SaveSystem.AutoSaveSlot, 1~5번과 따로)에 덮어쓴다. 새로 시작한 게임도 같은 슬롯을 덮어쓰고,
    /// 플레이어가 고른 1~5번 슬롯은 건드리지 않는다.
    ///
    /// - 그 주의 다른 처리(엔딩 판정 등)가 끝난 뒤 같은 프레임 LateUpdate에서 저장한다.
    /// - 한 달 스킵 도중에는 주마다 저장하지 않고 스킵이 끝났을 때 한 번 저장한다 (저장하면 스킵이 멈추므로).
    /// - 배드엔딩(게임 오버) 상태는 저장하지 않는다 (이어하기로 끝난 판이 열리지 않도록 직전 주 저장을 남김).
    /// - 불러오기 직후 날짜 알림은 한 주로 세지 않는다.
    /// 씬에 없으면 GameLoopManager.Start가 자기 오브젝트에 추가한다.
    /// </summary>
    public class AutoSaveManager : MonoBehaviour
    {
        public static AutoSaveManager Instance { get; private set; }

        [Tooltip("1주마다 자동 저장 (끄면 직접 저장할 때만)")]
        [SerializeField] private bool autoSaveEnabled = true;

        public bool AutoSaveEnabled { get => autoSaveEnabled; set => autoSaveEnabled = value; }

        /// <summary>이번 게임에서 자동 저장한 횟수</summary>
        public int AutoSaveCount { get; private set; }

        /// <summary>마지막 자동 저장 시도가 실패했는지 (이유는 SaveSystem.LastError)</summary>
        public bool LastAutoSaveFailed { get; private set; }

        /// <summary>저장할 주가 지나 이번 프레임 끝(또는 스킵이 끝난 뒤)에 저장을 기다리는 중</summary>
        public bool IsPending => _pending;

        /// <summary>자동 저장 성공 — '자동 저장됨' 표시 등</summary>
        public event Action OnAutoSaved;

        /// <summary>자동 저장 실패 (이유)</summary>
        public event Action<string> OnAutoSaveFailed;

        private TimeManager _time;
        private bool _pending;
        private (int year, int month, int week) _lastDate = (-1, -1, -1);

        private void Awake()
        {
            if (Instance != null && Instance != this) { SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;
        }

        private void Start()
        {
            _time = TimeManager.Instance;
            if (_time != null)
            {
                _lastDate = (_time.CurrentYear, _time.CurrentMonth, _time.CurrentWeek);
                _time.OnDateChanged += HandleDateChanged;
            }
        }

        private void OnDestroy()
        {
            if (_time != null) _time.OnDateChanged -= HandleDateChanged;
            if (Instance == this) Instance = null;
        }

        private void HandleDateChanged(int year, int month, int week)
        {
            // TimeManager.Start의 첫 알림·불러오기 직후 알림처럼 날짜가 그대로면 한 주로 세지 않음
            if (_lastDate == (year, month, week)) return;
            _lastDate = (year, month, week);
            if (SaveSystem.IsApplying) return; // 불러온 날짜로 맞추는 알림
            _pending = true;
        }

        private void LateUpdate()
        {
            if (!_pending) return;
            if (_time != null && _time.IsSkipping) return; // 스킵이 끝나면 한 번
            SaveNow();
        }

        /// <summary>기다리던 자동 저장을 지금 실행 (테스트·UI용). 저장했으면 true</summary>
        public bool SaveNow()
        {
            _pending = false;
            if (!autoSaveEnabled) return false;
            if (SaveSystem.IsApplying || SaveSystem.HasPendingLoad) return false;
            if (GameOutcomeManager.Instance != null && GameOutcomeManager.Instance.IsGameOver)
            {
                Debug.Log("[AutoSave] 게임 오버 상태라 자동 저장하지 않습니다.");
                return false;
            }

            bool ok = SaveSystem.AutoSave();
            LastAutoSaveFailed = !ok;
            if (ok)
            {
                AutoSaveCount++;
                OnAutoSaved?.Invoke();
            }
            else
            {
                Debug.LogWarning($"[AutoSave] 자동 저장 실패: {SaveSystem.LastError}");
                OnAutoSaveFailed?.Invoke(SaveSystem.LastError);
            }
            return ok;
        }
    }
}

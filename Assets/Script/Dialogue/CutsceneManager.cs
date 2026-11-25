using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Core;
using InfraTown.TimeSystem;

namespace InfraTown.EventSystem
{
    /// <summary>
    /// 컷씬 재생 매니저 (사용자 기획 2026-11-19). 두 가지 방식:
    ///  - 카툰 컷(Comic): 만화 페이지에 그림 컷이 하나씩 붙는다 (필수 시설 미설치·설치 대화 전 등). 화면은 UI(팀원 1)가 그린다.
    ///  - 시네머신(Cinemachine): 팀원이 씬에 만든 시네머신 연출(CinemachineSequence)을 재생하고 끝날 때까지 기다린다 (오프닝 등).
    /// 이 매니저는 순서·대기열·자동 넘김·시간 정지·카메라 조작 끄기만 맡는다.
    ///
    /// UI 연결 방법:
    ///   OnCutsceneStarted → 컷씬 화면 열기 (IsComic / IsCinemachine으로 구분. 시네머신이면 건너뛰기 버튼만 띄워도 됨)
    ///   OnPageStarted     → (카툰) 페이지 비우기
    ///   OnShotShown       → (카툰) 그림을 shot.pageRect 자리에 붙이기 / 자막 GetCurrentCaption(), 이름 GetCurrentSpeakerName()
    ///                       (지금 페이지의 컷 전체는 CurrentPageShots)
    ///   클릭/버튼          → Next() (카툰 다음 컷), 건너뛰기 버튼 → Skip() (CanSkip일 때)
    ///   OnCutsceneEnded   → 컷씬 화면 닫기
    /// 카툰 컷을 보여줄 UI(구독자)가 없으면 시간을 멈추지 않고 자막만 콘솔에 출력한 뒤 바로 끝낸다.
    /// 시네머신은 연출 자체가 화면이라 UI가 없어도 재생한다. 씬에 해당 ID의 시퀀스가 없으면 경고만 남기고 바로 끝낸다.
    /// </summary>
    public class CutsceneManager : MonoBehaviour
    {
        public static CutsceneManager Instance { get; private set; }

        [Tooltip("카메라 조작을 끌 카메라 (비우면 Main Camera)")]
        [SerializeField] private Camera targetCamera;

        [Tooltip("Main Camera의 RTSCameraController(가장자리 스크롤·줌)를 컷씬 중 끔")]
        [SerializeField] private bool disableCameraControllerDuringCutscene = true;

        [Tooltip("컷씬 중 추가로 끌 컴포넌트 (건설 UI 등)")]
        [SerializeField] private List<Behaviour> disableDuringCutscene = new List<Behaviour>();

        public bool IsPlaying => CurrentCutscene != null;
        public CutsceneData CurrentCutscene { get; private set; }
        public bool IsComic => IsPlaying && CurrentCutscene.style == CutsceneStyle.Comic;
        public bool IsCinemachine => IsPlaying && CurrentCutscene.style == CutsceneStyle.Cinemachine;

        /// <summary>(시네머신) 지금 재생 중인 시퀀스</summary>
        public CinemachineSequence CurrentSequence { get; private set; }

        public int CurrentShotIndex { get; private set; } = -1;
        public CutsceneShot CurrentShot =>
            IsComic && CurrentShotIndex >= 0 && CurrentShotIndex < CurrentCutscene.shots.Count
                ? CurrentCutscene.shots[CurrentShotIndex]
                : null;

        /// <summary>(카툰) 지금 페이지에 붙어 있는 컷들 — 마지막이 방금 나온 컷</summary>
        public IReadOnlyList<CutsceneShot> CurrentPageShots => _pageShots;

        /// <summary>(카툰) 지금 몇 번째 페이지인지 (0부터)</summary>
        public int CurrentPageIndex { get; private set; } = -1;

        public bool CanSkip => IsPlaying && CurrentCutscene.skippable;

        /// <summary>대기 중인 컷씬 수</summary>
        public int QueueCount => _queue.Count;

        public event Action<CutsceneData> OnCutsceneStarted;
        public event Action<int> OnPageStarted;                    // 페이지 번호
        public event Action<CutsceneShot, int, int> OnShotShown;   // (컷, 몇 번째, 전체 컷 수)
        public event Action<CutsceneData> OnCutsceneEnded;

        private struct Request
        {
            public CutsceneData cutscene;
            public Action onFinished;
        }

        private readonly Queue<Request> _queue = new Queue<Request>();
        private readonly HashSet<string> _played = new HashSet<string>();
        private readonly List<CutsceneShot> _pageShots = new List<CutsceneShot>();
        private readonly List<Behaviour> _disabledByMe = new List<Behaviour>();
        private Action _onFinished;
        private bool _paused;
        private float _shotTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this) { SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (CurrentSequence != null) CurrentSequence.OnSequenceEnded -= HandleSequenceEnded;
            if (_paused) TimeManager.Instance?.ReleaseEventPause(this);
            RestoreDisabled();
            if (Instance == this) Instance = null;
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>
        /// 컷씬 재생. 끝나면(건너뛰어도) onFinished 호출. 다른 컷씬이 재생 중이면 대기열에 넣는다.
        /// 내용이 없거나 한 번만 보는 컷씬을 이미 봤으면 false (onFinished도 호출하지 않음).
        /// </summary>
        public bool Play(CutsceneData cutscene, Action onFinished = null)
        {
            if (cutscene == null || !cutscene.HasContent) return false;
            if (cutscene.playOnce && HasPlayed(cutscene.cutsceneId)) return false;

            var request = new Request { cutscene = cutscene, onFinished = onFinished };
            if (IsPlaying) { _queue.Enqueue(request); return true; }

            Begin(request);
            return true;
        }

        /// <summary>(카툰) 다음 컷. 시네머신은 끝날 때까지 기다리므로 무시</summary>
        public void Next()
        {
            if (!IsComic) return;
            ShowShot(CurrentShotIndex + 1);
        }

        /// <summary>컷씬 끝까지 건너뜀 (skippable일 때만). 뒤에 이어지는 대화는 그대로 나온다</summary>
        public void Skip()
        {
            if (!CanSkip) return;
            if (IsCinemachine && CurrentSequence != null && CurrentSequence.IsPlaying)
            {
                CurrentSequence.Skip(); // → HandleSequenceEnded → End
                if (IsPlaying) End();
                return;
            }
            End();
        }

        /// <summary>현재 컷씬과 대기열을 모두 비움 (onFinished는 현재 컷씬 것만 호출)</summary>
        public void ClearAll()
        {
            _queue.Clear();
            if (!IsPlaying) return;
            if (CurrentSequence != null && CurrentSequence.IsPlaying) CurrentSequence.Skip();
            if (IsPlaying) End();
        }

        /// <summary>(카툰) 현재 컷 자막 — 쉬운 모드는 DialogueEventManager.UseEasyText, {player}는 주인공 이름</summary>
        public string GetCurrentCaption() => PlayerProfile.ApplyTokens(CurrentShot?.GetCaption(UseEasyText));

        /// <summary>(카툰) 현재 컷 자막의 화자 이름 (주인공이면 플레이어가 정한 이름, 해설이면 빈 문자열)</summary>
        public string GetCurrentSpeakerName()
        {
            string speaker = CurrentShot?.speaker;
            if (string.IsNullOrEmpty(speaker)) return string.Empty;
            var dialogue = DialogueEventManager.Instance;
            return dialogue != null ? dialogue.GetSpeakerDisplayName(speaker) : PlayerProfile.ApplyTokens(speaker);
        }

        public bool HasPlayed(string cutsceneId) => !string.IsNullOrEmpty(cutsceneId) && _played.Contains(cutsceneId);
        public IReadOnlyCollection<string> PlayedCutsceneIds => _played;
        public void MarkPlayed(string cutsceneId) { if (!string.IsNullOrEmpty(cutsceneId)) _played.Add(cutsceneId); }
        public void ResetPlayed() => _played.Clear();

        /// <summary>
        /// 시간 경과 처리 (Update에서 실제 시간으로 호출 — 게임 시간이 멈춰도 진행). 테스트에서 직접 호출 가능
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (!IsPlaying) return;

            if (IsCinemachine)
            {
                if (CurrentSequence != null) CurrentSequence.Tick(deltaSeconds);
                return;
            }

            var shot = CurrentShot;
            if (shot != null && shot.autoAdvanceSeconds > 0f)
            {
                _shotTimer += deltaSeconds;
                if (_shotTimer >= shot.autoAdvanceSeconds) Next();
            }
        }

        // ─── 내부 ───

        private bool UseEasyText => DialogueEventManager.Instance != null && DialogueEventManager.Instance.UseEasyText;

        private bool HasViewer => OnShotShown != null || OnCutsceneStarted != null;

        private void Begin(Request request)
        {
            var cutscene = request.cutscene;
            CurrentCutscene = cutscene;
            _onFinished = request.onFinished;
            if (cutscene.playOnce) MarkPlayed(cutscene.cutsceneId);

            if (cutscene.style == CutsceneStyle.Cinemachine) BeginCinemachine(cutscene);
            else BeginComic(cutscene);
        }

        private void BeginComic(CutsceneData cutscene)
        {
            if (!HasViewer)
            {
                foreach (var s in cutscene.shots)
                {
                    string caption = PlayerProfile.ApplyTokens(s.GetCaption(UseEasyText));
                    if (!string.IsNullOrEmpty(caption))
                        Debug.Log($"[컷씬] {(string.IsNullOrEmpty(s.speaker) ? "" : PlayerProfile.ApplyTokens(s.speaker) + ": ")}{caption}");
                }
                End();
                return;
            }

            StartPlayback(cutscene);
            ShowShot(0);
        }

        private void BeginCinemachine(CutsceneData cutscene)
        {
            var sequence = CinemachineSequence.Find(cutscene.sequenceId);
            if (sequence == null)
            {
                Debug.LogWarning($"[컷씬] 시네머신 시퀀스 '{cutscene.sequenceId}'를 씬에서 찾지 못해 건너뜀 (CinemachineSequence의 ID 확인)");
                End();
                return;
            }

            StartPlayback(cutscene);
            CurrentSequence = sequence;
            sequence.OnSequenceEnded += HandleSequenceEnded;
            sequence.Play();
        }

        private void StartPlayback(CutsceneData cutscene)
        {
            if (cutscene.pauseGame && TimeManager.Instance != null)
            {
                TimeManager.Instance.RequestEventPause(this);
                _paused = true;
            }
            DisableControls();
            OnCutsceneStarted?.Invoke(cutscene);
        }

        private void HandleSequenceEnded(bool skipped)
        {
            if (IsCinemachine) End();
        }

        private void ShowShot(int index)
        {
            if (!IsComic) return;
            if (index >= CurrentCutscene.shots.Count) { End(); return; }

            CurrentShotIndex = index;
            _shotTimer = 0f;
            var shot = CurrentShot;

            if (index == 0 || shot.newPage)
            {
                _pageShots.Clear();
                CurrentPageIndex++;
                OnPageStarted?.Invoke(CurrentPageIndex);
            }
            _pageShots.Add(shot);

            OnShotShown?.Invoke(shot, index, CurrentCutscene.shots.Count);
        }

        private void End()
        {
            var ended = CurrentCutscene;
            var callback = _onFinished;

            if (CurrentSequence != null)
            {
                CurrentSequence.OnSequenceEnded -= HandleSequenceEnded;
                CurrentSequence = null;
            }

            CurrentCutscene = null;
            CurrentShotIndex = -1;
            CurrentPageIndex = -1;
            _pageShots.Clear();
            _onFinished = null;

            if (_paused) { TimeManager.Instance?.ReleaseEventPause(this); _paused = false; }
            RestoreDisabled();

            OnCutsceneEnded?.Invoke(ended);
            callback?.Invoke(); // 뒤에 붙은 대화 시작 등

            if (!IsPlaying && _queue.Count > 0) Begin(_queue.Dequeue());
        }

        // ─── 컷씬 중 조작 끄기 ───

        private void DisableControls()
        {
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (disableCameraControllerDuringCutscene && cam != null)
            {
                Disable(cam.GetComponent<InfraTown.CameraSystem.RTSCameraController>());
            }
            foreach (var b in disableDuringCutscene) Disable(b);
        }

        private void Disable(Behaviour b)
        {
            if (b == null || !b.enabled) return;
            b.enabled = false;
            _disabledByMe.Add(b);
        }

        private void RestoreDisabled()
        {
            foreach (var b in _disabledByMe) if (b != null) b.enabled = true;
            _disabledByMe.Clear();
        }
    }
}

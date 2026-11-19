using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.TimeSystem;

namespace InfraTown.EventSystem
{
    /// <summary>
    /// 컷씬 재생 매니저 (사용자 기획 2026-11-19).
    ///  - 카툰 컷(Comic): 만화 페이지에 그림 컷이 하나씩 붙는다. 필수 시설 미설치·설치 대화 전에 보여준다.
    ///  - 게임 화면 연출(Scene): 카메라가 CutsceneCameraPoint를 따라 움직이며 자막이 나온다. 오프닝(마을에 도착하기까지)에 쓴다.
    /// 화면 표시는 UI(팀원 1)가 이벤트를 구독해서 하고, 이 매니저는 순서·자동 넘김·카메라 이동·시간 정지만 관리한다.
    ///
    /// UI 연결 방법:
    ///   OnCutsceneStarted → 컷씬 화면 열기 (CurrentCutscene.style로 카툰/게임 화면 구분)
    ///   OnPageStarted     → (카툰) 페이지 비우기
    ///   OnShotShown       → 그림을 shot.pageRect 자리에 붙이기 / 자막 GetCurrentCaption() 표시
    ///                       (지금 페이지의 컷 전체는 CurrentPageShots)
    ///   클릭/버튼          → Next(), 건너뛰기 버튼 → Skip() (CanSkip일 때)
    ///   OnCutsceneEnded   → 컷씬 화면 닫기
    /// 보여줄 UI(구독자)가 없으면 시간을 멈추지 않고 자막만 콘솔에 출력한 뒤 바로 끝낸다
    /// (restoreCamera가 꺼진 컷씬은 카메라만 마지막 장면 위치로 옮겨 둔다).
    /// </summary>
    public class CutsceneManager : MonoBehaviour
    {
        public static CutsceneManager Instance { get; private set; }

        [Tooltip("움직일 카메라 (비우면 Main Camera)")]
        [SerializeField] private Camera targetCamera;

        [Tooltip("Main Camera의 RTSCameraController(가장자리 스크롤·줌)를 컷씬 중 끔")]
        [SerializeField] private bool disableCameraControllerDuringCutscene = true;

        [Tooltip("컷씬 중 추가로 끌 컴포넌트 (건설 UI 등)")]
        [SerializeField] private List<Behaviour> disableDuringCutscene = new List<Behaviour>();

        public bool IsPlaying => CurrentCutscene != null;
        public CutsceneData CurrentCutscene { get; private set; }
        public int CurrentShotIndex { get; private set; } = -1;
        public CutsceneShot CurrentShot =>
            IsPlaying && CurrentShotIndex >= 0 && CurrentShotIndex < CurrentCutscene.shots.Count
                ? CurrentCutscene.shots[CurrentShotIndex]
                : null;

        /// <summary>(카툰) 지금 페이지에 붙어 있는 컷들 — 마지막이 방금 나온 컷</summary>
        public IReadOnlyList<CutsceneShot> CurrentPageShots => _pageShots;

        /// <summary>(카툰) 지금 몇 번째 페이지인지 (0부터)</summary>
        public int CurrentPageIndex { get; private set; } = -1;

        public bool CanSkip => IsPlaying && CurrentCutscene.skippable;

        /// <summary>카메라가 장면 위치로 움직이는 중</summary>
        public bool IsCameraMoving => _cameraMoving;

        /// <summary>대기 중인 컷씬 수</summary>
        public int QueueCount => _queue.Count;

        public event Action<CutsceneData> OnCutsceneStarted;
        public event Action<int> OnPageStarted;                    // 페이지 번호
        public event Action<CutsceneShot, int, int> OnShotShown;   // (장면, 몇 번째, 전체 장면 수)
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

        // 카메라
        private Camera _camera;
        private bool _cameraSaved;
        private Vector3 _savedPos;
        private Quaternion _savedRot;
        private float _savedFov;
        private bool _cameraMoving;
        private Vector3 _fromPos, _toPos;
        private Quaternion _fromRot, _toRot;
        private float _fromFov, _toFov;
        private float _moveTime, _moveDuration;

        private void Awake()
        {
            if (Instance != null && Instance != this) { InfraTown.Core.SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (_paused) TimeManager.Instance?.ReleaseEventPause(this);
            RestoreDisabled();
            if (Instance == this) Instance = null;
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>
        /// 컷씬 재생. 끝나면(건너뛰어도) onFinished 호출. 다른 컷씬이 재생 중이면 대기열에 넣는다.
        /// 장면이 없거나 한 번만 보는 컷씬을 이미 봤으면 false (onFinished도 호출하지 않음).
        /// </summary>
        public bool Play(CutsceneData cutscene, Action onFinished = null)
        {
            if (cutscene == null || cutscene.shots == null || cutscene.shots.Count == 0) return false;
            if (cutscene.playOnce && HasPlayed(cutscene.cutsceneId)) return false;

            var request = new Request { cutscene = cutscene, onFinished = onFinished };
            if (IsPlaying) { _queue.Enqueue(request); return true; }

            Begin(request);
            return true;
        }

        /// <summary>다음 장면 (카메라가 움직이는 중이면 이동을 끝내고 다음으로)</summary>
        public void Next()
        {
            if (!IsPlaying) return;
            FinishCameraMove();
            ShowShot(CurrentShotIndex + 1);
        }

        /// <summary>컷씬 끝까지 건너뜀 (skippable일 때만). 뒤에 이어지는 대화는 그대로 나온다</summary>
        public void Skip()
        {
            if (!CanSkip) return;
            End();
        }

        /// <summary>현재 컷씬과 대기열을 모두 비움 (onFinished는 현재 컷씬 것만 호출)</summary>
        public void ClearAll()
        {
            _queue.Clear();
            if (IsPlaying) End();
        }

        /// <summary>현재 장면 자막 (쉬운 모드는 DialogueEventManager.UseEasyText를 따름)</summary>
        public string GetCurrentCaption() => InfraTown.Core.PlayerProfile.ApplyTokens(CurrentShot?.GetCaption(UseEasyText));

        /// <summary>현재 장면 자막의 화자 이름 (주인공이면 플레이어가 정한 이름, 해설이면 빈 문자열)</summary>
        public string GetCurrentSpeakerName()
        {
            string speaker = CurrentShot?.speaker;
            if (string.IsNullOrEmpty(speaker)) return string.Empty;
            var dialogue = DialogueEventManager.Instance;
            return dialogue != null ? dialogue.GetSpeakerDisplayName(speaker) : InfraTown.Core.PlayerProfile.ApplyTokens(speaker);
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

            if (_cameraMoving)
            {
                _moveTime += deltaSeconds;
                float t = _moveDuration <= 0f ? 1f : Mathf.Clamp01(_moveTime / _moveDuration);
                ApplyCameraLerp(Mathf.SmoothStep(0f, 1f, t));
                if (t >= 1f) _cameraMoving = false;
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

            _camera = targetCamera != null ? targetCamera : Camera.main;
            SaveCamera();

            if (!HasViewer)
            {
                foreach (var s in cutscene.shots)
                {
                    string caption = InfraTown.Core.PlayerProfile.ApplyTokens(s.GetCaption(UseEasyText));
                    if (!string.IsNullOrEmpty(caption))
                        Debug.Log($"[컷씬] {(string.IsNullOrEmpty(s.speaker) ? "" : s.speaker + ": ")}{caption}");
                }
                // 오프닝처럼 마지막 위치에 남는 컷씬이면 카메라는 옮겨 둠
                if (!cutscene.restoreCamera)
                {
                    for (int i = cutscene.shots.Count - 1; i >= 0; i--)
                    {
                        var point = CutsceneCameraPoint.Find(cutscene.shots[i].cameraPointId);
                        if (point == null) continue;
                        StartCameraMove(point, 0f);
                        FinishCameraMove();
                        break;
                    }
                }
                End();
                return;
            }

            if (cutscene.pauseGame && TimeManager.Instance != null)
            {
                TimeManager.Instance.RequestEventPause(this);
                _paused = true;
            }
            DisableControls();

            OnCutsceneStarted?.Invoke(cutscene);
            ShowShot(0);
        }

        private void ShowShot(int index)
        {
            if (!IsPlaying) return;
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

            var point = CutsceneCameraPoint.Find(shot.cameraPointId);
            if (point != null) StartCameraMove(point, shot.cameraMoveSeconds);

            CutsceneSceneEvent.Trigger(shot.sceneEventId);

            OnShotShown?.Invoke(shot, index, CurrentCutscene.shots.Count);
        }

        private void End()
        {
            var ended = CurrentCutscene;
            var callback = _onFinished;

            FinishCameraMove();
            if (ended != null && ended.restoreCamera) RestoreCamera();
            _cameraSaved = false;

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

        // ─── 카메라 ───

        private void SaveCamera()
        {
            if (_camera == null) return;
            _savedPos = _camera.transform.position;
            _savedRot = _camera.transform.rotation;
            _savedFov = _camera.fieldOfView;
            _cameraSaved = true;
        }

        private void RestoreCamera()
        {
            if (_camera == null || !_cameraSaved) return;
            _camera.transform.SetPositionAndRotation(_savedPos, _savedRot);
            _camera.fieldOfView = _savedFov;
        }

        private void StartCameraMove(CutsceneCameraPoint point, float seconds)
        {
            if (_camera == null) return;
            _fromPos = _camera.transform.position;
            _fromRot = _camera.transform.rotation;
            _fromFov = _camera.fieldOfView;
            _toPos = point.transform.position;
            _toRot = point.transform.rotation;
            _toFov = point.FieldOfView > 0f ? point.FieldOfView : _fromFov;
            _moveTime = 0f;
            _moveDuration = Mathf.Max(0f, seconds);
            _cameraMoving = true;
            if (_moveDuration <= 0f) FinishCameraMove();
        }

        private void FinishCameraMove()
        {
            if (!_cameraMoving) return;
            ApplyCameraLerp(1f);
            _cameraMoving = false;
        }

        private void ApplyCameraLerp(float t)
        {
            if (_camera == null) return;
            _camera.transform.SetPositionAndRotation(
                Vector3.Lerp(_fromPos, _toPos, t),
                Quaternion.Slerp(_fromRot, _toRot, t));
            _camera.fieldOfView = Mathf.Lerp(_fromFov, _toFov, t);
        }

        // ─── 컷씬 중 조작 끄기 ───

        private void DisableControls()
        {
            if (disableCameraControllerDuringCutscene && _camera != null)
            {
                var rts = _camera.GetComponent<InfraTown.CameraSystem.RTSCameraController>();
                Disable(rts);
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

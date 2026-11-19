using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;

namespace InfraTown.EventSystem
{
    /// <summary>
    /// 시네머신 컷씬 연결 지점. 팀원이 만든 시네머신 연출(보통 Timeline + Cinemachine Track)을 컷씬 시스템에 연결한다.
    /// 씬 오브젝트에 붙이고 ID를 정한 뒤, CutsceneData(스타일 Cinemachine)의 sequenceId에 같은 ID를 쓰면
    /// 대화 전에 이 연출이 재생되고, 끝나면 대화가 이어진다.
    ///
    /// 끝나는 시점 (아래 중 먼저 오는 것):
    ///   1) director(PlayableDirector)가 끝까지 재생됨 / 멈춤
    ///   2) director가 없으면 duration초가 지남
    ///   3) Timeline Signal이나 다른 스크립트가 Finish()를 부름 (duration 0 + director 없음이면 이것만 기다림)
    ///
    /// 게임 시간이 멈춘 동안에도 재생되도록 director는 실제 시간(UnscaledGameTime)으로 돌린다.
    /// 시네머신을 쓴다면 Main Camera의 CinemachineBrain에서 'Ignore Time Scale'을 켜야 카메라도 움직인다.
    /// </summary>
    public class CinemachineSequence : MonoBehaviour
    {
        [Tooltip("CutsceneData.sequenceId에 쓸 ID (예: opening)")]
        [SerializeField] private string sequenceId;

        [Tooltip("재생할 Timeline (비우면 duration초 뒤 또는 Finish() 호출로 끝남)")]
        [SerializeField] private PlayableDirector director;

        [Tooltip("director가 없을 때 끝나기까지의 초. 0이면 Finish()가 불릴 때까지 기다림")]
        [SerializeField] private float duration = 0f;

        [Tooltip("재생하는 동안만 켤 오브젝트 (시네머신 가상 카메라 묶음 등). 끝나면 다시 끔")]
        [SerializeField] private GameObject activateWhilePlaying;

        [Tooltip("건너뛰면 Timeline 끝 장면으로 맞춘 뒤 멈춤 (끄면 그 자리에서 멈춤)")]
        [SerializeField] private bool jumpToEndOnSkip = true;

        [SerializeField] private UnityEvent onStarted = new UnityEvent();
        [SerializeField] private UnityEvent onFinished = new UnityEvent();

        private static readonly List<CinemachineSequence> All = new List<CinemachineSequence>();

        public string SequenceId { get => sequenceId; set => sequenceId = value; }
        public PlayableDirector Director { get => director; set => director = value; }
        public float Duration { get => duration; set => duration = value; }
        public GameObject ActivateWhilePlaying { get => activateWhilePlaying; set => activateWhilePlaying = value; }
        public UnityEvent OnStarted => onStarted;
        public UnityEvent OnFinished => onFinished;

        public bool IsPlaying { get; private set; }

        /// <summary>재생 시작 후 지난 실제 시간(초)</summary>
        public float Elapsed { get; private set; }

        /// <summary>끝났을 때 (건너뛰었으면 true)</summary>
        public event Action<bool> OnSequenceEnded;

        private bool _directorStarted;

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() => All.Remove(this);

        /// <summary>ID로 찾기 (없으면 null). 꺼진 오브젝트는 찾지 않음</summary>
        public static CinemachineSequence Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            All.RemoveAll(s => s == null);
            foreach (var s in All) if (s.sequenceId == id) return s;
            return null;
        }

        /// <summary>재생 시작 (CutsceneManager가 부름)</summary>
        public void Play()
        {
            if (IsPlaying) return;
            IsPlaying = true;
            Elapsed = 0f;
            _directorStarted = false;

            if (activateWhilePlaying != null) activateWhilePlaying.SetActive(true);

            if (director != null && director.playableAsset != null)
            {
                director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime; // 시간 정지 중에도 재생
                director.time = 0;
                director.Play();
                _directorStarted = true;
            }

            onStarted.Invoke();
        }

        /// <summary>연출이 끝났다고 알림 (Timeline Signal Receiver 등에서 호출)</summary>
        public void Finish() => End(false);

        /// <summary>건너뛰기 (CutsceneManager.Skip이 부름)</summary>
        public void Skip()
        {
            if (!IsPlaying) return;
            if (_directorStarted && director != null && jumpToEndOnSkip)
            {
                director.time = director.duration;
                director.Evaluate();
            }
            End(true);
        }

        /// <summary>시간 경과 확인 (CutsceneManager.Tick이 실제 시간으로 부름)</summary>
        public void Tick(float deltaSeconds)
        {
            if (!IsPlaying) return;
            Elapsed += deltaSeconds;

            if (_directorStarted && director != null)
            {
                // 끝까지 갔거나(Hold면 멈추지 않으므로 시간으로도 확인) 누가 멈췄으면 끝
                bool reachedEnd = director.duration > 0 && director.time >= director.duration - 0.0001;
                if (reachedEnd || director.state != PlayState.Playing) End(false);
                return;
            }

            if (duration > 0f && Elapsed >= duration) End(false);
        }

        private void End(bool skipped)
        {
            if (!IsPlaying) return;
            IsPlaying = false;

            if (_directorStarted && director != null && director.state == PlayState.Playing) director.Stop();
            _directorStarted = false;
            if (activateWhilePlaying != null) activateWhilePlaying.SetActive(false);

            onFinished.Invoke();
            OnSequenceEnded?.Invoke(skipped);
        }

        private void OnDestroy()
        {
            // 재생 중에 사라지면 컷씬이 멈춘 채로 남지 않도록 끝냄
            if (IsPlaying) End(true);
        }
    }
}

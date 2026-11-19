using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.TimeSystem;

namespace InfraTown.EventSystem
{
    /// <summary>
    /// 스토리·해금 연출·경고 등 대화 재생 매니저 (기획서 2장 '스토리 연출 캐릭터 대화 상자', 4장 해금 메시지).
    /// 화면 표시는 UI(팀원 1)가 이벤트를 구독해서 하고, 이 매니저는 순서·대기열·시간 정지·재생 기록만 관리한다.
    ///
    /// UI 연결 방법:
    ///   OnDialogueStarted  → 대화창 열기
    ///   OnLineShown        → 이름·초상화·문장 표시 (GetCurrentText()로 모드에 맞는 문장)
    ///   클릭/버튼          → Next() (끝까지 넘기기 Skip())
    ///   OnDialogueEnded    → 대화창 닫기
    /// 대화를 보여줄 UI(구독자)가 없으면 시간을 멈추지 않고 콘솔에만 출력한 뒤 바로 끝낸다 (게임이 멈춘 채로 남지 않도록).
    /// </summary>
    public class DialogueEventManager : MonoBehaviour
    {
        public static DialogueEventManager Instance { get; private set; }

        [Tooltip("어르신·어린이 모드: 쉬운 문장(easyText)이 있으면 그것을 표시")]
        [SerializeField] private bool useEasyText = false;

        public bool UseEasyText { get => useEasyText; set => useEasyText = value; }

        public bool IsPlaying => CurrentDialogue != null;
        public DialogueData CurrentDialogue { get; private set; }
        public int CurrentLineIndex { get; private set; } = -1;
        public DialogueLine CurrentLine =>
            IsPlaying && CurrentLineIndex >= 0 && CurrentLineIndex < CurrentDialogue.lines.Count
                ? CurrentDialogue.lines[CurrentLineIndex]
                : null;

        /// <summary>대기 중인 대화 수</summary>
        public int QueueCount => _queue.Count;

        public event Action<DialogueData> OnDialogueStarted;
        public event Action<DialogueLine, int, int> OnLineShown; // (줄, 몇 번째, 전체 줄 수)
        public event Action<DialogueData> OnDialogueEnded;

        private readonly Queue<DialogueData> _queue = new Queue<DialogueData>();
        private readonly HashSet<string> _played = new HashSet<string>();
        private bool _pausedForDialogue;
        private bool _manualPause;

        private void Awake()
        {
            // 같은 오브젝트의 다른 컴포넌트까지 지우지 않도록 컴포넌트만 제거
            if (Instance != null && Instance != this) { InfraTown.Core.SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            ReleasePause();
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 대화 재생. 다른 대화가 재생 중이면 대기열에 넣는다.
        /// 한 번만 재생하는 대화를 이미 봤거나 줄이 없으면 false.
        /// </summary>
        public bool Play(DialogueData dialogue)
        {
            if (dialogue == null || dialogue.lines == null || dialogue.lines.Count == 0) return false;
            if (dialogue.playOnce && HasPlayed(dialogue.dialogueId)) return false;
            if (dialogue == CurrentDialogue || _queue.Contains(dialogue)) return false;

            if (IsPlaying) { _queue.Enqueue(dialogue); return true; }

            Begin(dialogue);
            return true;
        }

        /// <summary>코드에서 바로 한 사람의 대사를 재생 (매번 재생, 시간 정지)</summary>
        public bool Play(string speaker, params string[] texts)
        {
            var lines = new DialogueLine[texts.Length];
            for (int i = 0; i < texts.Length; i++) lines[i] = new DialogueLine(speaker, texts[i]);
            return Play(DialogueData.Create($"adhoc_{speaker}_{Time.frameCount}_{texts.GetHashCode()}", true, false, lines));
        }

        /// <summary>다음 줄로 (마지막 줄이면 대화 종료 후 대기열 다음 대화 시작)</summary>
        public void Next()
        {
            if (!IsPlaying) return;

            CurrentLineIndex++;
            if (CurrentLineIndex >= CurrentDialogue.lines.Count) { End(); return; }
            OnLineShown?.Invoke(CurrentLine, CurrentLineIndex, CurrentDialogue.lines.Count);
        }

        /// <summary>현재 대화를 끝까지 건너뜀 (대기열 다음 대화는 이어서 재생)</summary>
        public void Skip()
        {
            if (IsPlaying) End();
        }

        /// <summary>현재 대화와 대기열을 모두 비움</summary>
        public void ClearAll()
        {
            _queue.Clear();
            if (IsPlaying) End();
        }

        /// <summary>현재 줄을 모드(일반/쉬운)에 맞게</summary>
        public string GetCurrentText() => CurrentLine?.GetText(useEasyText) ?? string.Empty;

        public bool HasPlayed(string dialogueId) => !string.IsNullOrEmpty(dialogueId) && _played.Contains(dialogueId);

        /// <summary>재생 기록 (저장/불러오기용)</summary>
        public IReadOnlyCollection<string> PlayedDialogueIds => _played;

        public void MarkPlayed(string dialogueId)
        {
            if (!string.IsNullOrEmpty(dialogueId)) _played.Add(dialogueId);
        }

        public void ResetPlayed() => _played.Clear();

        // ─── 이전 API 호환: 대화창을 직접 띄우는 코드가 시간만 멈추고 싶을 때 ───

        /// <summary>주민 대화나 이벤트 팝업이 열릴 때 호출 (시간 정지)</summary>
        public void StartDialogueEvent()
        {
            _manualPause = true;
            TimeManager.Instance?.RequestEventPause(this);
        }

        /// <summary>대화가 끝나고 팝업이 닫힐 때 호출 (시간 재개)</summary>
        public void EndDialogueEvent()
        {
            _manualPause = false;
            if (!_pausedForDialogue) TimeManager.Instance?.ReleaseEventPause(this);
        }

        // ─── 내부 ───

        private bool HasViewer => OnLineShown != null || OnDialogueStarted != null;

        private void Begin(DialogueData dialogue)
        {
            CurrentDialogue = dialogue;
            CurrentLineIndex = 0;
            if (dialogue.playOnce) MarkPlayed(dialogue.dialogueId);

            if (!HasViewer)
            {
                // 표시할 UI가 없으면 멈추지 않고 기록만 남김
                foreach (var line in dialogue.lines) Debug.Log($"[대화] {line.speaker}: {line.GetText(useEasyText)}");
                End();
                return;
            }

            if (dialogue.pauseGame && TimeManager.Instance != null)
            {
                TimeManager.Instance.RequestEventPause(this);
                _pausedForDialogue = true;
            }

            OnDialogueStarted?.Invoke(dialogue);
            OnLineShown?.Invoke(CurrentLine, CurrentLineIndex, dialogue.lines.Count);
        }

        private void End()
        {
            var ended = CurrentDialogue;
            CurrentDialogue = null;
            CurrentLineIndex = -1;

            if (_pausedForDialogue && !_manualPause) ReleasePause();
            _pausedForDialogue = false;

            OnDialogueEnded?.Invoke(ended);

            if (_queue.Count > 0) Begin(_queue.Dequeue());
        }

        private void ReleasePause()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.ReleaseEventPause(this);
        }
    }
}

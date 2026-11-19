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
    ///   OnLineShown        → 문장 표시 (GetCurrentText()로 모드에 맞는 문장)
    ///                        화자 배치: 왼쪽 = 주인공(LeftSpeakerName/LeftPortrait, 항상),
    ///                                  오른쪽 = 지금(또는 마지막으로) 말한 다른 인물(RightSpeakerName/RightPortrait),
    ///                                  CurrentSide = 지금 말하는 쪽(밝게 표시, 반대쪽은 어둡게)
    ///   클릭/버튼          → Next() (끝까지 넘기기 Skip())
    ///   OnDialogueEnded    → 대화창 닫기
    /// 대화에 컷씬(cutsceneBefore)이 있으면 CutsceneManager로 컷씬을 먼저 보여준 뒤 대화를 시작한다.
    /// 대화를 보여줄 UI(구독자)가 없으면 시간을 멈추지 않고 콘솔에만 출력한 뒤 바로 끝낸다 (게임이 멈춘 채로 남지 않도록).
    /// </summary>
    public class DialogueEventManager : MonoBehaviour
    {
        public static DialogueEventManager Instance { get; private set; }

        [Tooltip("어르신·어린이 모드: 쉬운 문장(easyText)이 있으면 그것을 표시")]
        [SerializeField] private bool useEasyText = false;

        [Header("인물 (주인공은 항상 왼쪽, 나머지는 오른쪽)")]
        [Tooltip("주인공 이름 — 대화 줄의 speaker가 이 이름이면 왼쪽에 선다")]
        [SerializeField] private string protagonistName = "주인공";

        [SerializeField] private Sprite protagonistPortrait;

        [Tooltip("주민·비서·이장님 등 다른 인물의 기본 초상화")]
        [SerializeField] private List<DialogueCharacter> characters = new List<DialogueCharacter>
        {
            new DialogueCharacter("주민"),
            new DialogueCharacter("비서"),
            new DialogueCharacter("이장님"),
            new DialogueCharacter("안내원"),
        };

        public bool UseEasyText { get => useEasyText; set => useEasyText = value; }

        public string ProtagonistName { get => protagonistName; set => protagonistName = value; }
        public Sprite ProtagonistPortrait { get => protagonistPortrait; set => protagonistPortrait = value; }
        public IReadOnlyList<DialogueCharacter> Characters => characters;

        public bool IsPlaying => CurrentDialogue != null;

        /// <summary>대화 전 컷씬을 보여주는 중 (컷씬이 끝나면 대화 시작)</summary>
        public bool IsWaitingForCutscene => _waitingDialogue != null;

        /// <summary>대화 또는 대화 앞 컷씬이 진행 중 (새 대화는 대기열로 감)</summary>
        public bool IsBusy => IsPlaying || IsWaitingForCutscene;

        public DialogueData CurrentDialogue { get; private set; }
        public int CurrentLineIndex { get; private set; } = -1;
        public DialogueLine CurrentLine =>
            IsPlaying && CurrentLineIndex >= 0 && CurrentLineIndex < CurrentDialogue.lines.Count
                ? CurrentDialogue.lines[CurrentLineIndex]
                : null;

        // ─── 화자 배치 (OnLineShown 때 갱신) ───

        /// <summary>지금 말하는 쪽 (Left = 주인공, Right = 다른 인물, Narration = 해설)</summary>
        public DialogueSide CurrentSide { get; private set; } = DialogueSide.Narration;

        /// <summary>왼쪽 = 항상 주인공</summary>
        public string LeftSpeakerName => protagonistName;
        public Sprite LeftPortrait { get; private set; }

        /// <summary>오른쪽 = 지금 말하는 다른 인물. 주인공이 말할 때는 마지막(아직 없으면 다음)에 말하는 다른 인물. 없으면 null</summary>
        public string RightSpeakerName { get; private set; }
        public Sprite RightPortrait { get; private set; }

        /// <summary>대기 중인 대화 수</summary>
        public int QueueCount => _queue.Count;

        public event Action<DialogueData> OnDialogueStarted;
        public event Action<DialogueLine, int, int> OnLineShown; // (줄, 몇 번째, 전체 줄 수)
        public event Action<DialogueData> OnDialogueEnded;

        private struct Request
        {
            public DialogueData dialogue;
            public CutsceneData cutscene;
        }

        private readonly Queue<Request> _queue = new Queue<Request>();
        private readonly HashSet<string> _played = new HashSet<string>();
        private DialogueData _waitingDialogue;
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
        /// 대화 재생. 대화에 cutsceneBefore가 있으면 컷씬을 먼저 보여준다.
        /// 다른 대화가 재생 중이면 대기열에 넣는다. 한 번만 재생하는 대화를 이미 봤거나 줄이 없으면 false.
        /// </summary>
        public bool Play(DialogueData dialogue) => Play(dialogue, null);

        /// <summary>
        /// 컷씬을 지정해서 대화 재생 (cutsceneOverride가 있으면 대화 에셋의 cutsceneBefore 대신 이것을 먼저 보여줌)
        /// </summary>
        public bool Play(DialogueData dialogue, CutsceneData cutsceneOverride)
        {
            if (dialogue == null || dialogue.lines == null || dialogue.lines.Count == 0) return false;
            if (dialogue.playOnce && HasPlayed(dialogue.dialogueId)) return false;
            if (dialogue == CurrentDialogue || dialogue == _waitingDialogue) return false;
            foreach (var r in _queue) if (r.dialogue == dialogue) return false;

            var request = new Request { dialogue = dialogue, cutscene = cutsceneOverride != null ? cutsceneOverride : dialogue.cutsceneBefore };
            if (IsBusy) { _queue.Enqueue(request); return true; }

            Begin(request);
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
            ShowCurrentLine();
        }

        /// <summary>현재 대화를 끝까지 건너뜀 (대기열 다음 대화는 이어서 재생)</summary>
        public void Skip()
        {
            if (IsPlaying) End();
        }

        /// <summary>현재 대화와 대기열을 모두 비움 (대화 앞 컷씬이 진행 중이면 컷씬이 끝나도 대화는 시작하지 않음)</summary>
        public void ClearAll()
        {
            _queue.Clear();
            _waitingDialogue = null;
            if (IsPlaying) End();
        }

        /// <summary>현재 줄을 모드(일반/쉬운)에 맞게</summary>
        public string GetCurrentText() => CurrentLine?.GetText(useEasyText) ?? string.Empty;

        /// <summary>현재 줄의 초상화 (줄에 지정된 것 → 등록된 인물 기본 초상화)</summary>
        public Sprite GetCurrentPortrait() => GetPortrait(CurrentLine);

        public bool HasPlayed(string dialogueId) => !string.IsNullOrEmpty(dialogueId) && _played.Contains(dialogueId);

        /// <summary>재생 기록 (저장/불러오기용)</summary>
        public IReadOnlyCollection<string> PlayedDialogueIds => _played;

        public void MarkPlayed(string dialogueId)
        {
            if (!string.IsNullOrEmpty(dialogueId)) _played.Add(dialogueId);
        }

        public void ResetPlayed() => _played.Clear();

        // ─── 화자 정보 ───

        public bool IsProtagonist(string speaker) =>
            !string.IsNullOrEmpty(speaker) && speaker == protagonistName;

        /// <summary>줄이 서는 자리 (Auto면 주인공 → 왼쪽, 다른 이름 → 오른쪽, 이름 없음 → 해설)</summary>
        public DialogueSide GetSide(DialogueLine line)
        {
            if (line == null) return DialogueSide.Narration;
            if (line.side != DialogueSide.Auto) return line.side;
            if (string.IsNullOrEmpty(line.speaker)) return DialogueSide.Narration;
            return IsProtagonist(line.speaker) ? DialogueSide.Left : DialogueSide.Right;
        }

        /// <summary>줄의 초상화: 줄에 지정한 것 → 주인공/등록 인물의 기본 초상화</summary>
        public Sprite GetPortrait(DialogueLine line)
        {
            if (line == null) return null;
            if (line.portrait != null) return line.portrait;
            return GetCharacterPortrait(line.speaker);
        }

        /// <summary>이름으로 기본 초상화 찾기 (없으면 null)</summary>
        public Sprite GetCharacterPortrait(string speaker)
        {
            if (string.IsNullOrEmpty(speaker)) return null;
            if (IsProtagonist(speaker)) return protagonistPortrait;
            foreach (var c in characters)
            {
                if (c != null && c.name == speaker) return c.portrait;
            }
            return null;
        }

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

        private void Begin(Request request)
        {
            var dialogue = request.dialogue;
            if (dialogue.playOnce) MarkPlayed(dialogue.dialogueId);

            // 컷씬 먼저 → 끝나면 대화
            var cutscenes = CutsceneManager.Instance;
            if (request.cutscene != null && cutscenes != null)
            {
                _waitingDialogue = dialogue;
                bool started = cutscenes.Play(request.cutscene, () => OnCutsceneFinished(dialogue));
                if (started) return;
                _waitingDialogue = null; // 이미 본 컷씬 등으로 재생 안 되면 바로 대화
            }

            StartLines(dialogue);
        }

        private void OnCutsceneFinished(DialogueData dialogue)
        {
            if (_waitingDialogue != dialogue) { BeginNextInQueue(); return; } // ClearAll로 취소됨
            _waitingDialogue = null;
            StartLines(dialogue);
        }

        private void StartLines(DialogueData dialogue)
        {
            CurrentDialogue = dialogue;
            CurrentLineIndex = 0;
            LeftPortrait = protagonistPortrait;
            RightSpeakerName = null;
            RightPortrait = null;

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
            ShowCurrentLine();
        }

        /// <summary>화자 배치를 갱신하고 OnLineShown 발생</summary>
        private void ShowCurrentLine()
        {
            var line = CurrentLine;
            CurrentSide = GetSide(line);

            if (CurrentSide == DialogueSide.Left)
            {
                // 주인공 표정이 줄마다 바뀔 수 있음
                LeftPortrait = GetPortrait(line) ?? protagonistPortrait;
                // 오른쪽이 아직 비어 있으면 앞으로 말할 상대를 미리 세워 둠
                if (RightSpeakerName == null) SetRightFromUpcoming();
            }
            else if (CurrentSide == DialogueSide.Right)
            {
                RightSpeakerName = line.speaker;
                RightPortrait = GetPortrait(line);
            }

            OnLineShown?.Invoke(line, CurrentLineIndex, CurrentDialogue.lines.Count);
        }

        private void SetRightFromUpcoming()
        {
            for (int i = CurrentLineIndex + 1; i < CurrentDialogue.lines.Count; i++)
            {
                var next = CurrentDialogue.lines[i];
                if (GetSide(next) != DialogueSide.Right) continue;
                RightSpeakerName = next.speaker;
                RightPortrait = GetPortrait(next);
                return;
            }
        }

        private void End()
        {
            var ended = CurrentDialogue;
            CurrentDialogue = null;
            CurrentLineIndex = -1;
            CurrentSide = DialogueSide.Narration;

            if (_pausedForDialogue && !_manualPause) ReleasePause();
            _pausedForDialogue = false;

            OnDialogueEnded?.Invoke(ended);

            BeginNextInQueue();
        }

        private void BeginNextInQueue()
        {
            if (!IsBusy && _queue.Count > 0) Begin(_queue.Dequeue());
        }

        private void ReleasePause()
        {
            if (TimeManager.Instance != null) TimeManager.Instance.ReleaseEventPause(this);
        }
    }
}

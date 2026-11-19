using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Core;
using InfraTown.TimeSystem;

namespace InfraTown.EventSystem
{
    /// <summary>
    /// 스토리·해금 연출·경고 등 대화 재생 매니저 (기획서 2장 '스토리 연출 캐릭터 대화 상자', 4장 해금 메시지).
    /// 화면 표시는 UI(팀원 1)가 이벤트를 구독해서 하고, 이 매니저는 순서·대기열·시간 정지·재생 기록만 관리한다.
    ///
    /// UI 연결 방법:
    ///   OnDialogueStarted  → 대화창 열기
    ///   OnLineShown        → 이름 GetCurrentSpeakerName(), 문장 GetCurrentText() (모드에 맞는 문장, {player}는 주인공 이름으로)
    ///                        화자 배치 (사용자 기획 2026-11-19 수정):
    ///                          주인공은 이미지 없음 — 이름(LeftSpeakerName)만, 말할 때 CurrentSide == Left
    ///                          오른쪽 = 지금(또는 마지막으로) 말한 다른 인물(RightSpeakerName/RightPortrait)
    ///                          주인공·나레이션이 말하는 동안에도 오른쪽 인물은 그대로 두고 회색으로 가라앉힘
    ///                          → 초상화 Image.color = RightPortraitColor (IsRightDimmed면 dimmedPortraitColor, 아니면 흰색)
    ///   글자 출력          → 한 글자씩 나옴 (charactersPerSecond). TMP는 maxVisibleCharacters = VisibleCharacterCount 권장,
    ///                        일반 Text는 GetVisibleText(). 바뀔 때마다 OnVisibleTextChanged, 다 나오면 OnLineFullyShown
    ///   Enter / 마우스 왼쪽 → (이 매니저가 직접 받음) 출력 중이면 문장 전체를 한 번에, 다 나왔으면 다음 줄 = Advance()
    ///                        기본은 다 나와도 입력이 없으면 그 줄에서 멈춰 있음. 버튼 위 클릭은 무시
    ///   Skip 버튼          → Skip() (지금 대화를 한 번에 끝까지 건너뜀)
    ///   Auto 버튼          → ToggleAutoMode() (켜면 문장이 다 나온 뒤 autoAdvanceDelay(1초) 후 자동으로 다음 줄, 끌 때까지 유지)
    ///                        버튼 표시는 IsAutoMode / OnAutoModeChanged
    ///   OnDialogueEnded    → 대화창 닫기
    /// 대화에 컷씬(cutsceneBefore)이 있으면 CutsceneManager로 컷씬을 먼저 보여준 뒤 대화를 시작한다.
    /// 대화를 보여줄 UI(구독자)가 없으면 시간을 멈추지 않고 콘솔에만 출력한 뒤 바로 끝낸다 (게임이 멈춘 채로 남지 않도록).
    /// </summary>
    public class DialogueEventManager : MonoBehaviour
    {
        public static DialogueEventManager Instance { get; private set; }

        [Tooltip("어르신·어린이 모드: 쉬운 문장(easyText)이 있으면 그것을 표시")]
        [SerializeField] private bool useEasyText = false;

        [Header("인물 (주인공은 이미지 없음, 다른 인물은 오른쪽)")]
        [Tooltip("대화 에셋에서 주인공을 가리키는 화자 이름 — 이 이름(또는 {player})이면 주인공 대사, 화면에는 플레이어가 정한 이름이 나온다")]
        [SerializeField] private string protagonistName = "주인공";

        [Tooltip("주인공·나레이션이 말하는 동안 오른쪽 인물 초상화에 곱할 색 (회색으로 가라앉힘)")]
        [SerializeField] private Color dimmedPortraitColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        [Tooltip("주민·비서·이장님 등 다른 인물의 기본 초상화")]
        [SerializeField] private List<DialogueCharacter> characters = new List<DialogueCharacter>
        {
            new DialogueCharacter("주민"),
            new DialogueCharacter("비서"),
            new DialogueCharacter("이장님"),
            new DialogueCharacter("안내원"),
        };

        [Header("글자 출력 / 넘기기 (사용자 기획 2026-11-19)")]
        [Tooltip("1초에 나오는 글자 수. 0이면 한 번에 전부")]
        [SerializeField] private float charactersPerSecond = 30f;

        [Tooltip("Auto: 문장이 다 나온 뒤 다음 줄로 넘어가기까지 기다리는 시간(초)")]
        [SerializeField] private float autoAdvanceDelay = 1f;

        [Tooltip("Auto 모드 (켜면 끌 때까지 다음 대화에도 유지)")]
        [SerializeField] private bool autoMode = false;

        [Tooltip("Enter·마우스 왼쪽 클릭을 이 매니저가 직접 받아 Advance() (끄면 UI가 직접 Advance()를 부름)")]
        [SerializeField] private bool handleInput = true;

        public bool UseEasyText { get => useEasyText; set => useEasyText = value; }

        public float CharactersPerSecond { get => charactersPerSecond; set => charactersPerSecond = Mathf.Max(0f, value); }
        public float AutoAdvanceDelay { get => autoAdvanceDelay; set => autoAdvanceDelay = Mathf.Max(0f, value); }
        public bool HandleInput { get => handleInput; set => handleInput = value; }

        /// <summary>Auto 모드 켜짐</summary>
        public bool IsAutoMode => autoMode;

        /// <summary>현재 줄에서 지금까지 보이는 글자 수 (TMP maxVisibleCharacters에 그대로)</summary>
        public int VisibleCharacterCount { get; private set; }

        /// <summary>현재 줄 전체 글자 수</summary>
        public int CurrentTextLength => _currentText.Length;

        /// <summary>현재 줄이 끝까지 다 나왔는지</summary>
        public bool IsLineFullyShown => !IsPlaying || VisibleCharacterCount >= _currentText.Length;

        /// <summary>Auto 모드에서 다음 줄까지 남은 시간(초). Auto가 아니거나 아직 출력 중이면 -1</summary>
        public float AutoAdvanceRemaining => IsPlaying && autoMode && IsLineFullyShown ? Mathf.Max(0f, autoAdvanceDelay - _autoTimer) : -1f;

        /// <summary>보이는 글자가 늘었을 때 (지금까지 보이는 문장)</summary>
        public event Action<string> OnVisibleTextChanged;

        /// <summary>현재 줄이 끝까지 다 나왔을 때 (한 번에 띄웠을 때 포함)</summary>
        public event Action OnLineFullyShown;

        /// <summary>Auto 모드가 바뀌었을 때</summary>
        public event Action<bool> OnAutoModeChanged;

        public string ProtagonistName { get => protagonistName; set => protagonistName = value; }
        public Color DimmedPortraitColor { get => dimmedPortraitColor; set => dimmedPortraitColor = value; }
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

        /// <summary>주인공 이름 (플레이어가 정한 이름). 주인공은 초상화가 없다</summary>
        public string LeftSpeakerName => PlayerProfile.PlayerName;

        /// <summary>
        /// 오른쪽 = 지금 말하는 다른 인물. 주인공·나레이션이 말할 때는 마지막(아직 없으면 다음)에 말하는 다른 인물이 그대로 남는다. 없으면 null
        /// </summary>
        public string RightSpeakerName { get; private set; }
        public Sprite RightPortrait { get; private set; }

        /// <summary>오른쪽 인물이 지금 말하고 있지 않음 (주인공·나레이션 차례) → 회색으로 가라앉혀 표시</summary>
        public bool IsRightDimmed => RightSpeakerName != null && CurrentSide != DialogueSide.Right;

        /// <summary>오른쪽 초상화 Image.color에 그대로 넣을 색 (말하는 중 흰색, 가라앉힘 dimmedPortraitColor)</summary>
        public Color RightPortraitColor => IsRightDimmed ? dimmedPortraitColor : Color.white;

        /// <summary>대기 중인 대화 수</summary>
        public int QueueCount => _queue.Count;

        public event Action<DialogueData> OnDialogueStarted;
        public event Action<DialogueLine, int, int> OnLineShown; // (줄, 몇 번째, 전체 줄 수)
        public event Action<DialogueData> OnDialogueEnded;

        private struct Request
        {
            public DialogueData dialogue;
            public CutsceneData cutscene;
            public Action onStarted;
            public Action onFinished;
        }

        private Action _currentOnFinished;

        private readonly Queue<Request> _queue = new Queue<Request>();
        private readonly HashSet<string> _played = new HashSet<string>();
        private DialogueData _waitingDialogue;
        private bool _pausedForDialogue;
        private bool _manualPause;

        // 글자 출력
        private string _currentText = string.Empty;
        private float _revealProgress;
        private float _autoTimer;
        private int _lineShownFrame = -1;
        private int _endFrame = -1;
        private bool _shownOnScreen;

        /// <summary>
        /// 대화 중이거나 이번 프레임에 대화가 끝났음 — 마지막 줄을 넘긴 클릭이 같은 프레임에 건설·건물 클릭으로 새지 않도록 PointerGuard가 사용
        /// </summary>
        public bool BlocksWorldInput => IsBusy || _endFrame == Time.frameCount;

        private void Update()
        {
            ProcessInput();
            Tick(Time.unscaledDeltaTime); // 대화 중 게임 시간이 멈춰도 글자는 실제 시간으로 나옴
        }

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
        public bool Play(DialogueData dialogue, CutsceneData cutsceneOverride) => Play(dialogue, cutsceneOverride, null, null);

        /// <summary>
        /// 대화 재생 + 시작/끝 알림 (튜토리얼 연출처럼 이 대화에 맞춰 무언가 할 때).
        /// onStarted는 컷씬이 끝나고 첫 줄이 나오기 직전, onFinished는 마지막 줄을 넘기거나 Skip했을 때 호출된다.
        /// 재생하지 않으면(false) 둘 다 호출되지 않는다.
        /// </summary>
        public bool Play(DialogueData dialogue, CutsceneData cutsceneOverride, Action onStarted, Action onFinished)
        {
            if (dialogue == null || dialogue.lines == null || dialogue.lines.Count == 0) return false;
            if (dialogue.playOnce && HasPlayed(dialogue.dialogueId)) return false;
            if (dialogue == CurrentDialogue || dialogue == _waitingDialogue) return false;
            foreach (var r in _queue) if (r.dialogue == dialogue) return false;

            var request = new Request
            {
                dialogue = dialogue,
                cutscene = cutsceneOverride != null ? cutsceneOverride : dialogue.cutsceneBefore,
                onStarted = onStarted,
                onFinished = onFinished
            };
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

        /// <summary>
        /// Enter·클릭 동작: 문장이 아직 나오는 중이면 전체를 한 번에 띄우고, 이미 다 나왔으면 다음 줄로
        /// </summary>
        public void Advance()
        {
            if (!IsPlaying) return;
            if (!IsLineFullyShown) RevealCurrentLine();
            else Next();
        }

        /// <summary>현재 줄을 끝까지 한 번에 띄움 (다음 줄로는 넘어가지 않음)</summary>
        public void RevealCurrentLine()
        {
            if (!IsPlaying || IsLineFullyShown) return;
            SetVisibleCount(_currentText.Length);
        }

        /// <summary>Skip 버튼: 현재 대화를 끝까지 건너뜀 (대기열 다음 대화는 이어서 재생)</summary>
        public void Skip()
        {
            ClearButtonSelection();
            if (IsPlaying) End();
        }

        /// <summary>Auto 버튼: 켜고 끄기</summary>
        public void ToggleAutoMode() => SetAutoMode(!autoMode);

        /// <summary>Auto 모드 설정. 켜면 문장이 다 나온 뒤 autoAdvanceDelay초 후 자동으로 다음 줄 (켠 순간부터 다시 셈)</summary>
        public void SetAutoMode(bool on)
        {
            ClearButtonSelection();
            if (autoMode == on) return;
            autoMode = on;
            _autoTimer = 0f;
            OnAutoModeChanged?.Invoke(autoMode);
        }

        /// <summary>현재 줄에서 지금까지 보이는 문장</summary>
        public string GetVisibleText() =>
            VisibleCharacterCount >= _currentText.Length ? _currentText : _currentText.Substring(0, VisibleCharacterCount);

        /// <summary>
        /// 시간 경과 처리: 글자 출력, Auto 넘김 (Update에서 실제 시간으로 호출, 테스트에서 직접 호출 가능)
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (!IsPlaying || deltaSeconds <= 0f) return;

            if (!IsLineFullyShown)
            {
                _revealProgress += deltaSeconds * charactersPerSecond;
                int count = Mathf.Min(_currentText.Length, Mathf.FloorToInt(_revealProgress));
                if (count > VisibleCharacterCount) SetVisibleCount(count);
                return;
            }

            if (!autoMode) return; // 기본: 다 나와도 입력이 있을 때까지 그 줄에서 멈춤

            _autoTimer += deltaSeconds;
            if (_autoTimer >= autoAdvanceDelay) Next();
        }

        /// <summary>현재 대화와 대기열을 모두 비움 (대화 앞 컷씬이 진행 중이면 컷씬이 끝나도 대화는 시작하지 않음)</summary>
        public void ClearAll()
        {
            _queue.Clear();
            _waitingDialogue = null;
            if (IsPlaying) End();
        }

        /// <summary>현재 줄을 모드(일반/쉬운)에 맞게</summary>
        public string GetCurrentText() => PlayerProfile.ApplyTokens(CurrentLine?.GetText(useEasyText));

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

        /// <summary>
        /// 주인공인지: 대화 에셋의 주인공 이름(protagonistName), {player}/{이름}, 또는 플레이어가 정한 이름
        /// </summary>
        public bool IsProtagonist(string speaker) =>
            !string.IsNullOrEmpty(speaker) &&
            (speaker == protagonistName || PlayerProfile.IsNameToken(speaker) ||
             (PlayerProfile.HasName && speaker == PlayerProfile.PlayerName));

        /// <summary>화면에 보일 이름: 주인공이면 플레이어가 정한 이름, 아니면 {player}만 바꾼 이름</summary>
        public string GetSpeakerDisplayName(string speaker) =>
            IsProtagonist(speaker) ? PlayerProfile.PlayerName : PlayerProfile.ApplyTokens(speaker);

        /// <summary>현재 줄의 화면용 화자 이름</summary>
        public string GetCurrentSpeakerName() => CurrentLine == null ? string.Empty : GetSpeakerDisplayName(CurrentLine.speaker);

        /// <summary>줄이 서는 자리 (Auto면 주인공 → 왼쪽, 다른 이름 → 오른쪽, 이름 없음 → 해설)</summary>
        public DialogueSide GetSide(DialogueLine line)
        {
            if (line == null) return DialogueSide.Narration;
            if (line.side != DialogueSide.Auto) return line.side;
            if (string.IsNullOrEmpty(line.speaker)) return DialogueSide.Narration;
            return IsProtagonist(line.speaker) ? DialogueSide.Left : DialogueSide.Right;
        }

        /// <summary>줄의 초상화: 줄에 지정한 것 → 등록 인물의 기본 초상화. 주인공은 이미지가 없으므로 항상 null</summary>
        public Sprite GetPortrait(DialogueLine line)
        {
            if (line == null || IsProtagonist(line.speaker)) return null;
            if (line.portrait != null) return line.portrait;
            return GetCharacterPortrait(line.speaker);
        }

        /// <summary>이름으로 기본 초상화 찾기 (없거나 주인공이면 null)</summary>
        public Sprite GetCharacterPortrait(string speaker)
        {
            if (string.IsNullOrEmpty(speaker) || IsProtagonist(speaker)) return null;
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
                bool started = cutscenes.Play(request.cutscene, () => OnCutsceneFinished(request));
                if (started) return;
                _waitingDialogue = null; // 이미 본 컷씬 등으로 재생 안 되면 바로 대화
            }

            StartLines(request);
        }

        private void OnCutsceneFinished(Request request)
        {
            if (_waitingDialogue != request.dialogue) { BeginNextInQueue(); return; } // ClearAll로 취소됨
            _waitingDialogue = null;
            StartLines(request);
        }

        private void StartLines(Request request)
        {
            var dialogue = request.dialogue;
            CurrentDialogue = dialogue;
            CurrentLineIndex = 0;
            RightSpeakerName = null;
            RightPortrait = null;
            _currentOnFinished = request.onFinished;

            request.onStarted?.Invoke();

            if (!HasViewer)
            {
                // 표시할 UI가 없으면 멈추지 않고 기록만 남김
                foreach (var line in dialogue.lines)
                    Debug.Log($"[대화] {GetSpeakerDisplayName(line.speaker)}: {PlayerProfile.ApplyTokens(line.GetText(useEasyText))}");
                End();
                return;
            }

            if (dialogue.pauseGame && TimeManager.Instance != null)
            {
                TimeManager.Instance.RequestEventPause(this);
                _pausedForDialogue = true;
            }

            _shownOnScreen = true;
            OnDialogueStarted?.Invoke(dialogue);
            ShowCurrentLine();
        }

        /// <summary>화자 배치를 갱신하고 OnLineShown 발생</summary>
        private void ShowCurrentLine()
        {
            var line = CurrentLine;
            CurrentSide = GetSide(line);

            if (CurrentSide != DialogueSide.Right)
            {
                // 주인공·나레이션 차례: 오른쪽 인물은 그대로 두고(IsRightDimmed로 회색),
                // 아직 아무도 없으면 앞으로 말할 상대를 미리 세워 둠
                if (RightSpeakerName == null) SetRightFromUpcoming();
            }
            else
            {
                RightSpeakerName = GetSpeakerDisplayName(line.speaker);
                RightPortrait = GetPortrait(line);
            }

            // 글자 출력 준비: 처음부터 다시 (글자 속도 0이면 한 번에)
            _currentText = GetCurrentText();
            _revealProgress = 0f;
            _autoTimer = 0f;
            _lineShownFrame = Time.frameCount; // 이 줄을 띄운 클릭이 바로 넘기기로 처리되지 않도록
            VisibleCharacterCount = charactersPerSecond <= 0f ? _currentText.Length : 0;

            OnLineShown?.Invoke(line, CurrentLineIndex, CurrentDialogue.lines.Count);
            if (!IsPlaying || CurrentLine != line) return; // 구독자가 그 자리에서 넘긴 경우
            OnVisibleTextChanged?.Invoke(GetVisibleText());
            if (IsLineFullyShown) OnLineFullyShown?.Invoke();
        }

        private void SetVisibleCount(int count)
        {
            VisibleCharacterCount = Mathf.Clamp(count, 0, _currentText.Length);
            _revealProgress = Mathf.Max(_revealProgress, VisibleCharacterCount);
            _autoTimer = 0f; // Auto는 다 나온 순간부터 셈
            OnVisibleTextChanged?.Invoke(GetVisibleText());
            if (IsLineFullyShown) OnLineFullyShown?.Invoke();
        }

        // ─── 입력 (Enter / 마우스 왼쪽) ───

        private void ProcessInput()
        {
            if (!handleInput || !IsPlaying) return;
            if (_lineShownFrame == Time.frameCount) return; // 대화를 띄운 그 클릭·키는 무시

            bool key = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            bool click = Input.GetMouseButtonDown(0) && !IsPointerOverButton();
            if (key || click) Advance();
        }

        /// <summary>마우스가 버튼(Skip·Auto 등 Selectable) 위에 있으면 true — 버튼 클릭이 넘기기로도 처리되지 않도록</summary>
        private static bool IsPointerOverButton()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es == null) return false;
            var data = new UnityEngine.EventSystems.PointerEventData(es) { position = Input.mousePosition };
            var results = new List<UnityEngine.EventSystems.RaycastResult>();
            es.RaycastAll(data, results);
            foreach (var r in results)
            {
                if (r.gameObject != null && r.gameObject.GetComponentInParent<UnityEngine.UI.Selectable>() != null) return true;
            }
            return false;
        }

        /// <summary>
        /// 버튼을 누른 뒤 선택 상태가 남으면 Enter가 그 버튼을 다시 누르므로 선택을 풀어 둠
        /// </summary>
        private static void ClearButtonSelection()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null &&
                es.currentSelectedGameObject.GetComponent<UnityEngine.UI.Selectable>() != null)
            {
                es.SetSelectedGameObject(null);
            }
        }

        private void SetRightFromUpcoming()
        {
            for (int i = CurrentLineIndex + 1; i < CurrentDialogue.lines.Count; i++)
            {
                var next = CurrentDialogue.lines[i];
                if (GetSide(next) != DialogueSide.Right) continue;
                RightSpeakerName = GetSpeakerDisplayName(next.speaker);
                RightPortrait = GetPortrait(next);
                return;
            }
        }

        private void End()
        {
            var ended = CurrentDialogue;
            var onFinished = _currentOnFinished;
            _currentOnFinished = null;
            CurrentDialogue = null;
            CurrentLineIndex = -1;
            CurrentSide = DialogueSide.Narration;
            _currentText = string.Empty;
            VisibleCharacterCount = 0;
            if (_shownOnScreen) _endFrame = Time.frameCount; // 화면에 보인 대화만 (UI 없이 콘솔로 끝난 대화는 클릭과 무관)
            _shownOnScreen = false;

            if (_pausedForDialogue && !_manualPause) ReleasePause();
            _pausedForDialogue = false;

            OnDialogueEnded?.Invoke(ended);
            onFinished?.Invoke();

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

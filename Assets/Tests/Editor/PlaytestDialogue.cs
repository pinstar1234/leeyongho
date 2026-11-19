using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using InfraTown.Core;
using InfraTown.TimeSystem;
using Dlg = InfraTown.EventSystem;

namespace InfraTown.Tests.Playtest
{
    /// <summary>
    /// 대화 확인 모드 (-Mode dialogue): 대화창 UI(팀원 1)가 아직 없어서, 플레이 중에만 메모리로 임시 대화창·카툰 컷 화면을 만들어
    /// 대화 시스템이 화면에서 어떻게 보이는지 확인하고 스크린샷을 찍는다. 씬은 저장하지 않는다.
    /// 확인: 시간 정지, 글자 출력, 넘기기(Advance = Enter·클릭과 같은 동작), 다 나와도 멈춤, 주인공·나레이션 차례에 상대 초상화 회색,
    ///       {player} 이름, Auto 1초, Skip 버튼, 카툰 컷 → 대화, 일주일 스킵 도중 대화가 나오면 멈춤
    /// </summary>
    public static partial class PlaytestRunner
    {
        private const string FontPath = "Assets/Font/Maplestory Bold SDF.asset";
        private const string TestPlayerName = "민수";

        private class TempDialogueView
        {
            public GameObject root;
            public Canvas canvas;
            public GameObject panel;
            public RectTransform namePlate;
            public TextMeshProUGUI nameText, bodyText, infoText, autoLabel;
            public Image rightPortrait;
            public Button skipButton, autoButton;
            public GameObject comicRoot;
            public RectTransform comicPage;
            public TextMeshProUGUI comicCaption;
            public readonly List<GameObject> comicPanels = new List<GameObject>();
        }

        private static TempDialogueView _view;
        private static TMP_FontAsset _font;

        // ───────────────────────── 시나리오 ─────────────────────────

        private static IEnumerator DialogueScenario()
        {
            Section("D-0. 준비 (마을 배치 + 임시 대화창)");
            yield return PlaceHouses();
            yield return PlaceMiddle(withNimby: false);
            yield return EnsurePower();

            var dlg = Dlg.DialogueEventManager.Instance;
            var cut = Dlg.CutsceneManager.Instance;
            var time = TimeManager.Instance;
            Check("대화 매니저", dlg != null);
            Check("컷씬 매니저 (DialogueTriggers가 자동 추가)", cut != null);
            if (dlg == null || cut == null) yield break;

            if (Dlg.TutorialDialogues.Instance != null)
            {
                Dlg.TutorialDialogues.Instance.SkipTutorial();
                Note("튜토리얼은 이 확인에서 끔 (날짜가 바뀔 때 튜토리얼 대화가 끼어들지 않도록)");
            }
            dlg.ClearAll();
            cut.ClearAll();

            // 테스트용 초상화 (메모리에서만 — 씬의 Characters 칸은 비어 있음)
            var chief = LoadSprite("Assets/Image/이장.png");
            var kid = LoadSprite("Assets/Image/마을 꼬마.png");
            var couple = LoadSprite("Assets/Image/젊은 부부.png");
            var characters = TestWorld.GetField<List<Dlg.DialogueCharacter>>(dlg, "characters");
            SetPortrait(characters, "이장님", chief);
            SetPortrait(characters, "주민", couple);
            SetPortrait(characters, "꼬마", kid);
            Note($"테스트용 초상화(메모리만): 이장님 = 이장.png, 주민 = 젊은 부부.png, 꼬마 = 마을 꼬마.png " +
                 $"({(chief != null && kid != null && couple != null ? "모두 불러옴" : "일부 없음")})");

            PlayerProfile.TrySetPlayerName(TestPlayerName, out _);
            Note($"주인공 이름 = '{TestPlayerName}' (대사의 {{player}}가 이 이름으로 바뀌어야 함)");

            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            BuildView();
            Subscribe(dlg, cut);
            Note("임시 대화창: 오른쪽 초상화 Image.color = RightPortraitColor, 본문 TMP maxVisibleCharacters = VisibleCharacterCount, Skip·Auto 버튼");

            yield return DialogueStartAssetCheck(dlg, time);
            yield return DialogueAutoCheck(dlg);
            yield return DialogueSkipButtonCheck(dlg, time);
            yield return ComicThenDialogueCheck(dlg, cut, time);
            yield return WeekSkipInterruptCheck(dlg, time);

            Unsubscribe(dlg, cut);
            if (_view != null) UnityEngine.Object.Destroy(_view.root);
            _view = null;
        }

        /// <summary>D-1. 사용자가 만든 Start.asset: 시간 정지, 글자 출력, 넘기기, 다 나와도 멈춤, 회색 처리, {player}</summary>
        private static IEnumerator DialogueStartAssetCheck(Dlg.DialogueEventManager dlg, TimeManager time)
        {
            Section("D-1. 게임 시작 대화 (사용자가 만든 Start.asset)");
            var start = AssetDatabase.LoadAssetAtPath<Dlg.DialogueData>("Assets/Data/Dialouge/Start.asset");
            if (start == null || start.lines.Count == 0) { Fail("Start.asset", "없거나 비어 있음"); yield break; }
            Note($"Start.asset {start.lines.Count}줄, 대화 전 컷씬 = {(start.cutsceneBefore != null ? start.cutsceneBefore.name : "없음(비었거나 연결된 에셋이 없음)")}");

            time.SetGameSpeed(GameSpeed.Normal);
            FastForward(); // 시간이 흐르면 하루가 1프레임에 지나감 → 대화 중 날짜가 그대로면 시간이 멈춘 것
            int dayBefore = DayIndex(time);

            dlg.ResetPlayed();
            Check("대화 시작", dlg.Play(start));
            yield return Frames(1);
            Check("대화 중 게임 시간 정지 (IsEventPaused, timeScale 0)", time.IsEventPaused && Mathf.Approximately(Time.timeScale, 0f),
                $"timeScale {Time.timeScale}");

            yield return RealSeconds(0.25f);
            int vis = dlg.VisibleCharacterCount, len = dlg.CurrentTextLength;
            RefreshView();
            Check("글자가 하나씩 나오는 중", vis > 0 && vis < len, $"{vis}/{len}글자");
            Shot("d01_typing", _view.canvas);

            yield return RealSeconds(1.5f);
            RefreshView();
            Check("다 나와도 입력이 없으면 그 줄에서 멈춤", dlg.IsPlaying && dlg.CurrentLineIndex == 0 && dlg.IsLineFullyShown,
                $"{dlg.CurrentLineIndex + 1}번째 줄, {dlg.VisibleCharacterCount}/{dlg.CurrentTextLength}");
            Check("대화 중에는 날짜가 넘어가지 않음 (1.75초 대기)", DayIndex(time) == dayBefore, $"{dayBefore} → {DayIndex(time)}");

            // 한 줄씩: 출력 중 넘기기 = 한 번에 / 다 나온 뒤 넘기기 = 다음 줄
            bool revealOk = true, nextOk = true, colorOk = true, playerOk = false;
            bool shotRight = false, shotDim = false, shotPlayer = false, rightAppeared = false;
            int dimLines = 0, brightLines = 0;
            for (int guard = 0; guard < 60 && dlg.IsPlaying; guard++)
            {
                int index = dlg.CurrentLineIndex;
                if (!dlg.IsLineFullyShown)
                {
                    dlg.Advance(); // Enter·클릭과 같은 동작
                    if (dlg.CurrentLineIndex != index || !dlg.IsLineFullyShown) revealOk = false;
                }
                yield return Frames(1);
                RefreshView();

                var side = dlg.CurrentSide;
                bool hasRight = dlg.RightSpeakerName != null;
                if (side == Dlg.DialogueSide.Right) { rightAppeared = true; brightLines++; }
                bool expectDim = hasRight && side != Dlg.DialogueSide.Right;
                if (expectDim) dimLines++;
                Color expected = expectDim ? dlg.DimmedPortraitColor : Color.white;
                if (_view.rightPortrait.enabled && _view.rightPortrait.color != expected) colorOk = false;
                if (dlg.IsRightDimmed != expectDim) colorOk = false;

                string text = dlg.GetCurrentText();
                if (dlg.CurrentLine.text.Contains("{player}") && text.Contains(TestPlayerName) && !text.Contains("{player}")) playerOk = true;

                if (side == Dlg.DialogueSide.Right && !shotRight) { Shot("d02_right_speaking", _view.canvas); shotRight = true; }
                else if (expectDim && rightAppeared && !shotDim) { Shot("d03_protagonist_or_narration_dimmed", _view.canvas); shotDim = true; }
                if (dlg.CurrentLine.text.Contains("{player}") && !shotPlayer) { Shot("d04_player_name", _view.canvas); shotPlayer = true; }

                dlg.Advance();
                if (dlg.IsPlaying && dlg.CurrentLineIndex != index + 1) nextOk = false;
            }
            Check("출력 중에 넘기면 그 줄이 한 번에 다 나옴 (다음 줄로는 안 감)", revealOk);
            Check("다 나온 뒤 넘기면 다음 줄로", nextOk);
            Check("상대가 말할 때 초상화 밝게, 주인공·나레이션 차례에는 회색으로 그대로 남음", colorOk && dimLines > 0 && brightLines > 0,
                $"밝게 {brightLines}줄, 회색 {dimLines}줄");
            Check("{player}가 정한 이름으로 바뀜", playerOk);
            Check("마지막 줄을 넘기면 대화 끝", !dlg.IsPlaying);

            yield return Frames(2);
            Check("대화가 끝나면 시간이 다시 흐름", !time.IsEventPaused && Time.timeScale > 0f, $"timeScale {Time.timeScale}");
            int afterEnd = DayIndex(time);
            yield return Frames(10);
            Check("대화가 끝난 뒤 날짜가 다시 넘어감", DayIndex(time) > afterEnd, $"{afterEnd} → {DayIndex(time)}");
            RestoreSpeed();
            time.SetGameSpeed(GameSpeed.Pause); // 이후 확인 동안 마을 상태 고정 (대화 시간 정지와는 별개)
            yield return ClearStrayDialogues(dlg);
        }

        /// <summary>D-2. Auto 버튼: 다 나온 뒤 1초 후 자동으로 다음 줄</summary>
        private static IEnumerator DialogueAutoCheck(Dlg.DialogueEventManager dlg)
        {
            Section("D-2. Auto 버튼 (다 나온 뒤 1초 후 자동으로 다음 줄)");
            var data = Dlg.DialogueData.Create("test_auto", true, false,
                new Dlg.DialogueLine("이장님", "자, 그럼 마을을 한 바퀴 둘러볼까?"),
                new Dlg.DialogueLine("주인공", "네, 좋아요! {player}, 열심히 하겠습니다."),
                new Dlg.DialogueLine("꼬마", "이장님! 이 사람이 새로 온 사람이에요?"));

            var fullyAt = new List<double>();
            var shownAt = new List<double>();
            Action onFully = () => fullyAt.Add(EditorApplication.timeSinceStartup);
            Action<Dlg.DialogueLine, int, int> onShown = (l, i, n) => shownAt.Add(EditorApplication.timeSinceStartup);
            dlg.OnLineFullyShown += onFully;
            dlg.OnLineShown += onShown;

            dlg.Play(data);
            yield return Frames(1);
            _view.autoButton.onClick.Invoke(); // Auto 버튼 클릭
            Check("Auto 버튼 → Auto 켜짐", dlg.IsAutoMode);

            double startT = EditorApplication.timeSinceStartup;
            bool shot = false;
            while (dlg.IsPlaying && EditorApplication.timeSinceStartup - startT < 15)
            {
                if (!shot && dlg.IsLineFullyShown && dlg.CurrentLineIndex == 0)
                {
                    RefreshView();
                    Shot("d05_auto_on", _view.canvas);
                    shot = true;
                }
                yield return null;
            }

            dlg.OnLineFullyShown -= onFully;
            dlg.OnLineShown -= onShown;

            // 줄 i가 다 나온 시각 → 줄 i+1이 나온 시각
            var gaps = new List<double>();
            for (int i = 0; i < fullyAt.Count && i + 1 < shownAt.Count; i++) gaps.Add(shownAt[i + 1] - fullyAt[i]);
            Check("Auto: 입력 없이 끝까지 진행", !dlg.IsPlaying);
            Check("Auto: 다 나온 뒤 약 1초 후 다음 줄", gaps.Count == 2 && gaps.All(g => g >= 0.9 && g <= 1.5),
                string.Join(", ", gaps.Select(g => $"{g:F2}초")));

            _view.autoButton.onClick.Invoke();
            Check("Auto 버튼 다시 누르면 꺼짐", !dlg.IsAutoMode);
            yield return ClearStrayDialogues(dlg);
        }

        /// <summary>D-3. Skip 버튼: 대화를 한 번에 건너뜀</summary>
        private static IEnumerator DialogueSkipButtonCheck(Dlg.DialogueEventManager dlg, TimeManager time)
        {
            Section("D-3. Skip 버튼 (대화를 한 번에 건너뜀)");
            var data = Dlg.DialogueData.Create("test_skip", true, false,
                new Dlg.DialogueLine("주민", "이장님 말로는 이 마을에 병원이 없다던데요."),
                new Dlg.DialogueLine("주민", "아플 때마다 읍내까지 가야 해요."),
                new Dlg.DialogueLine("주인공", "곧 방법을 찾아볼게요."),
                new Dlg.DialogueLine("주민", "꼭 부탁드려요!"));
            bool ended = false;
            Action<Dlg.DialogueData> onEnd = d => { if (d == data) ended = true; };
            dlg.OnDialogueEnded += onEnd;

            dlg.Play(data);
            yield return Frames(1);
            dlg.Advance(); dlg.Advance(); // 두 번째 줄로
            yield return Frames(1);
            RefreshView();
            Shot("d06_before_skip", _view.canvas);

            _view.skipButton.onClick.Invoke(); // Skip 버튼 클릭
            yield return Frames(1);
            RefreshView();
            dlg.OnDialogueEnded -= onEnd;
            Check("Skip 버튼 → 남은 줄을 건너뛰고 대화 끝", ended && !dlg.IsPlaying);
            Check("Skip 후 대화창이 닫히고 시간 정지 풀림", !_view.panel.activeSelf && !time.IsEventPaused);
            Shot("d07_after_skip", _view.canvas);
            yield return ClearStrayDialogues(dlg);
        }

        /// <summary>D-4. 카툰 컷 → 대화</summary>
        private static IEnumerator ComicThenDialogueCheck(Dlg.DialogueEventManager dlg, Dlg.CutsceneManager cut, TimeManager time)
        {
            Section("D-4. 카툰 컷씬 → 대화 (필수 시설 설치 연출 예시)");
            var chief = LoadSprite("Assets/Image/이장.png");
            var kid = LoadSprite("Assets/Image/마을 꼬마.png");
            var couple = LoadSprite("Assets/Image/젊은 부부.png");
            var comic = Dlg.CutsceneData.CreateComic("test_comic_hospital",
                new Dlg.CutsceneShot(chief, "그날 아침, 마을에 첫 병원이 문을 열었다.") { pageRect = new Rect(0.02f, 0.5f, 0.47f, 0.48f) },
                new Dlg.CutsceneShot(couple, "젊은 부부는 아이를 안고 가장 먼저 달려왔다.") { pageRect = new Rect(0.51f, 0.5f, 0.47f, 0.48f) },
                new Dlg.CutsceneShot(kid, "\"이제 아파도 읍내까지 안 가도 돼요!\"", newPage: true) { pageRect = new Rect(0.2f, 0.06f, 0.6f, 0.9f), speaker = "꼬마" });
            var after = Dlg.DialogueData.Create("test_after_comic", true, false,
                new Dlg.DialogueLine("이장님", "{player} 자네 덕분에 마을에 병원이 생겼구먼."),
                new Dlg.DialogueLine("주인공", "다들 좋아하시니 다행이에요."));

            var order = new List<string>();
            Action<Dlg.CutsceneData> cStart = c => order.Add("컷씬 시작");
            Action<Dlg.CutsceneData> cEnd = c => order.Add("컷씬 끝");
            Action<Dlg.DialogueData> dStart = d => order.Add("대화 시작");
            cut.OnCutsceneStarted += cStart; cut.OnCutsceneEnded += cEnd; dlg.OnDialogueStarted += dStart;

            dlg.Play(after, comic);
            yield return Frames(1);
            Check("대화보다 카툰 컷이 먼저", cut.IsComic && dlg.IsWaitingForCutscene && !dlg.IsPlaying);
            Check("카툰 컷 중에도 시간 정지", time.IsEventPaused);

            cut.Next(); // 두 번째 컷 (같은 페이지)
            yield return Frames(1);
            RefreshView();
            Check("같은 페이지에 컷이 하나씩 붙음", cut.CurrentPageShots.Count == 2 && _view.comicPanels.Count == 2);
            Shot("d08_comic_page1", _view.canvas);

            cut.Next(); // 새 페이지
            yield return Frames(1);
            RefreshView();
            Check("새 페이지에서 앞 컷이 지워짐", cut.CurrentPageIndex == 1 && cut.CurrentPageShots.Count == 1 && _view.comicPanels.Count == 1);
            Shot("d09_comic_page2", _view.canvas);

            cut.Next(); // 컷씬 끝 → 대화
            yield return Frames(1);
            dlg.RevealCurrentLine();
            yield return Frames(1);
            RefreshView();
            Check("컷씬이 끝나면 바로 대화", dlg.IsPlaying && !cut.IsPlaying && string.Join(" → ", order) == "컷씬 시작 → 컷씬 끝 → 대화 시작",
                string.Join(" → ", order));
            Shot("d10_dialogue_after_comic", _view.canvas);

            cut.OnCutsceneStarted -= cStart; cut.OnCutsceneEnded -= cEnd; dlg.OnDialogueStarted -= dStart;
            dlg.Skip();
            yield return ClearStrayDialogues(dlg);
        }

        /// <summary>D-5. 일주일 스킵 도중 대화가 화면에 뜨면 그 자리에서 멈춤</summary>
        private static IEnumerator WeekSkipInterruptCheck(Dlg.DialogueEventManager dlg, TimeManager time)
        {
            Section("D-5. 일주일 스킵 도중 대화가 나오면 멈춤");
            var data = Dlg.DialogueData.Create("test_skip_event", true, false,
                new Dlg.DialogueLine("주민", "큰일 났어요! 밤사이 마을 길이 물에 잠겼어요!"));
            int days = 0, startDay = DayIndex(time);
            bool? interrupted = null;
            Action<int, int, int> onDate = (y, m, d) => { if (++days == 3) dlg.Play(data); };
            Action<bool> onEnd = i => interrupted = i;
            time.OnDateChanged += onDate;
            time.OnSkipEnded += onEnd;

            Check("스킵 시작", time.StartWeekSkip());
            double deadline = EditorApplication.timeSinceStartup + 15;
            while (time.IsSkipping && EditorApplication.timeSinceStartup < deadline) yield return null;
            if (time.IsSkipping) time.CancelSkip();
            time.OnDateChanged -= onDate;
            time.OnSkipEnded -= onEnd;

            dlg.RevealCurrentLine();
            yield return Frames(1);
            RefreshView();
            Check("3일째에 대화가 뜨자 스킵이 그 자리에서 멈춤", interrupted == true && DayIndex(time) - startDay == 3 && dlg.IsPlaying,
                $"{DayIndex(time) - startDay}일 진행, 중단={interrupted}");
            Check("남은 날은 지나가지 않고 시간 정지", time.IsEventPaused && !time.IsSkipping);
            Shot("d11_week_skip_stopped_by_dialogue", _view.canvas);

            int before = DayIndex(time);
            yield return RealSeconds(0.5f);
            Check("대화 중에는 스킵이 다시 시작되지 않음", DayIndex(time) == before && !time.CanSkip);
            dlg.Skip();
            yield return Frames(1);
            Check("대화를 닫으면 다시 스킵 가능", time.CanSkip);
        }

        /// <summary>날짜가 바뀌며 다른 트리거(해금 안내 등)가 띄운 대화를 정리</summary>
        private static IEnumerator ClearStrayDialogues(Dlg.DialogueEventManager dlg)
        {
            yield return Frames(1);
            if (dlg.IsBusy || dlg.QueueCount > 0)
            {
                Note($"(다른 트리거가 띄운 대화 정리: {(dlg.CurrentDialogue != null ? dlg.CurrentDialogue.dialogueId : "-")}, 대기 {dlg.QueueCount}개)");
                dlg.ClearAll();
            }
            Dlg.CutsceneManager.Instance?.ClearAll();
            if (dlg.IsAutoMode) dlg.SetAutoMode(false);
            yield return Frames(1);
            RefreshView();
        }

        // ───────────────────────── 임시 대화창 ─────────────────────────

        private static Sprite LoadSprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

        private static void SetPortrait(List<Dlg.DialogueCharacter> list, string name, Sprite sprite)
        {
            var c = list.FirstOrDefault(x => x != null && x.name == name);
            if (c == null) list.Add(new Dlg.DialogueCharacter(name, sprite));
            else c.portrait = sprite;
        }

        private static void BuildView()
        {
            var v = new TempDialogueView();
            v.root = new GameObject("[Playtest] TempDialogueCanvas");
            v.canvas = v.root.AddComponent<Canvas>();
            v.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            v.canvas.sortingOrder = 500;
            var scaler = v.root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            v.root.AddComponent<GraphicRaycaster>();
            var rootRt = v.root.GetComponent<RectTransform>();

            // 오른쪽 인물 초상화 (대화창보다 뒤)
            var portraitRt = NewRect("RightPortrait", rootRt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0));
            portraitRt.sizeDelta = new Vector2(520, 560);
            portraitRt.anchoredPosition = new Vector2(-150, 250);
            v.rightPortrait = portraitRt.gameObject.AddComponent<Image>();
            v.rightPortrait.preserveAspect = true;

            // 대화창
            var panelRt = NewRect("Panel", rootRt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0));
            panelRt.offsetMin = new Vector2(120, 30);
            panelRt.offsetMax = new Vector2(-120, 330);
            panelRt.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.08f, 0.13f, 0.92f);
            v.panel = panelRt.gameObject;

            var bodyRt = NewRect("Body", panelRt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            bodyRt.offsetMin = new Vector2(50, 30);
            bodyRt.offsetMax = new Vector2(-50, -50);
            v.bodyText = AddText(bodyRt, "", 44, TextAlignmentOptions.TopLeft, Color.white);

            v.skipButton = AddButton(panelRt, "Skip", new Vector2(-200, 16), () => Dlg.DialogueEventManager.Instance?.Skip());
            v.autoButton = AddButton(panelRt, "Auto", new Vector2(-30, 16), () => Dlg.DialogueEventManager.Instance?.ToggleAutoMode());
            v.autoLabel = v.autoButton.GetComponentInChildren<TextMeshProUGUI>();

            // 이름표
            v.namePlate = NewRect("NamePlate", rootRt, Vector2.zero, Vector2.zero, Vector2.zero);
            v.namePlate.sizeDelta = new Vector2(380, 70);
            v.namePlate.gameObject.AddComponent<Image>().color = new Color(0.95f, 0.78f, 0.35f, 1f);
            var nameRt = NewRect("Name", v.namePlate, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            nameRt.offsetMin = nameRt.offsetMax = Vector2.zero;
            v.nameText = AddText(nameRt, "", 38, TextAlignmentOptions.Center, new Color(0.15f, 0.1f, 0.05f));

            // 테스트 정보 (화면 왼쪽 위)
            var infoRt = NewRect("Info", rootRt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1));
            infoRt.sizeDelta = new Vector2(1180, 150);
            infoRt.anchoredPosition = new Vector2(20, -20);
            infoRt.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.7f);
            var infoTextRt = NewRect("InfoText", infoRt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            infoTextRt.offsetMin = new Vector2(18, 10);
            infoTextRt.offsetMax = new Vector2(-18, -10);
            v.infoText = AddText(infoTextRt, "", 26, TextAlignmentOptions.TopLeft, new Color(0.7f, 1f, 0.75f));

            // 카툰 컷 화면
            var comicRt = NewRect("Comic", rootRt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            comicRt.offsetMin = comicRt.offsetMax = Vector2.zero;
            comicRt.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.07f, 0.94f);
            v.comicRoot = comicRt.gameObject;
            v.comicPage = NewRect("Page", comicRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            v.comicPage.sizeDelta = new Vector2(1400, 660);
            v.comicPage.anchoredPosition = new Vector2(0, 40);
            v.comicPage.gameObject.AddComponent<Image>().color = new Color(0.97f, 0.95f, 0.9f);
            var capRt = NewRect("Caption", comicRt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
            capRt.sizeDelta = new Vector2(1500, 150);
            capRt.anchoredPosition = new Vector2(0, 30);
            capRt.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.95f);
            var capTextRt = NewRect("CaptionText", capRt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            capTextRt.offsetMin = new Vector2(30, 10);
            capTextRt.offsetMax = new Vector2(-30, -10);
            v.comicCaption = AddText(capTextRt, "", 40, TextAlignmentOptions.Center, new Color(0.1f, 0.1f, 0.1f));
            v.comicRoot.SetActive(false);

            infoRt.SetAsLastSibling(); // 정보는 맨 위
            _view = v;
            RefreshView();
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            return rt;
        }

        private static TextMeshProUGUI AddText(RectTransform rt, string text, float size, TextAlignmentOptions align, Color color)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.enableWordWrapping = true;
            return t;
        }

        private static Button AddButton(RectTransform parent, string label, Vector2 topRightOffset, Action onClick)
        {
            var rt = NewRect(label + "Button", parent, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0)); // 대화창 오른쪽 아래
            rt.sizeDelta = new Vector2(150, 56);
            rt.anchoredPosition = topRightOffset;
            rt.gameObject.AddComponent<Image>().color = new Color(0.3f, 0.34f, 0.45f, 1f);
            var b = rt.gameObject.AddComponent<Button>();
            b.onClick.AddListener(() => onClick());
            var labelRt = NewRect("Label", rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
            AddText(labelRt, label, 30, TextAlignmentOptions.Center, Color.white);
            return b;
        }

        // ─── 이벤트 연결 (실제 UI가 할 일과 같은 방식) ───

        private static Action<Dlg.DialogueData> _hStart, _hEnd;
        private static Action<Dlg.DialogueLine, int, int> _hLine;
        private static Action<string> _hVisible;
        private static Action<bool> _hAuto;
        private static Action<Dlg.CutsceneData> _hcStart, _hcEnd;
        private static Action<int> _hcPage;
        private static Action<Dlg.CutsceneShot, int, int> _hcShot;

        private static void Subscribe(Dlg.DialogueEventManager dlg, Dlg.CutsceneManager cut)
        {
            _hStart = d => RefreshView();
            _hEnd = d => RefreshView();
            _hLine = (l, i, n) => RefreshView();
            _hVisible = t => RefreshView();
            _hAuto = on => RefreshView();
            dlg.OnDialogueStarted += _hStart;
            dlg.OnDialogueEnded += _hEnd;
            dlg.OnLineShown += _hLine;
            dlg.OnVisibleTextChanged += _hVisible;
            dlg.OnAutoModeChanged += _hAuto;

            _hcStart = c => { ClearComic(); RefreshView(); };
            _hcEnd = c => { ClearComic(); RefreshView(); };
            _hcPage = p => ClearComic();
            _hcShot = (s, i, n) => { AddComicPanel(s); RefreshView(); };
            cut.OnCutsceneStarted += _hcStart;
            cut.OnCutsceneEnded += _hcEnd;
            cut.OnPageStarted += _hcPage;
            cut.OnShotShown += _hcShot;
        }

        private static void Unsubscribe(Dlg.DialogueEventManager dlg, Dlg.CutsceneManager cut)
        {
            dlg.OnDialogueStarted -= _hStart;
            dlg.OnDialogueEnded -= _hEnd;
            dlg.OnLineShown -= _hLine;
            dlg.OnVisibleTextChanged -= _hVisible;
            dlg.OnAutoModeChanged -= _hAuto;
            cut.OnCutsceneStarted -= _hcStart;
            cut.OnCutsceneEnded -= _hcEnd;
            cut.OnPageStarted -= _hcPage;
            cut.OnShotShown -= _hcShot;
        }

        private static void ClearComic()
        {
            if (_view == null) return;
            foreach (var p in _view.comicPanels) if (p != null) UnityEngine.Object.Destroy(p);
            _view.comicPanels.Clear();
        }

        private static void AddComicPanel(Dlg.CutsceneShot shot)
        {
            if (_view == null) return;
            var r = shot.pageRect;
            var rt = NewRect("ComicPanel", _view.comicPage, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(0.5f, 0.5f));
            rt.offsetMin = new Vector2(8, 8);
            rt.offsetMax = new Vector2(-8, -8);
            rt.gameObject.AddComponent<Image>().color = Color.black; // 컷 테두리
            var inner = NewRect("Picture", rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            inner.offsetMin = new Vector2(6, 6);
            inner.offsetMax = new Vector2(-6, -6);
            inner.gameObject.AddComponent<Image>().color = new Color(1f, 0.98f, 0.92f);
            var pic = NewRect("Sprite", inner, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            pic.offsetMin = pic.offsetMax = Vector2.zero;
            var img = pic.gameObject.AddComponent<Image>();
            img.sprite = shot.image;
            img.preserveAspect = true;
            img.enabled = shot.image != null;
            _view.comicPanels.Add(rt.gameObject);
        }

        /// <summary>현재 상태를 임시 대화창에 반영 (실제 UI가 이벤트에서 할 일)</summary>
        private static void RefreshView()
        {
            var v = _view;
            var dlg = Dlg.DialogueEventManager.Instance;
            var cut = Dlg.CutsceneManager.Instance;
            var time = TimeManager.Instance;
            if (v == null || dlg == null) return;

            bool comic = cut != null && cut.IsComic;
            v.comicRoot.SetActive(comic);
            if (comic)
            {
                string sp = cut.GetCurrentSpeakerName();
                v.comicCaption.text = (string.IsNullOrEmpty(sp) ? "" : $"<b>{sp}</b>  ") + cut.GetCurrentCaption();
            }

            bool playing = dlg.IsPlaying;
            v.panel.SetActive(playing);
            v.namePlate.gameObject.SetActive(false);
            v.rightPortrait.enabled = false;

            if (playing)
            {
                v.bodyText.text = dlg.GetCurrentText();
                v.bodyText.maxVisibleCharacters = dlg.VisibleCharacterCount;

                string speaker = dlg.GetCurrentSpeakerName();
                if (!string.IsNullOrEmpty(speaker))
                {
                    v.namePlate.gameObject.SetActive(true);
                    v.nameText.text = speaker;
                    bool isRight = dlg.CurrentSide == Dlg.DialogueSide.Right;
                    v.namePlate.anchoredPosition = isRight ? new Vector2(1920 - 140 - 380, 300) : new Vector2(140, 300);
                }

                if (dlg.RightPortrait != null)
                {
                    v.rightPortrait.enabled = true;
                    v.rightPortrait.sprite = dlg.RightPortrait;
                    v.rightPortrait.color = dlg.RightPortraitColor;
                }
            }
            v.autoLabel.text = dlg.IsAutoMode ? "Auto ON" : "Auto";
            v.autoButton.GetComponent<Image>().color = dlg.IsAutoMode ? new Color(0.25f, 0.6f, 0.35f) : new Color(0.3f, 0.34f, 0.45f);

            // 테스트 정보
            string side = !playing ? "-" : dlg.CurrentSide switch
            {
                Dlg.DialogueSide.Left => "주인공",
                Dlg.DialogueSide.Right => "오른쪽 인물",
                _ => "나레이션"
            };
            string right = dlg.RightSpeakerName == null ? "없음" : $"{dlg.RightSpeakerName} ({(dlg.IsRightDimmed ? "회색" : "밝게")})";
            string skip = time == null ? "-" : time.IsSkipping ? $"진행 중 {time.SkipDaysDone}/{time.SkipDays}일" : "멈춤";
            v.infoText.text =
                $"[테스트 정보] {time?.CurrentYear}년 {time?.CurrentMonth}월 {time?.CurrentDay}일   게임 시간 정지: {(time != null && time.IsEventPaused ? "예" : "아니오")} (timeScale {Time.timeScale:0.#})\n" +
                (comic
                    ? $"카툰 컷 {cut.CurrentShotIndex + 1}/{cut.CurrentCutscene.shots.Count}  (페이지 {cut.CurrentPageIndex + 1}, 이 페이지 컷 {cut.CurrentPageShots.Count}개)\n"
                    : playing
                        ? $"대화 {dlg.CurrentDialogue.dialogueId}  {dlg.CurrentLineIndex + 1}/{dlg.CurrentDialogue.lines.Count}줄   글자 {dlg.VisibleCharacterCount}/{dlg.CurrentTextLength}\n"
                        : "대화 없음\n") +
                $"말하는 쪽: {side}   오른쪽 초상화: {right}\n" +
                $"Auto: {(dlg.IsAutoMode ? "켜짐" : "꺼짐")}   일주일 스킵: {skip}";
        }
    }
}

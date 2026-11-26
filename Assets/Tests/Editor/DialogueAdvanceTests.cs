using System.Collections.Generic;
using NUnit.Framework;
using InfraTown.EventSystem;
using InfraTown.TimeSystem;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace InfraTown.Tests
{
    /// <summary>
    /// 대화 넘기기 (사용자 기획 2026-11-19):
    /// 글자가 하나씩 나오고, Enter·클릭(Advance)은 출력 중이면 한 번에 띄우고 다 나왔으면 다음 줄.
    /// 기본은 다 나와도 입력이 없으면 멈춰 있음. Skip = 대화 전체 건너뛰기, Auto = 다 나온 뒤 1초 후 자동 다음 줄.
    /// </summary>
    public class DialogueAdvanceTests
    {
        private TestWorld _world;
        private DialogueEventManager _dialogue;
        private readonly List<string> _ended = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _dialogue = _world.CreateManager<DialogueEventManager>();
            _dialogue.OnLineShown += (l, i, n) => { };
            _dialogue.OnDialogueEnded += d => _ended.Add(d.dialogueId);
            _dialogue.CharactersPerSecond = 10f; // 0.1초에 한 글자
            _ended.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            DialogueEventManager.PauseMenuOpenOverrideForTests = null;
            _world.Dispose();
        }

        // ─── 일시정지 메뉴가 열려 있는 동안 (2026-11-26) ───

        [Test]
        public void 일시정지_메뉴가_열려_있으면_글자_출력과_Auto가_멈춘다()
        {
            bool menuOpen = false;
            DialogueEventManager.PauseMenuOpenOverrideForTests = () => menuOpen;
            _dialogue.SetAutoMode(true);
            PlayTwoLines();
            _dialogue.Tick(0.2f);
            Assert.AreEqual("안녕", _dialogue.GetVisibleText());

            menuOpen = true;
            Assert.IsTrue(_dialogue.IsHeldByMenu);
            _dialogue.Tick(5f);
            Assert.AreEqual("안녕", _dialogue.GetVisibleText(), "메뉴 뒤에서 글자가 더 나오지 않음");
            Assert.AreEqual(0, _dialogue.CurrentLineIndex, "Auto로 다음 줄로 넘어가지 않음");

            menuOpen = false;
            _dialogue.Tick(0.3f);
            Assert.AreEqual("안녕하세요", _dialogue.GetVisibleText(), "메뉴를 닫으면 그 자리에서 이어짐");
        }

        [Test]
        public void 실제_일시정지_메뉴_상태를_읽는다()
        {
            var prop = typeof(InfraTown.UI.GamePauseMenu).GetProperty("IsOpen");
            try
            {
                prop.SetValue(null, true);
                Assert.IsTrue(_dialogue.IsHeldByMenu);
                prop.SetValue(null, false);
                Assert.IsFalse(_dialogue.IsHeldByMenu);
            }
            finally { prop.SetValue(null, false); }
        }

        private void PlayTwoLines() => _dialogue.Play(DialogueData.Create("talk", true, false,
            new DialogueLine("주민", "안녕하세요"),   // 5글자
            new DialogueLine("비서", "반가워요")));    // 4글자

        [Test]
        public void 게임_씬_대화창의_Auto와_Skip이_동작하고_오른쪽_초상화를_유지한다()
        {
            const string scenePath = "Assets/Scenes/2Park 1.unity";
            var existingScene = SceneManager.GetSceneByPath(scenePath);
            bool wasLoaded = existingScene.isLoaded;
            var scene = wasLoaded ? existingScene : EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var source = System.Array.Find(scene.GetRootGameObjects(), root => root.name == "DialogueCanvas");
                var canvas = Object.Instantiate(source);
                SceneManager.MoveGameObjectToScene(canvas, preview);
                var view = canvas.GetComponent<DialogueWindow>();
                var window = TestWorld.GetField<GameObject>(view, "window");
                var portrait = TestWorld.GetField<Image>(view, "portrait");
                var skip = TestWorld.GetField<Button>(view, "skipButton");
                var auto = TestWorld.GetField<Button>(view, "autoButton");
                var autoLabel = TestWorld.GetField<TMP_Text>(view, "autoLabel");
                var autoOffSprite = auto.image.sprite;
                var autoOnSprite = TestWorld.GetField<Sprite>(view, "autoOnSprite");
                Assert.IsNotNull(autoOffSprite);
                Assert.IsNotNull(autoOnSprite);
                Assert.AreNotEqual(autoOffSprite, autoOnSprite);
                TestWorld.SetField(view, "manager", _dialogue);
                TestWorld.Invoke(view, "Awake");
                TestWorld.Invoke(view, "OnEnable");

                var intro = AssetDatabase.LoadAssetAtPath<DialogueData>("Assets/Data/Dialouge/Start.asset");
                _dialogue.CharactersPerSecond = 0f;
                _dialogue.Play(intro);
                Assert.IsTrue(window.activeSelf);
                Assert.IsTrue(portrait.gameObject.activeSelf);
                Assert.AreEqual(intro.lines[1].portrait, portrait.sprite);
                Assert.AreEqual(_dialogue.DimmedPortraitColor, portrait.color);

                auto.onClick.Invoke();
                Assert.AreEqual("Auto ON", autoLabel.text);
                Assert.AreEqual(autoOnSprite, auto.image.sprite);
                _dialogue.Tick(_dialogue.AutoAdvanceDelay);
                Assert.AreEqual(1, _dialogue.CurrentLineIndex);
                Assert.AreEqual(Color.white, portrait.color);

                auto.onClick.Invoke();
                Assert.AreEqual("Auto", autoLabel.text);
                Assert.AreEqual(autoOffSprite, auto.image.sprite);
                skip.onClick.Invoke();
                Assert.IsFalse(_dialogue.IsPlaying);
                Assert.IsFalse(window.activeSelf);
                TestWorld.Invoke(view, "OnDisable");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
                if (!wasLoaded)
                    EditorSceneManager.CloseScene(scene, removeScene: !existingScene.IsValid());
            }
        }

        [Test]
        public void 글자가_시간에_따라_하나씩_나온다()
        {
            var visible = new List<string>();
            _dialogue.OnVisibleTextChanged += t => visible.Add(t);
            PlayTwoLines();

            Assert.AreEqual(0, _dialogue.VisibleCharacterCount);
            Assert.AreEqual(5, _dialogue.CurrentTextLength);
            _dialogue.Tick(0.25f);
            Assert.AreEqual("안녕", _dialogue.GetVisibleText());
            Assert.IsFalse(_dialogue.IsLineFullyShown);
            _dialogue.Tick(0.3f);
            Assert.AreEqual("안녕하세요", _dialogue.GetVisibleText());
            Assert.IsTrue(_dialogue.IsLineFullyShown);
            CollectionAssert.AreEqual(new[] { "", "안녕", "안녕하세요" }, visible);
        }

        [Test]
        public void 출력_중에_넘기면_문장이_한번에_다_나온다()
        {
            int fully = 0;
            _dialogue.OnLineFullyShown += () => fully++;
            PlayTwoLines();
            _dialogue.Tick(0.1f);

            _dialogue.Advance();

            Assert.AreEqual(0, _dialogue.CurrentLineIndex, "다음 줄로는 아직 안 넘어감");
            Assert.IsTrue(_dialogue.IsLineFullyShown);
            Assert.AreEqual("안녕하세요", _dialogue.GetVisibleText());
            Assert.AreEqual(1, fully);
        }

        [Test]
        public void 다_나온_뒤에_넘기면_다음_줄로_간다()
        {
            PlayTwoLines();
            _dialogue.Advance(); // 한 번에 띄움
            _dialogue.Advance(); // 다음 줄

            Assert.AreEqual(1, _dialogue.CurrentLineIndex);
            Assert.AreEqual(0, _dialogue.VisibleCharacterCount, "다음 줄은 처음부터 다시 출력");

            _dialogue.Advance();
            _dialogue.Advance();
            Assert.IsFalse(_dialogue.IsPlaying);
            CollectionAssert.AreEqual(new[] { "talk" }, _ended);
        }

        [Test]
        public void 기본은_다_나와도_입력이_없으면_그_줄에서_멈춰_있다()
        {
            PlayTwoLines();
            _dialogue.Tick(1f);
            Assert.IsTrue(_dialogue.IsLineFullyShown);

            _dialogue.Tick(100f);

            Assert.AreEqual(0, _dialogue.CurrentLineIndex);
            Assert.IsTrue(_dialogue.IsPlaying);
            Assert.AreEqual(-1f, _dialogue.AutoAdvanceRemaining);
        }

        [Test]
        public void Skip은_대화를_한번에_끝낸다()
        {
            PlayTwoLines();
            _dialogue.Tick(0.1f);
            _dialogue.Skip();

            Assert.IsFalse(_dialogue.IsPlaying);
            CollectionAssert.AreEqual(new[] { "talk" }, _ended);
        }

        [Test]
        public void Auto는_다_나온_뒤_1초_후_다음_줄로_간다()
        {
            bool? autoState = null;
            _dialogue.OnAutoModeChanged += on => autoState = on;
            PlayTwoLines();
            _dialogue.ToggleAutoMode();
            Assert.IsTrue(_dialogue.IsAutoMode);
            Assert.AreEqual(true, autoState);

            _dialogue.Tick(0.5f);   // 5글자 다 나옴
            Assert.IsTrue(_dialogue.IsLineFullyShown);
            Assert.AreEqual(0, _dialogue.CurrentLineIndex);

            _dialogue.Tick(0.9f);
            Assert.AreEqual(0, _dialogue.CurrentLineIndex, "1초가 지나기 전에는 기다림");
            Assert.That(_dialogue.AutoAdvanceRemaining, Is.EqualTo(0.1f).Within(0.001f));

            _dialogue.Tick(0.2f);
            Assert.AreEqual(1, _dialogue.CurrentLineIndex, "1초 뒤 자동으로 다음 줄");

            _dialogue.Tick(0.4f);   // 4글자
            _dialogue.Tick(1f);
            Assert.IsFalse(_dialogue.IsPlaying, "마지막 줄도 자동으로 끝남");
        }

        [Test]
        public void Auto_중에_한번에_띄우면_그때부터_1초를_센다()
        {
            PlayTwoLines();
            _dialogue.SetAutoMode(true);
            _dialogue.Tick(0.1f);
            _dialogue.Advance();      // 한 번에 띄움
            _dialogue.Tick(0.9f);
            Assert.AreEqual(0, _dialogue.CurrentLineIndex);
            _dialogue.Tick(0.11f);
            Assert.AreEqual(1, _dialogue.CurrentLineIndex);
        }

        [Test]
        public void Auto를_끄면_다시_입력을_기다린다()
        {
            PlayTwoLines();
            _dialogue.SetAutoMode(true);
            _dialogue.Tick(0.5f);
            _dialogue.ToggleAutoMode();
            Assert.IsFalse(_dialogue.IsAutoMode);

            _dialogue.Tick(5f);
            Assert.AreEqual(0, _dialogue.CurrentLineIndex);
        }

        [Test]
        public void Auto는_다음_대화에도_유지된다()
        {
            _dialogue.SetAutoMode(true);
            _dialogue.Play(DialogueData.Create("a", true, false, new DialogueLine("주민", "가")));
            _dialogue.Play(DialogueData.Create("b", true, false, new DialogueLine("주민", "나")));
            _dialogue.Tick(0.1f);
            _dialogue.Tick(1f);
            Assert.AreEqual("b", _dialogue.CurrentDialogue.dialogueId);
            _dialogue.Tick(0.1f);
            _dialogue.Tick(1f);
            Assert.IsFalse(_dialogue.IsPlaying);
            CollectionAssert.AreEqual(new[] { "a", "b" }, _ended);
        }

        [Test]
        public void 글자_속도가_0이면_한번에_나온다()
        {
            _dialogue.CharactersPerSecond = 0f;
            PlayTwoLines();
            Assert.IsTrue(_dialogue.IsLineFullyShown);
            Assert.AreEqual("안녕하세요", _dialogue.GetVisibleText());
        }

        [Test]
        public void 주인공_이름도_글자_출력에_들어간다()
        {
            InfraTown.Core.PlayerProfile.TrySetPlayerName("민수", out _);
            _dialogue.Play(DialogueData.Create("n", true, false, new DialogueLine("비서", "{player}님")));
            Assert.AreEqual(3, _dialogue.CurrentTextLength);
            _dialogue.Advance();
            Assert.AreEqual("민수님", _dialogue.GetVisibleText());
        }

        [Test]
        public void 대화_중_게임시간이_멈춰도_글자는_나온다()
        {
            var time = _world.CreateManager<TimeManager>();
            PlayTwoLines();
            Assert.Greater(time.PauseLockCount, 0);
            _dialogue.Tick(0.5f); // Update는 실제 시간(unscaledDeltaTime)으로 Tick을 부름
            Assert.IsTrue(_dialogue.IsLineFullyShown);
        }
    }
}

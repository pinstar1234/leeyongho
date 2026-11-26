using System.Collections.Generic;
using NUnit.Framework;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.EventSystem;
using InfraTown.Population;
using InfraTown.TimeSystem;

namespace InfraTown.Tests
{
    /// <summary>
    /// 주인공 이름: 시작할 때 정하고 대화·컷씬에서 {player}로 나옴
    /// </summary>
    public class PlayerNameTests
    {
        private TestWorld _world;
        private DialogueEventManager _dialogue;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _dialogue = _world.CreateManager<DialogueEventManager>();
            _dialogue.OnLineShown += (l, i, n) => { };
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void 이름은_공백과_특수기호를_빼고_저장한다()
        {
            Assert.IsTrue(PlayerProfile.TrySetPlayerName("  <b>철수</b>{x}  ", out _));
            Assert.AreEqual("b철수/bx", PlayerProfile.PlayerName);
            Assert.IsTrue(PlayerProfile.HasName);
        }

        [Test]
        public void 빈_이름이나_너무_긴_이름은_거절한다()
        {
            Assert.IsFalse(PlayerProfile.TrySetPlayerName("   ", out var e1));
            Assert.IsNotEmpty(e1);
            Assert.IsFalse(PlayerProfile.TrySetPlayerName(new string('가', PlayerProfile.MaxNameLength + 1), out var e2));
            Assert.IsNotEmpty(e2);
            Assert.IsFalse(PlayerProfile.HasName);
            Assert.AreEqual(PlayerProfile.DefaultName, PlayerProfile.PlayerName, "안 정하면 기본 이름");
        }

        [Test]
        public void 이름이_바뀌면_이벤트가_발생한다()
        {
            string got = null;
            System.Action<string> handler = n => got = n;
            PlayerProfile.OnPlayerNameChanged += handler;
            try
            {
                PlayerProfile.TrySetPlayerName("영희", out _);
                Assert.AreEqual("영희", got);
            }
            finally { PlayerProfile.OnPlayerNameChanged -= handler; }
        }

        [Test]
        public void 대사와_화자의_player가_정한_이름으로_바뀐다()
        {
            PlayerProfile.TrySetPlayerName("민수", out _);
            _dialogue.Play(DialogueData.Create("d", true, false,
                new DialogueLine("비서", "{player}님, 반가워요!", "{이름}님 안녕!"),
                new DialogueLine("{player}", "잘 부탁해요"),
                new DialogueLine("주인공", "저도요")));

            Assert.AreEqual("{player}님, 반가워요!".Replace("{player}", "민수"), _dialogue.GetCurrentText());
            Assert.AreEqual("비서", _dialogue.GetCurrentSpeakerName());
            Assert.AreEqual("민수", _dialogue.LeftSpeakerName, "왼쪽 이름표는 정한 이름");

            _dialogue.UseEasyText = true;
            Assert.AreEqual("민수님 안녕!", _dialogue.GetCurrentText());

            _dialogue.Next();
            Assert.AreEqual(DialogueSide.Left, _dialogue.CurrentSide, "{player} 화자는 주인공 = 왼쪽");
            Assert.AreEqual("민수", _dialogue.GetCurrentSpeakerName());

            _dialogue.Next();
            Assert.AreEqual(DialogueSide.Left, _dialogue.CurrentSide, "기존 '주인공' 화자도 주인공");
            Assert.AreEqual("민수", _dialogue.GetCurrentSpeakerName());
        }

        [Test]
        public void 정한_이름을_화자로_써도_주인공으로_본다()
        {
            PlayerProfile.TrySetPlayerName("민수", out _);
            Assert.IsTrue(_dialogue.IsProtagonist("민수"));
            Assert.IsFalse(_dialogue.IsProtagonist("비서"));
        }

        [Test]
        public void 컷씬_자막에도_이름이_들어간다()
        {
            PlayerProfile.TrySetPlayerName("민수", out _);
            var cutscene = _world.CreateManager<CutsceneManager>();
            cutscene.OnShotShown += (s, i, n) => { };
            cutscene.Play(CutsceneData.CreateComic("c",
                new CutsceneShot { speaker = "{player}", caption = "{player}의 첫 출근 날" }));

            Assert.AreEqual("민수의 첫 출근 날", cutscene.GetCurrentCaption());
            Assert.AreEqual("민수", cutscene.GetCurrentSpeakerName());
        }
    }

    /// <summary>
    /// 게임 시작: 이름 입력 대기 → 오프닝 → 튜토리얼
    /// </summary>
    public class PlayerNameStartTests
    {
        private TestWorld _world;
        private DialogueEventManager _dialogue;
        private DialogueTriggers _triggers;
        private TutorialDialogues _tutorial;
        private TimeManager _time;
        private readonly List<string> _started = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            var buildings = _world.CreateManager<BuildingManager>();
            TestWorld.SetField(buildings, "buildingDatabase", new List<BuildingData>());
            _world.CreateManager<PopulationManager>();
            _time = _world.CreateManager<TimeManager>();
            _dialogue = _world.CreateManager<DialogueEventManager>();
            _triggers = _dialogue.gameObject.AddComponent<DialogueTriggers>();
            TestWorld.Invoke(_triggers, "Awake");
            _tutorial = _dialogue.gameObject.AddComponent<TutorialDialogues>();
            TestWorld.Invoke(_tutorial, "Awake");
            TestWorld.SetField(_triggers, "introDialogue", DialogueData.Create("arrive", true, true, new DialogueLine("주민", "어서 와요, {player}씨")));

            _started.Clear();
            _dialogue.OnDialogueStarted += d => _started.Add(d.dialogueId);
        }

        [TearDown]
        public void TearDown()
        {
            TestWorld.Invoke(_tutorial, "OnDestroy");
            TestWorld.Invoke(_triggers, "OnDestroy");
            _world.Dispose();
        }

        private void StartGame()
        {
            TestWorld.Invoke(_triggers, "Start");
            TestWorld.Invoke(_triggers, "Update");
        }

        [Test]
        public void 이름_입력창이_있으면_이름을_정할_때까지_오프닝을_기다린다()
        {
            int requested = 0;
            _triggers.OnPlayerNameRequested += () => requested++;
            StartGame();

            Assert.AreEqual(1, requested);
            Assert.IsTrue(_triggers.IsWaitingForPlayerName);
            Assert.IsEmpty(_started);
            Assert.Greater(_time.PauseLockCount, 0, "기다리는 동안 시간 정지");

            Assert.IsFalse(_triggers.SubmitPlayerName("  ", out var error), "빈 이름은 거절");
            Assert.IsNotEmpty(error);
            Assert.IsTrue(_triggers.IsWaitingForPlayerName);

            Assert.IsTrue(_triggers.SubmitPlayerName("민수"));
            Assert.IsFalse(_triggers.IsWaitingForPlayerName);
            CollectionAssert.AreEqual(new[] { "arrive" }, _started);
            Assert.AreEqual("어서 와요, 민수씨", _dialogue.GetCurrentText());
        }

        [Test]
        public void 이름_입력창이_없거나_이미_정했으면_바로_시작한다()
        {
            StartGame();
            Assert.IsFalse(_triggers.IsWaitingForPlayerName);
            CollectionAssert.AreEqual(new[] { "arrive" }, _started);
        }

        [Test]
        public void Start만으로는_시작하지_않고_첫_Update에서_시작한다()
        {
            TestWorld.Invoke(_triggers, "Start");
            Assert.IsEmpty(_started, "UI가 Start에서 구독할 시간을 줌");
            TestWorld.Invoke(_triggers, "Update");
            Assert.IsNotEmpty(_started);
        }

        [Test]
        public void 오프닝_대화가_끝나면_튜토리얼_첫_단계가_이어진다()
        {
            StartGame();
            Assert.IsTrue(_tutorial.IsActive);
            Assert.AreEqual(1, _tutorial.CurrentWeek);
            _dialogue.Next(); // 마을 도착 대화 끝
            CollectionAssert.AreEqual(new[] { "arrive", "tutorial_guide_welcome" }, _started);
        }
    }

}

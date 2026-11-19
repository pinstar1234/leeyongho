using System.Collections.Generic;
using NUnit.Framework;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.EventSystem;
using InfraTown.Population;
using InfraTown.TimeSystem;

namespace InfraTown.Tests
{
    public class DialogueManagerTests
    {
        private TestWorld _world;
        private DialogueEventManager _dialogue;
        private readonly List<string> _shown = new List<string>();
        private readonly List<string> _ended = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _dialogue = _world.CreateManager<DialogueEventManager>();
            _shown.Clear();
            _ended.Clear();
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        /// <summary>UI가 붙어 있는 상황 (구독자 있음)</summary>
        private void AttachViewer()
        {
            _dialogue.OnLineShown += (line, i, n) => _shown.Add($"{line.speaker}:{_dialogue.GetCurrentText()}");
            _dialogue.OnDialogueEnded += d => _ended.Add(d.dialogueId);
        }

        private static DialogueData Make(string id, bool playOnce = true, bool pause = true, int lines = 2)
        {
            var arr = new DialogueLine[lines];
            for (int i = 0; i < lines; i++) arr[i] = new DialogueLine("이장님", $"{id}-{i + 1}", $"{id}-쉬운{i + 1}");
            return DialogueData.Create(id, pause, playOnce, arr);
        }

        [Test]
        public void 첫_줄을_보여주고_Next로_넘기다가_끝난다()
        {
            AttachViewer();
            Assert.IsTrue(_dialogue.Play(Make("intro")));
            Assert.IsTrue(_dialogue.IsPlaying);
            CollectionAssert.AreEqual(new[] { "이장님:intro-1" }, _shown);

            _dialogue.Next();
            Assert.AreEqual("intro-2", _dialogue.GetCurrentText());
            _dialogue.Next();

            Assert.IsFalse(_dialogue.IsPlaying);
            CollectionAssert.AreEqual(new[] { "intro" }, _ended);
        }

        [Test]
        public void 재생_중에_들어온_대화는_대기했다가_이어서_재생된다()
        {
            AttachViewer();
            _dialogue.Play(Make("a", lines: 1));
            _dialogue.Play(Make("b", lines: 1));
            Assert.AreEqual(1, _dialogue.QueueCount);

            _dialogue.Next();

            Assert.AreEqual("b", _dialogue.CurrentDialogue.dialogueId);
            _dialogue.Next();
            CollectionAssert.AreEqual(new[] { "a", "b" }, _ended);
        }

        [Test]
        public void 한번만_재생하는_대화는_다시_재생되지_않는다()
        {
            AttachViewer();
            var d = Make("once", lines: 1);
            Assert.IsTrue(_dialogue.Play(d));
            _dialogue.Next();
            Assert.IsFalse(_dialogue.Play(d));
            Assert.IsTrue(_dialogue.HasPlayed("once"));
        }

        [Test]
        public void 대화_중에는_시간이_멈추고_끝나면_다시_흐른다()
        {
            var time = _world.CreateManager<TimeManager>();
            AttachViewer();
            _dialogue.Play(Make("pause", lines: 1));
            Assert.IsTrue(time.IsEventPaused);

            _dialogue.Next();
            Assert.IsFalse(time.IsEventPaused);
        }

        [Test]
        public void 시간정지_꺼진_대화는_시간을_멈추지_않는다()
        {
            var time = _world.CreateManager<TimeManager>();
            AttachViewer();
            _dialogue.Play(Make("nopause", pause: false));
            Assert.IsFalse(time.IsEventPaused);
        }

        [Test]
        public void 표시할_UI가_없으면_멈추지_않고_바로_끝낸다()
        {
            var time = _world.CreateManager<TimeManager>();
            bool ended = false;
            _dialogue.OnDialogueEnded += d => ended = true; // 종료만 듣는 구독자는 'UI'로 보지 않음

            Assert.IsTrue(_dialogue.Play(Make("headless")));

            Assert.IsFalse(_dialogue.IsPlaying);
            Assert.IsFalse(time.IsEventPaused);
            Assert.IsTrue(ended);
        }

        [Test]
        public void 쉬운_모드면_쉬운_문장을_쓴다()
        {
            AttachViewer();
            _dialogue.UseEasyText = true;
            _dialogue.Play(Make("easy", lines: 1));
            Assert.AreEqual("easy-쉬운1", _dialogue.GetCurrentText());
        }

        [Test]
        public void Skip은_현재_대화를_끝내고_ClearAll은_대기열까지_비운다()
        {
            AttachViewer();
            _dialogue.Play(Make("x", lines: 3));
            _dialogue.Play(Make("y"));
            _dialogue.Play(Make("z"));

            _dialogue.Skip();
            Assert.AreEqual("y", _dialogue.CurrentDialogue.dialogueId);

            _dialogue.ClearAll();
            Assert.IsFalse(_dialogue.IsPlaying);
            Assert.AreEqual(0, _dialogue.QueueCount);
        }

        [Test]
        public void 빈_대화는_재생하지_않는다()
        {
            AttachViewer();
            Assert.IsFalse(_dialogue.Play(Make("empty", lines: 0)));
            Assert.IsFalse(_dialogue.Play((DialogueData)null));
        }

        [Test]
        public void 이전_API로_시간을_멈추고_재개할_수_있다()
        {
            var time = _world.CreateManager<TimeManager>();
            _dialogue.StartDialogueEvent();
            Assert.IsTrue(time.IsEventPaused);
            _dialogue.EndDialogueEvent();
            Assert.IsFalse(time.IsEventPaused);
        }
    }

    public class DialogueTriggerTests
    {
        private TestWorld _world;
        private DialogueEventManager _dialogue;
        private DialogueTriggers _triggers;
        private BuildingManager _buildings;
        private PopulationManager _population;
        private readonly List<string> _played = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _buildings = _world.CreateManager<BuildingManager>();
            _population = _world.CreateManager<PopulationManager>();
            _dialogue = _world.CreateManager<DialogueEventManager>();
            _triggers = _dialogue.gameObject.AddComponent<DialogueTriggers>();
            _played.Clear();
            _dialogue.OnDialogueStarted += d => _played.Add(d.dialogueId);
            _dialogue.OnLineShown += (l, i, n) => { };
            _dialogue.OnDialogueEnded += d => { };

            var t1 = _world.CreateData(BuildingType.Housing, id: "house", name: "주택", capacity: 100);
            t1.unlockTier = 1; t1.unlockMessage = "살 곳이 부족해요.";
            var t2 = _world.CreateData(BuildingType.Facility, id: "store", name: "편의점", requiredPopulation: 50);
            t2.unlockTier = 2; t2.unlockMessage = "휴지 하나 사려면 읍내까지 가야 해요.";
            TestWorld.SetField(_buildings, "buildingDatabase", new List<BuildingData> { t1, t2 });
            _population.RecalculateCapacity(new List<BuildingInstance> { _world.CreateBuilding(t1, 0, 0) });
        }

        [TearDown]
        public void TearDown()
        {
            TestWorld.Invoke(_triggers, "OnDestroy");
            _world.Dispose();
        }

        private void Start() => TestWorld.Invoke(_triggers, "Start");

        [Test]
        public void 시작하면_Tier1_해금_스토리를_보여준다()
        {
            Start();
            Assert.AreEqual(1, _played.Count);
            Assert.AreEqual("살 곳이 부족해요.", _dialogue.GetCurrentText());
        }

        [Test]
        public void 인구가_늘어_새_건물이_해금되면_그_스토리를_보여준다()
        {
            Start();
            _dialogue.ClearAll();
            _played.Clear();

            _population.AddPopulation(50);

            Assert.AreEqual(1, _played.Count);
            Assert.AreEqual("휴지 하나 사려면 읍내까지 가야 해요.", _dialogue.GetCurrentText());
            _dialogue.Next();
            StringAssert.Contains("편의점", _dialogue.GetCurrentText());
        }

        [Test]
        public void 같은_해금은_다시_보여주지_않는다()
        {
            Start();
            _dialogue.Skip(); // 시작 대화를 넘겨 다음 대화가 바로 시작되게
            _population.AddPopulation(50);
            _population.AddPopulation(10);
            Assert.AreEqual(2, _played.Count); // 시작 1 + Tier2 1
        }

        [Test]
        public void 나쁨이_경고_일수가_되면_안내원이_경고한다()
        {
            var outcome = _world.CreateManager<GameOutcomeManager>();
            Start();
            _dialogue.ClearAll();
            _played.Clear();

            var raise = typeof(GameOutcomeManager).GetField("OnBadStreakChanged",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var handler = (System.Action<int, int>)raise.GetValue(outcome);
            handler?.Invoke(9, 30);
            Assert.AreEqual(0, _played.Count);
            handler?.Invoke(10, 30);

            Assert.AreEqual(1, _played.Count);
            Assert.AreEqual("안내원", _dialogue.CurrentLine.speaker);
            StringAssert.Contains("20일", _dialogue.GetCurrentText());
        }
    }
}

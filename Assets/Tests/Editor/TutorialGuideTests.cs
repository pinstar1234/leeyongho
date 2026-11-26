using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using InfraTown.Building;
using InfraTown.EventSystem;
using InfraTown.TimeSystem;

namespace InfraTown.Tests
{
    /// <summary>
    /// 튜토리얼 (사용자 기획 2026-11-26): 1주차에 기능을 하나씩 단계별로 — 설명 → 그 행동을 해야 다음 단계.
    /// 튜토리얼 동안 시간 정지, 지금 알려 주는 건물·버튼·건물 클릭만 쓸 수 있다.
    /// </summary>
    public class TutorialTests
    {
        private TestWorld _world;
        private DialogueEventManager _dialogue;
        private TutorialDialogues _tutorial;
        private TimeManager _time;
        private BuildingManager _buildings;
        private GameObject _prefab;
        private BuildingData _road, _house, _pole, _hall;
        private readonly List<string> _started = new List<string>();
        private readonly List<string> _finished = new List<string>();
        private readonly List<GameObject> _ui = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _time = _world.CreateManager<TimeManager>();
            _buildings = _world.CreateManager<BuildingManager>(callStart: true);
            _buildings.RequireRoadAccess = false;
            _dialogue = _world.CreateManager<DialogueEventManager>();
            _dialogue.OnLineShown += (l, i, n) => { }; // 대화창이 있는 상황 (대화가 줄마다 멈춤)
            _tutorial = _world.CreateManager<TutorialDialogues>();
            _prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _road = Data(BuildingType.Infrastructure, "road");
            _pole = Data(BuildingType.Infrastructure, "power_pole");
            _pole.isPowerPole = true;
            _house = Data(BuildingType.Housing, "house");
            _hall = Data(BuildingType.Facility, "village_hall");
            TestWorld.SetField(_buildings, "buildingDatabase", new List<BuildingData> { _road, _pole, _house, _hall });
            _started.Clear(); _finished.Clear();
            _dialogue.OnDialogueStarted += d => _started.Add(d.dialogueId);
            _tutorial.OnStepFinished += s => _finished.Add(s.stepId);
        }

        [TearDown]
        public void TearDown()
        {
            _buildings.CancelBuildingMode();
            foreach (var b in Object.FindObjectsOfType<BuildingInstance>()) Object.DestroyImmediate(b.gameObject);
            TestWorld.Invoke(_tutorial, "OnDestroy");
            foreach (var go in _ui) if (go != null) Object.DestroyImmediate(go);
            _ui.Clear();
            Object.DestroyImmediate(_prefab);
            _world.Dispose();
        }

        private BuildingData Data(BuildingType category, string id)
        {
            var d = _world.CreateData(category, id: id, name: id);
            d.buildingPrefab = _prefab;
            return d;
        }

        /// <summary>지금 대화 하나만 끝까지 넘김 (그다음 단계 대화는 그대로 둠)</summary>
        private void FinishTalk()
        {
            var current = _dialogue.CurrentDialogue;
            for (int i = 0; i < 100 && _dialogue.IsPlaying && _dialogue.CurrentDialogue == current; i++) _dialogue.Next();
        }

        private BuildingInstance Place(BuildingData data, int x, int y)
        {
            _buildings.SelectBuildingToBuild(data);
            _buildings.TryPlaceArea(new Vector2Int(x, y), new Vector2Int(x, y));
            return _buildings.GetBuildingAt(new Vector2Int(x, y));
        }

        private string Step => _tutorial.CurrentStep?.stepId;

        private Button MakeButton(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            _ui.Add(go);
            var b = go.GetComponent<Button>();
            b.targetGraphic = go.GetComponent<Image>();
            return b;
        }

        [Test]
        public void 기본_단계는_인사부터_마무리까지_순서대로()
        {
            CollectionAssert.AreEqual(new[] { "guide_welcome", "guide_road", "guide_house", "guide_power_pole", "guide_overview_open",
                "guide_overview_close", "guide_speed", "guide_demolish", "guide_finish" }, _tutorial.Steps.Select(s => s.stepId));
        }

        [Test]
        public void 튜토리얼_동안은_시간이_멈추고_끝나면_흐른다()
        {
            _tutorial.BeginTutorial();
            Assert.IsTrue(_time.IsEventPaused, "1주차에 튜토리얼을 다 하도록 시간 정지");
            Assert.AreEqual(1, _tutorial.CurrentWeek);
            FinishTalk(); // 인사 (대사도 시간을 멈추므로 닫고 확인)
            FinishTalk(); // 도로 설명
            Assert.IsTrue(_tutorial.IsWaitingForAction);
            Assert.IsTrue(_time.IsEventPaused, "대사가 없어도 튜토리얼 동안은 정지");
            _tutorial.SkipTutorial();
            Assert.IsFalse(_time.IsEventPaused);
            Assert.IsTrue(_tutorial.IsCompleted);
        }

        [Test]
        public void 설명만_있는_단계는_대화가_끝나면_다음_단계()
        {
            _tutorial.BeginTutorial();
            Assert.AreEqual("guide_welcome", Step);
            Assert.AreEqual("tutorial_guide_welcome", _started.Last());
            FinishTalk();
            Assert.AreEqual("guide_road", Step);
            CollectionAssert.AreEqual(new[] { "guide_welcome" }, _finished);
        }

        [Test]
        public void 행동_단계는_설명이_끝나도_그_행동을_할_때까지_기다린다()
        {
            _tutorial.BeginTutorial();
            FinishTalk(); // 인사
            Assert.AreEqual("guide_road", Step);
            Assert.IsFalse(_tutorial.IsWaitingForAction, "설명 중");
            FinishTalk();
            Assert.IsTrue(_tutorial.IsWaitingForAction);
            Assert.AreEqual("guide_road", Step, "설명만 끝나서는 넘어가지 않음");

            Assert.IsNotNull(Place(_road, 5, 5));
            Assert.AreEqual("guide_house", Step, "도로를 깔면 다음");
        }

        [Test]
        public void 설명_중이나_다른_건물은_고를_수_없다()
        {
            _tutorial.BeginTutorial();
            FinishTalk(); // 인사 → 도로 단계 설명 중
            _buildings.SelectBuildingToBuild(_road);
            Assert.IsFalse(_buildings.IsPlacingBuilding, "설명을 다 듣기 전에는 못 고름");
            FinishTalk();

            _buildings.SelectBuildingToBuild(_house);
            Assert.IsFalse(_buildings.IsPlacingBuilding, "도로 단계에서 주택은 못 고름");
            StringAssert.Contains("road", _buildings.LastPlacementError ?? "", "지금 해야 할 일(도로)을 알려 줌");
            _buildings.SelectBuildingToBuild(_hall);
            Assert.IsFalse(_buildings.IsPlacingBuilding);

            _buildings.SelectBuildingToBuild(_road);
            Assert.IsTrue(_buildings.IsPlacingBuilding, "알려 주는 건물은 고를 수 있음");
        }

        [Test]
        public void 철거_단계에서만_건물을_눌러_정보창을_열_수_있다()
        {
            _tutorial.BeginTutorial();
            Assert.IsTrue(TutorialDialogues.BlocksBuildingClick);
            for (int i = 0; i < 20 && Step != "guide_demolish"; i++)
            {
                FinishTalk();
                switch (Step)
                {
                    case "guide_road": Place(_road, 1, 1); break;
                    case "guide_house": Place(_house, 2, 1); break;
                    case "guide_power_pole": Place(_pole, 3, 1); break;
                }
            }
            Assert.AreEqual("guide_demolish", Step, "현황판·속도 버튼이 없는 씬이면 그 단계는 막히지 않고 건너뜀");
            Assert.IsTrue(TutorialDialogues.BlocksBuildingClick, "철거 설명 중에는 아직 막힘");
            FinishTalk();
            Assert.IsFalse(TutorialDialogues.BlocksBuildingClick, "철거 단계 행동 중에는 건물 클릭 가능");

            var house = _buildings.GetBuildingAt(new Vector2Int(2, 1));
            TestWorld.Invoke(house, "OnDestroy"); // 철거 알림 (UI 철거 버튼의 Destroy와 같은 경로)
            Assert.AreEqual("guide_finish", Step);
            FinishTalk();
            Assert.IsTrue(_tutorial.IsCompleted);
            Assert.IsFalse(TutorialDialogues.BlocksBuildingClick, "끝나면 잠금 해제");
            Assert.IsFalse(_time.IsEventPaused);
        }

        [Test]
        public void 버튼은_지금_단계_버튼만_눌리고_강조되며_끝나면_되돌린다()
        {
            var allowed = MakeButton("AllowedButton");
            var other = MakeButton("OtherButton");
            var steps = TestWorld.GetField<List<TutorialStep>>(_tutorial, "guideSteps");
            steps.Clear();
            steps.Add(TutorialStep.Do("press", TutorialGoal.ChangeSpeed, "AllowedButton"));
            steps[0].dialogue = DialogueData.Create("press_talk", true, false, new DialogueLine("비서", "이 버튼을 눌러 보세요"));
            steps.Add(TutorialStep.Talk("guide_finish"));

            _tutorial.BeginTutorial();
            Assert.IsFalse(allowed.interactable, "설명 중에는 모두 잠금");
            Assert.IsFalse(other.interactable);
            FinishTalk();
            Assert.IsTrue(_tutorial.IsWaitingForAction);
            Assert.IsTrue(allowed.interactable, "지금 단계 버튼만 눌림");
            Assert.IsFalse(other.interactable, "다른 버튼은 잠금");
            CollectionAssert.Contains(_tutorial.HighlightedObjects(), allowed.gameObject, "누를 버튼 강조");
            Assert.IsNotNull(allowed.GetComponent<Outline>());

            _time.SetGameSpeed(GameSpeed.Fast); // 그 버튼이 하는 일 (속도 변경)
            Assert.AreEqual("guide_finish", Step);
            FinishTalk();
            Assert.IsTrue(_tutorial.IsCompleted);
            Assert.IsTrue(other.interactable, "끝나면 튜토리얼이 잠근 버튼을 되돌림");
            Assert.IsNull(allowed.GetComponent<Outline>(), "강조도 지움");
            Assert.AreEqual(0, _tutorial.LockedCount);
        }

        [Test]
        public void 처음부터_꺼져_있던_버튼은_끝나도_켜지_않는다()
        {
            var disabled = MakeButton("DisabledButton");
            disabled.interactable = false;
            _tutorial.BeginTutorial();
            _tutorial.SkipTutorial();
            Assert.IsFalse(disabled.interactable);
        }

        [Test]
        public void 연출용_이벤트에_단계가_온다()
        {
            var waiting = new List<string>();
            var shown = new List<string>();
            _tutorial.OnWaitingForAction += s => waiting.Add(s.stepId);
            _tutorial.OnStepStarted += s => shown.Add(s.stepId);
            _tutorial.BeginTutorial();
            FinishTalk(); FinishTalk();
            CollectionAssert.AreEqual(new[] { "guide_welcome", "guide_road" }, shown);
            CollectionAssert.AreEqual(new[] { "guide_road" }, waiting);
            StringAssert.Contains("road", _tutorial.CurrentHint());
        }

        [Test]
        public void 대화_에셋을_넣으면_기본_문장_대신_그것을_쓴다()
        {
            var custom = DialogueData.Create("custom_welcome", true, false, new DialogueLine("비서", "안녕하세요"));
            _tutorial.Steps[0].dialogue = custom;
            _tutorial.BeginTutorial();
            Assert.AreEqual("custom_welcome", _started.Last());
            Object.DestroyImmediate(custom);
        }

        [Test]
        public void 튜토리얼을_끄면_아무것도_안_나온다()
        {
            _tutorial.TutorialEnabled = false;
            _tutorial.BeginTutorial();
            Assert.IsFalse(_tutorial.IsActive);
            Assert.IsEmpty(_started);
            Assert.IsFalse(_time.IsEventPaused);
        }

        [Test]
        public void 모든_기본_단계에_비서_문장이_있다()
        {
            foreach (var step in _tutorial.Steps)
            {
                var lines = _tutorial.DefaultLines(step.stepId);
                Assert.IsNotNull(lines, step.stepId);
                Assert.IsTrue(lines.Any(l => l.speaker == "비서"), step.stepId);
                Assert.IsTrue(lines.All(l => !string.IsNullOrEmpty(l.easyText)), $"{step.stepId}: 쉬운 문장");
            }
            StringAssert.Contains("ESC", string.Join(" ", _tutorial.DefaultLines("guide_finish").Select(l => l.text)), "저장은 마무리에서 안내");
            StringAssert.Contains("도로", string.Join(" ", _tutorial.DefaultLines("guide_road").Select(l => l.text)));
        }

        [Test]
        public void 저장하면_하던_단계부터_이어서_한다()
        {
            _tutorial.BeginTutorial();
            FinishTalk(); FinishTalk();
            Place(_road, 5, 5); // → 주택 단계
            var state = _tutorial.GetSaveState();
            Assert.AreEqual(2, state.currentStep);
            CollectionAssert.AreEquivalent(new[] { "guide_welcome", "guide_road" }, state.triggeredStepIds);

            _tutorial.SkipTutorial();
            _dialogue.ClearAll();
            _started.Clear();
            _tutorial.ApplySaveState(state);
            Assert.IsTrue(_tutorial.IsActive);
            Assert.AreEqual("guide_house", Step);
            Assert.AreEqual("tutorial_guide_house", _started.Last(), "그 단계 설명부터 다시");
            Assert.IsTrue(_time.IsEventPaused);
        }

        [Test]
        public void 예전_주차_방식_저장을_불러와도_막히지_않는다()
        {
            _tutorial.ApplySaveState(new InfraTown.Save.TutorialSaveState { active = true, currentWeek = 4, triggeredStepIds = { "day1_welcome", "first_building" } });
            Assert.IsTrue(_tutorial.IsActive);
            Assert.AreEqual("guide_welcome", Step, "새 단계 기준으로 처음부터");
        }
    }

    /// <summary>해금 안내는 비서가 말함 (2026-11-26 버그: 오프닝에 이어 이장님이 계속 말하는 것처럼 보임)</summary>
    public class UnlockMessageSpeakerTests
    {
        [Test]
        public void 처음_해금_안내는_비서가_말한다()
        {
            var world = new TestWorld();
            try
            {
                var buildings = world.CreateManager<BuildingManager>();
                var house = world.CreateData(BuildingType.Housing, id: "house", name: "주택");
                house.unlockMessage = "주민이 살 집을 지을 수 있어요.";
                TestWorld.SetField(buildings, "buildingDatabase", new List<BuildingData> { house });
                world.CreateManager<InfraTown.Population.PopulationManager>();
                world.CreateManager<TimeManager>();
                var dialogue = world.CreateManager<DialogueEventManager>();
                var speakers = new List<string>();
                dialogue.OnLineShown += (l, i, n) => speakers.Add($"{dialogue.CurrentDialogue.dialogueId}:{l.speaker}");
                var triggers = dialogue.gameObject.AddComponent<DialogueTriggers>();
                TestWorld.Invoke(triggers, "Awake");
                TestWorld.SetField(triggers, "introDialogue", DialogueData.Create("Start", true, false, new DialogueLine("이장님", "어서 오게")));
                TestWorld.Invoke(triggers, "Start");
                var tutorial = TutorialDialogues.Instance;
                if (tutorial != null) tutorial.TutorialEnabled = false;
                TestWorld.Invoke(triggers, "Update");
                for (int i = 0; i < 20 && dialogue.IsPlaying; i++) dialogue.Next();

                Assert.Contains("Start:이장님", speakers);
                var unlock = speakers.Where(s => s.StartsWith("unlock_initial")).ToList();
                Assert.IsNotEmpty(unlock);
                Assert.IsTrue(unlock.All(s => s.EndsWith(":비서")), string.Join(", ", unlock));
                Assert.AreEqual("비서", triggers.UnlockMessageSpeaker);
                TestWorld.Invoke(triggers, "OnDestroy");
            }
            finally { world.Dispose(); }
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.EventSystem;
using InfraTown.Population;
using InfraTown.Services;
using InfraTown.TimeSystem;

namespace InfraTown.Tests
{
    /// <summary>
    /// 대화 화자 배치: 주인공은 항상 왼쪽, 말하는 다른 인물은 오른쪽
    /// </summary>
    public class DialogueSpeakerLayoutTests
    {
        private TestWorld _world;
        private DialogueEventManager _dialogue;
        private Sprite _hero, _resident, _secretary, _heroAngry;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _dialogue = _world.CreateManager<DialogueEventManager>();
            _dialogue.OnLineShown += (l, i, n) => { };
            _hero = MakeSprite("hero"); _resident = MakeSprite("resident"); _secretary = MakeSprite("secretary"); _heroAngry = MakeSprite("heroAngry");
            _dialogue.ProtagonistName = "주인공";
            _dialogue.ProtagonistPortrait = _hero;
            TestWorld.SetField(_dialogue, "characters", new List<DialogueCharacter>
            {
                new DialogueCharacter("주민", _resident), new DialogueCharacter("비서", _secretary)
            });
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private static Sprite MakeSprite(string name)
        {
            var s = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero);
            s.name = name;
            return s;
        }

        [Test]
        public void 주인공은_왼쪽_다른_인물은_오른쪽에_말하는_사람이_보인다()
        {
            _dialogue.Play(DialogueData.Create("talk", true, false,
                new DialogueLine("주인공", "안녕하세요"),
                new DialogueLine("주민", "어서 오세요"),
                new DialogueLine("비서", "일정이 있어요"),
                new DialogueLine("주인공", "알겠어요")));

            // 1줄: 주인공이 말함 → 왼쪽 강조, 오른쪽에는 다음에 말할 주민을 미리 세워 둠
            Assert.AreEqual(DialogueSide.Left, _dialogue.CurrentSide);
            Assert.AreEqual("주인공", _dialogue.LeftSpeakerName);
            Assert.AreEqual(_hero, _dialogue.LeftPortrait);
            Assert.AreEqual("주민", _dialogue.RightSpeakerName);

            _dialogue.Next();
            Assert.AreEqual(DialogueSide.Right, _dialogue.CurrentSide);
            Assert.AreEqual(_resident, _dialogue.RightPortrait);

            _dialogue.Next();
            Assert.AreEqual("비서", _dialogue.RightSpeakerName, "오른쪽은 지금 말하는 인물로 바뀜");
            Assert.AreEqual(_secretary, _dialogue.RightPortrait);

            _dialogue.Next();
            Assert.AreEqual(DialogueSide.Left, _dialogue.CurrentSide);
            Assert.AreEqual("비서", _dialogue.RightSpeakerName, "주인공이 말할 때 오른쪽은 마지막 상대 유지");
        }

        [Test]
        public void 줄에_넣은_초상화가_우선이고_이름없으면_해설이다()
        {
            _dialogue.Play(DialogueData.Create("talk2", true, false,
                new DialogueLine("주인공", "화났어", null, _heroAngry),
                new DialogueLine("", "다음 날 아침...")));

            Assert.AreEqual(_heroAngry, _dialogue.LeftPortrait);
            Assert.IsNull(_dialogue.RightSpeakerName, "말할 상대가 없으면 오른쪽 비움");
            _dialogue.Next();
            Assert.AreEqual(DialogueSide.Narration, _dialogue.CurrentSide);
        }

        [Test]
        public void 자리를_직접_지정하면_그대로_따른다()
        {
            var line = new DialogueLine("주민", "...") { side = DialogueSide.Left };
            Assert.AreEqual(DialogueSide.Left, _dialogue.GetSide(line));
            Assert.AreEqual(DialogueSide.Right, _dialogue.GetSide(new DialogueLine("처음 보는 사람", "...")));
        }
    }

    /// <summary>
    /// 컷씬: 카툰 컷(페이지·자동 넘김), 게임 화면 연출(카메라 이동), 대화 전 컷씬
    /// </summary>
    public class CutsceneTests
    {
        private TestWorld _world;
        private CutsceneManager _cutscene;
        private DialogueEventManager _dialogue;
        private readonly List<string> _log = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _cutscene = _world.CreateManager<CutsceneManager>();
            _dialogue = _world.CreateManager<DialogueEventManager>();
            _log.Clear();
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private void AttachViewers()
        {
            _cutscene.OnCutsceneStarted += c => _log.Add($"컷씬시작:{c.cutsceneId}");
            _cutscene.OnPageStarted += p => _log.Add($"페이지:{p}");
            _cutscene.OnShotShown += (s, i, n) => _log.Add($"컷:{s.caption}");
            _cutscene.OnCutsceneEnded += c => _log.Add($"컷씬끝:{c.cutsceneId}");
            _dialogue.OnDialogueStarted += d => _log.Add($"대화시작:{d.dialogueId}");
            _dialogue.OnLineShown += (l, i, n) => _log.Add($"대사:{l.text}");
            _dialogue.OnDialogueEnded += d => _log.Add($"대화끝:{d.dialogueId}");
        }

        private static CutsceneData Comic(string id) => CutsceneData.Create(id, CutsceneStyle.Comic,
            new CutsceneShot(null, "컷1"),
            new CutsceneShot(null, "컷2"),
            new CutsceneShot(null, "컷3", newPage: true));

        [Test]
        public void 카툰_컷은_페이지에_하나씩_붙고_새페이지에서_비워진다()
        {
            AttachViewers();
            _cutscene.Play(Comic("comic"));
            Assert.AreEqual(1, _cutscene.CurrentPageShots.Count);
            _cutscene.Next();
            Assert.AreEqual(2, _cutscene.CurrentPageShots.Count, "같은 페이지에 컷이 쌓임");
            _cutscene.Next();
            Assert.AreEqual(1, _cutscene.CurrentPageShots.Count, "새 페이지");
            Assert.AreEqual(1, _cutscene.CurrentPageIndex);
            _cutscene.Next();

            Assert.IsFalse(_cutscene.IsPlaying);
            CollectionAssert.AreEqual(new[] { "컷씬시작:comic", "페이지:0", "컷:컷1", "컷:컷2", "페이지:1", "컷:컷3", "컷씬끝:comic" }, _log);
        }

        [Test]
        public void 자동_넘김_시간이_지나면_다음_컷으로_간다()
        {
            AttachViewers();
            var data = CutsceneData.Create("auto", CutsceneStyle.Comic,
                new CutsceneShot(null, "a", 2f), new CutsceneShot(null, "b", 0f));
            _cutscene.Play(data);

            _cutscene.Tick(1.5f);
            Assert.AreEqual(0, _cutscene.CurrentShotIndex);
            _cutscene.Tick(0.6f);
            Assert.AreEqual(1, _cutscene.CurrentShotIndex);
            _cutscene.Tick(100f);
            Assert.AreEqual(1, _cutscene.CurrentShotIndex, "0초면 클릭할 때까지 기다림");
        }

        [Test]
        public void 대화_전에_컷씬을_먼저_보여주고_끝나면_대화가_나온다()
        {
            AttachViewers();
            var dialogue = DialogueData.Create("installed", true, true, new DialogueLine("이장님", "병원이 생겼어요"));
            _dialogue.Play(dialogue, Comic("hospital_comic"));

            Assert.IsTrue(_cutscene.IsPlaying);
            Assert.IsFalse(_dialogue.IsPlaying);
            Assert.IsTrue(_dialogue.IsWaitingForCutscene);

            _cutscene.Skip();

            Assert.IsTrue(_dialogue.IsPlaying, "건너뛰어도 대화는 나옴");
            CollectionAssert.AreEqual(new[] { "컷씬시작:hospital_comic", "페이지:0", "컷:컷1", "컷씬끝:hospital_comic", "대화시작:installed", "대사:병원이 생겼어요" }, _log);
        }

        [Test]
        public void 대화_에셋의_cutsceneBefore도_먼저_나온다()
        {
            AttachViewers();
            var dialogue = DialogueData.Create("d", true, false, new DialogueLine("주민", "hi"));
            dialogue.cutsceneBefore = Comic("own");
            _dialogue.Play(dialogue);
            Assert.AreEqual("own", _cutscene.CurrentCutscene.cutsceneId);
        }

        [Test]
        public void 컷씬_중에_들어온_대화는_기다렸다가_순서대로_나온다()
        {
            AttachViewers();
            _dialogue.Play(DialogueData.Create("first", true, false, new DialogueLine("주민", "1")), Comic("c"));
            _dialogue.Play(DialogueData.Create("second", true, false, new DialogueLine("주민", "2")));
            Assert.AreEqual(1, _dialogue.QueueCount);

            _cutscene.Skip();
            Assert.AreEqual("first", _dialogue.CurrentDialogue.dialogueId);
            _dialogue.Next();
            Assert.AreEqual("second", _dialogue.CurrentDialogue.dialogueId);
        }

        [Test]
        public void 컷씬_중에는_시간이_멈추고_월드_클릭이_막힌다()
        {
            var time = _world.CreateManager<TimeManager>();
            AttachViewers();
            _cutscene.Play(Comic("c"));
            Assert.Greater(time.PauseLockCount, 0);
            Assert.IsTrue(PointerGuard.IsPointerOverUI(), "컷씬 넘기는 클릭이 건설로 새지 않음");

            _cutscene.Skip();
            Assert.AreEqual(0, time.PauseLockCount);
        }

        [Test]
        public void 표시할_UI가_없으면_바로_끝내고_대화로_넘어간다()
        {
            bool finished = false;
            Assert.IsTrue(_cutscene.Play(Comic("c"), () => finished = true));
            Assert.IsTrue(finished);
            Assert.IsFalse(_cutscene.IsPlaying);
        }

        [Test]
        public void 한번만_보는_컷씬은_다시_안나오고_대화는_바로_나온다()
        {
            AttachViewers();
            var comic = Comic("once");
            comic.playOnce = true;
            _cutscene.Play(comic);
            _cutscene.Skip();
            _log.Clear();

            _dialogue.Play(DialogueData.Create("d", true, false, new DialogueLine("주민", "hi")), comic);
            Assert.IsFalse(_cutscene.IsPlaying);
            Assert.IsTrue(_dialogue.IsPlaying);
        }

        [Test]
        public void 건너뛰기_불가_컷씬은_Skip이_무시된다()
        {
            AttachViewers();
            var comic = Comic("c");
            comic.skippable = false;
            _cutscene.Play(comic);
            _cutscene.Skip();
            Assert.IsTrue(_cutscene.IsPlaying);
        }

        [Test]
        public void 게임화면_연출은_카메라를_지점으로_옮기고_끝나면_되돌린다()
        {
            var cam = new GameObject("CutsceneCam").AddComponent<Camera>();
            try
            {
                cam.transform.position = new Vector3(0, 30, 0);
                TestWorld.SetField(_cutscene, "targetCamera", cam);
                var point = new GameObject("Point").AddComponent<CutsceneCameraPoint>();
                point.PointId = "village";
                point.transform.position = new Vector3(50, 10, 20);
                TestWorld.Invoke(point, "OnEnable");

                AttachViewers();
                var data = CutsceneData.Create("opening", CutsceneStyle.Scene,
                    new CutsceneShot { caption = "마을로 가는 길", cameraPointId = "village", cameraMoveSeconds = 2f });
                _cutscene.Play(data);

                _cutscene.Tick(1f);
                Assert.IsTrue(_cutscene.IsCameraMoving);
                Assert.That(cam.transform.position.x, Is.InRange(1f, 49f), "움직이는 중");
                _cutscene.Tick(1.5f);
                Assert.AreEqual(new Vector3(50, 10, 20), cam.transform.position);

                _cutscene.Next();
                Assert.AreEqual(new Vector3(0, 30, 0), cam.transform.position, "restoreCamera 기본값이면 되돌림");

                // 마을 도착 위치에 남기기
                data.restoreCamera = false;
                _cutscene.Play(data);
                _cutscene.Next();
                Assert.AreEqual(new Vector3(50, 10, 20), cam.transform.position);

                TestWorld.Invoke(point, "OnDisable");
                Object.DestroyImmediate(point.gameObject);
            }
            finally { Object.DestroyImmediate(cam.gameObject); }
        }

        [Test]
        public void 장면이_시작되면_씬_이벤트가_실행된다()
        {
            var ev = new GameObject("Bus").AddComponent<CutsceneSceneEvent>();
            try
            {
                ev.EventId = "bus_arrive";
                int called = 0;
                ev.OnTriggered.AddListener(() => called++);
                TestWorld.Invoke(ev, "OnEnable");

                AttachViewers();
                _cutscene.Play(CutsceneData.Create("o", CutsceneStyle.Scene, new CutsceneShot { sceneEventId = "bus_arrive" }));
                Assert.AreEqual(1, called);
                TestWorld.Invoke(ev, "OnDisable");
            }
            finally { Object.DestroyImmediate(ev.gameObject); }
        }
    }

    /// <summary>
    /// 트리거: 오프닝 컷씬 → 마을 도착 대화, 필수 시설 설치 컷씬 → 대화
    /// </summary>
    public class CutsceneTriggerTests
    {
        private TestWorld _world;
        private DialogueEventManager _dialogue;
        private CutsceneManager _cutscene;
        private DialogueTriggers _triggers;
        private BuildingManager _buildings;
        private readonly List<string> _log = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _buildings = _world.CreateManager<BuildingManager>();
            _world.CreateManager<PopulationManager>();
            _world.CreateManager<ServiceAccessibilityManager>();
            _cutscene = _world.CreateManager<CutsceneManager>();
            _dialogue = _world.CreateManager<DialogueEventManager>();
            _triggers = _dialogue.gameObject.AddComponent<DialogueTriggers>();
            TestWorld.Invoke(_triggers, "Awake");
            TestWorld.SetField(_buildings, "buildingDatabase", new List<BuildingData>());
            TestWorld.SetField(_triggers, "showInitialUnlockStories", false);
            _log.Clear();
            _cutscene.OnCutsceneStarted += c => _log.Add($"컷씬:{c.cutsceneId}");
            _dialogue.OnDialogueStarted += d => _log.Add($"대화:{d.dialogueId}");
        }

        [TearDown]
        public void TearDown()
        {
            TestWorld.Invoke(_triggers, "OnDestroy");
            _world.Dispose();
        }

        [Test]
        public void 시작하면_오프닝_컷씬_후_마을_도착_대화가_나온다()
        {
            TestWorld.SetField(_triggers, "openingCutscene", CutsceneData.Create("opening", CutsceneStyle.Scene, new CutsceneShot { caption = "버스" }));
            TestWorld.SetField(_triggers, "introDialogue", DialogueData.Create("arrive", true, true, new DialogueLine("주민", "어서 와요")));
            TestWorld.Invoke(_triggers, "Start");

            CollectionAssert.AreEqual(new[] { "컷씬:opening" }, _log);
            _cutscene.Next();
            CollectionAssert.AreEqual(new[] { "컷씬:opening", "대화:arrive" }, _log);
        }

        [Test]
        public void 필수_시설을_처음_지으면_카툰_컷씬_후_대화가_나온다()
        {
            TestWorld.Invoke(_triggers, "Start");
            var entries = TestWorld.GetField<List<EssentialFacilityDialogue>>(_triggers, "essentialDialogues");
            entries.Find(e => e.service == ServiceType.Hospital).installedCutscene =
                CutsceneData.Create("hospital_comic", CutsceneStyle.Comic, new CutsceneShot(null, "구급차"));

            var hospital = _world.CreateBuilding(_world.CreateData(BuildingType.Essential, id: "hospital", name: "병원"), 0, 0);
            TestWorld.Invoke(_triggers, "HandleBuildingPlaced", hospital);

            CollectionAssert.AreEqual(new[] { "컷씬:hospital_comic" }, _log);
            _cutscene.Next();
            CollectionAssert.AreEqual(new[] { "컷씬:hospital_comic", "대화:installed_Hospital" }, _log);
        }
    }
}

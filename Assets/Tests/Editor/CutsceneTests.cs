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
    /// 대화 화자 배치: 주인공은 이미지 없음, 오른쪽 인물은 주인공·나레이션 차례에 회색으로 남음
    /// </summary>
    public class DialogueSpeakerLayoutTests
    {
        private TestWorld _world;
        private DialogueEventManager _dialogue;
        private Sprite _resident, _secretary, _residentAngry, _heroAngry;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _dialogue = _world.CreateManager<DialogueEventManager>();
            _dialogue.OnLineShown += (l, i, n) => { };
            _resident = MakeSprite("resident"); _secretary = MakeSprite("secretary");
            _residentAngry = MakeSprite("residentAngry"); _heroAngry = MakeSprite("heroAngry");
            _dialogue.ProtagonistName = "주인공";
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
        public void 주인공_차례에는_오른쪽_인물이_회색으로_남는다()
        {
            _dialogue.Play(DialogueData.Create("talk", true, false,
                new DialogueLine("주인공", "안녕하세요"),
                new DialogueLine("주민", "어서 오세요"),
                new DialogueLine("비서", "일정이 있어요"),
                new DialogueLine("주인공", "알겠어요")));

            // 1줄: 주인공이 말함 → 오른쪽에는 다음에 말할 주민을 회색으로 미리 세워 둠
            Assert.AreEqual(DialogueSide.Left, _dialogue.CurrentSide);
            Assert.AreEqual("주인공", _dialogue.LeftSpeakerName);
            Assert.IsNull(_dialogue.GetCurrentPortrait(), "주인공은 이미지 없음");
            Assert.AreEqual("주민", _dialogue.RightSpeakerName);
            Assert.AreEqual(_resident, _dialogue.RightPortrait);
            Assert.IsTrue(_dialogue.IsRightDimmed);
            Assert.AreEqual(_dialogue.DimmedPortraitColor, _dialogue.RightPortraitColor);

            _dialogue.Next();
            Assert.AreEqual(DialogueSide.Right, _dialogue.CurrentSide);
            Assert.IsFalse(_dialogue.IsRightDimmed, "말하는 중이면 밝게");
            Assert.AreEqual(Color.white, _dialogue.RightPortraitColor);

            _dialogue.Next();
            Assert.AreEqual("비서", _dialogue.RightSpeakerName, "오른쪽은 지금 말하는 인물로 바뀜");
            Assert.AreEqual(_secretary, _dialogue.RightPortrait);

            _dialogue.Next();
            Assert.AreEqual(DialogueSide.Left, _dialogue.CurrentSide);
            Assert.AreEqual("비서", _dialogue.RightSpeakerName, "주인공이 말할 때 오른쪽은 마지막 상대 유지");
            Assert.AreEqual(_secretary, _dialogue.RightPortrait);
            Assert.IsTrue(_dialogue.IsRightDimmed);
        }

        [Test]
        public void 나레이션_차례에도_오른쪽_인물이_회색으로_남는다()
        {
            _dialogue.Play(DialogueData.Create("talk", true, false,
                new DialogueLine("주민", "큰일 났어요!"),
                new DialogueLine("", "그때 멀리서 사이렌 소리가 들렸다...")));
            _dialogue.Next();

            Assert.AreEqual(DialogueSide.Narration, _dialogue.CurrentSide);
            Assert.AreEqual("주민", _dialogue.RightSpeakerName);
            Assert.AreEqual(_resident, _dialogue.RightPortrait);
            Assert.IsTrue(_dialogue.IsRightDimmed);
        }

        [Test]
        public void 나레이션으로_시작하면_다음에_말할_인물을_회색으로_세워_둔다()
        {
            _dialogue.Play(DialogueData.Create("talk", true, false,
                new DialogueLine("", "다음 날 아침..."),
                new DialogueLine("비서", "좋은 아침이에요")));

            Assert.AreEqual("비서", _dialogue.RightSpeakerName);
            Assert.IsTrue(_dialogue.IsRightDimmed);
        }

        [Test]
        public void 상대가_없는_대화는_오른쪽이_비어_있다()
        {
            _dialogue.Play(DialogueData.Create("talk2", true, false,
                new DialogueLine("주인공", "혼잣말"),
                new DialogueLine("", "다음 날 아침...")));

            Assert.IsNull(_dialogue.RightSpeakerName);
            Assert.IsNull(_dialogue.RightPortrait);
            Assert.IsFalse(_dialogue.IsRightDimmed);
            Assert.AreEqual(Color.white, _dialogue.RightPortraitColor);
        }

        [Test]
        public void 주인공_줄에_초상화를_넣어도_쓰지_않고_다른_인물은_줄_초상화가_우선이다()
        {
            _dialogue.Play(DialogueData.Create("talk3", true, false,
                new DialogueLine("주인공", "화났어", null, _heroAngry),
                new DialogueLine("주민", "죄송해요", null, _residentAngry)));

            Assert.IsNull(_dialogue.GetCurrentPortrait());
            Assert.AreEqual(_residentAngry, _dialogue.RightPortrait, "미리 세워 둔 인물도 그 줄의 표정");
            _dialogue.Next();
            Assert.AreEqual(_residentAngry, _dialogue.RightPortrait);
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
    /// 컷씬: 카툰 컷(페이지·자동 넘김), 시네머신 시퀀스, 대화 전 컷씬
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

        private static CutsceneData Comic(string id) => CutsceneData.CreateComic(id,
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
            var data = CutsceneData.CreateComic("auto",
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

        // ─── 시네머신 ───

        private readonly List<GameObject> _temp = new List<GameObject>();

        private CinemachineSequence MakeSequence(string id, float duration = 0f)
        {
            var go = new GameObject("Seq_" + id);
            _temp.Add(go);
            var seq = go.AddComponent<CinemachineSequence>();
            seq.SequenceId = id;
            seq.Duration = duration;
            TestWorld.Invoke(seq, "OnEnable");
            return seq;
        }

        private void CleanupTemp()
        {
            foreach (var go in _temp)
            {
                if (go == null) continue;
                foreach (var s in go.GetComponents<CinemachineSequence>()) TestWorld.Invoke(s, "OnDisable");
                Object.DestroyImmediate(go);
            }
            _temp.Clear();
        }

        [Test]
        public void 시네머신_시퀀스가_끝나면_대화가_이어진다()
        {
            try
            {
                var seq = MakeSequence("opening", duration: 3f);
                AttachViewers();
                _dialogue.Play(DialogueData.Create("arrive", true, false, new DialogueLine("주민", "어서 와요")),
                    CutsceneData.CreateCinemachine("opening_cut", "opening"));

                Assert.IsTrue(_cutscene.IsCinemachine);
                Assert.IsTrue(seq.IsPlaying);
                Assert.AreEqual(seq, _cutscene.CurrentSequence);
                _cutscene.Next();
                Assert.IsTrue(seq.IsPlaying, "시네머신은 Next로 넘어가지 않고 끝날 때까지 기다림");

                _cutscene.Tick(2f);
                Assert.IsFalse(_dialogue.IsPlaying);
                _cutscene.Tick(1.5f);

                Assert.IsFalse(seq.IsPlaying);
                Assert.IsFalse(_cutscene.IsPlaying);
                CollectionAssert.AreEqual(new[] { "컷씬시작:opening_cut", "컷씬끝:opening_cut", "대화시작:arrive", "대사:어서 와요" }, _log);
            }
            finally { CleanupTemp(); }
        }

        [Test]
        public void 시네머신은_Finish를_부를_때까지_기다릴_수_있다()
        {
            try
            {
                var seq = MakeSequence("bus"); // director 없음 + duration 0 → Finish()만 기다림
                int started = 0, finished = 0;
                seq.OnStarted.AddListener(() => started++);
                seq.OnFinished.AddListener(() => finished++);

                _cutscene.Play(CutsceneData.CreateCinemachine("c", "bus"));
                _cutscene.Tick(100f);
                Assert.IsTrue(_cutscene.IsPlaying, "UI가 없어도 시네머신은 재생");

                seq.Finish(); // Timeline Signal 등에서 호출
                Assert.IsFalse(_cutscene.IsPlaying);
                Assert.AreEqual(1, started);
                Assert.AreEqual(1, finished);
            }
            finally { CleanupTemp(); }
        }

        [Test]
        public void 시네머신을_건너뛰면_시퀀스도_멈추고_켜둔_카메라를_끈다()
        {
            try
            {
                var seq = MakeSequence("s", duration: 10f);
                var vcams = new GameObject("VCams");
                _temp.Add(vcams);
                vcams.SetActive(false);
                seq.ActivateWhilePlaying = vcams;

                bool skipped = false;
                seq.OnSequenceEnded += s => skipped = s;
                _cutscene.Play(CutsceneData.CreateCinemachine("c", "s"));
                Assert.IsTrue(vcams.activeSelf, "재생 중에는 켬");

                _cutscene.Skip();
                Assert.IsFalse(seq.IsPlaying);
                Assert.IsTrue(skipped);
                Assert.IsFalse(vcams.activeSelf, "끝나면 끔");
                Assert.IsFalse(_cutscene.IsPlaying);
            }
            finally { CleanupTemp(); }
        }

        [Test]
        public void 시퀀스가_씬에_없으면_건너뛰고_대화로_간다()
        {
            AttachViewers();
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("시네머신 시퀀스 'none'"));
            _dialogue.Play(DialogueData.Create("d", true, false, new DialogueLine("주민", "hi")),
                CutsceneData.CreateCinemachine("c", "none"));
            Assert.IsFalse(_cutscene.IsPlaying);
            Assert.IsTrue(_dialogue.IsPlaying);
        }

        [Test]
        public void 시네머신_중에는_시간이_멈추고_끝나면_풀린다()
        {
            try
            {
                var time = _world.CreateManager<TimeManager>();
                MakeSequence("s", duration: 1f);
                _cutscene.Play(CutsceneData.CreateCinemachine("c", "s"));
                Assert.Greater(time.PauseLockCount, 0);
                _cutscene.Tick(1f);
                Assert.AreEqual(0, time.PauseLockCount);
            }
            finally { CleanupTemp(); }
        }

        [Test]
        public void 시퀀스_ID가_없는_시네머신_컷씬은_재생하지_않는다()
        {
            Assert.IsFalse(_cutscene.Play(CutsceneData.CreateCinemachine("c", "")));
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
            TestWorld.SetField(_triggers, "openingCutscene", CutsceneData.CreateComic("opening", new CutsceneShot { caption = "버스" }));
            TestWorld.SetField(_triggers, "introDialogue", DialogueData.Create("arrive", true, true, new DialogueLine("주민", "어서 와요")));
            TestWorld.Invoke(_triggers, "Start"); TestWorld.Invoke(_triggers, "Update");

            CollectionAssert.AreEqual(new[] { "컷씬:opening" }, _log);
            _cutscene.Next();
            CollectionAssert.AreEqual(new[] { "컷씬:opening", "대화:arrive" }, _log);
        }

        [Test]
        public void 필수_시설을_처음_지으면_카툰_컷씬_후_대화가_나온다()
        {
            TestWorld.Invoke(_triggers, "Start"); TestWorld.Invoke(_triggers, "Update");
            var entries = TestWorld.GetField<List<EssentialFacilityDialogue>>(_triggers, "essentialDialogues");
            entries.Find(e => e.service == ServiceType.Hospital).installedCutscene =
                CutsceneData.CreateComic("hospital_comic", new CutsceneShot(null, "구급차"));

            var hospital = _world.CreateBuilding(_world.CreateData(BuildingType.Essential, id: "hospital", name: "병원"), 0, 0);
            TestWorld.Invoke(_triggers, "HandleBuildingPlaced", hospital);

            CollectionAssert.AreEqual(new[] { "컷씬:hospital_comic" }, _log);
            _cutscene.Next();
            CollectionAssert.AreEqual(new[] { "컷씬:hospital_comic", "대화:installed_Hospital" }, _log);
        }
    }
}

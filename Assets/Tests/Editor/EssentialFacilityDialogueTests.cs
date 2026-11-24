using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.EventSystem;
using InfraTown.Population;
using InfraTown.Satisfaction;
using InfraTown.Services;

namespace InfraTown.Tests
{
    /// <summary>
    /// 대화 조건 2) 필수 시설을 해금 후 4~12주(1~3달, 무작위) 동안 안 지으면 주민 불만 (아래 Days(n)는 n주)
    ///          3) 필수 시설을 처음 설치하면 설치 대사
    /// </summary>
    public class EssentialFacilityDialogueTests
    {
        private TestWorld _world;
        private BuildingManager _buildings;
        private PopulationManager _population;
        private DialogueEventManager _dialogue;
        private DialogueTriggers _triggers;
        private GameObject _prefab;
        private BuildingData _hospital, _school;
        private readonly List<ServiceType> _complaints = new List<ServiceType>();
        private readonly List<ServiceType> _installed = new List<ServiceType>();
        private readonly List<string> _started = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _buildings = _world.CreateManager<BuildingManager>();
            _buildings.RequireRoadAccess = false; // 대화 조건만 검사 (도로 접근 조건은 RoadAccessTests)
            _population = _world.CreateManager<PopulationManager>();
            _world.CreateManager<ServiceAccessibilityManager>();
            _world.CreateManager<GameLoopManager>(callStart: true);
            _dialogue = _world.CreateManager<DialogueEventManager>();
            _triggers = _dialogue.gameObject.AddComponent<DialogueTriggers>();

            _prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _hospital = _world.CreateData(BuildingType.Essential, id: "hospital", name: "병원");
            _hospital.buildingPrefab = _prefab;
            _school = _world.CreateData(BuildingType.Essential, id: "school", name: "학교");
            _school.buildingPrefab = _prefab;
            TestWorld.SetField(_buildings, "buildingDatabase", new List<BuildingData> { _hospital, _school });

            SetRange(30, 30); // 테스트는 정확히 30주로 고정 (무작위 범위 테스트는 따로)
            TestWorld.SetField(_triggers, "showInitialUnlockStories", false);

            _complaints.Clear(); _installed.Clear(); _started.Clear();
            _triggers.OnFacilityComplaint += t => _complaints.Add(t);
            _triggers.OnFacilityFirstInstalled += t => _installed.Add(t);
            _dialogue.OnDialogueStarted += d => _started.Add(d.dialogueId);
            _dialogue.OnLineShown += (l, i, n) => { };

            TestWorld.Invoke(_triggers, "Start"); TestWorld.Invoke(_triggers, "Update");
        }

        [TearDown]
        public void TearDown()
        {
            TestWorld.Invoke(_triggers, "OnDestroy");
            _buildings.CancelBuildingMode();
            var grid = TestWorld.GetField<BuildingInstance[,]>(_buildings, "_occupiedGrid");
            foreach (var b in grid) if (b != null) Object.DestroyImmediate(b.gameObject);
            Object.DestroyImmediate(_prefab);
            _world.Dispose();
        }

        private void SetRange(int min, int max)
        {
            TestWorld.SetField(_triggers, "complaintMinWeeks", min);
            TestWorld.SetField(_triggers, "complaintMaxWeeks", max);
            TestWorld.SetField(_triggers, "randomSeed", 1234);
        }

        private void Days(int n)
        {
            for (int i = 0; i < n; i++) _triggers.ProcessWeek();
        }

        private BuildingInstance Place(BuildingData data, int x, int y)
        {
            _buildings.SelectBuildingToBuild(data);
            TestWorld.Invoke(_buildings, "TryPlaceBuilding", new Vector2Int(x, y));
            _buildings.CancelBuildingMode();
            return TestWorld.GetField<BuildingInstance[,]>(_buildings, "_occupiedGrid")[x, y];
        }

        // ── 2) 미설치 불만 ──

        [Test]
        public void 해금된_필수시설을_정해진_기간동안_안_지으면_주민이_불만을_말한다()
        {
            Days(29);
            Assert.IsFalse(_complaints.Contains(ServiceType.Hospital), "29주째는 아직");

            Days(1);
            Assert.IsTrue(_complaints.Contains(ServiceType.Hospital));
            Assert.IsTrue(_started.Any(id => id.StartsWith("missing_Hospital")));
            Assert.AreEqual("주민", _dialogue.CurrentLine.speaker);
        }

        [Test]
        public void 불만이_나오는_기간은_기본_4주에서_12주_사이_무작위()
        {
            // 기본값 확인 (주 단위 변경 2026-11-24: 1~3달 = 4~12주)
            var go = new GameObject("defaults");
            var fresh = go.AddComponent<DialogueTriggers>();
            Assert.AreEqual((4, 12), (fresh.ComplaintMinWeeks, fresh.ComplaintMaxWeeks));
            Object.DestroyImmediate(go);

            SetRange(4, 12);
            TestWorld.SetField(_triggers, "_rng", null);
            var rolls = Enumerable.Range(0, 2000).Select(_ => _triggers.RollComplaintWeeks()).ToList();

            Assert.IsTrue(rolls.All(w => w >= 4 && w <= 12), $"범위 밖: {rolls.Min()}~{rolls.Max()}");
            Assert.AreEqual(4, rolls.Min(), "1달 값도 나와야 함");
            Assert.AreEqual(12, rolls.Max(), "3달 값도 나와야 함");
            Assert.AreEqual(9, rolls.Distinct().Count(), "고르게 무작위");
        }

        [Test]
        public void 시설마다_기간을_따로_정한다()
        {
            SetRange(4, 12);
            TestWorld.SetField(_triggers, "_rng", null);
            Days(1);

            int hospital = _triggers.GetComplaintThreshold(ServiceType.Hospital);
            int school = _triggers.GetComplaintThreshold(ServiceType.School);
            Assert.That(hospital, Is.InRange(4, 12));
            Assert.That(school, Is.InRange(4, 12));
            Assert.AreEqual(1, _triggers.GetMissingWeeks(ServiceType.Hospital));
        }

        [Test]
        public void 아직_해금되지_않은_시설은_날짜를_세지_않는다()
        {
            _hospital.requiredPopulation = 300;
            Days(100);
            Assert.AreEqual(0, _triggers.GetMissingWeeks(ServiceType.Hospital));
            Assert.IsFalse(_complaints.Contains(ServiceType.Hospital));
        }

        [Test]
        public void 설치되어_있으면_불만이_없다()
        {
            Place(_hospital, 0, 0);
            Days(100);
            Assert.IsFalse(_complaints.Contains(ServiceType.Hospital));
            Assert.AreEqual(0, _triggers.GetMissingWeeks(ServiceType.Hospital));
        }

        [Test]
        public void 계속_안_지으면_새_기간_뒤에_다시_불만()
        {
            Days(30);
            Days(30);
            Assert.AreEqual(2, _complaints.Count(t => t == ServiceType.Hospital));
        }

        [Test]
        public void 철거해서_다시_없어지면_처음부터_다시_센다()
        {
            var hospital = Place(_hospital, 0, 0);
            Days(10);
            TestWorld.Invoke(hospital, "OnDestroy"); // 철거 알림 (Destroy와 같은 경로)

            Days(29);
            Assert.IsFalse(_complaints.Contains(ServiceType.Hospital));
            Days(1);
            Assert.IsTrue(_complaints.Contains(ServiceType.Hospital));
        }

        [Test]
        public void 만족도가_좋을_때는_세지_않는_옵션()
        {
            TestWorld.SetField(_triggers, "countOnlyWhenNotGood", true);
            var sat = _world.CreateManager<SatisfactionManager>();
            var house = _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 10), 30, 30);
            var park = _world.CreateBuilding(_world.CreateData(BuildingType.Facility, impact: 10f), 31, 30);
            sat.RecalculateSatisfaction(new List<BuildingInstance> { house, park });
            Assert.AreEqual(SatisfactionState.Good, sat.CurrentState);

            Days(60);
            Assert.AreEqual(0, _triggers.GetMissingWeeks(ServiceType.Hospital));
        }

        // ── 3) 설치 대사 ──

        [Test]
        public void 필수시설을_처음_설치하면_설치_대사가_나온다()
        {
            Place(_hospital, 0, 0);

            CollectionAssert.AreEqual(new[] { ServiceType.Hospital }, _installed);
            Assert.Contains("installed_Hospital", _started);
            StringAssert.Contains("병원이 생겼어요", _dialogue.GetCurrentText());
        }

        [Test]
        public void 같은_종류를_두번째_설치할_때는_대사가_없다()
        {
            Place(_hospital, 0, 0);
            _dialogue.ClearAll();
            Place(_hospital, 6, 0);

            Assert.AreEqual(1, _installed.Count(t => t == ServiceType.Hospital));
            Assert.IsFalse(_dialogue.IsPlaying);
        }

        [Test]
        public void 필수시설이_아닌_건물은_설치_대사가_없다()
        {
            var hall = _world.CreateData(BuildingType.Facility, id: "village_hall", name: "마을 회관");
            hall.buildingPrefab = _prefab;
            Place(hall, 0, 0);
            Assert.IsEmpty(_installed);
        }

        [Test]
        public void 에셋을_넣으면_기본_문장_대신_에셋을_재생한다()
        {
            var custom = DialogueData.Create("custom_hospital", true, true, new DialogueLine("의사", "드디어 병원이 문을 열었습니다."));
            var list = TestWorld.GetField<List<EssentialFacilityDialogue>>(_triggers, "essentialDialogues");
            list.First(e => e.service == ServiceType.Hospital).installedDialogue = custom;

            Place(_hospital, 0, 0);

            Assert.Contains("custom_hospital", _started);
            Assert.AreEqual("의사", _dialogue.CurrentLine.speaker);
            Object.DestroyImmediate(custom);
        }
    }
}

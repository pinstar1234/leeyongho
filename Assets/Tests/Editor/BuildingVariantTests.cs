using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.Grid;
using InfraTown.Save;

namespace InfraTown.Tests
{
    /// <summary>
    /// 건물 모양 (사용자 기획 2026-11-26): 도로처럼 모양이 여러 가지인 건물은 건설 모드에서 R키로 다음 모양.
    /// 개수 제한 없이 BuildingData.variants에 넣은 만큼 차례로, 마지막 다음은 처음. 모양 프리팹이 비어 있으면 buildingPrefab.
    /// </summary>
    public class BuildingVariantTests
    {
        private TestWorld _world;
        private BuildingManager _manager;
        private GameLoopManager _loop;
        private EconomyManager _economy;
        private GameObject _basePrefab;
        private readonly List<GameObject> _shapePrefabs = new List<GameObject>();
        private BuildingData _road, _house;
        private readonly List<string> _changed = new List<string>();

        private static readonly string[] ShapeNames = { "직선", "곡선", "T자", "십자", "막다른 길" };

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _world.CreateManager<LightweightGridManager>();
            _manager = _world.CreateManager<BuildingManager>(callStart: true);
            _loop = _world.CreateManager<GameLoopManager>();
            _economy = _world.CreateManager<EconomyManager>();
            _economy.ApplySaveState(new EconomySaveState { money = 1_000_000, requestedSubsidy = 15000 });

            _basePrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _basePrefab.name = "RoadBase";
            _road = _world.CreateData(BuildingType.Infrastructure, id: "road", name: "도로", construction: 10);
            _road.buildingPrefab = _basePrefab;
            foreach (var shape in ShapeNames)
            {
                var prefab = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                prefab.name = "Road_" + shape;
                _shapePrefabs.Add(prefab);
                _road.variants.Add(new BuildingVariant { variantName = shape, prefab = prefab });
            }

            _house = _world.CreateData(BuildingType.Housing, id: "house", name: "주택", construction: 10, capacity: 10);
            _house.buildingPrefab = _basePrefab;
            _manager.RequireRoadAccess = false;

            _changed.Clear();
            _manager.OnPreviewVariantChanged += (i, name) => _changed.Add($"{i}:{name}");
        }

        [TearDown]
        public void TearDown()
        {
            _manager.CancelBuildingMode();
            foreach (var b in Object.FindObjectsOfType<BuildingInstance>()) Object.DestroyImmediate(b.gameObject);
            foreach (var p in _shapePrefabs) Object.DestroyImmediate(p);
            _shapePrefabs.Clear();
            Object.DestroyImmediate(_basePrefab);
            _world.Dispose();
        }

        private GameObject Preview => TestWorld.GetField<GameObject>(_manager, "_previewInstance");

        private BuildingInstance PlaceOne(BuildingData data, int x, int y)
        {
            _manager.TryPlaceArea(new Vector2Int(x, y), new Vector2Int(x, y));
            return _manager.GetBuildingAt(new Vector2Int(x, y));
        }

        [Test]
        public void R을_누를_때마다_다음_모양이고_마지막_다음은_처음()
        {
            _manager.SelectBuildingToBuild(_road);
            Assert.AreEqual(0, _manager.CurrentVariantIndex);
            Assert.AreEqual("직선", _manager.CurrentVariantName);
            StringAssert.StartsWith("Road_직선", Preview.name, "처음 모양으로 미리보기");

            for (int i = 1; i < ShapeNames.Length; i++)
            {
                Assert.IsTrue(_manager.CycleVariant());
                Assert.AreEqual(i, _manager.CurrentVariantIndex);
                StringAssert.StartsWith("Road_" + ShapeNames[i], Preview.name, "미리보기도 그 모양으로 바뀜");
            }
            Assert.IsTrue(_manager.CycleVariant());
            Assert.AreEqual(0, _manager.CurrentVariantIndex, "마지막 다음은 처음");
            CollectionAssert.AreEqual(new[] { "1:곡선", "2:T자", "3:십자", "4:막다른 길", "0:직선" }, _changed);

            Assert.IsTrue(_manager.CycleVariant(-1));
            Assert.AreEqual(ShapeNames.Length - 1, _manager.CurrentVariantIndex, "이전 모양(-1)은 처음에서 마지막으로");
        }

        [Test]
        public void 모양_수는_넣은_만큼이고_제한이_없다()
        {
            for (int i = 0; i < 7; i++) _road.variants.Add(new BuildingVariant { variantName = $"추가{i}" });
            Assert.AreEqual(12, _road.VariantCount);
            _manager.SelectBuildingToBuild(_road);
            for (int i = 0; i < 11; i++) _manager.CycleVariant();
            Assert.AreEqual(11, _manager.CurrentVariantIndex);
            Assert.AreEqual("추가6", _manager.CurrentVariantName);
            _manager.CycleVariant();
            Assert.AreEqual(0, _manager.CurrentVariantIndex);
        }

        [Test]
        public void 모양이_하나뿐인_건물은_R을_눌러도_그대로()
        {
            _manager.SelectBuildingToBuild(_house);
            Assert.IsFalse(_house.HasVariants);
            Assert.IsFalse(_manager.CycleVariant());
            Assert.AreEqual(0, _manager.CurrentVariantIndex);
            Assert.IsEmpty(_changed);
        }

        [Test]
        public void 고른_모양의_프리팹으로_설치된다()
        {
            _manager.SelectBuildingToBuild(_road);
            _manager.CycleVariant(); // 곡선
            _manager.CycleVariant(); // T자
            var placed = PlaceOne(_road, 5, 5);

            Assert.IsNotNull(placed);
            Assert.AreEqual(2, placed.VariantIndex);
            Assert.AreEqual("T자", placed.VariantName);
            StringAssert.StartsWith("Road_T자", placed.gameObject.name);
        }

        [Test]
        public void 드래그로_여러_개를_지어도_모두_고른_모양()
        {
            _manager.SelectBuildingToBuild(_road);
            _manager.CycleVariant(); // 곡선
            int built = _manager.TryPlaceArea(new Vector2Int(2, 8), new Vector2Int(6, 8));

            Assert.AreEqual(5, built);
            for (int x = 2; x <= 6; x++)
                Assert.AreEqual(1, _manager.GetBuildingAt(new Vector2Int(x, 8)).VariantIndex);
        }

        [Test]
        public void 같은_건물을_다시_고르면_모양_유지_다른_건물을_고르면_처음_모양()
        {
            _manager.SelectBuildingToBuild(_road);
            _manager.CycleVariant();
            _manager.CycleVariant();
            _manager.SelectBuildingToBuild(_road);
            Assert.AreEqual(2, _manager.CurrentVariantIndex, "연속 설치 중에는 유지 (회전과 같은 규칙)");

            _manager.SelectBuildingToBuild(_house);
            _manager.SelectBuildingToBuild(_road);
            Assert.AreEqual(0, _manager.CurrentVariantIndex);
        }

        [Test]
        public void 모양에_프리팹이_비어_있으면_기본_도로_프리팹으로_설치()
        {
            _road.variants[1].prefab = null;
            _manager.SelectBuildingToBuild(_road);
            _manager.CycleVariant();
            StringAssert.StartsWith("RoadBase", Preview.name);
            var placed = PlaceOne(_road, 3, 3);
            Assert.AreEqual(1, placed.VariantIndex, "모양 번호는 그대로 기억");
            StringAssert.StartsWith("RoadBase", placed.gameObject.name);
        }

        [Test]
        public void 저장하면_모양도_저장되고_되살릴_때_같은_모양()
        {
            TestWorld.SetField(_manager, "buildingDatabase", new List<BuildingData> { _road, _house });
            _manager.SelectBuildingToBuild(_road);
            _manager.CycleVariant(); _manager.CycleVariant(); _manager.CycleVariant(); // 십자
            PlaceOne(_road, 10, 10);
            _manager.CancelBuildingMode();

            var data = SaveSystem.CaptureCurrentGame();
            var saved = data.buildings.Single(b => b.buildingID == "road");
            Assert.AreEqual(3, saved.variant);

            var restored = _manager.RestoreBuilding(_road, new Vector2Int(20, 20), 0f, saved.variant);
            Assert.AreEqual(3, restored.VariantIndex);
            StringAssert.StartsWith("Road_십자", restored.gameObject.name);

            var clamped = _manager.RestoreBuilding(_road, new Vector2Int(22, 20), 0f, 99);
            Assert.AreEqual(ShapeNames.Length - 1, clamped.VariantIndex, "없어진 모양 번호는 범위 안으로");
        }

        [Test]
        public void 모양_이름이_비어_있으면_값_검사가_알린다()
        {
            _road.variants.Add(new BuildingVariant());
            Assert.IsTrue(_road.Validate(fix: false).Any(p => p.Contains("모양 이름이 비어 있음")));
        }
    }
}

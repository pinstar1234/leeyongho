using NUnit.Framework;
using UnityEngine;
using InfraTown.Building;

namespace InfraTown.Tests
{
    /// <summary>
    /// 건설 회전: Q/E(RotatePreview)로 돌린 미리보기 모습 그대로 설치되는지, 프리팹 원래 회전이 유지되는지
    /// </summary>
    public class BuildingRotationTests
    {
        private TestWorld _world;
        private BuildingManager _manager;
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _manager = _world.CreateManager<BuildingManager>();

            // 에셋에서 흔한 경우처럼 원래 회전이 들어간 '프리팹' (X -90°, Y 30°)
            _prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _prefab.name = "RotatedPrefab";
            _prefab.transform.rotation = Quaternion.Euler(-90f, 30f, 0f);
        }

        [TearDown]
        public void TearDown()
        {
            _manager.CancelBuildingMode();
            var grid = TestWorld.GetField<BuildingInstance[,]>(_manager, "_occupiedGrid");
            foreach (var b in grid) if (b != null) Object.DestroyImmediate(b.gameObject);
            Object.DestroyImmediate(_prefab);
            _world.Dispose();
        }

        private BuildingData Data(string id = "rot", BuildingSizeType size = BuildingSizeType.Medium)
        {
            var data = _world.CreateData(BuildingType.Facility, id: id, size: size);
            data.buildingPrefab = _prefab;
            return data;
        }

        private GameObject Preview() => TestWorld.GetField<GameObject>(_manager, "_previewInstance");

        private BuildingInstance Place(int x, int y)
        {
            TestWorld.Invoke(_manager, "TryPlaceBuilding", new Vector2Int(x, y));
            return TestWorld.GetField<BuildingInstance[,]>(_manager, "_occupiedGrid")[x, y];
        }

        private static void AssertSameRotation(Quaternion expected, Quaternion actual, string message = null) =>
            Assert.Less(Quaternion.Angle(expected, actual), 0.01f, message ?? $"expected {expected.eulerAngles}, actual {actual.eulerAngles}");

        [Test]
        public void Q는_플러스90_E는_마이너스90이고_0에서_360사이로_돈다()
        {
            _manager.SelectBuildingToBuild(Data());

            _manager.RotatePreview(90f);
            Assert.AreEqual(90f, _manager.CurrentRotationY);
            _manager.RotatePreview(-90f);
            _manager.RotatePreview(-90f);
            Assert.AreEqual(270f, _manager.CurrentRotationY);
            _manager.RotatePreview(90f);
            Assert.AreEqual(0f, _manager.CurrentRotationY);
        }

        [Test]
        public void 미리보기는_프리팹의_원래_회전을_유지한다()
        {
            _manager.SelectBuildingToBuild(Data());
            AssertSameRotation(_prefab.transform.rotation, Preview().transform.rotation);
        }

        [Test]
        public void 회전하면_미리보기가_바로_돈다()
        {
            _manager.SelectBuildingToBuild(Data());
            _manager.RotatePreview(90f);
            AssertSameRotation(Quaternion.Euler(0f, 90f, 0f) * _prefab.transform.rotation, Preview().transform.rotation);
        }

        [Test]
        public void 설치된_건물은_미리보기와_같은_회전이다()
        {
            _manager.SelectBuildingToBuild(Data());
            _manager.RotatePreview(-90f);
            Quaternion previewRotation = Preview().transform.rotation;

            var placed = Place(10, 10);

            Assert.IsNotNull(placed);
            AssertSameRotation(previewRotation, placed.transform.rotation);
            Assert.AreEqual(270f, placed.RotationY);
        }

        [Test]
        public void 회전하지_않으면_프리팹_원래_회전으로_설치된다()
        {
            _manager.SelectBuildingToBuild(Data());
            var placed = Place(5, 5);
            AssertSameRotation(_prefab.transform.rotation, placed.transform.rotation, "예전 버그: Quaternion.identity로 덮어써짐");
        }

        [Test]
        public void 같은_건물을_연속_설치하면_회전이_유지된다()
        {
            var data = Data();
            _manager.SelectBuildingToBuild(data);
            _manager.RotatePreview(90f);
            var first = Place(0, 0);
            var second = Place(4, 0);
            AssertSameRotation(first.transform.rotation, second.transform.rotation);

            _manager.SelectBuildingToBuild(data); // 같은 건물 다시 선택
            Assert.AreEqual(90f, _manager.CurrentRotationY);
        }

        [Test]
        public void 다른_건물을_고르면_회전이_0으로_돌아간다()
        {
            _manager.SelectBuildingToBuild(Data("a"));
            _manager.RotatePreview(90f);
            _manager.SelectBuildingToBuild(Data("b"));
            Assert.AreEqual(0f, _manager.CurrentRotationY);
        }

        [Test]
        public void 회전해도_점유_칸은_그대로()
        {
            _manager.SelectBuildingToBuild(Data(size: BuildingSizeType.Large));
            _manager.RotatePreview(90f);
            var placed = Place(10, 10);
            var grid = TestWorld.GetField<BuildingInstance[,]>(_manager, "_occupiedGrid");
            for (int x = 10; x < 14; x++)
                for (int y = 10; y < 14; y++)
                    Assert.AreSame(placed, grid[x, y]);
            Assert.IsNull(grid[14, 10]);
        }

        [Test]
        public void 회전_이벤트와_설치_이벤트가_발생한다()
        {
            float rotated = -1f;
            BuildingInstance placedEvent = null;
            _manager.OnPreviewRotationChanged += r => rotated = r;
            _manager.OnBuildingPlaced += b => placedEvent = b;

            _manager.SelectBuildingToBuild(Data());
            _manager.RotatePreview(90f);
            var placed = Place(1, 1);

            Assert.AreEqual(90f, rotated);
            Assert.AreSame(placed, placedEvent);
        }
    }
}

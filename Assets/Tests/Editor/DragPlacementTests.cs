using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.Grid;
using InfraTown.Save;
using InfraTown.Satisfaction;

namespace InfraTown.Tests
{
    /// <summary>
    /// 드래그 건설 (사용자 기획 2026-11-25): 누른 채 끌어서 여러 개를 한 번에.
    /// 도로·전신주는 가로/세로 한 줄(더 많이 움직인 방향), 나머지는 사각형을 건물 크기 간격으로 채움.
    /// 지을 수 없는 칸은 건너뛰고 되는 칸만 짓고, 돈이 떨어지면 그 뒤 칸은 짓지 않는다
    /// </summary>
    public class DragPlacementTests
    {
        private TestWorld _world;
        private LightweightGridManager _grid;
        private BuildingManager _manager;
        private EconomyManager _economy;
        private GameObject _prefab;
        private BuildingData _road, _pole, _house, _medium;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _grid = _world.CreateManager<LightweightGridManager>();
            _manager = _world.CreateManager<BuildingManager>(callStart: true); // 드래그·철거 알림 구독
            _economy = _world.CreateManager<EconomyManager>();
            _prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);

            _road = Data(BuildingType.Infrastructure, "road", 10);
            _pole = Data(BuildingType.Infrastructure, "power_pole", 10);
            _pole.isPowerPole = true;
            _house = Data(BuildingType.Housing, "house", 100);
            _medium = Data(BuildingType.Facility, "medium", 100, BuildingSizeType.Medium);
            SetMoney(1_000_000);
        }

        [TearDown]
        public void TearDown()
        {
            _manager.CancelBuildingMode();
            foreach (var b in Object.FindObjectsOfType<BuildingInstance>()) Object.DestroyImmediate(b.gameObject);
            Object.DestroyImmediate(_prefab);
            _world.Dispose();
        }

        private BuildingData Data(BuildingType category, string id, int cost, BuildingSizeType size = BuildingSizeType.Small)
        {
            var data = _world.CreateData(category, id: id, name: id, size: size, construction: cost);
            data.buildingPrefab = _prefab;
            return data;
        }

        private void SetMoney(int money) => _economy.ApplySaveState(new EconomySaveState { money = money, requestedSubsidy = 15000 });

        private int CountOf(BuildingData data) => Object.FindObjectsOfType<BuildingInstance>().Count(b => b.Data == data);

        private int Area(BuildingData data, int x0, int y0, int x1, int y1)
        {
            _manager.SelectBuildingToBuild(data);
            return _manager.TryPlaceArea(new Vector2Int(x0, y0), new Vector2Int(x1, y1));
        }

        private static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        // ─── 칸 계산 ───

        [Test]
        public void 도로는_가로로_더_많이_끌면_시작_줄을_따라_한_줄()
        {
            var cells = _manager.GetDragCells(_road, V(2, 5), V(7, 7));
            CollectionAssert.AreEqual(new[] { V(2, 5), V(3, 5), V(4, 5), V(5, 5), V(6, 5), V(7, 5) }, cells);
        }

        [Test]
        public void 도로는_세로로_더_많이_끌면_세로_한_줄()
        {
            var cells = _manager.GetDragCells(_road, V(2, 5), V(3, 9));
            CollectionAssert.AreEqual(new[] { V(2, 5), V(2, 6), V(2, 7), V(2, 8), V(2, 9) }, cells);
        }

        [Test]
        public void 왼쪽_아래로_끌어도_시작_칸부터_순서대로()
        {
            var cells = _manager.GetDragCells(_pole, V(5, 5), V(2, 5));
            CollectionAssert.AreEqual(new[] { V(5, 5), V(4, 5), V(3, 5), V(2, 5) }, cells);
        }

        [Test]
        public void 주택은_끈_사각형을_채운다()
        {
            var cells = _manager.GetDragCells(_house, V(2, 2), V(4, 3));
            CollectionAssert.AreEqual(new[] { V(2, 2), V(3, 2), V(4, 2), V(2, 3), V(3, 3), V(4, 3) }, cells);
        }

        [Test]
        public void 큰_건물은_건물_크기_간격으로_채운다()
        {
            // 2x2: x 0~5 → 0,2,4 / y 0~3 → 0,2
            var cells = _manager.GetDragCells(_medium, V(0, 0), V(5, 3));
            CollectionAssert.AreEqual(new[] { V(0, 0), V(2, 0), V(4, 0), V(0, 2), V(2, 2), V(4, 2) }, cells);
        }

        [Test]
        public void 제자리면_한_칸()
        {
            CollectionAssert.AreEqual(new[] { V(3, 3) }, _manager.GetDragCells(_house, V(3, 3), V(3, 3)));
            CollectionAssert.AreEqual(new[] { V(3, 3) }, _manager.GetDragCells(_road, V(3, 3), V(3, 3)));
        }

        [Test]
        public void 맵_밖으로_나가는_건물은_빠진다()
        {
            // 2x2를 x 58에서 오른쪽 끝까지: 58은 58~59라 들어가고, 그 다음은 없음 / 위쪽 y 38 → 38~39
            var cells = _manager.GetDragCells(_medium, V(58, 38), V(59, 39));
            CollectionAssert.AreEqual(new[] { V(58, 38) }, cells);
            // 왼쪽으로 끌어 0 아래로 내려가는 칸
            Assert.IsTrue(_manager.GetDragCells(_medium, V(1, 0), V(0, 0)).All(c => c.x >= 0));
        }

        [Test]
        public void 한_번에_최대_개수까지만()
        {
            _manager.MaxDragBuildings = 10;
            Assert.AreEqual(10, _manager.GetDragCells(_road, V(0, 0), V(59, 0)).Count);
            Assert.AreEqual(10, _manager.GetDragCells(_house, V(0, 0), V(20, 20)).Count);
        }

        // ─── 설치 ───

        [Test]
        public void 도로를_한_줄로_한_번에_짓고_돈은_개수만큼_빠진다()
        {
            int placed = Area(_road, 0, 0, 9, 0);
            Assert.AreEqual(10, placed);
            Assert.AreEqual(10, CountOf(_road));
            for (int x = 0; x < 10; x++) Assert.IsTrue(_manager.IsRoadCell(x, 0), $"({x},0)");
            Assert.AreEqual(1_000_000 - 10 * 10, _economy.CurrentMoney);
            Assert.IsNull(_manager.LastPlacementError);
        }

        [Test]
        public void 도로_위에_전신주를_한_줄로()
        {
            Area(_road, 0, 0, 9, 0);
            Assert.AreEqual(10, Area(_pole, 0, 0, 9, 0));
            for (int x = 0; x < 10; x++) Assert.IsNotNull(_manager.GetRoadPoleAt(V(x, 0)));
        }

        [Test]
        public void 도로에_닿지_않는_칸은_건너뛰고_되는_칸만_짓는다()
        {
            Area(_road, 0, 0, 9, 0); // y=0 도로
            var rejected = new List<Vector2Int>();
            _manager.OnPlacementRejected += (d, cell, reason) => rejected.Add(cell);
            int done = -1, fail = -1;
            _manager.OnAreaPlaced += (p, f) => { done = p; fail = f; };

            // y=1~2 두 줄: y=1은 도로에 닿고 y=2는 닿지 않음
            int placed = Area(_house, 0, 1, 4, 2);
            Assert.AreEqual(5, placed);
            Assert.AreEqual(5, done);
            Assert.AreEqual(5, fail);
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 5).Select(x => V(x, 2)), rejected);
            StringAssert.Contains("도로", _manager.LastPlacementError);
            for (int x = 0; x < 5; x++) Assert.IsNotNull(_manager.GetBuildingAt(V(x, 1)));
        }

        [Test]
        public void 겹치는_칸은_건너뛴다()
        {
            Area(_road, 0, 0, 9, 0);
            Area(_house, 2, 1, 2, 1); // 한 채 먼저
            Assert.AreEqual(4, Area(_house, 0, 1, 4, 1));
            Assert.AreEqual(5, CountOf(_house));
        }

        [Test]
        public void 돈이_떨어지면_거기서_멈추고_뒤_칸은_짓지_않는다()
        {
            Area(_road, 0, 0, 9, 0);
            SetMoney(350); // 주택 100원 → 3채
            var reasons = new List<string>();
            _manager.OnPlacementRejected += (d, cell, reason) => reasons.Add(reason);

            Assert.AreEqual(3, Area(_house, 0, 1, 9, 1));
            Assert.AreEqual(50, _economy.CurrentMoney);
            Assert.AreEqual(7, reasons.Count);
            Assert.IsTrue(reasons.All(r => r == BuildingManager.MoneyShortPlacementError));
            for (int x = 0; x < 3; x++) Assert.IsNotNull(_manager.GetBuildingAt(V(x, 1)), "시작 칸부터 순서대로");
        }

        [Test]
        public void 하나도_못_지으면_0()
        {
            Assert.AreEqual(0, Area(_house, 20, 20, 25, 20)); // 도로 없음
            Assert.AreEqual(0, CountOf(_house));
        }

        [Test]
        public void 게임_오버면_드래그로도_짓지_못한다()
        {
            var time = _world.CreateManager<InfraTown.TimeSystem.TimeManager>();
            var outcome = _world.CreateManager<GameOutcomeManager>();
            _manager.SelectBuildingToBuild(_road);
            outcome.ApplySaveState(new OutcomeSaveState { result = (int)GameResult.Defeat });
            Assert.AreEqual(0, _manager.TryPlaceArea(V(0, 0), V(9, 0)));
            Assert.AreEqual(0, CountOf(_road));
        }

        // ─── 마우스 드래그 (그리드 이벤트) ───

        [Test]
        public void 끌고_떼면_그_범위에_짓고_건설_모드는_그대로()
        {
            _manager.SelectBuildingToBuild(_road);
            _grid.BeginDrag(V(0, 0));
            Assert.IsTrue(_manager.IsDragPlacing);
            _grid.UpdateDrag(V(4, 1));
            Assert.AreEqual(5, _manager.DragCells.Count);
            Assert.AreEqual(0, CountOf(_road), "떼기 전에는 짓지 않는다");

            _grid.EndDrag();
            Assert.IsFalse(_manager.IsDragPlacing);
            Assert.AreEqual(5, CountOf(_road));
            Assert.IsTrue(_manager.IsPlacingBuilding, "건설 모드는 이어짐");
            Assert.AreEqual(0, _manager.DragCells.Count);
        }

        [Test]
        public void 제자리_클릭은_한_개()
        {
            _manager.SelectBuildingToBuild(_road);
            _grid.BeginDrag(V(3, 3));
            _grid.EndDrag();
            Assert.AreEqual(1, CountOf(_road));
        }

        [Test]
        public void 오른쪽_버튼_취소면_아무것도_짓지_않고_건설_모드는_그대로()
        {
            _manager.SelectBuildingToBuild(_road);
            _grid.BeginDrag(V(0, 0));
            _grid.UpdateDrag(V(5, 0));
            _grid.CancelDrag();
            Assert.IsFalse(_manager.IsDragPlacing);
            Assert.AreEqual(0, CountOf(_road));
            Assert.IsTrue(_manager.IsPlacingBuilding);
        }

        [Test]
        public void 드래그_미리보기는_돈을_순서대로_써서_되는_칸을_센다()
        {
            Area(_road, 0, 0, 9, 0);
            SetMoney(250); // 주택 2채
            _manager.SelectBuildingToBuild(_house);
            int changed = 0;
            _manager.OnDragCellsChanged += c => changed++;

            _grid.BeginDrag(V(0, 1));
            _grid.UpdateDrag(V(4, 1));
            Assert.AreEqual(5, _manager.DragCells.Count);
            Assert.AreEqual(2, _manager.DragValidCount);
            Assert.AreEqual(200, _manager.DragTotalCost);
            Assert.IsTrue(_manager.IsDragCellValid(0) && _manager.IsDragCellValid(1));
            Assert.IsFalse(_manager.IsDragCellValid(2) || _manager.IsDragCellValid(3) || _manager.IsDragCellValid(4));
            Assert.Greater(changed, 0);

            var ghosts = Object.FindObjectsOfType<GameObject>().Count(g => g.name.StartsWith("DragPreview_") && g.activeSelf);
            Assert.AreEqual(5, ghosts, "칸마다 미리보기");
            Assert.AreEqual(0, Object.FindObjectsOfType<BuildingInstance>().Count(b => b.Data == _house));

            _grid.EndDrag();
            Assert.AreEqual(2, CountOf(_house));
            Assert.AreEqual(0, Object.FindObjectsOfType<GameObject>().Count(g => g.name.StartsWith("DragPreview_")), "미리보기 정리");
        }

        [Test]
        public void 건설_모드를_끄면_드래그도_끝나고_짓지_않는다()
        {
            _manager.SelectBuildingToBuild(_road);
            _grid.BeginDrag(V(0, 0));
            _grid.UpdateDrag(V(5, 0));
            _manager.CancelBuildingMode();
            Assert.IsFalse(_manager.IsDragPlacing);
            _grid.EndDrag();
            Assert.AreEqual(0, CountOf(_road));
        }

        [Test]
        public void 건설_모드가_아닐_때_드래그는_무시()
        {
            _grid.BeginDrag(V(0, 0));
            _grid.UpdateDrag(V(5, 0));
            Assert.IsFalse(_manager.IsDragPlacing);
            _grid.EndDrag();
            Assert.AreEqual(0, Object.FindObjectsOfType<BuildingInstance>().Length);
        }

        [Test]
        public void 한_번_설치_설정이면_드래그가_끝나면_건설_모드도_끝난다()
        {
            TestWorld.SetField(_manager, "singlePlacement", true);
            _manager.SelectBuildingToBuild(_road);
            _grid.BeginDrag(V(0, 0));
            _grid.UpdateDrag(V(5, 0));
            _grid.EndDrag();
            Assert.AreEqual(6, CountOf(_road), "한 번 설치 설정이어도 드래그한 만큼은 짓는다");
            Assert.IsFalse(_manager.IsPlacingBuilding);
        }

        [Test]
        public void 드래그를_끄면_누르는_순간_한_개만()
        {
            _manager.DragPlacementEnabled = false;
            _manager.SelectBuildingToBuild(_road);
            _grid.BeginDrag(V(0, 0));
            Assert.AreEqual(1, CountOf(_road));
            Assert.IsFalse(_manager.IsDragPlacing);
            _grid.UpdateDrag(V(5, 0));
            _grid.EndDrag();
            Assert.AreEqual(1, CountOf(_road));
        }

        [Test]
        public void 그리드_밖_칸은_드래그_끝_칸이_되지_않는다()
        {
            _grid.BeginDrag(V(0, 0));
            _grid.UpdateDrag(V(70, 0));
            Assert.AreEqual(V(0, 0), _grid.DragCurrentCell);
            _grid.UpdateDrag(V(3, 0));
            Assert.AreEqual(V(3, 0), _grid.DragCurrentCell);
        }

        // ─── 철거 API (5번 대비) ───

        [Test]
        public void TryDemolish는_게임_오버면_false()
        {
            Area(_road, 0, 0, 0, 0);
            var road = _manager.GetBuildingAt(V(0, 0));
            var time = _world.CreateManager<InfraTown.TimeSystem.TimeManager>();
            var outcome = _world.CreateManager<GameOutcomeManager>();
            outcome.ApplySaveState(new OutcomeSaveState { result = (int)GameResult.Defeat });

            Assert.IsFalse(BuildingInstance.CanDemolishNow);
            Assert.IsFalse(road.TryDemolish());
            Assert.IsTrue(road != null);
        }
    }
}

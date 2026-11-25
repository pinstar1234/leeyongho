using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Power;

namespace InfraTown.Tests
{
    /// <summary>
    /// 사용자 기획 2026-11-24: 건물은 한 면 이상(상하좌우) 도로에 맞닿아야 설치할 수 있다.
    /// 도로는 어디든, 전신주는 도로 위에도(또는 도로에 맞닿은 맨땅에) 세울 수 있다. 전기는 따로 전봇대 범위로 판정
    /// </summary>
    public class RoadAccessTests
    {
        private TestWorld _world;
        private BuildingManager _manager;
        private GameObject _prefab;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private BuildingData _road, _house, _pole, _large;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _manager = _world.CreateManager<BuildingManager>(callStart: true); // 철거 알림 구독
            _prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);

            _road = Data(BuildingType.Infrastructure, "road", "도로");
            _house = Data(BuildingType.Housing, "house", "주택");
            _pole = Data(BuildingType.Infrastructure, "power_pole", "전신주");
            _pole.isPowerPole = true;
            _large = Data(BuildingType.Facility, "large", "큰 시설", BuildingSizeType.Large);
        }

        [TearDown]
        public void TearDown()
        {
            _manager.CancelBuildingMode();
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            Object.DestroyImmediate(_prefab);
            _world.Dispose();
        }

        private BuildingData Data(BuildingType category, string id, string name, BuildingSizeType size = BuildingSizeType.Small)
        {
            var data = _world.CreateData(category, id: id, name: name, size: size);
            data.buildingPrefab = _prefab;
            return data;
        }

        /// <summary>실제 설치 경로로 설치. 성공하면 그 건물, 실패하면 null</summary>
        private BuildingInstance Place(BuildingData data, int x, int y)
        {
            var cell = new Vector2Int(x, y);
            // 도로는 전신주 아래 칸(일반 칸)을 보고, 나머지는 위에 선 전신주부터 본다
            BuildingInstance At() => data.IsRoad ? TestWorld.GetField<BuildingInstance[,]>(_manager, "_occupiedGrid")[x, y] : _manager.GetBuildingAt(cell);
            var before = At();
            _manager.SelectBuildingToBuild(data);
            TestWorld.Invoke(_manager, "TryPlaceBuilding", cell);
            _manager.CancelBuildingMode();
            var placed = At();
            if (placed == null || placed == before || placed.Data != data) return null;
            _spawned.Add(placed.gameObject);
            return placed;
        }

        private void Demolish(BuildingInstance b)
        {
            TestWorld.Invoke(b, "NotifyDestroyed"); // DestroyBuilding()의 알림 단계 (EditMode에서는 Destroy 대신)
            _spawned.Remove(b.gameObject);
            Object.DestroyImmediate(b.gameObject);
        }

        [Test]
        public void 도로는_어디든_지을_수_있다()
        {
            Assert.IsNotNull(Place(_road, 10, 10));
            Assert.IsTrue(_manager.IsRoadCell(10, 10));
        }

        [Test]
        public void 도로에_닿지_않으면_건물을_지을_수_없고_이유를_알려준다()
        {
            string rejected = null;
            _manager.OnPlacementRejected += (data, cell, reason) => rejected = reason;

            Assert.IsNull(Place(_house, 5, 5));
            StringAssert.Contains("도로", _manager.LastPlacementError);
            Assert.AreEqual(_manager.LastPlacementError, rejected);
            Assert.IsFalse(_manager.CanPlaceAt(_house, new Vector2Int(5, 5), out _));
        }

        [TestCase(10, 11)] // 위
        [TestCase(10, 9)]  // 아래
        [TestCase(9, 10)]  // 왼쪽
        [TestCase(11, 10)] // 오른쪽
        public void 한_면이라도_도로에_맞닿으면_지을_수_있다(int x, int y)
        {
            Place(_road, 10, 10);
            Assert.IsNotNull(Place(_house, x, y));
            Assert.IsNull(_manager.LastPlacementError);
        }

        [Test]
        public void 대각선으로만_닿으면_지을_수_없다()
        {
            Place(_road, 10, 10);
            Assert.IsNull(Place(_house, 11, 11));
        }

        [Test]
        public void 큰_건물은_둘레_어느_칸이든_도로에_닿으면_된다()
        {
            Place(_road, 13, 9); // 4x4(10~13, 10~13)의 아래쪽 오른쪽 끝 칸과 맞닿음
            Assert.IsTrue(_manager.HasRoadAccess(_large, new Vector2Int(10, 10)));

            Place(_road, 20, 20);
            Assert.IsFalse(_manager.HasRoadAccess(_large, new Vector2Int(21, 21)), "모서리 대각선(20,20)만 닿음");
            Assert.IsNotNull(Place(_large, 10, 10));
        }

        [Test]
        public void 전신주는_도로_위에_세울_수_있고_도로는_그대로_남는다()
        {
            var road = Place(_road, 10, 10);
            var pole = Place(_pole, 10, 10);

            Assert.IsNotNull(pole, "도로 위 전신주 설치 실패");
            Assert.AreSame(pole, _manager.GetRoadPoleAt(new Vector2Int(10, 10)));
            Assert.AreSame(pole, _manager.GetBuildingAt(new Vector2Int(10, 10)), "칸 조회는 위에 선 전신주를 먼저");
            Assert.IsTrue(_manager.IsRoadCell(10, 10), "전신주를 세워도 도로는 도로");
            Assert.AreSame(road, TestWorld.GetField<BuildingInstance[,]>(_manager, "_occupiedGrid")[10, 10]);
            Assert.IsNotNull(pole.GetComponent<UtilityPoleInstance>());

            // 도로 위 전신주가 있어도 그 도로에 맞닿은 건물은 지을 수 있음
            Assert.IsNotNull(Place(_house, 10, 11));
        }

        [Test]
        public void 도로_위_전신주_칸에는_다른_전신주나_건물을_지을_수_없다()
        {
            Place(_road, 10, 10);
            Place(_pole, 10, 10);
            Assert.IsNull(Place(_pole, 10, 10), "같은 칸에 전신주 두 개");
            Assert.IsFalse(_manager.IsAreaFree(_house, new Vector2Int(10, 10), out _));
        }

        [Test]
        public void 전신주도_맨땅이면_도로에_맞닿아야_한다()
        {
            Assert.IsNull(Place(_pole, 20, 20), "도로 없는 맨땅");
            Place(_road, 10, 10);
            var pole = Place(_pole, 11, 10);
            Assert.IsNotNull(pole, "도로 옆 맨땅");
            Assert.IsNull(_manager.GetRoadPoleAt(new Vector2Int(11, 10)), "맨땅 전신주는 일반 칸에 등록");
        }

        [Test]
        public void 다른_건물_위에는_전신주를_세울_수_없다()
        {
            Place(_road, 10, 10);
            Place(_house, 10, 11);
            Assert.IsNull(Place(_pole, 10, 11));
        }

        [Test]
        public void 도로를_철거해도_전신주는_남고_그_아래에_도로를_다시_깔_수_있다()
        {
            var road = Place(_road, 10, 10);
            var pole = Place(_pole, 10, 10);
            Demolish(road);

            Assert.IsFalse(_manager.IsRoadCell(10, 10));
            Assert.AreSame(pole, _manager.GetBuildingAt(new Vector2Int(10, 10)), "도로가 없어져도 전신주는 그대로");
            Assert.IsFalse(_manager.IsAreaFree(_house, new Vector2Int(10, 10), out _), "전신주 자리에 다른 건물 불가");

            var newRoad = Place(_road, 10, 10);
            Assert.IsNotNull(newRoad, "전신주 아래에 도로 다시 깔기");
            Assert.IsTrue(_manager.IsRoadCell(10, 10));

            Demolish(pole);
            Assert.IsNull(_manager.GetRoadPoleAt(new Vector2Int(10, 10)), "철거하면 도로 위 전신주 칸도 비워짐");
            Assert.AreSame(newRoad, _manager.GetBuildingAt(new Vector2Int(10, 10)));
        }

        [Test]
        public void 이미_지은_건물은_도로가_없어져도_그대로_남는다()
        {
            var road = Place(_road, 10, 10);
            var house = Place(_house, 10, 11);
            Demolish(road);
            Assert.AreSame(house, _manager.GetBuildingAt(new Vector2Int(10, 11)));
        }

        [Test]
        public void 세이브_복원은_도로_조건_없이_되살리고_도로_위_전신주도_복원한다()
        {
            Assert.IsNotNull(_manager.RestoreBuilding(_house, new Vector2Int(30, 30), 0f), "도로 없는 주택도 저장된 그대로 복원");
            Assert.IsNotNull(_manager.RestoreBuilding(_road, new Vector2Int(5, 5), 0f));
            var pole = _manager.RestoreBuilding(_pole, new Vector2Int(5, 5), 0f);
            Assert.IsNotNull(pole, "도로 위 전신주 복원");
            Assert.AreSame(pole, _manager.GetRoadPoleAt(new Vector2Int(5, 5)));
            foreach (var cell in new[] { new Vector2Int(30, 30), new Vector2Int(5, 5) })
            {
                var b = TestWorld.GetField<BuildingInstance[,]>(_manager, "_occupiedGrid")[cell.x, cell.y];
                if (b != null) _spawned.Add(b.gameObject);
            }
            _spawned.Add(pole.gameObject);
        }

        [Test]
        public void 도로_조건을_끄면_어디든_지을_수_있다()
        {
            _manager.RequireRoadAccess = false;
            Assert.IsNotNull(Place(_house, 5, 5));
        }

        [Test]
        public void 도로는_도로_분류이고_전신주는_도로가_아니다()
        {
            Assert.IsTrue(_road.IsRoad);
            Assert.IsFalse(_pole.IsRoad);
            Assert.IsFalse(_house.IsRoad);
        }
    }
}

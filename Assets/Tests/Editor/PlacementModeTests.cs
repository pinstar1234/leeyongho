using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using InfraTown.Building;

namespace InfraTown.Tests
{
    /// <summary>
    /// 건설 모드 상태 알림 (BuildDockUI가 사용): IsPlacingBuilding, OnPlacementModeChanged
    /// </summary>
    public class PlacementModeTests
    {
        private TestWorld _world;
        private BuildingManager _manager;
        private GameObject _prefab;
        private readonly List<bool> _events = new List<bool>();

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _manager = _world.CreateManager<BuildingManager>();
            _prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _events.Clear();
            _manager.OnPlacementModeChanged += on => _events.Add(on);
        }

        [TearDown]
        public void TearDown()
        {
            _manager.CancelBuildingMode();
            Object.DestroyImmediate(_prefab);
            _world.Dispose();
        }

        private BuildingData Data(string id)
        {
            var data = _world.CreateData(BuildingType.Facility, id: id);
            data.buildingPrefab = _prefab;
            return data;
        }

        [Test]
        public void 건물을_고르면_배치_중이고_취소하면_끝난다()
        {
            Assert.IsFalse(_manager.IsPlacingBuilding);

            _manager.SelectBuildingToBuild(Data("a"));
            Assert.IsTrue(_manager.IsPlacingBuilding);

            _manager.CancelBuildingMode();
            Assert.IsFalse(_manager.IsPlacingBuilding);
            CollectionAssert.AreEqual(new[] { true, false }, _events);
        }

        [Test]
        public void 다른_건물로_바꿔_골라도_다시_알리지_않는다()
        {
            _manager.SelectBuildingToBuild(Data("a"));
            _manager.SelectBuildingToBuild(Data("b"));
            Assert.IsTrue(_manager.IsPlacingBuilding);
            CollectionAssert.AreEqual(new[] { true }, _events, "꺼짐→켜짐 깜빡임 없음");
        }

        [Test]
        public void 배치_중이_아닐_때_취소해도_알리지_않는다()
        {
            _manager.CancelBuildingMode();
            Assert.IsEmpty(_events);
        }

        [Test]
        public void 잠긴_건물은_골라지지_않아_배치_모드가_켜지지_않는다()
        {
            var locked = Data("locked");
            locked.requiredPopulation = 9999;
            _manager.SelectBuildingToBuild(locked);
            Assert.IsFalse(_manager.IsPlacingBuilding);
            Assert.IsEmpty(_events);
        }
    }
}

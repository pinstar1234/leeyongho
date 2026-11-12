using System.Collections.Generic;
using NUnit.Framework;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Services;

namespace InfraTown.Tests
{
    /// <summary>
    /// 건물이 어떤 경로로 삭제돼도(DestroyBuilding, UI의 Destroy(gameObject)) 철거 알림이 한 번만 가는지
    /// </summary>
    public class BuildingDestroyTests
    {
        private TestWorld _world;
        private int _notified;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _notified = 0;
            BuildingInstance.OnBuildingDestroyed += Count;
        }

        [TearDown]
        public void TearDown()
        {
            BuildingInstance.OnBuildingDestroyed -= Count;
            _world.Dispose();
        }

        private void Count(BuildingInstance b) => _notified++;

        [Test]
        public void Destroy만_불려도_OnDestroy에서_철거를_알린다()
        {
            var building = _world.CreateBuilding(_world.CreateData(BuildingType.Facility), 0, 0);

            TestWorld.Invoke(building, "OnDestroy"); // Destroy(gameObject) 시 Unity가 호출하는 경로

            Assert.AreEqual(1, _notified);
        }

        [Test]
        public void DestroyBuilding_후_OnDestroy가_와도_한번만_알린다()
        {
            var building = _world.CreateBuilding(_world.CreateData(BuildingType.Facility), 0, 0);

            TestWorld.Invoke(building, "NotifyDestroyed"); // DestroyBuilding()의 알림 단계
            TestWorld.Invoke(building, "OnDestroy");

            Assert.AreEqual(1, _notified);
        }

        [Test]
        public void 데이터가_없는_오브젝트는_알리지_않는다()
        {
            var go = new UnityEngine.GameObject("no data");
            var building = go.AddComponent<BuildingInstance>();
            try
            {
                TestWorld.Invoke(building, "OnDestroy");
                Assert.AreEqual(0, _notified);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void UI처럼_직접_삭제해도_접근성이_갱신된다()
        {
            var service = _world.CreateManager<ServiceAccessibilityManager>();
            var loop = _world.CreateManager<GameLoopManager>(callStart: true);
            var store = _world.CreateBuilding(_world.CreateData(BuildingType.Facility, id: "convenience_store", name: "편의점"), 0, 0);
            loop.RegisterBuilding(store);
            Assert.IsTrue(service.HasConvenienceStore);

            TestWorld.Invoke(store, "OnDestroy");

            Assert.IsFalse(service.HasConvenienceStore);
            CollectionAssert.DoesNotContain(new List<BuildingInstance>(loop.PlacedBuildings), store);
        }
    }
}

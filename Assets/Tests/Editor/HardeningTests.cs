using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.EventSystem;
using InfraTown.Grid;
using InfraTown.Population;
using InfraTown.Power;
using InfraTown.Satisfaction;
using InfraTown.Services;
using InfraTown.TimeSystem;

namespace InfraTown.Tests
{
    /// <summary>
    /// 취약점 점검(높음 3건) 수정 확인:
    ///  1) 중복 싱글톤이 같은 오브젝트의 다른 컴포넌트까지 지우지 않음
    ///  2) UI 위 클릭은 그리드 클릭으로 처리하지 않음
    ///  3) 삭제된 오브젝트가 건 일시정지·패배 유예는 자동으로 풀림
    /// </summary>
    public class HardeningTests
    {
        private TestWorld _world;
        private readonly List<GameObject> _extra = new List<GameObject>();

        [SetUp]
        public void SetUp() => _world = new TestWorld();

        [TearDown]
        public void TearDown()
        {
            PointerGuard.OverrideForTests = null;
            foreach (var go in _extra) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _extra.Clear();
            typeof(LightweightGridManager).GetProperty("Instance").SetValue(null, null);
            _world.Dispose();
        }

        private GameObject Extra(string name)
        {
            var go = new GameObject(name);
            _extra.Add(go);
            return go;
        }

        private static void Awake(MonoBehaviour mb) => TestWorld.Invoke(mb, "Awake");

        // ── 1) 중복 싱글톤 ──

        private static readonly Type[] ManagerTypes =
        {
            typeof(BuildingManager), typeof(EconomyManager), typeof(PopulationManager), typeof(PowerManager),
            typeof(SatisfactionManager), typeof(ServiceAccessibilityManager), typeof(TimeManager),
            typeof(GameLoopManager), typeof(GameOutcomeManager), typeof(DialogueEventManager)
        };

        [Test]
        public void 중복_매니저는_자기_컴포넌트만_제거되고_오브젝트와_다른_컴포넌트는_남는다(
            [ValueSource(nameof(ManagerTypes))] Type managerType)
        {
            var first = (MonoBehaviour)typeof(TestWorld).GetMethod("CreateManager")
                .MakeGenericMethod(managerType).Invoke(_world, new object[] { false });

            var shared = Extra("공용 오브젝트");
            var other = shared.AddComponent<BoxCollider>();
            var duplicate = (MonoBehaviour)shared.AddComponent(managerType);
            Awake(duplicate);

            Assert.IsTrue(shared != null, "오브젝트가 지워지면 안 됨");
            Assert.IsTrue(other != null, "같은 오브젝트의 다른 컴포넌트가 지워지면 안 됨");
            Assert.IsTrue(duplicate == null, "중복 매니저 컴포넌트는 제거");
            Assert.AreSame(first, managerType.GetProperty("Instance").GetValue(null), "기존 Instance 유지");
        }

        [Test]
        public void GridManager_오브젝트에_중복_BuildingManager가_있어도_그리드는_살아남는다()
        {
            var main = _world.CreateManager<BuildingManager>();

            var gridObject = Extra("GridManager");
            var grid = gridObject.AddComponent<LightweightGridManager>();
            Awake(grid);
            var duplicate = gridObject.AddComponent<BuildingManager>();
            Awake(duplicate);

            Assert.IsTrue(grid != null);
            Assert.AreSame(grid, LightweightGridManager.Instance);
            Assert.AreSame(main, BuildingManager.Instance);
        }

        // ── 2) UI 클릭 ──

        private LightweightGridManager GridHoveringAt(Vector2Int cell)
        {
            var grid = Extra("Grid").AddComponent<LightweightGridManager>();
            Awake(grid);
            typeof(LightweightGridManager).GetProperty("HoveredGridPos").SetValue(grid, cell);
            return grid;
        }

        [Test]
        public void UI_위를_누르면_그리드_클릭이_발생하지_않는다()
        {
            var grid = GridHoveringAt(new Vector2Int(3, 4));
            int clicks = 0;
            grid.OnGridClicked += p => clicks++;

            PointerGuard.OverrideForTests = () => true;
            Assert.IsFalse(grid.TryRaiseClick());
            Assert.AreEqual(0, clicks);
        }

        [Test]
        public void UI_밖을_누르면_그_칸이_클릭된다()
        {
            var grid = GridHoveringAt(new Vector2Int(3, 4));
            Vector2Int clicked = new Vector2Int(-1, -1);
            grid.OnGridClicked += p => clicked = p;

            PointerGuard.OverrideForTests = () => false;
            Assert.IsTrue(grid.TryRaiseClick());
            Assert.AreEqual(new Vector2Int(3, 4), clicked);
        }

        [Test]
        public void 그리드_밖에서는_클릭이_발생하지_않는다()
        {
            var grid = GridHoveringAt(new Vector2Int(-1, -1));
            PointerGuard.OverrideForTests = () => false;
            Assert.IsFalse(grid.TryRaiseClick());
        }

        [Test]
        public void EventSystem이_없으면_UI_위가_아니다()
        {
            Assert.IsFalse(PointerGuard.IsPointerOverUI());
        }

        // ── 3) 삭제된 요청자의 잠금 자동 해제 ──

        [Test]
        public void 일시정지를_건_오브젝트가_삭제되면_다음_프레임에_시간이_다시_흐른다()
        {
            var time = _world.CreateManager<TimeManager>();
            var popup = Extra("팝업");
            time.RequestEventPause(popup);
            Assert.IsTrue(time.IsEventPaused);
            Assert.AreEqual(0f, Time.timeScale);

            UnityEngine.Object.DestroyImmediate(popup);
            TestWorld.Invoke(time, "Update");

            Assert.IsFalse(time.IsEventPaused);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [Test]
        public void 살아있는_요청자나_일반_객체의_일시정지는_유지된다()
        {
            var time = _world.CreateManager<TimeManager>();
            var popup = Extra("팝업");
            var plain = new object();
            time.RequestEventPause(popup);
            time.RequestEventPause(plain);

            TestWorld.Invoke(time, "Update");

            Assert.AreEqual(2, time.PauseLockCount);
        }

        [Test]
        public void null_요청은_무시된다()
        {
            var time = _world.CreateManager<TimeManager>();
            time.RequestEventPause(null);
            Assert.IsFalse(time.IsEventPaused);
        }

        [Test]
        public void 유예를_건_공사가_삭제되면_패배_유예도_끝난다()
        {
            var outcome = _world.CreateManager<GameOutcomeManager>();
            var construction = Extra("공사 중 건물");
            outcome.RequestDefeatGrace(construction);
            Assert.IsTrue(outcome.IsDefeatGraceActive);

            UnityEngine.Object.DestroyImmediate(construction);
            outcome.ProcessWeek();

            Assert.IsFalse(outcome.IsDefeatGraceActive);
        }
    }
}

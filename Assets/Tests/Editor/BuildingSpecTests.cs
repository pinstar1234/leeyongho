using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Power;
using InfraTown.Services;

namespace InfraTown.Tests
{
    /// <summary>
    /// Assets/Data/Build/Spec 의 건물 데이터가 기획서 v5 4장·3장과 맞는지 검증
    /// </summary>
    public class BuildingSpecTests
    {
        private const string SpecFolder = "Assets/Data/Build/Spec";

        private static List<BuildingData> LoadAll() =>
            AssetDatabase.FindAssets("t:BuildingData", new[] { SpecFolder })
                .Select(guid => AssetDatabase.LoadAssetAtPath<BuildingData>(AssetDatabase.GUIDToAssetPath(guid)))
                .ToList();

        private static BuildingData Get(string id) => LoadAll().Single(d => d.buildingID == id);

        [Test]
        public void 기획서_건물_13종이_모두_있고_ID가_겹치지_않는다()
        {
            var all = LoadAll();
            Assert.AreEqual(13, all.Count);
            Assert.AreEqual(13, all.Select(d => d.buildingID).Distinct().Count());
            Assert.IsTrue(all.All(d => d.buildingPrefab != null), "모든 건물에 (임시) 프리팹이 연결되어야 한다");
        }

        // id, 크기, 분류, 티어, 건설 일수, 철거 일수 (기획서 4장 표 / 3장 건설·철거 기간)
        [TestCase("house", BuildingSizeType.Small, BuildingType.Housing, 1, 7, 1)]
        [TestCase("road", BuildingSizeType.Small, BuildingType.Infrastructure, 1, 0, 0)]
        [TestCase("power_pole", BuildingSizeType.Small, BuildingType.Infrastructure, 1, 0, 0)]
        [TestCase("village_hall", BuildingSizeType.Medium, BuildingType.Facility, 1, 14, 2)]
        [TestCase("convenience_store", BuildingSizeType.Medium, BuildingType.Facility, 2, 14, 2)]
        [TestCase("bus_stop", BuildingSizeType.Medium, BuildingType.Facility, 2, 14, 2)]
        [TestCase("senior_center", BuildingSizeType.Medium, BuildingType.Facility, 2, 14, 2)]
        [TestCase("livestock_farm", BuildingSizeType.Medium, BuildingType.Nimbys, 2, 14, 2)]
        [TestCase("fire_station", BuildingSizeType.Medium, BuildingType.Essential, 3, 14, 2)]
        [TestCase("police_station", BuildingSizeType.Medium, BuildingType.Essential, 3, 14, 2)]
        [TestCase("school", BuildingSizeType.Large, BuildingType.Essential, 4, 28, 3)]
        [TestCase("hospital", BuildingSizeType.Large, BuildingType.Essential, 4, 28, 3)]
        [TestCase("landfill", BuildingSizeType.Large, BuildingType.Nimbys, 4, 28, 3)]
        public void 크기_분류_티어_공사기간이_기획서와_같다(string id, BuildingSizeType size, BuildingType category, int tier, int buildDays, int demolishDays)
        {
            var data = Get(id);
            Assert.AreEqual(size, data.sizeType);
            Assert.AreEqual(category, data.category);
            Assert.AreEqual(tier, data.unlockTier);
            Assert.AreEqual(buildDays, data.constructionDays);
            Assert.AreEqual(demolishDays, data.demolitionDays);
            Assert.IsNotEmpty(data.description);
            Assert.IsNotEmpty(data.easyDescription);
            Assert.IsNotEmpty(data.unlockMessage);
        }

        [TestCase("fire_station", 12)]
        [TestCase("police_station", 12)]
        [TestCase("school", 15)]
        [TestCase("hospital", 15)]
        public void 필수시설_만족도_반경이_기획서와_같다(string id, int radius)
        {
            var data = Get(id);
            Assert.AreEqual(radius, data.satisfactionRadius);
            Assert.Greater(data.satisfactionImpact, 0f);
        }

        [TestCase("livestock_farm")]
        [TestCase("landfill")]
        public void 불쾌시설은_만족도를_깎는다(string id)
        {
            Assert.Less(Get(id).satisfactionImpact, 0f);
        }

        [Test]
        public void 높은_티어일수록_해금_인구가_같거나_많다()
        {
            var byTier = LoadAll().GroupBy(d => d.unlockTier).OrderBy(g => g.Key).ToList();
            for (int i = 1; i < byTier.Count; i++)
            {
                Assert.GreaterOrEqual(byTier[i].Min(d => d.requiredPopulation), byTier[i - 1].Max(d => d.requiredPopulation));
            }
            Assert.IsTrue(byTier[0].All(d => d.requiredPopulation == 0), "Tier 1은 처음부터 지을 수 있어야 한다");
        }

        [Test]
        public void 주택만_수용인구가_있다()
        {
            Assert.Greater(Get("house").housingCapacity, 0);
            Assert.IsTrue(LoadAll().Where(d => d.category != BuildingType.Housing).All(d => d.housingCapacity == 0));
        }

        [Test]
        public void 전신주만_전력연결_건물이다()
        {
            Assert.IsTrue(Get("power_pole").isPowerPole);
            Assert.AreEqual(1, LoadAll().Count(d => d.isPowerPole));
        }

        [TestCase("hospital", ServiceType.Hospital)]
        [TestCase("school", ServiceType.School)]
        [TestCase("fire_station", ServiceType.FireStation)]
        [TestCase("police_station", ServiceType.Police)]
        [TestCase("convenience_store", ServiceType.Convenience)]
        public void 서비스_시설로_인식된다(string id, ServiceType expected)
        {
            Assert.AreEqual(expected, ServiceAccessibilityManager.GetServiceType(Get(id)));
        }

        [TestCase("house")]
        [TestCase("road")]
        [TestCase("village_hall")]
        [TestCase("bus_stop")]
        [TestCase("senior_center")]
        [TestCase("livestock_farm")]
        [TestCase("landfill")]
        public void 서비스_시설이_아닌_건물은_접근성_계산에서_빠진다(string id)
        {
            Assert.IsNull(ServiceAccessibilityManager.GetServiceType(Get(id)));
        }
    }

    public class BuildingPlacementTests
    {
        private TestWorld _world;
        private BuildingManager _manager;

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _manager = _world.CreateManager<BuildingManager>();
        }

        [TearDown]
        public void TearDown()
        {
            // TryPlaceBuilding이 Instantiate한 건물 정리
            var grid = TestWorld.GetField<BuildingInstance[,]>(_manager, "_occupiedGrid");
            foreach (var instance in grid)
            {
                if (instance != null) Object.DestroyImmediate(instance.gameObject);
            }
            _world.Dispose();
        }

        private BuildingInstance Place(BuildingData data, int x, int y)
        {
            TestWorld.SetField(_manager, "_selectedBuildingData", data);
            TestWorld.Invoke(_manager, "TryPlaceBuilding", new Vector2Int(x, y));
            return TestWorld.GetField<BuildingInstance[,]>(_manager, "_occupiedGrid")[x, y];
        }

        [Test]
        public void 전신주를_지으면_전력연결_컴포넌트가_붙는다()
        {
            var pole = Place(AssetDatabase.LoadAssetAtPath<BuildingData>("Assets/Data/Build/Spec/T1_PowerPole.asset"), 3, 3);
            Assert.IsNotNull(pole);
            Assert.IsNotNull(pole.GetComponent<UtilityPoleInstance>());
            Assert.AreEqual(new Vector2Int(3, 3), pole.GetComponent<UtilityPoleInstance>().GridPos);
        }

        [Test]
        public void 일반_건물에는_전력연결_컴포넌트가_붙지_않는다()
        {
            var road = Place(AssetDatabase.LoadAssetAtPath<BuildingData>("Assets/Data/Build/Spec/T1_Road.asset"), 3, 3);
            Assert.IsNotNull(road);
            Assert.IsNull(road.GetComponent<UtilityPoleInstance>());
        }

        [Test]
        public void 같은_분류의_건물을_모두_가져온다()
        {
            var facilities = AssetDatabase.FindAssets("t:BuildingData", new[] { "Assets/Data/Build/Spec" })
                .Select(g => AssetDatabase.LoadAssetAtPath<BuildingData>(AssetDatabase.GUIDToAssetPath(g)))
                .ToList();
            TestWorld.SetField(_manager, "buildingDatabase", facilities);

            var list = _manager.GetBuildingsByCategory(BuildingType.Facility);

            CollectionAssert.AreEquivalent(
                new[] { "village_hall", "convenience_store", "bus_stop", "senior_center" },
                list.Select(d => d.buildingID).ToArray());
        }

        [Test]
        public void 인구_0에서는_티어1만_해금된다()
        {
            var all = AssetDatabase.FindAssets("t:BuildingData", new[] { "Assets/Data/Build/Spec" })
                .Select(g => AssetDatabase.LoadAssetAtPath<BuildingData>(AssetDatabase.GUIDToAssetPath(g)))
                .ToList();
            TestWorld.SetField(_manager, "buildingDatabase", all);

            Assert.IsTrue(_manager.GetUnlockedBuildings().All(d => d.unlockTier == 1));
            Assert.AreEqual(4, _manager.GetUnlockedBuildings().Count);
        }
    }
}

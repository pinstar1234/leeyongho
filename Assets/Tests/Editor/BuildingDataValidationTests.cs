using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using InfraTown.Building;

namespace InfraTown.Tests
{
    /// <summary>
    /// 건물 데이터 값 검사 (2026-11-26): 잘못 넣은 값이 조용히 게임을 망가뜨리지 않도록 BuildingData.Validate / OnValidate.
    /// </summary>
    public class BuildingDataValidationTests
    {
        [Test]
        public void 기획서_건물_에셋은_모두_값_검사를_통과한다()
        {
            var guids = AssetDatabase.FindAssets("t:BuildingData", new[] { "Assets/Data/Build/Spec" });
            Assert.That(guids.Length, Is.GreaterThanOrEqualTo(13), "기획서 건물 13종");
            var report = guids
                .Select(g => AssetDatabase.LoadAssetAtPath<BuildingData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null)
                .Select(d => (d.name, problems: d.Validate(fix: false)))
                .Where(r => r.problems.Count > 0)
                .Select(r => $"{r.name}: {string.Join(" / ", r.problems)}")
                .ToList();
            Assert.IsEmpty(report, string.Join("\n", report));
        }

        [Test]
        public void 음수_값은_0으로_고치고_문제를_알린다()
        {
            var data = ScriptableObject.CreateInstance<BuildingData>();
            try
            {
                data.buildingID = "test";
                data.buildingName = "테스트";
                data.category = BuildingType.Facility;
                data.constructionCost = -500;
                data.maintenanceCost = -10;
                data.requiredPower = -5;
                data.fixedAccessTimeMinutes = -1f;

                var problems = data.Validate(fix: true);

                Assert.AreEqual(4, problems.Count, string.Join(" / ", problems));
                Assert.AreEqual(0, data.constructionCost, "음수 건설비면 지을 때 돈이 늘지는 않지만 공짜가 되므로 0");
                Assert.AreEqual(0, data.maintenanceCost);
                Assert.AreEqual(0, data.requiredPower);
                Assert.AreEqual(0f, data.fixedAccessTimeMinutes);
                Assert.IsEmpty(data.Validate(fix: false), "고친 뒤에는 문제 없음");
            }
            finally { Object.DestroyImmediate(data); }
        }

        [Test]
        public void 고칠_수_없는_문제는_알리기만_한다()
        {
            var data = ScriptableObject.CreateInstance<BuildingData>();
            try
            {
                data.buildingID = "";
                data.buildingName = "주택";
                data.category = BuildingType.Housing;
                data.housingCapacity = 0;

                var problems = data.Validate(fix: true);

                Assert.IsTrue(problems.Any(p => p.Contains("buildingID")));
                Assert.IsTrue(problems.Any(p => p.Contains("housingCapacity가 0")));
                Assert.AreEqual("", data.buildingID, "ID는 바꾸지 않음");
            }
            finally { Object.DestroyImmediate(data); }
        }

        [Test]
        public void 전신주인데_공급_반경이_0이면_알린다()
        {
            var data = ScriptableObject.CreateInstance<BuildingData>();
            try
            {
                data.buildingID = "pole";
                data.buildingName = "전신주";
                data.category = BuildingType.Infrastructure;
                data.isPowerPole = true;
                data.powerSupplyRadius = 0;
                Assert.IsTrue(data.Validate(fix: false).Any(p => p.Contains("powerSupplyRadius가 0")));
            }
            finally { Object.DestroyImmediate(data); }
        }
    }
}

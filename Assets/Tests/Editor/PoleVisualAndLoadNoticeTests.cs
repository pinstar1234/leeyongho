using System;
using NUnit.Framework;
using UnityEngine;
using InfraTown.Power;
using InfraTown.Save;
using Object = UnityEngine.Object;

namespace InfraTown.Tests
{
    /// <summary>
    /// 2026-11-26 취약점 수정:
    /// 1) 전봇대 정전 표시가 머티리얼 색을 흰색으로 덮어쓰고 복사본을 만들던 문제 → MaterialPropertyBlock으로 자식 렌더러까지 어둡게, 복구되면 원래대로
    /// 2) 불러오기 실패 안내 이벤트에 사라진 UI가 남으면 임시 창이 안 뜨던 문제 → 사라진·예외 나는 구독자는 빼고 판단
    /// </summary>
    public class PoleVisualAndLoadNoticeTests
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static Color ColorOf(Material m) => m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : m.GetColor(ColorId);

        private static void AssertColor(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, 0.001f, message);
            Assert.AreEqual(expected.g, actual.g, 0.001f, message);
            Assert.AreEqual(expected.b, actual.b, 0.001f, message);
        }

        private static Color BlockColor(Renderer r, Material m)
        {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            return block.GetColor(m.HasProperty(BaseColorId) ? BaseColorId : ColorId);
        }

        [Test]
        public void 정전_표시는_머티리얼을_바꾸지_않고_자식_렌더러까지_어둡게_했다가_되돌린다()
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var child = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            child.transform.SetParent(root.transform);
            var mat = new Material(root.GetComponent<Renderer>().sharedMaterial);
            var original = new Color(0.8f, 0.3f, 0.2f, 1f);
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, original);
            if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, original);
            var rootRend = root.GetComponent<Renderer>();
            var childRend = child.GetComponent<Renderer>();
            rootRend.sharedMaterial = mat;
            childRend.sharedMaterial = mat;
            try
            {
                var pole = root.AddComponent<UtilityPoleInstance>();

                pole.SetPowerState(false);
                Assert.IsTrue(pole.IsShowingBlackout);
                Assert.AreSame(mat, rootRend.sharedMaterial, "머티리얼 복사본을 만들지 않음");
                AssertColor(original, ColorOf(mat), "머티리얼 색은 그대로");
                Color dim = BlockColor(rootRend, mat);
                Assert.Less(dim.r, original.r, "맨 위 렌더러 어둡게");
                Assert.AreEqual(original.r * 0.45f, dim.r, 0.01f, "원래 색 × 정전 색 (흰색으로 덮지 않음)");
                Assert.Less(BlockColor(childRend, mat).r, original.r, "자식 렌더러도 어둡게 (새 모델처럼 자식에 렌더러가 있어도)");

                pole.SetPowerState(true);
                Assert.IsFalse(pole.IsShowingBlackout);
                var block = new MaterialPropertyBlock();
                rootRend.GetPropertyBlock(block);
                Assert.IsTrue(block.isEmpty, "전기가 들어오면 덮어쓴 색을 지움 → 원래 색");
                childRend.GetPropertyBlock(block);
                Assert.IsTrue(block.isEmpty);
                AssertColor(original, ColorOf(mat), "머티리얼 색은 끝까지 그대로");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mat);
            }
        }

        [Test]
        public void 사라진_UI의_구독은_안내_창_UI로_치지_않는다()
        {
            var oldUi = new GameObject("old title UI");
            Action<string> dead = oldUi.SendMessage; // 대상이 Unity 오브젝트인 구독 (해제하지 않고 씬이 바뀐 UI)
            SaveSystem.OnLoadFailed += dead;
            try
            {
                Assert.IsTrue(SaveSystem.HasLoadFailedUI);
                Object.DestroyImmediate(oldUi);
                Assert.IsFalse(SaveSystem.HasLoadFailedUI, "사라진 UI는 빠짐 → 임시 창이 뜸");
                Assert.IsFalse(SaveSystem.NotifyLoadFailedUI("깨짐"));
            }
            finally { SaveSystem.OnLoadFailed -= dead; }
        }

        [Test]
        public void 예외가_나는_구독자만_있으면_임시_창을_띄우고_정상_UI가_있으면_그_UI에_맡긴다()
        {
            Action<string> broken = m => throw new MissingReferenceException("사라진 화면");
            string received = null;
            Action<string> ok = m => received = m;
            SaveSystem.OnLoadFailed += broken;
            try
            {
                Assert.IsFalse(SaveSystem.NotifyLoadFailedUI("깨짐"), "창을 띄운 UI가 없음 → 임시 창");
                Assert.IsFalse(SaveSystem.HasLoadFailedUI, "예외 난 구독은 빠짐");

                SaveSystem.OnLoadFailed += broken;
                SaveSystem.OnLoadFailed += ok;
                Assert.IsTrue(SaveSystem.NotifyLoadFailedUI("깨짐"));
                Assert.AreEqual("깨짐", received);
            }
            finally
            {
                SaveSystem.OnLoadFailed -= broken;
                SaveSystem.OnLoadFailed -= ok;
            }
        }
    }
}

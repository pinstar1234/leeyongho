using System.Collections.Generic;
using NUnit.Framework;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.EventSystem;
using InfraTown.Population;
using InfraTown.Satisfaction;
using InfraTown.TimeSystem;
using InfraTown.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace InfraTown.Tests
{
    /// <summary>
    /// 한 달 스킵 (사용자 기획 2026-11-19, 2026-11-24 주 단위 변경으로 일주일 → 한 달(4주)):
    /// 누르면 4주가 한 주씩 빠르게 지나가고(값 변화가 보이게), 대화 등 이벤트가 일어나면 그 시점에서 멈춘다.
    /// </summary>
    public class TimeSkipTests
    {
        private TestWorld _world;
        private TimeManager _time;
        private readonly List<string> _log = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _world = new TestWorld();
            _time = _world.CreateManager<TimeManager>();
            _time.SkipSecondsPerWeek = 0.3f;
            _log.Clear();
            _time.OnDateChanged += (y, m, w) => _log.Add($"{m}/{w}");
            _time.OnSkipEnded += interrupted => _log.Add(interrupted ? "중단" : "완료");
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        private void AssertDate(int month, int week, string message = null)
        {
            Assert.AreEqual((month, week), (_time.CurrentMonth, _time.CurrentWeek), message);
        }

        [Test]
        public void 도움말은_월_넘기기_위에서_열리고_연속건축_시연을_전환한다()
        {
            var hud = new GameObject("HUD", typeof(RectTransform), typeof(Canvas));
            try
            {
                var skip = new GameObject("1달 넘기기", typeof(RectTransform));
                skip.transform.SetParent(hud.transform, false);
                var help = hud.AddComponent<ConstructionHelpWindow>();
                TestWorld.SetField(help, "monthSkipButton", (RectTransform)skip.transform);
                TestWorld.SetField(help, "font", AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Font/Maplestory Bold SDF.asset"));
                TestWorld.Invoke(help, "Start");

                var launcher = skip.GetComponentInChildren<Button>();
                Assert.IsNotNull(launcher.GetComponent<CanvasRenderer>(), "도움말 버튼에는 CanvasRenderer가 필요함");
                var demoGraphic = TestWorld.GetField<Graphic>(help, "demo");
                Assert.IsNotNull(demoGraphic.GetComponent<CanvasRenderer>(), "건축 시연에는 CanvasRenderer가 필요함");
                var placement = (RectTransform)launcher.transform;
                Assert.AreEqual(new Vector2(.5f, 1f), placement.anchorMin);
                Assert.AreEqual(placement.anchorMin, placement.anchorMax);
                Assert.AreEqual(new Vector2(.5f, 0f), placement.pivot);
                Assert.AreEqual(new Vector3(2.5f, 16.5f, 0f), placement.anchoredPosition3D);
                Assert.AreEqual(new Vector2(50f, 50f), placement.sizeDelta);
                launcher.onClick.Invoke();
                Assert.IsTrue(ConstructionHelpWindow.IsOpen);
                Assert.IsTrue(PointerGuard.IsPointerOverUI());
                Assert.IsFalse(_time.CanSkip);

                TestWorld.GetField<Button>(help, "dragButton").onClick.Invoke();
                Assert.AreEqual("연속건축", TestWorld.GetField<TMP_Text>(help, "title").text);
                StringAssert.Contains("드래그", TestWorld.GetField<TMP_Text>(help, "instructions").text);
                var playback = TestWorld.GetField<Button>(help, "playbackButton");
                playback.onClick.Invoke();
                Assert.AreEqual("재생", playback.GetComponentInChildren<TMP_Text>().text);
                playback.onClick.Invoke();
                Assert.AreEqual("일시정지", playback.GetComponentInChildren<TMP_Text>().text);

                Assert.IsTrue(ConstructionHelpWindow.TryCloseOpenWindow());
                Assert.IsTrue(_time.CanSkip);
            }
            finally
            {
                ConstructionHelpWindow.TryCloseOpenWindow();
                Object.DestroyImmediate(hud);
            }
        }

        [Test]
        public void 누르면_한_달이_한_주씩_빠르게_지나간다()
        {
            int started = 0;
            _time.OnSkipStarted += w => started = w;
            Assert.IsTrue(_time.StartMonthSkip());
            Assert.AreEqual(4, started);
            Assert.IsTrue(_time.IsSkipping);
            Assert.IsEmpty(_log, "누른 순간에 한 번에 지나가지 않음");

            _time.TickSkip(0.3f);
            CollectionAssert.AreEqual(new[] { "1/2" }, _log);
            Assert.AreEqual(1, _time.SkipWeeksDone);
            Assert.That(_time.SkipProgress, Is.EqualTo(1f / 4f).Within(0.001f));

            _time.TickSkip(0.65f); // 두 주 더 (남는 시간은 다음으로)
            Assert.AreEqual(3, _time.SkipWeeksDone);

            _time.TickSkip(10f);
            Assert.IsFalse(_time.IsSkipping);
            AssertDate(2, 1);
            CollectionAssert.AreEqual(new[] { "1/2", "1/3", "1/4", "2/1", "완료" }, _log);
        }

        [Test]
        public void 예전_이름_StartWeekSkip도_한_달_스킵()
        {
            Assert.IsTrue(_time.StartWeekSkip());
            _time.TickSkip(10f);
            AssertDate(2, 1);
        }

        [Test]
        public void 스킵하면_주간_틱과_월간_틱이_돈다()
        {
            int weekly = 0, monthly = 0;
            _time.OnWeeklyTick += () => weekly++;
            _time.OnMonthlyTick += () => monthly++;
            _time.StartMonthSkip();
            _time.TickSkip(10f);
            Assert.AreEqual(4, weekly);
            Assert.AreEqual(1, monthly);
        }

        [Test]
        public void 일시정지_배속에서도_스킵할_수_있다()
        {
            _time.SetGameSpeed(GameSpeed.Pause);
            Assert.IsTrue(_time.StartMonthSkip());
            _time.TickSkip(10f);
            AssertDate(2, 1);
            Assert.AreEqual(GameSpeed.Pause, _time.CurrentSpeed, "배속은 그대로");
        }

        [Test]
        public void 스킵_중에_다시_누르면_무시된다()
        {
            _time.StartMonthSkip();
            Assert.IsFalse(_time.CanSkip);
            Assert.IsFalse(_time.StartMonthSkip());
            _time.TickSkip(10f);
            AssertDate(2, 1, "4주만 지나감");
        }

        [Test]
        public void 대화_등으로_멈춰_있으면_스킵을_시작하지_않는다()
        {
            var source = new object();
            _time.RequestEventPause(source);
            Assert.IsFalse(_time.CanSkip);
            Assert.IsFalse(_time.StartMonthSkip());
            _time.ReleaseEventPause(source);
            Assert.IsTrue(_time.CanSkip);
        }

        [Test]
        public void 스킵_도중_이벤트가_일어나면_그_시점에서_멈춘다()
        {
            var source = new object();
            // 3주차에 대화가 시작됐다고 가정
            _time.OnDateChanged += (y, m, w) => { if (w == 3) _time.RequestEventPause(source); };

            _time.StartMonthSkip();
            _time.TickSkip(10f);

            Assert.IsFalse(_time.IsSkipping);
            AssertDate(1, 3, "이벤트가 난 주에서 멈춤");
            CollectionAssert.AreEqual(new[] { "1/2", "1/3", "중단" }, _log);

            _time.TickSkip(10f);
            AssertDate(1, 3, "남은 주는 지나가지 않음");
        }

        [Test]
        public void 실제_대화가_화면에_뜨면_스킵이_멈춘다()
        {
            var dialogue = _world.CreateManager<DialogueEventManager>();
            dialogue.OnLineShown += (l, i, n) => { }; // 대화창 UI가 있음
            _time.OnDateChanged += (y, m, w) =>
            {
                if (w == 3) dialogue.Play(DialogueData.Create("event", true, false, new DialogueLine("주민", "큰일이에요!")));
            };

            _time.StartMonthSkip();
            _time.TickSkip(10f);

            Assert.IsFalse(_time.IsSkipping);
            AssertDate(1, 3);
            Assert.IsTrue(dialogue.IsPlaying);
        }

        [Test]
        public void 대화창_UI가_없어서_콘솔로만_나온_대화는_스킵을_멈추지_않는다()
        {
            var dialogue = _world.CreateManager<DialogueEventManager>();
            _time.OnDateChanged += (y, m, w) =>
            {
                if (w == 3) dialogue.Play(DialogueData.Create("event", true, false, new DialogueLine("주민", "...")));
            };
            _time.StartMonthSkip();
            _time.TickSkip(10f);
            AssertDate(2, 1);
        }

        [Test]
        public void 엔딩이_나면_스킵이_멈춘다()
        {
            var outcome = _world.CreateManager<GameOutcomeManager>();
            _time.StartMonthSkip();
            _time.TickSkip(0.3f);
            TestWorld.Invoke(outcome, "EndGame", GameResult.Defeat);
            Assert.IsFalse(_time.IsSkipping);
        }

        [Test]
        public void 취소하면_그_자리에서_멈춘다()
        {
            _time.StartMonthSkip();
            _time.TickSkip(0.6f);
            _time.CancelSkip();
            Assert.IsFalse(_time.IsSkipping);
            AssertDate(1, 3);
            Assert.AreEqual("중단", _log[_log.Count - 1]);
        }

        [Test]
        public void 한_주_간격이_0이면_한번에_지나간다()
        {
            _time.SkipSecondsPerWeek = 0f;
            _time.StartMonthSkip();
            Assert.IsFalse(_time.IsSkipping);
            AssertDate(2, 1);
        }

        [Test]
        public void 달_중간에_스킵해도_4주가_지나간다()
        {
            int monthly = 0;
            _time.OnMonthlyTick += () => monthly++;
            TestWorld.SetField(_time, "currentWeek", 3);
            _time.StartMonthSkip();
            _time.TickSkip(10f);
            Assert.AreEqual(1, monthly);
            AssertDate(2, 3);
        }

        [Test]
        public void 게임루프와_함께_스킵하면_인구가_바뀐다()
        {
            var population = _world.CreateManager<PopulationManager>();
            _world.CreateManager<SatisfactionManager>();
            _world.CreateManager<EconomyManager>();
            var loop = _world.CreateManager<GameLoopManager>();
            TestWorld.Invoke(loop, "Start");
            var house = _world.CreateBuilding(_world.CreateData(BuildingType.Housing, capacity: 100), 0, 0);
            loop.RegisterBuilding(house);
            int before = population.CurrentPopulation;

            _time.StartMonthSkip();
            _time.TickSkip(10f);

            Assert.Greater(population.CurrentPopulation, before, "주간 틱으로 인구 유입");
            TestWorld.Invoke(loop, "OnDestroy");
        }
    }
}

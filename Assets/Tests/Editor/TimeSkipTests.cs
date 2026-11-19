using System.Collections.Generic;
using NUnit.Framework;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.EventSystem;
using InfraTown.Population;
using InfraTown.Satisfaction;
using InfraTown.TimeSystem;

namespace InfraTown.Tests
{
    /// <summary>
    /// 일주일 스킵 (사용자 기획 2026-11-19): 누르면 7일이 하루씩 빠르게 지나가고(값 변화가 보이게),
    /// 대화 등 이벤트가 일어나면 그 시점에서 멈춘다.
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
            _time.SkipSecondsPerDay = 0.3f;
            _log.Clear();
            _time.OnDateChanged += (y, m, d) => _log.Add($"{m}/{d}");
            _time.OnSkipEnded += interrupted => _log.Add(interrupted ? "중단" : "완료");
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void 누르면_일주일이_하루씩_빠르게_지나간다()
        {
            int started = 0;
            _time.OnSkipStarted += d => started = d;
            Assert.IsTrue(_time.StartWeekSkip());
            Assert.AreEqual(7, started);
            Assert.IsTrue(_time.IsSkipping);
            Assert.IsEmpty(_log, "누른 순간에 한 번에 지나가지 않음");

            _time.TickSkip(0.3f);
            CollectionAssert.AreEqual(new[] { "1/2" }, _log);
            Assert.AreEqual(1, _time.SkipDaysDone);
            Assert.That(_time.SkipProgress, Is.EqualTo(1f / 7f).Within(0.001f));

            _time.TickSkip(0.65f); // 이틀 더 (남는 시간은 다음으로)
            Assert.AreEqual(3, _time.SkipDaysDone);

            _time.TickSkip(10f);
            Assert.IsFalse(_time.IsSkipping);
            Assert.AreEqual(8, _time.CurrentDay);
            Assert.AreEqual("완료", _log[_log.Count - 1]);
        }

        [Test]
        public void 스킵하면_주간_틱이_돌아_인구_등이_바뀐다()
        {
            int weekly = 0;
            _time.OnWeeklyTick += () => weekly++;
            _time.StartWeekSkip();
            _time.TickSkip(10f);
            Assert.AreEqual(1, weekly);
        }

        [Test]
        public void 일시정지_배속에서도_스킵할_수_있다()
        {
            _time.SetGameSpeed(GameSpeed.Pause);
            Assert.IsTrue(_time.StartWeekSkip());
            _time.TickSkip(10f);
            Assert.AreEqual(8, _time.CurrentDay);
            Assert.AreEqual(GameSpeed.Pause, _time.CurrentSpeed, "배속은 그대로");
        }

        [Test]
        public void 스킵_중에_다시_누르면_무시된다()
        {
            _time.StartWeekSkip();
            Assert.IsFalse(_time.CanSkip);
            Assert.IsFalse(_time.StartWeekSkip());
            _time.TickSkip(10f);
            Assert.AreEqual(8, _time.CurrentDay, "7일만 지나감");
        }

        [Test]
        public void 대화_등으로_멈춰_있으면_스킵을_시작하지_않는다()
        {
            var source = new object();
            _time.RequestEventPause(source);
            Assert.IsFalse(_time.CanSkip);
            Assert.IsFalse(_time.StartWeekSkip());
            _time.ReleaseEventPause(source);
            Assert.IsTrue(_time.CanSkip);
        }

        [Test]
        public void 스킵_도중_이벤트가_일어나면_그_시점에서_멈춘다()
        {
            var source = new object();
            // 3일째 되는 날 대화가 시작됐다고 가정
            _time.OnDateChanged += (y, m, d) => { if (d == 4) _time.RequestEventPause(source); };

            _time.StartWeekSkip();
            _time.TickSkip(10f);

            Assert.IsFalse(_time.IsSkipping);
            Assert.AreEqual(4, _time.CurrentDay, "이벤트가 난 날에서 멈춤");
            CollectionAssert.AreEqual(new[] { "1/2", "1/3", "1/4", "중단" }, _log);

            _time.TickSkip(10f);
            Assert.AreEqual(4, _time.CurrentDay, "남은 날은 지나가지 않음");
        }

        [Test]
        public void 실제_대화가_화면에_뜨면_스킵이_멈춘다()
        {
            var dialogue = _world.CreateManager<DialogueEventManager>();
            dialogue.OnLineShown += (l, i, n) => { }; // 대화창 UI가 있음
            _time.OnDateChanged += (y, m, d) =>
            {
                if (d == 3) dialogue.Play(DialogueData.Create("event", true, false, new DialogueLine("주민", "큰일이에요!")));
            };

            _time.StartWeekSkip();
            _time.TickSkip(10f);

            Assert.IsFalse(_time.IsSkipping);
            Assert.AreEqual(3, _time.CurrentDay);
            Assert.IsTrue(dialogue.IsPlaying);
        }

        [Test]
        public void 대화창_UI가_없어서_콘솔로만_나온_대화는_스킵을_멈추지_않는다()
        {
            var dialogue = _world.CreateManager<DialogueEventManager>();
            _time.OnDateChanged += (y, m, d) =>
            {
                if (d == 3) dialogue.Play(DialogueData.Create("event", true, false, new DialogueLine("주민", "...")));
            };
            _time.StartWeekSkip();
            _time.TickSkip(10f);
            Assert.AreEqual(8, _time.CurrentDay);
        }

        [Test]
        public void 엔딩이_나면_스킵이_멈춘다()
        {
            var outcome = _world.CreateManager<GameOutcomeManager>();
            _time.StartWeekSkip();
            _time.TickSkip(0.3f);
            TestWorld.Invoke(outcome, "EndGame", GameResult.Defeat);
            Assert.IsFalse(_time.IsSkipping);
        }

        [Test]
        public void 취소하면_그_자리에서_멈춘다()
        {
            _time.StartWeekSkip();
            _time.TickSkip(0.6f);
            _time.CancelSkip();
            Assert.IsFalse(_time.IsSkipping);
            Assert.AreEqual(3, _time.CurrentDay);
            Assert.AreEqual("중단", _log[_log.Count - 1]);
        }

        [Test]
        public void 하루_간격이_0이면_한번에_지나간다()
        {
            _time.SkipSecondsPerDay = 0f;
            _time.StartWeekSkip();
            Assert.IsFalse(_time.IsSkipping);
            Assert.AreEqual(8, _time.CurrentDay);
        }

        [Test]
        public void 달이_넘어가면_월간_정산도_돈다()
        {
            int monthly = 0;
            _time.OnMonthlyTick += () => monthly++;
            TestWorld.SetField(_time, "currentDay", 27);
            _time.StartWeekSkip();
            _time.TickSkip(10f);
            Assert.AreEqual(1, monthly);
            Assert.AreEqual(2, _time.CurrentMonth);
            Assert.AreEqual(4, _time.CurrentDay);
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

            _time.StartWeekSkip();
            _time.TickSkip(10f);

            Assert.Greater(population.CurrentPopulation, before, "주간 틱으로 인구 유입");
            TestWorld.Invoke(loop, "OnDestroy");
        }
    }
}

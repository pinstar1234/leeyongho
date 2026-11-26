using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using InfraTown.Core;
using InfraTown.Save;
using InfraTown.TimeSystem;

namespace InfraTown.Tests
{
    /// <summary>
    /// 세이브 흐름 (사용자 기획 2026-11-25):
    /// - 자동 저장은 1~5번과 따로 있는 자동 저장 전용 슬롯에만, 1주마다 덮어씀 (새로 시작한 게임도 같은 슬롯)
    /// - 자동 저장 슬롯은 플레이어가 직접 저장할 수 없고 저장하기 화면에는 나오지 않음, 불러오기·이어하기로는 불러옴
    /// - 새로 시작은 처음부터(이름은 게임 시작 때 다시 입력), 저장 파일은 지우지 않음
    /// </summary>
    public class SaveFlowTests
    {
        private TestWorld _world;
        private string _folder;
        private TimeManager _time;
        private AutoSaveManager _auto;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "InfraTownSaveFlow_" + System.Guid.NewGuid().ToString("N"));
            SaveSystem.FolderOverrideForTests = _folder;
            SaveSystem.ResetForNewGame();
            _world = new TestWorld();
            _time = _world.CreateManager<TimeManager>();
            _auto = _world.CreateManager<AutoSaveManager>(callStart: true);
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            SaveSystem.FolderOverrideForTests = null;
            SaveSystem.ResetForNewGame();
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        /// <summary>한 주 진행 + 그 프레임 끝 (LateUpdate)</summary>
        private void PassWeek()
        {
            TestWorld.Invoke(_time, "AdvanceOneWeek");
            TestWorld.Invoke(_auto, "LateUpdate");
        }

        private static int SavedWeek(int slot) => SaveSystem.LoadData(slot).time.totalWeeksPassed;

        private static bool AnyManualSave() => Enumerable.Range(1, SaveSystem.SlotCount).Any(s => SaveSystem.HasSave(s));

        // ─── 자동 저장 전용 슬롯 ───

        [Test]
        public void 한_주가_지나면_자동_저장_슬롯에만_저장()
        {
            int saved = 0;
            _auto.OnAutoSaved += () => saved++;

            PassWeek();
            Assert.AreEqual(1, saved);
            Assert.IsTrue(SaveSystem.HasAutoSave());
            Assert.AreEqual(_time.TotalWeeksPassed, SavedWeek(SaveSystem.AutoSaveSlot));
            Assert.IsFalse(AnyManualSave(), "1~5번은 건드리지 않음");
            Assert.AreEqual(1, SaveSystem.CurrentSlot, "현재 슬롯도 그대로");
        }

        [Test]
        public void 직접_저장한_슬롯이_있어도_자동_저장은_전용_슬롯에()
        {
            Assert.IsTrue(SaveSystem.SaveGame(3));
            int week3 = SavedWeek(3);
            PassWeek();
            Assert.AreEqual(week3, SavedWeek(3), "3번은 그대로");
            Assert.AreEqual(_time.TotalWeeksPassed, SavedWeek(SaveSystem.AutoSaveSlot));
            Assert.AreEqual(3, SaveSystem.CurrentSlot);
        }

        [Test]
        public void 매주_같은_자동_저장_슬롯을_덮어쓴다()
        {
            PassWeek();
            PassWeek();
            PassWeek();
            Assert.AreEqual(3, _auto.AutoSaveCount);
            Assert.AreEqual(_time.TotalWeeksPassed, SavedWeek(SaveSystem.AutoSaveSlot));
            Assert.AreEqual(1, Directory.GetFiles(_folder, "*.json").Length, "파일은 자동 저장 하나뿐");
        }

        [Test]
        public void 자동_저장_슬롯에는_직접_저장할_수_없다()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("자동 저장 슬롯"));
            Assert.IsFalse(SaveSystem.SaveGame(SaveSystem.AutoSaveSlot));
            StringAssert.Contains("자동 저장", SaveSystem.LastError);
            Assert.IsFalse(SaveSystem.HasAutoSave());
            Assert.IsFalse(SaveSystem.SetCurrentSlot(SaveSystem.AutoSaveSlot), "현재 슬롯으로도 못 정함");
        }

        [Test]
        public void 저장하기_화면에는_자동_저장_칸이_없다()
        {
            PassWeek();
            var slots = SaveSystem.GetAllSlots();
            Assert.AreEqual(SaveSystem.SlotCount, slots.Length);
            Assert.IsTrue(slots.All(s => !s.isAutoSave && s.slot >= 1 && s.slot <= SaveSystem.SlotCount));
            Assert.IsTrue(slots.All(s => s.isEmpty), "자동 저장이 있어도 1~5번은 비어 있음");
        }

        [Test]
        public void 불러오기_화면은_자동_저장_칸이_맨_앞()
        {
            SaveSystem.SaveGame(2);
            PassWeek();
            var slots = SaveSystem.GetLoadSlots();
            Assert.AreEqual(SaveSystem.SlotCount + 1, slots.Length);
            Assert.IsTrue(slots[0].isAutoSave && slots[0].slot == SaveSystem.AutoSaveSlot && !slots[0].isEmpty);
            Assert.IsNotNull(slots[0].summary);
            Assert.AreEqual(_time.CurrentWeek, slots[0].summary.week);
            Assert.IsTrue(slots[0].isLatest, "가장 최근 저장은 자동 저장");
            Assert.IsFalse(slots[2].isEmpty, "2번");
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, slots.Skip(1).Select(s => s.slot));
            Assert.IsTrue(slots.Skip(1).All(s => !s.isAutoSave));
        }

        [Test]
        public void 빈_자동_저장_칸도_불러오기_화면에_나온다()
        {
            var info = SaveSystem.GetAutoSaveSlotInfo();
            Assert.IsTrue(info.isAutoSave && info.isEmpty);
            Assert.IsTrue(SaveSystem.GetLoadSlots()[0].isEmpty);
        }

        [Test]
        public void 이어하기는_가장_최근_저장_자동_저장_포함()
        {
            SaveSystem.SaveGame(4);
            File.SetLastWriteTimeUtc(SaveSystem.GetSavePath(4), System.DateTime.UtcNow.AddMinutes(-5));
            PassWeek();
            Assert.AreEqual(SaveSystem.AutoSaveSlot, SaveSystem.GetLatestSlot());

            SaveSystem.SaveGame(4); // 직접 저장이 더 최근이면 그 슬롯
            File.SetLastWriteTimeUtc(SaveSystem.GetSavePath(4), System.DateTime.UtcNow.AddMinutes(5));
            Assert.AreEqual(4, SaveSystem.GetLatestSlot());
        }

        [Test]
        public void 같은_순간에_연달아_저장해도_마지막_저장이_가장_최근()
        {
            // 시계 해상도 때문에 수정 시각이 같게 찍히면 이어하기가 엉뚱한 슬롯을 고르던 문제 (빌드 테스트에서 발견, 2026-11-25)
            for (int i = 0; i < 5; i++)
            {
                SaveSystem.SaveGame(2);
                PassWeek(); // 자동 저장
                Assert.AreEqual(SaveSystem.AutoSaveSlot, SaveSystem.GetLatestSlot(), $"{i}회: 자동 저장 직후");
                SaveSystem.SaveGame(4);
                Assert.AreEqual(4, SaveSystem.GetLatestSlot(), $"{i}회: 4번 직접 저장 직후");
                SaveSystem.SaveGame(2);
                Assert.AreEqual(2, SaveSystem.GetLatestSlot(), $"{i}회: 2번 직접 저장 직후");
            }
        }

        [Test]
        public void 자동_저장_슬롯을_읽고_지울_수_있다()
        {
            PassWeek();
            Assert.IsNotNull(SaveSystem.LoadData(SaveSystem.AutoSaveSlot));
            Assert.IsNotNull(SaveSystem.GetSummary(SaveSystem.AutoSaveSlot));
            Assert.IsTrue(SaveSystem.HasAnySave());
            Assert.IsTrue(SaveSystem.DeleteSave(SaveSystem.AutoSaveSlot));
            Assert.IsFalse(SaveSystem.HasAnySave());
        }

        [Test]
        public void 날짜가_그대로인_알림은_한_주로_세지_않는다()
        {
            _time.NotifyStateRestored();
            Assert.IsFalse(_auto.IsPending);
            TestWorld.Invoke(_auto, "LateUpdate");
            Assert.IsFalse(SaveSystem.HasAnySave());
        }

        [Test]
        public void 같은_프레임에_다른_처리가_끝난_뒤_LateUpdate에서_저장()
        {
            TestWorld.Invoke(_time, "AdvanceOneWeek");
            Assert.IsTrue(_auto.IsPending, "주가 바뀐 순간에는 기다림");
            Assert.IsFalse(SaveSystem.HasAutoSave());
            TestWorld.Invoke(_auto, "LateUpdate");
            Assert.IsTrue(SaveSystem.HasAutoSave());
            Assert.IsFalse(_auto.IsPending);
        }

        [Test]
        public void 한_달_스킵_중에는_기다렸다가_끝나면_한_번_저장하고_스킵도_끝까지_간다()
        {
            int count = 0;
            _auto.OnAutoSaved += () => count++;
            int before = _time.TotalWeeksPassed;
            Assert.IsTrue(_time.StartMonthSkip());

            for (int i = 0; i < 10 && _time.IsSkipping; i++)
            {
                _time.TickSkip(1f);
                TestWorld.Invoke(_auto, "LateUpdate");
                if (_time.IsSkipping) Assert.AreEqual(0, count, "스킵 도중에는 저장하지 않음");
            }
            TestWorld.Invoke(_auto, "LateUpdate");

            Assert.IsFalse(_time.IsSkipping);
            Assert.AreEqual(before + 4, _time.TotalWeeksPassed, "저장 때문에 스킵이 끊기지 않음");
            Assert.AreEqual(1, count);
            Assert.AreEqual(_time.TotalWeeksPassed, SavedWeek(SaveSystem.AutoSaveSlot));
        }

        [Test]
        public void 배드엔딩_게임_오버면_자동_저장하지_않는다()
        {
            var outcome = _world.CreateManager<GameOutcomeManager>();
            outcome.ApplySaveState(new OutcomeSaveState { result = (int)GameResult.Defeat });
            PassWeek();
            Assert.IsFalse(SaveSystem.HasAnySave());
        }

        [Test]
        public void 해피엔딩_선택_대기는_저장한다()
        {
            var outcome = _world.CreateManager<GameOutcomeManager>();
            outcome.ApplySaveState(new OutcomeSaveState { result = (int)GameResult.Victory });
            PassWeek();
            Assert.IsTrue(SaveSystem.HasAutoSave());
        }

        [Test]
        public void 자동_저장을_끄면_저장하지_않는다()
        {
            _auto.AutoSaveEnabled = false;
            PassWeek();
            Assert.IsFalse(SaveSystem.HasAnySave());
        }

        [Test]
        public void 불러오는_중의_날짜_알림은_자동_저장하지_않는다()
        {
            var data = SaveSystem.CaptureCurrentGame();
            data.time.week = 3;
            data.time.month = 5;
            data.time.totalWeeksPassed = 19;
            SaveSystem.ApplySaveData(data); // 끝에서 NotifyStateRestored → 날짜 알림 (IsApplying 중)
            Assert.AreEqual(19, _time.TotalWeeksPassed);
            Assert.IsFalse(_auto.IsPending);
            TestWorld.Invoke(_auto, "LateUpdate");
            Assert.IsFalse(SaveSystem.HasAnySave());

            PassWeek(); // 그다음 주부터는 저장
            Assert.AreEqual(20, SavedWeek(SaveSystem.AutoSaveSlot));
        }

        // ─── 불러온 뒤 첫 달 구매가 다시 일어나지 않음 (빌드 테스트에서 발견, 2026-11-25) ───

        [Test]
        public void 세이브를_되돌린_뒤_가스_매니저가_시작해도_첫_달_구매를_하지_않는다()
        {
            var economy = _world.CreateManager<InfraTown.Economy.EconomyManager>();
            var gas = _world.CreateManager<InfraTown.Gas.GasManager>(); // Start 전 (GameLoopManager가 실행 중에 붙인 경우)
            gas.ApplySaveState(new GasSaveState { targetPurchasedGas = 500, currentAvailableGas = 320 });
            economy.ApplySaveState(new EconomySaveState { money = 123456, requestedSubsidy = 15000 });

            TestWorld.Invoke(gas, "Start"); // 세이브 적용보다 늦게 돈 Start
            Assert.AreEqual(123456, economy.CurrentMoney, "가스값이 다시 빠지면 안 됨");
            Assert.AreEqual(320, gas.CurrentAvailableGas, "불러온 이번 달 보유량 그대로");
        }

        [Test]
        public void 세이브를_되돌린_뒤_전력_매니저가_시작해도_첫_달_구매를_하지_않는다()
        {
            var economy = _world.CreateManager<InfraTown.Economy.EconomyManager>();
            var power = _world.CreateManager<InfraTown.Power.PowerManager>();
            power.ApplySaveState(new PowerSaveState { targetPurchasedPower = 800, currentAvailablePower = 410 });
            economy.ApplySaveState(new EconomySaveState { money = 50000, requestedSubsidy = 15000 });

            TestWorld.Invoke(power, "Start");
            Assert.AreEqual(50000, economy.CurrentMoney);
            Assert.AreEqual(410, power.CurrentAvailablePower);
        }

        [Test]
        public void 새_게임이면_가스_매니저는_시작할_때_첫_달을_산다()
        {
            var economy = _world.CreateManager<InfraTown.Economy.EconomyManager>();
            var gas = _world.CreateManager<InfraTown.Gas.GasManager>(callStart: true);
            Assert.AreEqual(gas.TargetPurchasedGas, gas.CurrentAvailableGas);
            Assert.Less(economy.CurrentMoney, 10000);
        }

        // ─── 새로 시작 ───

        [Test]
        public void 새로_시작하면_이름과_현재_슬롯이_초기화되고_저장_파일은_그대로()
        {
            SaveSystem.SaveGame(4);
            PassWeek();
            PlayerProfile.TrySetPlayerName("민수", out _);
            Assert.AreEqual(4, SaveSystem.CurrentSlot);

            SaveSystem.ResetForNewGame();
            Assert.AreEqual(1, SaveSystem.CurrentSlot);
            Assert.IsFalse(PlayerProfile.HasName, "이름 입력부터 다시 (게임 시작 때)");
            Assert.IsFalse(SaveSystem.HasPendingLoad);
            Assert.IsTrue(SaveSystem.HasSave(4), "직접 저장한 파일은 그대로 — 새로 시작한 뒤에도 불러오기 가능");
            Assert.IsTrue(SaveSystem.HasAutoSave(), "자동 저장도 그대로 (다음 자동 저장 때 덮어씀)");
        }

        [Test]
        public void 새로_시작한_게임의_자동_저장은_같은_자동_저장_슬롯을_덮어쓴다()
        {
            SaveSystem.SaveGame(4);
            int week4 = SavedWeek(4);
            PassWeek();
            PassWeek();
            SaveSystem.ResetForNewGame();

            // 새 게임: 시간을 처음으로
            _time.ApplySaveState(new TimeSaveState { year = 1, month = 1, week = 1, totalWeeksPassed = 0 });
            PassWeek();
            Assert.AreEqual(1, SavedWeek(SaveSystem.AutoSaveSlot), "자동 저장 슬롯이 새 게임으로 덮어써짐");
            Assert.AreEqual(week4, SavedWeek(4), "4번은 그대로");
            Assert.IsFalse(SaveSystem.HasSave(1), "1번을 덮어쓰지 않음");
        }

        [Test]
        public void 새로_시작_씬이_없으면_false이고_아무것도_바꾸지_않는다()
        {
            SaveSystem.SaveGame(3);
            PlayerProfile.TrySetPlayerName("민수", out _);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Build Settings"));
            Assert.IsFalse(SaveSystem.StartNewGame("__없는_씬__"));
            StringAssert.Contains("Build Settings", SaveSystem.LastError);
            Assert.AreEqual(3, SaveSystem.CurrentSlot);
            Assert.AreEqual("민수", PlayerProfile.PlayerName);
        }

        // ─── 타이틀 '이어하기' (resume.json + 자동 저장, 2026-11-26) ───

        private string ResumeFile => Path.Combine(_folder, "resume.json");

        [Test]
        public void 게임_오버면_이어하기_저장을_하지_않지만_종료는_막지_않는다()
        {
            PassWeek();
            Assert.IsTrue(SaveSystem.SaveResume());
            int savedWeek = JsonUtility.FromJson<SaveData>(File.ReadAllText(ResumeFile)).time.totalWeeksPassed;

            var outcome = _world.CreateManager<GameOutcomeManager>();
            TestWorld.Invoke(_time, "AdvanceOneWeek");
            outcome.ApplySaveState(new OutcomeSaveState { result = (int)GameResult.Defeat });

            Assert.IsTrue(SaveSystem.SaveResume(), "종료 버튼이 '저장 실패'로 막히지 않게 true");
            var resume = JsonUtility.FromJson<SaveData>(File.ReadAllText(ResumeFile));
            Assert.AreEqual(savedWeek, resume.time.totalWeeksPassed, "패배 직전 저장이 그대로 남음");
            Assert.AreNotEqual((int)GameResult.Defeat, resume.outcome.result);
        }

        [Test]
        public void 게임_오버_중에는_슬롯에_직접_저장할_수_없다()
        {
            Assert.IsTrue(SaveSystem.SaveGame(2), "진행 중에는 저장");
            int savedWeek = SavedWeek(2);
            var outcome = _world.CreateManager<GameOutcomeManager>();
            TestWorld.Invoke(_time, "AdvanceOneWeek");
            outcome.ApplySaveState(new OutcomeSaveState { result = (int)GameResult.Defeat });

            Assert.IsFalse(SaveSystem.SaveGame(2));
            Assert.AreEqual(SaveSystem.GameOverSaveError, SaveSystem.LastError);
            Assert.IsFalse(SaveSystem.SaveGame(4));
            Assert.IsFalse(SaveSystem.HasSave(4));
            Assert.AreEqual(savedWeek, SavedWeek(2), "예전 2번 저장은 그대로");
            Assert.IsFalse(SaveSystem.SaveAndQuitToTitle(3), "저장 후 나가기도 저장하지 않음 (게임 오버 화면은 ReturnToTitle)");
            Assert.IsFalse(SaveSystem.HasSave(3));
        }

        [Test]
        public void 자동_저장만_있어도_이어하기를_할_수_있다()
        {
            Assert.IsFalse(SaveSystem.HasResume());
            PassWeek();
            Assert.IsFalse(File.Exists(ResumeFile), "강제 종료 등으로 resume.json이 없는 상황");
            Assert.IsTrue(SaveSystem.HasResume());
            var data = SaveSystem.LoadResumeData(out bool fromAuto);
            Assert.IsTrue(fromAuto);
            Assert.AreEqual(_time.TotalWeeksPassed, data.time.totalWeeksPassed);
        }

        [Test]
        public void 이어하기는_더_최근인_자동_저장을_고르고_하던_슬롯을_기억한다()
        {
            Assert.IsTrue(SaveSystem.SaveGame(3));
            Assert.IsTrue(SaveSystem.SaveResume()); // 예전에 정상 종료
            PassWeek();                              // 그 뒤 몇 주 플레이하다 튕김 → 자동 저장이 더 최근
            PassWeek();

            var data = SaveSystem.LoadResumeData(out bool fromAuto);
            Assert.IsTrue(fromAuto, "더 최근인 자동 저장");
            Assert.AreEqual(_time.TotalWeeksPassed, data.time.totalWeeksPassed);
            Assert.AreEqual(3, data.resumeSlot, "자동 저장도 하던 슬롯(3번)을 기억");
        }

        [Test]
        public void 종료_직전_저장이_더_최근이면_그것을_고른다()
        {
            PassWeek();
            TestWorld.Invoke(_time, "AdvanceOneWeek"); // 자동 저장 전에 끔 (주 중간)
            Assert.IsTrue(SaveSystem.SaveResume());

            var data = SaveSystem.LoadResumeData(out bool fromAuto);
            Assert.IsFalse(fromAuto);
            Assert.AreEqual(_time.TotalWeeksPassed, data.time.totalWeeksPassed);
        }

        [Test]
        public void 최근_파일을_읽을_수_없으면_다른_쪽으로_이어한다()
        {
            Assert.IsTrue(SaveSystem.SaveResume());
            int resumeWeek = _time.TotalWeeksPassed;
            PassWeek();
            File.WriteAllText(SaveSystem.GetSavePath(SaveSystem.AutoSaveSlot), "{ 깨진 파일");

            var data = SaveSystem.LoadResumeData(out bool fromAuto);
            Assert.IsNotNull(data, SaveSystem.LastError);
            Assert.IsFalse(fromAuto);
            Assert.AreEqual(resumeWeek, data.time.totalWeeksPassed);
        }

        [Test]
        public void 불러오기_중_한_단계에서_예외가_나도_나머지는_적용하고_알린다()
        {
            _world.CreateManager<InfraTown.EventSystem.DialogueEventManager>();
            var data = SaveSystem.CaptureCurrentGame();
            data.time.month = 5;
            data.time.week = 3;
            data.time.totalWeeksPassed = 19;
            data.dialogue = null; // 손상된 저장 파일 → '대화 기록' 단계에서 예외
            int dateNotified = 0;
            _time.OnDateChanged += (y, m, w) => dateNotified++;

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("'대화 기록' 단계 실패"));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("일부를 불러오지 못했어요"));
            SaveSystem.ApplySaveData(data);

            CollectionAssert.AreEqual(new[] { "대화 기록" }, SaveSystem.LastApplyFailedSteps);
            StringAssert.Contains("대화 기록", SaveSystem.LastError);
            Assert.AreEqual(19, _time.TotalWeeksPassed, "앞 단계(시간)는 적용됨");
            Assert.AreEqual(1, dateNotified, "뒤 단계(날짜 알림)도 이어서 적용됨");
            Assert.IsFalse(SaveSystem.IsApplying);

            data.dialogue = new DialogueSaveState();
            SaveSystem.ApplySaveData(data);
            Assert.IsEmpty(SaveSystem.LastApplyFailedSteps, "다 되면 비어 있음");
        }

        [Test]
        public void 이어할_파일이_없으면_실패와_이유()
        {
            Assert.IsNull(SaveSystem.LoadResumeData(out _));
            Assert.IsFalse(SaveSystem.ContinueResume());
            Assert.IsNotEmpty(SaveSystem.LastError);
        }
    }
}

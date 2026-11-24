// 실제 빌드(Windows 플레이어) 안에서 도는 자동 플레이 테스트 (사용자 요청 2026-11-24).
// Tools/run-build-test.ps1이 INFRATOWN_BUILD_TEST 정의를 넣어 테스트용으로 빌드하고,
// 실행 파일을 -infratownTest -infratownSeed <seed> -infratownTestOut <결과 파일> 인자로 여러 번 실행한다.
// 일반 빌드에는 들어가지 않는다 (에디터에서는 컴파일 확인용으로만 포함, 실행되지 않음).
#if UNITY_EDITOR || INFRATOWN_BUILD_TEST
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.EventSystem;
using InfraTown.Gas;
using InfraTown.Population;
using InfraTown.Power;
using InfraTown.Satisfaction;
using InfraTown.Save;
using InfraTown.Services;
using InfraTown.TimeSystem;

namespace InfraTown.Testing
{
    public class BuildPlaytestRunner : MonoBehaviour
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private int _seed;
        private string _outPath;
        private readonly List<string> _results = new List<string>();
        private readonly List<string> _consoleErrors = new List<string>();

        // 지금 돌고 있는 검사의 첫 실패 이유 (null = 통과)
        private string _failure;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isEditor) return;
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("-infratownTest")) return;

            var go = new GameObject("BuildPlaytestRunner");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<BuildPlaytestRunner>();
            runner._seed = int.TryParse(Arg(args, "-infratownSeed"), out int s) ? s : 1;
            runner._outPath = Arg(args, "-infratownTestOut") ?? Path.Combine(Application.persistentDataPath, "build-test-result.txt");
        }

        private static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private void OnEnable() => Application.logMessageReceived += OnLog;
        private void OnDisable() => Application.logMessageReceived -= OnLog;

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                _consoleErrors.Add($"{type}: {message.Split('\n')[0]}");
        }

        // ───────────────────────── 실행 ─────────────────────────

        private IEnumerator Start()
        {
            UnityEngine.Random.InitState(_seed);
            float started = Time.realtimeSinceStartup;
            WriteLine($"RUN\t{_seed}\t{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}\t{Application.version}");

            // 씬 Start·첫 Update(오프닝 시작)가 끝나도록 몇 프레임 기다림
            for (int i = 0; i < 5; i++) yield return null;

            var tests = new (string name, Func<IEnumerator> body)[]
            {
                ("Boot_ManagersPresent", Boot_ManagersPresent),
                ("Boot_BuildingData13", Boot_BuildingData13),
                ("Boot_FirstMonthPowerGasBought", Boot_FirstMonthPowerGasBought),
                ("Boot_StartsWinterWeek1", Boot_StartsWinterWeek1),
                ("Story_OpeningRunsWithoutDialogueUI", Story_OpeningRunsWithoutDialogueUI),
                ("Time_WeekPassesInRealTime", Time_WeekPassesInRealTime),
                ("Time_SpeedSetsTimeScale", Time_SpeedSetsTimeScale),
                ("Time_PauseLockStopsTime", Time_PauseLockStopsTime),
                ("Time_MonthSkipFourWeeks", Time_MonthSkipFourWeeks),
                ("Time_SkipStopsOnEventPause", Time_SkipStopsOnEventPause),
                ("Build_LockedBuildingRejected", Build_LockedBuildingRejected),
                ("Build_HouseCostsMoney", Build_HouseCostsMoney),
                ("Build_OccupiedCellRejected", Build_OccupiedCellRejected),
                ("Build_NoMoneyRejected", Build_NoMoneyRejected),
                ("Build_RotateQE", Build_RotateQE),
                ("Build_DemolishFreesCells", Build_DemolishFreesCells),
                ("Pop_WeeklyInflow", Pop_WeeklyInflow),
                ("Pop_UnlocksAllTiersAt300", Pop_UnlocksAllTiersAt300),
                ("Satisfaction_MiddleVillage84", Satisfaction_MiddleVillage84),
                ("Satisfaction_EssentialsRaise", Satisfaction_EssentialsRaise),
                ("Power_BuildingPlusSeasonal", Power_BuildingPlusSeasonal),
                ("Power_PoleGridRadius", Power_PoleGridRadius),
                ("Economy_MonthlySettlement", Economy_MonthlySettlement),
                ("Power_TargetAppliesNextMonth", Power_TargetAppliesNextMonth),
                ("Gas_TargetAppliesNextMonth", Gas_TargetAppliesNextMonth),
                ("Season_YearCyclePowerGas", Season_YearCyclePowerGas),
                ("Power_BlackoutPenalty", Power_BlackoutPenalty),
                ("Gas_ShortagePenalty", Gas_ShortagePenalty),
                ("Build_RandomPlacementSeed", Build_RandomPlacementSeed),
                ("Save_WritesFile", Save_WritesFile),
                ("Save_ContinueRestoresState", Save_ContinueRestoresState),
                ("Save_PlayContinuesAfterLoad", Save_PlayContinuesAfterLoad),
                ("Outcome_DefeatAfter12BadWeeks", Outcome_DefeatAfter12BadWeeks),
                ("Outcome_VictoryAt480Weeks", Outcome_VictoryAt480Weeks),
                ("Console_NoErrors", Console_NoErrors),
            };

            int failed = 0;
            foreach (var test in tests)
            {
                _failure = null;
                float t0 = Time.realtimeSinceStartup;
                yield return RunSafely(test.body());
                float seconds = Time.realtimeSinceStartup - t0;
                if (_failure != null) failed++;
                WriteLine($"TEST\t{test.name}\t{(_failure == null ? "O" : "X")}\t{seconds:F1}\t{Clean(_failure)}");
                ClearDialogues();
            }

            WriteLine($"END\t{tests.Length - failed}\t{tests.Length}\t{Time.realtimeSinceStartup - started:F1}");
            Flush();
            Application.Quit(failed == 0 ? 0 : 2);
        }

        /// <summary>코루틴 안 예외를 잡아 그 검사만 실패로 처리 (중첩 IEnumerator도 펼쳐서 실행)</summary>
        private IEnumerator RunSafely(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                object current;
                try
                {
                    if (!top.MoveNext()) { stack.Pop(); continue; }
                    current = top.Current;
                }
                catch (Exception e)
                {
                    Fail($"예외 {e.GetType().Name}: {e.Message}");
                    yield break;
                }
                if (current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return current;
            }
        }

        private static string Clean(string s) => s == null ? "" : s.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");

        private void WriteLine(string line)
        {
            _results.Add(line);
            Flush(); // 도중에 멈춰도 그때까지 결과가 남도록
        }

        private void Flush()
        {
            try
            {
                var dir = Path.GetDirectoryName(_outPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(_outPath, _results, new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning($"[BuildPlaytest] 결과 파일 쓰기 실패: {e.Message}"); }
        }

        // ───────────────────────── 검사 도우미 ─────────────────────────

        private void Fail(string reason)
        {
            if (_failure == null) _failure = reason;
        }

        /// <summary>조건이 거짓이면 실패 이유를 남김 (첫 실패만 기록)</summary>
        private bool Expect(bool condition, string reason)
        {
            if (!condition) Fail(reason);
            return condition;
        }

        private static BuildingManager Buildings => BuildingManager.Instance;
        private static TimeManager TimeM => TimeManager.Instance;

        private static BuildingInstance[,] Grid =>
            (BuildingInstance[,])typeof(BuildingManager).GetField("_occupiedGrid", AnyInstance).GetValue(Buildings);

        private static BuildingData Data(string id) => Buildings.AllBuildingData.First(d => d != null && d.buildingID == id);

        private static int SizeOf(BuildingData d) => d.sizeType == BuildingSizeType.Small ? 1 : d.sizeType == BuildingSizeType.Medium ? 2 : 4;

        private static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, AnyInstance).Invoke(target, args);

        /// <summary>실제 건설 경로(TryPlaceBuilding)로 설치. 실패하면 null</summary>
        private static BuildingInstance Place(string id, int x, int y, float rotationY = 0f)
        {
            var grid = Grid;
            if (x < 0 || y < 0 || x >= grid.GetLength(0) || y >= grid.GetLength(1)) { Buildings.SelectBuildingToBuild(Data(id)); Call(Buildings, "TryPlaceBuilding", new Vector2Int(x, y)); Buildings.CancelBuildingMode(); return null; } // 맵 밖: 실제 경로로 시도만 (예외가 나면 그 검사 실패)
            var data = Data(id);
            Buildings.SelectBuildingToBuild(data);
            float delta = Mathf.DeltaAngle(Buildings.CurrentRotationY, rotationY);
            if (Mathf.Abs(delta) > 0.1f) Buildings.RotatePreview(delta);
            var before = Grid[x, y];
            Call(Buildings, "TryPlaceBuilding", new Vector2Int(x, y));
            Buildings.CancelBuildingMode();
            var placed = Grid[x, y];
            return placed != null && placed != before && placed.Data == data ? placed : null;
        }

        private static void Demolish(BuildingInstance b)
        {
            if (b != null) b.DestroyBuilding();
        }

        private static void AdvanceWeek()
        {
            Call(TimeM, "AdvanceOneWeek");
            ClearDialogues();
        }

        private static void AdvanceWeeks(int n)
        {
            for (int i = 0; i < n; i++) AdvanceWeek();
        }

        private static void AdvanceToNextMonth()
        {
            int month = TimeM.CurrentMonth;
            while (TimeM.CurrentMonth == month) AdvanceWeek();
        }

        /// <summary>대화창 UI가 붙어 있어도 테스트가 멈추지 않도록 대화·컷씬·이름 입력을 정리</summary>
        private static void ClearDialogues()
        {
            var triggers = FindObjectOfType<DialogueTriggers>();
            if (triggers != null && triggers.IsWaitingForPlayerName) triggers.SubmitPlayerName("민수");
            if (CutsceneManager.Instance != null && CutsceneManager.Instance.IsPlaying) CutsceneManager.Instance.ClearAll();
            if (DialogueEventManager.Instance != null && DialogueEventManager.Instance.IsBusy) DialogueEventManager.Instance.ClearAll();
        }

        /// <summary>전력·가스를 충분히 사서 공급 부족 감점을 없앰</summary>
        private static void EnsureSupply()
        {
            var power = PowerManager.Instance;
            if (power.TargetPurchasedPower < 1000) power.SetTargetPurchasedPower(1000);
            if (power.IsPowerShortage) power.ProcessMonthlyPowerPurchase();
            var gas = GasManager.Instance;
            if (gas.TargetPurchasedGas < 500) gas.SetTargetPurchasedGas(500);
            if (gas.IsGasShortage) gas.ProcessMonthlyGasPurchase();
        }

        private static int WeekIndex => ((TimeM.CurrentYear - 1) * TimeManager.MonthsPerYear + (TimeM.CurrentMonth - 1)) * TimeManager.WeeksPerMonth + TimeM.CurrentWeek;

        private static IEnumerator RealSeconds(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        private static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        private static float Satisfaction => SatisfactionManager.Instance.CurrentAverageSatisfaction;

        // 마을 배치 (에디터 자동 플레이 테스트와 같은 밸런스 기준 배치)
        private static readonly (string id, int x, int y)[] MiddleLayout =
        {
            ("village_hall", 2, 2), ("convenience_store", 9, 2), ("bus_stop", 16, 2), ("senior_center", 23, 2), ("road", 0, 1),
        };
        private static readonly (string id, int x, int y)[] EssentialLayout =
        {
            ("hospital", 2, 5), ("school", 9, 5), ("fire_station", 16, 5), ("police_station", 20, 5),
        };

        private static void PlaceLayout((string id, int x, int y)[] layout)
        {
            foreach (var (id, x, y) in layout) Place(id, x, y);
        }

        // ───────────────────────── 0. 시작 ─────────────────────────

        private IEnumerator Boot_ManagersPresent()
        {
            Expect(GameLoopManager.Instance != null, "GameLoopManager 없음");
            Expect(EconomyManager.Instance != null, "EconomyManager 없음");
            Expect(PopulationManager.Instance != null, "PopulationManager 없음");
            Expect(PowerManager.Instance != null, "PowerManager 없음");
            Expect(GasManager.Instance != null, "GasManager 없음 (GameLoopManager가 자동 추가해야 함)");
            Expect(SatisfactionManager.Instance != null, "SatisfactionManager 없음");
            Expect(ServiceAccessibilityManager.Instance != null, "ServiceAccessibilityManager 없음");
            Expect(TimeM != null, "TimeManager 없음");
            Expect(Buildings != null, "BuildingManager 없음");
            Expect(GameOutcomeManager.Instance != null, "GameOutcomeManager 없음");
            yield break;
        }

        private IEnumerator Boot_BuildingData13()
        {
            int count = Buildings.AllBuildingData.Count(d => d != null);
            Expect(count == 13, $"건물 데이터 {count}종 (13종이어야 함)");
            Expect(Buildings.AllBuildingData.All(d => d == null || d.buildingPrefab != null), "프리팹이 없는 건물이 있음");
            yield break;
        }

        private IEnumerator Boot_FirstMonthPowerGasBought()
        {
            var power = PowerManager.Instance;
            var gas = GasManager.Instance;
            var eco = EconomyManager.Instance;
            Expect(power.CurrentAvailablePower == power.TargetPurchasedPower, $"첫 달 전력 {power.CurrentAvailablePower}kW (구매량 {power.TargetPurchasedPower})");
            Expect(gas.CurrentAvailableGas == gas.TargetPurchasedGas, $"첫 달 가스 {gas.CurrentAvailableGas} (구매량 {gas.TargetPurchasedGas})");
            int expected = 10000 - power.MonthlyPurchaseCost - gas.MonthlyPurchaseCost;
            Expect(eco.CurrentMoney == expected, $"잔액 {eco.CurrentMoney:N0}원 (예상 {expected:N0}원)");
            yield break;
        }

        private IEnumerator Boot_StartsWinterWeek1()
        {
            Expect(TimeM.CurrentYear == 1 && TimeM.CurrentMonth == 1 && TimeM.CurrentWeek == 1,
                $"시작 날짜 {TimeM.CurrentYear}년 {TimeM.CurrentMonth}월 {TimeM.CurrentWeek}주차");
            Expect(TimeM.CurrentSeason == Season.Winter, $"시작 계절 {TimeM.CurrentSeason}");
            Expect(Mathf.Approximately(TimeM.SecondsPerWeek, 180f), $"1주 {TimeM.SecondsPerWeek}초 (180초여야 함)");
            yield break;
        }

        private IEnumerator Story_OpeningRunsWithoutDialogueUI()
        {
            var triggers = FindObjectOfType<DialogueTriggers>();
            if (!Expect(triggers != null, "DialogueTriggers 없음")) yield break;
            yield return Frames(3);
            ClearDialogues();
            yield return Frames(2);
            Expect(triggers.StoryStarted, "오프닝(이야기)이 시작되지 않음");
            Expect(!TimeM.IsEventPaused, $"오프닝 뒤에도 시간이 멈춰 있음 (잠금 {TimeM.PauseLockCount}개)");
        }

        // ───────────────────────── 1. 시간 ─────────────────────────

        private IEnumerator Time_WeekPassesInRealTime()
        {
            var field = typeof(TimeManager).GetField("secondsPerWeek", AnyInstance);
            float original = (float)field.GetValue(TimeM);
            field.SetValue(TimeM, 0.5f);
            TimeM.SetGameSpeed(GameSpeed.Normal);
            int before = WeekIndex;
            yield return RealSeconds(1.2f);
            int passed = WeekIndex - before;
            TimeM.SetGameSpeed(GameSpeed.Pause);
            field.SetValue(TimeM, original);
            ClearDialogues();
            Expect(passed >= 1 && passed <= 3, $"1주 0.5초로 1.2초 동안 {passed}주 지남 (1~3주여야 함)");
        }

        private IEnumerator Time_SpeedSetsTimeScale()
        {
            foreach (var speed in new[] { GameSpeed.Normal, GameSpeed.Fast, GameSpeed.SuperFast, GameSpeed.Pause })
            {
                TimeM.SetGameSpeed(speed);
                Expect(Mathf.Approximately(Time.timeScale, (float)speed), $"{speed} 배속에서 timeScale {Time.timeScale}");
            }
            yield break;
        }

        private IEnumerator Time_PauseLockStopsTime()
        {
            var field = typeof(TimeManager).GetField("secondsPerWeek", AnyInstance);
            float original = (float)field.GetValue(TimeM);
            field.SetValue(TimeM, 0.1f);
            TimeM.SetGameSpeed(GameSpeed.Normal);
            var source = new object();
            TimeM.RequestEventPause(source);
            int before = WeekIndex;
            yield return RealSeconds(0.5f);
            Expect(WeekIndex == before, $"잠금 중에 {WeekIndex - before}주가 지남");
            Expect(Mathf.Approximately(Time.timeScale, 0f), $"잠금 중 timeScale {Time.timeScale}");
            TimeM.ReleaseEventPause(source);
            Expect(Mathf.Approximately(Time.timeScale, 1f), $"잠금 해제 후 timeScale {Time.timeScale}");
            TimeM.SetGameSpeed(GameSpeed.Pause);
            field.SetValue(TimeM, original);
            ClearDialogues();
        }

        private IEnumerator Time_MonthSkipFourWeeks()
        {
            TimeM.SkipSecondsPerWeek = 0.05f;
            int before = WeekIndex;
            int progress = 0;
            Action<int, int> onProgress = (d, t) => progress++;
            TimeM.OnSkipProgress += onProgress;
            Expect(TimeM.StartMonthSkip(), "스킵을 시작하지 못함");
            float deadline = Time.realtimeSinceStartup + 5f;
            while (TimeM.IsSkipping && Time.realtimeSinceStartup < deadline) { ClearDialogues(); yield return null; }
            TimeM.OnSkipProgress -= onProgress;
            if (TimeM.IsSkipping) TimeM.CancelSkip();
            Expect(WeekIndex - before == 4, $"스킵으로 {WeekIndex - before}주 지남 (4주여야 함)");
            Expect(progress == 4, $"진행 알림 {progress}번 (4번이어야 함)");
        }

        private IEnumerator Time_SkipStopsOnEventPause()
        {
            TimeM.SkipSecondsPerWeek = 0.05f;
            var source = new object();
            int count = 0, before = WeekIndex;
            Action<int, int, int> onDate = (y, m, w) => { if (++count == 2) TimeM.RequestEventPause(source); };
            TimeM.OnDateChanged += onDate;
            TimeM.StartMonthSkip();
            float deadline = Time.realtimeSinceStartup + 5f;
            while (TimeM.IsSkipping && Time.realtimeSinceStartup < deadline) yield return null;
            TimeM.OnDateChanged -= onDate;
            if (TimeM.IsSkipping) TimeM.CancelSkip();
            Expect(WeekIndex - before == 2, $"이벤트 뒤에도 진행: {WeekIndex - before}주 (2주에서 멈춰야 함)");
            Expect(!TimeM.CanSkip, "멈춰 있는데 스킵 가능");
            TimeM.ReleaseEventPause(source);
            Expect(TimeM.CanSkip, "잠금을 풀었는데 스킵 불가");
        }

        // ───────────────────────── 2. 건설 ─────────────────────────

        private IEnumerator Build_LockedBuildingRejected()
        {
            Expect(PopulationManager.Instance.CurrentPopulation < Data("hospital").requiredPopulation,
                $"인구 {PopulationManager.Instance.CurrentPopulation}명이 이미 병원 해금 기준 이상");
            EconomyManager.Instance.AddMoney(1_000_000);
            int money = EconomyManager.Instance.CurrentMoney;
            Expect(Place("hospital", 40, 30) == null, "잠긴 병원이 지어짐");
            Expect(EconomyManager.Instance.CurrentMoney == money, "잠긴 건물인데 돈이 빠짐");
            yield break;
        }

        private IEnumerator Build_HouseCostsMoney()
        {
            int money = EconomyManager.Instance.CurrentMoney;
            var house = Place("house", 0, 0);
            if (!Expect(house != null, "주택을 짓지 못함")) yield break;
            Expect(EconomyManager.Instance.CurrentMoney == money - Data("house").constructionCost,
                $"잔액 {money:N0} → {EconomyManager.Instance.CurrentMoney:N0} (건설비 {Data("house").constructionCost:N0})");
            Expect(GameLoopManager.Instance.PlacedBuildings.Contains(house), "게임 루프 목록에 없음");
            Expect(PopulationManager.Instance.MaxPopulationCapacity == Data("house").housingCapacity,
                $"수용 인구 {PopulationManager.Instance.MaxPopulationCapacity}");
        }

        private IEnumerator Build_OccupiedCellRejected()
        {
            int money = EconomyManager.Instance.CurrentMoney;
            int count = GameLoopManager.Instance.PlacedBuildings.Count;
            Expect(Place("house", 0, 0) == null, "이미 지은 자리에 또 지어짐");
            Expect(EconomyManager.Instance.CurrentMoney == money && GameLoopManager.Instance.PlacedBuildings.Count == count, "겹친 자리 건설에 돈·건물 수가 바뀜");
            Expect(Place("house", -1, 0) == null && Place("house", 60, 0) == null, "맵 밖에 지어짐");
            yield break;
        }

        private IEnumerator Build_NoMoneyRejected()
        {
            var eco = EconomyManager.Instance;
            int saved = eco.CurrentMoney;
            eco.TrySpendMoney(saved);
            Expect(Place("house", 1, 0) == null, "돈이 0원인데 지어짐");
            eco.AddMoney(saved);
            Expect(eco.CurrentMoney == saved, "돈 되돌리기 실패");
            yield break;
        }

        private IEnumerator Build_RotateQE()
        {
            Buildings.SelectBuildingToBuild(Data("village_hall"));
            float start = Buildings.CurrentRotationY;
            Buildings.RotatePreview(90f);  // Q
            Expect(Mathf.Approximately(Mathf.DeltaAngle(start, Buildings.CurrentRotationY), 90f), $"Q 회전 {Buildings.CurrentRotationY}");
            Buildings.RotatePreview(-90f); // E
            Buildings.RotatePreview(-90f);
            Expect(Mathf.Approximately(Mathf.Repeat(Buildings.CurrentRotationY, 360f), 270f), $"E 두 번 뒤 {Buildings.CurrentRotationY}");
            Buildings.CancelBuildingMode();

            var store = Place("village_hall", 40, 20, 90f); // Tier 1 중형 (인구 0에서도 지을 수 있음)
            if (!Expect(store != null, "회전한 마을회관을 짓지 못함")) yield break;
            Expect(Mathf.Approximately(store.RotationY, 90f), $"설치 회전 {store.RotationY}");
            var expected = Buildings.GetPlacementRotation(store.Data, 90f);
            Expect(Quaternion.Angle(expected, store.transform.rotation) < 0.5f, "설치 모습이 미리보기 회전과 다름");
            Expect(Grid[41, 21] == store, "2x2 점유 칸이 비어 있음");
            Demolish(store);
            yield return Frames(1);
        }

        private IEnumerator Build_DemolishFreesCells()
        {
            var house = Grid[0, 0];
            if (!Expect(house != null, "철거할 주택이 없음")) yield break;
            Demolish(house);
            yield return Frames(1);
            Expect(Grid[0, 0] == null, "철거 후 칸이 비지 않음");
            Expect(PopulationManager.Instance.MaxPopulationCapacity == 0, $"철거 후 수용 인구 {PopulationManager.Instance.MaxPopulationCapacity}");
            Expect(Place("house", 0, 0) != null, "철거한 자리에 다시 짓지 못함");
        }

        // ───────────────────────── 3. 인구·만족도 ─────────────────────────

        private IEnumerator Pop_WeeklyInflow()
        {
            int placed = 1;
            for (int x = 1; x < 30; x++) if (Place("house", x, 0) != null) placed++;
            Expect(placed == 30, $"주택 {placed}채");
            Expect(PopulationManager.Instance.MaxPopulationCapacity == 300, $"수용 인구 {PopulationManager.Instance.MaxPopulationCapacity}");
            int before = PopulationManager.Instance.CurrentPopulation;
            AdvanceWeek();
            Expect(PopulationManager.Instance.CurrentPopulation > before,
                $"주간 유입 없음 ({before} → {PopulationManager.Instance.CurrentPopulation}, 만족도 {Satisfaction:F1})");
            yield break;
        }

        private IEnumerator Pop_UnlocksAllTiersAt300()
        {
            PopulationManager.Instance.AddPopulation(300);
            Expect(PopulationManager.Instance.CurrentPopulation == 300, $"인구 {PopulationManager.Instance.CurrentPopulation}");
            var locked = Buildings.AllBuildingData.Where(d => d != null && !Buildings.IsBuildingUnlocked(d)).Select(d => d.buildingID).ToList();
            Expect(locked.Count == 0, $"아직 잠긴 건물: {string.Join(", ", locked)}");
            yield break;
        }

        private float _middle;

        private IEnumerator Satisfaction_MiddleVillage84()
        {
            PlaceLayout(MiddleLayout);
            Place("livestock_farm", 50, 30);
            EnsureSupply();
            yield return Frames(1);
            _middle = Satisfaction;
            Expect(Mathf.Abs(_middle - 84f) <= 1.5f, $"중간 단계 평균 만족도 {_middle:F1}% (84±1.5)");
        }

        private IEnumerator Satisfaction_EssentialsRaise()
        {
            PlaceLayout(EssentialLayout);
            Place("landfill", 54, 34);
            EnsureSupply();
            yield return Frames(1);
            var service = ServiceAccessibilityManager.Instance;
            foreach (var type in new[] { ServiceType.Hospital, ServiceType.School, ServiceType.FireStation, ServiceType.Police })
                Expect(service.HasService(type), $"{type} 미설치");
            Expect(Satisfaction > _middle, $"필수 시설을 지어도 만족도가 오르지 않음 ({_middle:F1} → {Satisfaction:F1})");
            Expect(GameOutcomeManager.Instance.IsInfrastructureNormalized(), "인프라 정상화 판정이 아님");
        }

        // ───────────────────────── 4. 전력·가스·정산·계절 ─────────────────────────

        private IEnumerator Power_BuildingPlusSeasonal()
        {
            var power = PowerManager.Instance;
            int buildingSum = GameLoopManager.Instance.PlacedBuildings.Where(b => b != null).Sum(b => b.Data.requiredPower);
            Expect(power.BuildingPowerUsage == buildingSum, $"건물 소비 {power.BuildingPowerUsage} (합계 {buildingSum})");
            Expect(power.SeasonalPowerUsage == power.GetSeasonalDemand(TimeM.CurrentSeason), $"계절 소비 {power.SeasonalPowerUsage}");
            Expect(power.CurrentUsedPower == power.BuildingPowerUsage + power.SeasonalPowerUsage, $"필요 전력 {power.CurrentUsedPower}");
            Expect(power.MaxSeasonalDemand == Mathf.RoundToInt(power.MinSeasonalDemand * 2.5f) || Mathf.Abs(power.MaxSeasonalDemand - power.MinSeasonalDemand * 2.5f) <= 1f,
                $"최고 {power.MaxSeasonalDemand} / 최저 {power.MinSeasonalDemand}");
            Expect(power.MinSeasonalDemand == 30, $"인구 300명 최저 소비 {power.MinSeasonalDemand} (30이어야 함)");
            yield break;
        }

        private IEnumerator Power_PoleGridRadius()
        {
            var power = PowerManager.Instance;
            var pole = Place("power_pole", 1, 1);
            if (!Expect(pole != null, "전신주를 짓지 못함")) yield break;
            yield return Frames(1);
            Expect(pole.GetComponent<UtilityPoleInstance>() != null, "UtilityPoleInstance 자동 부착 안 됨");
            Expect(power.HasPowerGrid, "전력망이 생기지 않음");
            Expect(power.IsHousingPowered(Grid[0, 0]), "반경 안 주택(x=0)에 전기 없음");
            Expect(!power.IsHousingPowered(Grid[29, 0]), "반경 밖 주택(x=29)에 전기 들어감");
            Demolish(pole);
            yield return Frames(1);
            Expect(!power.HasPowerGrid, "전신주 철거 후에도 전력망 있음");
        }

        private IEnumerator Economy_MonthlySettlement()
        {
            var eco = EconomyManager.Instance;
            var power = PowerManager.Instance;
            var gas = GasManager.Instance;
            // 정산 바로 전 주까지 진행
            while (TimeM.CurrentWeek != TimeManager.WeeksPerMonth) AdvanceWeek();
            int before = eco.CurrentMoney;
            int maintenance = GameLoopManager.Instance.GetTotalMaintenanceCost();
            int requested = eco.GetRequestedSubsidy();
            int powerCost = power.MonthlyPurchaseCost, gasCost = gas.MonthlyPurchaseCost;
            AdvanceWeek();
            Expect(eco.LastMonthlyIncome == requested, $"지원금 {eco.LastMonthlyIncome:N0} (신청 {requested:N0})");
            Expect(eco.LastMaintenancePaid == maintenance, $"유지비 {eco.LastMaintenancePaid:N0} (합계 {maintenance:N0})");
            int expected = before + requested - maintenance - powerCost - gasCost;
            Expect(eco.CurrentMoney == expected, $"잔액 {before:N0} → {eco.CurrentMoney:N0} (예상 {expected:N0})");
            Expect(power.CurrentAvailablePower == power.TargetPurchasedPower, $"정산 후 전력 {power.CurrentAvailablePower}");
            Expect(gas.CurrentAvailableGas == gas.TargetPurchasedGas, $"정산 후 가스 {gas.CurrentAvailableGas}");
            yield break;
        }

        private IEnumerator Power_TargetAppliesNextMonth()
        {
            var power = PowerManager.Instance;
            int now = power.CurrentAvailablePower;
            power.SetTargetPurchasedPower(1237);
            Expect(power.TargetPurchasedPower == 1240, $"10 단위 맞춤 {power.TargetPurchasedPower}");
            Expect(power.CurrentAvailablePower == now, "이번 달 보유 전력이 바로 바뀜");
            AdvanceToNextMonth();
            Expect(power.CurrentAvailablePower == 1240, $"다음 달 보유 전력 {power.CurrentAvailablePower}");
            power.SetTargetPurchasedPower(1000);
            yield break;
        }

        private IEnumerator Gas_TargetAppliesNextMonth()
        {
            var gas = GasManager.Instance;
            int now = gas.CurrentAvailableGas;
            gas.SetTargetPurchasedGas(333);
            Expect(gas.TargetPurchasedGas == 330, $"10 단위 맞춤 {gas.TargetPurchasedGas}");
            Expect(gas.CurrentAvailableGas == now, "이번 달 보유 가스가 바로 바뀜");
            AdvanceToNextMonth();
            Expect(gas.CurrentAvailableGas == 330, $"다음 달 보유 가스 {gas.CurrentAvailableGas}");
            gas.SetTargetPurchasedGas(500);
            yield break;
        }

        private IEnumerator Season_YearCyclePowerGas()
        {
            var power = PowerManager.Instance;
            var gas = GasManager.Instance;
            int changes = 0;
            Action<Season> onSeason = s => changes++;
            TimeM.OnSeasonChanged += onSeason;
            var seen = new Dictionary<Season, (int power, int gas)>();
            for (int i = 0; i < TimeManager.MonthsPerYear; i++)
            {
                AdvanceToNextMonth();
                if (PopulationManager.Instance.CurrentPopulation != 300)
                    PopulationManager.Instance.ApplySaveState(new PopulationSaveState { currentPopulation = 300 });
                Expect(TimeM.CurrentSeason == SeasonUtil.FromMonth(TimeM.CurrentMonth), $"{TimeM.CurrentMonth}월 계절 {TimeM.CurrentSeason}");
                seen[TimeM.CurrentSeason] = (power.SeasonalPowerUsage, gas.CurrentUsedGas);
            }
            TimeM.OnSeasonChanged -= onSeason;
            yield return Frames(1);

            Expect(changes == 4, $"1년 동안 계절 변경 {changes}번");
            if (!Expect(seen.Count == 4, $"계절 {seen.Count}개만 지남")) yield break;
            Expect(seen[Season.Summer].power == 75 && seen[Season.Winter].power == 30 && seen[Season.Spring].power == 52 && seen[Season.Autumn].power == 52,
                $"전력 여름 {seen[Season.Summer].power}, 겨울 {seen[Season.Winter].power}, 봄 {seen[Season.Spring].power}, 가을 {seen[Season.Autumn].power} (75/30/52/52)");
            Expect(seen[Season.Winter].gas == 75 && seen[Season.Summer].gas == 30 && seen[Season.Spring].gas == 52 && seen[Season.Autumn].gas == 52,
                $"가스 겨울 {seen[Season.Winter].gas}, 여름 {seen[Season.Summer].gas}, 봄 {seen[Season.Spring].gas}, 가을 {seen[Season.Autumn].gas} (75/30/52/52)");
        }

        private IEnumerator Power_BlackoutPenalty()
        {
            var power = PowerManager.Instance;
            float before = Satisfaction;
            power.SetTargetPurchasedPower(0);
            power.ProcessMonthlyPowerPurchase();
            yield return Frames(1);
            Expect(power.CurrentAvailablePower == 0, $"전력 {power.CurrentAvailablePower}");
            Expect(Mathf.Approximately(power.GetPowerBlackoutSatisfactionPenalty(), 30f), $"전면 정전 감점 {power.GetPowerBlackoutSatisfactionPenalty():F1}");
            Expect(Satisfaction < before - 20f, $"정전인데 만족도 {before:F1} → {Satisfaction:F1}");
            power.SetTargetPurchasedPower(1000);
            power.ProcessMonthlyPowerPurchase();
            yield return Frames(1);
            Expect(Mathf.Abs(Satisfaction - before) < 0.01f, $"전력 회복 후 만족도 {Satisfaction:F1} (원래 {before:F1})");
        }

        private IEnumerator Gas_ShortagePenalty()
        {
            var gas = GasManager.Instance;
            float before = Satisfaction;
            int need = gas.CurrentUsedGas;
            gas.SetTargetPurchasedGas((need / 2) / 10 * 10);
            gas.ProcessMonthlyGasPurchase();
            yield return Frames(1);
            float expectedPenalty = 30f * (1f - (float)gas.CurrentAvailableGas / need);
            Expect(Mathf.Abs(gas.GetGasShortageSatisfactionPenalty() - expectedPenalty) < 0.01f,
                $"가스 부족 감점 {gas.GetGasShortageSatisfactionPenalty():F2} (예상 {expectedPenalty:F2}, {gas.CurrentAvailableGas}/{need})");
            Expect(Satisfaction < before, $"가스 부족인데 만족도 {before:F1} → {Satisfaction:F1}");
            gas.SetTargetPurchasedGas(500);
            gas.ProcessMonthlyGasPurchase();
            yield return Frames(1);
            Expect(Mathf.Abs(Satisfaction - before) < 0.01f, $"가스 회복 후 만족도 {Satisfaction:F1} (원래 {before:F1})");
        }

        // ───────────────────────── 5. 무작위 배치 (seed) ─────────────────────────

        private IEnumerator Build_RandomPlacementSeed()
        {
            var rng = new System.Random(_seed);
            string[] ids = { "house", "road", "village_hall", "convenience_store", "bus_stop", "senior_center", "hospital", "school" };
            var placed = new List<BuildingInstance>();
            EconomyManager.Instance.AddMoney(5_000_000);
            int attempts = 0;
            while (placed.Count < 25 && attempts++ < 400)
            {
                string id = ids[rng.Next(ids.Length)];
                int x = rng.Next(30, 56), y = rng.Next(12, 28);
                float rot = rng.Next(4) * 90f;
                var b = Place(id, x, y, rot);
                if (b == null) continue;
                placed.Add(b);
                int size = SizeOf(b.Data);
                if (!Expect(Mathf.Approximately(Mathf.Repeat(b.RotationY, 360f), rot), $"{id}@{x},{y} 회전 {b.RotationY} (요청 {rot})")) break;
                if (!Expect(Quaternion.Angle(Buildings.GetPlacementRotation(b.Data, rot), b.transform.rotation) < 0.5f, $"{id}@{x},{y} 모습 회전 불일치")) break;
                int occupied = 0;
                for (int i = 0; i < size; i++) for (int j = 0; j < size; j++) if (Grid[x + i, y + j] == b) occupied++;
                if (!Expect(occupied == size * size, $"{id}@{x},{y} 점유 칸 {occupied}/{size * size}")) break;
            }
            Expect(placed.Count >= 10, $"무작위 배치 {placed.Count}개만 성공 (시도 {attempts})");
            yield return Frames(1);

            var cells = new List<Vector2Int>();
            foreach (var b in placed)
            {
                int size = SizeOf(b.Data);
                for (int i = 0; i < size; i++) for (int j = 0; j < size; j++) cells.Add(b.OriginGridPos + new Vector2Int(i, j));
            }
            foreach (var b in placed) Demolish(b);
            yield return Frames(1);
            int left = cells.Count(c => Grid[c.x, c.y] != null);
            Expect(left == 0, $"철거 후 남은 점유 칸 {left}개");
        }

        // ───────────────────────── 6. 세이브 / 이어하기 ─────────────────────────

        private class Snapshot
        {
            public int year, month, week, totalWeeks, money, population, powerTarget, powerAvailable, gasTarget, gasAvailable, gasUsed, buildings, weeksElapsed;
            public float satisfaction;
            public string layout;

            public static Snapshot Take() => new Snapshot
            {
                year = TimeM.CurrentYear, month = TimeM.CurrentMonth, week = TimeM.CurrentWeek, totalWeeks = TimeM.TotalWeeksPassed,
                money = EconomyManager.Instance.CurrentMoney,
                population = PopulationManager.Instance.CurrentPopulation,
                powerTarget = PowerManager.Instance.TargetPurchasedPower, powerAvailable = PowerManager.Instance.CurrentAvailablePower,
                gasTarget = GasManager.Instance.TargetPurchasedGas, gasAvailable = GasManager.Instance.CurrentAvailableGas, gasUsed = GasManager.Instance.CurrentUsedGas,
                buildings = GameLoopManager.Instance.PlacedBuildings.Count(b => b != null),
                weeksElapsed = GameOutcomeManager.Instance.WeeksElapsed,
                satisfaction = SatisfactionManager.Instance.CurrentAverageSatisfaction,
                layout = string.Join(";", GameLoopManager.Instance.PlacedBuildings.Where(b => b != null)
                    .Select(b => $"{b.Data.buildingID}@{b.OriginGridPos.x},{b.OriginGridPos.y}/{Mathf.Repeat(b.RotationY, 360f)}").OrderBy(s => s)),
            };

            public string Diff(Snapshot o)
            {
                var d = new List<string>();
                if ((year, month, week, totalWeeks) != (o.year, o.month, o.week, o.totalWeeks)) d.Add($"날짜 {year}/{month}/{week}({totalWeeks}) → {o.year}/{o.month}/{o.week}({o.totalWeeks})");
                if (money != o.money) d.Add($"돈 {money} → {o.money}");
                if (population != o.population) d.Add($"인구 {population} → {o.population}");
                if ((powerTarget, powerAvailable) != (o.powerTarget, o.powerAvailable)) d.Add($"전력 {powerAvailable}({powerTarget}) → {o.powerAvailable}({o.powerTarget})");
                if ((gasTarget, gasAvailable, gasUsed) != (o.gasTarget, o.gasAvailable, o.gasUsed)) d.Add($"가스 {gasUsed}/{gasAvailable}({gasTarget}) → {o.gasUsed}/{o.gasAvailable}({o.gasTarget})");
                if (buildings != o.buildings || layout != o.layout) d.Add($"건물 {buildings} → {o.buildings}개 (배치 다름: {layout != o.layout})");
                if (weeksElapsed != o.weeksElapsed) d.Add($"엔딩 판정 {weeksElapsed} → {o.weeksElapsed}주");
                if (Mathf.Abs(satisfaction - o.satisfaction) > 0.01f) d.Add($"만족도 {satisfaction:F2} → {o.satisfaction:F2}");
                return string.Join(", ", d);
            }
        }

        private Snapshot _beforeSave;

        private IEnumerator Save_WritesFile()
        {
            SaveSystem.FolderOverrideForTests = Path.Combine(Application.temporaryCachePath, $"BuildTestSaves_{_seed}");
            SaveSystem.DeleteSave();
            Place("convenience_store", 40, 20, 270f); // 회전한 건물도 저장
            EconomyManager.Instance.SetGovernmentSubsidyAmount(15100);
            yield return Frames(1);
            _beforeSave = Snapshot.Take();
            Expect(SaveSystem.SaveGame(), $"저장 실패: {SaveSystem.LastError}");
            Expect(SaveSystem.HasSave(), "저장 파일이 없음");
            var summary = SaveSystem.GetSummary();
            Expect(summary != null && summary.money == _beforeSave.money && summary.population == _beforeSave.population && summary.buildingCount == _beforeSave.buildings,
                summary == null ? $"요약 없음: {SaveSystem.LastError}" : $"요약 돈 {summary.money} 인구 {summary.population} 건물 {summary.buildingCount}");
        }

        private IEnumerator Save_ContinueRestoresState()
        {
            if (!Expect(_beforeSave != null, "저장 단계가 먼저 실패함")) yield break;
            var oldLoop = GameLoopManager.Instance;
            if (!Expect(SaveSystem.ContinueGame(), $"이어하기 실패: {SaveSystem.LastError}")) yield break;
            float deadline = Time.realtimeSinceStartup + 30f;
            while ((SaveSystem.HasPendingLoad || GameLoopManager.Instance == null || GameLoopManager.Instance == oldLoop) && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return Frames(5);
            ClearDialogues();
            if (!Expect(!SaveSystem.HasPendingLoad && GameLoopManager.Instance != oldLoop, "새 씬에서 저장 데이터가 적용되지 않음")) yield break;
            var after = Snapshot.Take();
            string diff = _beforeSave.Diff(after);
            Expect(diff.Length == 0, $"이어하기 후 달라짐: {diff}");
            Expect(EconomyManager.Instance.GetRequestedSubsidy() == 15100, $"신청 지원금 {EconomyManager.Instance.GetRequestedSubsidy()}");
            var triggers = FindObjectOfType<DialogueTriggers>();
            Expect(triggers != null && triggers.StoryStarted, "이어하기 후 이야기 상태가 처음으로 돌아감");
        }

        private IEnumerator Save_PlayContinuesAfterLoad()
        {
            TimeM.SetGameSpeed(GameSpeed.Pause);
            int week = WeekIndex, elapsed = GameOutcomeManager.Instance.WeeksElapsed;
            AdvanceWeeks(2);
            Expect(WeekIndex - week == 2 && GameOutcomeManager.Instance.WeeksElapsed - elapsed == 2,
                $"이어서 날짜 +{WeekIndex - week}, 판정 +{GameOutcomeManager.Instance.WeeksElapsed - elapsed}");
            Expect(Grid[40, 20] != null && Mathf.Approximately(Mathf.Repeat(Grid[40, 20].RotationY, 360f), 270f), "회전한 편의점이 복원되지 않음");
            Expect(Place("house", 40, 20) == null, "복원된 건물 자리에 겹쳐 지어짐");
            var house = Place("house", 58, 38);
            Expect(house != null, "이어서 건물을 짓지 못함");
            Demolish(house);
            yield return Frames(1);
            SaveSystem.DeleteSave();
        }

        // ───────────────────────── 7. 엔딩 ─────────────────────────

        private IEnumerator Outcome_DefeatAfter12BadWeeks()
        {
            var outcome = GameOutcomeManager.Instance;
            GameResult ended = GameResult.None;
            int lastBad = 0;
            Action<GameResult> onEnd = r => ended = r;
            Action<int, int> onBad = (w, limit) => lastBad = w;
            outcome.OnGameEnded += onEnd;
            outcome.OnBadStreakChanged += onBad;

            foreach (var b in GameLoopManager.Instance.PlacedBuildings.Where(b => b != null && b.Data.category != BuildingType.Housing).ToList()) Demolish(b);
            yield return Frames(1);
            PowerManager.Instance.SetTargetPurchasedPower(0);
            PowerManager.Instance.ProcessMonthlyPowerPurchase();
            foreach (int x in new[] { 0, 8, 16, 24 }) Place("landfill", x, 2);
            yield return Frames(1);
            Expect(SatisfactionManager.Instance.CurrentState == SatisfactionState.Bad, $"마을 상태 {SatisfactionManager.Instance.CurrentState} ({Satisfaction:F1})");

            int weeks = 0;
            while (ended == GameResult.None && weeks < 20)
            {
                AdvanceWeek();
                weeks++;
                PowerManager.Instance.SetTargetPurchasedPower(0);
            }
            outcome.OnGameEnded -= onEnd;
            outcome.OnBadStreakChanged -= onBad;
            Expect(ended == GameResult.Defeat, $"결과 {ended} ({weeks}주 진행)");
            Expect(weeks == 12 && lastBad == 12, $"{weeks}주 만에 배드엔딩, 나쁨 연속 {lastBad}주 (12주여야 함)");
            Expect(TimeM.IsEventPaused, "엔딩 후 시간이 멈추지 않음");
        }

        private IEnumerator Outcome_VictoryAt480Weeks()
        {
            // 같은 판에서 엔딩 판정을 새로 시작
            var old = GameOutcomeManager.Instance;
            TimeM.ReleaseEventPause(old);
            Destroy(old);
            yield return Frames(1);
            GameLoopManager.Instance.gameObject.AddComponent<GameOutcomeManager>();
            yield return Frames(2);
            var outcome = GameOutcomeManager.Instance;
            if (!Expect(outcome != null && outcome != old && !outcome.IsGameOver, "새 엔딩 판정을 시작하지 못함")) yield break;

            foreach (var b in GameLoopManager.Instance.PlacedBuildings.Where(b => b != null && b.Data.buildingID == "landfill").ToList()) Demolish(b);
            yield return Frames(1);
            EconomyManager.Instance.AddMoney(10_000_000);
            PlaceLayout(MiddleLayout);
            PlaceLayout(EssentialLayout);
            EnsureSupply();
            yield return Frames(1);

            GameResult ended = GameResult.None;
            Action<GameResult> onEnd = r => ended = r;
            outcome.OnGameEnded += onEnd;
            int guard = 0;
            while (ended == GameResult.None && guard++ < outcome.VictoryWeeks + 60)
            {
                AdvanceWeek();
                if (PowerManager.Instance.IsPowerShortage || GasManager.Instance.IsGasShortage) EnsureSupply();
            }
            outcome.OnGameEnded -= onEnd;
            Expect(ended == GameResult.Victory, $"결과 {ended} ({outcome.WeeksElapsed}주, 좋음 {SatisfactionManager.Instance.GoodHousingRatio:P0})");
            Expect(outcome.WeeksElapsed == 480 && outcome.VictoryWeeks == 480, $"{outcome.WeeksElapsed}주째 판정 (480주여야 함)");
        }

        // ───────────────────────── 8. 콘솔 ─────────────────────────

        private IEnumerator Console_NoErrors()
        {
            Expect(_consoleErrors.Count == 0, $"콘솔 에러 {_consoleErrors.Count}개: {string.Join(" | ", _consoleErrors.Take(3))}");
            yield break;
        }
    }
}
#endif

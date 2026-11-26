using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.EventSystem;
using InfraTown.Population;
using InfraTown.Power;
using InfraTown.Satisfaction;
using InfraTown.TimeSystem;

namespace InfraTown.Save
{
    /// <summary>
    /// 세이브 기능 (사용자 기획 2026-11-24). 카메라 위치·줌을 뺀 게임 상태 전부를 JSON 파일 하나에 저장한다.
    /// 슬롯 5개 (1~5번, 쯔꾸르식): 저장할 때 슬롯을 골라 저장하고, 불러올 때도 고른 슬롯을 불러온다.
    ///
    /// UI(팀원 1) 연결:
    ///   슬롯 목록 화면 (저장·불러오기 공용) → SaveSystem.GetAllSlots()  (5칸: 빈 슬롯 / 깨진 파일 / 요약 정보, 현재·최근 슬롯 표시)
    ///   ESC '저장' 화면에서 슬롯 선택     → SaveSystem.SaveGame(slot)   (덮어쓸지 묻는 건 UI가 HasSave(slot)로 판단)
    ///   ESC '저장 후 나가기'              → SaveSystem.SaveAndQuitToTitle(slot)  (실패하면 false, LastError — 나가지 않음)
    ///   타이틀 '이어하기'                 → SaveSystem.ContinueLatest()  (직전에 저장한 게임 = 가장 최근에 저장된 슬롯, 자동 저장 포함)
    ///                                       버튼 표시 여부는 SaveSystem.HasAnySave()
    ///   (팀원 1, Jinyu234) 게임을 끌 때 이어하기 전용 파일 resume.json에 저장 → 타이틀 '이어하기' HasResume() / ContinueResume()
    ///   타이틀 '불러오기' 화면에서 슬롯 선택 → SaveSystem.ContinueGame(slot)  (저장한 씬을 열고 그 시점으로 되돌림)
    ///   타이틀 '새로 시작'                → SaveSystem.StartNewGame(게임 씬 이름)  (이전 게임 상태를 지우고 처음부터 — 이름은 게임 씬에서 입력, 저장 파일은 지우지 않음)
    ///   타이틀 '불러오기' 슬롯 화면       → SaveSystem.GetLoadSlots()  (자동 저장 칸 + 1~5번, 6칸)
    ///   슬롯 지우기                       → SaveSystem.DeleteSave(slot)
    ///   자동 저장                         → AutoSaveManager가 1주마다 자동 저장 전용 슬롯(AutoSaveSlot)에만 덮어씀 (사용자 기획 2026-11-25).
    ///                                       플레이어는 이 슬롯에 직접 저장할 수 없고 '저장하기' 화면(GetAllSlots, 1~5번)에는 나오지 않는다
    ///
    /// slot 인자를 생략하거나 0을 주면 '현재 슬롯'(CurrentSlot: 이번 게임에서 마지막으로 저장했거나 불러온 슬롯, 처음엔 1번).
    /// 지우기(DeleteSave)는 실수를 막기 위해 슬롯 번호를 꼭 줘야 한다.
    ///
    /// 파일: Application.persistentDataPath/Saves/save_{1~5}.json. 임시 파일에 쓴 뒤 바꿔 끼워 저장 도중 꺼져도 이전 저장이 남는다.
    /// 이어하기할 씬은 Build Settings에 들어 있어야 열린다.
    /// </summary>
    public static class SaveSystem
    {
        public const string DefaultTitleScene = "Title";

        /// <summary>슬롯 수 (1~SlotCount번)</summary>
        public const int SlotCount = 5;

        /// <summary>slot 인자에 이 값(0)을 주면 현재 슬롯</summary>
        public const int CurrentSlotArg = 0;

        /// <summary>
        /// 자동 저장 전용 슬롯 (사용자 기획 2026-11-25). 1~5번과 따로 있고, 1주마다 자동 저장만 여기에 덮어쓴다
        /// (새로 시작한 게임도 같은 슬롯을 덮어씀). 플레이어가 직접 저장할 수 없고(SaveGame이 거부), 저장하기 화면(GetAllSlots)에는 나오지 않는다.
        /// 불러오기 화면(GetLoadSlots)·이어하기(ContinueLatest)·ContinueGame(AutoSaveSlot)으로는 불러올 수 있다. 파일 save_auto.json
        /// </summary>
        public const int AutoSaveSlot = -1;

        /// <summary>테스트에서 저장 폴더를 바꿀 때 (null이면 persistentDataPath/Saves)</summary>
        public static string FolderOverrideForTests;

        public static string SaveFolder => FolderOverrideForTests ?? Path.Combine(Application.persistentDataPath, "Saves");

        /// <summary>
        /// 지금 진행 중인 게임의 슬롯 (마지막으로 저장했거나 불러온 슬롯, 처음엔 1). slot 인자를 생략하면 이 슬롯을 쓴다.
        /// 게임을 꺼도 유지되지는 않는다 (다음 실행에선 1번부터, 불러오면 그 슬롯).
        /// </summary>
        public static int CurrentSlot { get; private set; } = 1;

        /// <summary>현재 슬롯이 바뀜 (새 슬롯)</summary>
        public static event Action<int> OnCurrentSlotChanged;

        /// <summary>플레이어가 직접 저장할 수 있는 슬롯(1~5번)인지</summary>
        public static bool IsValidSlot(int slot) => slot >= 1 && slot <= SlotCount;

        /// <summary>불러올 수 있는 슬롯(1~5번 + 자동 저장 슬롯)인지</summary>
        public static bool IsLoadableSlot(int slot) => IsValidSlot(slot) || slot == AutoSaveSlot;

        /// <summary>현재 슬롯을 직접 정함 (예: 새 게임을 시작하며 슬롯을 고르는 UI). 범위 밖이면 false</summary>
        public static bool SetCurrentSlot(int slot)
        {
            if (!IsValidSlot(slot)) return false;
            if (CurrentSlot == slot) return true;
            CurrentSlot = slot;
            OnCurrentSlotChanged?.Invoke(slot);
            return true;
        }

        /// <summary>0이면 현재 슬롯으로 바꿈</summary>
        private static int Resolve(int slot) => slot == CurrentSlotArg ? CurrentSlot : slot;

        /// <summary>슬롯 번호가 맞는지 확인 (틀리면 LastError). allowAutoSave면 자동 저장 슬롯도 허용</summary>
        private static bool CheckSlot(int slot, bool allowAutoSave = false)
        {
            if (IsValidSlot(slot) || (allowAutoSave && slot == AutoSaveSlot)) return true;
            LastError = slot == AutoSaveSlot
                ? "자동 저장 슬롯에는 직접 저장할 수 없어요."
                : $"슬롯은 1~{SlotCount}번만 있어요 (받은 값: {slot}).";
            return false;
        }

        public static string GetSavePath(int slot = CurrentSlotArg)
        {
            slot = Resolve(slot);
            return Path.Combine(SaveFolder, slot == AutoSaveSlot ? "save_auto.json" : $"save_{slot}.json");
        }

        /// <summary>이어하기 전용 파일 (팀원 1 Jinyu234: 게임을 끌 때 저장 — 1주 자동 저장 슬롯과는 별개 파일)</summary>
        private static string ResumePath => Path.Combine(SaveFolder, "resume.json");

        /// <summary>
        /// 타이틀 '이어하기'로 이어할 게임이 있는지: 종료 직전 저장(resume.json) 또는 1주 자동 저장 (2026-11-26 — 강제 종료·튕김으로
        /// resume.json이 안 써졌어도 자동 저장으로 이어가도록)
        /// </summary>
        public static bool HasResume() => File.Exists(ResumePath) || HasAutoSave();

        /// <summary>
        /// '이어하기'가 불러올 데이터: resume.json과 자동 저장 중 파일 수정 시각이 더 최근인 것 (읽을 수 없으면 다른 쪽).
        /// fromAutoSave = 자동 저장을 골랐는지. 둘 다 없거나 못 읽으면 null (LastError)
        /// </summary>
        public static SaveData LoadResumeData(out bool fromAutoSave)
        {
            LastError = null;
            fromAutoSave = false;
            string auto = GetSavePath(AutoSaveSlot);
            bool hasResume = File.Exists(ResumePath), hasAuto = File.Exists(auto);
            if (!hasResume && !hasAuto) { LastError = "이어할 게임이 없어요."; return null; }

            bool autoFirst = hasAuto && (!hasResume || File.GetLastWriteTimeUtc(auto) > File.GetLastWriteTimeUtc(ResumePath));
            foreach (bool useAuto in autoFirst ? new[] { true, false } : new[] { false, true })
            {
                if (useAuto ? !hasAuto : !hasResume) continue;
                var data = ReadSnapshot(useAuto ? auto : ResumePath, false);
                if (data != null) { fromAutoSave = useAuto; LastError = null; return data; }
            }
            Debug.LogError($"[SaveSystem] 이어하기 파일을 읽지 못했어요: {LastError}");
            return null;
        }

        /// <summary>마지막 실패 이유 (성공하면 null)</summary>
        public static string LastError { get; private set; }

        /// <summary>저장 성공 (슬롯)</summary>
        public static event Action<int> OnGameSaved;

        /// <summary>저장 시점으로 되돌리기가 끝났을 때 (이어하기로 씬이 뜬 직후)</summary>
        public static event Action<SaveData> OnGameLoaded;

        /// <summary>씬을 연 뒤 적용할 저장 데이터가 기다리는 중</summary>
        public static bool HasPendingLoad => _pending != null;

        /// <summary>지금 저장 데이터를 적용하는 중 (건물 복원 등으로 생기는 연출·대화를 막는 데 사용)</summary>
        public static bool IsApplying { get; private set; }

        private static SaveData _pending;
        private static bool _resumeSavedForQuit;

        // ───────────────── 저장 ─────────────────

        /// <summary>그 슬롯에 저장 파일이 있는지 (생략하면 현재 슬롯, 자동 저장 슬롯도 가능)</summary>
        public static bool HasSave(int slot = CurrentSlotArg)
        {
            slot = Resolve(slot);
            return IsLoadableSlot(slot) && File.Exists(GetSavePath(slot));
        }

        /// <summary>자동 저장이 있는지</summary>
        public static bool HasAutoSave() => HasSave(AutoSaveSlot);

        /// <summary>슬롯(1~5번 + 자동 저장) 중 하나라도 저장이 있는지 (타이틀 '이어하기'·'불러오기' 버튼 표시)</summary>
        public static bool HasAnySave()
        {
            if (HasAutoSave()) return true;
            for (int s = 1; s <= SlotCount; s++) if (HasSave(s)) return true;
            return false;
        }

        /// <summary>
        /// 현재 게임을 그 슬롯(1~5번)에 저장 (생략하면 현재 슬롯). 성공하면 그 슬롯이 현재 슬롯이 된다.
        /// 자동 저장 슬롯은 직접 저장할 수 없다. 실패하면 false (LastError)
        /// </summary>
        public static bool SaveGame(int slot = CurrentSlotArg)
        {
            LastError = null;
            slot = Resolve(slot);
            if (!CheckSlot(slot)) { Debug.LogWarning($"[SaveSystem] {LastError}"); return false; }
            if (!WriteSave(slot)) return false;
            SetCurrentSlot(slot);
            OnGameSaved?.Invoke(slot);
            return true;
        }

        /// <summary>
        /// 자동 저장 (AutoSaveManager가 1주마다 부름): 자동 저장 전용 슬롯에 덮어쓴다. 현재 슬롯은 바꾸지 않는다. 실패하면 false (LastError)
        /// </summary>
        public static bool AutoSave()
        {
            LastError = null;
            if (!WriteSave(AutoSaveSlot)) return false;
            OnAutoSaved?.Invoke();
            return true;
        }

        /// <summary>자동 저장 성공</summary>
        public static event Action OnAutoSaved;

        /// <summary>지금 시각과 '이미 있는 저장 중 가장 나중 시각 + 1틱' 중 늦은 쪽 (방금 저장한 파일이 늘 가장 최근이 되게)</summary>
        private static DateTime NextWriteStamp()
        {
            DateTime stamp = DateTime.UtcNow;
            foreach (int s in AllSlots())
            {
                string p = GetSavePath(s);
                if (!File.Exists(p)) continue;
                DateTime t = File.GetLastWriteTimeUtc(p);
                if (t >= stamp) stamp = t.AddTicks(1);
            }
            if (File.Exists(ResumePath))
            {
                DateTime t = File.GetLastWriteTimeUtc(ResumePath); // 이어하기가 resume.json과 자동 저장 중 최근 것을 고르므로 함께 비교
                if (t >= stamp) stamp = t.AddTicks(1);
            }
            return stamp;
        }

        /// <summary>자동 저장 슬롯 + 1~5번</summary>
        private static IEnumerable<int> AllSlots()
        {
            yield return AutoSaveSlot;
            for (int s = 1; s <= SlotCount; s++) yield return s;
        }

        /// <summary>이어하기 파일(resume.json)에 지금 게임과 현재 슬롯을 저장 (팀원 1 Jinyu234)</summary>
        public static bool SaveResume()
        {
            LastError = null;
            if (TimeManager.Instance == null || HasPendingLoad || IsApplying)
            {
                LastError = "이어하기에 저장할 게임이 준비되지 않았어요.";
                return false;
            }
            // 배드엔딩 게임 오버 상태는 저장하지 않음 (2026-11-26, 자동 저장과 같은 규칙 — 패배한 채 끄면 '이어하기'가 게임 오버 화면으로 열리던 문제).
            // 직전 이어하기·자동 저장을 그대로 남긴다. 종료를 막지 않도록 true
            if (GameOutcomeManager.Instance != null && GameOutcomeManager.Instance.IsGameOver)
            {
                Debug.Log("[SaveSystem] 게임 오버 상태라 이어하기 저장을 하지 않습니다.");
                return true;
            }
            return WriteSnapshot(ResumePath, CurrentSlot, "이어하기");
        }

        public static bool SaveResumeForQuit()
        {
            if (!_resumeSavedForQuit) _resumeSavedForQuit = SaveResume();
            return _resumeSavedForQuit;
        }

        private static void HandleApplicationQuit()
        {
            var time = TimeManager.Instance;
            if (time != null && time.gameObject.scene == SceneManager.GetActiveScene())
                SaveResumeForQuit();
        }

        // 자동 저장도 현재 슬롯을 기억 → '이어하기'가 자동 저장을 골라도 이어서 '저장'하면 하던 슬롯에
        private static bool WriteSave(int slot) => WriteSnapshot(GetSavePath(slot), slot == AutoSaveSlot ? CurrentSlot : 0, SlotLabel(slot));

        /// <summary>지금 게임을 그 파일에 씀 (임시 파일에 쓴 뒤 교체). resumeSlot은 이어하기 파일이 기억할 현재 슬롯</summary>
        private static bool WriteSnapshot(string path, int resumeSlot, string label)
        {
            try
            {
                var data = CaptureCurrentGame();
                if (data == null) return false;
                data.resumeSlot = resumeSlot;

                Directory.CreateDirectory(SaveFolder);
                string temp = path + ".tmp";
                DateTime stamp = NextWriteStamp(); // 쓰기 전에 다른 저장들의 시각을 봄
                File.WriteAllText(temp, JsonUtility.ToJson(data, true));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                // 이어하기(GetLatestSlot)는 파일 수정 시각으로 '가장 최근 저장'을 고른다. 자동 저장 직후 같은 순간에 직접 저장하면
                // 시계 해상도 때문에 시각이 같게 찍혀 엉뚱한 슬롯이 골라질 수 있으므로, 방금 저장한 파일이 항상 가장 나중이 되게 맞춘다
                File.SetLastWriteTimeUtc(path, stamp);

                Debug.Log($"[SaveSystem] 저장 완료 ({label}): {path}");
                return true;
            }
            catch (Exception e)
            {
                LastError = $"저장하지 못했어요: {e.Message}";
                Debug.LogError($"[SaveSystem] {LastError}");
                return false;
            }
        }

        /// <summary>
        /// ESC 메뉴 '저장 후 나가기': 고른 슬롯에 저장에 성공하면 타이틀 씬을 연다. 실패하면 나가지 않고 false
        /// </summary>
        public static bool SaveAndQuitToTitle(int slot) => SaveAndQuitToTitle(DefaultTitleScene, slot);

        /// <summary>'저장 후 나가기' (타이틀 씬 이름 지정, 슬롯 생략하면 현재 슬롯)</summary>
        public static bool SaveAndQuitToTitle(string titleScene = DefaultTitleScene, int slot = CurrentSlotArg)
        {
            if (!SaveGame(slot)) return false;
            _pending = null;
            Time.timeScale = 1f; // 대화·메뉴로 멈춘 상태가 타이틀까지 이어지지 않도록
            SceneManager.LoadScene(titleScene);
            return true;
        }

        /// <summary>현재 씬의 매니저들에서 저장 데이터를 모음</summary>
        public static SaveData CaptureCurrentGame()
        {
            var time = TimeManager.Instance;
            if (time != null && time.IsSkipping) time.CancelSkip(); // 스킵 도중이면 지금 날짜에서 저장

            var data = new SaveData
            {
                sceneName = SceneManager.GetActiveScene().name,
                savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                playerName = PlayerProfile.HasName ? PlayerProfile.PlayerName : null,
            };

            if (time != null) data.time = time.GetSaveState();
            if (EconomyManager.Instance != null) data.economy = EconomyManager.Instance.GetSaveState();
            if (PopulationManager.Instance != null) data.population = PopulationManager.Instance.GetSaveState();
            if (PowerManager.Instance != null) data.power = PowerManager.Instance.GetSaveState();
            if (InfraTown.Gas.GasManager.Instance != null) data.gas = InfraTown.Gas.GasManager.Instance.GetSaveState();
            if (SatisfactionManager.Instance != null) data.satisfaction = SatisfactionManager.Instance.GetSaveState();
            if (GameOutcomeManager.Instance != null) data.outcome = GameOutcomeManager.Instance.GetSaveState();

            if (GameLoopManager.Instance != null)
            {
                foreach (var b in GameLoopManager.Instance.PlacedBuildings)
                {
                    if (b == null || b.Data == null) continue;
                    data.buildings.Add(new BuildingSaveState
                    {
                        buildingID = b.Data.buildingID,
                        buildingName = b.Data.buildingName,
                        x = b.OriginGridPos.x,
                        y = b.OriginGridPos.y,
                        rotationY = b.RotationY,
                    });
                }
            }

            var dialogue = DialogueEventManager.Instance;
            if (dialogue != null)
            {
                data.dialogue.playedDialogueIds.AddRange(dialogue.PlayedDialogueIds);
                data.dialogue.useEasyText = dialogue.UseEasyText;
                data.dialogue.autoMode = dialogue.IsAutoMode;
            }
            if (CutsceneManager.Instance != null) data.dialogue.playedCutsceneIds.AddRange(CutsceneManager.Instance.PlayedCutsceneIds);

            var triggers = UnityEngine.Object.FindObjectOfType<DialogueTriggers>();
            if (triggers != null) data.dialogueTriggers = triggers.GetSaveState();
            if (TutorialDialogues.Instance != null) data.tutorial = TutorialDialogues.Instance.GetSaveState();

            return data;
        }

        // ───────────────── 불러오기 ─────────────────

        /// <summary>그 슬롯의 저장 파일 읽기 (생략하면 현재 슬롯. 없거나 깨졌으면 null, LastError)</summary>
        public static SaveData LoadData(int slot = CurrentSlotArg) => LoadData(slot, true);

        private static string SlotLabel(int slot) => slot == AutoSaveSlot ? "자동 저장" : $"{slot}번 슬롯";

        private static SaveData LoadData(int slot, bool logErrors)
        {
            LastError = null;
            slot = Resolve(slot);
            if (!CheckSlot(slot, allowAutoSave: true)) return null;
            string path = GetSavePath(slot);
            if (!File.Exists(path)) { LastError = $"{SlotLabel(slot)}에 저장된 게임이 없어요."; return null; }
            return ReadSnapshot(path, logErrors);
        }

        private static SaveData ReadSnapshot(string path, bool logErrors)
        {
            try
            {
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if (data == null) { LastError = "저장 파일이 손상됐어요."; return null; }
                if (data.version > SaveData.CurrentVersion) { LastError = "더 새로운 버전에서 만든 저장 파일이에요."; return null; }
                if (data.version < SaveData.CurrentVersion) { LastError = "이전 버전(일 단위 시간)에서 만든 저장 파일이라 불러올 수 없어요."; return null; }
                return data;
            }
            catch (Exception e)
            {
                LastError = $"저장 파일을 읽지 못했어요: {e.Message}";
                if (logErrors) Debug.LogError($"[SaveSystem] {LastError}");
                return null;
            }
        }

        /// <summary>그 슬롯의 저장 요약 (생략하면 현재 슬롯, 없으면 null)</summary>
        public static SaveSummary GetSummary(int slot = CurrentSlotArg) => ToSummary(Resolve(slot), LoadData(slot));

        /// <summary>
        /// '저장하기' 화면의 슬롯 5칸(1~5번) 정보 — 자동 저장 슬롯은 직접 저장할 수 없으므로 빠진다 (사용자 기획 2026-11-25).
        /// 빈 슬롯·깨진 파일도 칸으로 돌려주고, 깨진 파일은 콘솔 에러 없이 isBroken·error로 알려준다.
        /// </summary>
        public static SaveSlotInfo[] GetAllSlots()
        {
            var slots = new SaveSlotInfo[SlotCount];
            int latest = GetLatestSlot();
            for (int s = 1; s <= SlotCount; s++) slots[s - 1] = BuildSlotInfo(s, latest);
            LastError = null;
            return slots;
        }

        /// <summary>
        /// '불러오기' 화면의 슬롯 정보: 맨 앞이 자동 저장 칸(isAutoSave, 비어 있어도 칸으로), 그 뒤 1~5번 — 6칸
        /// </summary>
        public static SaveSlotInfo[] GetLoadSlots()
        {
            var slots = new SaveSlotInfo[SlotCount + 1];
            int latest = GetLatestSlot();
            slots[0] = BuildSlotInfo(AutoSaveSlot, latest);
            for (int s = 1; s <= SlotCount; s++) slots[s] = BuildSlotInfo(s, latest);
            LastError = null;
            return slots;
        }

        /// <summary>자동 저장 칸 정보 (불러오기 화면에서 따로 그릴 때)</summary>
        public static SaveSlotInfo GetAutoSaveSlotInfo()
        {
            var info = BuildSlotInfo(AutoSaveSlot, GetLatestSlot());
            LastError = null;
            return info;
        }

        private static SaveSlotInfo BuildSlotInfo(int s, int latest)
        {
            var info = new SaveSlotInfo { slot = s, isAutoSave = s == AutoSaveSlot, isCurrent = s == CurrentSlot, isLatest = s == latest };
            if (!File.Exists(GetSavePath(s))) info.isEmpty = true;
            else
            {
                var data = LoadData(s, false);
                if (data == null) { info.isBroken = true; info.error = LastError; }
                else info.summary = ToSummary(s, data);
            }
            return info;
        }

        /// <summary>가장 최근에 저장한 슬롯 (파일 수정 시각 기준, 자동 저장이면 AutoSaveSlot, 저장이 없으면 0)</summary>
        public static int GetLatestSlot()
        {
            int latest = 0;
            DateTime latestTime = DateTime.MinValue;
            foreach (int s in AllSlots())
            {
                string path = GetSavePath(s);
                if (!File.Exists(path)) continue;
                var time = File.GetLastWriteTimeUtc(path);
                if (latest == 0 || time > latestTime) { latest = s; latestTime = time; }
            }
            return latest;
        }

        private static SaveSummary ToSummary(int slot, SaveData d)
        {
            if (d == null) return null;
            return new SaveSummary
            {
                slot = slot,
                savedAt = d.savedAt,
                sceneName = d.sceneName,
                playerName = string.IsNullOrEmpty(d.playerName) ? PlayerProfile.DefaultName : d.playerName,
                year = d.time.year, month = d.time.month, week = d.time.week,
                money = d.economy.money,
                population = d.population.currentPopulation,
                buildingCount = d.buildings.Count,
            };
        }

        /// <summary>그 슬롯의 저장 지우기 (실수 방지로 슬롯 번호 1~5 또는 AutoSaveSlot을 꼭 줘야 함). 지웠으면 true</summary>
        public static bool DeleteSave(int slot)
        {
            LastError = null;
            if (!CheckSlot(slot, allowAutoSave: true)) return false;
            string path = GetSavePath(slot);
            if (!File.Exists(path)) { LastError = $"{SlotLabel(slot)}은 비어 있어요."; return false; }
            File.Delete(path);
            return true;
        }

        /// <summary>
        /// 불러오기: 고른 슬롯의 저장 파일을 읽어 저장한 씬을 열고, 씬이 뜨면 저장 시점으로 되돌린다.
        /// 성공하면 그 슬롯이 현재 슬롯이 된다(이후 '저장'은 기본으로 이 슬롯 — 자동 저장 슬롯을 불러오면 현재 슬롯은 그대로). 실패하면 false (LastError)
        /// </summary>
        public static bool ContinueGame(int slot = CurrentSlotArg)
        {
            slot = Resolve(slot);
            var data = LoadData(slot);
            return OpenSnapshot(data, slot);
        }

        /// <summary>
        /// 타이틀 '이어하기': resume.json(종료 직전)과 자동 저장(1주마다) 중 더 최근 것을 열고, 저장 당시 슬롯을 현재 슬롯으로 되돌린다.
        /// 실패하면 false (LastError)
        /// </summary>
        public static bool ContinueResume()
        {
            var data = LoadResumeData(out _);
            if (data == null) return false;
            return OpenSnapshot(data, IsValidSlot(data.resumeSlot) ? data.resumeSlot : 1);
        }

        private static bool OpenSnapshot(SaveData data, int slot)
        {
            if (data == null) return false;
            if (string.IsNullOrEmpty(data.sceneName) || !Application.CanStreamedLevelBeLoaded(data.sceneName))
            {
                LastError = $"저장한 씬 '{data.sceneName}'이 Build Settings에 없어 열 수 없어요.";
                Debug.LogError($"[SaveSystem] {LastError}");
                return false;
            }

            PrepareLoad(data);
            if (IsValidSlot(slot)) SetCurrentSlot(slot);
            Time.timeScale = 1f;
            SceneManager.LoadScene(data.sceneName);
            return true;
        }

        /// <summary>타이틀 '이어하기': 가장 최근에 저장한 슬롯(자동 저장 포함)을 불러옴 (저장이 없으면 false)</summary>
        public static bool ContinueLatest()
        {
            int latest = GetLatestSlot();
            if (latest == 0) { LastError = "저장된 게임이 없어요."; return false; }
            return ContinueGame(latest);
        }

        /// <summary>다음에 열리는 씬(data.sceneName)이 뜨면 이 데이터로 되돌리도록 예약 (씬은 직접 연다 — 에디터 테스트용)</summary>
        public static void PrepareLoad(SaveData data) => _pending = data;

        /// <summary>예약된 불러오기 취소</summary>
        public static void ClearPendingLoad() => _pending = null;

        /// <summary>
        /// 타이틀 '새로 시작' (사용자 기획 2026-11-25): 이전 게임에서 이어지는 상태를 지우고 게임 씬을 처음부터 연다.
        /// 지우는 것: 예약된 불러오기, 현재 슬롯(→ 1번), 주인공 이름(게임 씬이 뜨면 DialogueTriggers가 이름 입력부터 다시 — 이름은 타이틀이 아니라 게임을 시작할 때 정함), 멈춘 시간 배율.
        /// 저장 파일(1~5번·자동 저장)은 지우지 않는다 — 새로 시작한 뒤에도 불러오기로 다른 저장을 가져올 수 있다 (쯔꾸르식).
        /// 씬이 Build Settings에 없거나 이름이 잘못되면 false (LastError) — 그때는 아무것도 바꾸지 않는다
        /// </summary>
        public static bool StartNewGame(string gameScene)
        {
            LastError = null;
            if (string.IsNullOrEmpty(gameScene) || !Application.CanStreamedLevelBeLoaded(gameScene))
            {
                LastError = $"게임 씬 '{gameScene}'이 Build Settings에 없어 열 수 없어요.";
                Debug.LogWarning($"[SaveSystem] {LastError}");
                return false;
            }

            ResetForNewGame();
            Time.timeScale = 1f;
            SceneManager.LoadScene(gameScene);
            return true;
        }

        /// <summary>새 게임 준비: 예약된 불러오기·현재 슬롯(1번)·주인공 이름을 초기화 (씬은 열지 않음, 저장 파일은 그대로 — 테스트·UI용)</summary>
        public static void ResetForNewGame()
        {
            _pending = null;
            SetCurrentSlot(1);
            PlayerProfile.ClearPlayerName();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _pending = null;
            IsApplying = false;
            CurrentSlot = 1;
            _resumeSavedForQuit = false;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            Application.quitting -= HandleApplicationQuit;
            Application.quitting += HandleApplicationQuit;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_pending == null || mode != LoadSceneMode.Single) return;
            if (scene.name != _pending.sceneName)
            {
                Debug.LogWarning($"[SaveSystem] 저장한 씬({_pending.sceneName})이 아닌 '{scene.name}'이 열려 불러오기를 취소했어요.");
                _pending = null;
                return;
            }
            // 모든 매니저의 Start가 끝난 뒤, 다른 Update보다 먼저 적용 (SaveGameApplier)
            new GameObject("[SaveGameApplier]").AddComponent<SaveGameApplier>();
        }

        /// <summary>예약된 데이터를 지금 씬에 적용 (SaveGameApplier가 부름)</summary>
        internal static void ApplyPending()
        {
            var data = _pending;
            _pending = null;
            if (data != null) ApplySaveData(data);
        }

        /// <summary>
        /// 저장 데이터를 지금 씬에 적용. 순서: 이름 → 시간 → 건물 → 인구 → 전력 → 돈 → 만족도 → 재계산 → 엔딩 → 대화·튜토리얼 → 화면 갱신 알림
        /// </summary>
        public static void ApplySaveData(SaveData data)
        {
            if (data == null) return;
            IsApplying = true;
            try
            {
                if (!string.IsNullOrEmpty(data.playerName)) PlayerProfile.TrySetPlayerName(data.playerName, out _);
                else PlayerProfile.ClearPlayerName();

                var time = TimeManager.Instance;
                time?.ApplySaveState(data.time);

                RestoreBuildings(data.buildings);

                PopulationManager.Instance?.ApplySaveState(data.population);
                PowerManager.Instance?.ApplySaveState(data.power);
                InfraTown.Gas.GasManager.Instance?.ApplySaveState(data.gas);
                EconomyManager.Instance?.ApplySaveState(data.economy);
                SatisfactionManager.Instance?.ApplySaveState(data.satisfaction);
                GameLoopManager.Instance?.RecalculateAll();
                GameOutcomeManager.Instance?.ApplySaveState(data.outcome);

                var dialogue = DialogueEventManager.Instance;
                if (dialogue != null)
                {
                    dialogue.ClearAll();
                    dialogue.ResetPlayed();
                    foreach (var id in data.dialogue.playedDialogueIds) dialogue.MarkPlayed(id);
                    dialogue.UseEasyText = data.dialogue.useEasyText;
                    dialogue.SetAutoMode(data.dialogue.autoMode);
                }
                var cutscenes = CutsceneManager.Instance;
                if (cutscenes != null)
                {
                    cutscenes.ClearAll();
                    cutscenes.ResetPlayed();
                    foreach (var id in data.dialogue.playedCutsceneIds) cutscenes.MarkPlayed(id);
                }

                UnityEngine.Object.FindObjectOfType<DialogueTriggers>()?.ApplySaveState(data.dialogueTriggers);
                TutorialDialogues.Instance?.ApplySaveState(data.tutorial);

                // 마지막에 날짜·배속 알림 (날짜를 세는 매니저는 위에서 이미 이 날짜로 맞춰 둬서 하루로 세지 않음)
                time?.NotifyStateRestored();
            }
            finally
            {
                IsApplying = false;
            }

            Debug.Log($"[SaveSystem] 불러오기 완료: {data.time.year}년 {data.time.month}월 {data.time.week}주차, 건물 {data.buildings.Count}개");
            OnGameLoaded?.Invoke(data);
        }

        private static void RestoreBuildings(List<BuildingSaveState> buildings)
        {
            var manager = BuildingManager.Instance;
            var loop = GameLoopManager.Instance;
            if (manager == null) return;

            // 씬에 이미 있던 건물은 지우고 저장된 건물로 바꿈
            if (loop != null && Application.isPlaying)
            {
                foreach (var b in new List<BuildingInstance>(loop.PlacedBuildings))
                {
                    if (b != null) b.DestroyBuilding();
                }
            }

            int missing = 0;
            // 도로를 먼저 되살림: 도로 위 전신주가 도로보다 먼저 복원되면 맨땅 전신주로 자리를 차지해 도로가 겹치게 된다 (사용자 기획 2026-11-24)
            var entries = new List<(BuildingSaveState state, BuildingData data)>();
            foreach (var s in buildings) entries.Add((s, manager.FindBuildingData(s.buildingID, s.buildingName)));

            for (int pass = 0; pass < 2; pass++) // 0: 도로, 1: 나머지 (각각 저장된 순서대로)
            {
                foreach (var (s, data) in entries)
                {
                    bool isRoad = data != null && data.IsRoad;
                    if ((pass == 0) != isRoad) continue;
                    if (data == null || manager.RestoreBuilding(data, new Vector2Int(s.x, s.y), s.rotationY) == null) missing++;
                }
            }
            if (missing > 0) Debug.LogWarning($"[SaveSystem] 건물 {missing}개를 되살리지 못했어요 (건물 목록에 없거나 자리가 겹침).");
        }
    }
}

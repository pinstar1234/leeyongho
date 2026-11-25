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
    ///   타이틀 '이어하기' 버튼 표시 여부  → SaveSystem.HasAnySave()
    ///   타이틀 '불러오기' 화면에서 슬롯 선택 → SaveSystem.ContinueGame(slot)  (저장한 씬을 열고 그 시점으로 되돌림)
    ///   (가장 최근 저장 바로 이어하기)    → SaveSystem.ContinueLatest()
    ///   슬롯 지우기                       → SaveSystem.DeleteSave(slot)
    ///   타이틀 '새 게임'                  → SaveSystem.ClearPendingLoad() 후 게임 씬 열기 (저장 파일은 지우지 않음)
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

        public static bool IsValidSlot(int slot) => slot >= 1 && slot <= SlotCount;

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

        /// <summary>슬롯 번호가 맞는지 확인 (틀리면 LastError)</summary>
        private static bool CheckSlot(int slot)
        {
            if (IsValidSlot(slot)) return true;
            LastError = $"슬롯은 1~{SlotCount}번만 있어요 (받은 값: {slot}).";
            return false;
        }

        public static string GetSavePath(int slot = CurrentSlotArg) => Path.Combine(SaveFolder, $"save_{Resolve(slot)}.json");

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

        // ───────────────── 저장 ─────────────────

        /// <summary>그 슬롯에 저장 파일이 있는지 (생략하면 현재 슬롯)</summary>
        public static bool HasSave(int slot = CurrentSlotArg)
        {
            slot = Resolve(slot);
            return IsValidSlot(slot) && File.Exists(GetSavePath(slot));
        }

        /// <summary>슬롯 중 하나라도 저장이 있는지 (타이틀 '이어하기'·'불러오기' 버튼 표시)</summary>
        public static bool HasAnySave()
        {
            for (int s = 1; s <= SlotCount; s++) if (HasSave(s)) return true;
            return false;
        }

        /// <summary>현재 게임을 그 슬롯에 저장 (생략하면 현재 슬롯). 성공하면 그 슬롯이 현재 슬롯이 된다. 실패하면 false (LastError)</summary>
        public static bool SaveGame(int slot = CurrentSlotArg)
        {
            LastError = null;
            slot = Resolve(slot);
            if (!CheckSlot(slot)) { Debug.LogWarning($"[SaveSystem] {LastError}"); return false; }
            try
            {
                var data = CaptureCurrentGame();
                if (data == null) return false;

                Directory.CreateDirectory(SaveFolder);
                string path = GetSavePath(slot);
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(data, true));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);

                Debug.Log($"[SaveSystem] 저장 완료 (슬롯 {slot}): {path}");
                SetCurrentSlot(slot);
                OnGameSaved?.Invoke(slot);
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

        private static SaveData LoadData(int slot, bool logErrors)
        {
            LastError = null;
            slot = Resolve(slot);
            if (!CheckSlot(slot)) return null;
            string path = GetSavePath(slot);
            if (!File.Exists(path)) { LastError = $"{slot}번 슬롯에 저장된 게임이 없어요."; return null; }
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
        /// 슬롯 5칸의 정보 (저장·불러오기 화면). 빈 슬롯·깨진 파일도 칸으로 돌려주고, 깨진 파일은 콘솔 에러 없이 isBroken·error로 알려준다.
        /// </summary>
        public static SaveSlotInfo[] GetAllSlots()
        {
            var slots = new SaveSlotInfo[SlotCount];
            int latest = GetLatestSlot();
            for (int s = 1; s <= SlotCount; s++)
            {
                var info = new SaveSlotInfo { slot = s, isCurrent = s == CurrentSlot, isLatest = s == latest };
                if (!File.Exists(GetSavePath(s))) info.isEmpty = true;
                else
                {
                    var data = LoadData(s, false);
                    if (data == null) { info.isBroken = true; info.error = LastError; }
                    else info.summary = ToSummary(s, data);
                }
                slots[s - 1] = info;
            }
            LastError = null;
            return slots;
        }

        /// <summary>가장 최근에 저장한 슬롯 (파일 수정 시각 기준, 저장이 없으면 0)</summary>
        public static int GetLatestSlot()
        {
            int latest = 0;
            DateTime latestTime = DateTime.MinValue;
            for (int s = 1; s <= SlotCount; s++)
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

        /// <summary>그 슬롯의 저장 지우기 (실수 방지로 슬롯 번호 1~5를 꼭 줘야 함). 지웠으면 true</summary>
        public static bool DeleteSave(int slot)
        {
            LastError = null;
            if (!CheckSlot(slot)) return false;
            string path = GetSavePath(slot);
            if (!File.Exists(path)) { LastError = $"{slot}번 슬롯은 비어 있어요."; return false; }
            File.Delete(path);
            return true;
        }

        /// <summary>
        /// 불러오기: 고른 슬롯의 저장 파일을 읽어 저장한 씬을 열고, 씬이 뜨면 저장 시점으로 되돌린다.
        /// 성공하면 그 슬롯이 현재 슬롯이 된다(이후 '저장'은 기본으로 이 슬롯). 실패하면 false (LastError)
        /// </summary>
        public static bool ContinueGame(int slot = CurrentSlotArg)
        {
            slot = Resolve(slot);
            var data = LoadData(slot);
            if (data == null) return false;
            if (string.IsNullOrEmpty(data.sceneName) || !Application.CanStreamedLevelBeLoaded(data.sceneName))
            {
                LastError = $"저장한 씬 '{data.sceneName}'이 Build Settings에 없어 열 수 없어요.";
                Debug.LogError($"[SaveSystem] {LastError}");
                return false;
            }

            PrepareLoad(data);
            SetCurrentSlot(slot);
            Time.timeScale = 1f;
            SceneManager.LoadScene(data.sceneName);
            return true;
        }

        /// <summary>타이틀 '이어하기': 가장 최근에 저장한 슬롯을 불러옴 (저장이 없으면 false)</summary>
        public static bool ContinueLatest()
        {
            int latest = GetLatestSlot();
            if (latest == 0) { LastError = "저장된 게임이 없어요."; return false; }
            return ContinueGame(latest);
        }

        /// <summary>다음에 열리는 씬(data.sceneName)이 뜨면 이 데이터로 되돌리도록 예약 (씬은 직접 연다 — 에디터 테스트용)</summary>
        public static void PrepareLoad(SaveData data) => _pending = data;

        /// <summary>예약된 불러오기 취소 (타이틀 '새 게임')</summary>
        public static void ClearPendingLoad() => _pending = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _pending = null;
            IsApplying = false;
            CurrentSlot = 1;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
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

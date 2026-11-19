using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Population;
using InfraTown.Satisfaction;
using InfraTown.Services;
using InfraTown.TimeSystem;

namespace InfraTown.EventSystem
{
    /// <summary>
    /// 필수 시설 1종에 대한 대화 설정. 에셋을 비워 두면 기본 문장을 쓴다
    /// </summary>
    [Serializable]
    public class EssentialFacilityDialogue
    {
        public ServiceType service;

        [Tooltip("해금 후 오랫동안 설치하지 않았을 때 (비우면 기본 문장)")]
        public DialogueData missingDialogue;

        [Tooltip("처음 설치했을 때 (비우면 기본 문장)")]
        public DialogueData installedDialogue;

        public EssentialFacilityDialogue() { }
        public EssentialFacilityDialogue(ServiceType service) { this.service = service; }
    }

    /// <summary>
    /// 게임 상황에 맞춰 DialogueEventManager로 대화를 띄우는 트리거 모음. DialogueEventManager와 같은 오브젝트에 붙인다.
    ///  1) 게임 시작 스토리: introDialogue + 처음부터 해금된 건물의 해금 메시지
    ///  2) 필수 시설 미설치 불만: 필수 시설이 해금된 뒤 설치하지 않은 날이 30~90일(무작위)이 되면 주민이 불만을 말함
    ///  3) 필수 시설 설치: 종류별로 처음 설치했을 때 대사
    ///  그 밖에 인구 증가로 새 건물 해금 스토리(기획서 4장), '나쁨' 지속 경고, 엔딩 대화
    /// </summary>
    [RequireComponent(typeof(DialogueEventManager))]
    public class DialogueTriggers : MonoBehaviour
    {
        [Header("1. 게임 시작")]
        [Tooltip("게임 시작 시 재생할 기본 스토리 (비워도 됨)")]
        [SerializeField] private DialogueData introDialogue;

        [Tooltip("시작할 때부터 해금된 건물(Tier 1)의 해금 메시지도 시작 직후 보여줌")]
        [SerializeField] private bool showInitialUnlockStories = true;

        [Header("2·3. 필수 시설 (미설치 불만 / 설치)")]
        [SerializeField] private List<EssentialFacilityDialogue> essentialDialogues = new List<EssentialFacilityDialogue>
        {
            new EssentialFacilityDialogue(ServiceType.Hospital),
            new EssentialFacilityDialogue(ServiceType.School),
            new EssentialFacilityDialogue(ServiceType.FireStation),
            new EssentialFacilityDialogue(ServiceType.Police),
        };

        [Tooltip("불만이 나오기까지 최소 일수 (1달 = 30일)")]
        [SerializeField] private int complaintMinDays = 30;

        [Tooltip("불만이 나오기까지 최대 일수 (3달 = 90일). 시설마다 이 범위에서 무작위로 정해짐")]
        [SerializeField] private int complaintMaxDays = 90;

        [Tooltip("불만이 나온 뒤에도 계속 안 지으면 새 무작위 기간 뒤에 다시 불만")]
        [SerializeField] private bool repeatComplaint = true;

        [Tooltip("마을 만족도가 '좋음'이 아닌 날만 센다 (끄면 설치 안 된 날을 모두 셈)")]
        [SerializeField] private bool countOnlyWhenNotGood = false;

        [Tooltip("무작위 기간의 시드 (0 = 매번 다르게)")]
        [SerializeField] private int randomSeed = 0;

        [SerializeField] private string residentSpeaker = "주민";
        [SerializeField] private Sprite residentPortrait;

        [Header("건물 해금 스토리")]
        [SerializeField] private string unlockSpeaker = "이장님";
        [SerializeField] private Sprite unlockSpeakerPortrait;

        [Header("'나쁨' 지속 경고")]
        [SerializeField] private string warningSpeaker = "안내원";
        [SerializeField] private Sprite warningSpeakerPortrait;

        [Tooltip("'나쁨'이 이 일수째가 되면 경고 (패배 기준은 GameOutcomeManager, 기본 30일)")]
        [SerializeField] private List<int> badStreakWarningDays = new List<int> { 10, 20, 25 };

        [Header("엔딩")]
        [SerializeField] private DialogueData victoryDialogue;
        [SerializeField] private DialogueData defeatDialogue;

        /// <summary>필수 시설 미설치 불만이 나왔을 때 (시설 종류)</summary>
        public event Action<ServiceType> OnFacilityComplaint;

        /// <summary>필수 시설을 처음 설치해 대사가 나왔을 때 (시설 종류)</summary>
        public event Action<ServiceType> OnFacilityFirstInstalled;

        private class EssentialState
        {
            public int missingDays;
            public int threshold = -1; // -1 = 아직 정하지 않음
            public bool firstInstallPlayed;
        }

        private readonly Dictionary<ServiceType, EssentialState> _essential = new Dictionary<ServiceType, EssentialState>();
        private readonly HashSet<BuildingData> _knownUnlocked = new HashSet<BuildingData>();
        private bool _initialized;
        private GameOutcomeManager _outcome;
        private PopulationManager _population;
        private TimeManager _time;
        private BuildingManager _buildings;
        private (int y, int m, int d) _lastDate = (-1, -1, -1);
        private System.Random _rng;

        private DialogueEventManager Dialogue => DialogueEventManager.Instance;

        public int ComplaintMinDays => complaintMinDays;
        public int ComplaintMaxDays => complaintMaxDays;

        /// <summary>해당 필수 시설이 해금된 뒤 설치 안 된 채 지난 일수 (설치돼 있거나 잠겨 있으면 0)</summary>
        public int GetMissingDays(ServiceType type) => _essential.TryGetValue(type, out var s) ? s.missingDays : 0;

        /// <summary>이번에 불만이 나올 일수 (아직 세기 시작 전이면 -1)</summary>
        public int GetComplaintThreshold(ServiceType type) => _essential.TryGetValue(type, out var s) ? s.threshold : -1;

        private void Awake()
        {
            foreach (var e in essentialDialogues) _essential[e.service] = new EssentialState();
        }

        private void Start()
        {
            TrySubscribe();
        }

        private void Update()
        {
            // GameOutcomeManager 등은 다른 매니저의 Start에서 늦게 붙을 수 있어서 나타날 때까지 구독 시도
            if (_outcome == null || _population == null || _time == null || _buildings == null) TrySubscribe();
        }

        private void OnDestroy()
        {
            if (_population != null) _population.OnPopulationChanged -= HandlePopulationChanged;
            if (_outcome != null)
            {
                _outcome.OnBadStreakChanged -= HandleBadStreakChanged;
                _outcome.OnGameEnded -= HandleGameEnded;
            }
            if (_time != null) _time.OnDateChanged -= HandleDateChanged;
            if (_buildings != null) _buildings.OnBuildingPlaced -= HandleBuildingPlaced;
        }

        private void TrySubscribe()
        {
            if (!_initialized && BuildingManager.Instance != null)
            {
                _initialized = true;
                if (introDialogue != null) Dialogue?.Play(introDialogue);

                var unlockedNow = BuildingManager.Instance.GetUnlockedBuildings();
                if (showInitialUnlockStories) PlayUnlockStories(unlockedNow, "unlock_initial");
                foreach (var d in unlockedNow) _knownUnlocked.Add(d);
            }

            if (_buildings == null && BuildingManager.Instance != null)
            {
                _buildings = BuildingManager.Instance;
                _buildings.OnBuildingPlaced += HandleBuildingPlaced;
            }

            if (_population == null && PopulationManager.Instance != null)
            {
                _population = PopulationManager.Instance;
                _population.OnPopulationChanged += HandlePopulationChanged;
            }

            if (_time == null && TimeManager.Instance != null)
            {
                _time = TimeManager.Instance;
                _lastDate = (_time.CurrentYear, _time.CurrentMonth, _time.CurrentDay);
                _time.OnDateChanged += HandleDateChanged;
            }

            if (_outcome == null && GameOutcomeManager.Instance != null)
            {
                _outcome = GameOutcomeManager.Instance;
                _outcome.OnBadStreakChanged += HandleBadStreakChanged;
                _outcome.OnGameEnded += HandleGameEnded;
            }
        }

        // ───────────────── 2) 필수 시설 미설치 불만 ─────────────────

        private void HandleDateChanged(int year, int month, int day)
        {
            if (_lastDate == (year, month, day)) return; // TimeManager.Start의 첫 알림 등 같은 날짜는 세지 않음
            _lastDate = (year, month, day);
            ProcessDay();
        }

        /// <summary>
        /// 하루 경과 처리: 해금됐는데 설치 안 된 필수 시설의 일수를 세고, 무작위로 정한 기간(30~90일)에 닿으면 불만 대사
        /// </summary>
        public void ProcessDay()
        {
            var service = ServiceAccessibilityManager.Instance;
            bool townIsGood = SatisfactionManager.Instance != null && SatisfactionManager.Instance.CurrentState == SatisfactionState.Good;

            foreach (var entry in essentialDialogues)
            {
                var state = GetState(entry.service);
                bool installed = service != null && service.HasService(entry.service);

                if (installed || !IsServiceUnlocked(entry.service))
                {
                    // 지어졌거나 아직 지을 수 없으면 처음부터 다시 셈
                    state.missingDays = 0;
                    state.threshold = -1;
                    continue;
                }

                if (countOnlyWhenNotGood && townIsGood) continue;

                if (state.threshold < 0) state.threshold = RollComplaintDays();
                state.missingDays++;

                if (state.missingDays >= state.threshold)
                {
                    PlayComplaint(entry);
                    state.missingDays = 0;
                    state.threshold = repeatComplaint ? RollComplaintDays() : int.MaxValue;
                }
            }
        }

        /// <summary>complaintMinDays ~ complaintMaxDays 사이 무작위 일수</summary>
        public int RollComplaintDays()
        {
            if (_rng == null) _rng = randomSeed == 0 ? new System.Random() : new System.Random(randomSeed);
            int min = Mathf.Max(1, Mathf.Min(complaintMinDays, complaintMaxDays));
            int max = Mathf.Max(min, complaintMaxDays);
            return _rng.Next(min, max + 1);
        }

        private bool IsServiceUnlocked(ServiceType type)
        {
            var buildings = BuildingManager.Instance;
            return buildings != null && buildings.GetUnlockedBuildings()
                .Any(d => ServiceAccessibilityManager.GetServiceType(d) == type);
        }

        private void PlayComplaint(EssentialFacilityDialogue entry)
        {
            OnFacilityComplaint?.Invoke(entry.service);
            if (Dialogue == null) return;

            if (entry.missingDialogue != null)
            {
                Dialogue.Play(entry.missingDialogue);
                return;
            }

            var (text, easy) = DefaultComplaint(entry.service);
            Dialogue.Play(DialogueData.Create($"missing_{entry.service}_{_lastDate.y}_{_lastDate.m}_{_lastDate.d}", true, false,
                new DialogueLine(residentSpeaker, text, easy, residentPortrait)));
        }

        // ───────────────── 3) 필수 시설 설치 ─────────────────

        private void HandleBuildingPlaced(BuildingInstance building)
        {
            if (building == null || building.Data == null) return;
            var type = ServiceAccessibilityManager.GetServiceType(building.Data);
            if (!type.HasValue) return;

            var entry = essentialDialogues.FirstOrDefault(e => e.service == type.Value);
            if (entry == null) return;

            var state = GetState(entry.service);
            state.missingDays = 0;
            state.threshold = -1;
            if (state.firstInstallPlayed) return; // 같은 종류 두 번째 설치부터는 대사 없음
            state.firstInstallPlayed = true;

            OnFacilityFirstInstalled?.Invoke(entry.service);
            if (Dialogue == null) return;

            if (entry.installedDialogue != null)
            {
                Dialogue.Play(entry.installedDialogue);
                return;
            }

            var (text, easy) = DefaultInstalled(entry.service);
            Dialogue.Play(DialogueData.Create($"installed_{entry.service}", true, true,
                new DialogueLine(unlockSpeaker, text, easy, unlockSpeakerPortrait)));
        }

        private EssentialState GetState(ServiceType type)
        {
            if (!_essential.TryGetValue(type, out var state)) _essential[type] = state = new EssentialState();
            return state;
        }

        // 에셋을 넣지 않았을 때 쓰는 기본 문장 (임시 문구 — 기획에서 DialogueData 에셋으로 바꾸면 됨)
        private static (string text, string easy) DefaultComplaint(ServiceType type) => type switch
        {
            ServiceType.Hospital => ("아이가 열이 펄펄 나는데 병원이 없어서 읍내까지 한 시간을 달려갔어요. 병원 좀 지어 주세요!",
                                     "아플 때 갈 병원이 없어요! 병원을 지어 주세요."),
            ServiceType.School => ("아이들이 버스를 두 번이나 갈아타고 학교에 가요. 이러다 젊은 가족들이 다 떠나겠어요.",
                                   "학교가 너무 멀어요! 학교를 지어 주세요."),
            ServiceType.FireStation => ("어젯밤 옆집 창고에 불이 났는데 소방차가 한참 뒤에야 왔어요. 소방서가 꼭 필요해요.",
                                        "불이 나면 무서워요! 소방서를 지어 주세요."),
            ServiceType.Police => ("밤마다 농산물 도둑이 들어서 잠을 못 자요. 경찰서가 있으면 좋겠어요.",
                                   "도둑이 무서워요! 경찰서를 지어 주세요."),
            _ => ($"{type} 시설이 없어서 불편해요.", null)
        };

        private static (string text, string easy) DefaultInstalled(ServiceType type) => type switch
        {
            ServiceType.Hospital => ("병원이 생겼어요! 이제 아플 때 바로 치료받을 수 있겠네요.", "병원이 생겼어요! 이제 아파도 걱정 없어요."),
            ServiceType.School => ("학교가 문을 열었어요! 아이들 웃음소리가 마을에 가득하네요.", "학교가 생겼어요! 친구들이랑 공부해요."),
            ServiceType.FireStation => ("소방서가 생겼으니 이제 불이 나도 금방 끌 수 있어요.", "소방서가 생겼어요! 불이 나도 괜찮아요."),
            ServiceType.Police => ("경찰서가 생겼어요. 이제 밤에도 마음 놓고 잘 수 있겠어요.", "경찰서가 생겼어요! 이제 안전해요."),
            _ => ($"{type} 시설이 생겼어요!", null)
        };

        // ───────────────── 해금 스토리 / 경고 / 엔딩 ─────────────────

        /// <summary>인구 변화로 새로 해금된 건물이 있으면 해금 스토리 재생</summary>
        private void HandlePopulationChanged(int current, int max)
        {
            if (!_initialized || BuildingManager.Instance == null) return;

            var newlyUnlocked = BuildingManager.Instance.GetUnlockedBuildings()
                .Where(d => !_knownUnlocked.Contains(d)).ToList();
            if (newlyUnlocked.Count == 0) return;

            foreach (var d in newlyUnlocked) _knownUnlocked.Add(d);
            int tier = newlyUnlocked.Max(d => d.unlockTier);
            PlayUnlockStories(newlyUnlocked, $"unlock_tier{tier}_{string.Join("_", newlyUnlocked.Select(d => d.buildingID))}");
        }

        /// <summary>해금된 건물들의 메시지를 한 대화로 묶어 재생 + 마지막에 해금 안내</summary>
        private void PlayUnlockStories(List<BuildingData> buildings, string dialogueId)
        {
            if (Dialogue == null || buildings.Count == 0) return;

            var lines = new List<DialogueLine>();
            foreach (var d in buildings.Where(b => !string.IsNullOrEmpty(b.unlockMessage)))
            {
                // 같은 메시지를 쓰는 건물(도로·전신주)은 한 번만
                if (lines.Any(l => l.text == d.unlockMessage)) continue;
                lines.Add(new DialogueLine(unlockSpeaker, d.unlockMessage, null, unlockSpeakerPortrait));
            }

            string names = string.Join(", ", buildings.Select(b => b.buildingName));
            lines.Add(new DialogueLine(unlockSpeaker,
                $"이제 {names}을(를) 지을 수 있어요!",
                $"새 건물이 생겼어요! {names}",
                unlockSpeakerPortrait));

            Dialogue.Play(DialogueData.Create(dialogueId, true, true, lines.ToArray()));
        }

        private void HandleBadStreakChanged(int days, int limit)
        {
            if (Dialogue == null || !badStreakWarningDays.Contains(days)) return;

            int left = Mathf.Max(0, limit - days);
            var line = new DialogueLine(warningSpeaker,
                $"주민 불만이 {days}일째 계속되고 있어요. {left}일 안에 나아지지 않으면 마을이 무너집니다.",
                $"주민들이 {days}일째 화가 나 있어요! {left}일 안에 마을을 고쳐 주세요.",
                warningSpeakerPortrait);
            Dialogue.Play(DialogueData.Create($"warning_bad_{days}_{Time.frameCount}", true, false, line));
        }

        private void HandleGameEnded(GameResult result)
        {
            var dialogue = result == GameResult.Victory ? victoryDialogue : defeatDialogue;
            if (dialogue != null) Dialogue?.Play(dialogue);
        }
    }
}

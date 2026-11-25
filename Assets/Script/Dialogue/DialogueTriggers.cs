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

        [Tooltip("미설치 불만 대화 전에 보여줄 카툰 컷씬 (비우면 대화 에셋의 cutsceneBefore, 그것도 없으면 바로 대화)")]
        public CutsceneData missingCutscene;

        [Tooltip("설치 대화 전에 보여줄 카툰 컷씬 (비우면 대화 에셋의 cutsceneBefore, 그것도 없으면 바로 대화)")]
        public CutsceneData installedCutscene;

        public EssentialFacilityDialogue() { }
        public EssentialFacilityDialogue(ServiceType service) { this.service = service; }
    }

    /// <summary>
    /// 게임 상황에 맞춰 DialogueEventManager로 대화를 띄우는 트리거 모음. DialogueEventManager와 같은 오브젝트에 붙인다.
    ///  1) 게임 시작 스토리: introDialogue + 처음부터 해금된 건물의 해금 메시지
    ///  2) 필수 시설 미설치 불만: 필수 시설이 해금된 뒤 설치하지 않은 주가 4~12주(1~3달, 무작위)가 되면 주민이 불만을 말함
    ///  3) 필수 시설 설치: 종류별로 처음 설치했을 때 대사
    ///  그 밖에 인구 증가로 새 건물 해금 스토리(기획서 4장), '나쁨' 지속 경고, 엔딩 대화
    /// </summary>
    [RequireComponent(typeof(DialogueEventManager))]
    public class DialogueTriggers : MonoBehaviour
    {
        [Header("0. 주인공 이름")]
        [Tooltip("게임 시작 때 이름을 아직 정하지 않았으면 이름 입력을 요청하고, 정할 때까지 오프닝을 기다림 " +
                 "(OnPlayerNameRequested를 구독한 입력창이 없으면 기본 이름으로 바로 진행)")]
        [SerializeField] private bool askPlayerNameAtStart = true;

        [Header("1. 게임 시작 (오프닝 컷씬 → 마을 도착 후 주민 대화 → 튜토리얼)")]
        [Tooltip("처음에 보여줄 오프닝 컷씬 — 시네머신(Cinemachine) 방식 권장, 팀원이 만든 시퀀스가 마을 도착까지 보여줌 (비워도 됨)")]
        [SerializeField] private CutsceneData openingCutscene;

        [Tooltip("오프닝 컷씬이 끝나고 마을에 도착해서 나누는 주민 대화 (비워도 됨)")]
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

        [Tooltip("불만이 나오기까지 최소 주 수 (1달 = 4주)")]
        [SerializeField] private int complaintMinWeeks = 4;

        [Tooltip("불만이 나오기까지 최대 주 수 (3달 = 12주). 시설마다 이 범위에서 무작위로 정해짐")]
        [SerializeField] private int complaintMaxWeeks = 12;

        [Tooltip("불만이 나온 뒤에도 계속 안 지으면 새 무작위 기간 뒤에 다시 불만")]
        [SerializeField] private bool repeatComplaint = true;

        [Tooltip("마을 만족도가 '좋음'이 아닌 주만 센다 (끄면 설치 안 된 주를 모두 셈)")]
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

        [Tooltip("'나쁨'이 이 주째가 되면 경고 (패배 기준은 GameOutcomeManager, 기본 12주)")]
        [SerializeField] private List<int> badStreakWarningWeeks = new List<int> { 4, 8, 10 };

        [Header("엔딩")]
        [SerializeField] private DialogueData victoryDialogue;
        [SerializeField] private DialogueData defeatDialogue;

        /// <summary>
        /// 이름 입력창을 띄울 때 (게임 시작 첫 프레임). 입력창은 SubmitPlayerName(이름)을 부르고, true면 닫는다.
        /// 기다리는 동안 게임 시간은 멈춘다.
        /// </summary>
        public event Action OnPlayerNameRequested;

        /// <summary>이름 입력을 기다리는 중 (오프닝 전)</summary>
        public bool IsWaitingForPlayerName { get; private set; }

        /// <summary>오프닝·튜토리얼이 시작됐는지 (이름 입력 대기 중이면 false)</summary>
        public bool StoryStarted { get; private set; }

        /// <summary>필수 시설 미설치 불만이 나왔을 때 (시설 종류)</summary>
        public event Action<ServiceType> OnFacilityComplaint;

        /// <summary>필수 시설을 처음 설치해 대사가 나왔을 때 (시설 종류)</summary>
        public event Action<ServiceType> OnFacilityFirstInstalled;

        private class EssentialState
        {
            public int missingWeeks;
            public int threshold = -1; // -1 = 아직 정하지 않음
            public bool firstInstallPlayed;
        }

        private readonly Dictionary<ServiceType, EssentialState> _essential = new Dictionary<ServiceType, EssentialState>();
        private readonly HashSet<BuildingData> _knownUnlocked = new HashSet<BuildingData>();
        private bool _initialized;
        private bool _firstUpdateDone;
        private List<BuildingData> _initialUnlocked = new List<BuildingData>();
        private GameOutcomeManager _outcome;
        private PopulationManager _population;
        private TimeManager _time;
        private BuildingManager _buildings;
        private (int y, int m, int d) _lastDate = (-1, -1, -1);
        private System.Random _rng;

        private DialogueEventManager Dialogue => DialogueEventManager.Instance;

        public int ComplaintMinWeeks => complaintMinWeeks;
        public int ComplaintMaxWeeks => complaintMaxWeeks;

        /// <summary>해당 필수 시설이 해금된 뒤 설치 안 된 채 지난 주 수 (설치돼 있거나 잠겨 있으면 0)</summary>
        public int GetMissingWeeks(ServiceType type) => _essential.TryGetValue(type, out var s) ? s.missingWeeks : 0;

        /// <summary>이번에 불만이 나올 주 수 (아직 세기 시작 전이면 -1)</summary>
        public int GetComplaintThreshold(ServiceType type) => _essential.TryGetValue(type, out var s) ? s.threshold : -1;

        private void Awake()
        {
            foreach (var e in essentialDialogues) _essential[e.service] = new EssentialState();
        }

        private void Start()
        {
            // 컷씬 매니저가 씬에 없으면 이 오브젝트에 추가 (씬 수정 없이 동작하도록)
            if (CutsceneManager.Instance == null && Application.isPlaying)
            {
                gameObject.AddComponent<CutsceneManager>();
            }
            // 튜토리얼도 없으면 추가
            if (TutorialDialogues.Instance == null && Application.isPlaying)
            {
                gameObject.AddComponent<TutorialDialogues>();
            }
            TrySubscribe();
        }

        private void Update()
        {
            // 이야기 시작(이름 요청·오프닝)은 첫 Update에서 — 모든 UI가 Start에서 이벤트를 구독한 뒤라 이름 입력창이 요청을 받을 수 있음
            _firstUpdateDone = true;
            // GameOutcomeManager 등은 다른 매니저의 Start에서 늦게 붙을 수 있어서 나타날 때까지 구독 시도
            if (!_initialized || _outcome == null || _population == null || _time == null || _buildings == null) TrySubscribe();
        }

        /// <summary>
        /// 이름 입력창에서 부름. 이름이 올바르면 저장하고 오프닝을 시작한 뒤 true,
        /// 비었거나 너무 길면 false와 오류 문구(입력창에 보여주면 됨)
        /// </summary>
        public bool SubmitPlayerName(string name, out string error)
        {
            if (!PlayerProfile.TrySetPlayerName(name, out error)) return false;
            if (IsWaitingForPlayerName)
            {
                IsWaitingForPlayerName = false;
                TimeManager.Instance?.ReleaseEventPause(this);
                StartStory();
            }
            return true;
        }

        public bool SubmitPlayerName(string name) => SubmitPlayerName(name, out _);

        private void OnDestroy()
        {
            if (IsWaitingForPlayerName) TimeManager.Instance?.ReleaseEventPause(this);
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
            // 이어하기로 열린 씬이면 저장 데이터가 적용될 때까지 오프닝·이름 입력을 시작하지 않음
            if (!_initialized && _firstUpdateDone && BuildingManager.Instance != null && !InfraTown.Save.SaveSystem.HasPendingLoad)
            {
                _initialized = true;
                _initialUnlocked = BuildingManager.Instance.GetUnlockedBuildings();
                foreach (var d in _initialUnlocked) _knownUnlocked.Add(d);

                if (askPlayerNameAtStart && !PlayerProfile.HasName && OnPlayerNameRequested != null)
                {
                    // 이름을 정할 때까지 시간 정지 후 대기 → SubmitPlayerName에서 StartStory
                    IsWaitingForPlayerName = true;
                    TimeManager.Instance?.RequestEventPause(this);
                    OnPlayerNameRequested.Invoke();
                }
                else
                {
                    StartStory();
                }
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
                _lastDate = (_time.CurrentYear, _time.CurrentMonth, _time.CurrentWeek);
                _time.OnDateChanged += HandleDateChanged;
            }

            if (_outcome == null && GameOutcomeManager.Instance != null)
            {
                _outcome = GameOutcomeManager.Instance;
                _outcome.OnBadStreakChanged += HandleBadStreakChanged;
                _outcome.OnGameEnded += HandleGameEnded;
            }
        }

        // ───────────────── 1) 오프닝 ─────────────────

        /// <summary>오프닝 컷씬 → 마을 도착 대화 → 처음 해금 메시지 → 튜토리얼 1일차 (대화 대기열 순서대로)</summary>
        private void StartStory()
        {
            if (StoryStarted) return;
            StoryStarted = true;

            PlayOpening();
            if (showInitialUnlockStories) PlayUnlockStories(_initialUnlocked, "unlock_initial");

            var tutorial = TutorialDialogues.Instance != null ? TutorialDialogues.Instance : GetComponent<TutorialDialogues>();
            if (tutorial != null) tutorial.BeginTutorial();
        }

        /// <summary>오프닝 컷씬 → 마을 도착 대화. 대화가 없으면 컷씬만</summary>
        private void PlayOpening()
        {
            if (introDialogue != null && Dialogue != null)
            {
                Dialogue.Play(introDialogue, openingCutscene);
            }
            else if (openingCutscene != null)
            {
                CutsceneManager.Instance?.Play(openingCutscene);
            }
        }

        // ───────────────── 2) 필수 시설 미설치 불만 ─────────────────

        private void HandleDateChanged(int year, int month, int day)
        {
            if (_lastDate == (year, month, day)) return; // TimeManager.Start의 첫 알림 등 같은 날짜는 세지 않음 (한 주 단위)
            _lastDate = (year, month, day);
            ProcessWeek();
        }

        /// <summary>
        /// 한 주 경과 처리: 해금됐는데 설치 안 된 필수 시설의 주 수를 세고, 무작위로 정한 기간(4~12주)에 닿으면 불만 대사
        /// </summary>
        public void ProcessWeek()
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
                    state.missingWeeks = 0;
                    state.threshold = -1;
                    continue;
                }

                if (countOnlyWhenNotGood && townIsGood) continue;

                if (state.threshold < 0) state.threshold = RollComplaintWeeks();
                state.missingWeeks++;

                if (state.missingWeeks >= state.threshold)
                {
                    PlayComplaint(entry);
                    state.missingWeeks = 0;
                    state.threshold = repeatComplaint ? RollComplaintWeeks() : int.MaxValue;
                }
            }
        }

        /// <summary>complaintMinWeeks ~ complaintMaxWeeks 사이 무작위 주 수</summary>
        public int RollComplaintWeeks()
        {
            if (_rng == null) _rng = randomSeed == 0 ? new System.Random() : new System.Random(randomSeed);
            int min = Mathf.Max(1, Mathf.Min(complaintMinWeeks, complaintMaxWeeks));
            int max = Mathf.Max(min, complaintMaxWeeks);
            return _rng.Next(min, max + 1);
        }

        private static bool IsServiceUnlocked(ServiceType type) => ServiceAccessibilityManager.IsServiceUnlocked(type);

        private void PlayComplaint(EssentialFacilityDialogue entry)
        {
            OnFacilityComplaint?.Invoke(entry.service);
            if (Dialogue == null) return;

            if (entry.missingDialogue != null)
            {
                Dialogue.Play(entry.missingDialogue, entry.missingCutscene);
                return;
            }

            var (text, easy) = DefaultComplaint(entry.service);
            Dialogue.Play(DialogueData.Create($"missing_{entry.service}_{_lastDate.y}_{_lastDate.m}_{_lastDate.d}", true, false,
                new DialogueLine(residentSpeaker, text, easy, residentPortrait)), entry.missingCutscene);
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
            state.missingWeeks = 0;
            state.threshold = -1;
            if (state.firstInstallPlayed) return; // 같은 종류 두 번째 설치부터는 대사 없음
            state.firstInstallPlayed = true;

            OnFacilityFirstInstalled?.Invoke(entry.service);
            if (Dialogue == null) return;

            if (entry.installedDialogue != null)
            {
                Dialogue.Play(entry.installedDialogue, entry.installedCutscene);
                return;
            }

            var (text, easy) = DefaultInstalled(entry.service);
            Dialogue.Play(DialogueData.Create($"installed_{entry.service}", true, true,
                new DialogueLine(unlockSpeaker, text, easy, unlockSpeakerPortrait)), entry.installedCutscene);
        }

        // ───────────────── 세이브 ─────────────────

        public InfraTown.Save.DialogueTriggerSaveState GetSaveState()
        {
            var s = new InfraTown.Save.DialogueTriggerSaveState { storyStarted = StoryStarted };
            foreach (var d in _knownUnlocked) if (d != null) s.knownUnlockedBuildingIds.Add(d.buildingID);
            foreach (var pair in _essential)
            {
                s.essentials.Add(new InfraTown.Save.EssentialFacilitySaveState
                {
                    service = (int)pair.Key,
                    missingWeeks = pair.Value.missingWeeks,
                    threshold = pair.Value.threshold,
                    firstInstallPlayed = pair.Value.firstInstallPlayed,
                });
            }
            return s;
        }

        /// <summary>
        /// 이어하기: 이미 본 오프닝은 다시 시작하지 않고, 해금 안내·필수 시설 불만/설치 대사 진행을 저장 시점으로 되돌림.
        /// 저장할 때 아직 이야기가 시작되기 전(이름 입력 대기 등)이었으면 처음처럼 시작한다
        /// </summary>
        public void ApplySaveState(InfraTown.Save.DialogueTriggerSaveState s)
        {
            if (s == null || !s.storyStarted) return;

            if (IsWaitingForPlayerName)
            {
                IsWaitingForPlayerName = false;
                TimeManager.Instance?.ReleaseEventPause(this);
            }
            _initialized = true;
            StoryStarted = true;

            _knownUnlocked.Clear();
            if (BuildingManager.Instance != null)
            {
                foreach (var id in s.knownUnlockedBuildingIds)
                {
                    var d = BuildingManager.Instance.FindBuildingData(id);
                    if (d != null) _knownUnlocked.Add(d);
                }
            }

            foreach (var e in s.essentials)
            {
                var state = GetState((ServiceType)e.service);
                state.missingWeeks = e.missingWeeks;
                state.threshold = e.threshold;
                state.firstInstallPlayed = e.firstInstallPlayed;
            }

            // 이번 주는 이미 센 것으로 (불러온 직후 날짜 알림을 한 주로 세지 않음)
            if (TimeManager.Instance != null)
                _lastDate = (TimeManager.Instance.CurrentYear, TimeManager.Instance.CurrentMonth, TimeManager.Instance.CurrentWeek);
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

        private void HandleBadStreakChanged(int weeks, int limit)
        {
            if (Dialogue == null || !badStreakWarningWeeks.Contains(weeks)) return;

            int left = Mathf.Max(0, limit - weeks);
            var line = new DialogueLine(warningSpeaker,
                $"주민 불만이 {weeks}주째 계속되고 있어요. {left}주 안에 나아지지 않으면 마을이 무너집니다.",
                $"주민들이 {weeks}주째 화가 나 있어요! {left}주 안에 마을을 고쳐 주세요.",
                warningSpeakerPortrait);
            Dialogue.Play(DialogueData.Create($"warning_bad_{weeks}_{Time.frameCount}", true, false, line));
        }

        private void HandleGameEnded(GameResult result)
        {
            var dialogue = result == GameResult.Victory ? victoryDialogue : defeatDialogue;
            if (dialogue != null) Dialogue?.Play(dialogue);
        }
    }
}

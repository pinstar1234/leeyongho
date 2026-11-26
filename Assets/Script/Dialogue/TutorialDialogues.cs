using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using InfraTown.Building;
using InfraTown.TimeSystem;

namespace InfraTown.EventSystem
{
    /// <summary>튜토리얼 단계를 끝내려면 플레이어가 해야 하는 행동 (사용자 기획 2026-11-26)</summary>
    public enum TutorialGoal
    {
        /// <summary>설명만 — 대화가 끝나면 다음 단계</summary>
        None,
        /// <summary>buildingId 건물을 설치하면 다음</summary>
        PlaceBuilding,
        /// <summary>종합 현황판을 열면 다음 (OverviewUI의 패널이 켜짐, 또는 UI가 NotifyAction(overview_opened))</summary>
        OpenOverview,
        /// <summary>종합 현황판을 닫으면 다음</summary>
        CloseOverview,
        /// <summary>시간 속도를 바꾸면 다음 (일시정지·1·2·3배속 아무거나)</summary>
        ChangeSpeed,
        /// <summary>건물을 철거하면 다음</summary>
        Demolish,
    }

    /// <summary>
    /// 튜토리얼 행동 ID. 건물·시간 관련은 코드가 자동으로 알고, UI 행동은 UI가 TutorialDialogues.Instance.NotifyAction(ID)으로 보낼 수도 있다.
    /// </summary>
    public static class TutorialActions
    {
        public const string BuildingPlaced = "building_placed";
        public const string HousingPlaced = "housing_placed";
        public const string PowerPolePlaced = "power_pole_placed";
        public const string BuildingDemolished = "building_demolished";
        public const string SpeedChanged = "speed_changed";
        public const string OverviewOpened = "overview_opened";
        public const string OverviewClosed = "overview_closed";
        public const string BuildMenuOpened = "build_menu_opened";
    }

    /// <summary>
    /// 튜토리얼 한 단계: 설명 대화 → (goal이 있으면) 그 행동을 할 때까지 기다림 → 다음 단계.
    /// 기다리는 동안에는 allowedBuildings 건물만 고를 수 있고, allowedTargets 버튼만 눌리며(나머지 버튼은 잠시 비활성) 그 버튼이 깜빡인다.
    /// </summary>
    [Serializable]
    public class TutorialStep
    {
        [Tooltip("단계 ID (연출 팀원이 OnStepStarted에서 이 ID로 화살표 등을 띄울 수 있음)")]
        public string stepId;

        [Tooltip("이 단계를 끝내는 행동")]
        public TutorialGoal goal = TutorialGoal.None;

        [Tooltip("PlaceBuilding: 이 건물 ID를 설치하면 다음")]
        public string buildingId;

        [Tooltip("기다리는 동안 고를 수 있는 건물 ID (건설 메뉴·드래그)")]
        public List<string> allowedBuildings = new List<string>();

        [Tooltip("기다리는 동안 눌리는 버튼: '컴포넌트.필드'(예: BuildingUIManager.btnInfrastructure, 배열 필드면 전부), " +
                 "'slot:건물ID'(건설 목록의 그 건물 칸), 또는 오브젝트 이름. 나머지 버튼은 잠시 비활성")]
        public List<string> allowedTargets = new List<string>();

        [Tooltip("기다리는 동안 맵의 건물을 눌러 정보창(철거 버튼)을 열 수 있는지")]
        public bool allowBuildingClick;

        [Tooltip("대화 (비우면 stepId에 맞는 기본 문장)")]
        public DialogueData dialogue;

        [Tooltip("대화 전에 보여줄 컷씬 (비워도 됨)")]
        public CutsceneData cutsceneBefore;

        public TutorialStep() { }

        public static TutorialStep Talk(string id) => new TutorialStep { stepId = id };

        public static TutorialStep Build(string id, string buildingId, params string[] targets) => new TutorialStep
        {
            stepId = id, goal = TutorialGoal.PlaceBuilding, buildingId = buildingId,
            allowedBuildings = new List<string> { buildingId }, allowedTargets = new List<string>(targets),
        };

        public static TutorialStep Do(string id, TutorialGoal goal, params string[] targets) => new TutorialStep
        {
            stepId = id, goal = goal, allowedTargets = new List<string>(targets),
        };
    }

    /// <summary>
    /// 튜토리얼 (사용자 기획 2026-11-26, 예전 7주 주차별 대화를 바꿈): 게임을 시작하면 1주차에 기능을 하나씩 단계별로 알려준다.
    /// 튜토리얼 동안은 시간이 멈추고, 지금 알려 주는 기능 말고는 건물 선택·건물 클릭·다른 버튼을 쓸 수 없다.
    /// 오프닝(DialogueTriggers) 다음에 자동으로 시작 (씬에 없으면 DialogueTriggers가 추가). 연출은 OnStepStarted/OnWaitingForAction/OnStepFinished.
    /// </summary>
    public class TutorialDialogues : MonoBehaviour
    {
        public static TutorialDialogues Instance { get; private set; }

        [SerializeField] private bool tutorialEnabled = true;

        [Tooltip("튜토리얼을 설명하는 인물 (기본 문장에 사용, 초상화는 DialogueEventManager의 Characters에 등록)")]
        [SerializeField] private string secretarySpeaker = "비서";

        [Tooltip("누를 버튼 강조 테두리 색 (깜빡임)")]
        [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.1f, 1f);

        [Tooltip("새로 생긴 버튼(건설 목록 칸 등)을 잠그기 위해 다시 살피는 간격 (실제 초)")]
        [SerializeField] private float gateRefreshSeconds = 0.25f;

        // 예전 7주 단계(steps)와 이름이 겹치지 않게 새 필드 — 씬에 저장된 예전 목록은 쓰지 않는다
        [SerializeField] private List<TutorialStep> guideSteps = DefaultSteps();

        /// <summary>기본 단계 (사용자 결정 2026-11-26: 인사 → 도로 → 주택 → 전봇대 → 종합 현황판 → 속도 → 철거 → 마무리(저장은 안내만))</summary>
        public static List<TutorialStep> DefaultSteps()
        {
            const string infra = "BuildingUIManager.btnInfrastructure", housing = "BuildingUIManager.btnHousing";
            const string small = "BuildingUIManager.smallButton", cancel = "BuildingUIManager.cancelButton";
            return new List<TutorialStep>
            {
                TutorialStep.Talk("guide_welcome"),
                TutorialStep.Build("guide_road", "road", infra, small, cancel, "slot:road"),
                TutorialStep.Build("guide_house", "house", housing, small, cancel, "slot:house"),
                TutorialStep.Build("guide_power_pole", "power_pole", infra, small, cancel, "slot:power_pole"),
                TutorialStep.Do("guide_overview_open", TutorialGoal.OpenOverview, "TopHUDManager.overviewButton", "OverviewButton", "OpenOverviewButton"),
                TutorialStep.Do("guide_overview_close", TutorialGoal.CloseOverview,
                    "OverviewUI.closeButton", "OverviewUI.powerSlider", "OverviewUI.gasSlider", "OverviewUI.subsidySlider", "OverviewUI.overviewTabs"),
                TutorialStep.Do("guide_speed", TutorialGoal.ChangeSpeed, "TimeUI.btnPause", "TimeUI.btnSpeed1", "TimeUI.btnSpeed2", "TimeUI.btnSpeed3"),
                new TutorialStep
                {
                    stepId = "guide_demolish", goal = TutorialGoal.Demolish, allowBuildingClick = true,
                    allowedTargets = new List<string> { "BuildingUIManager.btnDemolish", "BuildingUIManager.btnCloseInfo", "BuildingUIManager.btnInfo" },
                },
                TutorialStep.Talk("guide_finish"),
            };
        }

        public bool TutorialEnabled { get => tutorialEnabled; set => tutorialEnabled = value; }

        /// <summary>자동 플레이·빌드 테스트용: 켜면 튜토리얼을 시작하자마자 완료 처리 (튜토리얼 자체를 확인하는 테스트는 끔)</summary>
        public static bool SkipForAutomatedTests;
        public IReadOnlyList<TutorialStep> Steps => guideSteps;

        /// <summary>튜토리얼 진행 중 (시작했고 아직 안 끝남)</summary>
        public bool IsActive { get; private set; }
        public bool IsCompleted { get; private set; }

        /// <summary>튜토리얼은 1주차에 모두 진행 (예전 7주 방식과의 호환용, 진행 중이면 1)</summary>
        public int CurrentWeek => IsActive || IsCompleted ? 1 : 0;

        /// <summary>지금 단계 번호 (0부터, 시작 전 -1)</summary>
        public int CurrentStepIndex { get; private set; } = -1;

        /// <summary>지금 단계 (대화 중이거나 행동을 기다리는 중, 없으면 null)</summary>
        public TutorialStep CurrentStep => IsActive && CurrentStepIndex >= 0 && CurrentStepIndex < guideSteps.Count ? guideSteps[CurrentStepIndex] : null;

        /// <summary>설명 대화가 끝나고 플레이어 행동을 기다리는 중</summary>
        public bool IsWaitingForAction { get; private set; }

        public event Action OnTutorialStarted;
        public event Action<TutorialStep> OnStepStarted;        // 단계 대화의 첫 줄이 나오기 직전
        public event Action<TutorialStep> OnWaitingForAction;   // 설명이 끝나고 행동을 기다리기 시작 (화살표 등)
        public event Action<TutorialStep> OnStepFinished;       // 단계를 마침 (행동을 했거나 설명만인 단계의 대화가 끝남)
        public event Action OnTutorialCompleted;
        /// <summary>예전 주차 이벤트 (호환용 — 튜토리얼을 시작할 때 1주차로 한 번)</summary>
        public event Action<int> OnTutorialWeekChanged;

        private readonly HashSet<string> _finished = new HashSet<string>();
        private TimeManager _time;
        private BuildingManager _buildings;
        private bool _subscribedDestroy;
        private int _dialogueToken; // 이전 단계 대화의 콜백이 늦게 와도 무시
        private float _gateTimer;
        private bool _overviewWasOpen;

        private DialogueEventManager Dialogue => DialogueEventManager.Instance;

        /// <summary>그 단계를 이미 마쳤는지</summary>
        public bool IsStepTriggered(string stepId) => _finished.Contains(stepId) || (CurrentStep != null && CurrentStep.stepId == stepId);

        private void Awake()
        {
            if (Instance != null && Instance != this) { InfraTown.Core.SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            ReleaseGate();
            if (TimeManager.Instance != null) TimeManager.Instance.ReleaseEventPause(this);
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!IsActive) return;
            PollOverview();
            _gateTimer -= Time.unscaledDeltaTime;
            if (_gateTimer <= 0f)
            {
                _gateTimer = gateRefreshSeconds;
                ApplyGate();
            }
            PulseHighlights();
        }

        // ───────────────── 진행 ─────────────────

        /// <summary>튜토리얼 시작 (DialogueTriggers가 오프닝 다음에 부름). 꺼져 있거나 이미 시작·완료했으면 무시</summary>
        public void BeginTutorial()
        {
            if (!tutorialEnabled || IsActive || IsCompleted) return;
            if (SkipForAutomatedTests) { Complete(); return; } // 자동 플레이·빌드 테스트는 튜토리얼 없이 (시간 정지·건물 잠금이 시나리오를 막지 않게)
            IsActive = true;
            Subscribe();
            HoldTime(true);
            OnTutorialStarted?.Invoke();
            OnTutorialWeekChanged?.Invoke(1);
            StartStep(0);
        }

        /// <summary>UI 등에서 플레이어 행동을 알림 (예: NotifyAction(TutorialActions.OverviewOpened))</summary>
        public void NotifyAction(string actionId)
        {
            var step = CurrentStep;
            if (step == null || !IsWaitingForAction || string.IsNullOrEmpty(actionId)) return;
            bool done = step.goal switch
            {
                TutorialGoal.OpenOverview => actionId == TutorialActions.OverviewOpened,
                TutorialGoal.CloseOverview => actionId == TutorialActions.OverviewClosed,
                TutorialGoal.ChangeSpeed => actionId == TutorialActions.SpeedChanged,
                TutorialGoal.Demolish => actionId == TutorialActions.BuildingDemolished,
                _ => false,
            };
            if (done) FinishStep();
        }

        /// <summary>튜토리얼 건너뛰기 (잠금·시간 정지를 모두 풀고 끝)</summary>
        public void SkipTutorial()
        {
            if (IsCompleted) return;
            Complete();
        }

        /// <summary>예전 주차 진행 (호환용 — 이제 튜토리얼은 1주차에 끝나므로 아무것도 하지 않음)</summary>
        public void AdvanceWeek() { }

        private void StartStep(int index)
        {
            IsWaitingForAction = false;
            CurrentStepIndex = index;
            if (index >= guideSteps.Count) { Complete(); return; }

            var step = guideSteps[index];
            ApplyGate(); // 대화 중에는 모든 버튼 잠금

            int token = ++_dialogueToken;
            var dialogue = step.dialogue != null ? step.dialogue : CreateDefaultDialogue(step);
            if (dialogue == null || Dialogue == null)
            {
                OnStepStarted?.Invoke(step);
                OnDialogueDone(token);
                return;
            }
            Dialogue.Play(dialogue, step.cutsceneBefore,
                () => { if (token == _dialogueToken) OnStepStarted?.Invoke(step); },
                () => OnDialogueDone(token));
        }

        private void OnDialogueDone(int token)
        {
            if (token != _dialogueToken || !IsActive) return;
            var step = CurrentStep;
            if (step == null) return;

            if (step.goal == TutorialGoal.None) { FinishStep(); return; }

            // 그 행동을 할 화면이 이 씬에 없으면 (예: 종합 현황판이 없는 씬) 막히지 않게 건너뜀
            if (NeedsUi(step.goal) && ResolveTargets(step).Count == 0 && !(step.goal == TutorialGoal.CloseOverview && IsOverviewOpen()))
            {
                Debug.LogWarning($"[Tutorial] '{step.stepId}'에 필요한 버튼이 이 씬에 없어 건너뜁니다.");
                FinishStep();
                return;
            }
            // 이미 해 둔 상태면 (현황판이 이미 닫혀 있음 등) 바로 다음
            if (step.goal == TutorialGoal.CloseOverview && !IsOverviewOpen()) { FinishStep(); return; }

            IsWaitingForAction = true;
            _overviewWasOpen = IsOverviewOpen();
            ApplyGate();
            OnWaitingForAction?.Invoke(step);
        }

        private static bool NeedsUi(TutorialGoal goal) =>
            goal == TutorialGoal.OpenOverview || goal == TutorialGoal.CloseOverview || goal == TutorialGoal.ChangeSpeed;

        private void FinishStep()
        {
            var step = CurrentStep;
            if (step == null) return;
            IsWaitingForAction = false;
            _finished.Add(step.stepId);
            BuildingManager.Instance?.CancelBuildingMode(); // 다음 단계에서 고른 건물이 남지 않게
            OnStepFinished?.Invoke(step);
            StartStep(CurrentStepIndex + 1);
        }

        private void Complete()
        {
            IsActive = false;
            IsCompleted = true;
            IsWaitingForAction = false;
            CurrentStepIndex = guideSteps.Count;
            _dialogueToken++;
            Unsubscribe();
            ReleaseGate();
            HoldTime(false);
            OnTutorialCompleted?.Invoke();
        }

        private void HoldTime(bool hold)
        {
            var time = TimeManager.Instance;
            if (time == null) return;
            if (hold) time.RequestEventPause(this); // 튜토리얼 동안 시간 정지 → 1주차에 끝남
            else time.ReleaseEventPause(this);
        }

        // ───────────────── 기능 잠금 (기능 코드가 묻는 곳) ─────────────────

        /// <summary>
        /// 튜토리얼 때문에 이 건물을 고를 수 없는지 (BuildingManager.SelectBuildingToBuild가 물음). hint = 지금 해야 할 일 안내
        /// </summary>
        public static bool BlocksBuilding(BuildingData data, out string hint)
        {
            hint = null;
            var t = Instance;
            if (t == null || !t.IsActive || data == null) return false;
            var step = t.CurrentStep;
            if (t.IsWaitingForAction && step != null && step.allowedBuildings.Contains(data.buildingID)) return false;
            hint = t.CurrentHint();
            return true;
        }

        /// <summary>튜토리얼 때문에 맵의 건물을 눌러 정보창(철거)을 열 수 없는지</summary>
        public static bool BlocksBuildingClick
        {
            get
            {
                var t = Instance;
                if (t == null || !t.IsActive) return false;
                var step = t.CurrentStep;
                return !(t.IsWaitingForAction && step != null && step.allowBuildingClick);
            }
        }

        /// <summary>지금 해야 할 일 한 줄 안내 (토스트용)</summary>
        public string CurrentHint()
        {
            var step = CurrentStep;
            if (step == null) return null;
            if (!IsWaitingForAction) return "튜토리얼: 비서의 설명을 먼저 들어 주세요.";
            return step.goal switch
            {
                TutorialGoal.PlaceBuilding => $"튜토리얼: 지금은 {BuildingName(step.buildingId)}을(를) 지어 보세요.",
                TutorialGoal.OpenOverview => "튜토리얼: 종합 현황판을 열어 보세요.",
                TutorialGoal.CloseOverview => "튜토리얼: 종합 현황판을 닫아 보세요.",
                TutorialGoal.ChangeSpeed => "튜토리얼: 시간 속도 버튼을 눌러 보세요.",
                TutorialGoal.Demolish => "튜토리얼: 건물을 눌러 철거해 보세요.",
                _ => null,
            };
        }

        private static string BuildingName(string id)
        {
            var data = BuildingManager.Instance != null ? BuildingManager.Instance.FindBuildingData(id) : null;
            return data != null ? data.buildingName : id;
        }

        // ───────────────── 버튼 잠금·강조 (UI 스크립트·씬은 바꾸지 않고 실행 중에만) ─────────────────

        private readonly List<Selectable> _lockedByTutorial = new List<Selectable>();
        private readonly List<Outline> _highlights = new List<Outline>();

        // 잠그지 않는 화면 (대화창·일시정지 메뉴·불러오기 실패 창 — 튜토리얼 중에도 써야 함)
        private static readonly string[] ExemptOwners = { "DialogueWindow", "GamePauseMenu", "SaveLoadFailurePopup" };

        /// <summary>지금 단계에서 누를 수 있는 버튼 (설명 대화 중이면 없음)</summary>
        public List<Selectable> AllowedSelectables() => IsActive && IsWaitingForAction && CurrentStep != null ? ResolveTargets(CurrentStep) : new List<Selectable>();

        /// <summary>단계의 allowedTargets를 지금 씬의 버튼으로 바꿈</summary>
        public static List<Selectable> ResolveTargets(TutorialStep step)
        {
            var result = new List<Selectable>();
            if (step == null) return result;
            MonoBehaviour[] behaviours = null;
            foreach (var target in step.allowedTargets)
            {
                if (string.IsNullOrWhiteSpace(target)) continue;
                if (target.StartsWith("slot:"))
                {
                    string id = target.Substring(5);
                    behaviours ??= FindObjectsOfType<MonoBehaviour>(true);
                    foreach (var b in behaviours)
                    {
                        if (b == null || b.GetType().Name != "BuildingSlotUI") continue;
                        var data = GetMember(b, "buildingData") as BuildingData;
                        if (data == null || data.buildingID != id) continue;
                        var button = GetMember(b, "button") as Selectable ?? b.GetComponentInChildren<Selectable>(true);
                        if (button != null) result.Add(button);
                    }
                    continue;
                }
                int dot = target.IndexOf('.');
                if (dot > 0)
                {
                    string typeName = target.Substring(0, dot), field = target.Substring(dot + 1);
                    behaviours ??= FindObjectsOfType<MonoBehaviour>(true);
                    foreach (var b in behaviours)
                    {
                        if (b == null || b.GetType().Name != typeName) continue;
                        AddSelectables(GetMember(b, field), result);
                    }
                    continue;
                }
                foreach (var s in FindObjectsOfType<Selectable>(true))
                    if (s != null && s.gameObject.name == target) result.Add(s);
            }
            result.RemoveAll(s => s == null);
            return result;
        }

        private static void AddSelectables(object value, List<Selectable> into)
        {
            switch (value)
            {
                case Selectable s when s != null: into.Add(s); break;
                case GameObject go when go != null: into.AddRange(go.GetComponentsInChildren<Selectable>(true)); break;
                case Component c when c != null: into.AddRange(c.GetComponentsInChildren<Selectable>(true)); break;
                case System.Collections.IEnumerable list when !(value is string):
                    foreach (var item in list) AddSelectables(item, into);
                    break;
            }
        }

        private static object GetMember(object owner, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (var type = owner.GetType(); type != null; type = type.BaseType)
            {
                var f = type.GetField(name, flags);
                if (f != null) return f.GetValue(owner);
                var p = type.GetProperty(name, flags);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(owner);
            }
            return null;
        }

        private static bool IsExempt(Selectable s)
        {
            for (var t = s.transform; t != null; t = t.parent)
            {
                foreach (var c in t.GetComponents<MonoBehaviour>())
                    if (c != null && Array.IndexOf(ExemptOwners, c.GetType().Name) >= 0) return true;
            }
            return false;
        }

        /// <summary>허용 버튼만 눌리게 하고 나머지는 잠시 비활성 + 허용 버튼 깜빡임 (주기적으로 다시 — 새로 생긴 목록 칸도)</summary>
        public void ApplyGate()
        {
            if (!IsActive) { ReleaseGate(); return; }
            var allowed = new HashSet<Selectable>(AllowedSelectables());

            // 이제 허용된 버튼은 되돌림
            for (int i = _lockedByTutorial.Count - 1; i >= 0; i--)
            {
                var s = _lockedByTutorial[i];
                if (s == null) { _lockedByTutorial.RemoveAt(i); continue; }
                if (allowed.Contains(s)) { s.interactable = true; _lockedByTutorial.RemoveAt(i); }
            }
            foreach (var s in FindObjectsOfType<Selectable>(true))
            {
                if (s == null || allowed.Contains(s) || !s.interactable) continue;
                if (_lockedByTutorial.Contains(s)) { s.interactable = false; continue; } // UI가 다시 켰으면 다시 잠금
                if (IsExempt(s)) continue;
                s.interactable = false;
                _lockedByTutorial.Add(s);
            }
            UpdateHighlights(allowed);
        }

        /// <summary>튜토리얼이 잠근 버튼을 모두 되돌림</summary>
        private void ReleaseGate()
        {
            foreach (var s in _lockedByTutorial) if (s != null) s.interactable = true;
            _lockedByTutorial.Clear();
            UpdateHighlights(new HashSet<Selectable>());
        }

        /// <summary>튜토리얼이 지금 잠가 둔 버튼 수 (테스트·디버그용)</summary>
        public int LockedCount => _lockedByTutorial.Count;

        private void UpdateHighlights(HashSet<Selectable> allowed)
        {
            for (int i = _highlights.Count - 1; i >= 0; i--)
            {
                var h = _highlights[i];
                var owner = h != null ? h.GetComponentInParent<Selectable>() : null;
                if (h == null || owner == null || !allowed.Contains(owner))
                {
                    if (h != null) { if (Application.isPlaying) Destroy(h); else DestroyImmediate(h); }
                    _highlights.RemoveAt(i);
                }
            }
            foreach (var s in allowed)
            {
                var graphic = s.targetGraphic != null ? s.targetGraphic.gameObject : s.gameObject;
                if (_highlights.Exists(h => h != null && h.gameObject == graphic)) continue;
                if (graphic.GetComponent<Graphic>() == null) continue;
                var outline = graphic.AddComponent<Outline>(); // 실행 중에만 붙였다가 지움
                outline.effectDistance = new Vector2(4f, -4f);
                outline.effectColor = highlightColor;
                _highlights.Add(outline);
            }
        }

        /// <summary>강조 중인 버튼 (테스트·연출용)</summary>
        public List<GameObject> HighlightedObjects() => _highlights.FindAll(h => h != null).ConvertAll(h => h.gameObject);

        private void PulseHighlights()
        {
            float a = 0.35f + 0.65f * Mathf.PingPong(Time.unscaledTime * 2f, 1f);
            foreach (var h in _highlights)
            {
                if (h == null) continue;
                var c = highlightColor; c.a *= a;
                h.effectColor = c;
            }
        }

        // ───────────────── 종합 현황판 열림 감지 ─────────────────

        private MonoBehaviour _overview;

        private bool IsOverviewOpen()
        {
            if (_overview == null)
            {
                foreach (var b in FindObjectsOfType<MonoBehaviour>(true))
                    if (b != null && b.GetType().Name == "OverviewUI") { _overview = b; break; }
            }
            if (_overview == null) return false;
            var panel = GetMember(_overview, "overviewPanel") as GameObject;
            return panel != null ? panel.activeInHierarchy : _overview.gameObject.activeInHierarchy;
        }

        private void PollOverview()
        {
            var step = CurrentStep;
            if (!IsWaitingForAction || step == null) return;
            bool open = IsOverviewOpen();
            if (open == _overviewWasOpen) return;
            _overviewWasOpen = open;
            NotifyAction(open ? TutorialActions.OverviewOpened : TutorialActions.OverviewClosed);
        }

        // ───────────────── 세이브 ─────────────────

        public InfraTown.Save.TutorialSaveState GetSaveState()
        {
            var s = new InfraTown.Save.TutorialSaveState
            {
                active = IsActive, completed = IsCompleted, currentWeek = CurrentWeek,
                currentStep = IsActive ? CurrentStepIndex : -1,
            };
            s.triggeredStepIds.AddRange(_finished);
            return s;
        }

        /// <summary>
        /// 튜토리얼 진행을 저장 시점으로 되돌림. 진행 중이면 그 단계의 설명부터 다시(이미 마친 단계는 다시 안 나옴), 끝났으면 끝난 상태로
        /// </summary>
        public void ApplySaveState(InfraTown.Save.TutorialSaveState s)
        {
            if (s == null) return;
            Unsubscribe();
            ReleaseGate();
            HoldTime(false);
            _dialogueToken++;
            _finished.Clear();
            foreach (var id in s.triggeredStepIds) if (!string.IsNullOrEmpty(id)) _finished.Add(id);
            IsWaitingForAction = false;
            IsCompleted = s.completed;
            IsActive = s.active && !s.completed;
            CurrentStepIndex = IsCompleted ? guideSteps.Count : -1;
            if (!IsActive) return;

            // 예전(7주 방식) 저장이면 currentStep이 없음 → 마친 단계 다음부터
            int index = s.currentStep >= 0 ? s.currentStep : 0;
            while (index < guideSteps.Count && _finished.Contains(guideSteps[index].stepId)) index++;
            Subscribe();
            HoldTime(true);
            StartStep(index);
        }

        // ───────────────── 이벤트 구독 ─────────────────

        private void Subscribe()
        {
            if (_time == null && TimeManager.Instance != null)
            {
                _time = TimeManager.Instance;
                _time.OnSpeedChanged += HandleSpeedChanged;
            }
            if (_buildings == null && BuildingManager.Instance != null)
            {
                _buildings = BuildingManager.Instance;
                _buildings.OnBuildingPlaced += HandleBuildingPlaced;
            }
            if (!_subscribedDestroy)
            {
                BuildingInstance.OnBuildingDestroyed += HandleBuildingDestroyed;
                _subscribedDestroy = true;
            }
        }

        private void Unsubscribe()
        {
            if (_time != null) { _time.OnSpeedChanged -= HandleSpeedChanged; _time = null; }
            if (_buildings != null) { _buildings.OnBuildingPlaced -= HandleBuildingPlaced; _buildings = null; }
            if (_subscribedDestroy) { BuildingInstance.OnBuildingDestroyed -= HandleBuildingDestroyed; _subscribedDestroy = false; }
        }

        private void HandleSpeedChanged(GameSpeed speed) => NotifyAction(TutorialActions.SpeedChanged);

        private void HandleBuildingPlaced(BuildingInstance building)
        {
            var step = CurrentStep;
            if (!IsWaitingForAction || step == null || building == null || building.Data == null) return;
            if (step.goal == TutorialGoal.PlaceBuilding && building.Data.buildingID == step.buildingId) FinishStep();
        }

        private void HandleBuildingDestroyed(BuildingInstance building)
        {
            if (InfraTown.Save.SaveSystem.IsApplying) return;
            NotifyAction(TutorialActions.BuildingDemolished);
        }

        // ───────────────── 기본(임시) 문장: 대화 에셋을 넣으면 그것으로 바뀜. {player}는 주인공 이름 ─────────────────

        private DialogueData CreateDefaultDialogue(TutorialStep step)
        {
            var lines = DefaultLines(step.stepId);
            if (lines == null) return null;
            // 불러오기로 같은 단계를 다시 설명할 수 있게 한 번만 재생(playOnce)하지 않음
            return DialogueData.Create($"tutorial_{step.stepId}", true, false, lines);
        }

        private DialogueLine S(string text, string easy) => new DialogueLine(secretarySpeaker, text, easy);
        private DialogueLine P(string text, string easy) => new DialogueLine("{player}", text, easy);

        /// <summary>기본 단계 ID의 임시 문장 (없는 ID면 null)</summary>
        public DialogueLine[] DefaultLines(string stepId) => stepId switch
        {
            "guide_welcome" => new[]
            {
                S("{player}님, 어서 오세요! 오늘부터 이 마을을 맡아 주실 분이시죠? 저는 옆에서 도와드릴 비서예요.",
                  "{player}님, 반가워요! 저는 비서예요. 제가 도와 드릴게요."),
                P("잘 부탁해요. 뭐부터 하면 될까요?", "잘 부탁해요! 뭐부터 할까요?"),
                S("마을을 꾸리는 방법을 하나씩 직접 해 보면서 알려 드릴게요. 배우는 동안에는 시간이 멈춰 있으니 천천히 따라 해 주세요.",
                  "하나씩 같이 해 봐요. 배우는 동안 시간은 멈춰 있어요."),
            },
            "guide_road" => new[]
            {
                S("모든 건물은 도로에 맞닿아 있어야 지을 수 있어요. 먼저 길부터 깔아 볼까요?",
                  "건물은 도로 옆에만 지을 수 있어요. 먼저 길을 깔아요."),
                S("반짝이는 '인프라' 버튼을 누르고 도로를 고른 뒤 땅을 눌러 주세요. 누른 채로 끌면 한 줄로 여러 칸을 한 번에 깔 수 있고, R키로 도로 모양을 바꿀 수 있어요.",
                  "반짝이는 '인프라' 버튼 → 도로 → 땅을 누르세요. 끌면 여러 칸이 깔려요."),
            },
            "guide_house" => new[]
            {
                S("잘하셨어요! 이번에는 주민이 살 집을 지어 봐요. 반짝이는 '주택' 버튼을 누르고, 방금 깐 도로 바로 옆 땅에 지어 주세요.",
                  "잘했어요! '주택' 버튼을 누르고 도로 옆에 집을 지어요."),
                S("Q, E 키로 건물 방향을 돌릴 수 있어요. 건물을 지을 때마다 건설비가 들고, 매달 유지비도 나가요.",
                  "Q, E 키로 돌릴 수 있어요. 건물을 지으면 돈이 들어요."),
            },
            "guide_power_pole" => new[]
            {
                S("집과 시설은 전기가 있어야 제대로 돌아가요. 전봇대 둘레에 있는 건물에만 전기가 들어가요.",
                  "건물은 전기가 필요해요. 전봇대 가까운 건물에만 전기가 가요."),
                S("'인프라' 버튼에서 전봇대를 골라 방금 지은 집 가까이 세워 주세요. 전봇대는 도로 위에도 세울 수 있어요.",
                  "'인프라'에서 전봇대를 골라 집 옆에 세워요. 도로 위에도 돼요."),
            },
            "guide_overview_open" => new[]
            {
                S("이제 불이 들어왔네요! 마을 형편은 종합 현황판에서 한눈에 볼 수 있어요. 반짝이는 현황판 버튼을 눌러 열어 주세요.",
                  "불이 켜졌어요! 반짝이는 현황판 버튼을 눌러 보세요."),
            },
            "guide_overview_close" => new[]
            {
                S("여기서 인구, 돈, 전기, 만족도와 주민들의 불만을 볼 수 있어요.",
                  "여기서 인구, 돈, 전기, 만족도를 볼 수 있어요."),
                S("전기와 가스는 매달 사 와야 해요. 슬라이더로 다음 달에 살 양을 정하고, 정부에 신청할 지원금도 여기서 정해요. 여름에는 전기, 겨울에는 가스를 가장 많이 써요.",
                  "전기와 가스는 매달 사요. 여기서 얼마나 살지 정해요."),
                S("다 보셨으면 현황판을 닫아 주세요.", "다 봤으면 닫아 주세요."),
            },
            "guide_speed" => new[]
            {
                S("시간은 화면의 속도 버튼으로 멈추거나 빠르게 흐르게 할 수 있어요. 아무 속도 버튼이나 하나 눌러 보세요.",
                  "속도 버튼으로 시간을 멈추거나 빠르게 해요. 하나 눌러 보세요."),
            },
            "guide_demolish" => new[]
            {
                S("잘못 지은 건물은 허물 수 있어요. 맵에서 건물을 하나 눌러 정보창을 열고 철거 버튼을 눌러 보세요.",
                  "건물을 누르고 철거 버튼을 눌러 보세요."),
                S("집을 허물면 살던 주민들이 갈 곳을 잃어 불만이 생기니, 실제로 할 때는 조심해 주세요.",
                  "집을 부수면 주민들이 속상해해요. 조심해요."),
            },
            "guide_finish" => new[]
            {
                S("기본은 다 배우셨어요! 주민 만족도가 높으면 이웃들이 이사 오고, 낮으면 떠나요.",
                  "다 배웠어요! 주민들이 기분 좋으면 이사 와요."),
                S("주민이 늘면 병원, 학교, 소방서, 경찰서를 지을 수 있게 돼요. 집에서 가까울수록 좋고, 지을 수 있는데 안 지으면 만족도가 떨어져요.",
                  "주민이 늘면 병원, 학교, 소방서, 경찰서를 지어요. 집 가까이 지어요."),
                S("게임은 ESC 키를 누르면 나오는 메뉴에서 1~5번 칸 중 하나를 골라 저장할 수 있어요. 매주 자동으로도 저장되고, 타이틀의 '이어하기'로 이어서 할 수 있어요.",
                  "ESC를 누르면 저장할 수 있어요. 매주 자동으로도 저장돼요."),
                S("주민 불만이 석 달 넘게 이어지면 마을이 무너지니 조심하세요. 10년 동안 모두가 살고 싶은 마을을 만들어 주세요!",
                  "주민들이 오래 화나 있으면 안 돼요. 10년 동안 멋진 마을을 만들어요!"),
                P("고마워요. 열심히 해 볼게요!", "고마워요! 열심히 할게요!"),
            },
            _ => null,
        };
    }
}

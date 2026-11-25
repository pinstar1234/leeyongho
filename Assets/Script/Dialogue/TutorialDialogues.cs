using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Building;
using InfraTown.TimeSystem;

namespace InfraTown.EventSystem
{
    /// <summary>튜토리얼 대화가 나오는 시점</summary>
    public enum TutorialTrigger
    {
        /// <summary>N주차가 시작될 때 (1주차 = 게임 시작, 오프닝·마을 도착 대화 다음)</summary>
        Day,
        /// <summary>튜토리얼 기간에 플레이어가 그 행동을 처음 했을 때 (TutorialActions 또는 NotifyAction으로 보낸 ID)</summary>
        Action
    }

    /// <summary>
    /// 튜토리얼에서 쓰는 행동 ID. 건물·시간 관련은 코드가 자동으로 보내고,
    /// UI 행동(종합 현황판 열기 등)은 UI가 TutorialDialogues.Instance.NotifyAction(ID)으로 보낸다.
    /// </summary>
    public static class TutorialActions
    {
        public const string BuildingPlaced = "building_placed";       // 자동: 아무 건물이나 설치
        public const string HousingPlaced = "housing_placed";         // 자동: 주택 설치
        public const string PowerPolePlaced = "power_pole_placed";    // 자동: 전봇대 설치
        public const string BuildingDemolished = "building_demolished"; // 자동: 건물 철거
        public const string SpeedChanged = "speed_changed";           // 자동: 시간 속도 변경
        public const string OverviewOpened = "overview_opened";       // UI가 보냄: 종합 현황판 열기
        public const string BuildMenuOpened = "build_menu_opened";    // UI가 보냄: 건설 메뉴 열기
    }

    /// <summary>
    /// 튜토리얼 대화 한 단계. 대화 에셋을 비워 두면 stepId에 맞는 기본(임시) 문장을 쓴다.
    /// </summary>
    [Serializable]
    public class TutorialStep
    {
        [Tooltip("단계 ID (연출 팀원이 OnStepStarted에서 이 ID로 화살표·하이라이트를 띄움)")]
        public string stepId;

        public TutorialTrigger trigger = TutorialTrigger.Day;

        [Tooltip("Day: 몇 주차에 나올지 (1~튜토리얼 기간, 2026-11-24 일 → 주 단위)")]
        public int day = 1;

        [Tooltip("Action: 이 행동을 처음 했을 때 (TutorialActions 참고)")]
        public string actionId;

        [Tooltip("대화 (비우면 기본 문장)")]
        public DialogueData dialogue;

        [Tooltip("대화 전에 보여줄 컷씬 (비워도 됨)")]
        public CutsceneData cutsceneBefore;

        public TutorialStep() { }

        public static TutorialStep OnDay(string id, int day) =>
            new TutorialStep { stepId = id, trigger = TutorialTrigger.Day, day = day };

        public static TutorialStep OnAction(string id, string actionId) =>
            new TutorialStep { stepId = id, trigger = TutorialTrigger.Action, actionId = actionId };
    }

    /// <summary>
    /// 튜토리얼 대화 (사용자 기획 2026-11-19): 1주차부터 7주 동안 비서가 기능을 하나씩 알려준다 (2026-11-24 일 → 주 단위).
    /// 이 컴포넌트는 '언제 어떤 대화를 띄울지'만 맡고, 화살표·하이라이트 같은 연출은 다른 팀원이
    /// OnStepStarted / OnStepFinished(단계 ID)를 구독해서 만든다.
    /// DialogueTriggers와 같은 오브젝트에 두면 오프닝(컷씬 → 마을 도착 대화)이 끝난 뒤 자동으로 시작한다
    /// (씬에 없으면 DialogueTriggers가 추가).
    /// </summary>
    public class TutorialDialogues : MonoBehaviour
    {
        public static TutorialDialogues Instance { get; private set; }

        [SerializeField] private bool tutorialEnabled = true;

        [Tooltip("튜토리얼 기간 (주). 이 주가 지나면 끝나고, 아직 안 나온 행동 대화는 나오지 않는다")]
        [SerializeField] private int tutorialWeeks = 7;

        [Tooltip("튜토리얼을 설명하는 인물 (기본 문장에 사용, 초상화는 DialogueEventManager의 Characters에 등록)")]
        [SerializeField] private string secretarySpeaker = "비서";

        [SerializeField] private List<TutorialStep> steps = new List<TutorialStep>
        {
            TutorialStep.OnDay("day1_welcome", 1),
            TutorialStep.OnAction("first_building", TutorialActions.BuildingPlaced),
            TutorialStep.OnDay("day2_power", 2),
            TutorialStep.OnAction("first_power_pole", TutorialActions.PowerPolePlaced),
            TutorialStep.OnDay("day3_money", 3),
            TutorialStep.OnDay("day4_satisfaction", 4),
            TutorialStep.OnDay("day5_access", 5),
            TutorialStep.OnDay("day6_overview", 6),
            TutorialStep.OnAction("first_demolish", TutorialActions.BuildingDemolished),
            TutorialStep.OnDay("day7_finish", 7),
        };

        public bool TutorialEnabled { get => tutorialEnabled; set => tutorialEnabled = value; }
        public int TutorialWeeks => tutorialWeeks;
        public IReadOnlyList<TutorialStep> Steps => steps;

        /// <summary>튜토리얼 진행 중 (시작했고 아직 안 끝남)</summary>
        public bool IsActive { get; private set; }
        public bool IsCompleted { get; private set; }

        /// <summary>튜토리얼 몇 주차인지 (시작 전 0)</summary>
        public int CurrentWeek { get; private set; }

        /// <summary>지금 대화가 나오고 있는 단계 (없으면 null)</summary>
        public TutorialStep CurrentStep { get; private set; }

        public event Action OnTutorialStarted;
        public event Action<int> OnTutorialWeekChanged;      // 주차
        public event Action<TutorialStep> OnStepStarted;     // 단계 대화의 첫 줄이 나오기 직전
        public event Action<TutorialStep> OnStepFinished;    // 단계 대화가 끝났을 때 (건너뛰어도)
        public event Action OnTutorialCompleted;

        private readonly HashSet<TutorialStep> _triggered = new HashSet<TutorialStep>();
        private TimeManager _time;
        private BuildingManager _buildings;
        private bool _subscribedDestroy;
        private (int y, int m, int d) _lastDate = (-1, -1, -1);

        private DialogueEventManager Dialogue => DialogueEventManager.Instance;

        /// <summary>이 단계의 대화가 이미 나왔는지 (대기 중 포함)</summary>
        public bool IsStepTriggered(string stepId) => FindStep(stepId) is TutorialStep s && _triggered.Contains(s);

        private void Awake()
        {
            if (Instance != null && Instance != this) { InfraTown.Core.SingletonGuard.RemoveDuplicate(this, Instance); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 튜토리얼 시작 (1주차 대화). DialogueTriggers가 오프닝 다음에 부른다. 꺼져 있거나 이미 시작했으면 무시
        /// </summary>
        public void BeginTutorial()
        {
            if (!tutorialEnabled || IsActive || IsCompleted) return;

            IsActive = true;
            CurrentWeek = 1;
            Subscribe();
            OnTutorialStarted?.Invoke();
            OnTutorialWeekChanged?.Invoke(CurrentWeek);
            PlayWeekSteps(CurrentWeek);
        }

        /// <summary>UI 등에서 플레이어 행동을 알림 (예: NotifyAction(TutorialActions.OverviewOpened))</summary>
        public void NotifyAction(string actionId)
        {
            if (!IsActive || string.IsNullOrEmpty(actionId)) return;
            foreach (var step in steps)
            {
                if (step.trigger == TutorialTrigger.Action && step.actionId == actionId) PlayStep(step);
            }
        }

        /// <summary>튜토리얼 건너뛰기 (이미 대기열에 들어간 대화는 그대로 나옴)</summary>
        // ─── 세이브 ───

        public InfraTown.Save.TutorialSaveState GetSaveState()
        {
            var s = new InfraTown.Save.TutorialSaveState { active = IsActive, completed = IsCompleted, currentWeek = CurrentWeek };
            foreach (var step in _triggered) if (step != null) s.triggeredStepIds.Add(step.stepId);
            return s;
        }

        /// <summary>
        /// 튜토리얼 진행을 저장 시점으로 되돌림. 진행 중이면 그 주차부터 이어서(이미 나온 단계는 다시 안 나옴), 끝났으면 끝난 상태로
        /// </summary>
        public void ApplySaveState(InfraTown.Save.TutorialSaveState s)
        {
            if (s == null) return;
            Unsubscribe();
            _triggered.Clear();
            foreach (var id in s.triggeredStepIds)
            {
                var step = FindStep(id);
                if (step != null) _triggered.Add(step);
            }
            CurrentStep = null;
            CurrentWeek = Mathf.Max(0, s.currentWeek);
            IsCompleted = s.completed;
            IsActive = s.active && !s.completed;
            if (IsActive) Subscribe(); // 이번 주를 기준으로 다음 주부터 셈
        }

        public void SkipTutorial()
        {
            if (IsCompleted) return;
            Complete();
        }

        /// <summary>한 주 경과 (TimeManager 날짜 변경에서 호출, 테스트에서 직접 호출 가능)</summary>
        public void AdvanceWeek()
        {
            if (!IsActive) return;
            CurrentWeek++;
            if (CurrentWeek > tutorialWeeks) { Complete(); return; }
            OnTutorialWeekChanged?.Invoke(CurrentWeek);
            PlayWeekSteps(CurrentWeek);
        }

        // ─── 내부 ───

        private void PlayWeekSteps(int week)
        {
            foreach (var step in steps)
            {
                if (step.trigger == TutorialTrigger.Day && step.day == week) PlayStep(step);
            }
        }

        private void PlayStep(TutorialStep step)
        {
            if (step == null || _triggered.Contains(step)) return;
            _triggered.Add(step);

            var dialogue = step.dialogue != null ? step.dialogue : CreateDefaultDialogue(step);
            if (dialogue == null || Dialogue == null) return;

            Dialogue.Play(dialogue, step.cutsceneBefore,
                () => { CurrentStep = step; OnStepStarted?.Invoke(step); },
                () => { if (CurrentStep == step) CurrentStep = null; OnStepFinished?.Invoke(step); });
        }

        private void Complete()
        {
            IsActive = false;
            IsCompleted = true;
            Unsubscribe();
            OnTutorialCompleted?.Invoke();
        }

        private TutorialStep FindStep(string stepId)
        {
            foreach (var s in steps) if (s.stepId == stepId) return s;
            return null;
        }

        private void Subscribe()
        {
            if (_time == null && TimeManager.Instance != null)
            {
                _time = TimeManager.Instance;
                _lastDate = (_time.CurrentYear, _time.CurrentMonth, _time.CurrentWeek);
                _time.OnDateChanged += HandleDateChanged;
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
            if (_time != null)
            {
                _time.OnDateChanged -= HandleDateChanged;
                _time.OnSpeedChanged -= HandleSpeedChanged;
                _time = null;
            }
            if (_buildings != null)
            {
                _buildings.OnBuildingPlaced -= HandleBuildingPlaced;
                _buildings = null;
            }
            if (_subscribedDestroy)
            {
                BuildingInstance.OnBuildingDestroyed -= HandleBuildingDestroyed;
                _subscribedDestroy = false;
            }
        }

        private void HandleDateChanged(int year, int month, int day)
        {
            if (_lastDate == (year, month, day)) return;
            _lastDate = (year, month, day);
            AdvanceWeek();
        }

        private void HandleSpeedChanged(GameSpeed speed) => NotifyAction(TutorialActions.SpeedChanged);

        private void HandleBuildingPlaced(BuildingInstance building)
        {
            NotifyAction(TutorialActions.BuildingPlaced);
            if (building == null || building.Data == null) return;
            if (building.Data.category == BuildingType.Housing) NotifyAction(TutorialActions.HousingPlaced);
            if (building.Data.isPowerPole) NotifyAction(TutorialActions.PowerPolePlaced);
        }

        private void HandleBuildingDestroyed(BuildingInstance building) => NotifyAction(TutorialActions.BuildingDemolished);

        // ─── 기본(임시) 문장: 대화 에셋을 넣으면 그것으로 바뀜. {player}는 주인공 이름 ───

        private DialogueData CreateDefaultDialogue(TutorialStep step)
        {
            var lines = DefaultLines(step.stepId);
            if (lines == null) return null;
            return DialogueData.Create($"tutorial_{step.stepId}", true, true, lines);
        }

        private DialogueLine S(string text, string easy) => new DialogueLine(secretarySpeaker, text, easy);
        private DialogueLine P(string text, string easy) => new DialogueLine("{player}", text, easy);

        private DialogueLine[] DefaultLines(string stepId) => stepId switch
        {
            "day1_welcome" => new[]
            {
                S("{player}님, 어서 오세요! 오늘부터 이 마을을 맡아 주실 분이시죠? 저는 옆에서 도와드릴 비서예요.",
                  "{player}님, 반가워요! 저는 비서예요. 제가 도와 드릴게요."),
                P("잘 부탁해요. 뭐부터 하면 될까요?", "잘 부탁해요! 뭐부터 할까요?"),
                S("앞으로 7주 동안 마을을 꾸리는 방법을 하나씩 알려 드릴게요. 이번 주에는 주민이 살 집부터 지어 봐요.",
                  "7주 동안 하나씩 알려 드릴게요. 이번 주에는 집을 지어 봐요."),
                S("아래 건설 메뉴에서 주택을 고르고 빈 땅을 누르면 지어져요. Q, E 키로 건물 방향을 돌릴 수도 있어요.",
                  "건설 메뉴에서 집을 고르고 땅을 누르세요. Q, E 키로 돌릴 수 있어요."),
            },
            "first_building" => new[]
            {
                S("잘하셨어요! 건물을 지을 때마다 건설비가 들고, 매달 유지비도 나가요. 돈이 모자라지 않게 살펴 주세요.",
                  "잘했어요! 건물을 지으면 돈이 들어요. 돈을 잘 살펴봐요."),
            },
            "day2_power" => new[]
            {
                S("이번 주는 전기 이야기예요. 집과 시설은 전기가 있어야 제대로 돌아가요.",
                  "이번 주는 전기를 알아봐요. 건물은 전기가 필요해요."),
                S("전봇대를 세우면 그 둘레 집까지 전기가 닿아요. 전봇대에서 먼 집은 정전이 나서 주민들이 불편해해요.",
                  "전봇대 가까운 집에만 전기가 가요. 전봇대를 세워 주세요."),
                S("전기는 매달 사 와야 해요. 종합 현황판의 전력 슬라이더로 다음 달에 살 양을 정할 수 있어요.",
                  "전기는 매달 사요. 종합 현황판에서 얼마나 살지 정해요."),
                S("가스도 전기처럼 매달 사 와야 해요. 여름에는 냉방으로 전기를, 겨울에는 난방으로 가스를 가장 많이 쓰니 계절에 맞춰 구매량을 바꿔 주세요.",
                  "가스도 매달 사요. 여름엔 전기를, 겨울엔 가스를 많이 써요."),
            },
            "first_power_pole" => new[]
            {
                S("전봇대를 세우셨네요! 이제 근처 집들에 불이 들어올 거예요.", "전봇대를 세웠어요! 이제 집에 불이 켜져요."),
            },
            "day3_money" => new[]
            {
                S("마을 살림은 매달 들어오는 정부 지원금으로 꾸려요. 주민이 많을수록 지원금도 늘어나요.",
                  "매달 나라에서 돈을 받아요. 주민이 많으면 더 많이 받아요."),
                S("지원금이 들어오면 건물 유지비, 전기값, 가스값이 차례로 나가요. 종합 현황판에서 신청할 지원금도 정할 수 있어요.",
                  "받은 돈으로 건물, 전기, 가스 값을 내요. 종합 현황판에서 확인해요."),
            },
            "day4_satisfaction" => new[]
            {
                S("주민 만족도가 높으면 이웃들이 이사 오고, 낮으면 마을을 떠나요.",
                  "주민들이 기분이 좋으면 이사 오고, 나쁘면 떠나요."),
                S("마을회관이나 경로당 같은 시설 가까운 집은 만족도가 올라가요. 주민들이 싫어하는 시설은 집에서 멀리 지어 주세요.",
                  "좋은 시설은 집 가까이, 싫어하는 시설은 멀리 지어요."),
            },
            "day5_access" => new[]
            {
                S("우리 마을에서 가장 중요한 건 '접근성'이에요. 병원·학교·소방서·경찰서가 집에서 가까울수록 주민들이 마음을 놓아요.",
                  "병원, 학교, 소방서, 경찰서가 집에서 가까우면 좋아요."),
                S("이 필수 시설들은 주민이 늘면 지을 수 있게 돼요. 지을 수 있는데도 짓지 않으면 매주 만족도가 떨어지니 꼭 챙겨 주세요.",
                  "지을 수 있게 되면 꼭 지어 주세요. 안 지으면 주민들이 속상해해요."),
            },
            "day6_overview" => new[]
            {
                S("종합 현황판을 열면 인구, 돈, 전기, 만족도를 한눈에 볼 수 있어요. 주민들 불만도 거기에 모여요.",
                  "종합 현황판에서 마을 상태를 한눈에 볼 수 있어요."),
                S("무언가 이상하다 싶으면 종합 현황판부터 열어 보세요.", "궁금하면 종합 현황판을 열어 보세요."),
            },
            "first_demolish" => new[]
            {
                S("건물을 허무셨네요. 집을 허물면 살던 주민들이 갈 곳을 잃어 불만이 생기니 조심해 주세요.",
                  "집을 부수면 주민들이 살 곳이 없어져요. 조심해요."),
            },
            "day7_finish" => new[]
            {
                S("벌써 7주가 지났네요! 이제 기본은 다 아셨어요.", "벌써 7주가 지났어요! 이제 다 배웠어요."),
                S("주민 불만이 석 달 넘게 이어지면 마을이 무너지니 조심하세요. 10년 동안 모두가 살고 싶은 마을을 만들어 주세요!",
                  "주민들이 오래 화나 있으면 안 돼요. 10년 동안 멋진 마을을 만들어요!"),
                P("고마워요. 열심히 해 볼게요!", "고마워요! 열심히 할게요!"),
                S("언제든 도움이 필요하면 불러 주세요, {player}님!", "힘내세요, {player}님!"),
            },
            _ => null
        };
    }
}

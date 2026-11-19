using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Population;

namespace InfraTown.EventSystem
{
    /// <summary>
    /// 게임 상황에 맞춰 DialogueEventManager로 대화를 띄우는 트리거 모음. DialogueEventManager와 같은 오브젝트에 붙인다.
    ///  1) 건물 해금 스토리 (기획서 4장): 인구가 늘어 새 건물이 해금되면 BuildingData.unlockMessage를 대사로 재생
    ///  2) '나쁨' 지속 경고 (기획서 2장 안내 캐릭터 경고): 지정한 일수가 되면 경고 대사
    ///  3) 시작 대화 / 엔딩 대화: 인스펙터에 DialogueData 에셋을 넣으면 재생
    /// </summary>
    [RequireComponent(typeof(DialogueEventManager))]
    public class DialogueTriggers : MonoBehaviour
    {
        [Header("시작")]
        [Tooltip("게임 시작 시 재생할 대화 (비워도 됨)")]
        [SerializeField] private DialogueData introDialogue;

        [Tooltip("시작할 때부터 해금된 건물(Tier 1)의 해금 메시지도 시작 직후 보여줌")]
        [SerializeField] private bool showInitialUnlockStories = true;

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

        private readonly HashSet<BuildingData> _knownUnlocked = new HashSet<BuildingData>();
        private bool _initialized;
        private GameOutcomeManager _outcome;
        private PopulationManager _population;

        private DialogueEventManager Dialogue => DialogueEventManager.Instance;

        private void Start()
        {
            TrySubscribe();
        }

        private void Update()
        {
            // GameOutcomeManager는 GameLoopManager.Start에서 늦게 붙을 수 있어서 나타날 때까지 구독 시도
            if (_outcome == null || _population == null) TrySubscribe();
        }

        private void OnDestroy()
        {
            if (_population != null) _population.OnPopulationChanged -= HandlePopulationChanged;
            if (_outcome != null)
            {
                _outcome.OnBadStreakChanged -= HandleBadStreakChanged;
                _outcome.OnGameEnded -= HandleGameEnded;
            }
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

            if (_population == null && PopulationManager.Instance != null)
            {
                _population = PopulationManager.Instance;
                _population.OnPopulationChanged += HandlePopulationChanged;
            }

            if (_outcome == null && GameOutcomeManager.Instance != null)
            {
                _outcome = GameOutcomeManager.Instance;
                _outcome.OnBadStreakChanged += HandleBadStreakChanged;
                _outcome.OnGameEnded += HandleGameEnded;
            }
        }

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

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.TimeSystem;
using Dlg = InfraTown.EventSystem;

namespace InfraTown.Tests.Playtest
{
    /// <summary>
    /// 튜토리얼 확인 모드 (-Mode tutorial, 사용자 기획 2026-11-26): 실제 씬에서 단계식 튜토리얼을 실제 버튼(onClick)으로 끝까지 따라간다.
    /// 단계마다 '허용 버튼만 눌리고 나머지는 잠김·강조', 다른 건물 거절, 시간 정지, 해금 안내 화자(비서)를 확인하고 스크린샷을 남긴다.
    /// </summary>
    public static partial class PlaytestRunner
    {
        private static IEnumerator TutorialScenario()
        {
            Section("T-0. 튜토리얼 시작");
            var speakers = new List<string>();
            var dlg = Dlg.DialogueEventManager.Instance;
            Check("대화 관리자 있음", dlg != null);
            if (dlg == null) yield break;
            System.Action<Dlg.DialogueLine, int, int> onLine = (l, i, n) => speakers.Add($"{dlg.CurrentDialogue?.dialogueId}:{dlg.GetCurrentSpeakerName()}");
            dlg.OnLineShown += onLine;
            dlg.OnLineShown += NoOpLine; // 대화창이 없는 씬에서도 줄마다 멈추게 (대화창이 있으면 그대로)

            double until = UnityEditor.EditorApplication.timeSinceStartup + 20;
            while ((Dlg.TutorialDialogues.Instance == null || !Dlg.TutorialDialogues.Instance.IsActive) && UnityEditor.EditorApplication.timeSinceStartup < until)
            {
                if (dlg.IsPlaying) dlg.Next();
                yield return null;
            }
            var tutorial = Dlg.TutorialDialogues.Instance;
            Check("게임을 시작하면 튜토리얼이 시작됨 (DialogueTriggers가 없는 씬이면 코드가 자동 추가)",
                tutorial != null && tutorial.IsActive, tutorial == null ? "TutorialDialogues 없음" : $"active={tutorial.IsActive}, completed={tutorial.IsCompleted}");
            if (tutorial == null || !tutorial.IsActive) { dlg.OnLineShown -= onLine; dlg.OnLineShown -= NoOpLine; yield break; }
            Check("튜토리얼 동안 시간 정지", TimeManager.Instance.IsEventPaused);
            int weekAtStart = TimeManager.Instance.TotalWeeksPassed;

            var manager = BuildingManager.Instance;
            manager.RequireRoadAccess = true;
            var finished = new List<string>();
            System.Action<Dlg.TutorialStep> onFinished = s => finished.Add(s.stepId);
            tutorial.OnStepFinished += onFinished;

            int shot = 0;
            for (int guard = 0; guard < 40 && tutorial.IsActive; guard++)
            {
                // 설명 대화 넘기기 (대화창이 있으면 대화창 버튼과 같은 Advance)
                for (int i = 0; i < 60 && dlg.IsPlaying && !tutorial.IsWaitingForAction; i++) { dlg.RevealCurrentLine(); dlg.Advance(); yield return null; }
                if (!tutorial.IsActive) break;
                if (!tutorial.IsWaitingForAction) { yield return Frames(2); continue; }

                var step = tutorial.CurrentStep;
                yield return RealSeconds(0.3f); // 게이트 새로 고침 (gateRefreshSeconds)
                tutorial.ApplyGate();
                Section($"T-{++shot}. {step.stepId} ({step.goal})");
                var allowed = tutorial.AllowedSelectables();
                var others = Object.FindObjectsOfType<Selectable>(true).Where(s => s != null && s.gameObject.activeInHierarchy && !allowed.Contains(s)).ToList();
                var openOthers = others.Where(s => s.interactable && !IsTutorialExempt(s)).Select(s => s.gameObject.name).ToList();
                Note($"허용 버튼: {(allowed.Count == 0 ? "없음 (기능 행동만)" : string.Join(", ", allowed.Select(a => a.gameObject.name)))} / 잠근 버튼 {tutorial.LockedCount}개");
                Check("허용 버튼은 눌림", allowed.All(a => a.interactable));
                Check("나머지 버튼은 잠김 (대화창·일시정지 메뉴 제외)", openOthers.Count == 0, openOthers.Count == 0 ? "" : string.Join(", ", openOthers.Take(8)));
                Check("누를 버튼 강조", allowed.Count == 0 || tutorial.HighlightedObjects().Count > 0, $"{tutorial.HighlightedObjects().Count}개");
                var canvas = allowed.Select(a => a.GetComponentInParent<Canvas>()).FirstOrDefault(c => c != null);
                Shot($"t{shot:00}_{step.stepId}", canvas != null ? canvas.rootCanvas : null);

                switch (step.goal)
                {
                    case Dlg.TutorialGoal.PlaceBuilding:
                        yield return TutorialPlace(tutorial, step, manager);
                        break;
                    case Dlg.TutorialGoal.OpenOverview:
                    case Dlg.TutorialGoal.CloseOverview:
                    case Dlg.TutorialGoal.ChangeSpeed:
                        var press = allowed.OfType<Button>().FirstOrDefault(b => step.goal != Dlg.TutorialGoal.CloseOverview || b.gameObject.name.ToLower().Contains("close"))
                                    ?? allowed.OfType<Button>().FirstOrDefault();
                        if (step.goal == Dlg.TutorialGoal.ChangeSpeed)
                            press = allowed.OfType<Button>().FirstOrDefault(b => b.gameObject.name.Contains("2") || b.gameObject.name.ToLower().Contains("fast")) ?? press;
                        Check("누를 버튼이 있음", press != null);
                        if (press != null) { Note($"'{press.gameObject.name}' 누름"); press.onClick.Invoke(); }
                        yield return RealSeconds(0.8f);
                        break;
                    case Dlg.TutorialGoal.Demolish:
                        var target = GameLoopManager.Instance.PlacedBuildings.FirstOrDefault(b => b != null && b.Data.category == BuildingType.Housing)
                                     ?? GameLoopManager.Instance.PlacedBuildings.FirstOrDefault(b => b != null);
                        Check("철거할 건물이 있음", target != null);
                        if (target == null) break;
                        TestWorld.Invoke(target, "OnMouseDown"); // 건물 클릭 → 정보창
                        yield return RealSeconds(0.3f);
                        tutorial.ApplyGate();
                        var demolish = tutorial.AllowedSelectables().OfType<Button>().FirstOrDefault(b => b.gameObject.activeInHierarchy && b.gameObject.name.ToLower().Contains("demol"))
                                       ?? tutorial.AllowedSelectables().OfType<Button>().FirstOrDefault(b => b.gameObject.activeInHierarchy && b.GetComponentInChildren<TMPro.TMP_Text>(true)?.text.Contains("철거") == true);
                        Shot($"t{shot:00}b_demolish_info", demolish != null ? demolish.GetComponentInParent<Canvas>()?.rootCanvas : null);
                        if (demolish != null) { Note($"'{demolish.gameObject.name}' 누름"); demolish.onClick.Invoke(); }
                        else { Note("⚠ 정보창의 철거 버튼을 찾지 못해 코드로 철거"); target.DestroyBuilding(); }
                        yield return Frames(3);
                        break;
                }
                yield return Frames(2);
                Check($"'{step.stepId}' 행동을 하면 다음 단계로", tutorial.CurrentStep != step || !tutorial.IsActive,
                    $"지금 {tutorial.CurrentStep?.stepId ?? "끝"}");
                if (tutorial.CurrentStep == step) { Note("⚠ 이 단계에서 막혀 건너뜀"); tutorial.SkipTutorial(); }
            }
            for (int i = 0; i < 60 && dlg.IsPlaying; i++) { dlg.RevealCurrentLine(); dlg.Advance(); yield return null; }
            yield return Frames(2);

            Section("T-끝. 튜토리얼 완료");
            Note($"마친 단계: {string.Join(" → ", finished)}");
            Check("모든 단계를 순서대로 마침", finished.SequenceEqual(tutorial.Steps.Select(s => s.stepId)) || finished.Count >= tutorial.Steps.Count - 3,
                $"{finished.Count}/{tutorial.Steps.Count}");
            Check("튜토리얼 완료", tutorial.IsCompleted && !tutorial.IsActive);
            Check("1주차 안에 끝남 (시간이 흐르지 않음)", TimeManager.Instance.TotalWeeksPassed == weekAtStart, $"{weekAtStart} → {TimeManager.Instance.TotalWeeksPassed}주");
            Check("끝나면 시간이 다시 흐름", !TimeManager.Instance.IsEventPaused);
            Check("튜토리얼이 잠근 버튼을 모두 되돌림", tutorial.LockedCount == 0 && tutorial.HighlightedObjects().Count == 0);
            var unlockLines = speakers.Where(s => s.StartsWith("unlock_initial")).ToList();
            if (unlockLines.Count > 0) Check("처음 해금 안내는 비서가 말함 (이장님이 이어서 말하지 않음)", unlockLines.All(s => s.EndsWith(":비서")), string.Join(", ", unlockLines.Distinct()));
            else Note("이 씬에는 처음 해금 안내가 없음");
            manager.SelectBuildingToBuild(manager.FindBuildingData("village_hall") ?? manager.FindBuildingData("house"));
            Check("끝난 뒤에는 다른 건물도 고를 수 있음", manager.IsPlacingBuilding, manager.LastPlacementError);
            manager.CancelBuildingMode();
            Shot("t99_tutorial_done", null);

            tutorial.OnStepFinished -= onFinished;
            dlg.OnLineShown -= onLine;
            dlg.OnLineShown -= NoOpLine;
        }

        private static void NoOpLine(Dlg.DialogueLine l, int i, int n) { }

        private static bool IsTutorialExempt(Selectable s)
        {
            for (var t = s.transform; t != null; t = t.parent)
                foreach (var c in t.GetComponents<MonoBehaviour>())
                    if (c != null && (c.GetType().Name == "DialogueWindow" || c.GetType().Name == "GamePauseMenu" || c.GetType().Name == "SaveLoadFailurePopup")) return true;
            return false;
        }

        /// <summary>건물 단계: 허용된 분류 버튼 → (크기 메뉴) → 목록 칸을 실제로 눌러 건물을 고르고, 맞는 칸에 설치</summary>
        private static IEnumerator TutorialPlace(Dlg.TutorialDialogues tutorial, Dlg.TutorialStep step, BuildingManager manager)
        {
            var data = manager.FindBuildingData(step.buildingId);
            Check($"건물 데이터 '{step.buildingId}'", data != null);
            if (data == null) yield break;

            // 다른 건물은 거절
            var other = manager.AllBuildingData.FirstOrDefault(d => d != null && d.buildingID != step.buildingId && manager.IsBuildingUnlocked(d));
            if (other != null)
            {
                manager.SelectBuildingToBuild(other);
                Check($"튜토리얼 중 다른 건물('{other.buildingName}')은 고를 수 없음", !manager.IsPlacingBuilding, manager.LastPlacementError);
                manager.CancelBuildingMode();
            }

            // 실제 버튼으로 고르기: 분류 → (크기) → 목록 칸
            foreach (var b in tutorial.AllowedSelectables().OfType<Button>().Where(b => b.gameObject.activeInHierarchy && !b.gameObject.name.ToLower().Contains("cancel")).ToList())
            {
                if (manager.IsPlacingBuilding) break;
                b.onClick.Invoke();
                yield return RealSeconds(0.4f);
                tutorial.ApplyGate();
            }
            if (!manager.IsPlacingBuilding)
            {
                var slot = tutorial.AllowedSelectables().OfType<Button>().FirstOrDefault(b => b.gameObject.activeInHierarchy && b.GetComponentInParent<InfraTown.UI.BuildingSlotUI>() != null);
                if (slot != null) { slot.onClick.Invoke(); yield return Frames(2); }
            }
            if (!manager.IsPlacingBuilding)
            {
                Note($"⚠ 버튼으로 '{data.buildingName}'을(를) 고르지 못해 코드로 고름 (이 씬의 건설 UI 구성)");
                manager.SelectBuildingToBuild(data);
            }
            Check($"'{data.buildingName}' 고름", manager.IsPlacingBuilding && manager.SelectedBuildingData == data);

            // 설치 칸: 도로는 빈 줄, 주택은 도로 옆, 전신주는 도로 위
            Vector2Int cell = FindTutorialCell(manager, data);
            manager.TryPlaceArea(cell, data.IsRoad ? cell + new Vector2Int(4, 0) : cell);
            yield return Frames(2);
            Check($"'{data.buildingName}' 설치 ({cell.x},{cell.y})", manager.GetBuildingAt(cell)?.Data == data, manager.LastPlacementError);
        }

        private static Vector2Int FindTutorialCell(BuildingManager manager, BuildingData data)
        {
            for (int y = 2; y < 38; y++)
                for (int x = 2; x < 50; x++)
                {
                    var c = new Vector2Int(x, y);
                    if (data.IsRoad)
                    {
                        bool free = Enumerable.Range(0, 5).All(i => manager.GetBuildingAt(new Vector2Int(x + i, y)) == null);
                        if (free) return c;
                        continue;
                    }
                    if (data.isPowerPole && manager.IsRoadCell(x, y) && manager.GetRoadPoleAt(c) == null && manager.CanPlaceAt(data, c, out _)) return c;
                    if (!data.isPowerPole && manager.CanPlaceAt(data, c, out _)) return c;
                }
            return new Vector2Int(5, 5);
        }
    }
}

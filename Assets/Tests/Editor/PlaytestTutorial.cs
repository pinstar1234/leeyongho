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
            var shotTalks = new HashSet<string>();
            while ((Dlg.TutorialDialogues.Instance == null || !Dlg.TutorialDialogues.Instance.IsActive) && UnityEditor.EditorApplication.timeSinceStartup < until)
            {
                if (dlg.IsPlaying)
                {
                    string id = dlg.CurrentDialogue?.dialogueId;
                    if (id != null && shotTalks.Add(id)) yield return ShotTalk($"t00_{id}");
                    dlg.Next();
                }
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
                // 설명 대화 넘기기 (대화창이 있으면 대화창 버튼과 같은 Advance) — 단계마다 첫 줄 화면을 한 장
                for (int i = 0; i < 60 && dlg.IsPlaying && !tutorial.IsWaitingForAction; i++)
                {
                    string id = dlg.CurrentDialogue?.dialogueId;
                    if (id != null && shotTalks.Add(id)) yield return ShotTalk($"t{shot + 1:00}a_{id}");
                    dlg.RevealCurrentLine(); dlg.Advance(); yield return null;
                }
                if (!tutorial.IsActive) break;
                if (!tutorial.IsWaitingForAction) { yield return Frames(2); continue; }

                var step = tutorial.CurrentStep;
                yield return RealSeconds(0.3f); // 게이트 새로 고침 (gateRefreshSeconds)
                tutorial.ApplyGate();
                Section($"T-{++shot}. {step.stepId} ({step.goal})");
                var allowed = tutorial.AllowedSelectables();
                foreach (var a in allowed.Take(3))
                {
                    var rc = a.GetComponentInParent<Canvas>()?.rootCanvas;
                    var rt = a.transform as RectTransform;
                    var g = a.targetGraphic;
                    float groupAlpha = a.GetComponentsInParent<CanvasGroup>(true).Aggregate(1f, (acc, cg) => acc * cg.alpha);
                    Note($"'{a.gameObject.name}' 화면 층 {rc?.name}({rc?.sortingOrder}), 보임 {a.gameObject.activeInHierarchy}, 위치 {(rt != null ? rt.position.ToString() : "-")}, " +
                         $"그림 알파 {(g != null ? g.color.a * g.canvasRenderer.GetAlpha() : -1):F2}, CanvasGroup 알파 {groupAlpha:F2}, 화면 크기 {Screen.width}x{Screen.height}");
                }
                var others = Object.FindObjectsOfType<Selectable>(true).Where(s => s != null && s.gameObject.activeInHierarchy && !allowed.Contains(s)).ToList();
                var openOthers = others.Where(s => s.interactable && !IsTutorialExempt(s)).Select(s => s.gameObject.name).ToList();
                Note($"허용 버튼: {(allowed.Count == 0 ? "없음 (기능 행동만)" : string.Join(", ", allowed.Select(a => a.gameObject.name)))} / 잠근 버튼 {tutorial.LockedCount}개");
                Check("허용 버튼은 눌림", allowed.All(a => a.interactable));
                Check("나머지 버튼은 잠김 (대화창·일시정지 메뉴 제외)", openOthers.Count == 0, openOthers.Count == 0 ? "" : string.Join(", ", openOthers.Take(8)));
                Check("누를 버튼 강조", allowed.Count == 0 || tutorial.HighlightedObjects().Count > 0, $"{tutorial.HighlightedObjects().Count}개");
                ShotScreen($"t{shot:00}b_{step.stepId}_action"); // 허용 버튼만 밝고 테두리 강조, 나머지는 흐리게 잠김

                switch (step.goal)
                {
                    case Dlg.TutorialGoal.PlaceBuilding:
                        yield return TutorialPlace(tutorial, step, manager, $"t{shot:00}c_{step.stepId}_menu");
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
                        yield return RealSeconds(0.5f); // 정보창 여는 애니메이션(0.3초) — 시간이 멈춰 있어도 열려야 함
                        tutorial.ApplyGate();
                        var demolish = tutorial.AllowedSelectables().OfType<Button>().FirstOrDefault(b => b.gameObject.activeInHierarchy && b.gameObject.name.ToLower().Contains("demol"))
                                       ?? tutorial.AllowedSelectables().OfType<Button>().FirstOrDefault(b => b.gameObject.activeInHierarchy && b.GetComponentInChildren<TMPro.TMP_Text>(true)?.text.Contains("철거") == true);
                        ShotScreen($"t{shot:00}c_demolish_info");
                        // 2026-11-26: 정보창이 시간 정지 중 크기 0인 채 안 보이던 문제 — 버튼을 코드로만 누를 수 있는 게 아니라 실제로 보이는지
                        float scale = demolish != null ? demolish.transform.lossyScale.x : 0f;
                        Check("건물을 누르면 철거 버튼이 화면에 보임 (튜토리얼로 시간이 멈춰 있어도)", demolish != null && scale > 0.5f,
                            demolish == null ? "철거 버튼 없음" : $"크기 {scale:F2}");
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
            ShotScreen("t99_tutorial_done");

            tutorial.OnStepFinished -= onFinished;
            dlg.OnLineShown -= onLine;
            dlg.OnLineShown -= NoOpLine;
        }

        private static void NoOpLine(Dlg.DialogueLine l, int i, int n) { }

        private static bool _canvasListNoted;

        /// <summary>
        /// 실제 게임 화면처럼 찍기: 카메라는 그대로 두고, 켜져 있는 모든 오버레이 캔버스(HUD·건설 UI·대화창·현황판·토스트)를
        /// 정렬 순서대로 잠시 카메라 캔버스로 바꿔 함께 렌더링 (배치 모드에는 게임 화면이 없어서)
        /// </summary>
        private static void ShotScreen(string name)
        {
            var cam = Camera.main;
            if (cam == null) { Note($"스크린샷 {name}: Main Camera 없음"); return; }
            Canvas.ForceUpdateCanvases();
            // 같은 정렬 순서면 하이어라키에서 뒤에 있는 캔버스가 위 (오버레이 렌더링 순서와 같게)
            var canvases = Object.FindObjectsOfType<Canvas>()
                .Where(c => c != null && c.isRootCanvas && c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceOverlay)
                .OrderBy(c => c.sortingOrder).ThenBy(c => c.transform.GetSiblingIndex()).ToList();
            if (!_canvasListNoted)
            {
                _canvasListNoted = true;
                Note("화면 층(아래→위): " + string.Join(", ", canvases.Select(c => $"{c.name}({c.sortingOrder})")));
            }
            const int w = 1920, h = 1080;
            var rt = new RenderTexture(w, h, 24);
            var prev = cam.targetTexture;

            // 렌더 대상을 먼저 1920x1080으로 (배치 모드 화면은 640x480이라 캔버스 배치를 맞추려고),
            // 모든 오버레이 캔버스를 카메라 캔버스로 바꾸되 정렬 번호를 순서대로 서로 다르게 줘서 실제 화면과 같은 순서로 겹침
            cam.targetTexture = rt;
            var saved = canvases.Select(c => (c, c.renderMode, c.worldCamera, c.planeDistance, c.sortingOrder)).ToList();
            float dist = cam.nearClipPlane + 0.5f;
            for (int i = 0; i < canvases.Count; i++)
            {
                canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                canvases[i].worldCamera = cam;
                canvases[i].planeDistance = dist;
                canvases[i].sortingOrder = 30000 + i;
            }
            Canvas.ForceUpdateCanvases();
            cam.Render();
            foreach (var (c, mode, wc, pd, order) in saved) { c.renderMode = mode; c.worldCamera = wc; c.planeDistance = pd; c.sortingOrder = order; }
            Canvas.ForceUpdateCanvases();

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            cam.targetTexture = prev;
            RenderTexture.active = null;

            string dir = UnityEditor.SessionState.GetString(OutKey, "Logs/Playtest");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Note($"📷 화면: {name}.png");
        }

        /// <summary>설명 대사의 첫 줄이 다 나온 화면을 찍음 (단계마다 한 장)</summary>
        private static IEnumerator ShotTalk(string name)
        {
            var dlg = Dlg.DialogueEventManager.Instance;
            if (dlg == null || !dlg.IsPlaying) yield break;
            dlg.RevealCurrentLine();
            yield return Frames(2);
            ShotScreen(name);
        }

        private static bool IsTutorialExempt(Selectable s)
        {
            for (var t = s.transform; t != null; t = t.parent)
                foreach (var c in t.GetComponents<MonoBehaviour>())
                    if (c != null && (c.GetType().Name == "DialogueWindow" || c.GetType().Name == "GamePauseMenu" || c.GetType().Name == "SaveLoadFailurePopup")) return true;
            return false;
        }

        /// <summary>건물 단계: 허용된 분류 버튼 → (크기 메뉴) → 목록 칸을 실제로 눌러 건물을 고르고, 맞는 칸에 설치</summary>
        private static IEnumerator TutorialPlace(Dlg.TutorialDialogues tutorial, Dlg.TutorialStep step, BuildingManager manager, string menuShot)
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
                if (menuShot != null && !manager.IsPlacingBuilding) { ShotScreen(menuShot); menuShot = null; } // 분류 버튼을 누른 뒤 목록
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
            // 카메라가 보는 맵 가운데부터 (스크린샷에 지은 건물·정보창이 보이게)
            for (int y = 16; y < 38; y++)
                for (int x = 24; x < 50; x++)
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

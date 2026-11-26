using System;


namespace InfraTown.Core
{
    /// <summary>
    /// 마우스가 UI 위에 있는지 판정. UI 버튼을 누를 때 그 뒤의 그리드·건물까지 클릭되지 않도록 막는 데 쓴다.
    /// </summary>
    public static class PointerGuard
    {
        /// <summary>테스트에서 판정 결과를 바꿀 때 사용 (null이면 실제 EventSystem 판정)</summary>
        public static Func<bool> OverrideForTests;

        /// <summary>
        /// UI 위이거나 컷씬·대화가 진행 중이면 true — 그리드·건물 클릭을 막는다
        /// (컷씬·대화를 넘기려고 누른 클릭이 건설·건물 정보창으로 새지 않도록)
        /// </summary>
        public static bool IsPointerOverUI()
        {
            if (InfraTown.UI.ConstructionHelpWindow.IsOpen) return true;
            if (InfraTown.EventSystem.CutsceneManager.Instance != null && InfraTown.EventSystem.CutsceneManager.Instance.IsPlaying) return true;
            if (InfraTown.EventSystem.DialogueEventManager.Instance != null && InfraTown.EventSystem.DialogueEventManager.Instance.BlocksWorldInput) return true;
            if (OverrideForTests != null) return OverrideForTests();
            // InfraTown.EventSystem 네임스페이스와 이름이 겹치므로 전체 이름으로 씀
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }
    }
}

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

        public static bool IsPointerOverUI()
        {
            if (OverrideForTests != null) return OverrideForTests();
            // InfraTown.EventSystem 네임스페이스와 이름이 겹치므로 전체 이름으로 씀
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }
    }
}

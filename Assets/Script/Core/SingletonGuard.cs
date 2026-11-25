using UnityEngine;

namespace InfraTown.Core
{
    /// <summary>
    /// 싱글톤 매니저 중복 처리 공용 함수.
    /// 예전처럼 Destroy(gameObject)를 하면 같은 오브젝트의 다른 컴포넌트(예: GridManager 오브젝트의 LightweightGridManager)까지
    /// 함께 사라지므로, 중복된 그 컴포넌트만 끄고 제거한다.
    /// </summary>
    public static class SingletonGuard
    {
        /// <summary>
        /// Awake에서: if (Instance != null && Instance != this) { SingletonGuard.RemoveDuplicate(this, Instance); return; }
        /// </summary>
        public static void RemoveDuplicate(MonoBehaviour duplicate, Object existing)
        {
            Debug.LogWarning($"[SingletonGuard] '{duplicate.gameObject.name}'의 {duplicate.GetType().Name}는 중복이라 제거합니다 " +
                             $"(사용 중: '{(existing is Component c ? c.gameObject.name : existing?.name)}'). 같은 오브젝트의 다른 컴포넌트는 그대로 둡니다.");
            duplicate.enabled = false; // Start/Update가 돌지 않도록

            if (Application.isPlaying) Object.Destroy(duplicate);
            else Object.DestroyImmediate(duplicate); // 에디터 테스트
        }
    }
}

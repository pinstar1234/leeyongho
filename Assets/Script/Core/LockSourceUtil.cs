using System.Collections.Generic;

namespace InfraTown.Core
{
    /// <summary>
    /// 일시정지·패배 유예처럼 '요청한 쪽'을 기록하는 잠금에서, 이미 삭제된 Unity 오브젝트를 정리한다.
    /// 요청한 UI·대화창이 해제 없이 사라지면 잠금이 영원히 남는 문제를 막는다.
    /// </summary>
    public static class LockSourceUtil
    {
        /// <summary>삭제된(또는 null인) 요청자를 지우고 지운 개수를 반환</summary>
        public static int RemoveDestroyed(HashSet<object> sources)
        {
            return sources.RemoveWhere(IsDestroyed);
        }

        public static bool IsDestroyed(object source)
        {
            if (source == null) return true;
            // UnityEngine.Object는 Destroy 후 '== null'이 true가 된다
            return source is UnityEngine.Object unityObject && unityObject == null;
        }
    }
}

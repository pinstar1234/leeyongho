using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace InfraTown.EventSystem
{
    /// <summary>
    /// 컷씬 장면이 시작될 때 씬에서 실행할 일 (버스 도착 애니메이션, 주민 등장, 효과음 등).
    /// 씬 오브젝트에 붙이고 ID와 On Triggered(애니메이터 Play, Timeline Play 등)를 인스펙터에서 연결한다.
    /// 컷씬 장면(CutsceneShot.sceneEventId)에 같은 ID를 쓰면 실행된다.
    /// </summary>
    public class CutsceneSceneEvent : MonoBehaviour
    {
        [SerializeField] private string eventId;
        [SerializeField] private UnityEvent onTriggered = new UnityEvent();

        private static readonly List<CutsceneSceneEvent> All = new List<CutsceneSceneEvent>();

        public string EventId { get => eventId; set => eventId = value; }
        public UnityEvent OnTriggered => onTriggered;

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() => All.Remove(this);

        /// <summary>같은 ID를 가진 모든 이벤트 실행. 실행한 개수 반환</summary>
        public static int Trigger(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            All.RemoveAll(e => e == null);
            int count = 0;
            foreach (var e in All.ToArray())
            {
                if (e.eventId != id) continue;
                e.onTriggered.Invoke();
                count++;
            }
            return count;
        }
    }
}

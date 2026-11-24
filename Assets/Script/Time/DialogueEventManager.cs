using UnityEngine;
using InfraTown.TimeSystem;

namespace InfraTown.EventSystem
{
    public class DialogueEventManager : MonoBehaviour
    {
        public static DialogueEventManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>
        /// 주민 대화나 이벤트 팝업이 열릴 때 호출
        /// </summary>
        public void StartDialogueEvent()
        {
            // TimeManager에 일시정지 요청
            TimeManager.Instance?.RequestEventPause(this);
            Debug.Log("[이벤트 발생] 시간이 일시정지되었습니다.");
        }

        /// <summary>
        /// 대화가 끝나고 팝업이 닫힐 때 호출
        /// </summary>
        public void EndDialogueEvent()
        {
            // TimeManager 일시정지 해제
            TimeManager.Instance?.ReleaseEventPause(this);
            Debug.Log("[이벤트 종료] 시간 흐름이 재개되었습니다.");
        }
    }
}
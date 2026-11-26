using TMPro;
using UnityEngine;
using UIEventSystem = UnityEngine.EventSystems.EventSystem;
using StandaloneInputModule = UnityEngine.EventSystems.StandaloneInputModule;
using InfraTown.UI;

namespace InfraTown.Save
{
    /// <summary>
    /// 저장 파일이 깨져 다시 시도해도 불러오지 못하고 타이틀로 돌아왔을 때 띄우는 임시 안내 창 (사용자 기획 2026-11-26, 사용자 승인 — 씬·프리팹 변경 없이 코드로 그림).
    /// 타이틀 씬이 열리면 SaveSystem이 만들고, 모든 Start가 끝난 뒤 팀원 UI가 SaveSystem.OnLoadFailed를 구독하고 있으면 그 UI에 알리고 사라진다.
    /// 구독한 UI가 없으면 일시정지 메뉴(GamePauseMenu)와 같은 모양의 창을 그린다. '확인'을 누르면 SaveSystem.AcknowledgeLoadFailure
    /// </summary>
    public class SaveLoadFailurePopup : MonoBehaviour
    {
        public const string Title = "불러오기 실패";

        /// <summary>지금 떠 있는 임시 창 (없으면 null)</summary>
        public static SaveLoadFailurePopup Current { get; private set; }

        /// <summary>창에 보이는 문구</summary>
        public string Message { get; private set; }

        private GameObject _canvas;

        /// <summary>보여 줄 안내가 있으면 임시 창 오브젝트를 만든다 (실제로 그릴지는 Start에서 정함)</summary>
        public static void ShowIfNoUI()
        {
            if (SaveSystem.PendingLoadFailureMessage == null || Current != null) return;
            Current = new GameObject("[SaveLoadFailurePopup]").AddComponent<SaveLoadFailurePopup>();
        }

        private void Awake()
        {
            if (Current != null && Current != this) { DestroySelf(); return; }
            Current = this;
        }

        private void Start()
        {
            Message = SaveSystem.PendingLoadFailureMessage;
            if (Message == null) { DestroySelf(); return; }

            // 팀원 UI가 직접 창을 띄우면 그쪽에 맡김 (확인 처리도 그 UI가 AcknowledgeLoadFailure로)
            if (SaveSystem.NotifyLoadFailedUI(Message)) { DestroySelf(); return; }

            Build();
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        /// <summary>확인: 안내를 지우고 창을 닫음</summary>
        public void Close()
        {
            SaveSystem.AcknowledgeLoadFailure();
            DestroySelf();
        }

        /// <summary>창에 그린 캔버스 (그리지 않았으면 null — 팀원 UI가 맡은 경우)</summary>
        public GameObject Canvas => _canvas;

        private void DestroySelf()
        {
            if (Current == this) Current = null;
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject); // 에디터 테스트
        }

        private void Build()
        {
            if (UIEventSystem.current == null)
                new GameObject("EventSystem", typeof(UIEventSystem), typeof(StandaloneInputModule)).transform.SetParent(transform, false);

            _canvas = new GameObject("Load Failure Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            _canvas.transform.SetParent(transform, false);
            var canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000; // 타이틀 메뉴·일시정지 메뉴(1100)보다 위
            var scaler = _canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;

            var backdrop = GamePauseMenu.SetFill(GamePauseMenu.CreateRect(_canvas.transform, "Backdrop"));
            GamePauseMenu.AddImage(backdrop, new Color(0f, 0f, 0f, 0.7f), true); // 뒤 타이틀 버튼을 막음
            var panel = GamePauseMenu.CreatePanel(backdrop, Title, 300);
            var label = GamePauseMenu.CreateLabel(GamePauseMenu.CreateRect(panel, "Message", 350, 130, 0, 5), Message, 18);
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Ellipsis;
            var ok = GamePauseMenu.CreateButton(panel, "확인", -100, Close);
            UIEventSystem.current?.SetSelectedGameObject(ok.gameObject);
        }
    }
}

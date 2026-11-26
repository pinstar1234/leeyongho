using InfraTown.EventSystem;
using InfraTown.Grid;
using InfraTown.TimeSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UIEventSystem = UnityEngine.EventSystems.EventSystem;

namespace InfraTown.UI
{
    public sealed class ConstructionHelpWindow : MonoBehaviour
    {
        [SerializeField] private RectTransform monthSkipButton;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private Sprite paper;
        [SerializeField] private Sprite wood;
        [SerializeField] private Sprite selected;
        [SerializeField] private Sprite unselected;

        private static readonly Color Ink = new Color32(56, 46, 33, 255);
        private static readonly Color Cream = new Color32(255, 244, 214, 255);
        private static ConstructionHelpWindow openWindow;
        public static bool IsOpen => openWindow != null;

        private RectTransform overlay;
        private Button launcher;
        private Button buildingButton;
        private Button dragButton;
        private Button playbackButton;
        private TextMeshProUGUI title;
        private TextMeshProUGUI instructions;
        private TextMeshProUGUI playbackLabel;
        private ConstructionHelpDemo demo;
        private TimeManager pausedTime;
        private bool showingDrag;

        private void Start()
        {
            var root = monthSkipButton.GetComponentInParent<Canvas>().rootCanvas.transform;
            overlay = Layer(root, "Construction Help Overlay", 80);
            Stretch(overlay, Vector2.zero, Vector2.one);
            GamePauseMenu.AddImage(overlay, new Color(0.09f, 0.12f, 0.06f, 0.53f), true);
            BuildPanel();
            overlay.gameObject.SetActive(false);

            var launcherLayer = Layer(monthSkipButton, "Construction Help Launcher", 81);
            launcherLayer.anchorMin = launcherLayer.anchorMax = new Vector2(0.5f, 1f);
            launcherLayer.pivot = new Vector2(0.5f, 0f);
            launcherLayer.anchoredPosition3D = new Vector3(2.5f, 16.5f, 0f);
            launcherLayer.sizeDelta = new Vector2(50f, 50f);
            var circle = launcherLayer.gameObject.AddComponent<HelpCircleGraphic>();
            launcher = launcherLayer.gameObject.AddComponent<Button>();
            launcher.targetGraphic = circle;
            launcher.onClick.AddListener(Toggle);
            Label(launcherLayer, "?", 40f, Cream, TextAlignmentOptions.Center);
        }

        private void LateUpdate()
        {
            launcher.interactable = openWindow == this || CanOpen();
        }

        private void OnDisable()
        {
            if (openWindow == this) Close();
            if (launcher != null) launcher.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (launcher != null) launcher.gameObject.SetActive(true);
        }

        private void OnDestroy()
        {
            if (overlay != null) Destroy(overlay.gameObject);
            if (launcher != null) Destroy(launcher.gameObject);
        }

        public static bool TryCloseOpenWindow()
        {
            if (!IsOpen) return false;
            openWindow.Close();
            return true;
        }

        private static bool CanOpen() =>
            !GamePauseMenu.IsOpen &&
            TimeManager.Instance?.IsSkipping != true &&
            DialogueEventManager.Instance?.BlocksWorldInput != true &&
            CutsceneManager.Instance?.IsPlaying != true;

        private void Toggle()
        {
            if (openWindow == this)
            {
                Close();
                return;
            }
            if (IsOpen || !CanOpen()) return;
            LightweightGridManager.Instance?.CancelDrag();
            openWindow = this;
            pausedTime = TimeManager.Instance;
            pausedTime?.RequestEventPause(this);
            overlay.gameObject.SetActive(true);
            ShowTopic(showingDrag);
            UIEventSystem.current?.SetSelectedGameObject((showingDrag ? dragButton : buildingButton).gameObject);
        }

        private void Close()
        {
            openWindow = null;
            overlay.gameObject.SetActive(false);
            pausedTime?.ReleaseEventPause(this);
            pausedTime = null;
            UIEventSystem.current?.SetSelectedGameObject(launcher.gameObject);
        }

        private void BuildPanel()
        {
            var panel = Region(overlay, "Help Paper", 0.10f, 0.08f, 0.81f, 0.90f);
            var background = AddSprite(panel, paper, Color.white);
            background.type = Image.Type.Sliced;
            Label(Region(panel, "Heading", .045f, .88f, .72f, .97f), "도움말", 56f, Ink, TextAlignmentOptions.MidlineLeft);
            var closeButton = MakeButton(panel, "닫기", .875f, .886f, .952f, .962f, unselected, Ink, 23f, Close);

            var divider = Region(panel, "Wood Divider", .026f, .765f, .974f, .808f);
            AddSprite(divider, wood, new Color32(220, 202, 175, 255));
            var tab = Region(panel, "Construction Tab", .046f, .776f, .228f, .865f);
            AddSprite(tab, selected, Color.white);
            Label(tab, "건축", 36f, Cream, TextAlignmentOptions.Center);

            var line = Region(panel, "Sidebar Divider", .229f, .046f, .231f, .765f);
            GamePauseMenu.AddImage(line, new Color32(192, 166, 121, 255));
            buildingButton = MakeButton(panel, "건축", .037f, .638f, .219f, .731f, unselected, Ink, 30f, () => ShowTopic(false));
            dragButton = MakeButton(panel, "연속건축", .037f, .534f, .219f, .627f, unselected, Ink, 30f, () => ShowTopic(true));
            title = Label(Region(panel, "Topic Title", .255f, .663f, .957f, .739f), "", 43f, Ink, TextAlignmentOptions.MidlineLeft);
            var frame = Region(panel, "Construction Demonstration", .254f, .23f, .958f, .651f);
            GamePauseMenu.AddImage(frame, new Color32(96, 77, 48, 255));
            demo = Region(frame, "Animated Yard", .003f, .009f, .997f, .991f).gameObject.AddComponent<ConstructionHelpDemo>();
            demo.raycastTarget = false;
            playbackButton = MakeButton(frame, "일시정지", .02f, .04f, .19f, .20f, selected, Cream, 23f, TogglePlayback);
            playbackLabel = playbackButton.GetComponentInChildren<TextMeshProUGUI>();
            instructions = Label(Region(panel, "Instructions", .255f, .054f, .959f, .207f), "", 32f, Ink, TextAlignmentOptions.TopLeft);
            GamePauseMenu.LinkNavigation(buildingButton, dragButton, playbackButton, closeButton);
        }

        private void ShowTopic(bool drag)
        {
            showingDrag = drag;
            title.text = drag ? "연속건축" : "건축";
            instructions.text = drag
                ? "건물을 선택한 뒤 마우스 왼쪽 버튼을 누른 채 드래그하세요.\n버튼을 놓으면 건설 가능한 칸에 한꺼번에 지어집니다."
                : "건축 메뉴에서 건물을 선택하세요.\n건설 가능한 칸을 클릭하면 건물 하나를 지을 수 있습니다.";
            SetSelected(buildingButton, !drag);
            SetSelected(dragButton, drag);
            demo.Restart(drag);
            playbackLabel.text = "일시정지";
        }

        private void TogglePlayback()
        {
            demo.Playing = !demo.Playing;
            playbackLabel.text = demo.Playing ? "일시정지" : "재생";
        }

        private void SetSelected(Button button, bool active)
        {
            button.image.sprite = active ? selected : unselected;
            button.GetComponentInChildren<TextMeshProUGUI>().color = active ? Cream : Ink;
        }

        private TextMeshProUGUI Label(RectTransform parent, string text, float size, Color color, TextAlignmentOptions alignment)
        {
            var label = GamePauseMenu.CreateLabel(GamePauseMenu.SetFill(GamePauseMenu.CreateRect(parent, "Label")), text, size);
            label.font = font;
            label.color = color;
            label.alignment = alignment;
            label.enableAutoSizing = true;
            label.fontSizeMin = size * .48f;
            label.fontSizeMax = size;
            return label;
        }

        private Button MakeButton(RectTransform parent, string label, float x0, float y0, float x1, float y1, Sprite sprite, Color textColor, float size, UnityEngine.Events.UnityAction action)
        {
            var rect = Region(parent, label, x0, y0, x1, y1);
            var image = AddSprite(rect, sprite, Color.white, true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            Label(rect, label, size, textColor, TextAlignmentOptions.Center);
            return button;
        }

        private static Image AddSprite(RectTransform rect, Sprite sprite, Color color, bool raycast = false)
        {
            var image = GamePauseMenu.AddImage(rect, color, raycast);
            image.sprite = sprite;
            return image;
        }

        private static RectTransform Layer(Transform parent, string name, int order)
        {
            var rect = GamePauseMenu.CreateRect(parent, name);
            var canvas = rect.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = order;
            rect.gameObject.AddComponent<GraphicRaycaster>();
            return rect;
        }

        private static RectTransform Region(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var rect = GamePauseMenu.CreateRect(parent, name);
            Stretch(rect, new Vector2(x0, y0), new Vector2(x1, y1));
            return rect;
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}

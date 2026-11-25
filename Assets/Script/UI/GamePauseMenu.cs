using System.Collections.Generic;
using InfraTown.Save;
using InfraTown.TimeSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UIEventSystem = UnityEngine.EventSystems.EventSystem;

namespace InfraTown.UI
{
    [DefaultExecutionOrder(-50)]
    public class GamePauseMenu : MonoBehaviour
    {
        [SerializeField] private BuildingUIManager buildingUI;

        private const string SensitivityKey = "InfraTown.MouseSensitivity";
        private const string VolumeKey = "InfraTown.MasterVolume";
        private static readonly Color PanelColor = new Color32(16, 27, 38, 255);
        private static readonly Color ButtonColor = new Color32(44, 67, 87, 255);
        private static readonly Color TextColor = new Color32(247, 240, 230, 255);
        private static readonly Color AccentColor = new Color32(92, 142, 112, 255);

        public static bool IsOpen { get; private set; }
        public static float MouseSensitivity { get; private set; } = 1f;

        private TimeManager _timeManager;
        private GameObject _overlay;
        private RectTransform _menu;
        private RectTransform _settings;
        private Button _resumeButton;
        private Button _settingsButton;
        private Button _backButton;
        private GameObject _previousSelection;
        private RectTransform _saveSlots;
        private RectTransform _loadSlots;
        private bool _loading;

        private void Start()
        {
            MouseSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensitivityKey, 1f), 0.25f, 2f);
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
            BuildMenu();
        }

        private void Update()
        {
            if (_loading || !Input.GetKeyDown(KeyCode.Escape)) return;

            if (!IsOpen) Open();
            else if (!_menu.gameObject.activeSelf) ShowMainMenu();
            else Resume();
        }

        private void Open()
        {
            _timeManager = TimeManager.Instance;
            if (_timeManager == null) return;

            _previousSelection = UIEventSystem.current?.currentSelectedGameObject;
            buildingUI?.CancelConstruction();
            _timeManager.RequestEventPause(this);
            IsOpen = true;
            _overlay.SetActive(true);
            UIEventSystem.current?.SetSelectedGameObject(_resumeButton.gameObject);
        }

        private void Resume()
        {
            _overlay.SetActive(false);
            ReleasePause();
            UIEventSystem.current?.SetSelectedGameObject(
                _previousSelection != null && _previousSelection.activeInHierarchy ? _previousSelection : null);
            _previousSelection = null;
        }

        private void ReleasePause()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (_timeManager != null) _timeManager.ReleaseEventPause(this);
            _timeManager = null;
            PlayerPrefs.Save();
        }

        private void OnDestroy()
        {
            ReleasePause();
        }

        private void ShowSettings(bool show)
        {
            _menu.gameObject.SetActive(!show);
            _settings.gameObject.SetActive(show);
            UIEventSystem.current?.SetSelectedGameObject((show ? _backButton : _settingsButton).gameObject);
        }

        private void ShowMainMenu()
        {
            _saveSlots.gameObject.SetActive(false);
            _loadSlots.gameObject.SetActive(false);
            ShowSettings(false);
        }

        private void BuildMenu()
        {
            _overlay = new GameObject("Pause Menu Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            _overlay.SetActive(false);
            _overlay.transform.SetParent(transform, false);
            var canvas = _overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1100;
            var scaler = _overlay.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var backdrop = SetFill(CreateRect(_overlay.transform, "Backdrop"));
            AddImage(backdrop, new Color(0f, 0f, 0f, 0.64f), true);
            _menu = CreatePanel(backdrop, "일시정지");
            _resumeButton = CreateButton(_menu, "게임 재개", 96, Resume);
            var saveButton = CreateButton(_menu, "저장", 38, () => ShowSlots(_saveSlots, true));
            var loadButton = CreateButton(_menu, "불러오기", -20, () => ShowSlots(_loadSlots, false));
            _settingsButton = CreateButton(_menu, "게임 설정", -78, () => ShowSettings(true));
            var quitButton = CreateButton(_menu, "게임 종료", -136, QuitGame);
            LinkNavigation(_resumeButton, saveButton, loadButton, _settingsButton, quitButton);

            _settings = CreatePanel(backdrop, "게임 설정");
            var sensitivity = CreateSlider(_settings, "마우스 감도", 88, 0.25f, 2f, MouseSensitivity, value =>
            {
                MouseSensitivity = value;
                PlayerPrefs.SetFloat(SensitivityKey, value);
            });
            var volume = CreateSlider(_settings, "전체 음량", -10, 0f, 1f, AudioListener.volume, value =>
            {
                AudioListener.volume = value;
                PlayerPrefs.SetFloat(VolumeKey, value);
            });
            _backButton = CreateButton(_settings, "뒤로", -136, () => ShowSettings(false));
            LinkNavigation(sensitivity, volume, _backButton);
            _settings.gameObject.SetActive(false);
            _saveSlots = CreatePanel(backdrop, "저장", 476);
            _loadSlots = CreatePanel(backdrop, "불러오기", 476);
            _saveSlots.gameObject.SetActive(false);
            _loadSlots.gameObject.SetActive(false);
        }

        private void ShowSlots(RectTransform panel, bool saving, string message = "")
        {
            const string rowsName = "Slot Controls";
            var existing = panel.Find(rowsName);
            if (existing != null)
            {
                existing.gameObject.SetActive(false);
                Destroy(existing.gameObject);
            }

            _menu.gameObject.SetActive(false);
            panel.gameObject.SetActive(true);
            var rows = CreateRect(panel, rowsName);
            var status = CreateLabel(CreateRect(rows, "Status", 350, 52, 0, -102), message, 17);
            status.overflowMode = TextOverflowModes.Ellipsis;
            int selectedSlot = 0;
            var confirm = CreateButton(rows, saving ? "저장 확인" : "불러오기 확인", -155, () =>
            {
                if (selectedSlot == 0 || _loading) return;
                bool success = saving ? SaveSystem.SaveGame(selectedSlot) : SaveSystem.ContinueGame(selectedSlot);
                if (!success)
                {
                    status.text = SaveSystem.LastError ?? "작업을 완료하지 못했습니다.";
                    return;
                }

                if (saving)
                {
                    ShowSlots(panel, true, $"{selectedSlot}번 슬롯 저장 완료");
                }
                else
                {
                    _loading = true;
                    PlayerPrefs.Save();
                }
            });
            confirm.interactable = false;
            var navigation = new List<Selectable>();
            foreach (var slot in SaveSystem.GetAllSlots())
            {
                string summary = slot.isEmpty ? "빈 슬롯" : slot.isBroken ? "읽을 수 없음" :
                    $"{slot.summary.year}년 {slot.summary.month}월 {slot.summary.week}주";
                var button = CreateButton(rows, $"{slot.slot}번   {summary}", 122 - (slot.slot - 1) * 44, () =>
                {
                    selectedSlot = slot.slot;
                    status.text = saving
                        ? slot.isEmpty ? $"{selectedSlot}번 슬롯에 저장할까요?" : $"{selectedSlot}번 저장을 덮어쓸까요?"
                        : "현재 진행 상황을 저장하지 않고 불러올까요?";
                    confirm.interactable = true;
                    LinkNavigation(navigation.FindAll(control => control.interactable).ToArray());
                    UIEventSystem.current?.SetSelectedGameObject(confirm.gameObject);
                }, 38);
                button.interactable = saving || (!slot.isEmpty && !slot.isBroken);
                if (button.interactable) navigation.Add(button);
            }
            navigation.Add(confirm);
            navigation.Add(CreateButton(rows, "뒤로", -207, ShowMainMenu));
            var available = navigation.FindAll(control => control.interactable).ToArray();
            LinkNavigation(available);
            UIEventSystem.current?.SetSelectedGameObject(available[0].gameObject);
        }

        private static RectTransform CreatePanel(Transform parent, string title, float height = 380)
        {
            var panel = CreateRect(parent, title, 400, height);
            AddImage(panel, PanelColor);
            CreateLabel(CreateRect(panel, "Title", 320, 48, 0, height / 2 - 44), title, 32);
            return panel;
        }

        private static Button CreateButton(Transform parent, string title, float y, UnityEngine.Events.UnityAction action, float height = 46)
        {
            var rect = CreateRect(parent, title, 310, height, 0, y);
            var image = AddImage(rect, ButtonColor, true);
            var label = CreateLabel(SetFill(CreateRect(rect, "Label")), title, 21);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.interactable = action != null;
            if (action != null) button.onClick.AddListener(action);
            else label.color = new Color32(180, 193, 203, 255);
            return button;
        }

        private static Slider CreateSlider(Transform parent, string title, float y, float min, float max,
            float initial, System.Action<float> apply)
        {
            var caption = CreateLabel(CreateRect(parent, title, 310, 28, 0, y + 25), title, 20);
            var track = CreateRect(parent, title + " Slider", 310, 30, 0, y - 8);
            AddImage(CreateRect(track, "Track", 310, 8), ButtonColor);
            var travel = SetFill(CreateRect(track, "Travel"));
            travel.offsetMin = new Vector2(12, 0);
            travel.offsetMax = new Vector2(-12, 0);
            var fill = SetFill(CreateRect(travel, "Fill"));
            fill.offsetMin = new Vector2(0, 11);
            fill.offsetMax = new Vector2(0, -11);
            AddImage(fill, AccentColor);
            var handle = CreateRect(travel, "Handle", 24, 28);
            var handleImage = AddImage(handle, TextColor, true);
            AddImage(track, Color.clear, true);

            var slider = track.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(initial);
            caption.text = $"{title}  {initial:P0}";
            slider.onValueChanged.AddListener(value =>
            {
                caption.text = $"{title}  {value:P0}";
                apply(value);
            });
            return slider;
        }

        private static void LinkNavigation(params Selectable[] controls)
        {
            for (int i = 0; i < controls.Length; i++)
            {
                controls[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = controls[(i + controls.Length - 1) % controls.Length],
                    selectOnDown = controls[(i + 1) % controls.Length]
                };
            }
        }

        private static RectTransform CreateRect(Transform parent, string name, float width = 0,
            float height = 0, float x = 0, float y = 0)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, y);
            return rect;
        }

        private static RectTransform SetFill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Image AddImage(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static TextMeshProUGUI CreateLabel(RectTransform rect, string text, float size)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.text = text;
            label.fontSize = size;
            label.color = TextColor;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }

        private static void QuitGame()
        {
            PlayerPrefs.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

using System.Collections.Generic;
using InfraTown.Save;
using InfraTown.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UIEventSystem = UnityEngine.EventSystems.EventSystem;

public class MainMenuManager : MonoBehaviour
{
    public string nextSceneName = "GameScene";
    [SerializeField] private Button startButton;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button loadButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private TextMeshProUGUI continueLabel;
    [SerializeField] private Image menuShade;

    private Button[] _buttons;
    private RectTransform _overlay;
    private RectTransform _settings;
    private RectTransform _loadSlots;
    private Button _settingsBack;
    private Selectable _firstLoadControl;
    private Button _modalButton;
    private TextMeshProUGUI _status;
    private bool _openingGame;

    private void Start()
    {
        GamePauseMenu.ReadSettings();
        if (startButton == null || continueButton == null || loadButton == null ||
            settingsButton == null || exitButton == null) return;

        _buttons = new[] { startButton, continueButton, loadButton, settingsButton, exitButton };
        continueButton.interactable = SaveSystem.HasResume();
        if (continueLabel != null && !continueButton.interactable)
            continueLabel.color = new Color32(197, 204, 189, 255);
        continueButton.onClick.AddListener(ContinueGame);
        loadButton.onClick.AddListener(() => OpenPanel(_loadSlots, loadButton, _firstLoadControl));
        settingsButton.onClick.AddListener(() => OpenPanel(_settings, settingsButton, _settingsBack));
        GamePauseMenu.LinkNavigation(new List<Selectable>(_buttons).FindAll(button => button.interactable).ToArray());

        var canvas = startButton.GetComponentInParent<Canvas>();
        _overlay = GamePauseMenu.SetFill(GamePauseMenu.CreateRect(canvas.transform, "Title Dialogs"));
        GamePauseMenu.AddImage(_overlay, new Color(0f, 0f, 0f, 0.62f), true);
        _settings = GamePauseMenu.CreateSettingsPanel(_overlay, ClosePanel, out _settingsBack);
        BuildLoadPanel();
        _overlay.gameObject.SetActive(false);

        var rect = GamePauseMenu.CreateRect(startButton.transform.parent, "Title Status", 380, 52);
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-224, -266);
        _status = GamePauseMenu.CreateLabel(rect, "", 19);
    }

    private void Update()
    {
        if (!_openingGame && _overlay != null && _overlay.gameObject.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            ClosePanel();
    }

    public void OnClickStart()
    {
        if (_openingGame) return;
        _openingGame = true;
        SaveSystem.ClearPendingLoad();
        SaveSystem.SetCurrentSlot(1);
        Time.timeScale = 1f;
        SceneManager.LoadScene(nextSceneName);
    }

    public void OnClickExit() => GamePauseMenu.ExitApplication();

    private void ContinueGame()
    {
        if (_openingGame) return;
        _openingGame = SaveSystem.ContinueResume();
        if (!_openingGame) _status.text = SaveSystem.LastError;
    }

    private void BuildLoadPanel()
    {
        _loadSlots = GamePauseMenu.CreatePanel(_overlay, "불러오기", 460);
        var status = GamePauseMenu.CreateLabel(
            GamePauseMenu.CreateRect(_loadSlots, "Load Status", 340, 55, 0, -114), "", 17);
        var navigation = new List<Selectable>();
        foreach (var slot in SaveSystem.GetAllSlots())
        {
            string summary = slot.isEmpty ? "빈 슬롯" : slot.isBroken ? "읽을 수 없음" :
                $"{slot.summary.year}년 {slot.summary.month}월 {slot.summary.week}주";
            var button = GamePauseMenu.CreateButton(_loadSlots, $"{slot.slot}번   {summary}",
                116 - (slot.slot - 1) * 44, () =>
                {
                    if (_openingGame) return;
                    _openingGame = SaveSystem.ContinueGame(slot.slot);
                    if (!_openingGame) status.text = SaveSystem.LastError;
                }, 38);
            button.interactable = !slot.isEmpty && !slot.isBroken;
            if (button.interactable) navigation.Add(button);
        }
        if (navigation.Count == 0) status.text = "직접 저장한 슬롯이 없어요.";
        navigation.Add(GamePauseMenu.CreateButton(_loadSlots, "뒤로", -179, ClosePanel));
        _firstLoadControl = navigation[0];
        GamePauseMenu.LinkNavigation(navigation.ToArray());
    }

    private void OpenPanel(RectTransform panel, Button opener, Selectable firstControl)
    {
        foreach (var button in _buttons) button.gameObject.SetActive(false);
        if (menuShade != null) menuShade.gameObject.SetActive(false);
        _status.text = "";
        _modalButton = opener;
        _settings.gameObject.SetActive(panel == _settings);
        _loadSlots.gameObject.SetActive(panel == _loadSlots);
        _overlay.gameObject.SetActive(true);
        UIEventSystem.current?.SetSelectedGameObject(firstControl.gameObject);
    }

    private void ClosePanel()
    {
        PlayerPrefs.Save();
        _overlay.gameObject.SetActive(false);
        foreach (var button in _buttons) button.gameObject.SetActive(true);
        if (menuShade != null) menuShade.gameObject.SetActive(true);
        UIEventSystem.current?.SetSelectedGameObject(_modalButton.gameObject);
    }
}

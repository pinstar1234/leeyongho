using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InfraTown.EventSystem
{
    public sealed class DialogueWindow : MonoBehaviour
    {
        [SerializeField] private DialogueEventManager manager;
        [SerializeField] private GameObject window;
        [SerializeField] private TMP_Text speakerLabel;
        [SerializeField] private Image portrait;
        [SerializeField] private ScrollRect bodyScroll;
        [SerializeField] private TMP_Text bodyText;
        [SerializeField] private Button skipButton;
        [SerializeField] private Button autoButton;
        [SerializeField] private TMP_Text autoLabel;
        [SerializeField] private Sprite autoOnSprite;

        private int lineShownFrame = -1;
        private Sprite autoOffSprite;

        private void Awake()
        {
            autoOffSprite = autoButton.image.sprite;
            window.SetActive(false);
        }

        private void OnEnable()
        {
            manager.OnLineShown += HandleLineShown;
            manager.OnVisibleTextChanged += UpdateVisibleText;
            manager.OnAutoModeChanged += UpdateAutoButton;
            manager.OnDialogueEnded += HideWindow;
            skipButton.onClick.AddListener(SkipDialogue);
            autoButton.onClick.AddListener(ToggleAuto);
            UpdateAutoButton(manager.IsAutoMode);
        }

        private void OnDisable()
        {
            manager.OnLineShown -= HandleLineShown;
            manager.OnVisibleTextChanged -= UpdateVisibleText;
            manager.OnAutoModeChanged -= UpdateAutoButton;
            manager.OnDialogueEnded -= HideWindow;
            skipButton.onClick.RemoveListener(SkipDialogue);
            autoButton.onClick.RemoveListener(ToggleAuto);
            window.SetActive(false);
        }

        private void Update()
        {
            if (!CanUseDialogueControls() || lineShownFrame == Time.frameCount) return;

            // Enter와 마우스 클릭은 기본 입력을 사용하는 매니저에서 처리한다.
            if (Input.GetKeyDown(KeyCode.Space) ||
                (!manager.HandleInput && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))))
                manager.Advance();
        }

        private void HandleLineShown(DialogueLine line, int index, int total)
        {
            if (!window.activeSelf)
            {
                UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
                window.SetActive(true);
            }

            lineShownFrame = Time.frameCount;
            string speaker = manager.GetCurrentSpeakerName();
            speakerLabel.text = string.IsNullOrWhiteSpace(speaker) ? "이야기" : speaker;

            portrait.sprite = manager.RightPortrait;
            portrait.color = manager.RightPortraitColor;
            portrait.gameObject.SetActive(portrait.sprite != null);

            bodyText.text = manager.GetCurrentText();
            bodyText.maxVisibleCharacters = int.MaxValue;
            bodyText.ForceMeshUpdate();
            UpdateVisibleText(manager.GetVisibleText());
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)bodyScroll.transform);
            bodyScroll.StopMovement();
            bodyScroll.verticalNormalizedPosition = 1f;
        }

        private void UpdateVisibleText(string visibleText)
        {
            // TMP skips markup and treats surrogate pairs as one character.
            var info = bodyText.textInfo;
            int count = 0;
            while (count < info.characterCount &&
                   info.characterInfo[count].index + info.characterInfo[count].stringLength <= visibleText.Length)
                count++;
            bodyText.maxVisibleCharacters = count;
        }

        private void UpdateAutoButton(bool enabled)
        {
            autoLabel.text = enabled ? "Auto ON" : "Auto";
            autoButton.image.sprite = enabled && autoOnSprite != null ? autoOnSprite : autoOffSprite;
        }

        private bool CanUseDialogueControls() => window.activeSelf && !manager.IsHeldByMenu;

        private void SkipDialogue()
        {
            if (CanUseDialogueControls()) manager.Skip();
        }

        private void ToggleAuto()
        {
            if (CanUseDialogueControls()) manager.ToggleAutoMode();
        }

        private void HideWindow(DialogueData dialogue) => window.SetActive(false);
    }
}

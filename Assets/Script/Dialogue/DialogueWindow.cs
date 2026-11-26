using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InfraTown.EventSystem
{
    public sealed class DialogueWindow : MonoBehaviour
    {
        [SerializeField] private DialogueEventManager manager;
        [SerializeField] private GameObject window;
        [SerializeField] private Image speakerBadge;
        [SerializeField] private TMP_Text speakerLabel;
        [SerializeField] private Image portrait;
        [SerializeField] private ScrollRect bodyScroll;
        [SerializeField] private TMP_Text bodyText;
        [SerializeField] private Button advanceButton;
        [SerializeField] private TMP_Text advanceLabel;

        private int lineShownFrame = -1;
        private bool isLastLine;
        private Color npcBadgeColor;

        private void Awake()
        {
            npcBadgeColor = speakerBadge.color;
            window.SetActive(false);
        }

        private void OnEnable()
        {
            manager.OnLineShown += ShowLine;
            manager.OnVisibleTextChanged += UpdateVisibleText;
            manager.OnLineFullyShown += UpdateAdvanceLabel;
            manager.OnDialogueEnded += HideWindow;
            advanceButton.onClick.AddListener(Advance);
        }

        private void OnDisable()
        {
            manager.OnLineShown -= ShowLine;
            manager.OnVisibleTextChanged -= UpdateVisibleText;
            manager.OnLineFullyShown -= UpdateAdvanceLabel;
            manager.OnDialogueEnded -= HideWindow;
            advanceButton.onClick.RemoveListener(Advance);
            window.SetActive(false);
        }

        private void Update()
        {
            if (!window.activeSelf)
                return;

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
                Input.GetKeyDown(KeyCode.KeypadEnter))
                Advance();
        }

        private void ShowLine(DialogueLine line, int index, int total)
        {
            if (!window.activeSelf)
            {
                UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
                window.SetActive(true);
            }

            lineShownFrame = Time.frameCount;
            isLastLine = index == total - 1;
            string speaker = manager.GetCurrentSpeakerName();
            speakerLabel.text = string.IsNullOrWhiteSpace(speaker) ? "이야기" : speaker;
            speakerBadge.color = manager.CurrentSide == DialogueSide.Left
                ? new Color32(202, 220, 228, 255)
                : npcBadgeColor;

            portrait.sprite = manager.GetPortrait(line);
            bool hasPortrait = portrait.sprite != null;
            portrait.gameObject.SetActive(hasPortrait);
            float textLeft = hasPortrait ? 246f : 48f;
            var badgeRect = speakerBadge.rectTransform;
            badgeRect.anchoredPosition = new Vector2(textLeft, badgeRect.anchoredPosition.y);
            var viewport = bodyScroll.GetComponent<RectTransform>();
            viewport.offsetMin = new Vector2(textLeft, viewport.offsetMin.y);

            bodyText.text = manager.GetCurrentText();
            bodyText.maxVisibleCharacters = int.MaxValue;
            bodyText.ForceMeshUpdate();
            UpdateVisibleText(manager.GetVisibleText());
            LayoutRebuilder.ForceRebuildLayoutImmediate(viewport);
            bodyScroll.StopMovement();
            bodyScroll.verticalNormalizedPosition = 1f;
            UpdateAdvanceLabel();
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

        private void UpdateAdvanceLabel()
        {
            advanceLabel.text = isLastLine && manager.IsLineFullyShown ? "닫기" : "다음";
        }

        private void Advance()
        {
            if (!InfraTown.UI.GamePauseMenu.IsOpen && lineShownFrame != Time.frameCount)
            {
                lineShownFrame = Time.frameCount;
                manager.Advance();
            }
        }

        private void HideWindow(DialogueData dialogue) => window.SetActive(false);
    }
}

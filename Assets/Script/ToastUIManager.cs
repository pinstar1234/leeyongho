using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

namespace InfraTown.UI
{
    public class ToastUIManager : MonoBehaviour
    {
        public static ToastUIManager Instance { get; private set; }

        [Header("Toast UI 요소")]
        [SerializeField] private RectTransform toastRect;      // 토스트 배경 패널의 RectTransform
        [SerializeField] private CanvasGroup toastCanvasGroup; // 투명도 제어용 CanvasGroup
        [SerializeField] private Image backgroundImage;        // 배경 색상 변경용 Image
        [SerializeField] private TextMeshProUGUI messageText;  // 알림 텍스트

        [Header("색상 및 연출 설정")]
        [SerializeField] private Color warningColor = new Color(0.85f, 0.2f, 0.2f, 0.95f); // 경고(빨강)
        [SerializeField] private Color infoColor = new Color(0.15f, 0.25f, 0.4f, 0.95f);   // 일반 안내(남색)
        [SerializeField] private float displayDuration = 1.6f; // 떠 있는 시간

        private Vector2 _targetPos;
        private Sequence _toastSequence;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (toastRect != null)
            {
                _targetPos = toastRect.anchoredPosition;
                toastRect.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 화면 상단에 토스트 메시지를 띄웁니다. (isWarning = true면 빨간 배경 + 흔들림 효과)
        /// </summary>
        public void ShowToast(string message, bool isWarning = true)
        {
            if (toastRect == null || messageText == null) return;

            _toastSequence?.Kill();
            toastRect.gameObject.SetActive(true);

            messageText.text = message;
            if (backgroundImage != null)
            {
                backgroundImage.color = isWarning ? warningColor : infoColor;
            }

            // 시작 위치: 목표 위치보다 80픽셀 위쪽에서 투명하게 시작
            toastRect.anchoredPosition = _targetPos + new Vector2(0f, 80f);
            toastRect.localScale = Vector3.one * 0.9f;
            if (toastCanvasGroup != null) toastCanvasGroup.alpha = 0f;

            _toastSequence = DOTween.Sequence();
            _toastSequence.SetUpdate(true); // 일시정지 상태에서도 정상 작동

            // 1. 스르륵 내려오며 선명해짐 (0.25초)
            _toastSequence.Append(toastRect.DOAnchorPos(_targetPos, 0.25f).SetEase(Ease.OutBack));
            _toastSequence.Join(toastRect.DOScale(Vector3.one, 0.25f).SetEase(Ease.OutBack));
            if (toastCanvasGroup != null)
            {
                _toastSequence.Join(toastCanvasGroup.DOFade(1f, 0.2f));
            }

            // 경고 메시지일 경우 좌우로 가볍게 떨리는 피드백 추가
            if (isWarning)
            {
                _toastSequence.Append(toastRect.DOShakeAnchorPos(0.25f, new Vector2(15f, 0f), 25, 90f, false, true));
            }

            // 2. 지정된 시간만큼 대기
            _toastSequence.AppendInterval(displayDuration);

            // 3. 다시 위로 올라가며 사라짐 (0.25초)
            _toastSequence.Append(toastRect.DOAnchorPos(_targetPos + new Vector2(0f, 60f), 0.25f).SetEase(Ease.InCubic));
            if (toastCanvasGroup != null)
            {
                _toastSequence.Join(toastCanvasGroup.DOFade(0f, 0.2f));
            }

            _toastSequence.OnComplete(() =>
            {
                toastRect.gameObject.SetActive(false);
            });
        }
    }
}
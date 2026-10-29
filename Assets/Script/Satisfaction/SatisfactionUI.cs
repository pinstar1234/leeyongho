using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace InfraTown.Satisfaction
{
    public class SatisfactionUI : MonoBehaviour
    {
        [Header("UI Text Components")]
        [SerializeField] private TextMeshProUGUI satisfactionText; // 퍼센티지 표기용 (예: "70%")

        [Header("UI Image Component (얼굴 표정 이미지)")]
        [SerializeField] private Image faceImage;                 // 표정 이미지가 들어갈 UI Image 레퍼런스

        [Header("Satisfaction Face Sprites (티어별 이미지)")]
        [SerializeField] private Sprite desolateSprite;   // 0~20% (황폐화)
        [SerializeField] private Sprite stagnantSprite;   // 21~40% (정체)
        [SerializeField] private Sprite developingSprite; // 41~60% (발전 중)
        [SerializeField] private Sprite stableSprite;     // 61~80% (안정)
        [SerializeField] private Sprite prosperousSprite; // 81~100% (활기찬)

        private void Start()
        {
            if (SatisfactionManager.Instance != null)
            {
                SatisfactionManager.Instance.OnSatisfactionChanged += UpdateUI;

                // 초기 데이터 적용 (현재 마을 평균 만족도 및 티어 수신)
                UpdateUI(SatisfactionManager.Instance.CurrentAverageSatisfaction, SatisfactionManager.Instance.CurrentTier);
            }
        }

        private void OnDestroy()
        {
            if (SatisfactionManager.Instance != null)
            {
                SatisfactionManager.Instance.OnSatisfactionChanged -= UpdateUI;
            }
        }

        /// <summary>
        /// 만족도 수치 및 티어 갱신 시 호출되는 이벤트 함수
        /// </summary>
        private void UpdateUI(float score, SatisfactionTier tier)
        {
            int scoreInt = Mathf.RoundToInt(score);

            // 1. 퍼센티지 텍스트 갱신
            if (satisfactionText != null)
            {
                satisfactionText.text = $"{scoreInt}%";
            }

            // 2. 표정 Sprite 이미지 갱신
            if (faceImage != null)
            {
                Sprite targetSprite = GetFaceSpriteByTier(tier);
                if (targetSprite != null)
                {
                    faceImage.sprite = targetSprite;
                    faceImage.gameObject.SetActive(true);
                }
            }
        }

        /// <summary>
        /// 티어 단계별 매핑할 표정 Sprite 반환
        /// </summary>
        private Sprite GetFaceSpriteByTier(SatisfactionTier tier)
        {
            return tier switch
            {
                SatisfactionTier.Desolate => desolateSprite,
                SatisfactionTier.Stagnant => stagnantSprite,
                SatisfactionTier.Developing => developingSprite,
                SatisfactionTier.Stable => stableSprite,
                SatisfactionTier.Prosperous => prosperousSprite,
                _ => developingSprite
            };
        }
    }
}
using UnityEngine;

namespace InfraTown.Building
{
    public class HousingInstance : MonoBehaviour
    {
        public const float BaseSatisfaction = 70f; // 기본 시작 만족도 70%

        /// <summary>
        /// 이 주택의 최종 산출 만족도 (0~100)
        /// </summary>
        public float CurrentSatisfaction { get; private set; } = BaseSatisfaction;

        /// <summary>
        /// 주변 건물들로부터 수신한 만족도 가감 수치를 기반으로 개별 만족도 재계산
        /// </summary>
        public void RecalculateSatisfaction(float totalOffset)
        {
            CurrentSatisfaction = Mathf.Clamp(BaseSatisfaction + totalOffset, 0f, 100f);
        }
    }
}
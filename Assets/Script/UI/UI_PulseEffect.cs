using UnityEngine;
using DG.Tweening; // DOTween 사용

public class UI_PulseEffect : MonoBehaviour
{
    [Header("효과 설정")]
    [Tooltip("커질 때의 최대 크기 비율 (기본 1.1 = 10% 커짐)")]
    public float targetScale = 1.05f;

    [Tooltip("한 번 커지거나 작아지는데 걸리는 시간 (초)")]
    public float duration = 1.5f;

    private void Start()
    {
        // 원래 크기 저장
        Vector3 originalScale = transform.localScale;

        // 크기를 목표 크기로 duration(초) 동안 변경
        transform.DOScale(originalScale * targetScale, duration)
            .SetEase(Ease.InOutSine)   // 부드럽게 시작하고 부드럽게 끝남
            .SetLoops(-1, LoopType.Yoyo); // -1: 무한반복, Yoyo: 커졌다 작아졌다 왕복
    }

    private void OnDestroy()
    {
        // 오브젝트가 파괴될 때 찌꺼기 애니메이션 정리
        transform.DOKill();
    }
}
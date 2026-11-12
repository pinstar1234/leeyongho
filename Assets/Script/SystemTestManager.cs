using UnityEngine;
using InfraTown.Economy;
using InfraTown.Population;
using InfraTown.Power;
using InfraTown.UI;

namespace InfraTown.Testing
{
    public class SystemTestManager : MonoBehaviour
    {
        [Header("테스트 참조 (OverviewUI)")]
        [SerializeField] private OverviewUI overviewUI;

        private void Update()
        {
            // [Key 1] 종합 현황 UI 토글 (시간 정지 연동 검증)
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                if (overviewUI != null)
                {
                    if (overviewUI.gameObject.activeSelf)
                        overviewUI.HideOverview();
                    else
                        overviewUI.ShowOverview();

                    Debug.Log("[Test] 1번 키: 전체 현황 UI 토글");
                }
            }

            // [Key 2] 인구 및 지원금 테스트
            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                Debug.Log("[Test] 2번 키: 인구/지원금 연동 테스트 진행 중");
            }

            // [Key 3] 전력 부족 상황 연출 (목표 전기 구매량 = 0)
            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                if (PowerManager.Instance != null)
                {
                    PowerManager.Instance.SetTargetPurchasedPower(0);
                    Debug.LogWarning("[Test] 3번 키: 목표 전기 구매량 0kW 설정 -> 전력 부족 및 정전 유도!");
                }
            }

            // [Key 4] 전력 정상 복구 (목표 전기 구매량 = 300)
            if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                if (PowerManager.Instance != null)
                {
                    PowerManager.Instance.SetTargetPurchasedPower(300);
                    Debug.Log("[Test] 4번 키: 목표 전기 구매량 300kW 설정 -> 전력 복구!");
                }
            }

            // [Key 5] 강제 월 정산 실행
            if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                if (EconomyManager.Instance != null)
                {
                    EconomyManager.Instance.ProcessMonthlySettlement();
                    Debug.Log("[Test] 5번 키: 월 정산 강제 실행 완료!");
                }
            }
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(10, 10, 320, 150), "=== 시스템 기능 테스트 단축키 ===");
            GUI.Label(new Rect(20, 35, 300, 20), "[1 키] : 종합 현황 UI 열기 / 닫기");
            GUI.Label(new Rect(20, 55, 300, 20), "[2 키] : 인구 및 지원금 상승 테스트");
            GUI.Label(new Rect(20, 75, 300, 20), "[3 키] : 강제 정전 시뮬레이션 (구매 전력 = 0)");
            GUI.Label(new Rect(20, 95, 300, 20), "[4 키] : 전력 복구 (구매 전력 = 300kW)");
            GUI.Label(new Rect(20, 115, 300, 20), "[5 키] : 월 정산 강제 실행 (수입/지출 처리)");
        }
    }
}
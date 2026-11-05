using System;
using System.Collections.Generic;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Economy;
using InfraTown.TimeSystem;

namespace InfraTown.Power
{
    public class PowerManager : MonoBehaviour
    {
        public static PowerManager Instance { get; private set; }

        [Header("전력 구매 설정 (1kW당 가격)")]
        [SerializeField] private int pricePerPowerUnit = 10;     // 1kW당 10원
        [SerializeField] private int targetPurchasedPower = 200; // 매달 자동 구매할 전력량 (플레이어 설정값)

        [Header("현재 전력 현황")]
        [SerializeField] private int currentAvailablePower = 0; // 현재 보유/소유 중인 전력
        [SerializeField] private int currentUsedPower = 0;      // 현재 사용 중인 총 필요 전력량

        public int CurrentAvailablePower => currentAvailablePower;
        public int CurrentUsedPower => currentUsedPower;
        public int TargetPurchasedPower => targetPurchasedPower;

        // 설치된 전체 전봇대 리스트
        private List<UtilityPoleInstance> _allPoles = new List<UtilityPoleInstance>();

        // 정전으로 지정된 전봇대 리스트
        private List<UtilityPoleInstance> _blackedOutPoles = new List<UtilityPoleInstance>();

        // 전력 데이터 변동 이벤트 (사용 중 전력, 소유 전력)
        public event Action<int, int> OnPowerDataChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnMonthlyTick += ProcessMonthlyPowerPurchase;
            }
        }

        private void OnDestroy()
        {
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnMonthlyTick -= ProcessMonthlyPowerPurchase;
            }
        }

        /// <summary>
        /// 플레이어가 UI를 통해 매달 구매할 전력 목표량을 설정
        /// </summary>
        public void SetTargetPurchasedPower(int amount)
        {
            targetPurchasedPower = Mathf.Max(0, amount);
            OnPowerDataChanged?.Invoke(currentUsedPower, currentAvailablePower);
        }

        /// <summary>
        /// 1달 초일마다 자동 호출: 지정한 수량만큼 외부에서 전기를 구매 (지출 발생)
        /// </summary>
        private void ProcessMonthlyPowerPurchase()
        {
            int totalCost = targetPurchasedPower * pricePerPowerUnit;

            // 자금 확인 및 차감
            if (EconomyManager.Instance != null)
            {
                if (EconomyManager.Instance.TrySpendMoney(totalCost))
                {
                    currentAvailablePower = targetPurchasedPower;
                    Debug.Log($"[PowerManager] 월초 전력 구매 완료: {targetPurchasedPower}kW (비용: -{totalCost}원)");
                }
                else
                {
                    // 자금 부족 시 전력 구매 실패 (소유 전력 0)
                    currentAvailablePower = 0;
                    Debug.LogWarning("[PowerManager] 자금 부족으로 월초 전력 구매에 실패했습니다!");
                }
            }

            // 전력 수급 상태 평가 및 정전 여부 판단
            EvaluatePowerSupply();
        }

        /// <summary>
        /// 건물이 건설/파괴되거나 전력 구매 후 전체 건물 필요 전력량을 재계산하고 정전 여부를 검사
        /// </summary>
        public void RecalculatePowerUsage(List<BuildingInstance> allBuildings)
        {
            int totalRequired = 0;
            _allPoles.Clear();

            foreach (var building in allBuildings)
            {
                if (building == null || building.Data == null) continue;

                totalRequired += building.Data.requiredPower;

                // 전봇대 컴포넌트 수집
                if (building.TryGetComponent<UtilityPoleInstance>(out var pole))
                {
                    _allPoles.Add(pole);
                }
            }

            currentUsedPower = totalRequired;
            EvaluatePowerSupply();
        }

        /// <summary>
        /// 필요 전력이 소유 전력보다 많을 경우 전봇대 1~2개를 차단(정전)시킴
        /// </summary>
        private void EvaluatePowerSupply()
        {
            // 기존 정전 해제
            RestoreAllPoles();

            // 필요 전력 > 소유 전력 상태인 경우
            if (currentUsedPower > currentAvailablePower)
            {
                if (_allPoles.Count > 0)
                {
                    // 1~2개의 전봇대를 무작위로 선택하여 정전 발생
                    int blackoutCount = UnityEngine.Random.Range(1, Mathf.Min(3, _allPoles.Count + 1));

                    List<UtilityPoleInstance> tempPool = new List<UtilityPoleInstance>(_allPoles);

                    for (int i = 0; i < blackoutCount; i++)
                    {
                        if (tempPool.Count == 0) break;

                        int randomIndex = UnityEngine.Random.Range(0, tempPool.Count);
                        UtilityPoleInstance targetPole = tempPool[randomIndex];

                        targetPole.SetPowerState(false); // 정전 발생
                        _blackedOutPoles.Add(targetPole);
                        tempPool.RemoveAt(randomIndex);
                    }

                    Debug.LogWarning($"[PowerManager] 전력 부족! 필요: {currentUsedPower}kW / 소유: {currentAvailablePower}kW -> 전봇대 {blackoutCount}개 정전 발생!");
                }
            }

            OnPowerDataChanged?.Invoke(currentUsedPower, currentAvailablePower);
        }

        private void RestoreAllPoles()
        {
            foreach (var pole in _blackedOutPoles)
            {
                if (pole != null)
                {
                    pole.SetPowerState(true);
                }
            }
            _blackedOutPoles.Clear();
        }

        /// <summary>
        /// 정전으로 인한 만족도 감점 총합 반환
        /// </summary>
        public float GetPowerBlackoutSatisfactionPenalty()
        {
            float totalPenalty = 0f;
            foreach (var pole in _blackedOutPoles)
            {
                if (pole != null)
                {
                    totalPenalty += pole.GetPenalty();
                }
            }
            return totalPenalty;
        }
    }
}
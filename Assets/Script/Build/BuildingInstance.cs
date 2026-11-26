using UnityEngine;
using System;

namespace InfraTown.Building
{
    public class BuildingInstance : MonoBehaviour
    {
        public BuildingData Data { get; private set; }
        public Vector2Int OriginGridPos { get; private set; }

        /// <summary>설치 시 플레이어가 돌린 Y축 회전 (0/90/180/270, 프리팹 원래 회전 제외)</summary>
        public float RotationY { get; private set; }

        private float _lastRightClickTime = 0f;
        private const float DoubleClickThreshold = 0.3f; // 더블클릭 인정 시간 (0.3초)

        public static event Action<BuildingInstance> OnBuildingDestroyed;

        public void Init(BuildingData data, Vector2Int gridPos, float rotationY = 0f, int variantIndex = 0)
        {
            Data = data;
            OriginGridPos = gridPos;
            RotationY = rotationY;
            VariantIndex = data != null ? data.ClampVariant(variantIndex) : 0;
        }

        /// <summary>설치한 모양 번호 (BuildingData.variants, 모양이 하나면 0 — 2026-11-26 도로 모양)</summary>
        public int VariantIndex { get; private set; }

        /// <summary>설치한 모양 이름 (없으면 빈 문자열)</summary>
        public string VariantName => Data != null ? Data.GetVariantName(VariantIndex) : "";


        private void OnMouseDown()
        {
            // 마우스 왼쪽 버튼 클릭 시 UI 호출. UI 위를 누른 클릭은 건물로 넘기지 않음
            if (InfraTown.Core.PointerGuard.IsPointerOverUI()) return;

            // 건설 모드 중에는 건물 정보 창을 띄우지 않음 (설치 클릭이 옆 건물 클릭으로 처리되지 않도록)
            if (BuildingManager.Instance != null && BuildingManager.Instance.IsBuildMode) return;

            // 배드엔딩(게임 오버) 뒤에는 건물 정보 창(철거 버튼)을 띄우지 않음 (사용자 기획 2026-11-24)
            if (!InfraTown.Core.GameOutcomeManager.CanModifyTownNow) return;

            if (InfraTown.UI.BuildingUIManager.Instance != null)
            {
                InfraTown.UI.BuildingUIManager.Instance.OnBuildingClicked(this);
            }
        }

        //private void OnMouseOver()
        //{
        //    // 우클릭 더블클릭 검출
        //    if (Input.GetMouseButtonDown(1))
        //    {
        //        float timeSinceLastClick = Time.time - _lastRightClickTime;

        //        if (timeSinceLastClick <= DoubleClickThreshold)
        //        {
        //            DestroyBuilding();
        //        }

        //        _lastRightClickTime = Time.time;
        //    }
        //}

        /// <summary>지금 철거할 수 있는지 (배드엔딩 게임 오버면 false, 세이브 복원 중은 항상 true) — UI가 [철거] 버튼을 끄는 데 쓴다</summary>
        public static bool CanDemolishNow => InfraTown.Core.GameOutcomeManager.CanModifyTownNow || InfraTown.Save.SaveSystem.IsApplying;

        /// <summary>
        /// UI [철거] 버튼용 (사용자 2026-11-25): 철거할 수 있으면 철거하고 true, 게임 오버면 아무것도 하지 않고 false.
        /// Destroy(gameObject)를 직접 부르면 게임 오버 뒤에도 철거되므로 이것을 쓴다
        /// </summary>
        public bool TryDemolish()
        {
            if (!CanDemolishNow) return false;
            DestroyBuilding();
            return true;
        }

        /// <summary>
        /// 철거. 배드엔딩(게임 오버) 뒤에는 철거하지 않는다 (사용자 기획 2026-11-24, GameOutcomeManager.CanModifyTown).
        /// 세이브 복원 중(SaveSystem.IsApplying)에는 씬에 있던 건물을 바꾸기 위해 항상 철거
        /// </summary>
        public void DestroyBuilding()
        {
            if (!CanDemolishNow)
            {
                Debug.LogWarning($"[BuildingInstance] 게임이 끝나 '{(Data != null ? Data.buildingName : name)}'을(를) 철거할 수 없습니다.");
                return;
            }
            NotifyDestroyed();
            Destroy(gameObject);
        }

        // ─── 철거 알림 보장 ───
        // UI 등에서 Destroy(gameObject)를 직접 불러도 점유 칸·인구·접근성·유지비가 갱신되도록
        // OnDestroy에서도 한 번만 알린다. 씬 전환/게임 종료로 사라지는 경우는 알리지 않는다.

        private bool _destroyNotified;
        private static bool _applicationQuitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetQuitFlag()
        {
            _applicationQuitting = false;
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        private static void OnApplicationQuitting() => _applicationQuitting = true;

        private void OnDestroy()
        {
            if (_applicationQuitting || !gameObject.scene.isLoaded) return;
            NotifyDestroyed();
        }

        private void NotifyDestroyed()
        {
            if (_destroyNotified || Data == null) return;
            _destroyNotified = true;
            OnBuildingDestroyed?.Invoke(this);
        }
    }
}
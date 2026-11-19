using System.Collections.Generic;
using UnityEngine;

namespace InfraTown.EventSystem
{
    /// <summary>
    /// 컷씬 카메라 위치 표시. 빈 오브젝트에 붙이고 카메라가 놓일 위치·방향으로 돌려 둔 뒤 ID를 정한다.
    /// 컷씬 장면(CutsceneShot.cameraPointId)에 같은 ID를 쓰면 카메라가 이 위치로 움직인다.
    /// </summary>
    public class CutsceneCameraPoint : MonoBehaviour
    {
        [Tooltip("컷씬 장면에서 부를 ID (예: opening_road, opening_village)")]
        [SerializeField] private string pointId;

        [Tooltip("시야각(FOV). 0이면 카메라 시야각을 바꾸지 않음")]
        [SerializeField] private float fieldOfView = 0f;

        private static readonly List<CutsceneCameraPoint> All = new List<CutsceneCameraPoint>();

        public string PointId { get => pointId; set => pointId = value; }
        public float FieldOfView { get => fieldOfView; set => fieldOfView = value; }

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() => All.Remove(this);

        /// <summary>ID로 찾기 (없으면 null)</summary>
        public static CutsceneCameraPoint Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            All.RemoveAll(p => p == null);
            foreach (var p in All) if (p.pointId == id) return p;
            return null;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, 0.5f);
            Gizmos.DrawRay(transform.position, transform.forward * 3f);
        }
    }
}

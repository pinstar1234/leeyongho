using UnityEngine;

namespace InfraTown.Save
{
    /// <summary>
    /// 이어하기로 씬이 열렸을 때 저장 데이터를 적용하는 일회용 오브젝트.
    /// 모든 매니저의 Start(첫 달 전력 구매 등)가 끝난 뒤, 첫 프레임의 다른 Update(대화 시작 등)보다 먼저 적용한다.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class SaveGameApplier : MonoBehaviour
    {
        private void Update()
        {
            SaveSystem.ApplyPending();
            Destroy(gameObject);
        }
    }
}

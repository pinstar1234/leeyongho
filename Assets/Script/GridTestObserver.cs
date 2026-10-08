using UnityEngine;
using InfraTown.Grid;

public class LightweightGridTester : MonoBehaviour
{
    private void Start()
    {
        if (LightweightGridManager.Instance != null)
        {
            LightweightGridManager.Instance.OnGridClicked += (gridPos) =>
            {
                Debug.Log($"[그리드 클릭 성공] 타일 좌표: ({gridPos.x}, {gridPos.y}) / 월드 위치: {LightweightGridManager.Instance.GetWorldPosition(gridPos.x, gridPos.y)}");
            };
        }
    }
}
using UnityEngine;
using UnityEngine.SceneManagement; // 씬 전환(LoadScene)을 사용하기 위해 반드시 추가해야 합니다.

public class MainMenuManager : MonoBehaviour
{
    [Header("이동할 씬 이름")]
    [Tooltip("Start 버튼을 누르면 이동할 씬의 이름을 정확히 적어주세요.")]
    public string nextSceneName = "GameScene"; // 인스펙터에서 수정 가능

    /// <summary>
    /// Start 버튼을 눌렀을 때 실행될 함수
    /// </summary>
    public void OnClickStart()
    {
        Debug.Log($"'{nextSceneName}' 씬으로 이동합니다.");
        // 지정한 이름의 씬을 불러옵니다.
        SceneManager.LoadScene(nextSceneName);
    }

    /// <summary>
    /// Exit 버튼을 눌렀을 때 실행될 함수
    /// </summary>
    public void OnClickExit()
    {
        Debug.Log("게임을 종료합니다.");

        // 1. 실제 빌드된 게임(PC, 모바일 등)에서 애플리케이션을 종료합니다.
        Application.Quit();

        // 2. 유니티 에디터 환경에서 테스트할 때 플레이 모드를 정지시킵니다.
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
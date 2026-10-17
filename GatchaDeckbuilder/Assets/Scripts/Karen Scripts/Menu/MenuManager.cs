using Unity.Netcode;
using UnityEngine;

public class MenuManager : MonoBehaviour
{
    public string tutorialSceneName = "Tutorial";
    public string AISceneName = "AIMode";
    public string loginSceneName = "Login";
    public GameObject menuPanel;
    public GameObject playPanel;
    public GameObject cardPanel;

    public void OpenPlayPanel()
    {
        playPanel.SetActive(true);
        menuPanel.SetActive(false);
    }
    public void ClosePlayPanel()
    {
        menuPanel.SetActive(true);
        playPanel.SetActive(false);
    }

    public void OpenCardPanel()
    {
        cardPanel.SetActive(true);
        menuPanel.SetActive(false);
    }
    public void CloseCardPanel()
    {
        menuPanel.SetActive(true);
        cardPanel.SetActive(false);
    }
    public void LoadTutorial()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(tutorialSceneName);
    }
    public void LoadAI()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(AISceneName);
    }
    public void LoadLogin()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(loginSceneName);
    }
    public void QuitGame()
    {
        Debug.Log("Quitting game...");

        Time.timeScale = 1f;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
        {
            NetworkManager.Singleton.Shutdown();
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}

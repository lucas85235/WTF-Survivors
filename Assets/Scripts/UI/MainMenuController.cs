using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles main menu buttons: Play, Continue, Options, Credits, Exit.
/// Attach this to the MainMenu GameObject (panel).
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("Scenes")]
    [Tooltip("Name of the world map or first gameplay scene")]
    public string worldMapSceneName = "WorldMapScene";

    public void OnPlayPressed()
    {
        // Start a new run: go to world map
        SceneManager.LoadScene(worldMapSceneName);
    }

    public void OnContinuePressed()
    {
        // Example: continue last session (you can expand saving system here)
        PlayerPrefs.SetInt("continue_exists", 1);
        SceneManager.LoadScene(worldMapSceneName);
    }

    public void OnOptionsPressed()
    {
        // open options panel - assume it's already in scene and will be enabled
        GameObject options = GameObject.Find("OptionsPanel");
        if (options) options.SetActive(true);
    }

    public void OnCreditsPressed()
    {
        SceneManager.LoadScene("CreditsScene");
    }

    public void OnExitPressed()
    {
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #else
        Application.Quit();
        #endif
    }
}

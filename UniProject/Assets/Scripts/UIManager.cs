using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages the UI of the application.
/// </summary>
public class UIManager : MonoBehaviour
{
    public GameObject pauseMenuUI;

    private bool isPaused = false;

    /// <summary>
    /// Called at the beginning of the execution.
    /// Ensure the pause menu is hidden by default in the main menu
    /// </summary>
    void Start()
    {
        if (pauseMenuUI != null)
        {
            pauseMenuUI.SetActive(false);
        }
    }

    /// <summary>
    /// Called every frame.
    /// Checks for "Escape" button press.
    /// </summary>
    void Update()
    {
        // Toggle pause menu on Escape key press
        if (SceneManager.GetActiveScene().buildIndex == 1 && Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused)
            {
                Resume();
            }
            else
            {
                Pause();
            }
        }
    }

    /// <summary>
    /// Loads the game scene.
    /// </summary>
    public void PlayGame()
    {
        SceneManager.LoadScene(1);
    }

    /// <summary>
    /// Quits the application.
    /// </summary>
    public void QuitGame()
    {
        Application.Quit();
    }

    /// <summary>
    /// Pauses the game and brings up the pause menu.
    /// </summary>
    public void Pause()
    {
        pauseMenuUI.SetActive(true);
        Time.timeScale = 0f;
        isPaused = true;
    }

    /// <summary>
    /// Resumes the game and closes the pause menu.
    /// </summary>
    public void Resume()
    {
        pauseMenuUI.SetActive(false);
        Time.timeScale = 1f;
        isPaused = false;
    }

    /// <summary>
    /// Returns to the main screen.
    /// </summary>
    public void QuitToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(0);
    }
}

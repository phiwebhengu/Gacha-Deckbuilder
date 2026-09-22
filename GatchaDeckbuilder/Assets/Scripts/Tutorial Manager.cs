using UnityEngine;
using UnityEngine.UI;

public class TutorialUIManager : MonoBehaviour
{
    [Header("UI Panel References")]
    [Tooltip("The parent GameObject holding the tutorial/instructions layout.")]
    [SerializeField] private GameObject howToPlayPanel;

    [Header("Button References")]
    [Tooltip("The main menu or HUD button used to open the tutorial panel.")]
    [SerializeField] private Button openHowToPlayButton;
    [Tooltip("Optional close button inside the tutorial panel.")]
    [SerializeField] private Button closeHowToPlayButton;

    private void Awake()
    {
        // Ensure the panel starts hidden
        if (howToPlayPanel != null)
        {
            howToPlayPanel.SetActive(false);
        }

        // Setup button listeners
        if (openHowToPlayButton != null)
        {
            openHowToPlayButton.onClick.AddListener(OpenTutorial);
        }

        if (closeHowToPlayButton != null)
        {
            closeHowToPlayButton.onClick.AddListener(CloseTutorial);
        }
    }

    private void Update()
    {
        // Listen for the Escape key to close or toggle off the tutorial panel
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (howToPlayPanel != null && howToPlayPanel.activeSelf)
            {
                CloseTutorial();
            }
        }
    }

    /// <summary>
    /// Opens the How To Play panel.
    /// </summary>
    public void OpenTutorial()
    {
        if (howToPlayPanel != null)
        {
            howToPlayPanel.SetActive(true);
        }
    }

    /// <summary>
    /// Closes the How To Play panel.
    /// </summary>
    public void CloseTutorial()
    {
        if (howToPlayPanel != null)
        {
            howToPlayPanel.SetActive(false);
        }
    }

    /// <summary>
    /// Toggles the tutorial panel visibility directly.
    /// </summary>
    public void ToggleTutorial()
    {
        if (howToPlayPanel != null)
        {
            howToPlayPanel.SetActive(!howToPlayPanel.activeSelf);
        }
    }
}
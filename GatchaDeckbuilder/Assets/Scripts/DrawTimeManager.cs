using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DrawTimerManager : MonoBehaviour
{
    [Header("Timer Settings")]
    [SerializeField] private float drawWindowDuration = 5f;
    [SerializeField] private float pulseSpeed = 8f;
    [SerializeField] private float pulseScaleAmount = 0.15f;

    [Header("UI References")]
    [SerializeField] private Button startDrawButton;
    [SerializeField] private Image radialTimerImage;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private RectTransform timerContainer;

    [Header("System References")]
    [SerializeField] private TokenSelectionManager tokenManager;
    [SerializeField] private List<DeckButton> availableDecks = new List<DeckButton>();

    private float currentTimer;
    private bool isTimerRunning = false;
    private bool playerHasDrawn = false;
    private Vector3 originalTimerScale = Vector3.one;

    [Header("AI Rival Integration")]
    [SerializeField] private AIRivalController aiRival;

    private void Awake()
    {
        if (timerContainer != null)
        {
            originalTimerScale = timerContainer.localScale;
            timerContainer.gameObject.SetActive(false);
        }

        if (startDrawButton != null)
        {
            startDrawButton.onClick.AddListener(StartDrawPhase);
        }

        if (tokenManager == null)
        {
            tokenManager = FindObjectOfType<TokenSelectionManager>();
        }

        if (tokenManager != null)
        {
            tokenManager.gameObject.SetActive(false);
        }

        UpdateTimerUI(1f, drawWindowDuration);
    }

    public void StartDrawPhase()
    {
        playerHasDrawn = false;
        currentTimer = drawWindowDuration;
        isTimerRunning = true;

        if (startDrawButton != null)
        {
            startDrawButton.gameObject.SetActive(false);
        }

        if (timerContainer != null)
        {
            timerContainer.gameObject.SetActive(true);
        }

        if (tokenManager != null)
        {
            tokenManager.gameObject.SetActive(true);
        }

        // Start AI rival turn alongside player
        if (aiRival != null)
        {
            aiRival.StartAIDrawPhase();
        }

        StartCoroutine(Routine_RunTimer());
    }

    private IEnumerator Routine_RunTimer()
    {
        // Loop runs for the entire 5 seconds regardless of how many draws happen
        while (currentTimer > 0f)
        {
            currentTimer -= Time.deltaTime;
            float fillRatio = Mathf.Clamp01(currentTimer / drawWindowDuration);

            UpdateTimerUI(fillRatio, currentTimer);

            if (timerContainer != null)
            {
                float pulse = 1f + (Mathf.Sin(Time.time * pulseSpeed) * pulseScaleAmount * (1f - fillRatio));
                timerContainer.localScale = originalTimerScale * pulse;
            }

            yield return null;
        }

        // Window closes strictly when time hits 0
        EndDrawPhase();
    }

    // Called whenever cards are drawn—flags that the player used the window
    public void NotifyCardsDrawn()
    {
        if (isTimerRunning)
        {
            playerHasDrawn = true;
            Debug.Log("[Draw Timer] Player drew cards! Selection window remains open until time runs out.");
        }
    }

    private void EndDrawPhase()
    {
        isTimerRunning = false;

        if (timerContainer != null)
        {
            timerContainer.localScale = originalTimerScale;
            timerContainer.gameObject.SetActive(false);
        }

        if (tokenManager != null)
        {
            tokenManager.gameObject.SetActive(false);
        }

        // Stop AI turn actions when window closes
        if (aiRival != null)
        {
            aiRival.StopAIDrawPhase();
        }

        if (!playerHasDrawn)
        {
            Debug.Log("[Draw Timer] Time expired with zero selections! Executing random auto-draw penalty.");
            ExecuteAutoDrawPenalty();
        }

        UpdateTimerUI(0f, 0f);
    }

    private void ExecuteAutoDrawPenalty()
    {
        if (tokenManager == null || availableDecks.Count == 0) return;

        int randomIndex = Random.Range(0, availableDecks.Count);
        DeckButton chosenDeck = availableDecks[randomIndex];

        // Enable player token manager to execute the penalty deal
        tokenManager.gameObject.SetActive(true);
        tokenManager.ForceAutoDrawSingleToken(chosenDeck);

        // NOTE: Do NOT set Active(false) here! 
        // Setting active to false instantly kills the dealing Coroutine before cards instantiate.
    }

    private void UpdateTimerUI(float fillRatio, float timeRemaining)
    {
        if (radialTimerImage != null)
        {
            radialTimerImage.fillAmount = fillRatio;
        }

        if (timerText != null)
        {
            timerText.text = Mathf.CeilToInt(Mathf.Max(0f, timeRemaining)).ToString();
        }
    }
}
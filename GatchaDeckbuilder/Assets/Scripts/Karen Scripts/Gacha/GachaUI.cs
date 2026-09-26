using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class GachaUI : MonoBehaviour
{
    [Header("Token Display")]
    public TMP_Text tokenText;
    public string tokenFormat = "Tokens: {0}";

    [Header("Pull Buttons")]
    public Button pullActionButton;
    public Button pullSupportButton;

    [Header("Opponent Visuals")]
    public GameObject cardBackPrefab;
    public Transform OpponentCardContainer;

    [Header("System References")]
    public DrawTimerManager drawTimerManager;

    // ADD THIS: Reference to the hand manager so we can delegate card spawning to it
    public PlayerHandManager playerHandManager;

    private GachaManager mgr;
    private int currentTokens = 0;
    private bool isDrawPhaseActive = true;

    private void OnEnable() => StartCoroutine(SubscribeWhenReady());

    private void OnDisable()
    {
        StopAllCoroutines();
        if (mgr != null)
        {
            mgr.OnMyTokensChanged -= UpdateTokens;
            // REMOVED: mgr.OnMyPullResolved -= HandlePullResolved;
            mgr.OnPullFailed -= HandlePullFailed;
            mgr.OnOpponentDrewCard -= HandleOpponentDrewCard;
        }
        if (drawTimerManager != null)
        {
            drawTimerManager.OnDrawPhaseChanged -= UpdateButtonInteractability;
        }
    }

    private IEnumerator SubscribeWhenReady()
    {
        while (mgr == null || !mgr.IsSpawned)
        {
            mgr = FindFirstObjectByType<GachaManager>();
            yield return null;
        }

        mgr.OnMyTokensChanged += UpdateTokens;
        // REMOVED: mgr.OnMyPullResolved += HandlePullResolved; (PlayerHandManager handles this now)
        mgr.OnPullFailed += HandlePullFailed;
        mgr.OnOpponentDrewCard += HandleOpponentDrewCard;

        currentTokens = mgr.GetLocalPlayerTokens();
        if (tokenText != null) tokenText.text = string.Format(tokenFormat, currentTokens);

        if (drawTimerManager != null)
        {
            drawTimerManager.OnDrawPhaseChanged += UpdateButtonInteractability;
            isDrawPhaseActive = drawTimerManager.IsDrawPhaseActive;
        }

        UpdateButtonInteractability(isDrawPhaseActive);
    }

    private void UpdateTokens(int tokens)
    {
        currentTokens = tokens;
        if (tokenText != null) tokenText.text = string.Format(tokenFormat, tokens);
        UpdateButtonInteractability(isDrawPhaseActive);
    }

    private void UpdateButtonInteractability(bool phaseActive)
    {
        isDrawPhaseActive = phaseActive;
        bool timerAllowsPull = (drawTimerManager == null) || isDrawPhaseActive;
        bool canPull = currentTokens > 0 && timerAllowsPull;

        if (pullActionButton != null) pullActionButton.interactable = canPull;
        if (pullSupportButton != null) pullSupportButton.interactable = canPull;
    }

    // REMOVED: HandlePullResolved entirely. PlayerHandManager is now the single source of truth for spawning.

    private void HandlePullFailed(string reason)
    {
        Debug.LogWarning($"Pull failed: {reason}");
        if (pullActionButton != null) pullActionButton.interactable = true;
        if (pullSupportButton != null) pullSupportButton.interactable = true;
    }

    private void HandleOpponentDrewCard(ulong opponentClientId)
    {
        if (cardBackPrefab != null && OpponentCardContainer != null)
        {
            Instantiate(cardBackPrefab, OpponentCardContainer);
        }
    }

    public void OnClickPullAction()
    {
        if (pullActionButton != null) pullActionButton.interactable = false;

        // Delegate to PlayerHandManager so it can track the card internally
        if (playerHandManager != null)
        {
            playerHandManager.DealCardsFromTokens(1, DeckType.Action);
        }
        else if (mgr != null)
        {
            mgr.RequestPullAction(); // Fallback if PlayerHandManager is missing
        }
    }

    public void OnClickPullSupport()
    {
        if (pullSupportButton != null) pullSupportButton.interactable = false;

        // Delegate to PlayerHandManager so it can track the card internally
        if (playerHandManager != null)
        {
            playerHandManager.DealCardsFromTokens(1, DeckType.Support);
        }
        else if (mgr != null)
        {
            mgr.RequestPullSupport(); // Fallback if PlayerHandManager is missing
        }
    }
}
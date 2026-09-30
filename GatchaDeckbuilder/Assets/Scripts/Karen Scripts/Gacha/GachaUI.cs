using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class GachaUI : MonoBehaviour
{
    [Header("Token Display")]
    public TMP_Text tokenText;
    public string tokenFormat = "Tokens: {0}";

    [Header("Pity Display")]
    public TMP_Text actionPityText;
    public TMP_Text supportPityText;
    public string pityFormat = "{0} / {1}";

    [Header("Pity Colors")] // <-- NEW: Easy color tweaking in Inspector
    public Color normalPityColor = Color.white;
    public Color highPityColor = Color.orange;

    [Header("Pull Buttons")]
    public Button pullActionButton;
    public Button pullSupportButton;

    [Header("Opponent Visuals")]
    public GameObject cardBackPrefab;
    public Transform OpponentCardContainer;

    [Header("System References")]
    public DrawTimerManager drawTimerManager;
    public PlayerHandManager playerHandManager;

    private GachaManager mgr;
    private int currentTokens = 0;
    private bool isDrawPhaseActive = true;

    private int actionPityCount = 0;
    private int supportPityCount = 0;
    private int pityThreshold = 90;

    private void OnEnable() => StartCoroutine(SubscribeWhenReady());

    private void OnDisable()
    {
        StopAllCoroutines();
        if (mgr != null)
        {
            mgr.OnMyTokensChanged -= UpdateTokens;
            mgr.OnPullFailed -= HandlePullFailed;
            mgr.OnOpponentDrewCard -= HandleOpponentDrewCard;
            mgr.OnPityUpdated -= HandlePityUpdated;
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
        mgr.OnPullFailed += HandlePullFailed;
        mgr.OnOpponentDrewCard += HandleOpponentDrewCard;
        mgr.OnPityUpdated += HandlePityUpdated;

        currentTokens = mgr.GetLocalPlayerTokens();
        if (tokenText != null) tokenText.text = string.Format(tokenFormat, currentTokens);

        // Initialize Pity UI with current server state so it's accurate on load
        if (mgr != null)
        {
            var actionPity = mgr.GetLocalPlayerPity(true);
            actionPityCount = actionPity.pulls;
            pityThreshold = actionPity.threshold;
            if (actionPityText != null)
            {
                actionPityText.text = string.Format(pityFormat, actionPityCount, pityThreshold);
                UpdatePityTextColor(actionPityText, actionPityCount, pityThreshold);
            }

            var supportPity = mgr.GetLocalPlayerPity(false);
            supportPityCount = supportPity.pulls;
            if (supportPityText != null)
            {
                supportPityText.text = string.Format(pityFormat, supportPityCount, pityThreshold);
                UpdatePityTextColor(supportPityText, supportPityCount, pityThreshold);
            }
        }

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

        if (drawTimerManager != null)
        {
            drawTimerManager.NotifyCardsDrawn();
        }
        else
        {
            Debug.LogWarning("[GachaUI] drawTimerManager is null!");
        }

        if (playerHandManager != null)
        {
            playerHandManager.DealCardsFromTokens(1, DeckType.Action);
        }
        else if (mgr != null)
        {
            mgr.RequestPullAction();
        }
    }

    public void OnClickPullSupport()
    {
        if (pullSupportButton != null) pullSupportButton.interactable = false;

        if (drawTimerManager != null)
        {
            drawTimerManager.NotifyCardsDrawn();
        }
        else
        {
            Debug.LogWarning("[GachaUI] drawTimerManager is null!");
        }

        if (playerHandManager != null)
        {
            playerHandManager.DealCardsFromTokens(1, DeckType.Support);
        }
        else if (mgr != null)
        {
            mgr.RequestPullSupport();
        }
    }

    private void HandlePityUpdated(int pullsSinceLegendary, int threshold, bool isActionDeck)
    {
        pityThreshold = threshold;
        if (isActionDeck)
        {
            actionPityCount = pullsSinceLegendary;
            if (actionPityText != null)
            {
                actionPityText.text = string.Format(pityFormat, actionPityCount, pityThreshold);
                UpdatePityTextColor(actionPityText, actionPityCount, pityThreshold);
            }
        }
        else
        {
            supportPityCount = pullsSinceLegendary;
            if (supportPityText != null)
            {
                supportPityText.text = string.Format(pityFormat, supportPityCount, pityThreshold);
                UpdatePityTextColor(supportPityText, supportPityCount, pityThreshold);
            }
        }
    }

    // NEW: Helper method to handle the color logic
    private void UpdatePityTextColor(TMP_Text text, int pulls, int threshold)
    {
        // Turns orange if we are 1 away from pity OR have reached/exceeded it (e.g., 4/5 or 5/5)
        if (threshold > 0 && pulls >= threshold - 1)
        {
            text.color = highPityColor;
        }
        else
        {
            text.color = normalPityColor;
        }
    }
}
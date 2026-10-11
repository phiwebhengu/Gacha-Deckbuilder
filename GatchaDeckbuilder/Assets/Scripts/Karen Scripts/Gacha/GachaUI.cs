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

    [Header("Pity Colors")]
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
    private bool isDrawPhaseActive = true;

    // Token bookkeeping.
    // serverTokens = last value the server told us. pullsInFlight = pulls we've asked for but the server hasn't
    // answered yet. The displayed number is serverTokens - pullsInFlight, so the UI reacts instantly on click
    // instead of waiting a full network round trip (that wait is what made the joining player's buttons feel dead).
    private int serverTokens = 0;
    private int pullsInFlight = 0;
    private int DisplayedTokens => Mathf.Max(0, serverTokens - pullsInFlight);

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

        serverTokens = mgr.GetLocalPlayerTokens();
        pullsInFlight = 0;
        RefreshTokenText();

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

        if (drawTimerManager != null)
        {
            drawTimerManager.OnDrawPhaseChanged += UpdateButtonInteractability;
            isDrawPhaseActive = drawTimerManager.IsDrawPhaseActive;
        }

        UpdateButtonInteractability(isDrawPhaseActive);
    }

    private void RefreshTokenText()
    {
        if (tokenText != null) tokenText.text = string.Format(tokenFormat, DisplayedTokens);
    }

    // Authoritative token count from the server (one arrives for every pull that succeeded).
    private void UpdateTokens(int tokens)
    {
        serverTokens = tokens;
        pullsInFlight = Mathf.Max(0, pullsInFlight - 1);
        RefreshTokenText();
        UpdateButtonInteractability(isDrawPhaseActive);
    }

    private void UpdateButtonInteractability(bool phaseActive)
    {
        isDrawPhaseActive = phaseActive;
        bool timerAllowsPull = (drawTimerManager == null) || isDrawPhaseActive;
        bool canPull = DisplayedTokens > 0 && pullsInFlight == 0 && timerAllowsPull;

        if (pullActionButton != null) pullActionButton.interactable = canPull;
        if (pullSupportButton != null) pullSupportButton.interactable = canPull;
    }

    private void HandlePullFailed(string reason)
    {
        Debug.LogWarning($"Pull failed: {reason}");

        // The server refused, so no token was spent: give back the optimistic deduction.
        pullsInFlight = Mathf.Max(0, pullsInFlight - 1);
        RefreshTokenText();
        UpdateButtonInteractability(isDrawPhaseActive);
    }

    private void HandleOpponentDrewCard(ulong opponentClientId)
    {
        if (cardBackPrefab != null && OpponentCardContainer != null)
        {
            Instantiate(cardBackPrefab, OpponentCardContainer);
        }
    }

    public void OnClickPullAction() => PullFromDeck(DeckType.Action);
    public void OnClickPullSupport() => PullFromDeck(DeckType.Support);

    private void PullFromDeck(DeckType deck)
    {
        bool timerAllowsPull = (drawTimerManager == null) || drawTimerManager.IsDrawPhaseActive;
        if (DisplayedTokens <= 0 || pullsInFlight > 0 || !timerAllowsPull) return;

        // Must happen BEFORE the request: on the host the server answers synchronously inside this call.
        pullsInFlight++;
        RefreshTokenText();
        UpdateButtonInteractability(isDrawPhaseActive);

        if (playerHandManager != null)
        {
            // PlayerHandManager notifies the draw timer and sends the pull request itself.
            Button sourceButton = deck == DeckType.Action ? pullActionButton : pullSupportButton;
            RectTransform sourceBtnRect = sourceButton != null ? sourceButton.GetComponent<RectTransform>() : null;
            playerHandManager.DealCardsFromTokens(1, deck, sourceBtnRect);
        }
        else if (mgr != null)
        {
            if (drawTimerManager != null) drawTimerManager.NotifyCardsDrawn();

            if (deck == DeckType.Action) mgr.RequestPullAction();
            else mgr.RequestPullSupport();
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

    private void UpdatePityTextColor(TMP_Text text, int pulls, int threshold)
    {
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
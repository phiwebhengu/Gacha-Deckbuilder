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

    [Header("Card Visuals")]
    public GameObject cardPrefab;
    public Transform CardContainer;

    [Header("System References")]
    public DrawTimerManager drawTimerManager;

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
            mgr.OnMyPullResolved -= HandlePullResolved;
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
        mgr.OnMyPullResolved += HandlePullResolved;
        mgr.OnPullFailed += HandlePullFailed;
        mgr.OnOpponentDrewCard += HandleOpponentDrewCard;

        // --- CRITICAL FIX: Proactively fetch tokens in case we missed the initial RPC ---
        currentTokens = mgr.GetLocalPlayerTokens();
        if (tokenText != null) tokenText.text = string.Format(tokenFormat, currentTokens);

        if (drawTimerManager != null)
        {
            drawTimerManager.OnDrawPhaseChanged += UpdateButtonInteractability;
            isDrawPhaseActive = drawTimerManager.IsDrawPhaseActive;
        }

        // --- CRITICAL FIX: Force an initial button state evaluation ---
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

    private void HandlePullResolved(PullResult result)
    {
        string tag = "";
        if (result.PityTriggered) tag += " [PITY]";
        if (result.Was5050Roll) tag += result.Won5050 ? " [50/50 WON]" : " [50/50 LOST]";
        Debug.Log($"<color=cyan>Pulled {result.Deck}:</color> {result.CardName} ({result.Tier}){tag}");

        if (cardPrefab == null || CardContainer == null) return;

        bool isAction = result.Deck == DeckType.Action;
        Sprite sprite = GetCardSprite(result.CardId, isAction);
        var obj = Instantiate(cardPrefab, CardContainer);

        // Get the unified CardVisual component
        var visual = obj.GetComponent<CardVisual>();
        if (visual != null)
        {
            // 1. Setup the visual data and sprite
            if (isAction)
                visual.Setup(result.ActionData, sprite);
            else
                visual.Setup(result.SupportData, sprite);

            // 2. Trigger the reveal animation (uses the string overload we added to CardVisual)
            visual.PlayReveal(result.Tier);
        }
    }

    private void HandlePullFailed(string reason)
    {
        Debug.LogWarning($"Pull failed: {reason}");
        // Re-enable buttons if the pull fails so the player can try again
        if (pullActionButton != null) pullActionButton.interactable = true;
        if (pullSupportButton != null) pullSupportButton.interactable = true;
    }

    private Sprite GetCardSprite(int cardId, bool isActionCard)
    {
        string folder = isActionCard ? "CardSprites/Action/" : "CardSprites/Support/";
        return Resources.Load<Sprite>($"{folder}{cardId}");
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
        if (mgr != null) mgr.RequestPullAction();
    }

    public void OnClickPullSupport()
    {
        if (pullSupportButton != null) pullSupportButton.interactable = false;
        if (mgr != null) mgr.RequestPullSupport();
    }
}
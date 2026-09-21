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

    [Header("Opponent Card Visuals")]
    public GameObject cardBackPrefab; // A simple UI Image of a card back
    public Transform OpponentCardContainer;

    [Header("Card Visuals (optional — same wiring as the old GachaManager)")]
    public GameObject cardPrefab;
    public Transform CardContainer;

    private int currentTokens;
    private GachaManager mgr;

    void OnEnable()
    {
        StartCoroutine(SubscribeWhenReady());
    }

    void OnDisable()
    {
        StopAllCoroutines();
        Unsubscribe();
    }

    private IEnumerator SubscribeWhenReady()
    {
        // Find the LOCAL instance of the GachaManager
        while (mgr == null)
        {
            // Use FindFirstObjectByType (Unity 2023+) or FindObjectOfType (older Unity)
            mgr = FindFirstObjectByType<GachaManager>();

            // Ensure we found it AND it has finished spawning over the network
            if (mgr != null && mgr.IsSpawned)
            {
                break;
            }

            yield return null;
        }

        // Now safely subscribe to the LOCAL manager's events
        mgr.OnMyTokensChanged += UpdateTokens;
        mgr.OnMyActionCardPulled += HandleActionPulled;
        mgr.OnMySupportCardPulled += HandleSupportPulled;
        mgr.OnPullFailed += HandlePullFailed;
        mgr.OnOpponentDrewCard += HandleOpponentDrewCard;
    }

    private void Unsubscribe()
    {
        if (mgr == null) return;

        mgr.OnMyTokensChanged -= UpdateTokens;
        mgr.OnMyActionCardPulled -= HandleActionPulled;
        mgr.OnMySupportCardPulled -= HandleSupportPulled;
        mgr.OnPullFailed -= HandlePullFailed;
        mgr.OnOpponentDrewCard -= HandleOpponentDrewCard;

        mgr = null;
    }

    private void UpdateTokens(int tokens)
    {
        currentTokens = tokens;
        if (tokenText != null) tokenText.text = string.Format(tokenFormat, tokens);
        if (pullActionButton != null) pullActionButton.interactable = tokens > 0;
        if (pullSupportButton != null) pullSupportButton.interactable = tokens > 0;
    }

    private void HandleActionPulled(ActionCardData card, int pullsUntilPity)
    {
        Debug.Log($"<color=cyan>You pulled Action Card:</color> {card.Name} ({card.Tier}) — {pullsUntilPity} pulls to pity.");

        if (cardPrefab == null || CardContainer == null) return;

        Sprite sprite = GetCardSprite(card.Id, isActionCard: true);
        GameObject newCardObj = Instantiate(cardPrefab, CardContainer);
        CardVisual visual = newCardObj.GetComponent<CardVisual>();
        visual?.Setup(card, sprite);
    }

    private void HandleSupportPulled(SupportCardData card, int pullsUntilPity)
    {
        Debug.Log($"<color=magenta>You pulled Support Card:</color> {card.Name} ({card.Tier}) — {pullsUntilPity} pulls to pity.");

        if (cardPrefab == null || CardContainer == null) return;

        Sprite sprite = GetCardSprite(card.Id, isActionCard: false);
        GameObject newCardObj = Instantiate(cardPrefab, CardContainer);
        CardVisual visual = newCardObj.GetComponent<CardVisual>();
        // Requires a CardVisual.Setup(SupportCardData, Sprite) overload —
        // same gap noted for the old GachaManager's support-pull spawner.
        visual?.Setup(card, sprite);
    }

    private void HandlePullFailed(string reason)
    {
        Debug.LogWarning($"Pull failed: {reason}");
    }

    private Sprite GetCardSprite(int cardId, bool isActionCard)
    {
        string folder = isActionCard ? "CardSprites/Action/" : "CardSprites/Support/";
        return Resources.Load<Sprite>($"{folder}{cardId}");
    }

    private void HandleOpponentDrewCard(ulong opponentClientId)
    {
        Debug.Log($"<color=yellow>Opponent (Client {opponentClientId}) drew a card!</color>");

        if (cardBackPrefab == null || OpponentCardContainer == null) return;

        // Simply instantiate the card back. 
        // Note: This does NOT need to be a NetworkObject because it's just a local UI element.
        Instantiate(cardBackPrefab, OpponentCardContainer);
    }
}

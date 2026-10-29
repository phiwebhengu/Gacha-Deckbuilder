using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

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

    [Header("Free Pulls (Xudong / Atea) - optional")]
    [Tooltip("Optional. Shows the free pulls / Support discount you have left. Hidden when you have none.")]
    public TMP_Text freePullsText;
    public string freePullsFormat = "Free pulls: {0}";
    public string supportDiscountFormat = "Support pull -{0} token";

    [Header("Opponent Visuals")]
    public GameObject cardBackPrefab;
    public Transform OpponentCardContainer;

    [Header("Opponent Card Back Fan")]
    [Tooltip("Copy spacing / angle / arc / speed straight from the player's PlayerHandManager so both fans always match.")]
    public bool matchPlayerHandFan = true;
    [Tooltip("Reflect the fan across the horizontal axis, so the opponent's arc is the player's arc upside-down (a U shape instead of an arch). Off = identical to the player's fan.")]
    public bool mirrorFan = true;
    [Tooltip("Scale the horizontal gap between card backs (use < 1 if your card backs are smaller than your cards).")]
    public float spacingMultiplier = 1f;

    [Tooltip("Only used when 'Match Player Hand Fan' is off or no Player Hand Manager is assigned.")]
    public float cardSpacing = 120f;
    public float maxFanAngle = 18f;
    public float arcHeight = 35f;
    public float fanLerpSpeed = 12f;

    [Header("System References")]
    public DrawTimerManager drawTimerManager;
    public PlayerHandManager playerHandManager;

    private GachaManager mgr;
    private bool isDrawPhaseActive = true;

    private readonly List<GameObject> opponentCardBacks = new List<GameObject>();
    private Coroutine opponentFanCoroutine;
    private bool opponentContainerPrepared = false;

    private int serverTokens = 0;
    private int pullsInFlight = 0;
    private int totalInFlightCost = 0;
    private int DisplayedTokens => Mathf.Max(0, serverTokens - totalInFlightCost);

    private int freePullCredits = 0;
    private int supportPullDiscount = 0;

    private int actionPityCount = 0;
    private int supportPityCount = 0;
    private int pityThreshold = 4;

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
            mgr.OnMyPullBenefitsChanged -= HandleBenefitsChanged;
            mgr.OnOpponentLostCards -= HandleOpponentLostCards;
        }
        if (drawTimerManager != null)
        {
            drawTimerManager.OnDrawPhaseChanged -= UpdateButtonInteractability;
        }
    }

    private IEnumerator SubscribeWhenReady()
    {
        // Offline (AI / Tutorial) the GachaManager is never spawned, so only wait for it to exist.
        while (mgr == null || (!mgr.isOfflineAIRival && !mgr.IsSpawned))
        {
            mgr = FindFirstObjectByType<GachaManager>();
            yield return null;
        }

        mgr.OnMyTokensChanged += UpdateTokens;
        mgr.OnPullFailed += HandlePullFailed;
        mgr.OnOpponentDrewCard += HandleOpponentDrewCard;
        mgr.OnPityUpdated += HandlePityUpdated;
        mgr.OnMyPullBenefitsChanged += HandleBenefitsChanged;
        mgr.OnOpponentLostCards += HandleOpponentLostCards;

        serverTokens = mgr.GetLocalPlayerTokens();
        pullsInFlight = 0;
        totalInFlightCost = 0;
        freePullCredits = 0;
        supportPullDiscount = 0;
        RefreshTokenText();
        RefreshFreePullsText();

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

    private void UpdateTokens(int tokens)
    {
        serverTokens = tokens;
        pullsInFlight = Mathf.Max(0, pullsInFlight - 1);

        if (pullsInFlight == 0)
        {
            totalInFlightCost = 0;
        }

        RefreshTokenText();
        UpdateButtonInteractability(isDrawPhaseActive);
    }

    private void UpdateButtonInteractability(bool phaseActive)
    {
        isDrawPhaseActive = phaseActive;
        bool timerAllowsPull = (drawTimerManager == null) || isDrawPhaseActive;
        bool canPull = timerAllowsPull;

        if (pullActionButton != null) pullActionButton.interactable = canPull && DisplayedTokens >= CostFor(DeckType.Action);
        if (pullSupportButton != null) pullSupportButton.interactable = canPull && DisplayedTokens >= CostFor(DeckType.Support);
    }

    private int CostFor(DeckType deck)
    {
        int baseCost = mgr != null ? mgr.PullCost : 1;
        return GachaManager.CostOfPull(baseCost, deck == DeckType.Support, freePullCredits, supportPullDiscount);
    }

    public bool CanAffordPull(DeckType deck)
    {
        return mgr == null || DisplayedTokens >= CostFor(deck);
    }

    public bool CanPayForPull(DeckType deck)
    {
        return mgr == null || serverTokens >= CostFor(deck);
    }

    private void HandleBenefitsChanged(int freePulls, int supportDiscount)
    {
        freePullCredits = freePulls;
        supportPullDiscount = supportDiscount;
        RefreshFreePullsText();
        RefreshTokenText();
        UpdateButtonInteractability(isDrawPhaseActive);
    }

    private void RefreshFreePullsText()
    {
        if (freePullsText == null) return;

        var parts = new List<string>();
        if (freePullCredits > 0) parts.Add(string.Format(freePullsFormat, freePullCredits));
        if (supportPullDiscount > 0) parts.Add(string.Format(supportDiscountFormat, supportPullDiscount));

        freePullsText.text = string.Join("\n", parts);
        freePullsText.gameObject.SetActive(parts.Count > 0);
    }

    private void HandleOpponentLostCards(int count)
    {
        RemoveOpponentCardBacks(count);
    }

    private void HandlePullFailed(string reason)
    {
        Debug.LogWarning($"Pull failed: {reason}");

        pullsInFlight = Mathf.Max(0, pullsInFlight - 1);
        if (pullsInFlight == 0) totalInFlightCost = 0;

        RefreshTokenText();
        UpdateButtonInteractability(isDrawPhaseActive);
    }

    private void HandleOpponentDrewCard(ulong opponentClientId)
    {
        AddOpponentCardBacks(1);
    }

    public void AddOpponentCardBacks(int count)
    {
        if (cardBackPrefab == null || OpponentCardContainer == null) return;

        PrepareOpponentContainer();

        for (int i = 0; i < count; i++)
        {
            GameObject back = Instantiate(cardBackPrefab, OpponentCardContainer);
            PrepareCardBack(back);
            opponentCardBacks.Add(back);
        }

        RefreshOpponentFan();
    }

    private void PrepareOpponentContainer()
    {
        if (opponentContainerPrepared) return;
        opponentContainerPrepared = true;

        LayoutGroup layoutGroup = OpponentCardContainer.GetComponent<LayoutGroup>();
        if (layoutGroup != null)
        {
            layoutGroup.enabled = false;
        }

        ContentSizeFitter fitter = OpponentCardContainer.GetComponent<ContentSizeFitter>();
        if (fitter != null) fitter.enabled = false;
    }

    private void PrepareCardBack(GameObject back)
    {
        RectTransform rt = back.transform as RectTransform;
        if (rt == null) return;

        Vector2 size = rt.rect.size;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;

        rt.anchoredPosition = Vector2.zero;
        rt.localRotation = Quaternion.identity;
    }

    private void GetFanSettings(out float spacing, out float angle, out float arc, out float speed)
    {
        if (matchPlayerHandFan && playerHandManager != null)
        {
            spacing = playerHandManager.CardSpacing;
            angle = playerHandManager.MaxFanAngle;
            arc = playerHandManager.ArcHeight;
            speed = playerHandManager.FanLerpSpeed;
        }
        else
        {
            spacing = cardSpacing;
            angle = maxFanAngle;
            arc = arcHeight;
            speed = fanLerpSpeed;
        }
        spacing *= spacingMultiplier;
    }

    private void RefreshOpponentFan()
    {
        if (!isActiveAndEnabled) return;

        if (opponentFanCoroutine != null)
        {
            StopCoroutine(opponentFanCoroutine);
            opponentFanCoroutine = null;
        }

        opponentCardBacks.RemoveAll(b => b == null);
        if (opponentCardBacks.Count == 0) return;

        opponentFanCoroutine = StartCoroutine(Routine_AnimateOpponentFan());
    }

    private IEnumerator Routine_AnimateOpponentFan()
    {
        GetFanSettings(out float spacing, out float maxAngle, out float arc, out float speed);

        List<RectTransform> backs = new List<RectTransform>();
        foreach (GameObject b in opponentCardBacks)
        {
            RectTransform rt = b != null ? b.transform as RectTransform : null;
            if (rt != null) backs.Add(rt);
        }

        int count = backs.Count;
        Vector2[] targetPos = new Vector2[count];
        Quaternion[] targetRot = new Quaternion[count];

        for (int i = 0; i < count; i++)
        {
            float x = 0f, y = 0f, z = 0f;

            if (count > 1)
            {
                float totalWidth = spacing * (count - 1);
                x = (-totalWidth * 0.5f) + (i * spacing);

                float n = (2f * i / (count - 1)) - 1f;
                y = -(n * n) * arc;
                z = -n * maxAngle;
            }

            if (mirrorFan)
            {
                y = -y;
                z = -z;
            }

            targetPos[i] = new Vector2(x, y);
            targetRot[i] = Quaternion.Euler(0f, 0f, z);
        }

        bool animating = true;
        while (animating)
        {
            animating = false;
            float blend = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);

            for (int i = 0; i < count; i++)
            {
                if (backs[i] == null) continue;

                backs[i].anchoredPosition = Vector2.Lerp(backs[i].anchoredPosition, targetPos[i], blend);
                backs[i].localRotation = Quaternion.Slerp(backs[i].localRotation, targetRot[i], blend);

                if (Vector2.Distance(backs[i].anchoredPosition, targetPos[i]) > 0.05f ||
                    Quaternion.Angle(backs[i].localRotation, targetRot[i]) > 0.1f)
                {
                    animating = true;
                }
            }

            yield return null;
        }

        for (int i = 0; i < count; i++)
        {
            if (backs[i] == null) continue;
            backs[i].anchoredPosition = targetPos[i];
            backs[i].localRotation = targetRot[i];
        }

        opponentFanCoroutine = null;
    }

    public void RemoveOpponentCardBacks(int count)
    {
        opponentCardBacks.RemoveAll(b => b == null);

        for (int i = 0; i < count && opponentCardBacks.Count > 0; i++)
        {
            int last = opponentCardBacks.Count - 1;
            GameObject back = opponentCardBacks[last];
            opponentCardBacks.RemoveAt(last);

            if (back != null)
            {
                back.SetActive(false);
                Destroy(back);
            }
        }

        RefreshOpponentFan();
    }

    public void OnClickPullAction() => PullFromDeck(DeckType.Action);
    public void OnClickPullSupport() => PullFromDeck(DeckType.Support);

    private void PullFromDeck(DeckType deck)
    {
        bool timerAllowsPull = (drawTimerManager == null) || drawTimerManager.IsDrawPhaseActive;
        int cost = CostFor(deck);

        if (DisplayedTokens < cost || !timerAllowsPull) return;

        pullsInFlight++;
        totalInFlightCost += cost;

        RefreshTokenText();
        UpdateButtonInteractability(isDrawPhaseActive);

        if (playerHandManager != null)
        {
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
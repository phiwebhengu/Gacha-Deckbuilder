using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AIRivalController : MonoBehaviour
{
    [Header("Rival Systems")]
    [SerializeField] private TokenSelectionManager rivalTokenManager;
    [SerializeField] private PlayerHandManager rivalHandManager;
    [SerializeField] private RectTransform rivalSelectedHandTransform;
    [SerializeField] private DeckButton actionDeck;
    [SerializeField] private DeckButton supportDeck;

    [Header("Timer System Link")]
    [SerializeField] private DrawTimerManager drawTimerManager;

    [Header("Decision Timing")]
    [SerializeField] private float minDecisionDelay = 0.6f;
    [SerializeField] private float maxDecisionDelay = 1.8f;

    [Header("Draw Timing (Human-like Delays)")]
    [SerializeField] private float minDrawDelay = 0.3f;
    [SerializeField] private float maxDrawDelay = 0.85f;
    [SerializeField] private float minMultiDrawDelay = 0.4f;
    [SerializeField] private float maxMultiDrawDelay = 0.9f;

    [Header("Tactical Behavior")]
    [Range(0f, 1f)]
    [SerializeField] private float splitDeckChance = 0.65f;

    private Coroutine aiDecisionCoroutine;
    private Coroutine currentDrawCoroutine;

    public RectTransform RivalSelectedHandTransform => rivalSelectedHandTransform;
    public PlayerHandManager RivalHandManager => rivalHandManager;

    private void Awake()
    {
        if (drawTimerManager == null)
        {
            drawTimerManager = FindObjectOfType<DrawTimerManager>();
        }
    }

    public void StartAIDrawPhase()
    {
        if (rivalTokenManager == null) return;

        rivalTokenManager.gameObject.SetActive(true);

        StopAllRunningCoroutines();
        aiDecisionCoroutine = StartCoroutine(Routine_MakeAIDecision());
    }

    private IEnumerator Routine_MakeAIDecision()
    {
        float initialDelay = Random.Range(minDecisionDelay, maxDecisionDelay);
        yield return new WaitForSeconds(initialDelay);

        // Terminate if timer expired during initial delay
        if (drawTimerManager != null && !drawTimerManager.IsDrawPhaseActive) yield break;

        int availableTokens = rivalTokenManager.RemainingTokenCount;

        if (availableTokens <= 0)
        {
            SelectCardsForEndTurn();
            yield break;
        }

        // Variance: Randomize token spend amount based on current pool
        int totalTokensToSpend = Random.Range(1, availableTokens + 1);
        bool shouldSplit = (totalTokensToSpend > 1) && (Random.value < splitDeckChance) && (actionDeck != null && supportDeck != null);

        if (shouldSplit)
        {
            int actionTokens = Random.Range(1, totalTokensToSpend);
            int supportTokens = totalTokensToSpend - actionTokens;

            bool focusActionFirst = Random.value > 0.5f;
            DeckButton firstDeck = focusActionFirst ? actionDeck : supportDeck;
            DeckButton secondDeck = focusActionFirst ? supportDeck : actionDeck;

            int firstCount = focusActionFirst ? actionTokens : supportTokens;
            int secondCount = focusActionFirst ? supportTokens : actionTokens;

            currentDrawCoroutine = StartCoroutine(Routine_ExecuteDraw(firstCount, firstDeck));
            yield return currentDrawCoroutine;

            if (drawTimerManager != null && !drawTimerManager.IsDrawPhaseActive) yield break;

            float pauseBetweenDecks = Random.Range(minMultiDrawDelay, maxMultiDrawDelay);
            yield return new WaitForSeconds(pauseBetweenDecks);

            if (drawTimerManager != null && !drawTimerManager.IsDrawPhaseActive) yield break;

            currentDrawCoroutine = StartCoroutine(Routine_ExecuteDraw(secondCount, secondDeck));
            yield return currentDrawCoroutine;
        }
        else
        {
            DeckButton chosenDeck = GetRandomAvailableDeck();
            if (chosenDeck != null)
            {
                currentDrawCoroutine = StartCoroutine(Routine_ExecuteDraw(totalTokensToSpend, chosenDeck));
                yield return currentDrawCoroutine;
            }
        }

        if (drawTimerManager != null && !drawTimerManager.IsDrawPhaseActive) yield break;

        yield return new WaitForSeconds(0.4f);
        SelectCardsForEndTurn();
    }

    private IEnumerator Routine_ExecuteDraw(int tokenCount, DeckButton deck)
    {
        if (rivalTokenManager == null || deck == null || tokenCount <= 0) yield break;

        for (int i = 0; i < tokenCount; i++)
        {
            // Strict timer guard check before executing each draw
            if (drawTimerManager != null && !drawTimerManager.IsDrawPhaseActive) yield break;
            if (rivalTokenManager.RemainingTokenCount <= 0) yield break;

            rivalTokenManager.OnDeckSelected(deck);

            float randomDrawInterval = Random.Range(minDrawDelay, maxDrawDelay);
            yield return new WaitForSeconds(randomDrawInterval);
        }
    }

    private void SelectCardsForEndTurn()
    {
        if (rivalHandManager == null) return;

        List<CardUI> cardsInHand = new List<CardUI>(rivalHandManager.GetComponentsInChildren<CardUI>(includeInactive: false));
        if (cardsInHand.Count == 0) return;

        int numToSelect = Random.Range(1, cardsInHand.Count + 1);

        for (int i = 0; i < numToSelect; i++)
        {
            BalatroCardController cardController = cardsInHand[i].GetComponent<BalatroCardController>();
            if (cardController != null && !cardController.IsSelected)
            {
                cardController.ToggleSelection();
            }
        }
    }

    public void SubmitRivalHand()
    {
        if (rivalHandManager != null && rivalSelectedHandTransform != null)
        {
            rivalHandManager.SubmitSelectedCardsToHand(rivalSelectedHandTransform);
        }
    }

    public void ClearRivalSubmittedCards()
    {
        if (rivalHandManager != null && rivalSelectedHandTransform != null)
        {
            rivalHandManager.ReturnSubmittedCardsToHand(rivalSelectedHandTransform);
        }
    }

    private DeckButton GetRandomAvailableDeck()
    {
        if (actionDeck != null && supportDeck != null)
        {
            return Random.value > 0.5f ? actionDeck : supportDeck;
        }
        return actionDeck != null ? actionDeck : supportDeck;
    }

    public void StopAIDrawPhase()
    {
        StopAllRunningCoroutines();

        if (rivalTokenManager != null)
        {
            rivalTokenManager.gameObject.SetActive(false);
        }
    }

    private void StopAllRunningCoroutines()
    {
        if (aiDecisionCoroutine != null)
        {
            StopCoroutine(aiDecisionCoroutine);
            aiDecisionCoroutine = null;
        }
        if (currentDrawCoroutine != null)
        {
            StopCoroutine(currentDrawCoroutine);
            currentDrawCoroutine = null;
        }
        StopAllCoroutines(); // Clears any orphan sub-coroutines running on this MonoBehaviour
    }
}
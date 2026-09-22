using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum AIStrategyType
{
    AggressiveStart,   // Heavy spend early, lighter late
    LateGamePower,     // Conservative early, heavy spend late
    Balanced,          // Even spread (~5 per round)
    FrontLoadedRush    // Max spend early, minimal late
}

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

    [Header("Round & Strategy System")]
    [SerializeField] private EndTurnManager endTurnManager;
    [SerializeField] private AIStrategyType activeStrategy;

    private Coroutine aiDecisionCoroutine;
    private Coroutine currentDrawCoroutine;
    private int[] tokenBudgetPlan = new int[4];

    public RectTransform RivalSelectedHandTransform => rivalSelectedHandTransform;
    public PlayerHandManager RivalHandManager => rivalHandManager;

    private void Awake()
    {
        if (drawTimerManager == null) drawTimerManager = FindObjectOfType<DrawTimerManager>();
        if (endTurnManager == null) endTurnManager = FindObjectOfType<EndTurnManager>();

        InitializeStrategy();
    }

    /// <summary>
    /// Pick a random strategy profile at game start that totals exactly 20 tokens over 4 rounds.
    /// </summary>
    private void InitializeStrategy()
    {
        activeStrategy = (AIStrategyType)Random.Range(0, System.Enum.GetValues(typeof(AIStrategyType)).Length);

        switch (activeStrategy)
        {
            case AIStrategyType.AggressiveStart:
                tokenBudgetPlan = new int[] { 7, 6, 4, 3 };
                break;
            case AIStrategyType.LateGamePower:
                tokenBudgetPlan = new int[] { 3, 4, 6, 7 };
                break;
            case AIStrategyType.Balanced:
                tokenBudgetPlan = new int[] { 5, 5, 5, 5 };
                break;
            case AIStrategyType.FrontLoadedRush:
                tokenBudgetPlan = new int[] { 8, 7, 3, 2 };
                break;
        }

        Debug.Log($"[AI Strategy] Chosen Strategy: {activeStrategy} | Planned Budget per Round: [{string.Join(", ", tokenBudgetPlan)}]");
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

        if (drawTimerManager != null && !drawTimerManager.IsDrawPhaseActive)
        {
            SelectCardsForEndTurn();
            yield break;
        }

        int availableTokens = rivalTokenManager.RemainingTokenCount;
        if (availableTokens <= 0)
        {
            SelectCardsForEndTurn();
            yield break;
        }

        int currentRound = (endTurnManager != null) ? endTurnManager.CurrentRoundIndex : 0;
        int totalTokensToSpend = CalculateTokensToSpend(currentRound, availableTokens);

        Debug.Log($"[AI Round {currentRound + 1}] Remaining Tokens: {availableTokens} | Decided to Spend: {totalTokensToSpend}");

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

            if (drawTimerManager != null && !drawTimerManager.IsDrawPhaseActive)
            {
                SelectCardsForEndTurn();
                yield break;
            }

            float pauseBetweenDecks = Random.Range(minMultiDrawDelay, maxMultiDrawDelay);
            yield return new WaitForSeconds(pauseBetweenDecks);

            if (drawTimerManager != null && !drawTimerManager.IsDrawPhaseActive)
            {
                SelectCardsForEndTurn();
                yield break;
            }

            currentDrawCoroutine = StartCoroutine(Routine_ExecuteDraw(secondCount, secondDeck));
            yield return currentDrawCoroutine;
        }
        else
        {
            DeckButton chosenDeck = GetRandomAvailableDeck();
            if (chosenDeck != null && totalTokensToSpend > 0)
            {
                currentDrawCoroutine = StartCoroutine(Routine_ExecuteDraw(totalTokensToSpend, chosenDeck));
                yield return currentDrawCoroutine;
            }
        }

        if (drawTimerManager != null && !drawTimerManager.IsDrawPhaseActive)
        {
            SelectCardsForEndTurn();
            yield break;
        }

        yield return new WaitForSeconds(0.4f);
        SelectCardsForEndTurn();
    }

    private int CalculateTokensToSpend(int roundIndex, int availableTokens)
    {
        // Round 4 (index 3) or final turns: Spend all remaining tokens
        if (roundIndex >= 3)
        {
            return availableTokens;
        }

        int planned = tokenBudgetPlan[Mathf.Clamp(roundIndex, 0, 3)];

        // Clamp to available tokens and ensure at least 1 token is spent if available
        int spendTarget = Mathf.Clamp(planned, 1, availableTokens);

        return spendTarget;
    }

    private IEnumerator Routine_ExecuteDraw(int tokenCount, DeckButton deck)
    {
        if (rivalTokenManager == null || deck == null || tokenCount <= 0) yield break;

        for (int i = 0; i < tokenCount; i++)
        {
            if (drawTimerManager != null && !drawTimerManager.IsDrawPhaseActive) yield break;
            if (rivalTokenManager.RemainingTokenCount <= 0) yield break;

            rivalTokenManager.OnDeckSelected(deck);

            float randomDrawInterval = Random.Range(minDrawDelay, maxDrawDelay);
            yield return new WaitForSeconds(randomDrawInterval);
        }
    }

    /// <summary>
    /// Selects cards in hand for submission.
    /// In Round 4, selects ALL cards to leave zero cards in hand.
    /// </summary>
    public void SelectCardsForEndTurn()
    {
        if (rivalHandManager == null) return;

        List<CardUI> cardsInHand = new List<CardUI>(rivalHandManager.GetComponentsInChildren<CardUI>(includeInactive: false));
        if (cardsInHand.Count == 0) return;

        int currentRound = (endTurnManager != null) ? endTurnManager.CurrentRoundIndex : 0;
        bool isFinalRound = (currentRound >= 3);

        int alreadySelectedCount = 0;
        foreach (var card in cardsInHand)
        {
            BalatroCardController cardController = card.GetComponent<BalatroCardController>();
            if (cardController != null && cardController.IsSelected)
            {
                alreadySelectedCount++;
            }
        }

        if (isFinalRound)
        {
            // ROUND 4: Select ALL unselected cards so every card is played
            foreach (var card in cardsInHand)
            {
                BalatroCardController cardController = card.GetComponent<BalatroCardController>();
                if (cardController != null && !cardController.IsSelected)
                {
                    cardController.ToggleSelection();
                }
            }
            Debug.Log($"[AI Hand] Round 4 Final Play: Selected ALL {cardsInHand.Count} cards in hand.");
        }
        else if (alreadySelectedCount == 0)
        {
            // Normal rounds: Select a random number of cards (at least 1)
            int numToSelect = Random.Range(1, cardsInHand.Count + 1);

            for (int i = 0; i < cardsInHand.Count; i++)
            {
                CardUI temp = cardsInHand[i];
                int randomIndex = Random.Range(i, cardsInHand.Count);
                cardsInHand[i] = cardsInHand[randomIndex];
                cardsInHand[randomIndex] = temp;
            }

            for (int i = 0; i < numToSelect; i++)
            {
                BalatroCardController cardController = cardsInHand[i].GetComponent<BalatroCardController>();
                if (cardController != null && !cardController.IsSelected)
                {
                    cardController.ToggleSelection();
                }
            }
        }
    }

    public void ForceEndTurnAndSelect()
    {
        StopAllRunningCoroutines();

        if (rivalTokenManager != null)
        {
            rivalTokenManager.gameObject.SetActive(false);
        }

        SelectCardsForEndTurn();
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
        StopAllCoroutines();
    }
}
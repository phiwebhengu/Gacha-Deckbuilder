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

    [Header("Decision Timing")]
    [SerializeField] private float minDecisionDelay = 0.6f;
    [SerializeField] private float maxDecisionDelay = 1.8f;
    [SerializeField] private float multiDrawDelay = 0.5f;

    [Header("Tactical Behavior")]
    [Range(0f, 1f)]
    [SerializeField] private float splitDeckChance = 0.65f;

    private Coroutine aiDecisionCoroutine;

    // Public getters to safely share references with EndTurnManager
    public RectTransform RivalSelectedHandTransform => rivalSelectedHandTransform;
    public PlayerHandManager RivalHandManager => rivalHandManager;

    public void StartAIDrawPhase()
    {
        if (rivalTokenManager == null) return;

        rivalTokenManager.gameObject.SetActive(true);

        if (aiDecisionCoroutine != null)
        {
            StopCoroutine(aiDecisionCoroutine);
        }

        aiDecisionCoroutine = StartCoroutine(Routine_MakeAIDecision());
    }

    private IEnumerator Routine_MakeAIDecision()
    {
        float initialDelay = Random.Range(minDecisionDelay, maxDecisionDelay);
        yield return new WaitForSeconds(initialDelay);

        int availableTokens = rivalTokenManager.tokenButtons.Count;

        if (availableTokens <= 0)
        {
            SelectCardsForEndTurn();
            yield break;
        }

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

            yield return StartCoroutine(Routine_ExecuteDraw(firstCount, firstDeck));
            yield return new WaitForSeconds(multiDrawDelay);
            yield return StartCoroutine(Routine_ExecuteDraw(secondCount, secondDeck));
        }
        else
        {
            DeckButton chosenDeck = GetRandomAvailableDeck();
            if (chosenDeck != null)
            {
                yield return StartCoroutine(Routine_ExecuteDraw(totalTokensToSpend, chosenDeck));
            }
        }

        // Wait for card dealing animations to settle before selecting cards
        yield return new WaitForSeconds(0.6f);
        SelectCardsForEndTurn();
    }

    private IEnumerator Routine_ExecuteDraw(int tokenCount, DeckButton deck)
    {
        if (rivalTokenManager == null || deck == null || tokenCount <= 0) yield break;

        for (int i = 0; i < tokenCount; i++)
        {
            // Stop if AI runs out of tokens mid-sequence
            if (rivalTokenManager.RemainingTokenCount <= 0) yield break;

            rivalTokenManager.OnDeckSelected(deck);
            yield return new WaitForSeconds(0.35f);
        }
    }

    // AI selects a random subset of cards currently held in hand
    // AI selects a random subset of cards currently held in hand
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

        Debug.Log($"[AI Rival] Highlighted {numToSelect} cards for turn lock-in.");

        // Notify EndTurnManager that the rival is done so prompt UI can trigger
        EndTurnManager endTurnMgr = FindObjectOfType<EndTurnManager>();
        if (endTurnMgr != null)
        {
           // endTurnMgr.NotifyRivalEndedTurn("Rival is ready! End your turn.");
        }
    }

    // Called simultaneously by EndTurnManager when End Turn button is clicked
    public void SubmitRivalHand()
    {
        if (rivalHandManager != null && rivalSelectedHandTransform != null)
        {
            rivalHandManager.SubmitSelectedCardsToHand(rivalSelectedHandTransform);
        }
    }

    /// <summary>
    /// Clears and animates the destruction of cards sitting in the rival's played hand slot.
    /// Called by EndTurnManager between rounds.
    /// </summary>
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
        if (aiDecisionCoroutine != null)
        {
            StopCoroutine(aiDecisionCoroutine);
        }

        if (rivalTokenManager != null)
        {
            rivalTokenManager.gameObject.SetActive(false);
        }
    }


}
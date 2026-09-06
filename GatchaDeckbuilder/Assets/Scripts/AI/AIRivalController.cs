using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AIRivalController : MonoBehaviour
{
    [Header("Rival Systems")]
    [SerializeField] private TokenSelectionManager rivalTokenManager;
    [SerializeField] private DeckButton actionDeck;
    [SerializeField] private DeckButton supportDeck;

    [Header("Decision Timing")]
    [Tooltip("Min delay before AI makes its first move")]
    [SerializeField] private float minDecisionDelay = 0.6f;

    [Tooltip("Max delay before AI makes its first move")]
    [SerializeField] private float maxDecisionDelay = 1.8f;

    [Tooltip("Pause between consecutive draws if AI splits tokens")]
    [SerializeField] private float multiDrawDelay = 0.5f;

    [Header("Tactical Behavior")]
    [Tooltip("Chance (0 to 1) for the AI to split its tokens across both decks")]
    [Range(0f, 1f)]
    [SerializeField] private float splitDeckChance = 0.65f;

    private Coroutine aiDecisionCoroutine;

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
        // 1. Initial human-like pause
        float initialDelay = Random.Range(minDecisionDelay, maxDecisionDelay);
        yield return new WaitForSeconds(initialDelay);

        int availableTokens = rivalTokenManager.tokenButtons.Count; //

        if (availableTokens <= 0)
        {
            Debug.Log("[AI Rival] Out of tokens! Passing turn.");
            yield break;
        }

        // 2. Decide total budget to spend this window
        int totalTokensToSpend = Random.Range(1, availableTokens + 1);

        // 3. Determine if AI should split budget or buy all from one deck
        bool shouldSplit = (totalTokensToSpend > 1) && (Random.value < splitDeckChance) && (actionDeck != null && supportDeck != null);

        if (shouldSplit)
        {
            // Split tokens between Action and Support
            int actionTokens = Random.Range(1, totalTokensToSpend);
            int supportTokens = totalTokensToSpend - actionTokens;

            // Randomize order (Action first or Support first)
            bool focusActionFirst = Random.value > 0.5f;
            DeckButton firstDeck = focusActionFirst ? actionDeck : supportDeck;
            DeckButton secondDeck = focusActionFirst ? supportDeck : actionDeck;

            int firstCount = focusActionFirst ? actionTokens : supportTokens;
            int secondCount = focusActionFirst ? supportTokens : actionTokens;

            // --- First Selection ---
            yield return StartCoroutine(Routine_ExecuteDraw(firstCount, firstDeck));

            // Small pause between drawing from two different decks
            yield return new WaitForSeconds(multiDrawDelay);

            // --- Second Selection ---
            yield return StartCoroutine(Routine_ExecuteDraw(secondCount, secondDeck));
        }
        else
        {
            // Single deck selection (picks Action or Support at random)
            DeckButton chosenDeck = GetRandomAvailableDeck();
            if (chosenDeck != null)
            {
                yield return StartCoroutine(Routine_ExecuteDraw(totalTokensToSpend, chosenDeck));
            }
        }
    }

    private IEnumerator Routine_ExecuteDraw(int tokenCount, DeckButton deck)
    {
        if (rivalTokenManager == null || deck == null || tokenCount <= 0) yield break;

        // Stage tokens
        rivalTokenManager.SelectTokens(tokenCount); //[cite: 10, 11]

        // Hover pause before clicking deck
        yield return new WaitForSeconds(0.35f);

        Debug.Log($"[AI Rival] Drawing {tokenCount} card(s) from {deck.Type} Deck.");
        rivalTokenManager.OnDeckSelected(deck); //[cite: 10, 11]
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
            rivalTokenManager.gameObject.SetActive(false); //[cite: 11]
        }
    }
}
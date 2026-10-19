using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GachaManager : NetworkBehaviour
{
    public static GachaManager Instance { get; private set; }

    [Header("Pull Config (ScriptableObject)")]
    public PullConfig pullConfig;

    [Header("Starting State")]
    public int startingTokens = 10;

    [Header("Pull Cost")]
    [Tooltip("Tokens one pull costs. Atea takes 1 off the next Support pull, Xudong makes pulls free.")]
    public int pullCost = 1;

    [Header("Support Card Effects")]
    [Tooltip("When a Support effect gives free cards without naming a deck (Liyue Harbor Wharf, Weeping Willow, Frostmoon Enclave, " +
             "Setaria, Hanachirusato, Golden House), the chance (0-1) that each card is an Action card. The rest are Support cards.")]
    [Range(0f, 1f)] public float bonusPullActionChance = 0.5f;

    [Tooltip("Jade Chamber: extra Legendary chance, in percentage points, per Jade Chamber played. Lasts for the next draw window.")]
    public int jadeChamberLuckPercent = 10;

    [Tooltip("Safety limit for Knights of Favonius Library re-pulling Knights of Favonius Library again and again.")]
    public int maxKnightsChain = 10;

    private class PlayerGachaState
    {
        public int Tokens;
        public PityState ActionPity = new PityState();
        public PityState SupportPity = new PityState();
        public System.Random Rng;
        public int FeaturedActionCardId;
        public int FeaturedSupportCardId;

        // ----- Support effect state (server only) -----

        // Network ids (see CardNetId) of the cards the server believes are in this player's hand, oldest first.
        // Needed for Seymour (copy the opponent's first card), Frostmoon Enclave (discard) and Setaria (empty collection).
        public readonly List<int> Hand = new List<int>();

        public int FreePullCredits;      // Xudong: pulls that cost no tokens
        public int SupportPullDiscount;  // Atea: Support pulls that cost 1 token less
        public int LuckBonusPercent;     // Jade Chamber: extra Legendary chance for the next draw window
        public int HanachirusatoDraws;   // Hanachirusato: cards drawn for each Weapon/Artefact pulled in the next draw window
        public int PullsThisRound;       // Weeping Willow: how many pulls the player asked for this round
    }

    /// <summary>What EndTurnManager hands over after a round is resolved: who played what, and the support multipliers.</summary>
    public class RoundEndInfo
    {
        public ulong ClientId;
        public SideOutcome Outcome;
    }

    private class RoundEndPlan
    {
        public ulong ClientId;
        public PlayerGachaState State;
        public int GoldPulls;
        public int DiscardAndRedraw;
        public int Copies;
        public readonly List<string> FreePullReasons = new List<string>();
    }

    private List<ActionCardData> actionCardSource;
    private List<SupportCardData> supportCardSource;
    private readonly Dictionary<ulong, PlayerGachaState> playerStates = new Dictionary<ulong, PlayerGachaState>();

    public event Action<int> OnMyTokensChanged;
    public event Action<PullResult> OnMyPullResolved;
    public event Action<string> OnPullFailed;
    public event Action<ulong> OnOpponentDrewCard;
    public event Action<int, int> OnFeaturedCardsSet;
    public event Action<int, int, bool> OnPityUpdated;

    /// <summary>(free pulls left, Support pull discount left). Raised whenever Xudong / Atea change.</summary>
    public event Action<int, int> OnMyPullBenefitsChanged;

    /// <summary>A Support effect gave this player a card (not a pull they asked for). The string says why.</summary>
    public event Action<PullResult, string> OnMyBonusCardGranted;

    /// <summary>A Support effect removed cards from this player's hand (network ids).</summary>
    public event Action<int[]> OnMyHandCardsRemoved;

    /// <summary>A Support effect removed this many cards from the OPPONENT's hand (their card backs must shrink).</summary>
    public event Action<int> OnOpponentLostCards;

    public int PullCost => pullCost;

    private void Awake()
    {
        if (IsClient) Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        // New match: clear the numbers shown on the Outcome page (every client keeps its own stats).
        if (IsClient) MatchStats.Reset(startingTokens);

        if (!IsServer) return;

        var deckMgr = FindFirstObjectByType<DeckManager>();
        if (deckMgr != null)
        {
            actionCardSource = deckMgr.loadedActionCards;
            supportCardSource = deckMgr.loadedSupportCards;
        }

        if (actionCardSource == null || actionCardSource.Count == 0)
            Debug.LogWarning("⚠️ GachaManager: Action deck not loaded.");
        if (supportCardSource == null || supportCardSource.Count == 0)
            Debug.LogWarning("⚠️ GachaManager: Support deck not loaded.");

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            RegisterPlayer(clientId);

        NetworkManager.Singleton.SceneManager.OnSynchronizeComplete += HandleSynchronizeComplete;
        NetworkManager.Singleton.OnClientDisconnectCallback += UnregisterPlayer;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer || NetworkManager.Singleton == null) return;
        if (NetworkManager.Singleton.SceneManager != null)
            NetworkManager.Singleton.SceneManager.OnSynchronizeComplete -= HandleSynchronizeComplete;
        NetworkManager.Singleton.OnClientDisconnectCallback -= UnregisterPlayer;
    }

    private void HandleSynchronizeComplete(ulong clientId) => RegisterPlayer(clientId);

    private void RegisterPlayer(ulong clientId)
    {
        if (playerStates.ContainsKey(clientId)) return;
        int seed = Environment.TickCount ^ (int)clientId;
        playerStates[clientId] = new PlayerGachaState
        {
            Tokens = startingTokens,
            Rng = new System.Random(seed)
        };
        SendTokensClientRpc(startingTokens, ToTarget(clientId));
    }

    private void UnregisterPlayer(ulong clientId) => playerStates.Remove(clientId);

    /// <summary>Server only. Gives a player tokens (used by Support card effects such as Paimon / Katheryne / Vanarana).</summary>
    public void AddTokens(ulong clientId, int amount)
    {
        if (!IsServer || amount == 0) return;
        if (!playerStates.TryGetValue(clientId, out var state)) return;

        state.Tokens = Mathf.Max(0, state.Tokens + amount);
        SendTokensClientRpc(state.Tokens, ToTarget(clientId));
    }

    public void SetFeaturedCards(int actionId, int supportId)
    {
        if (!IsServer) return;
        foreach (var state in playerStates.Values)
        {
            state.FeaturedActionCardId = actionId;
            state.FeaturedSupportCardId = supportId;
        }
        NotifyFeaturedCardsClientRpc(actionId, supportId);
    }

    // --- NEW METHOD: Allows UI to safely fetch tokens if it missed the initial RPC ---
    public int GetLocalPlayerTokens()
    {
        if (NetworkManager == null || !NetworkManager.IsListening) return startingTokens;
        if (playerStates.TryGetValue(NetworkManager.LocalClientId, out var state))
        {
            return state.Tokens;
        }
        return startingTokens; // Fallback
    }

    // ===================== pull cost (shared by the server and the UI) =====================

    /// <summary>
    /// What one pull costs. The server and GachaUI both use this, so the number the player sees is the number the server charges.
    /// Atea (support discount) is used first on Support pulls, then Xudong's free pulls.
    /// </summary>
    public static int CostOfPull(int baseCost, bool isSupportPull, int freePullCredits, int supportPullDiscount)
    {
        return ResolveCost(baseCost, isSupportPull, freePullCredits, supportPullDiscount, out _, out _);
    }

    private static int ResolveCost(int baseCost, bool isSupportPull, int freePullCredits, int supportPullDiscount,
                                   out bool usesDiscount, out bool usesFreeCredit)
    {
        int cost = Mathf.Max(0, baseCost);

        usesDiscount = isSupportPull && supportPullDiscount > 0;
        if (usesDiscount) cost = Mathf.Max(0, cost - 1);

        usesFreeCredit = cost > 0 && freePullCredits > 0;
        if (usesFreeCredit) cost = 0;

        return cost;
    }

    // ===================== pulling =====================

    public void RequestPullAction() => RequestPullActionServerRpc();
    public void RequestPullSupport() => RequestPullSupportServerRpc();

    [ServerRpc(RequireOwnership = false)]
    private void RequestPullActionServerRpc(ServerRpcParams rpcParams = default)
    {
        HandlePullRequest(rpcParams.Receive.SenderClientId, true);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPullSupportServerRpc(ServerRpcParams rpcParams = default)
    {
        HandlePullRequest(rpcParams.Receive.SenderClientId, false);
    }

    private void HandlePullRequest(ulong clientId, bool isActionDeck)
    {
        var target = ToTarget(clientId);
        if (!playerStates.TryGetValue(clientId, out var state)) { SendPullFailedClientRpc("Not ready yet.", target); return; }

        int cost = ResolveCost(pullCost, !isActionDeck, state.FreePullCredits, state.SupportPullDiscount,
                               out bool usesDiscount, out bool usesFreeCredit);

        if (state.Tokens < cost) { SendPullFailedClientRpc("Out of tokens.", target); return; }

        // Pay
        if (usesDiscount) state.SupportPullDiscount--;
        if (usesFreeCredit) state.FreePullCredits--;
        state.Tokens -= cost;
        state.PullsThisRound++;

        PullResult result = RollOne(state, isActionDeck, null);

        // Knights of Favonius Library: "Discard this pull and pull again for free".
        bool discardedByKnights = IsKnights(result);
        result.AutoDiscard = discardedByKnights;

        // Order matters: the UI must know the new free-pull numbers before the token message re-enables the buttons.
        if (usesDiscount || usesFreeCredit)
            SendBenefitsClientRpc(state.FreePullCredits, state.SupportPullDiscount, target);

        SendTokensClientRpc(state.Tokens, target);
        DispatchPullResult(result, PityOf(state, isActionDeck), target);

        if (discardedByKnights)
        {
            // The card never joins the hand, so the opponent doesn't get a new card back for it.
            Debug.Log($"[Support] Client {clientId}: Knights of Favonius Library pulled, discarded, pulling again for free.");
            GrantBonusPull(clientId, state, false, null, "Knights of Favonius Library: pull again", 1);
        }
        else
        {
            AddToHand(state, result);
            NotifyOpponentDrewCardClientRpc(clientId);
            TriggerHanachirusato(clientId, state, result);
        }
    }

    private PullResult RollOne(PlayerGachaState state, bool isActionDeck, Rarity? forcedTier)
    {
        if (isActionDeck)
            return GachaEngine.PullAction(pullConfig, state.ActionPity, state.Rng, actionCardSource, state.FeaturedActionCardId,
                                          state.LuckBonusPercent, forcedTier);

        return GachaEngine.PullSupport(pullConfig, state.SupportPity, state.Rng, supportCardSource, state.FeaturedSupportCardId,
                                       state.LuckBonusPercent, forcedTier);
    }

    private static int PityOf(PlayerGachaState state, bool isActionDeck)
    {
        return isActionDeck ? state.ActionPity.PullsSinceLegendary : state.SupportPity.PullsSinceLegendary;
    }

    private static bool IsKnights(PullResult result)
    {
        return result != null && result.Deck == DeckType.Support && result.CardId == SupportEffectResolver.KnightsOfFavoniusLibrary;
    }

    private static void AddToHand(PlayerGachaState state, PullResult result)
    {
        if (result == null || result.CardId <= 0) return; // empty-deck error result
        state.Hand.Add(CardNetId.Encode(result.CardId, result.Deck == DeckType.Support));
    }

    private bool RandomIsActionDeck(PlayerGachaState state)
    {
        return state.Rng.NextDouble() < bonusPullActionChance;
    }

    private static bool CategoryIs(string value, string expected)
    {
        return value != null && string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    // Hanachirusato: "Draw 1 card if you pull a Weapon or Artefact card". Active during the draw window after it was played.
    // Cards drawn this way don't trigger it again (no chains).
    private void TriggerHanachirusato(ulong clientId, PlayerGachaState state, PullResult pulled)
    {
        if (state.HanachirusatoDraws <= 0) return;
        if (pulled == null || pulled.Deck != DeckType.Action || pulled.ActionData == null) return;
        if (!CategoryIs(pulled.ActionData.Category, "Weapon") && !CategoryIs(pulled.ActionData.Category, "Artefact")) return;

        for (int i = 0; i < state.HanachirusatoDraws; i++)
            GrantBonusPull(clientId, state, RandomIsActionDeck(state), null, "Hanachirusato: pulled a " + pulled.ActionData.Category, 0);
    }

    // ===================== bonus cards from Support effects =====================

    /// <summary>One extra pull that costs nothing and isn't something the player asked for.</summary>
    private void GrantBonusPull(ulong clientId, PlayerGachaState state, bool isActionDeck, Rarity? forcedTier, string reason, int knightsDepth)
    {
        PullResult result = RollOne(state, isActionDeck, forcedTier);

        bool discardedByKnights = IsKnights(result);
        result.AutoDiscard = discardedByKnights;

        if (!discardedByKnights) AddToHand(state, result);

        SendBonusCardClientRpc((int)result.Deck, (int)result.Tier, result.CardId, result.CardName,
                               result.PityTriggered, result.Was5050Roll, result.Won5050,
                               PityOf(state, isActionDeck), result.AutoDiscard, reason, ToTarget(clientId));

        Debug.Log($"[Support] Client {clientId}: bonus card -> {result.CardName} ({reason}){(discardedByKnights ? " [discarded]" : "")}");

        if (discardedByKnights)
        {
            if (knightsDepth < maxKnightsChain)
                GrantBonusPull(clientId, state, false, null, "Knights of Favonius Library: pull again", knightsDepth + 1);
        }
        else
        {
            NotifyOpponentDrewCardClientRpc(clientId);
        }
    }

    /// <summary>Seymour: gives the player an exact copy of a card (no pull, no pity change).</summary>
    private void GrantCopy(ulong clientId, PlayerGachaState state, int netId, string reason)
    {
        bool isSupport = CardNetId.IsSupport(netId);
        int id = CardNetId.Decode(netId);

        string tierText;
        string cardName;

        if (isSupport)
        {
            SupportCardData card = supportCardSource != null ? supportCardSource.Find(c => c.Id == id) : null;
            if (card == null) return;
            tierText = card.Tier;
            cardName = card.Name;
        }
        else
        {
            ActionCardData card = actionCardSource != null ? actionCardSource.Find(c => c.Id == id) : null;
            if (card == null) return;
            tierText = card.Tier;
            cardName = card.Name;
        }

        Rarity tier = Rarity.Common;
        Enum.TryParse(tierText, true, out tier);

        state.Hand.Add(netId);

        SendBonusCardClientRpc(isSupport ? (int)DeckType.Support : (int)DeckType.Action, (int)tier, id, cardName,
                               false, false, false, PityOf(state, !isSupport), false, reason, ToTarget(clientId));

        Debug.Log($"[Support] Client {clientId}: copied {cardName} ({reason})");
        NotifyOpponentDrewCardClientRpc(clientId);
    }

    // ===================== end of round: the Support effects that belong to the gacha side =====================

    /// <summary>
    /// Server only. Called once per round, after both players finished the resolution animation and before the next
    /// round starts. Applies the pull/economy Support effects of the cards that were played this round.
    ///
    /// Jade Chamber ............. +10% Legendary chance for the next draw window (per copy played)
    /// Liyue Harbor Wharf ....... 2 free pulls
    /// Golden House ............. a gold (Legendary) pull
    /// Hanachirusato ............ next draw window: pulling a Weapon/Artefact draws 1 extra card
    /// Stadium of the Sacred Flame  opponent's pity counters reset to 0
    /// Knights of Favonius Library  handled at pull time (see HandlePullRequest)
    /// Weeping Willow ........... pulled fewer than 2 cards this round: draw 2 cards
    /// Frostmoon Enclave ........ discard 2 random cards from the hand, pull the same number of new ones for free
    /// Xudong ................... next 2 pulls cost no tokens
    /// Setaria .................. no cards left in the hand besides the ones played this round: pull 2 for free
    /// Seymour .................. copy the first (oldest) card in the opponent's hand
    /// Atea ..................... next Support pull costs 1 token less
    ///
    /// Every number is multiplied by the support's multiplier (Dawn Winery / Tubby / Stormterror's Lair doubling).
    /// </summary>
    public void ApplyRoundEnd(List<RoundEndInfo> infos)
    {
        if (!IsServer || infos == null || infos.Count == 0) return;

        // 0. Cards that left the hand for good this round, and effects from last round that run out now.
        foreach (RoundEndInfo info in infos)
        {
            if (!playerStates.TryGetValue(info.ClientId, out PlayerGachaState state)) continue;

            foreach (PlayedSupportInfo played in info.Outcome.PlayedSupports)
            {
                if (played.Spent) state.Hand.Remove(played.NetId);
            }
            foreach (int discardedNetId in info.Outcome.DiscardedNetIds)
            {
                state.Hand.Remove(discardedNetId);
            }

            state.LuckBonusPercent = 0;
            state.HanachirusatoDraws = 0;
        }

        // 1. Work out what every player's played supports do.
        var plans = new List<RoundEndPlan>();

        foreach (RoundEndInfo info in infos)
        {
            if (!playerStates.TryGetValue(info.ClientId, out PlayerGachaState state)) continue;

            var plan = new RoundEndPlan { ClientId = info.ClientId, State = state };

            // Cards played this round that came back into the hand (everything except spent / discarded ones).
            int returningPlayed = Mathf.Max(0, info.Outcome.PlayedCardCount - info.Outcome.RemovedFromHandCount);
            int unplayedInHand = state.Hand.Count - returningPlayed;

            ulong opponentId = OtherClientId(infos, info.ClientId);
            playerStates.TryGetValue(opponentId, out PlayerGachaState opponentState);

            foreach (PlayedSupportInfo played in info.Outcome.PlayedSupports)
            {
                int m = Mathf.Max(1, played.Mult);

                switch (played.SupportId)
                {
                    case SupportEffectResolver.JadeChamber:
                        state.LuckBonusPercent += jadeChamberLuckPercent * m;
                        Debug.Log($"[Support] Client {info.ClientId}: Jade Chamber, +{jadeChamberLuckPercent * m}% Legendary chance next draw window.");
                        break;

                    case SupportEffectResolver.LiyueHarborWharf:
                        AddFreePulls(plan, 2 * m, "Liyue Harbor Wharf: free pull");
                        break;

                    case SupportEffectResolver.GoldenHouse:
                        plan.GoldPulls += m;
                        break;

                    case SupportEffectResolver.Hanachirusato:
                        state.HanachirusatoDraws += m;
                        Debug.Log($"[Support] Client {info.ClientId}: Hanachirusato armed for the next draw window (x{m}).");
                        break;

                    case SupportEffectResolver.StadiumOfTheSacredFlame:
                        if (opponentState != null)
                        {
                            opponentState.ActionPity.PullsSinceLegendary = 0;
                            opponentState.SupportPity.PullsSinceLegendary = 0;
                            SendPityClientRpc(0, 0, ToTarget(opponentId));
                            Debug.Log($"[Support] Client {info.ClientId}: Stadium of the Sacred Flame reset client {opponentId}'s pity.");
                        }
                        break;

                    case SupportEffectResolver.WeepingWillowOfTheLake:
                        if (state.PullsThisRound < 2)
                            AddFreePulls(plan, 2 * m, "Weeping Willow of the Lake: drew fewer than 2 cards");
                        break;

                    case SupportEffectResolver.FrostmoonEnclave:
                        plan.DiscardAndRedraw += 2 * m;
                        break;

                    case SupportEffectResolver.Xudong:
                        state.FreePullCredits += 2 * m;
                        Debug.Log($"[Support] Client {info.ClientId}: Xudong, next {2 * m} pulls are free.");
                        break;

                    case SupportEffectResolver.Setaria:
                        if (unplayedInHand <= 0)
                            AddFreePulls(plan, 2 * m, "Setaria: empty collection");
                        break;

                    case SupportEffectResolver.Seymour:
                        plan.Copies += m;
                        break;

                    case SupportEffectResolver.Atea:
                        state.SupportPullDiscount += m;
                        Debug.Log($"[Support] Client {info.ClientId}: Atea, next Support pull costs {m} token(s) less.");
                        break;
                }
            }

            plans.Add(plan);
        }

        // 2. Tell the clients their free pulls / discounts.
        foreach (RoundEndPlan plan in plans)
        {
            SendBenefitsClientRpc(plan.State.FreePullCredits, plan.State.SupportPullDiscount, ToTarget(plan.ClientId));
        }

        // 3. Cards: copies, discards, then the free pulls.
        foreach (RoundEndPlan plan in plans)
        {
            PlayerGachaState state = plan.State;

            // Seymour
            if (plan.Copies > 0 && playerStates.TryGetValue(OtherClientId(infos, plan.ClientId), out PlayerGachaState opponent))
            {
                var opponentHand = new List<int>(opponent.Hand);
                int copies = Mathf.Min(plan.Copies, opponentHand.Count);
                for (int i = 0; i < copies; i++)
                    GrantCopy(plan.ClientId, state, opponentHand[i], "Seymour: copied the opponent's first card");
            }

            // Frostmoon Enclave: discard random cards, then pull as many new ones.
            if (plan.DiscardAndRedraw > 0 && state.Hand.Count > 0)
            {
                int count = Mathf.Min(plan.DiscardAndRedraw, state.Hand.Count);
                var removed = new List<int>(count);

                for (int i = 0; i < count; i++)
                {
                    int index = state.Rng.Next(0, state.Hand.Count);
                    removed.Add(state.Hand[index]);
                    state.Hand.RemoveAt(index);
                }

                SendHandCardsRemovedClientRpc(removed.ToArray(), ToTarget(plan.ClientId));
                NotifyOpponentLostCardsClientRpc(plan.ClientId, count);
                AddFreePulls(plan, count, "Frostmoon Enclave: replacement card");
                Debug.Log($"[Support] Client {plan.ClientId}: Frostmoon Enclave discarded {count} card(s).");
            }

            // Golden House: gold (Legendary) pulls
            for (int i = 0; i < plan.GoldPulls; i++)
                GrantBonusPull(plan.ClientId, state, RandomIsActionDeck(state), Rarity.Legendary, "Golden House: gold pull", 0);

            // Free pulls
            foreach (string reason in plan.FreePullReasons)
                GrantBonusPull(plan.ClientId, state, RandomIsActionDeck(state), null, reason, 0);
        }

        // 4. A new round starts counting pulls from zero.
        foreach (PlayerGachaState state in playerStates.Values)
            state.PullsThisRound = 0;
    }

    private static void AddFreePulls(RoundEndPlan plan, int count, string reason)
    {
        for (int i = 0; i < count; i++) plan.FreePullReasons.Add(reason);
    }

    private static ulong OtherClientId(List<RoundEndInfo> infos, ulong clientId)
    {
        foreach (RoundEndInfo info in infos)
        {
            if (info.ClientId != clientId) return info.ClientId;
        }
        return ulong.MaxValue;
    }

    // ===================== RPCs =====================

    [ClientRpc] private void NotifyFeaturedCardsClientRpc(int actionId, int supportId) => OnFeaturedCardsSet?.Invoke(actionId, supportId);

    [ClientRpc]
    private void NotifyOpponentDrewCardClientRpc(ulong drawerClientId)
    {
        if (drawerClientId != NetworkManager.Singleton.LocalClientId) OnOpponentDrewCard?.Invoke(drawerClientId);
    }

    [ClientRpc]
    private void NotifyOpponentLostCardsClientRpc(ulong loserClientId, int count)
    {
        if (loserClientId != NetworkManager.Singleton.LocalClientId) OnOpponentLostCards?.Invoke(count);
    }

    [ClientRpc]
    private void SendTokensClientRpc(int tokens, ClientRpcParams rpcParams = default)
    {
        MatchStats.RecordTokens(tokens);
        OnMyTokensChanged?.Invoke(tokens);
    }
    [ClientRpc] private void SendPullFailedClientRpc(string reason, ClientRpcParams rpcParams = default) => OnPullFailed?.Invoke(reason);
    [ClientRpc] private void SendBenefitsClientRpc(int freePulls, int supportDiscount, ClientRpcParams rpcParams = default) => OnMyPullBenefitsChanged?.Invoke(freePulls, supportDiscount);
    [ClientRpc] private void SendHandCardsRemovedClientRpc(int[] netIds, ClientRpcParams rpcParams = default) => OnMyHandCardsRemoved?.Invoke(netIds);

    [ClientRpc]
    private void SendPityClientRpc(int actionPulls, int supportPulls, ClientRpcParams rpcParams = default)
    {
        int threshold = pullConfig != null ? pullConfig.pityThreshold : 90;
        OnPityUpdated?.Invoke(actionPulls, threshold, true);
        OnPityUpdated?.Invoke(supportPulls, threshold, false);
    }

    [ClientRpc]
    private void SendPullResultClientRpc(int deckType, int tier, int cardId, string cardName, bool pityTriggered, bool was5050Roll, bool won5050, int pullsSinceLegendary, bool autoDiscard, ClientRpcParams rpcParams = default)
    {
        OnPityUpdated?.Invoke(pullsSinceLegendary, pullConfig.pityThreshold, deckType == 0);

        PullResult result = BuildClientResult(deckType, tier, cardId, cardName, pityTriggered, was5050Roll, won5050, autoDiscard);
        MatchStats.RecordPull(result, true);
        OnMyPullResolved?.Invoke(result);
    }

    [ClientRpc]
    private void SendBonusCardClientRpc(int deckType, int tier, int cardId, string cardName, bool pityTriggered, bool was5050Roll, bool won5050, int pullsSinceLegendary, bool autoDiscard, string reason, ClientRpcParams rpcParams = default)
    {
        OnPityUpdated?.Invoke(pullsSinceLegendary, pullConfig.pityThreshold, deckType == 0);

        PullResult result = BuildClientResult(deckType, tier, cardId, cardName, pityTriggered, was5050Roll, won5050, autoDiscard);
        MatchStats.RecordPull(result, false);
        OnMyBonusCardGranted?.Invoke(result, reason);
    }

    private PullResult BuildClientResult(int deckType, int tier, int cardId, string cardName, bool pityTriggered, bool was5050Roll, bool won5050, bool autoDiscard)
    {
        var result = new PullResult
        {
            Deck = (DeckType)deckType,
            Tier = (Rarity)tier,
            CardId = cardId,
            CardName = cardName,
            PityTriggered = pityTriggered,
            Was5050Roll = was5050Roll,
            Won5050 = won5050,
            AutoDiscard = autoDiscard
        };

        var deckMgr = FindFirstObjectByType<DeckManager>();
        if (deckMgr != null)
        {
            if (result.Deck == DeckType.Action)
                result.ActionData = deckMgr.loadedActionCards?.Find(c => c.Id == cardId);
            else
                result.SupportData = deckMgr.loadedSupportCards?.Find(c => c.Id == cardId);
        }

        return result;
    }

    private void DispatchPullResult(PullResult result, int pullsSinceLegendary, ClientRpcParams rpcParams)
    {
        SendPullResultClientRpc((int)result.Deck, (int)result.Tier, result.CardId, result.CardName, result.PityTriggered, result.Was5050Roll, result.Won5050, pullsSinceLegendary, result.AutoDiscard, rpcParams);
    }

    private ClientRpcParams ToTarget(ulong clientId) => new ClientRpcParams
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
    };

    public (int pulls, int threshold) GetLocalPlayerPity(bool isActionDeck)
    {
        if (NetworkManager == null || !NetworkManager.IsListening)
            return (0, pullConfig != null ? pullConfig.pityThreshold : 90);

        if (playerStates.TryGetValue(NetworkManager.LocalClientId, out var state))
        {
            int pulls = isActionDeck ? state.ActionPity.PullsSinceLegendary : state.SupportPity.PullsSinceLegendary;
            return (pulls, pullConfig.pityThreshold);
        }

        return (0, pullConfig != null ? pullConfig.pityThreshold : 90);
    }
}
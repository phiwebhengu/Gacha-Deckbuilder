using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GachaManager : NetworkBehaviour
{
    public static GachaManager Instance { get; private set; }

    [Header("Game Mode Settings")]
    [Tooltip("On for the AI and Tutorial scenes. The same pull, pity, token and Support-effect rules run locally, without a network.")]
    [SerializeField] public bool isOfflineAIRival = false;

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

    /// <summary>In an offline match the local player is "client" 0 and the AI is "client" 1.</summary>
    public const ulong OfflinePlayerId = 0;
    public const ulong OfflineOpponentId = 1;

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

    // Offline only: the AI's hand (network ids, oldest first), owned by EndTurnManager. Seymour copies from it.
    private List<int> offlineOpponentHand;

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

    /// <summary>True where the game rules run: the host in an online match, or this machine in an offline one.</summary>
    private bool RunsGameRules => isOfflineAIRival || IsServer;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (isOfflineAIRival) return;

        // New match: clear the numbers shown on the Outcome page (every client keeps its own stats).
        if (IsClient) MatchStats.Reset(startingTokens);

        if (!IsServer) return;

        LoadDeckSources();

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            RegisterPlayer(clientId);

        NetworkManager.Singleton.SceneManager.OnSynchronizeComplete += HandleSynchronizeComplete;
        NetworkManager.Singleton.OnClientDisconnectCallback += UnregisterPlayer;
    }

    private void Start()
    {
        if (!isOfflineAIRival) return;

        MatchStats.Reset(startingTokens);
        LoadDeckSources();
        RegisterPlayer(OfflinePlayerId);
    }

    private void LoadDeckSources()
    {
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
    }

    public override void OnNetworkDespawn()
    {
        if (isOfflineAIRival || !IsServer || NetworkManager.Singleton == null) return;
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
        SendTokens(clientId, startingTokens);
    }

    private void UnregisterPlayer(ulong clientId) => playerStates.Remove(clientId);

    /// <summary>Offline only: lets Seymour copy from the AI's hand. EndTurnManager owns the list.</summary>
    public void SetOfflineOpponentHand(List<int> hand) => offlineOpponentHand = hand;

    /// <summary>Server / offline only. Gives a player tokens (used by Support card effects such as Paimon / Katheryne / Vanarana).</summary>
    public void AddTokens(ulong clientId, int amount)
    {
        if (!RunsGameRules || amount == 0) return;
        if (!playerStates.TryGetValue(clientId, out var state)) return;

        state.Tokens = Mathf.Max(0, state.Tokens + amount);
        SendTokens(clientId, state.Tokens);
    }

    public void SetFeaturedCards(int actionId, int supportId)
    {
        if (!RunsGameRules) return;
        foreach (var state in playerStates.Values)
        {
            state.FeaturedActionCardId = actionId;
            state.FeaturedSupportCardId = supportId;
        }

        if (isOfflineAIRival) OnFeaturedCardsSet?.Invoke(actionId, supportId);
        else NotifyFeaturedCardsClientRpc(actionId, supportId);
    }

    // --- Allows UI to safely fetch tokens if it missed the initial message ---
    public int GetLocalPlayerTokens()
    {
        return TryGetLocalState(out var state) ? state.Tokens : startingTokens;
    }

    public (int pulls, int threshold) GetLocalPlayerPity(bool isActionDeck)
    {
        int threshold = pullConfig != null ? pullConfig.pityThreshold : 4;
        return TryGetLocalState(out var state) ? (PityOf(state, isActionDeck), threshold) : (0, threshold);
    }

    private bool TryGetLocalState(out PlayerGachaState state)
    {
        state = null;
        if (isOfflineAIRival) return playerStates.TryGetValue(OfflinePlayerId, out state);
        if (NetworkManager == null || !NetworkManager.IsListening) return false;
        return playerStates.TryGetValue(NetworkManager.LocalClientId, out state);
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

    public void RequestPullAction()
    {
        if (isOfflineAIRival) HandlePullRequest(OfflinePlayerId, true);
        else RequestPullActionServerRpc();
    }

    public void RequestPullSupport()
    {
        if (isOfflineAIRival) HandlePullRequest(OfflinePlayerId, false);
        else RequestPullSupportServerRpc();
    }

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
        if (!playerStates.TryGetValue(clientId, out var state)) { SendPullFailed(clientId, "Not ready yet."); return; }

        int cost = ResolveCost(pullCost, !isActionDeck, state.FreePullCredits, state.SupportPullDiscount,
                               out bool usesDiscount, out bool usesFreeCredit);

        if (state.Tokens < cost) { SendPullFailed(clientId, "Out of tokens."); return; }

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
            SendBenefits(clientId, state.FreePullCredits, state.SupportPullDiscount);

        SendTokens(clientId, state.Tokens);
        SendPullResult(clientId, result, PityOf(state, isActionDeck));

        if (discardedByKnights)
        {
            // The card never joins the hand, so the opponent doesn't get a new card back for it.
            Debug.Log($"[Support] Client {clientId}: Knights of Favonius Library pulled, discarded, pulling again for free.");
            GrantBonusPull(clientId, state, false, null, "Knights of Favonius Library: pull again", 1);
        }
        else
        {
            AddToHand(state, result);
            NotifyOpponentDrewCard(clientId);
            TriggerHanachirusato(clientId, state, result);
        }
    }

    private PullResult RollOne(PlayerGachaState state, bool isActionDeck, Rarity? forcedTier)
    {
        if (actionCardSource == null || supportCardSource == null) LoadDeckSources();

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

        SendBonusCard(clientId, result, PityOf(state, isActionDeck), reason);

        Debug.Log($"[Support] Client {clientId}: bonus card -> {result.CardName} ({reason}){(discardedByKnights ? " [discarded]" : "")}");

        if (discardedByKnights)
        {
            if (knightsDepth < maxKnightsChain)
                GrantBonusPull(clientId, state, false, null, "Knights of Favonius Library: pull again", knightsDepth + 1);
        }
        else
        {
            NotifyOpponentDrewCard(clientId);
        }
    }

    /// <summary>Seymour: gives the player an exact copy of a card (no pull, no pity change).</summary>
    private void GrantCopy(ulong clientId, PlayerGachaState state, int netId, string reason)
    {
        bool isSupport = CardNetId.IsSupport(netId);
        int id = CardNetId.Decode(netId);

        if (actionCardSource == null || supportCardSource == null) LoadDeckSources();

        var result = new PullResult { Deck = isSupport ? DeckType.Support : DeckType.Action, CardId = id };
        string tierText;

        if (isSupport)
        {
            SupportCardData card = supportCardSource != null ? supportCardSource.Find(c => c.Id == id) : null;
            if (card == null) return;
            result.SupportData = card;
            result.CardName = card.Name;
            tierText = card.Tier;
        }
        else
        {
            ActionCardData card = actionCardSource != null ? actionCardSource.Find(c => c.Id == id) : null;
            if (card == null) return;
            result.ActionData = card;
            result.CardName = card.Name;
            tierText = card.Tier;
        }

        Enum.TryParse(tierText, true, out Rarity tier);
        result.Tier = tier;

        state.Hand.Add(netId);
        SendBonusCard(clientId, result, PityOf(state, !isSupport), reason);

        Debug.Log($"[Support] Client {clientId}: copied {result.CardName} ({reason})");
        NotifyOpponentDrewCard(clientId);
    }

    // ===================== end of round: the Support effects that belong to the gacha side =====================

    /// <summary>
    /// Server / offline only. Called once per round, after both players finished the resolution animation and before the next
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
    /// Offline, only the player has gacha state, so the AI's pull-side Supports do nothing yet.
    /// </summary>
    public void ApplyRoundEnd(List<RoundEndInfo> infos)
    {
        if (!RunsGameRules || infos == null || infos.Count == 0) return;

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
                            SendPity(opponentId, 0, 0);
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

        // 2. Tell the players their free pulls / discounts.
        foreach (RoundEndPlan plan in plans)
        {
            SendBenefits(plan.ClientId, plan.State.FreePullCredits, plan.State.SupportPullDiscount);
        }

        // 3. Cards: copies, discards, then the free pulls.
        foreach (RoundEndPlan plan in plans)
        {
            PlayerGachaState state = plan.State;

            // Seymour
            if (plan.Copies > 0)
            {
                List<int> opponentHand = OpponentHandFor(infos, plan.ClientId);
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

                SendHandCardsRemoved(plan.ClientId, removed.ToArray());
                NotifyOpponentLostCards(plan.ClientId, count);
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

    private List<int> OpponentHandFor(List<RoundEndInfo> infos, ulong clientId)
    {
        if (isOfflineAIRival)
            return clientId == OfflinePlayerId && offlineOpponentHand != null ? new List<int>(offlineOpponentHand) : new List<int>();

        return playerStates.TryGetValue(OtherClientId(infos, clientId), out PlayerGachaState opponent)
            ? new List<int>(opponent.Hand)
            : new List<int>();
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

    // ===================== sending results: an RPC online, a direct event offline =====================

    private bool IsOfflinePlayer(ulong clientId) => isOfflineAIRival && clientId == OfflinePlayerId;

    private void SendTokens(ulong clientId, int tokens)
    {
        if (IsOfflinePlayer(clientId)) ReceiveTokens(tokens);
        else if (!isOfflineAIRival) SendTokensClientRpc(tokens, ToTarget(clientId));
    }

    private void SendPullFailed(ulong clientId, string reason)
    {
        if (IsOfflinePlayer(clientId)) OnPullFailed?.Invoke(reason);
        else if (!isOfflineAIRival) SendPullFailedClientRpc(reason, ToTarget(clientId));
    }

    private void SendBenefits(ulong clientId, int freePulls, int supportDiscount)
    {
        if (IsOfflinePlayer(clientId)) OnMyPullBenefitsChanged?.Invoke(freePulls, supportDiscount);
        else if (!isOfflineAIRival) SendBenefitsClientRpc(freePulls, supportDiscount, ToTarget(clientId));
    }

    private void SendHandCardsRemoved(ulong clientId, int[] netIds)
    {
        if (IsOfflinePlayer(clientId)) OnMyHandCardsRemoved?.Invoke(netIds);
        else if (!isOfflineAIRival) SendHandCardsRemovedClientRpc(netIds, ToTarget(clientId));
    }

    private void SendPity(ulong clientId, int actionPulls, int supportPulls)
    {
        if (IsOfflinePlayer(clientId)) ReceivePity(actionPulls, supportPulls);
        else if (!isOfflineAIRival) SendPityClientRpc(actionPulls, supportPulls, ToTarget(clientId));
    }

    private void SendPullResult(ulong clientId, PullResult result, int pullsSinceLegendary)
    {
        if (IsOfflinePlayer(clientId)) ReceivePullResult(result, pullsSinceLegendary);
        else if (!isOfflineAIRival)
            SendPullResultClientRpc((int)result.Deck, (int)result.Tier, result.CardId, result.CardName, result.PityTriggered,
                                    result.Was5050Roll, result.Won5050, pullsSinceLegendary, result.AutoDiscard, ToTarget(clientId));
    }

    private void SendBonusCard(ulong clientId, PullResult result, int pullsSinceLegendary, string reason)
    {
        if (IsOfflinePlayer(clientId)) ReceiveBonusCard(result, pullsSinceLegendary, reason);
        else if (!isOfflineAIRival)
            SendBonusCardClientRpc((int)result.Deck, (int)result.Tier, result.CardId, result.CardName, result.PityTriggered,
                                   result.Was5050Roll, result.Won5050, pullsSinceLegendary, result.AutoDiscard, reason, ToTarget(clientId));
    }

    // Offline, the AI's card backs are handled by EndTurnManager, so these two only matter online.
    private void NotifyOpponentDrewCard(ulong drawerClientId)
    {
        if (!isOfflineAIRival) NotifyOpponentDrewCardClientRpc(drawerClientId);
    }

    private void NotifyOpponentLostCards(ulong loserClientId, int count)
    {
        if (!isOfflineAIRival) NotifyOpponentLostCardsClientRpc(loserClientId, count);
    }

    // ===================== receiving (runs on the player's machine) =====================

    private void ReceiveTokens(int tokens)
    {
        MatchStats.RecordTokens(tokens);
        OnMyTokensChanged?.Invoke(tokens);
    }

    private void ReceivePity(int actionPulls, int supportPulls)
    {
        int threshold = pullConfig != null ? pullConfig.pityThreshold : 4;
        OnPityUpdated?.Invoke(actionPulls, threshold, true);
        OnPityUpdated?.Invoke(supportPulls, threshold, false);
    }

    private void ReceivePullResult(PullResult result, int pullsSinceLegendary)
    {
        OnPityUpdated?.Invoke(pullsSinceLegendary, pullConfig.pityThreshold, result.Deck == DeckType.Action);
        MatchStats.RecordPull(result, true);
        OnMyPullResolved?.Invoke(result);
    }

    private void ReceiveBonusCard(PullResult result, int pullsSinceLegendary, string reason)
    {
        OnPityUpdated?.Invoke(pullsSinceLegendary, pullConfig.pityThreshold, result.Deck == DeckType.Action);
        MatchStats.RecordPull(result, false);
        OnMyBonusCardGranted?.Invoke(result, reason);
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

    [ClientRpc] private void SendTokensClientRpc(int tokens, ClientRpcParams rpcParams = default) => ReceiveTokens(tokens);
    [ClientRpc] private void SendPullFailedClientRpc(string reason, ClientRpcParams rpcParams = default) => OnPullFailed?.Invoke(reason);
    [ClientRpc] private void SendBenefitsClientRpc(int freePulls, int supportDiscount, ClientRpcParams rpcParams = default) => OnMyPullBenefitsChanged?.Invoke(freePulls, supportDiscount);
    [ClientRpc] private void SendHandCardsRemovedClientRpc(int[] netIds, ClientRpcParams rpcParams = default) => OnMyHandCardsRemoved?.Invoke(netIds);
    [ClientRpc] private void SendPityClientRpc(int actionPulls, int supportPulls, ClientRpcParams rpcParams = default) => ReceivePity(actionPulls, supportPulls);

    [ClientRpc]
    private void SendPullResultClientRpc(int deckType, int tier, int cardId, string cardName, bool pityTriggered, bool was5050Roll, bool won5050, int pullsSinceLegendary, bool autoDiscard, ClientRpcParams rpcParams = default)
    {
        ReceivePullResult(BuildClientResult(deckType, tier, cardId, cardName, pityTriggered, was5050Roll, won5050, autoDiscard), pullsSinceLegendary);
    }

    [ClientRpc]
    private void SendBonusCardClientRpc(int deckType, int tier, int cardId, string cardName, bool pityTriggered, bool was5050Roll, bool won5050, int pullsSinceLegendary, bool autoDiscard, string reason, ClientRpcParams rpcParams = default)
    {
        ReceiveBonusCard(BuildClientResult(deckType, tier, cardId, cardName, pityTriggered, was5050Roll, won5050, autoDiscard), pullsSinceLegendary, reason);
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

    private ClientRpcParams ToTarget(ulong clientId) => new ClientRpcParams
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
    };
}

using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GachaManager : NetworkBehaviour
{
    public static GachaManager Instance { get; private set; }

    [Header("Game Mode Settings")]
    [SerializeField] public bool isOfflineAIRival = false;

    [Header("Pull Config (ScriptableObject)")]
    public PullConfig pullConfig;

    [Header("Starting State")]
    public int startingTokens = 10;

    [Header("Pull Cost")]
    [Tooltip("Tokens one pull costs. Atea takes 1 off the next Support pull, Xudong makes pulls free.")]
    public int pullCost = 1;

    [Header("Support Card Effects")]
    [Range(0f, 1f)] public float bonusPullActionChance = 0.5f;
    public int jadeChamberLuckPercent = 10;
    public int maxKnightsChain = 10;

    private class PlayerGachaState
    {
        public int Tokens;
        public PityState ActionPity = new PityState();
        public PityState SupportPity = new PityState();
        public System.Random Rng;
        public int FeaturedActionCardId;
        public int FeaturedSupportCardId;

        public readonly List<int> Hand = new List<int>();

        public int FreePullCredits;
        public int SupportPullDiscount;
        public int LuckBonusPercent;
        public int HanachirusatoDraws;
        public int PullsThisRound;
    }

    public class RoundEndInfo
    {
        public ulong ClientId;
        public SideOutcome Outcome;
    }

    private List<ActionCardData> actionCardSource;
    private List<SupportCardData> supportCardSource;
    private readonly Dictionary<ulong, PlayerGachaState> playerStates = new Dictionary<ulong, PlayerGachaState>();

    private PlayerGachaState offlineLocalState;

    public event Action<int> OnMyTokensChanged;
    public event Action<PullResult> OnMyPullResolved;
    public event Action<string> OnPullFailed;
    public event Action<ulong> OnOpponentDrewCard;
    public event Action<int, int> OnFeaturedCardsSet;
    public event Action<int, int, bool> OnPityUpdated;

    public event Action<int, int> OnMyPullBenefitsChanged;
    public event Action<PullResult, string> OnMyBonusCardGranted;
    public event Action<int[]> OnMyHandCardsRemoved;
    public event Action<int> OnOpponentLostCards;

    public int PullCost => pullCost;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (isOfflineAIRival) return;

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
        if (isOfflineAIRival)
        {
            var deckMgr = FindFirstObjectByType<DeckManager>();
            if (deckMgr != null)
            {
                actionCardSource = deckMgr.loadedActionCards;
                supportCardSource = deckMgr.loadedSupportCards;
            }

            offlineLocalState = new PlayerGachaState
            {
                Tokens = startingTokens,
                ActionPity = new PityState(),
                SupportPity = new PityState(),
                Rng = new System.Random(Environment.TickCount)
            };
            MatchStats.Reset(startingTokens);
            OnMyTokensChanged?.Invoke(startingTokens);
        }
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
            ActionPity = new PityState(),
            SupportPity = new PityState(),
            Rng = new System.Random(seed)
        };
        SendTokensClientRpc(startingTokens, ToTarget(clientId));
    }

    private void UnregisterPlayer(ulong clientId) => playerStates.Remove(clientId);

    public void AddTokens(ulong clientId, int amount)
    {
        if (isOfflineAIRival) return;
        if (!IsServer || amount == 0) return;
        if (!playerStates.TryGetValue(clientId, out var state)) return;

        state.Tokens = Mathf.Max(0, state.Tokens + amount);
        SendTokensClientRpc(state.Tokens, ToTarget(clientId));
    }

    public int GetLocalPlayerTokens()
    {
        if (isOfflineAIRival && offlineLocalState != null) return offlineLocalState.Tokens;
        if (NetworkManager == null || !NetworkManager.IsListening) return startingTokens;
        if (playerStates.TryGetValue(NetworkManager.LocalClientId, out var state))
        {
            return state.Tokens;
        }
        return startingTokens;
    }

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
        if (isOfflineAIRival)
        {
            HandleOfflinePull(true);
            return;
        }
        RequestPullActionServerRpc();
    }

    public void RequestPullSupport()
    {
        if (isOfflineAIRival)
        {
            HandleOfflinePull(false);
            return;
        }
        RequestPullSupportServerRpc();
    }

    private void HandleOfflinePull(bool isActionDeck)
    {
        if (offlineLocalState == null)
        {
            offlineLocalState = new PlayerGachaState
            {
                Tokens = startingTokens,
                ActionPity = new PityState(),
                SupportPity = new PityState(),
                Rng = new System.Random(Environment.TickCount)
            };
        }

        if (actionCardSource == null || supportCardSource == null) LoadDeckSources();

        int cost = ResolveCost(pullCost, !isActionDeck, offlineLocalState.FreePullCredits, offlineLocalState.SupportPullDiscount,
                               out bool usesDiscount, out bool usesFreeCredit);

        if (offlineLocalState.Tokens < cost)
        {
            OnPullFailed?.Invoke("Out of tokens.");
            return;
        }

        if (usesDiscount) offlineLocalState.SupportPullDiscount--;
        if (usesFreeCredit) offlineLocalState.FreePullCredits--;
        offlineLocalState.Tokens -= cost;
        offlineLocalState.PullsThisRound++;

        // RollOne executes through GachaEngine, which modifies offlineLocalState's PityState
        PullResult result = RollOne(offlineLocalState, isActionDeck, null);

        int currentPity = isActionDeck ? offlineLocalState.ActionPity.PullsSinceLegendary : offlineLocalState.SupportPity.PullsSinceLegendary;
        int threshold = pullConfig != null ? pullConfig.pityThreshold : 4;

        OnPityUpdated?.Invoke(currentPity, threshold, isActionDeck);

        MatchStats.RecordTokens(offlineLocalState.Tokens);
        OnMyTokensChanged?.Invoke(offlineLocalState.Tokens);

        MatchStats.RecordPull(result, true);
        OnMyPullResolved?.Invoke(result);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPullActionServerRpc(ServerRpcParams rpcParams = default)
    {
        if (isOfflineAIRival) return;
        HandlePullRequest(rpcParams.Receive.SenderClientId, true);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPullSupportServerRpc(ServerRpcParams rpcParams = default)
    {
        if (isOfflineAIRival) return;
        HandlePullRequest(rpcParams.Receive.SenderClientId, false);
    }

    private void HandlePullRequest(ulong clientId, bool isActionDeck)
    {
        var target = ToTarget(clientId);
        if (!playerStates.TryGetValue(clientId, out var state)) { SendPullFailedClientRpc("Not ready yet.", target); return; }

        int cost = ResolveCost(pullCost, !isActionDeck, state.FreePullCredits, state.SupportPullDiscount,
                               out bool usesDiscount, out bool usesFreeCredit);

        if (state.Tokens < cost) { SendPullFailedClientRpc("Out of tokens.", target); return; }

        if (usesDiscount) state.SupportPullDiscount--;
        if (usesFreeCredit) state.FreePullCredits--;
        state.Tokens -= cost;
        state.PullsThisRound++;

        PullResult result = RollOne(state, isActionDeck, null);

        bool discardedByKnights = IsKnights(result);
        result.AutoDiscard = discardedByKnights;

        if (usesDiscount || usesFreeCredit)
            SendBenefitsClientRpc(state.FreePullCredits, state.SupportPullDiscount, target);

        SendTokensClientRpc(state.Tokens, target);
        DispatchPullResult(result, PityOf(state, isActionDeck), target);

        if (discardedByKnights)
        {
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
        if (result == null || result.CardId <= 0) return;
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

    private void TriggerHanachirusato(ulong clientId, PlayerGachaState state, PullResult pulled)
    {
        if (state.HanachirusatoDraws <= 0) return;
        if (pulled == null || pulled.Deck != DeckType.Action || pulled.ActionData == null) return;
        if (!CategoryIs(pulled.ActionData.Category, "Weapon") && !CategoryIs(pulled.ActionData.Category, "Artefact")) return;

        for (int i = 0; i < state.HanachirusatoDraws; i++)
            GrantBonusPull(clientId, state, RandomIsActionDeck(state), null, "Hanachirusato: pulled a " + pulled.ActionData.Category, 0);
    }

    private void GrantBonusPull(ulong clientId, PlayerGachaState state, bool isActionDeck, Rarity? forcedTier, string reason, int knightsDepth)
    {
        PullResult result = RollOne(state, isActionDeck, forcedTier);

        bool discardedByKnights = IsKnights(result);
        result.AutoDiscard = discardedByKnights;

        if (!discardedByKnights) AddToHand(state, result);

        SendBonusCardClientRpc((int)result.Deck, (int)result.Tier, result.CardId, result.CardName,
                               result.PityTriggered, result.Was5050Roll, result.Won5050,
                               PityOf(state, isActionDeck), result.AutoDiscard, reason, ToTarget(clientId));

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

    public void ApplyRoundEnd(List<RoundEndInfo> infos)
    {
        if (isOfflineAIRival || !IsServer || infos == null || infos.Count == 0) return;
    }

    public void SetFeaturedCards(int actionId, int supportId)
    {
        if (isOfflineAIRival) return;
        if (!IsServer) return;
        foreach (var state in playerStates.Values)
        {
            state.FeaturedActionCardId = actionId;
            state.FeaturedSupportCardId = supportId;
        }
        NotifyFeaturedCardsClientRpc(actionId, supportId);
    }

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
        if (isOfflineAIRival && offlineLocalState != null)
        {
            int pulls = isActionDeck ? offlineLocalState.ActionPity.PullsSinceLegendary : offlineLocalState.SupportPity.PullsSinceLegendary;
            return (pulls, pullConfig != null ? pullConfig.pityThreshold : 4);
        }

        if (NetworkManager == null || !NetworkManager.IsListening)
            return (0, pullConfig != null ? pullConfig.pityThreshold : 4);

        if (playerStates.TryGetValue(NetworkManager.LocalClientId, out var state))
        {
            int pulls = isActionDeck ? state.ActionPity.PullsSinceLegendary : state.SupportPity.PullsSinceLegendary;
            return (pulls, pullConfig.pityThreshold);
        }

        return (0, pullConfig != null ? pullConfig.pityThreshold : 4);
    }
}
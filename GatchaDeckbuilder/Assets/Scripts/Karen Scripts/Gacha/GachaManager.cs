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

    private class PlayerGachaState
    {
        public int Tokens;
        public PityState ActionPity = new PityState();
        public PityState SupportPity = new PityState();
        public System.Random Rng;
        public int FeaturedActionCardId;
        public int FeaturedSupportCardId;
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

    private void Awake()
    {
        if (IsClient) Instance = this;
    }

    public override void OnNetworkSpawn()
    {
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

    public void RequestPullAction() => RequestPullActionServerRpc();
    public void RequestPullSupport() => RequestPullSupportServerRpc();

    [ServerRpc(RequireOwnership = false)]
    private void RequestPullActionServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        var target = ToTarget(clientId);
        if (!playerStates.TryGetValue(clientId, out var state)) { SendPullFailedClientRpc("Not ready yet.", target); return; }
        if (state.Tokens <= 0) { SendPullFailedClientRpc("Out of tokens.", target); return; }

        state.Tokens--;
        PullResult result = GachaEngine.PullAction(pullConfig, state.ActionPity, state.Rng, actionCardSource, state.FeaturedActionCardId);

        SendTokensClientRpc(state.Tokens, target);
        DispatchPullResult(result, state.ActionPity.PullsSinceLegendary, target);
        NotifyOpponentDrewCardClientRpc(clientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPullSupportServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        var target = ToTarget(clientId);
        if (!playerStates.TryGetValue(clientId, out var state)) { SendPullFailedClientRpc("Not ready yet.", target); return; }
        if (state.Tokens <= 0) { SendPullFailedClientRpc("Out of tokens.", target); return; }

        state.Tokens--;
        PullResult result = GachaEngine.PullSupport(pullConfig, state.SupportPity, state.Rng, supportCardSource, state.FeaturedSupportCardId);

        SendTokensClientRpc(state.Tokens, target);
        DispatchPullResult(result, state.SupportPity.PullsSinceLegendary, target);
        NotifyOpponentDrewCardClientRpc(clientId);
    }

    [ClientRpc] private void NotifyFeaturedCardsClientRpc(int actionId, int supportId) => OnFeaturedCardsSet?.Invoke(actionId, supportId);

    [ClientRpc]
    private void NotifyOpponentDrewCardClientRpc(ulong drawerClientId)
    {
        if (drawerClientId != NetworkManager.Singleton.LocalClientId) OnOpponentDrewCard?.Invoke(drawerClientId);
    }

    [ClientRpc] private void SendTokensClientRpc(int tokens, ClientRpcParams rpcParams = default) => OnMyTokensChanged?.Invoke(tokens);
    [ClientRpc] private void SendPullFailedClientRpc(string reason, ClientRpcParams rpcParams = default) => OnPullFailed?.Invoke(reason);

    [ClientRpc]
    private void SendPullResultClientRpc(int deckType, int tier, int cardId, string cardName, bool pityTriggered, bool was5050Roll, bool won5050, int pullsSinceLegendary, ClientRpcParams rpcParams = default)
    {
        OnPityUpdated?.Invoke(pullsSinceLegendary, pullConfig.pityThreshold, deckType == 0);

        var result = new PullResult
        {
            Deck = (DeckType)deckType,
            Tier = (Rarity)tier,
            CardId = cardId,
            CardName = cardName,
            PityTriggered = pityTriggered,
            Was5050Roll = was5050Roll,
            Won5050 = won5050
        };

        var deckMgr = FindFirstObjectByType<DeckManager>();
        if (deckMgr != null)
        {
            if (result.Deck == DeckType.Action)
                result.ActionData = deckMgr.loadedActionCards?.Find(c => c.Id == cardId);
            else
                result.SupportData = deckMgr.loadedSupportCards?.Find(c => c.Id == cardId);
        }

        OnMyPullResolved?.Invoke(result);
    }

    private void DispatchPullResult(PullResult result, int pullsSinceLegendary, ClientRpcParams rpcParams)
    {
        SendPullResultClientRpc((int)result.Deck, (int)result.Tier, result.CardId, result.CardName, result.PityTriggered, result.Was5050Roll, result.Won5050, pullsSinceLegendary, rpcParams);
    }

    private ClientRpcParams ToTarget(ulong clientId) => new ClientRpcParams
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
    };
}
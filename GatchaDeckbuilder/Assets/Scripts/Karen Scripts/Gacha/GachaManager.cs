using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GachaManager : NetworkBehaviour
{
    public static GachaManager Instance { get; private set; }

    [Header("CSV File References (server only needs these)")]
    public TextAsset actionDeckCsv;
    public TextAsset supportDeckCsv;

    [Header("Pull Settings")]
    public int startingTokens = 10;
    public int pityThreshold = 5;

    // ---------- Server-only state ----------

    private class PlayerGachaState
    {
        public int Tokens;
        public CardTierPool<ActionCardData> ActionPool;
        public CardTierPool<SupportCardData> SupportPool;
    }

    private List<ActionCardData> actionCardSource;
    private List<SupportCardData> supportCardSource;
    private readonly Dictionary<ulong, PlayerGachaState> playerStates = new Dictionary<ulong, PlayerGachaState>();

    // ---------- Local ("this client's own result") events for UI ----------
    // These only ever fire on a client for THAT client's own pulls.

    public event Action<int> OnMyTokensChanged;
    public event Action<ActionCardData, int> OnMyActionCardPulled;   // card, pulls left until pity
    public event Action<SupportCardData, int> OnMySupportCardPulled; // card, pulls left until pity
    public event Action<string> OnPullFailed;                        // reason, e.g. "Out of tokens"
    public event Action<ulong> OnOpponentDrewCard;

    private void Awake()
    {
        if (IsClient)
        {
            Instance = this;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        actionCardSource = actionDeckCsv != null ? CardLoader.LoadActionDeck(actionDeckCsv) : null;
        supportCardSource = supportDeckCsv != null ? CardLoader.LoadSupportDeck(supportDeckCsv) : null;

        if (actionCardSource == null) Debug.LogWarning("⚠️ GachaNetworkManager: Action Deck CSV is not assigned.");
        if (supportCardSource == null) Debug.LogWarning("⚠️ GachaNetworkManager: Support Deck CSV is not assigned.");

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            RegisterPlayer(clientId);

        // Late joiners go through Synchronize, not a Load event — OnSynchronizeComplete
        // is what fires once THEIR sync (scenes + NetworkObjects) is actually done.
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

    private void HandleSynchronizeComplete(ulong clientId)
    {
        RegisterPlayer(clientId);
    }

    private void RegisterPlayer(ulong clientId)
    {
        if (playerStates.ContainsKey(clientId)) return;
        if (actionCardSource == null || supportCardSource == null) return;

        var state = new PlayerGachaState
        {
            Tokens = startingTokens,
            ActionPool = new CardTierPool<ActionCardData>(actionCardSource, c => c.Tier, pityThreshold),
            SupportPool = new CardTierPool<SupportCardData>(supportCardSource, c => c.Tier, pityThreshold)
        };
        playerStates[clientId] = state;

        SendTokensClientRpc(state.Tokens, ToTarget(clientId));
    }

    private void UnregisterPlayer(ulong clientId)
    {
        playerStates.Remove(clientId);
    }

    // ---------- Public API — call these from UI ----------

    public void RequestPullAction() => RequestPullActionServerRpc();
    public void RequestPullSupport() => RequestPullSupportServerRpc();

    [ServerRpc(RequireOwnership = false)]
    private void RequestPullActionServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        ClientRpcParams target = ToTarget(clientId);

        if (!playerStates.TryGetValue(clientId, out var state))
        {
            SendPullFailedClientRpc("Not ready yet — try again in a moment.", target);
            return;
        }
        if (state.Tokens <= 0)
        {
            SendPullFailedClientRpc("Out of tokens.", target);
            return;
        }

        state.Tokens--;
        ActionCardData drawn = state.ActionPool.Pull();

        // 1. Send the actual card details ONLY to the player who pulled
        SendTokensClientRpc(state.Tokens, target);
        SendActionCardClientRpc(
            drawn.Id, drawn.Name, drawn.Category, drawn.Tier, drawn.WeaponType, drawn.Value, drawn.Role,
            state.ActionPool.PullsUntilGuaranteedLegendary, target);

        // 2. ADD THIS: Tell everyone else that this player drew a card
        NotifyOpponentDrewCardClientRpc(clientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPullSupportServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        ClientRpcParams target = ToTarget(clientId);

        if (!playerStates.TryGetValue(clientId, out var state))
        {
            SendPullFailedClientRpc("Not ready yet — try again in a moment.", target);
            return;
        }
        if (state.Tokens <= 0)
        {
            SendPullFailedClientRpc("Out of tokens.", target);
            return;
        }

        state.Tokens--;
        SupportCardData drawn = state.SupportPool.Pull();

        // 1. Send the actual card details ONLY to the player who pulled
        SendTokensClientRpc(state.Tokens, target);
        SendSupportCardClientRpc(
            drawn.Id, drawn.Name, drawn.Tier, drawn.Effect, drawn.EffectType,
            state.SupportPool.PullsUntilGuaranteedLegendary, target);

        // 2. ADD THIS: Tell everyone else that this player drew a card
        NotifyOpponentDrewCardClientRpc(clientId);
    }

    // ---------- ClientRpc results — each is targeted, so only the ----------
    // ---------- requesting client's copy of this script ever runs them. ----------

    [ClientRpc]
    private void NotifyOpponentDrewCardClientRpc(ulong drawerClientId)
    {
        // If I am the one who drew the card, ignore this message
        if (drawerClientId == NetworkManager.Singleton.LocalClientId) return;

        // Fire the event for the local UI to handle
        OnOpponentDrewCard?.Invoke(drawerClientId);
    }

    [ClientRpc]
    private void SendTokensClientRpc(int tokens, ClientRpcParams rpcParams = default)
    {
        OnMyTokensChanged?.Invoke(tokens);
    }

    [ClientRpc]
    private void SendPullFailedClientRpc(string reason, ClientRpcParams rpcParams = default)
    {
        OnPullFailed?.Invoke(reason);
    }

    [ClientRpc]
    private void SendActionCardClientRpc(int id, string name, string category, string tier, string weaponType,
        int value, string role, int pullsUntilPity, ClientRpcParams rpcParams = default)
    {
        var card = new ActionCardData
        {
            Id = id,
            Name = name,
            Category = category,
            Tier = tier,
            WeaponType = weaponType,
            Value = value,
            Role = role
        };
        OnMyActionCardPulled?.Invoke(card, pullsUntilPity);
    }

    [ClientRpc]
    private void SendSupportCardClientRpc(int id, string name, string tier, string effect, string effectType,
        int pullsUntilPity, ClientRpcParams rpcParams = default)
    {
        var card = new SupportCardData
        {
            Id = id,
            Name = name,
            Tier = tier,
            Effect = effect,
            EffectType = effectType
        };
        OnMySupportCardPulled?.Invoke(card, pullsUntilPity);
    }

    private ClientRpcParams ToTarget(ulong clientId) => new ClientRpcParams
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
    };
}

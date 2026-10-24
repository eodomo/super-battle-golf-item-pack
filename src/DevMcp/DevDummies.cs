#if DEVTOOLS
using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ItemPack.DevMcp;

/// <summary>
/// Dummy players for testing against other people on one machine: extra player objects the host spawns on a fake
/// connection (an id no transport knows, so everything sent to it is dropped). They stand where they're put and
/// count as players for hits, targeting and the pack's own player lookups. Nobody simulates them, so the host
/// stands in for their client wherever the pack asks a player's own client to move them (see <see cref="IsDummy"/>).
/// </summary>
internal static class DevDummies
{
    private const int FirstConnectionId = 9000;
    private const ulong FirstGuid = 7_000_000_000_000_000UL;

    private sealed class Dummy
    {
        public NetworkConnectionToClient Connection;
        public PlayerInfo Player;
    }

    private static readonly List<Dummy> dummies = new();
    private static int counter;

    public static IReadOnlyList<PlayerInfo> Players
    {
        get
        {
            var list = new List<PlayerInfo>();
            foreach (Dummy d in dummies)
            {
                if (d.Player != null)
                {
                    list.Add(d.Player);
                }
            }
            return list;
        }
    }

    public static bool IsDummy(uint netId)
    {
        foreach (Dummy d in dummies)
        {
            if (d.Player != null && d.Player.netId == netId)
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsDummyConnection(int connectionId) => connectionId >= FirstConnectionId && connectionId < FirstConnectionId + 1000;

    public static PlayerInfo Spawn(Vector3 position, float yaw, string name, out string error)
    {
        error = null;
        if (!NetworkServer.active)
        {
            error = "Only the host can spawn dummies.";
            return null;
        }
        GameObject prefab = BNetworkManager.singleton.playerPrefab;
        int index = ++counter;
        var connection = new NetworkConnectionToClient(FirstConnectionId + index)
        {
            isAuthenticated = true,
        };
        if (!NetworkServer.AddConnection(connection))
        {
            error = "Couldn't add the fake connection.";
            return null;
        }
        GameObject go = UnityEngine.Object.Instantiate(prefab, position, Quaternion.Euler(0f, yaw, 0f));
        go.name = $"Dummy {index} [connId={connection.connectionId}]";
        PlayerInfo info = go.GetComponent<PlayerInfo>();
        ulong guid = FirstGuid + (ulong)index;
        info.PlayerId.ServerSetGuid(guid);
        BNetworkManager.singleton.playerGuidPerConnectionId[connection.connectionId] = guid;
        NetworkServer.SetClientReady(connection);
        NetworkServer.AddPlayerForConnection(connection, go);
        info.AsGolfer.ServerInitializeAsParticipant(null);
        Plugin.Runner.StartCoroutine(Dress(info));
        dummies.Add(new Dummy { Connection = connection, Player = info });
        Plugin.Log.LogInfo($"Spawned dummy player '{name ?? go.name}' (netId {info.netId}) at {position}.");
        return info;
    }

    /// <summary>Once the object has finished spawning on this client (the hooks need that): visible, in the local player's outfit.</summary>
    private static System.Collections.IEnumerator Dress(PlayerInfo info)
    {
        yield return new WaitForSeconds(0.5f);
        if (info == null)
        {
            yield break;
        }
        info.Movement.NetworkisVisible = true; // normally the owner's client decides this
        CopyLook(GameManager.LocalPlayerInfo, info);
        // The visibility hooks only run for changes that arrive over the network.
        info.Movement.UpdateEnabledColliders();
        info.GetComponent<PlayerCosmeticsSwitcher>().UpdateVisibilityAndActiveRenderFeatures();
    }

    /// <summary>The owner's client normally sends its outfit; a dummy borrows the local player's.</summary>
    private static void CopyLook(PlayerInfo from, PlayerInfo to)
    {
        if (from?.Cosmetics == null || to?.Cosmetics == null)
        {
            return;
        }
        foreach (var property in typeof(PlayerCosmetics).GetProperties())
        {
            if (property.Name.StartsWith("Network") && (property.Name.EndsWith("CosmeticKey") || property.Name == "NetworkskinColorIndex")
                && property.CanRead && property.CanWrite)
            {
                property.SetValue(to.Cosmetics, property.GetValue(from.Cosmetics));
            }
        }
    }

    public static int RemoveAll()
    {
        int count = 0;
        foreach (Dummy d in dummies)
        {
            if (d.Player != null)
            {
                NetworkServer.Destroy(d.Player.gameObject);
            }
            BNetworkManager.singleton.playerGuidPerConnectionId.Remove(d.Connection.connectionId);
            NetworkServer.RemoveConnection(d.Connection.connectionId);
            count++;
        }
        dummies.Clear();
        return count;
    }
}

/// <summary>
/// A killed player's own client runs their respawn (hidden, a wait, back at the tee). A dummy has none, so the host plays
/// it out instead: hidden and "respawning" for the game's delay, then visible again where it stood.
/// </summary>
[HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.TryBeginRespawn))]
internal static class DummyRespawnPatch
{
    private static void Postfix(PlayerMovement __instance, bool __result)
    {
        if (__result && NetworkServer.active && DevDummies.IsDummy(__instance.netId))
        {
            __instance.StartCoroutine(Respawn(__instance));
        }
    }

    private static System.Collections.IEnumerator Respawn(PlayerMovement movement)
    {
        movement.NetworkrespawnState = RespawnState.WaitingToRespawn;
        movement.NetworkisVisible = false;
        yield return new WaitForSeconds(GameManager.MatchSettings.RespawnPostEliminationDelay + GameManager.MatchSettings.RespawnAnimationDuration);
        movement.NetworkrespawnState = RespawnState.None;
        movement.NetworkisVisible = true;
    }
}

/// <summary>Sends to a dummy's fake connection go nowhere (no transport has that id).</summary>
[HarmonyPatch(typeof(NetworkConnectionToClient), "SendToTransport")]
internal static class DummySendPatch
{
    private static bool Prefix(NetworkConnectionToClient __instance) => !DevDummies.IsDummyConnection(__instance.connectionId);
}
#endif

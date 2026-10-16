using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ItemPack.Items;

/// <summary>Everyone's copy of "this cart is a Zord" and crying.</summary>
public struct ZordState : NetworkMessage
{
    public uint cartNetId;
    public byte action;
}

/// <summary>The driver asks for a war cry (1) or a sword swing (2).</summary>
public struct ZordCryRequest : NetworkMessage
{
    public byte action;
}

/// <summary>
/// Which golf carts are Zords, on the host and on every client. The host marks a cart when it spawns from a Zord
/// item and tells everyone; each client then dresses that cart up as Zord (<see cref="ZordView"/>). The driver's right
/// click crys, through the host.
/// </summary>
internal static class ZordCarts
{
    private const double MinToggleInterval = 4.0;
    private const double SwingInterval = 2.2;

    // Host: marked carts (netId -> unused).
    private static readonly Dictionary<uint, bool> serverState = new();
    private static readonly Dictionary<int, double> lastToggle = new();
    // Clients: views per cart, and carts told about before they exist here.
    private static readonly Dictionary<GolfCartInfo, ZordView> views = new();
    private static readonly Dictionary<uint, (byte action, float since)> pending = new();

    public static void InitSerializers()
    {
        Writer<ZordState>.write = (w, m) => { w.WriteUInt(m.cartNetId); w.WriteByte(m.action); };
        Reader<ZordState>.read = r => new ZordState { cartNetId = r.ReadUInt(), action = r.ReadByte() };
        Writer<ZordCryRequest>.write = (w, m) => w.WriteByte(m.action);
        Reader<ZordCryRequest>.read = r => new ZordCryRequest { action = r.ReadByte() };
        ZordKnock.Init();
    }

    public static void RegisterServerHandlers()
    {
        NetworkServer.RegisterHandler<ZordCryRequest>(OnServerCryRequest);
        ZordKnock.RegisterServer();
    }

    public static void RegisterClientHandlers()
    {
        NetworkClient.ReplaceHandler<ZordState>(msg => Apply(msg.cartNetId, msg.action));
        ZordKnock.RegisterClient();
    }

    public static void ResetServer()
    {
        serverState.Clear();
        lastToggle.Clear();
    }

    public static void ResetClient()
    {
        views.Clear();
        pending.Clear();
    }

    public static bool TryGetView(GolfCartInfo cart, out ZordView view)
    {
        view = null;
        return cart != null && views.TryGetValue(cart, out view) && view != null;
    }

    /// <summary>True for a Zord cart, on the host (marked) or on a client (dressed up).</summary>
    public static bool IsZord(GolfCartInfo cart) =>
        cart != null && (serverState.ContainsKey(cart.netId) || (views.TryGetValue(cart, out ZordView v) && v != null));

    // ---- host ----

    public static void ServerMark(GolfCartInfo cart)
    {
        serverState[cart.netId] = false;
        NetworkServer.SendToAll(new ZordState { cartNetId = cart.netId, action = 0 });
        Plugin.Log.LogInfo($"Zord cart {cart.netId} spawned.");
    }

    public static void SendAllTo(NetworkConnectionToClient connection)
    {
        foreach (KeyValuePair<uint, bool> entry in serverState)
        {
            connection.Send(new ZordState { cartNetId = entry.Key, action = 0 });
        }
    }

    private static void OnServerCryRequest(NetworkConnectionToClient conn, ZordCryRequest request)
    {
        if (conn.identity == null || !conn.identity.TryGetComponent(out PlayerInfo player))
        {
            return;
        }
        GolfCartSeat seat = player.ActiveGolfCartSeat;
        if (request.action == 0 || request.action > 2 || !seat.IsDriver() || !serverState.ContainsKey(seat.golfCart.netId))
        {
            return;
        }
        double interval = request.action == 2 ? SwingInterval : MinToggleInterval;
        int key = conn.connectionId * 4 + request.action;
        if (lastToggle.TryGetValue(key, out double last) && Time.timeAsDouble - last < interval)
        {
            return;
        }
        lastToggle[key] = Time.timeAsDouble;
        Plugin.Log.LogInfo($"Zord: action {request.action}.");
        NetworkServer.SendToAll(new ZordState { cartNetId = seat.golfCart.netId, action = request.action });
    }

    private static void ServerTick()
    {
        if (!NetworkServer.active || serverState.Count == 0)
        {
            return;
        }
        List<uint> gone = null;
        foreach (uint id in serverState.Keys)
        {
            if (!NetworkServer.spawned.ContainsKey(id))
            {
                (gone ??= new List<uint>()).Add(id);
            }
        }
        if (gone != null)
        {
            foreach (uint id in gone)
            {
                serverState.Remove(id);
            }
        }
    }

    // ---- clients ----

    private static void Apply(uint netId, byte action)
    {
        if (TryFindCart(netId, out GolfCartInfo cart))
        {
            if (!views.TryGetValue(cart, out ZordView view) || view == null)
            {
                view = ZordView.Create(cart);
                if (view == null)
                {
                    // Not ready yet (the game's material is still loading): try again next frame.
                    if (!pending.ContainsKey(netId))
                    {
                        pending[netId] = (action, Time.time);
                    }
                    return;
                }
                views[cart] = view;
            }
            pending.Remove(netId);
            if (action == 1)
            {
                view.StartCry();
            }
            else if (action == 2)
            {
                view.StartSwing();
            }
        }
        else
        {
            pending[netId] = (action, Time.time);
        }
    }

    private static bool TryFindCart(uint netId, out GolfCartInfo cart)
    {
        cart = null;
        return NetworkClient.spawned.TryGetValue(netId, out NetworkIdentity identity) && identity != null && identity.TryGetComponent(out cart);
    }

    public static void Tick()
    {
        ServerTick();

        if (pending.Count > 0)
        {
            List<uint> ids = new(pending.Keys);
            foreach (uint id in ids)
            {
                (byte action, float since) = pending[id];
                if (TryFindCart(id, out _))
                {
                    Apply(id, action);
                }
                else if (Time.time - since > 15f)
                {
                    pending.Remove(id);
                }
            }
        }

        // The driver's left click swings the sword, right click strikes a pose.
        PlayerInfo local = GameManager.LocalPlayerInfo;
        if (local == null || !NetworkClient.active)
        {
            return;
        }
        GolfCartSeat seat = local.ActiveGolfCartSeat;
        if (seat.IsDriver() && TryGetView(seat.golfCart, out _) && Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame && Time.time >= nextLocalSwing)
            {
                nextLocalSwing = Time.time + (float)SwingInterval;
                NetworkClient.Send(new ZordCryRequest { action = 2 });
            }
            else if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                NetworkClient.Send(new ZordCryRequest { action = 1 });
            }
        }
    }

    private static float nextLocalSwing;
}
[HarmonyPatch(typeof(GolfCartInfo), nameof(GolfCartInfo.ServerReserveDriverSeatPostNetworkSpawn))]
internal static class ZordSpawnPatch
{
    // The host's cart spawn runs for the vanilla golf cart and for Zord alike; the equipped item says which it is.
    private static void Postfix(GolfCartInfo __instance)
    {
        PlayerInfo driver = __instance.NetworkdriverSeatReserver;
        if (driver != null && ZordItem.Instance != null && driver.Inventory.GetEffectivelyEquippedItem() == ZordItem.Instance.Type)
        {
            ZordCarts.ServerMark(__instance);
        }
    }
}

[HarmonyPatch(typeof(PlayerInfo), nameof(PlayerInfo.CanEnterGolfCart))]
internal static class ZordCanEnterPatch
{
    // The game only lets you board while you're using a golf cart item; Zord counts as one.
    private static bool Prefix(PlayerInfo __instance, ref bool __result)
    {
        PlayerInventory inventory = __instance.Inventory;
        if (ZordItem.Instance == null || !inventory.IsUsingItemAtAll || inventory.GetEffectivelyEquippedItem() != ZordItem.Instance.Type)
        {
            return true;
        }
        __result = !__instance.AsGolfer.IsMatchResolved
            && !__instance.Movement.IsKnockedOutOrRecovering
            && !__instance.Movement.IsRespawningOrDrowning
            && __instance.AsHittable.FrozenState != FrozenState.Frozen
            && !__instance.isInJumboBurgerGiantForm;
        return false;
    }
}

[HarmonyPatch(typeof(GolfCartInfo))]
internal static class ZordSeatPatches
{
    private const int Seats = 2;

    // The Zord has two cockpit seats, whatever number of seats the cart has.
    [HarmonyPrefix, HarmonyPatch(nameof(GolfCartInfo.IsSeatFreeFor))]
    private static bool IsSeatFreeFor(GolfCartInfo __instance, int seat, ref bool __result)
    {
        if (seat >= Seats && ZordCarts.IsZord(__instance))
        {
            __result = false;
            return false;
        }
        return true;
    }

    [HarmonyPrefix, HarmonyPatch(nameof(GolfCartInfo.TryGetFirstAvailableSeatFor))]
    private static bool TryGetFirstAvailableSeatFor(GolfCartInfo __instance, PlayerInfo player, out int seat, ref bool __result)
    {
        if (!ZordCarts.IsZord(__instance))
        {
            seat = -1;
            return true;
        }
        for (int i = 0; i < Seats && i < __instance.passengers.Count; i++)
        {
            if (__instance.IsSeatFreeFor(player, i))
            {
                seat = i;
                __result = true;
                return false;
            }
        }
        seat = -1;
        __result = false;
        return false;
    }

    // The camera frames the whole Zord, not just the cart.
    [HarmonyPostfix, HarmonyPatch(nameof(GolfCartInfo.GetOrbitCameraSubjectLocalBounds))]
    private static void CameraBounds(GolfCartInfo __instance, ref Bounds __result)
    {
        if (ZordCarts.TryGetView(__instance, out ZordView view))
        {
            float s = view.Rig.Root.localScale.x;
            // The camera backs off by the bounds' half height, so make them tall enough to see the whole Zord.
            float center = 1.9f * s - 0.2f, half = 3.1f * s;
            __result.Encapsulate(new Vector3(0f, center - half, 0f));
            __result.Encapsulate(new Vector3(0f, center + half, 0f));
        }
    }
}

[HarmonyPatch(typeof(GolfCartInfo), nameof(GolfCartInfo.GetExitData))]
internal static class ZordExitPatch
{
    private const float Margin = 0.8f;

    // The cart's normal exit spots are a golf cart's width from the middle, inside the Zord's body box. Standing a player
    // there made the game shove them through the ground, so move the spot to just outside the box.
    private static void Postfix(GolfCartInfo __instance, ref Vector3 worldPosition)
    {
        if (!ZordCarts.TryGetView(__instance, out ZordView view) || view.HitBox == null)
        {
            return;
        }
        BoxCollider box = view.HitBox;
        Transform cart = __instance.transform;
        Vector3 local = cart.InverseTransformPoint(worldPosition);
        Vector3 center = cart.InverseTransformPoint(box.transform.TransformPoint(box.center));
        Vector3 half = box.size * 0.5f;
        float dx = local.x - center.x, dz = local.z - center.z; // exit spot relative to the box center, in cart space
        if (Mathf.Abs(dx) >= half.x + Margin || Mathf.Abs(dz) >= half.z + Margin)
        {
            return;
        }
        // Leave by whichever face is closest.
        float toSide = half.x + Margin - Mathf.Abs(dx), toEnd = half.z + Margin - Mathf.Abs(dz);
        if (toSide <= toEnd)
        {
            local.x += (dx >= 0f ? 1f : -1f) * toSide;
        }
        else
        {
            local.z += (dz >= 0f ? 1f : -1f) * toEnd;
        }
        worldPosition = cart.TransformPoint(local);
    }
}

[HarmonyPatch(typeof(PlayerMovement), "LateUpdate")]
internal static class ZordRidersPatch
{
    // Players already hidden by us, so they are shown again once they climb out.
    private static readonly Dictionary<PlayerMovement, List<Renderer>> hidden = new();

    // After the game pins a rider to the cart's seat, move them into the cockpit and hide them: they are inside.
    private static void Postfix(PlayerMovement __instance)
    {
        PlayerInfo player = __instance.PlayerInfo;
        GolfCartSeat seat = player.ActiveGolfCartSeat;
        if (!seat.IsValid() || !ZordCarts.TryGetView(seat.golfCart, out ZordView view) || player.HipBone == null)
        {
            Reveal(__instance);
            return;
        }
        Vector3 shift = view.SeatWorld(seat.seat) - player.HipBone.position;
        __instance.transform.position += shift;
        __instance.rigidbody.position = __instance.transform.position;
        view.NoteSeatShift(seat.seat, shift);

        if (!hidden.TryGetValue(__instance, out List<Renderer> list))
        {
            list = new List<Renderer>();
            hidden[__instance] = list;
        }
        foreach (Renderer r in __instance.GetComponentsInChildren<Renderer>())
        {
            if (r != null && r.enabled)
            {
                r.enabled = false;
                list.Add(r);
            }
        }
    }

    private static void Reveal(PlayerMovement movement)
    {
        if (hidden.Count == 0 || !hidden.TryGetValue(movement, out List<Renderer> list))
        {
            return;
        }
        foreach (Renderer r in list)
        {
            if (r != null)
            {
                r.enabled = true;
            }
        }
        hidden.Remove(movement);
    }
}

[HarmonyPatch(typeof(PlayerInfo))]
internal static class ZordNoHornPatches
{
    // The Megazord has no horn: left click is the sword, so the cart's honk is switched off while riding one.
    private static bool InZord(PlayerInfo player)
    {
        GolfCartSeat seat = player.ActiveGolfCartSeat;
        return seat.IsValid() && ZordCarts.IsZord(seat.golfCart);
    }

    [HarmonyPrefix, HarmonyPatch(nameof(PlayerInfo.HonkGolfCart))]
    private static bool Honk(PlayerInfo __instance) => !InZord(__instance);

    [HarmonyPrefix, HarmonyPatch(nameof(PlayerInfo.StartSpecialGolfCartHonk))]
    private static bool StartSpecial(PlayerInfo __instance) => !InZord(__instance);

    [HarmonyPrefix, HarmonyPatch(nameof(PlayerInfo.EndSpecialGolfCartHonk))]
    private static bool EndSpecial(PlayerInfo __instance) => !InZord(__instance);
}

/// <summary>Driver to host: knock this cart away (direction is a unit vector, the speed is the host's own setting).</summary>
public struct ZordKnockRequest : NetworkMessage
{
    public uint cartNetId;
    public Vector3 direction;
}

/// <summary>Host to whoever simulates the cart: apply this velocity kick.</summary>
public struct ZordKnockApply : NetworkMessage
{
    public uint cartNetId;
    public Vector3 velocity;
}

internal static class ZordKnock
{
    public static void Init()
    {
        Writer<ZordKnockRequest>.write = (w, m) => { w.WriteUInt(m.cartNetId); w.WriteVector3(m.direction); };
        Reader<ZordKnockRequest>.read = r => new ZordKnockRequest { cartNetId = r.ReadUInt(), direction = r.ReadVector3() };
        Writer<ZordKnockApply>.write = (w, m) => { w.WriteUInt(m.cartNetId); w.WriteVector3(m.velocity); };
        Reader<ZordKnockApply>.read = r => new ZordKnockApply { cartNetId = r.ReadUInt(), velocity = r.ReadVector3() };
    }

    public static void RegisterServer() => NetworkServer.RegisterHandler<ZordKnockRequest>(OnRequest);

    public static void RegisterClient() => NetworkClient.ReplaceHandler<ZordKnockApply>(msg => Apply(msg.cartNetId, msg.velocity));

    /// <summary>The swinger's side: a normal hit barely moves a heavy cart, so give it a real shove.</summary>
    public static void Send(GolfCartInfo cart, Vector3 direction) =>
        NetworkClient.Send(new ZordKnockRequest { cartNetId = cart.netId, direction = direction.normalized });

    private static void OnRequest(NetworkConnectionToClient conn, ZordKnockRequest request)
    {
        if (conn.identity == null || !conn.identity.TryGetComponent(out PlayerInfo player) || !player.ActiveGolfCartSeat.IsDriver()
            || !ZordCarts.IsZord(player.ActiveGolfCartSeat.golfCart)
            || !NetworkServer.spawned.TryGetValue(request.cartNetId, out NetworkIdentity target) || target == null)
        {
            return;
        }
        Vector3 dir = request.direction.sqrMagnitude > 0.01f ? request.direction.normalized : Vector3.up;
        Vector3 velocity = new Vector3(dir.x, 0f, dir.z).normalized * ZordItem.CartKnockback.Value + Vector3.up * ZordItem.CartKnockback.Value * 0.3f;
        // Whoever simulates the cart applies it: its driver's machine if it has one, otherwise the host.
        if (target.connectionToClient != null && target.connectionToClient != NetworkServer.localConnection)
        {
            target.connectionToClient.Send(new ZordKnockApply { cartNetId = request.cartNetId, velocity = velocity });
        }
        else
        {
            Apply(request.cartNetId, velocity, serverSide: true);
        }
    }

    private static void Apply(uint netId, Vector3 velocity, bool serverSide = false)
    {
        Dictionary<uint, NetworkIdentity> spawned = serverSide ? NetworkServer.spawned : NetworkClient.spawned;
        if (spawned.TryGetValue(netId, out NetworkIdentity identity) && identity != null && identity.TryGetComponent(out Rigidbody body) && !body.isKinematic)
        {
            body.linearVelocity += velocity;
            body.angularVelocity += new Vector3(velocity.z, 0f, -velocity.x) * 0.05f;
        }
    }
}

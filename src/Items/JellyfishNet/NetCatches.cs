using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ItemPack.Items;

/// <summary>
/// Catching and carrying on each client. The carrier's net holds the caught player out in front of it: the caught player's
/// own client moves itself there every physics step (players own their movement) with its controls off, and the host
/// moves dummy players, which nobody else simulates. Left click throws them, or the carry times out.
/// </summary>
internal static class NetCatches
{
    private sealed class Carry
    {
        public uint Target;
        public float Until;
    }

    private static readonly Dictionary<uint, Carry> carries = new();
    private static readonly List<uint> finished = new();
    private static readonly Collider[] overlap = new Collider[32];

    private static bool localCarrying;
    private static int carrySlot;
    private static float carryStart;
    private static float carryTotal;
    private static bool releaseSent;
    private static bool trapped;
    private static uint trappedBy;

    /// <summary>The local player has someone in their net.</summary>
    public static bool LocalCarrying => localCarrying;

    public static bool IsCarrier(uint netId) => carries.ContainsKey(netId);

    /// <summary>The ring of the net holding <paramref name="targetNetId"/> (where its hanging net starts).</summary>
    public static bool TryGetRing(uint targetNetId, out Vector3 ring, out JellyfishNetHeld net)
    {
        foreach (KeyValuePair<uint, Carry> pair in carries)
        {
            if (pair.Value.Target == targetNetId && NetworkClient.spawned.TryGetValue(pair.Key, out NetworkIdentity id) && id != null
                && id.TryGetComponent(out PlayerInfo carrier))
            {
                net = HeldNet(carrier);
                ring = RingOf(carrier);
                return net != null;
            }
        }
        ring = default;
        net = null;
        return false;
    }

    /// <summary>The net <paramref name="carrier"/> has in their hand, or null when they've put it away.</summary>
    private static JellyfishNetHeld HeldNet(PlayerInfo carrier) =>
        carrier.RightHandEquipmentSwitcher != null ? carrier.RightHandEquipmentSwitcher.GetComponentInChildren<JellyfishNetHeld>() : null;

    private static Vector3 RingOf(PlayerInfo carrier)
    {
        JellyfishNetHeld net = HeldNet(carrier);
        Vector3 forward = Vector3.ProjectOnPlane(carrier.transform.forward, Vector3.up).normalized;
        return net != null ? net.RingCenter : carrier.transform.position + forward * 0.8f + Vector3.up * 2f;
    }

    /// <summary>Carrier and caught player pass through each other while carried (or the carried one would be shoved away).</summary>
    private static void SetCarrierCollisions(uint carrierId, uint targetId, bool ignore)
    {
        if (NetworkClient.spawned.TryGetValue(carrierId, out NetworkIdentity a) && a != null
            && NetworkClient.spawned.TryGetValue(targetId, out NetworkIdentity b) && b != null)
        {
            foreach (Collider ca in a.GetComponentsInChildren<Collider>(true))
            {
                foreach (Collider cb in b.GetComponentsInChildren<Collider>(true))
                {
                    Physics.IgnoreCollision(ca, cb, ignore);
                }
            }
        }
    }

    public static void Reset()
    {
        carries.Clear();
        localCarrying = false;
        Untrap();
    }

    // ---- swinging and catching ----

    public static void PlaySwing(PlayerInfo player)
    {
        JellyfishNetHeld net = HeldNet(player);
        if (net != null)
        {
            net.PlaySwing();
        }
        NetSounds.PlaySwing(player.transform.position);
    }

    /// <summary>The closest player the swing reaches: right in front of the swinger, free to be caught.</summary>
    public static PlayerInfo FindTarget(PlayerInfo swinger)
    {
        Vector3 forward = Vector3.ProjectOnPlane(swinger.transform.forward, Vector3.up).normalized;
        Vector3 chest = swinger.transform.position + Vector3.up * 1.1f;
        float reach = JellyfishNetItem.Reach.Value;
        Vector3 center = chest + forward * (reach * 0.6f);
        int count = Physics.OverlapSphereNonAlloc(center, reach * 0.5f + 0.2f, overlap, GameManager.LayerSettings.PlayersMask, QueryTriggerInteraction.Ignore);
        PlayerInfo best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            PlayerInfo other = overlap[i].GetComponentInParent<PlayerInfo>();
            if (other == null || other == swinger || !other.Movement.IsVisible || other.Movement.IsKnockedOutOrRecovering
                || other.ActiveGolfCartSeat.IsValid() || IsCarried(other.netId))
            {
                continue;
            }
            Vector3 flat = Vector3.ProjectOnPlane(other.transform.position - swinger.transform.position, Vector3.up);
            if (flat.magnitude > reach + 0.3f || Vector3.Angle(flat, forward) > 70f)
            {
                continue;
            }
            float distance = flat.magnitude;
            if (distance < bestDistance)
            {
                best = other;
                bestDistance = distance;
            }
        }
        return best;
    }

    private static bool IsCarried(uint netId)
    {
        foreach (Carry carry in carries.Values)
        {
            if (carry.Target == netId)
            {
                return true;
            }
        }
        return false;
    }

    // ---- network events ----

    public static void OnCaught(NetCaught msg)
    {
        carries[msg.carrierNetId] = new Carry { Target = msg.targetNetId, Until = Time.time + msg.seconds + 2f };
        SetCarrierCollisions(msg.carrierNetId, msg.targetNetId, true);
        if (NetworkClient.spawned.TryGetValue(msg.targetNetId, out NetworkIdentity caughtId) && caughtId != null && caughtId.GetComponent<NetCurl>() == null)
        {
            caughtId.gameObject.AddComponent<NetCurl>();
        }
        PlayerInfo local = GameManager.LocalPlayerInfo;
        if (local == null)
        {
            return;
        }
        if (local.netId == msg.carrierNetId)
        {
            localCarrying = true;
            releaseSent = false;
            carrySlot = local.Inventory.EquippedItemIndex;
            carryStart = Time.time;
            carryTotal = msg.seconds;
        }
        if (local.netId == msg.targetNetId)
        {
            Trap(msg.carrierNetId);
        }
    }

    public static void OnReleased(NetReleased msg)
    {
        carries.Remove(msg.carrierNetId);
        SetCarrierCollisions(msg.carrierNetId, msg.targetNetId, false);
        if (NetworkClient.spawned.TryGetValue(msg.targetNetId, out NetworkIdentity freedId) && freedId != null)
        {
            NetSounds.PlayRelease(freedId.transform.position);
            if (freedId.TryGetComponent(out NetCurl curl))
            {
                Object.Destroy(curl);
            }
        }
        PlayerInfo local = GameManager.LocalPlayerInfo;
        if (local != null && local.netId == msg.carrierNetId && localCarrying)
        {
            localCarrying = false;
            // The net has done its job.
            ItemUse.EndAway(local.Inventory, carrySlot, JellyfishNetItem.Instance.Type);
        }
        if (NetworkServer.active && NetworkServer.spawned.TryGetValue(msg.targetNetId, out NetworkIdentity dummy) && IsHostDriven(msg.targetNetId))
        {
            Plugin.Runner.StartCoroutine(FlyDummy(dummy.GetComponent<PlayerInfo>(), msg.velocity));
        }
        if (local != null && local.netId == msg.targetNetId && trapped)
        {
            Untrap();
            Throw(local, msg.velocity);
        }
    }

    /// <summary>A dummy has no client simulating it, so a throw is flown by hand: a simple arc until it lands.</summary>
    private static System.Collections.IEnumerator FlyDummy(PlayerInfo dummy, Vector3 velocity)
    {
        Rigidbody body = dummy.Rigidbody;
        for (float t = 0f; t < 4f && dummy != null; t += Time.fixedDeltaTime)
        {
            velocity += Vector3.down * 20f * Time.fixedDeltaTime;
            Vector3 next = body.position + velocity * Time.fixedDeltaTime;
            if (Physics.Raycast(body.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 0.5f + Mathf.Max(0f, -velocity.y * Time.fixedDeltaTime) + 0.05f, WorldMask.Solid, QueryTriggerInteraction.Ignore) && velocity.y <= 0f)
            {
                body.position = hit.point;
                yield break;
            }
            body.position = next;
            yield return new WaitForFixedUpdate();
        }
    }

    private static void Throw(PlayerInfo player, Vector3 velocity)
    {
        if (player == null || player.Rigidbody == null)
        {
            return;
        }
        player.Rigidbody.isKinematic = false;
        player.Rigidbody.linearVelocity = velocity;
        if (player.Movement.IsGrounded)
        {
            player.Movement.Unground();
        }
    }

    /// <summary>Left click while carrying: throw them out in front of you.</summary>
    public static void ThrowCaught(PlayerInventory inventory)
    {
        Camera cam = GameManager.Camera;
        Vector3 forward = cam != null ? cam.transform.forward : inventory.PlayerInfo.transform.forward;
        Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        SendRelease(flat * JellyfishNetItem.ThrowSpeed.Value + Vector3.up * 5f);
    }

    private static void SendRelease(Vector3 velocity)
    {
        if (!releaseSent) // once, while the host's answer is on its way
        {
            releaseSent = true;
            NetCatchNet.SendRelease(velocity);
        }
    }

    // ---- being carried ----

    private static void Trap(uint carrier)
    {
        PlayerInfo local = GameManager.LocalPlayerInfo;
        if (local == null)
        {
            return;
        }
        trapped = true;
        trappedBy = carrier;
        local.Inventory.CancelItemUse();
    }

    private static void Untrap()
    {
        if (!trapped)
        {
            return;
        }
        trapped = false;
        trappedBy = 0;
        SetControls(true);
    }

    private static void SetControls(bool enabled)
    {
        if (InputManager.CurrentMode != InputMode.Regular || InputManager.Controls == null)
        {
            return;
        }
        if (enabled)
        {
            if (BoogieDance.IsDancing || MeetingWorld.Frozen)
            {
                return;
            }
            InputManager.Controls.Gameplay.Enable();
            InputManager.Controls.Hotkeys.Enable();
        }
        else
        {
            InputManager.Controls.Gameplay.Disable();
            InputManager.Controls.Hotkeys.Disable();
        }
    }

    private static bool IsHostDriven(uint netId)
    {
#if DEVTOOLS
        return DevMcp.DevDummies.IsDummy(netId);
#else
        return false;
#endif
    }

    /// <summary>Where a caught player's feet go: their body sits in the net's mouth.</summary>
    private static Vector3 HoldFeet(PlayerInfo carrier, PlayerInfo target)
    {
        Vector3 hold = RingOf(carrier);
        // Never closer to the ground than their own feet standing in front of the carrier.
        // The curled body hangs in the net's bulb: its whole ball just below the ring (so nothing sticks out of the mouth)
        // and a little further out, so the bag hangs clear of the carrier, wherever the feet end up.
        bool curled = target.TryGetComponent(out NetCurl curl);
        Vector3 offset = curled ? curl.BodyCenter - target.transform.position : Vector3.up * 0.5f;
        float drop = curled ? Mathf.Max(0.65f, curl.BallRadius + 0.12f) : 0.65f;
        Vector3 ahead = Vector3.ProjectOnPlane(carrier.transform.forward, Vector3.up).normalized;
        Vector3 feet = hold + ahead * 0.28f - Vector3.up * drop - offset;
        float floor = carrier.transform.position.y;
        feet.y = Mathf.Max(feet.y, floor + 0.2f);
        return feet;
    }

    private static void Chase(PlayerInfo player, Vector3 goal, float dt)
    {
        Rigidbody body = player.Rigidbody;
        if (body == null)
        {
            return;
        }
        Vector3 toGoal = goal - body.position;
        if (body.isKinematic)
        {
            body.MovePosition(goal);
            return;
        }
        body.linearVelocity = Vector3.ClampMagnitude(toGoal / Mathf.Max(dt, 0.005f) * 0.5f, 30f);
    }

    // ---- per frame ----

    public static void Tick()
    {
        PlayerInfo local = GameManager.LocalPlayerInfo;
        if (localCarrying)
        {
            float left = carryTotal - (Time.time - carryStart);
            bool holding = local != null && ItemCatalog.TryGet(local.Inventory.GetEffectivelyEquippedItem(), out CustomItem item) && item is JellyfishNetItem;
            if (local == null || left <= 0f || !holding || local.Movement.IsKnockedOutOrRecovering)
            {
                SendRelease(local != null ? Vector3.ProjectOnPlane(local.transform.forward, Vector3.up) * 2f : Vector3.zero);
            }
            else if (local.Movement.IsVisible)
            {
                TimerWheel.Show(left / Mathf.Max(0.01f, carryTotal));
            }
        }
        if (trapped)
        {
            if (local == null || !NetworkClient.spawned.ContainsKey(trappedBy))
            {
                Untrap();
                return;
            }
            SetControls(false); // the game turns them back on when its input mode changes
        }
    }

    public static void FixedTick()
    {
        NetCatchNet.ServerTick();
        if (carries.Count == 0)
        {
            return;
        }
        PlayerInfo local = GameManager.LocalPlayerInfo;
        finished.Clear();
        foreach (KeyValuePair<uint, Carry> pair in carries)
        {
            bool targetIsLocal = local != null && trapped && local.netId == pair.Value.Target;
            bool driven = NetworkServer.active && IsHostDriven(pair.Value.Target);
            if (!targetIsLocal && !driven)
            {
                continue;
            }
            if (!NetworkClient.spawned.TryGetValue(pair.Key, out NetworkIdentity carrierId) || carrierId == null
                || !carrierId.TryGetComponent(out PlayerInfo carrier)
                || !NetworkClient.spawned.TryGetValue(pair.Value.Target, out NetworkIdentity targetId) || targetId == null
                || !targetId.TryGetComponent(out PlayerInfo target))
            {
                finished.Add(pair.Key);
                continue;
            }
            Chase(target, HoldFeet(carrier, target), Time.fixedDeltaTime);
            if (target.Movement.IsGrounded)
            {
                target.Movement.Unground();
            }
        }
        foreach (uint carrier in finished)
        {
            carries.Remove(carrier);
        }
    }
}

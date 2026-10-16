using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Thrown shields: each flies flat and spinning along the thrower's aim, hits at most one player or cart, and comes
/// back to the thrower's hand (after that hit, a wall or its full range). Every client flies everyone's shields the
/// same way; only the thrower's client checks for the hit (which takes the shield's charge) and tells the others when
/// it turns back. A throw that hits nothing is free.
/// </summary>
internal static class CapShieldThrows
{
    private const float Speed = 28f;
    private const float ReturnSpeed = 32f;
    private const float SpinDegreesPerSecond = 900f;
    private const float CatchDistance = 0.9f;
    private const float HitRadius = 0.6f;
    private const float LostAfterSeconds = 15f; // never left out in the air whatever happens
    private const float MaxReturnSeconds = 6f;
    private const byte Thrown = 0, Returning = 1, Gone = 2;

    private class Flight
    {
        public uint netId;
        public PlayerInfo thrower;
        public GameObject root;
        public Transform spinner;
        public Vector3 position;
        public Vector3 direction;
        public float traveled;
        public bool returning;
        public float returnStart = -1f;
        public float startTime;
        public float spin;
    }

    private static readonly Dictionary<uint, Flight> flights = new();
    private static readonly List<uint> ended = new();
    private static readonly Collider[] overlap = new Collider[32];

    // The local player's own flight.
    private static PlayerInventory localInventory;
    private static int localSlot;
    private static bool localHit;

    public static bool IsThrown(uint netId) => flights.ContainsKey(netId);

    public static bool LocalActive => GameManager.LocalPlayerInfo != null && flights.ContainsKey(GameManager.LocalPlayerInfo.netId);

    public static void LocalThrow(PlayerInventory inventory, int slot, Vector3 origin, Vector3 direction)
    {
        localInventory = inventory;
        localSlot = slot;
        localHit = false;
        Start(inventory.PlayerInfo, origin, direction);
        CapShieldNet.SendThrowState(Thrown, origin, direction);
    }

    public static void OnState(CapThrowState msg)
    {
        if (GameManager.LocalPlayerInfo != null && GameManager.LocalPlayerInfo.netId == msg.throwerNetId)
        {
            return; // our own shield flies here already
        }
        switch (msg.state)
        {
            case Thrown:
                if (NetworkClient.spawned.TryGetValue(msg.throwerNetId, out NetworkIdentity identity) && identity != null
                    && identity.TryGetComponent(out PlayerInfo thrower))
                {
                    Start(thrower, msg.origin, msg.direction);
                }
                break;
            case Returning:
                if (flights.TryGetValue(msg.throwerNetId, out Flight flight))
                {
                    flight.position = msg.origin;
                    flight.returning = true;
                }
                break;
            default:
                End(msg.throwerNetId);
                break;
        }
    }

    private static void Start(PlayerInfo thrower, Vector3 origin, Vector3 direction)
    {
        End(thrower.netId);
        var root = new GameObject("ThrownCapShield");
        var spinner = new GameObject("Spin").transform;
        spinner.SetParent(root.transform, false);
        // Fly flat, face up (the model's face looks along +Z), spinning like a frisbee.
        var flat = new GameObject("Flat").transform;
        flat.SetParent(spinner, false);
        flat.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        ModelAsset model = CapShieldItem.Instance.Model;
        if (model?.Mesh != null)
        {
            model.InitMaterials(ModelAsset.SharedTemplate);
            model.CreateModel(flat, CapShieldItem.Instance.HeldScale.Value);
        }
        root.transform.position = origin;
        flights[thrower.netId] = new Flight
        {
            netId = thrower.netId, thrower = thrower, root = root, spinner = spinner,
            position = origin, direction = direction.normalized, startTime = Time.time,
        };
        CapShieldSounds.PlayThrow(Volume(origin));
    }

    private static void End(uint netId)
    {
        if (flights.TryGetValue(netId, out Flight flight))
        {
            if (flight.root != null)
            {
                Object.Destroy(flight.root);
            }
            flights.Remove(netId);
        }
    }

    public static void ClearAll()
    {
        foreach (Flight flight in flights.Values)
        {
            if (flight.root != null)
            {
                Object.Destroy(flight.root);
            }
        }
        flights.Clear();
        localInventory = null;
    }

    public static void Tick()
    {
        if (flights.Count == 0)
        {
            return;
        }
        float dt = Time.deltaTime;
        ended.Clear();
        foreach (Flight flight in flights.Values)
        {
            if (flight.thrower == null || flight.root == null)
            {
                ended.Add(flight.netId);
                continue;
            }
            bool local = flight.thrower == GameManager.LocalPlayerInfo;
            if (Time.time - flight.startTime > LostAfterSeconds)
            {
                ended.Add(flight.netId);
                if (local)
                {
                    FinishLocal(flight, lost: !flight.returning);
                }
                continue;
            }
            if (flight.returning && flight.returnStart < 0f)
            {
                flight.returnStart = Time.time;
            }
            if (!flight.returning)
            {
                float step = Speed * dt;
                if (local && Physics.Raycast(flight.position, flight.direction, out RaycastHit wall, step + 0.4f, WorldMask.Solid, QueryTriggerInteraction.Ignore))
                {
                    // Off a wall: head back from here.
                    flight.position = wall.point - flight.direction * 0.4f;
                    flight.returning = true;
                    CapShieldSounds.PlayClang(Volume(flight.position) * 0.6f);
                    CapShieldNet.SendThrowState(Returning, flight.position, flight.direction);
                }
                else
                {
                    flight.position += flight.direction * step;
                    flight.traveled += step;
                    // It only comes back off something it hits. Flown its full range without hitting anything: it's lost.
                    if (local && flight.traveled >= CapShieldItem.ThrowRange.Value)
                    {
                        ended.Add(flight.netId);
                        FinishLocal(flight, lost: true);
                        continue;
                    }
                }
            }
            else
            {
                Vector3 toHand = SlingerWebs.HandOf(flight.thrower) - flight.position;
                float distance = toHand.magnitude;
                if (distance < CatchDistance || (flight.returnStart >= 0f && Time.time - flight.returnStart > MaxReturnSeconds))
                {
                    ended.Add(flight.netId);
                    CapShieldSounds.PlayCatch(Volume(flight.position));
                    if (local)
                    {
                        FinishLocal(flight, lost: false);
                    }
                    continue;
                }
                flight.position += toHand / distance * Mathf.Min(distance, ReturnSpeed * dt);
            }
            flight.spin += SpinDegreesPerSecond * dt;
            flight.root.transform.position = flight.position;
            flight.spinner.rotation = Quaternion.AngleAxis(flight.spin, Vector3.up) * Quaternion.AngleAxis(8f, Vector3.right);

            if (local && !localHit && localInventory != null && CheckHit(flight))
            {
                flight.returning = true;
                CapShieldNet.SendThrowState(Returning, flight.position, flight.direction);
            }
        }
        foreach (uint netId in ended)
        {
            End(netId);
        }
    }

    private static bool HoldsShield(PlayerInventory inventory, int slot) =>
        inventory != null && slot >= 0 && inventory.GetEffectiveSlot(slot).itemType == CapShieldItem.Instance.Type
        && inventory.GetEffectiveSlot(slot).remainingUses > 0;

    /// <summary>The thrower's client, when the flight ends: back in hand, or <paramref name="lost"/> (flew off without hitting anything, which takes its charge).</summary>
    private static void FinishLocal(Flight flight, bool lost)
    {
        CapShieldNet.SendThrowState(Gone, flight.position, flight.direction);
        PlayerInventory inventory = localInventory;
        localInventory = null;
        if (inventory == null)
        {
            return;
        }
        if (lost && !localHit && HoldsShield(inventory, localSlot))
        {
            ItemUse.ConsumeUseAt(inventory, localSlot);
        }
        // The use routine takes it out of the hand when it ends; if that was cancelled (knocked out mid-throw...),
        // tidy up here instead.
        if (!inventory.IsUsingItemAtAll && inventory.GetEffectiveSlot(localSlot).itemType == CapShieldItem.Instance.Type)
        {
            inventory.RemoveIfOutOfUses(localSlot, dueToFinishedItemUse: true);
        }
    }

    /// <summary>The thrower's client: the first player or cart the shield touches is hit (that takes its charge).</summary>
    private static bool CheckHit(Flight flight)
    {
        LayerSettings layers = GameManager.LayerSettings;
        int count = Physics.OverlapSphereNonAlloc(flight.position, HitRadius, overlap, layers.PlayersMask | layers.GolfCartsMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Hittable hittable = overlap[i].GetComponentInParent<Hittable>();
            if (hittable == null || hittable.AsEntity == flight.thrower.AsEntity
                || (!hittable.AsEntity.IsPlayer && !hittable.AsEntity.IsGolfCart)
                || (hittable.AsEntity.IsPlayer && hittable.AsEntity.PlayerInfo.ActiveGolfCartSeat.IsValid()))
            {
                continue; // whoever's in a cart is hit through the cart
            }
            if (!hittable.TryGetClosestPointOnAllActiveColliders(flight.position, out Vector3 point, out _))
            {
                point = overlap[i].ClosestPoint(flight.position);
            }
            Vector3 travel = flight.returning ? SlingerWebs.HandOf(flight.thrower) - flight.position : flight.direction;
            Vector3 direction = Vector3.ProjectOnPlane(travel, Vector3.up);
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            Hit(hittable, point, direction, flight.position - direction);
            return true;
        }
        return false;
    }

    private static void Hit(Hittable hittable, Vector3 point, Vector3 direction, Vector3 source)
    {
        PlayerInventory inventory = localInventory;
        localHit = true;
        if (!HoldsShield(inventory, localSlot))
        {
            return;
        }
        inventory.PlayerInfo.LocalPlayerCancelSmokeBombInvisibility();
        ulong useHash = inventory.GetEffectiveSlot(localSlot).GetUsageValidationHash();
        inventory.DecrementUseFromSlotAt(localSlot);
        hittable.HitWithItem(ItemType.DuelingPistol, inventory.IncrementAndGetCurrentItemUseId(ItemType.DuelingPistol),
            hittable.transform.InverseTransformPoint(point), direction, hittable.transform.InverseTransformPoint(source),
            hittable.ItemSettings.DuelingPistolMinKnockbackDistance, inventory, isReflected: false, isInSpecialState: false,
            canHitWithNoUser: false, hitTimestamp: NetworkTime.time, itemUseHash: useHash);
        inventory.CmdValidateAndConsumeItemUse(useHash);
        CapShieldSounds.PlayClang(Volume(point));
    }

    /// <summary>Every client: a held shield stopped something.</summary>
    public static void PlayBlock(Vector3 position)
    {
        VfxManager.PlayPooledVfxLocalOnly(VfxType.DuelingPistolImpact, position, Quaternion.identity);
        CapShieldSounds.PlayClang(Volume(position));
    }

    private static float Volume(Vector3 position) => ItemAudio.Attenuate(position, CapShieldItem.SoundVolume.Value, 12f, 90f);
}

/// <summary>Shield sounds (or the player's own cap_throw / cap_clang / cap_catch files): a whoosh, a ringing metal clang and a soft clank.</summary>
internal static class CapShieldSounds
{
    private static FMOD.Sound throwSound, clang, catchSound;

    public static void PlayThrow(float volume)
    {
        if (!throwSound.hasHandle())
        {
            throwSound = ItemAudio.LoadOrSynthesize(CapShieldItem.Instance, "cap_throw", () => new Synth(0.7f, 151)
                .Noise(t => Mathf.Sin(Mathf.Clamp01(t / 0.6f) * Mathf.PI), Synth.Ramp(0.08f, 0.35f, 0.3f), 0.8f, band: true)
                .Tone(Synth.Ramp(300f, 520f, 0.5f), t => Mathf.Sin(Mathf.Clamp01(t / 0.6f) * Mathf.PI) * (0.5f + 0.5f * Mathf.Sin(t * 60f)), 0.15f)
                .Normalized(0.7f));
        }
        ItemAudio.Play(throwSound, ItemAudio.SfxBus, volume);
    }

    public static void PlayClang(float volume)
    {
        if (!clang.hasHandle())
        {
            // Inharmonic partials ring like a struck metal disc.
            clang = ItemAudio.LoadOrSynthesize(CapShieldItem.Instance, "cap_clang", () => new Synth(1.4f, 152)
                .Click(0f, 2400f, 0.01f, 1f, 0.9f)
                .Tone(_ => 620f, Synth.Decay(0.45f), 0.7f)
                .Tone(_ => 1013f, Synth.Decay(0.35f), 0.5f)
                .Tone(_ => 1489f, Synth.Decay(0.28f), 0.4f)
                .Tone(_ => 2177f, Synth.Decay(0.2f), 0.3f)
                .Tone(_ => 3311f, Synth.Decay(0.12f), 0.2f)
                .Normalized(0.85f));
        }
        ItemAudio.Play(clang, ItemAudio.SfxBus, volume);
    }

    public static void PlayCatch(float volume)
    {
        if (!catchSound.hasHandle())
        {
            catchSound = ItemAudio.LoadOrSynthesize(CapShieldItem.Instance, "cap_catch", () => new Synth(0.5f, 153)
                .Click(0f, 900f, 0.02f, 1f, 0.6f)
                .Tone(_ => 480f, Synth.Decay(0.12f), 0.4f)
                .Tone(_ => 790f, Synth.Decay(0.08f), 0.25f)
                .Normalized(0.6f));
        }
        ItemAudio.Play(catchSound, ItemAudio.SfxBus, volume);
    }
}

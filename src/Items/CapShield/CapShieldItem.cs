using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Captain America's shield, worn on the right hand facing out from the chest. While right click is held it blocks any hit
/// from the front (shots, swings, explosions, mines, rockets, beams, freezes, even a railgun's vaporizing). Left click while
/// aiming throws it along your aim: it knocks out the first player it hits (or shoves a cart and knocks out whoever's in it) and comes back to your hand like a
/// boomerang off whatever it hits (a player, a cart, a wall or the ground). It has one charge: a block or a throw that hits
/// someone or a cart uses it up; bouncing off the world is free, but a throw that hits nothing at all is gone for good.
/// </summary>
internal class CapShieldItem : CustomItem
{
    public static CapShieldItem Instance { get; private set; }

    public override int Id => 115;
    public override string Key => "CapShield";
    public override string DisplayName => "Captain America's Shield";
    // Defense with a short-range throw: most worth having where the fighting is, near the lead.
    public override PoolWeights BoxWeights => new(close: 4, medium: 2, far: 1, veryFar: 0, aheadOfBall: 0, drivingRange: 20);
    // Thrown one-handed like the freeze bomb.
    public override ItemType TemplateItem => ItemType.FreezeBomb;
    public override EquipmentType TemplateEquipment => EquipmentType.FreezeBomb;
    public override string AssetName => "cap_shield";
    protected override string ConfigSection => "Captain America Shield";
    public override bool AnchorToHand => false; // CapShieldHeld straps it on itself
    // It flies straight, so the pistol's crosshair fits.
    public override ReticleType AimReticle => ReticleType.DuelingPistol;

    protected override int DefaultUses => 1;
    protected override float DefaultHeldScale => 1.04f;

    internal static ConfigEntry<float> ThrowRange;
    internal static ConfigEntry<float> SoundVolume;

    public CapShieldItem() => Instance = this;

    protected override void BindConfig()
    {
        ThrowRange = Bind("ThrowRange", 150f, "How far a thrown shield can fly (meters). It only comes back off something it hits; if it hits nothing in this distance, it's lost.");
        SoundVolume = Bind("SoundVolume", 1f, "Volume of the throw, clang and catch (0-1). Local setting.");
    }

    public override void ConfigureItemData(ItemData data)
    {
        data.NonAimUse = ItemNonAimingUse.None; // thrown only while aiming (right click held, then left click)
        data.IsExplosive = false;
        data.CanHitProjectiles = false;
        data.CanUsageAffectBalls = false;
    }

    public override GameObject CreateHeldModel(Transform parent, float scale)
    {
        GameObject model = base.CreateHeldModel(parent, scale);
        model.AddComponent<CapShieldHeld>(); // strapped to the hand facing out, and hidden while it's out flying
        return model;
    }

    // A dropped shield lies flat on the ground like any other dropped item (no strapping to a hand).
    public override GameObject CreateDroppedModel(Transform parent) => base.CreateHeldModel(parent, HeldScale.Value);

    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming)
    {
        int slot = inventory.EquippedItemIndex;
        if (CapShieldThrows.LocalActive || inventory.GetEffectiveSlot(slot).remainingUses <= 0)
        {
            yield break;
        }
        PlayerInfo player = inventory.PlayerInfo;
        ItemUse.BreakStealth(inventory);
        player.Movement.AlignWithCameraImmediately();
        inventory.SetCurrentItemUse(ItemUseType.Regular); // the freeze bomb's throw animation, played as it flies

        Camera cam = GameManager.Camera;
        Vector3 origin = SlingerWebs.HandOf(player);
        Vector3 direction = cam != null ? cam.transform.forward : player.transform.forward;
        // Head for what the crosshair is on, so it goes where you aim even though it leaves from the hand.
        if (cam != null && Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit aim, ThrowRange.Value + 10f, ~0, QueryTriggerInteraction.Ignore)
            && !aim.collider.transform.IsChildOf(player.transform) && (aim.point - origin).sqrMagnitude > 4f)
        {
            direction = (aim.point - origin).normalized;
        }
        CapShieldThrows.LocalThrow(inventory, slot, origin, direction);
        // Hands stay empty until it's back (a hit takes the charge; see CapShieldThrows).
        while (CapShieldThrows.LocalActive)
        {
            yield return null;
        }
        // Like any finished item use: out of the throw pose, and gone from the hotbar if that was its last charge.
        inventory.SetCurrentItemUse(ItemUseType.None);
        if (inventory.GetEffectiveSlot(slot).itemType == Type)
        {
            inventory.RemoveIfOutOfUses(slot, dueToFinishedItemUse: true);
        }
    }

    /// <summary>Whether this player is holding a charged shield they could block with (not out flying, not knocked out, not in a cart).</summary>
    public static bool IsHoldingCharged(PlayerInfo player, out PlayerInventory inventory, out int slot)
    {
        inventory = player.Inventory;
        slot = -1;
        if (inventory == null || !ItemCatalog.TryGet(inventory.GetEffectivelyEquippedItem(), out CustomItem item) || item is not CapShieldItem
            || CapShieldThrows.IsThrown(player.netId) || player.Movement.IsKnockedOutOrRecovering || player.ActiveGolfCartSeat.IsValid())
        {
            return false;
        }
        slot = inventory.EquippedItemIndex;
        return inventory.GetEffectiveSlot(slot).remainingUses > 0;
    }

    /// <summary>Half-width (degrees) of the arc in front of the player that a raised shield covers.</summary>
    public const float BlockAngle = 70f;

    /// <summary>
    /// Whether <paramref name="victim"/> has the shield raised (right click held) against an attack coming from
    /// <paramref name="source"/>, a world point (zero when unknown: blocked only if <paramref name="unknownBlocks"/>, for attacks that fall from above).
    /// </summary>
    public static bool IsBlocking(PlayerInfo victim, Vector3 source, bool unknownBlocks = true)
    {
        if (!IsHoldingCharged(victim, out PlayerInventory inventory, out _) || !inventory.IsAimingItemNetworked())
        {
            return false;
        }
        if (source == Vector3.zero)
        {
            return unknownBlocks;
        }
        Vector3 toSource = Vector3.ProjectOnPlane(source - victim.transform.position, Vector3.up);
        if (toSource.sqrMagnitude < 0.04f)
        {
            return true; // straight above or on top of us
        }
        Vector3 forward = Vector3.ProjectOnPlane(victim.transform.forward, Vector3.up);
        return Vector3.Angle(forward, toSource) <= BlockAngle;
    }

    /// <summary>
    /// On the victim's client, from <see cref="ShieldKnockOutPatch"/> and friends: whether their held shield stops the
    /// hit, whichever way it comes from. Uses up a charge, with a clang everyone hears.
    /// </summary>
    public static bool TryBlock(PlayerInfo victim, KnockoutType knockoutType, Vector3 source, bool forced = false)
    {
        // A ball coming home onto your own head isn't an attack.
        if (knockoutType == KnockoutType.ReturnedBall || !IsHoldingCharged(victim, out PlayerInventory inventory, out int slot)
            || (!forced && !IsBlocking(victim, source, unknownBlocks: false)))
        {
            return false;
        }
        Vector3 chest = victim.transform.position + Vector3.up * 1f;
        Vector3 forward = Vector3.ProjectOnPlane(victim.transform.forward, Vector3.up).normalized;
        ItemUse.ConsumeUseAt(inventory, slot);
        CapShieldNet.SendBlock(chest + forward * 0.5f);
        if (inventory.GetEffectiveSlot(slot).remainingUses <= 0 && !inventory.IsUsingItemAtAll)
        {
            inventory.RemoveIfOutOfUses(slot, dueToFinishedItemUse: true);
        }
        Plugin.Log.LogInfo($"Captain America's shield blocked a {knockoutType} hit.");
        return true;
    }

    protected override void RegisterMessages() => CapShieldNet.InitSerializers();
    public override void OnStartServer() => CapShieldNet.RegisterServerHandlers();
    public override void OnStopServer() => CapShieldNet.ResetServer();
    public override void OnStartClient() => CapShieldNet.RegisterClientHandlers();
    public override void OnStopClient() => CapShieldThrows.ClearAll();
    public override void Tick() => CapShieldThrows.Tick();
}

/// <summary>
/// A held shield: strapped to the right hand by its back, facing straight out from the chest through the hand (so it
/// turns with the arm), and hidden while its owner's shield is out flying. Placed after the animation every frame.
/// </summary>
internal class CapShieldHeld : MonoBehaviour
{
    // From the hand bone to the shield's origin (the middle of its back plate), at scale 1. The attach bone sits a little
    // ahead of the palm, so the back plate goes right on it and the straps wrap the hand.
    private const float StrapDepth = 0.0f;

    private PlayerInfo owner;
    private Renderer[] renderers;
    private bool shown = true;

    private void LateUpdate()
    {
        if (owner == null)
        {
            owner = GetComponentInParent<PlayerInfo>();
            if (owner == null)
            {
                return; // the equipment template
            }
        }
        Strap();
        bool show = !CapShieldThrows.IsThrown(owner.netId);
        if (show == shown)
        {
            return;
        }
        shown = show;
        renderers ??= GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r != null)
            {
                r.enabled = show;
            }
        }
    }

    private Transform hand;
    private bool searchedHand;

    /// <summary>
    /// The hand bone the shield hangs from: the first skinned bone above it (the same one the lightsaber is gripped by).
    /// The equipment holder itself sits off to the side of the hand.
    /// </summary>
    private Transform FindHand()
    {
        if (searchedHand)
        {
            return hand;
        }
        searchedHand = true;
        var bones = new HashSet<Transform>();
        foreach (SkinnedMeshRenderer skinned in owner.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            foreach (Transform bone in skinned.bones)
            {
                if (bone != null)
                {
                    bones.Add(bone);
                }
            }
        }
        for (Transform t = transform.parent; t != null && t != owner.transform; t = t.parent)
        {
            if (bones.Contains(t))
            {
                hand = t;
                break;
            }
        }
        Plugin.Log.LogInfo($"Captain America's shield hand: {(hand != null ? hand.name : "not found, using the equipment holder")}." + (hand != null && hand.parent != null ? $" ({hand.parent.name} to {hand.name}: {Vector3.Distance(hand.parent.position, hand.position):0.000} m, chest to hand {(owner.ChestBone != null ? Vector3.Distance(owner.ChestBone.position, hand.position) : 0f):0.000} m)" : ""));
        return hand;
    }

    private void Strap()
    {
        Transform hand = FindHand();
        if (hand == null)
        {
            hand = owner.RightHandEquipmentSwitcher != null ? owner.RightHandEquipmentSwitcher.transform : transform.parent;
        }
        if (hand == null)
        {
            return;
        }
        Vector3 chest = owner.ChestBone != null ? owner.ChestBone.position : owner.transform.position + Vector3.up * 1.1f;
        // The palm: halfway from the wrist to the tool bone (which sits out toward the fingertips).
        Vector3 palm = hand.parent != null ? Vector3.Lerp(hand.parent.position, hand.position, 0.5f) : hand.position;
        Vector3 outward = palm - chest;
        if (outward.sqrMagnitude < 0.0025f)
        {
            outward = owner.transform.forward;
        }
        outward.Normalize();
        // The model's face looks along +Z; keep its top toward the player's up.
        Vector3 up = Vector3.ProjectOnPlane(owner.transform.up, outward);
        transform.rotation = Quaternion.LookRotation(outward, up.sqrMagnitude > 0.001f ? up : owner.transform.forward);
        transform.position = palm + outward * (StrapDepth * transform.lossyScale.x);
    }
}

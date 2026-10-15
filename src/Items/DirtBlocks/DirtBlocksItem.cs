using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ItemPack.Items;

/// <summary>
/// Dirt Blocks (Minecraft): a stack of 64. Right click places a block where you're looking: on the grid,
/// against the face of the block you're pointing at, like Minecraft. Blocks are solid for players, carts
/// and balls. A golf club swing breaks them (left click while holding the stack does too).
/// </summary>
internal class DirtBlocksItem : CustomItem
{
    public static DirtBlocksItem Instance { get; private set; }

    public override int Id => 106;
    public override string Key => "DirtBlocks";
    public override string DisplayName => "Dirt Blocks";
    // Held in one hand like the coffee cup.
    public override ItemType TemplateItem => ItemType.Coffee;
    public override EquipmentType TemplateEquipment => EquipmentType.CoffeeMug;
    public override string AssetName => "dirt_block";
    public override bool HandlesOwnInput => true;
    public override bool SuppressAiming => true;
    protected override bool HasMesh => false;

    protected override float DefaultItemBoxChance => 0.08f;
    protected override int DefaultUses => 64;

    internal static ConfigEntry<float> BlockSize;
    internal static ConfigEntry<float> Reach;
    internal static ConfigEntry<int> MaxBlocks;

    private const float PlaceCooldown = 0.18f;
    private const float BreakCooldown = 0.25f;
    private static float nextPlaceTime;
    private static float nextBreakTime;
    private static readonly HashSet<Vector3Int> brokenThisSwing = new();
    private static readonly Collider[] overlap = new Collider[64];
    private static readonly RaycastHit[] hits = new RaycastHit[32];

    public DirtBlocksItem() => Instance = this;

    protected override void BindConfig()
    {
        BlockSize = Bind("BlockSize", 1f, "Edge length of a block in meters (players are about 1.7 m tall).");
        Reach = Bind("Reach", 6f, "How far from you blocks can be placed or broken (meters).");
        MaxBlocks = Bind("MaxBlocks", 1500, "Most blocks allowed in the world at once. Host setting.");
    }

    public override GameObject CreateHeldModel(Transform parent, float scale) => DirtWorld.CreateHeldBlock(parent, scale);

    // Placing comes from Tick (right click); the game's use button is swallowed (HandlesOwnInput).
    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming)
    {
        yield break;
    }

    protected override void RegisterMessages() => DirtWorld.InitSerializers();
    public override void OnStartServer() => DirtWorld.RegisterServerHandlers();
    public override void OnStopServer() => DirtWorld.ResetServer();
    public override void OnStartClient() => DirtWorld.RegisterClientHandlers();
    public override void OnStopClient() => DirtWorld.ClearAll();
    public override void OnServerReady(NetworkConnectionToClient connection) => DirtWorld.SendExisting(connection);

    public override void Tick()
    {
        BreakWithGolfClub();
        if (!OwnInput.TryGetHolder<DirtBlocksItem>(out PlayerInventory inventory) || Mouse.current == null)
        {
            return;
        }
        if (Mouse.current.rightButton.wasPressedThisFrame && Time.time >= nextPlaceTime && OwnInput.CanUse(inventory))
        {
            nextPlaceTime = Time.time + PlaceCooldown;
            TryPlace(inventory);
        }
        else if (Mouse.current.leftButton.wasPressedThisFrame && Time.time >= nextBreakTime
            && TryGetLookedAtBlock(inventory.PlayerInfo, out _, out Vector3Int cell, out bool onBlock) && onBlock)
        {
            nextBreakTime = Time.time + BreakCooldown;
            DirtWorld.RequestBreak(cell);
        }
    }

    private static void TryPlace(PlayerInventory inventory)
    {
        PlayerInfo player = inventory.PlayerInfo;
        if (!TryGetLookedAtBlock(player, out RaycastHit hit, out Vector3Int targetCell, out bool onBlock))
        {
            return;
        }
        // Against a block: the neighbour on the face we're pointing at. Against the world: the cell just off the surface.
        Vector3Int cell = onBlock
            ? targetCell + RoundToAxis(hit.normal)
            : DirtWorld.WorldToCell(hit.point + hit.normal * (DirtWorld.Size * 0.5f));
        Vector3 center = DirtWorld.CellCenter(cell);
        if (DirtWorld.HasBlock(cell) || DirtWorld.IsOccupied(center)
            || (center - player.transform.position).sqrMagnitude > Reach.Value * Reach.Value * 1.3f)
        {
            return;
        }
        int layer = onBlock ? hit.collider.gameObject.layer : BlockLayerFor(hit.collider);
        ItemUse.ConsumeUse(inventory);
        DirtWorld.RequestPlace(cell, layer);
        inventory.RemoveIfOutOfUses(inventory.EquippedItemIndex, dueToFinishedItemUse: true);
    }

    /// <summary>
    /// What the crosshair is on, within reach. <paramref name="onBlock"/> tells whether it's one of our blocks
    /// (then <paramref name="cell"/> is that block).
    /// </summary>
    private static bool TryGetLookedAtBlock(PlayerInfo player, out RaycastHit best, out Vector3Int cell, out bool onBlock)
    {
        best = default;
        cell = default;
        onBlock = false;
        Camera cam = GameManager.Camera;
        if (cam == null)
        {
            return false;
        }
        Vector3 chest = player.transform.position + Vector3.up * 1.2f;
        float maxDistance = Vector3.Distance(cam.transform.position, chest) + Reach.Value;
        int count = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, hits, maxDistance, SurfaceMask, QueryTriggerInteraction.Ignore);
        best.distance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].distance < best.distance && !hits[i].collider.transform.IsChildOf(player.transform))
            {
                best = hits[i];
            }
        }
        if (best.distance == float.MaxValue || (best.point - chest).sqrMagnitude > Reach.Value * Reach.Value)
        {
            return false;
        }
        onBlock = DirtWorld.TryGetCell(best.collider, out cell);
        return true;
    }

    /// <summary>Breaks blocks the local player's golf club swing passes through (during the swing's hit window).</summary>
    private static void BreakWithGolfClub()
    {
        PlayerInfo player = GameManager.LocalPlayerInfo;
        PlayerGolfer golfer = player != null ? player.AsGolfer : null;
        if (golfer == null || !golfer.IsSwinging)
        {
            brokenThisSwing.Clear();
            return;
        }
        SwingType type = golfer.GetSwingType();
        double sinceSwing = Time.timeAsDouble - golfer.swingTimestamp;
        if (sinceSwing < GameManager.GolfSettings.GetSwingHitStartTime(type) || sinceSwing > GameManager.GolfSettings.GetSwingHitEndTime(type) + 0.05)
        {
            return;
        }
        Brimstone.Geometry.Box box = golfer.GetSwingHitBox();
        int count = Physics.OverlapBoxNonAlloc(box.center, box.HalfSize, overlap, golfer.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (DirtWorld.TryGetCell(overlap[i], out Vector3Int cell) && brokenThisSwing.Add(cell))
            {
                DirtWorld.RequestBreak(cell);
            }
        }
    }

    private static Vector3Int RoundToAxis(Vector3 normal)
    {
        Vector3 a = new(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
        if (a.x >= a.y && a.x >= a.z)
        {
            return new Vector3Int(normal.x > 0 ? 1 : -1, 0, 0);
        }
        if (a.y >= a.z)
        {
            return new Vector3Int(0, normal.y > 0 ? 1 : -1, 0);
        }
        return new Vector3Int(0, 0, normal.z > 0 ? 1 : -1);
    }

    /// <summary>
    /// The layer new blocks go on: the one of the ground we place onto, so players and balls treat blocks like
    /// that ground, except terrain layers (which other systems expect to be real terrain).
    /// </summary>
    private static int BlockLayerFor(Collider ground)
    {
        LayerSettings layers = GameManager.LayerSettings;
        int layer = ground.gameObject.layer;
        bool groundable = (layers.PlayerGroundableMask & (1 << layer)) != 0;
        if (groundable && layer != layers.TerrainLayer && layer != layers.TerrainAdditionLayer)
        {
            return layer;
        }
        if ((layers.PlayerGroundableMask & 1) != 0)
        {
            return 0; // Default
        }
        for (int l = 0; l < 32; l++)
        {
            if ((layers.PlayerGroundableMask & (1 << l)) != 0 && l != layers.TerrainLayer && l != layers.TerrainAdditionLayer
                && (layers.PlayersMask & (1 << l)) == 0 && (layers.GolfCartsMask & (1 << l)) == 0)
            {
                return l;
            }
        }
        return 0;
    }

    private static int surfaceMask = -1;

    private static int SurfaceMask
    {
        get
        {
            if (surfaceMask == -1)
            {
                LayerSettings layers = GameManager.LayerSettings;
                int mask = Physics.DefaultRaycastLayers;
                mask &= ~layers.PlayersMask & ~layers.GolfCartsMask & ~layers.BallMask & ~layers.DynamicBallMask & ~layers.FoliageMask;
                mask &= ~(1 << layers.ItemsLayer) & ~(1 << layers.VfxLayer) & ~(1 << layers.ElectromagnetShieldLayer) & ~(1 << layers.GolfCartPassengersLayer);
                surfaceMask = mask;
            }
            return surfaceMask;
        }
    }
}

/// <summary>Block sounds: a soft crunchy thud when placed, a crumble when broken (or the player's own dirt_place / dirt_break files).</summary>
internal static class DirtSounds
{
    private static FMOD.Sound place, breakSound;

    public static void PlayPlace(Vector3 position)
    {
        if (!place.hasHandle())
        {
            place = ItemAudio.LoadOrSynthesize(DirtBlocksItem.Instance, "dirt_place", () =>
                new Synth(0.25f, 11)
                    .Noise(Synth.Decay(0.05f), Synth.Const(0.08f), 1.2f)
                    .Noise(Synth.Decay(0.03f), Synth.Const(0.5f), 0.5f)
                    .Tone(Synth.Ramp(140f, 70f, 0.08f), Synth.Decay(0.05f), 0.6f)
                    .Normalized(0.7f));
        }
        ItemAudio.Play(place, ItemAudio.SfxBus, ItemAudio.Attenuate(position, 1f, 6f, 45f));
    }

    public static void PlayBreak(Vector3 position)
    {
        if (!breakSound.hasHandle())
        {
            breakSound = ItemAudio.LoadOrSynthesize(DirtBlocksItem.Instance, "dirt_break", () =>
            {
                var s = new Synth(0.45f, 12).Noise(Synth.Decay(0.12f), Synth.Const(0.12f), 1f);
                for (int i = 0; i < 9; i++)
                {
                    s.Click(0.02f + i * 0.035f, 900f + i * 140f, 0.006f, 0.35f, noise: 1.2f); // little bits of dirt
                }
                return s.Normalized(0.75f);
            });
        }
        ItemAudio.Play(breakSound, ItemAudio.SfxBus, ItemAudio.Attenuate(position, 1f, 6f, 45f));
    }
}

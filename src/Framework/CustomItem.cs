using System.Collections;
using System.IO;
using BepInEx.Configuration;
using Mirror;
using UnityEngine;

namespace ItemPack;

/// <summary>
/// Base class for every item in the pack. A subclass picks an id, a display name, a vanilla item to
/// borrow animations and use rules from, and implements what happens when the item is used.
/// The framework handles the rest: item/equipment registration, the localized name, the held model,
/// the HUD icon, item box spawns, dropping and the debug give key.
/// </summary>
internal abstract class CustomItem
{
    /// <summary>Unique ItemType/EquipmentType value. Vanilla uses 0-17; the pack uses 100 and up.</summary>
    public abstract int Id { get; }

    /// <summary>Short identifier, used for the asset folder (plugins\ItemPack\{Key}) and network ids.</summary>
    public abstract string Key { get; }

    public abstract string DisplayName { get; }

    /// <summary>Vanilla item whose ItemData is cloned (hold/use animations, use rules, HUD behavior).</summary>
    public abstract ItemType TemplateItem { get; }

    /// <summary>Vanilla equipment whose material and hand position the held model borrows.</summary>
    public virtual EquipmentType TemplateEquipment => EquipmentType.Landmine;

    /// <summary>File name (without extension) of the .obj/.mtl/_icon.png assets in the item's folder.</summary>
    public abstract string AssetName { get; }

    /// <summary>Crosshair shown while aiming with the item.</summary>
    public virtual ReticleType AimReticle => ReticleType.None;

    /// <summary>Crosshair for the current aiming state (override to change it, e.g. when scoped in).</summary>
    public virtual ReticleType GetAimReticle(PlayerInventory inventory) => AimReticle;

    /// <summary>
    /// True if the item reads the mouse itself (see its Tick) instead of going through the game's use button,
    /// e.g. to give left and right click different actions. The game's use input is then swallowed.
    /// </summary>
    public virtual bool HandlesOwnInput => false;

    /// <summary>True to keep the aim button from putting the player into aim mode (e.g. when it fires instead).</summary>
    public virtual bool SuppressAiming => false;

    /// <summary>False if the item has no .obj (e.g. its held model is built in code); only the icon is loaded.</summary>
    protected virtual bool HasMesh => true;

    /// <summary>The model shown in the player's hand. Defaults to the item's .obj model.</summary>
    public virtual GameObject CreateHeldModel(Transform parent, float scale) => Model.CreateModel(parent, scale);

    /// <summary>The model shown when the item lies on the ground after being dropped. Defaults to the held model.</summary>
    public virtual GameObject CreateDroppedModel(Transform parent) => CreateHeldModel(parent, HeldScale.Value);

    /// <summary>Folder holding the item's assets (plugins\ItemPack\{Key}).</summary>
    public string AssetDir { get; private set; }

    public ItemType Type => (ItemType)Id;
    public EquipmentType Equipment => (EquipmentType)Id;
    public ModelAsset Model { get; private set; }

    public ConfigEntry<float> ItemBoxChance { get; private set; }
    public ConfigEntry<int> UsesPerPickup { get; private set; }

    /// <summary>Most uses one pickup can hold, whatever the config says (e.g. 64 for a stack of dirt).</summary>
    protected virtual int MaxUsesPerPickup => 999;

    /// <summary>Uses per pickup: the config value, kept within 1..<see cref="MaxUsesPerPickup"/>.</summary>
    public int Uses => Mathf.Clamp(UsesPerPickup.Value, 1, MaxUsesPerPickup);
    public ConfigEntry<float> HeldScale { get; private set; }
    public ConfigEntry<Vector3> HeldOffset { get; private set; }
    public ConfigEntry<Vector3> HeldRotation { get; private set; }

    private ConfigFile config;

    protected virtual float DefaultItemBoxChance => 0.1f;
    protected virtual int DefaultUses => 1;
    protected virtual float DefaultHeldScale => 1f;
    protected virtual Vector3 DefaultHeldRotation => Vector3.zero;

    public void Init(ConfigFile configFile, string pluginDir)
    {
        config = configFile;
        ItemBoxChance = Bind("ItemBoxChance", DefaultItemBoxChance,
            $"Chance (0-1) that an item box gives a {DisplayName} instead of its normal item. Host setting.");
        UsesPerPickup = Bind("UsesPerPickup", DefaultUses, "Uses per pickup.");
        HeldScale = Bind("HeldScale", DefaultHeldScale, "Size of the item held in hand.");
        HeldOffset = Bind("HeldOffset", Vector3.zero, "Extra offset of the held item (hand space).");
        HeldRotation = Bind("HeldRotation", DefaultHeldRotation, "Rotation of the held item (hand space).");
        BindConfig();

        AssetDir = Path.Combine(pluginDir, Key);
        Model = ModelAsset.Load(AssetDir, AssetName, loadIcon: true, loadMesh: HasMesh);
        RegisterMessages();
    }

    /// <summary>Config entries go in a section named after the item.</summary>
    protected ConfigEntry<T> Bind<T>(string key, T defaultValue, string description) =>
        config.Bind(DisplayName, key, defaultValue, description);

    /// <summary>Bind item-specific config with <see cref="Bind{T}"/>.</summary>
    protected virtual void BindConfig() { }

    /// <summary>Adjust the ItemData cloned from <see cref="TemplateItem"/>.</summary>
    public virtual void ConfigureItemData(ItemData data) { }

    /// <summary>
    /// Runs when the local player uses the item. <paramref name="aiming"/> is true when they hold the
    /// aim button (or are reacting to an airhorn). Call <see cref="ItemUse.ConsumeUse"/> at the moment
    /// the item takes effect and <see cref="ItemUse.Finish"/> at the end.
    /// </summary>
    public abstract IEnumerator UseRoutine(PlayerInventory inventory, bool aiming);

    // ---- networking & lifetime hooks (all optional) ----

    /// <summary>Set Mirror Writer/Reader delegates for the item's messages (called once at startup).</summary>
    protected virtual void RegisterMessages() { }
    public virtual void OnStartServer() { }
    public virtual void OnStopServer() { }
    public virtual void OnStartClient() { }
    public virtual void OnStopClient() { }
    /// <summary>On the server, when a client finishes loading and is ready (e.g. to send it existing state).</summary>
    public virtual void OnServerReady(NetworkConnectionToClient connection) { }
    /// <summary>On the server, when a client leaves.</summary>
    public virtual void OnServerDisconnect(NetworkConnectionToClient connection) { }
    /// <summary>Every frame.</summary>
    public virtual void Tick() { }
    /// <summary>Every physics step, before the physics simulation.</summary>
    public virtual void FixedTick() { }
    /// <summary>Immediate-mode GUI pass (overlays such as a scope).</summary>
    public virtual void OnGUI() { }
}

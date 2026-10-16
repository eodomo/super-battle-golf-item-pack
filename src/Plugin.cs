using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ItemPack.Items;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ItemPack;

[BepInPlugin(Guid, "Super Battle Golf Item Pack", Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "eodomo.superbattlegolf.itempack";
    public const string Version = "0.9.0";

    internal static ManualLogSource Log;

    /// <summary>The pack's always-running object, for coroutines (e.g. the updater).</summary>
    internal static MonoBehaviour Runner { get; private set; }

#if DEVTOOLS
    // Testing keys (F6 / F7 item menu): only in builds made with -p:DevTools=true, never in releases.
    internal static ConfigEntry<bool> DebugGiveEnabled;
    internal static ConfigEntry<Key> DebugGiveKey;
    internal static ConfigEntry<Key> MenuKey;
#endif

    private void Awake()
    {
        Log = Logger;
        ModUpdater.CleanupAfterUpdate();

#if DEVTOOLS
        DebugGiveEnabled = Config.Bind("Debug", "GiveKeyEnabled", true,
            "Lets the host give themselves items with the keys below. Only works in the Driving Range, " +
            "or in a match with the game's cheats (console commands) rule turned on.");
        DebugGiveKey = Config.Bind("Debug", "GiveKey", Key.F6, "Gives the host the next pack item (cycles through all of them).");
        MenuKey = Config.Bind("Debug", "MenuKey", Key.F7, "Opens a menu to give the host any pack item or game item.");
#endif

        // ---- The pack's items. To add one, write a CustomItem subclass and list it here. ----
        ItemCatalog.Add(new BananaPeelItem());
        ItemCatalog.Add(new PortalGunItem());
        ItemCatalog.Add(new SteamWhistleItem());
        ItemCatalog.Add(new SniperRifleItem());
        ItemCatalog.Add(new PredatorItem());
        ItemCatalog.Add(new KamehamehaItem());
        ItemCatalog.Add(new DirtBlocksItem());
        ItemCatalog.Add(new BlueShellItem());
        ItemCatalog.Add(new BoogieBombItem());
        ItemCatalog.Add(new LightsaberItem(SaberColor.Blue));
        ItemCatalog.Add(new LightsaberItem(SaberColor.Green));
        ItemCatalog.Add(new LightsaberItem(SaberColor.Red));
        ItemCatalog.Add(new WebSlingerItem());
        ItemCatalog.Add(new ZordItem());
        ItemCatalog.Add(new BubbleShieldItem());
        ItemCatalog.Add(new CapShieldItem());
        ItemCatalog.Add(new EnderPearlItem());
        ItemCatalog.Add(new MeetingItem());

        string pluginDir = Path.GetDirectoryName(Info.Location);
        foreach (CustomItem item in ItemCatalog.All)
        {
            item.Init(Config, pluginDir);
        }
        MigrateConfig();
        DroppedItems.InitSerializers();
        RangerSet.Register(pluginDir);
        new Harmony(Guid).PatchAll();

        // Own runner object, so updates keep going even if the BepInEx manager object gets disabled.
        var runner = new GameObject("ItemPackRunner");
        runner.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(runner);
        Runner = runner.AddComponent<ItemPackRunner>();

        Log.LogInfo($"Item Pack {Version} loaded with {ItemCatalog.All.Count} items.");
    }

    /// <summary>
    /// A config file keeps whatever values it was saved with, so changed defaults don't reach existing installs.
    /// Bump <see cref="ConfigVersion"/> when a default must take effect everywhere, and reset it here.
    /// </summary>
    private void MigrateConfig()
    {
        const int ConfigVersion = 11;
        ConfigEntry<int> stored = Config.Bind("Internal", "ConfigVersion", 0, "Used by the pack to update old settings. Don't change it.");
        if (stored.Value >= ConfigVersion)
        {
            return;
        }
        if (stored.Value < 2)
        {
            // 0.2.0 rebalanced uses per pickup (one banana peel, one sniper shot, one kamehameha...).
            foreach (CustomItem item in ItemCatalog.All)
            {
                item.UsesPerPickup.Value = (int)item.UsesPerPickup.DefaultValue;
            }
        }
        if (stored.Value < 3)
        {
            // 0.3.0 replaced the flat ItemBoxChance (which made pack items most of what boxes gave) with
            // ItemBoxFrequency on top of rarity-based odds. Drop the old setting from the file.
            if (AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(Config) is System.Collections.Generic.Dictionary<ConfigDefinition, string> orphans)
            {
                foreach (ConfigDefinition definition in new System.Collections.Generic.List<ConfigDefinition>(orphans.Keys))
                {
                    if (definition.Key == "ItemBoxChance")
                    {
                        orphans.Remove(definition);
                    }
                }
            }
        }
        if (stored.Value < 4)
        {
            // The portal gun and dirt blocks no longer count shots and blocks: one use covers a whole timed
            // build / both portals. Also drop the steam whistle's music settings.
            foreach (CustomItem item in ItemCatalog.All)
            {
                if (item is PortalGunItem || item is DirtBlocksItem)
                {
                    item.UsesPerPickup.Value = (int)item.UsesPerPickup.DefaultValue;
                }
            }
            if (AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(Config) is System.Collections.Generic.Dictionary<ConfigDefinition, string> orphans)
            {
                foreach (ConfigDefinition definition in new System.Collections.Generic.List<ConfigDefinition>(orphans.Keys))
                {
                    if (definition.Section == "Steam Whistle" && (definition.Key == "MusicVolume" || definition.Key == "MusicSeconds"))
                    {
                        orphans.Remove(definition);
                    }
                }
            }
        }
        if (stored.Value < 5)
        {
            // Dirt blocks and the web slinger last 5 seconds now, and the web slinger's music is gone. The lightsaber
            // swings freely and its one use is its first hit.
            DirtBlocksItem.BuildSeconds.Value = (float)DirtBlocksItem.BuildSeconds.DefaultValue;
            LightsaberItem.Primary.UsesPerPickup.Value = (int)LightsaberItem.Primary.UsesPerPickup.DefaultValue;
            WebSlingerItem.SlingSeconds.Value = (float)WebSlingerItem.SlingSeconds.DefaultValue;
            if (AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(Config) is System.Collections.Generic.Dictionary<ConfigDefinition, string> orphans)
            {
                foreach (ConfigDefinition definition in new System.Collections.Generic.List<ConfigDefinition>(orphans.Keys))
                {
                    if (definition.Section == "Web Slinger" && (definition.Key == "PlayTheme" || definition.Key == "MusicVolume"))
                    {
                        orphans.Remove(definition);
                    }
                }
            }
        }
        if (stored.Value < 6)
        {
            // The boogie bomb is thrown faster and flatter, at where you aim.
            BoogieBombItem.ThrowSpeed.Value = (float)BoogieBombItem.ThrowSpeed.DefaultValue;
        }
        if (stored.Value < 7)
        {
            // Reset the lightsaber's uses again: some test installs reached version 5 while its default was still 3.
            LightsaberItem.Primary.UsesPerPickup.Value = (int)LightsaberItem.Primary.UsesPerPickup.DefaultValue;
        }
        if (stored.Value < 8)
        {
            // The Megazord's cart knockback was halved.
            ZordItem.CartKnockback.Value = (float)ZordItem.CartKnockback.DefaultValue;
        }
        if (stored.Value < 9)
        {
            // The steam whistle now goes after the opponent you aim at, shown with the lock-on marker.
            SteamWhistleItem.TargetMode.Value = (WhistleTargetMode)SteamWhistleItem.TargetMode.DefaultValue;
        }
        if (stored.Value < 10)
        {
            // Captain America's shield: one charge, 30% bigger, strapped to the hand by code (no fixed held rotation).
            CapShieldItem.Instance.UsesPerPickup.Value = (int)CapShieldItem.Instance.UsesPerPickup.DefaultValue;
            CapShieldItem.Instance.HeldScale.Value = (float)CapShieldItem.Instance.HeldScale.DefaultValue;
            CapShieldItem.Instance.HeldRotation.Value = (Vector3)CapShieldItem.Instance.HeldRotation.DefaultValue;
        }
        if (stored.Value < 11)
        {
            // The thrown shield now flies until it hits something (its range is how far before it's lost).
            CapShieldItem.ThrowRange.Value = (float)CapShieldItem.ThrowRange.DefaultValue;
        }
        stored.Value = ConfigVersion;
        Config.Save();
        Log.LogInfo($"Updated settings to version {ConfigVersion}.");
    }
}

internal class ItemPackRunner : MonoBehaviour
{
    private void Update()
    {
        ItemCatalog.EnsureRegistered();
        if (CosmeticCatalog.CanInstall)
        {
            CosmeticCatalog.Install();
        }
        foreach (CustomItem item in ItemCatalog.All)
        {
            try
            {
                item.Tick();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"{item.DisplayName} tick failed: {e}");
            }
        }
        Beams.Tick();
#if DEVTOOLS
        ItemMenu.Update();
        if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current[Key.F9].wasPressedThisFrame)
        {
            CosmeticsProbe.Dump();
        }
        if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current[Key.F11].wasPressedThisFrame)
        {
            DevTeleport.ToWardrobe();
        }
        if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current[Key.Digit0].wasPressedThisFrame)
        {
            DevShot.Take();
        }
        if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current[Key.F10].wasPressedThisFrame)
        {
            CosmeticsProbe2.Dump();
        }
#endif
    }

    private void OnGUI()
    {
        foreach (CustomItem item in ItemCatalog.All)
        {
            try
            {
                item.OnGUI();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"{item.DisplayName} GUI failed: {e}");
            }
        }
#if DEVTOOLS
        ItemMenu.OnGUI();
#endif
    }

    private void LateUpdate()
    {
        foreach (CustomItem item in ItemCatalog.All)
        {
            try
            {
                item.LateTick();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"{item.DisplayName} late tick failed: {e}");
            }
        }
    }

    private void FixedUpdate()
    {
        foreach (CustomItem item in ItemCatalog.All)
        {
            try
            {
                item.FixedTick();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"{item.DisplayName} fixed tick failed: {e}");
            }
        }
    }
}

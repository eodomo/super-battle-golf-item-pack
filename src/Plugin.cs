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
    public const string Version = "0.1.0";

    internal static ManualLogSource Log;

    internal static ConfigEntry<bool> DebugGiveEnabled;
    internal static ConfigEntry<Key> DebugGiveKey;
    internal static ConfigEntry<Key> MenuKey;

    private void Awake()
    {
        Log = Logger;

        DebugGiveEnabled = Config.Bind("Debug", "GiveKeyEnabled", true,
            "Lets the host give themselves items with the keys below. Only works in the Driving Range, " +
            "or in a match with the game's cheats (console commands) rule turned on.");
        DebugGiveKey = Config.Bind("Debug", "GiveKey", Key.F6, "Gives the host the next pack item (cycles through all of them).");
        MenuKey = Config.Bind("Debug", "MenuKey", Key.F7, "Opens a menu to give the host any pack item or game item.");

        // ---- The pack's items. To add one, write a CustomItem subclass and list it here. ----
        ItemCatalog.Add(new BananaPeelItem());
        ItemCatalog.Add(new PortalGunItem());
        ItemCatalog.Add(new SteamWhistleItem());
        ItemCatalog.Add(new SniperRifleItem());

        string pluginDir = Path.GetDirectoryName(Info.Location);
        foreach (CustomItem item in ItemCatalog.All)
        {
            item.Init(Config, pluginDir);
        }
        new Harmony(Guid).PatchAll();

        // Own runner object, so updates keep going even if the BepInEx manager object gets disabled.
        var runner = new GameObject("ItemPackRunner");
        runner.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(runner);
        runner.AddComponent<ItemPackRunner>();

        Log.LogInfo($"Item Pack {Version} loaded with {ItemCatalog.All.Count} items.");
    }
}

internal class ItemPackRunner : MonoBehaviour
{
    private void Update()
    {
        ItemCatalog.EnsureRegistered();
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
        ItemMenu.Update();
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
        ItemMenu.OnGUI();
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

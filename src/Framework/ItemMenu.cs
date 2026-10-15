using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ItemPack;

/// <summary>
/// Testing helpers for the host: a menu (F7) listing every pack item and vanilla item to give yourself,
/// and a quick key (F6) that gives the pack's items in turn. Both only work where giving items is fair
/// game: in the Driving Range, or in a match with the game's cheats rule on.
/// </summary>
internal static class ItemMenu
{
    private const float ReferenceHeight = 1080f;
    private static bool open;
    private static Rect window = new(40f, 120f, 560f, 620f);
    private static Vector2 scroll;
    private static int nextQuickGive;
    private static string status = "";
    private static float statusTime;
    private static GUIStyle sectionStyle;

    public static void Update()
    {
        if (!Plugin.DebugGiveEnabled.Value || Keyboard.current == null)
        {
            if (open)
            {
                SetOpen(false);
            }
            return;
        }
        if (Keyboard.current[Plugin.DebugGiveKey.Value].wasPressedThisFrame)
        {
            GiveNextPackItem();
        }
        if (Keyboard.current[Plugin.MenuKey.Value].wasPressedThisFrame)
        {
            SetOpen(!open);
        }
        else if (open && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            SetOpen(false);
        }
        if (open && GameManager.LocalPlayerInventory == null)
        {
            SetOpen(false); // left the match
        }
    }

    public static void OnGUI()
    {
        if (!open)
        {
            return;
        }
        float scale = Screen.height / ReferenceHeight;
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        window = GUI.Window(0x17E3, window, DrawWindow, "Item Pack - give items");
        GUI.matrix = previous;
    }

    private static void DrawWindow(int id)
    {
        sectionStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 15 };
        string blocked = WhyCantGive();
        GUILayout.Label(blocked ?? "Click an item to add it to your inventory.");
        if (Time.unscaledTime - statusTime < 4f && status.Length > 0)
        {
            GUILayout.Label(status);
        }

        GUI.enabled = blocked == null;
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Label("Item Pack", sectionStyle);
        DrawButtons(PackItems());
        GUILayout.Space(8f);
        GUILayout.Label("Game items", sectionStyle);
        DrawButtons(VanillaItems());
        GUILayout.EndScrollView();
        GUI.enabled = true;

        GUILayout.BeginHorizontal();
        GUILayout.Label($"{Plugin.MenuKey.Value} or Esc closes this menu. {Plugin.DebugGiveKey.Value} gives the next pack item.");
        if (GUILayout.Button("Close", GUILayout.Width(80f)))
        {
            SetOpen(false);
        }
        GUILayout.EndHorizontal();
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
    }

    private static void DrawButtons(List<(ItemType type, string name)> items)
    {
        for (int i = 0; i < items.Count; i += 2)
        {
            GUILayout.BeginHorizontal();
            for (int j = i; j < Math.Min(i + 2, items.Count); j++)
            {
                if (GUILayout.Button(items[j].name, GUILayout.Height(32f), GUILayout.Width(255f)))
                {
                    Give(items[j].type);
                }
            }
            GUILayout.EndHorizontal();
        }
    }

    private static List<(ItemType, string)> PackItems()
    {
        var list = new List<(ItemType, string)>();
        foreach (CustomItem item in ItemCatalog.All)
        {
            list.Add((item.Type, item.DisplayName));
        }
        return list;
    }

    private static List<(ItemType, string)> VanillaItems()
    {
        var list = new List<(ItemType, string)>();
        foreach (ItemType type in (ItemType[])Enum.GetValues(typeof(ItemType)))
        {
            if (type == ItemType.None || ItemCatalog.IsCustom(type))
            {
                continue;
            }
            string name = type.ToString();
            try
            {
                if (GameManager.AllItems != null && GameManager.AllItems.TryGetItemData(type, out ItemData data) && !string.IsNullOrEmpty(data.Name))
                {
                    name = data.Name;
                }
            }
            catch (Exception)
            {
                // localization not ready yet: keep the enum name
            }
            list.Add((type, name));
        }
        return list;
    }

    // ---- giving ----

    /// <summary>Null if the local player may give themselves items right now, else the reason they can't.</summary>
    private static string WhyCantGive()
    {
        if (GameManager.LocalPlayerInventory == null)
        {
            return "Join a game first.";
        }
        if (!Mirror.NetworkServer.active)
        {
            return "Only the host can give items.";
        }
        if (!SingletonBehaviour<DrivingRangeManager>.HasInstance && !MatchSetupRules.IsCheatsEnabled())
        {
            return "Only in the Driving Range, or in a match with cheats turned on.";
        }
        return null;
    }

    public static void Give(ItemType type)
    {
        string blocked = WhyCantGive();
        if (blocked != null)
        {
            SetStatus(blocked);
            return;
        }
        if (GameManager.AllItems == null || !GameManager.AllItems.TryGetItemData(type, out ItemData data))
        {
            SetStatus($"{type} isn't loaded yet.");
            return;
        }
        int uses = ItemCatalog.TryGet(type, out CustomItem custom) ? custom.Uses : data.MaxUses;
        string name = custom != null ? custom.DisplayName : type.ToString();
        bool added = GameManager.LocalPlayerInventory.ServerTryAddItem(type, uses);
        SetStatus(added ? $"Gave you: {name}." : "Your inventory is full.");
    }

    private static void GiveNextPackItem()
    {
        if (ItemCatalog.All.Count == 0)
        {
            return;
        }
        CustomItem item = ItemCatalog.All[nextQuickGive % ItemCatalog.All.Count];
        nextQuickGive++;
        Give(item.Type);
    }

    private static void SetStatus(string message)
    {
        status = message;
        statusTime = Time.unscaledTime;
        Plugin.Log.LogInfo($"Give: {message}");
    }

    private static void SetOpen(bool value)
    {
        if (open == value)
        {
            return;
        }
        open = value;
        // The game's dev console mode turns off gameplay controls and frees the mouse cursor.
        if (open)
        {
            InputManager.EnableMode(InputMode.DevConsole);
        }
        else
        {
            InputManager.DisableMode(InputMode.DevConsole);
        }
    }
}

using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ItemPack;

/// <summary>
/// Adds an "Item Pack" row with a "Check for updates" button to the game's Settings menu (General tab), by
/// cloning the row that holds the game's own "Skip/Reset tutorial" button so it looks and behaves native.
/// </summary>
[HarmonyPatch(typeof(SettingsMenu), "Start")]
internal static class UpdateButton
{
    private static void Postfix(SettingsMenu __instance)
    {
        try
        {
            Create(__instance);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Couldn't add the update button to the settings menu: {e}");
        }
    }

    private static void Create(SettingsMenu menu)
    {
        Button original = menu.skipOrResetTutorial;
        if (original == null)
        {
            Plugin.Log.LogWarning("Settings menu has no tutorial button to copy; no update button.");
            return;
        }

        // Copy the whole row (label + button): climb to the element that sits directly in the list's layout group.
        Transform row = original.transform;
        while (row.parent != null && row.parent.GetComponent<VerticalLayoutGroup>() == null && row.parent != menu.transform)
        {
            row = row.parent;
        }
        if (row.parent == null || row.parent == menu.transform)
        {
            row = original.transform;
        }
        GameObject clone = Object.Instantiate(row.gameObject, row.parent);
        clone.name = "ItemPackUpdateRow";
        clone.transform.SetSiblingIndex(row.GetSiblingIndex() + 1);

        // The copies of the game's localization components would overwrite our text: remove them.
        foreach (LocalizeStringEvent localize in clone.GetComponentsInChildren<LocalizeStringEvent>(true))
        {
            Object.DestroyImmediate(localize);
        }

        Button button = clone.GetComponentInChildren<Button>(true);
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => ModUpdater.CheckAndInstall(Plugin.Runner));

        TMP_Text buttonLabel = button.GetComponentInChildren<TMP_Text>(true);
        foreach (TMP_Text text in clone.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text != buttonLabel)
            {
                text.text = $"Item Pack v{Plugin.Version}";
            }
        }
        if (buttonLabel == null)
        {
            return;
        }
        buttonLabel.text = ModUpdater.Status;

        void Refresh()
        {
            if (buttonLabel == null)
            {
                ModUpdater.StatusChanged -= Refresh; // menu was destroyed
                return;
            }
            buttonLabel.text = ModUpdater.Status;
        }
        ModUpdater.StatusChanged += Refresh;
        Plugin.Log.LogInfo($"Added the update button to the settings menu ({row.name}).");
    }
}

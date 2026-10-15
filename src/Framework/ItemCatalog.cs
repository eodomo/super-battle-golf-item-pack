using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace ItemPack;

/// <summary>
/// The pack's items, and their registration in the game's item and equipment tables. Registration is
/// polled every frame because the game can (re)initialize these ScriptableObjects at any time, e.g.
/// when they are first loaded.
/// </summary>
internal static class ItemCatalog
{
    private static readonly MethodInfo MemberwiseCloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly List<CustomItem> items = new();
    private static readonly Dictionary<ItemType, CustomItem> byType = new();
    private static readonly Dictionary<CustomItem, Equipment> equipmentPrefabs = new();
    private static bool localized;

    public static IReadOnlyList<CustomItem> All => items;

    public static void Add(CustomItem item)
    {
        if (byType.ContainsKey(item.Type))
        {
            throw new ArgumentException($"Item id {item.Id} is used twice ({byType[item.Type].DisplayName} and {item.DisplayName}).");
        }
        items.Add(item);
        byType[item.Type] = item;
    }

    public static bool TryGet(ItemType type, out CustomItem item) => byType.TryGetValue(type, out item);

    public static bool IsCustom(ItemType type) => byType.ContainsKey(type);

    public static bool IsRegistered(CustomItem item) =>
        GameManager.AllItems != null && GameManager.AllItems.TryGetItemData(item.Type, out _);

    public static void EnsureRegistered()
    {
        foreach (CustomItem item in items)
        {
            try
            {
                RegisterItem(item);
                RegisterEquipment(item);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Registering {item.DisplayName} failed: {e}");
            }
        }
        RegisterNames();
        ItemBoxes.ScanLoaded();
    }

    private static void RegisterItem(CustomItem item)
    {
        ItemCollection collection = GameManager.AllItems;
        if (collection == null || collection.allItemData.ContainsKey(item.Type))
        {
            return;
        }
        if (!collection.allItemData.TryGetValue(item.TemplateItem, out ItemData template))
        {
            return;
        }

        var data = (ItemData)MemberwiseCloneMethod.Invoke(template, null);
        data.Type = item.Type;
        data.name = null;
        data.MaxUses = item.Uses;
        if (item.Model.Icon != null)
        {
            data.Icon = item.Model.Icon;
        }
        // Prefab is only used for dropped items. Dropping a pack item discards it (see DropItemPatch),
        // but keep it something harmless.
        if (collection.allItemData.TryGetValue(ItemType.Coffee, out ItemData coffee))
        {
            data.Prefab = coffee.Prefab;
        }
        item.ConfigureItemData(data);
        collection.allItemData[item.Type] = data;
        Plugin.Log.LogInfo($"Registered item {item.DisplayName} ({item.Id}).");
    }

    private static void RegisterEquipment(CustomItem item)
    {
        if (!SingletonBehaviour<EquipmentManager>.HasInstance)
        {
            return;
        }
        EquipmentCollection collection = SingletonBehaviour<EquipmentManager>.Instance.equipmentCollection;
        if (collection == null || collection.equipmentDictionary.ContainsKey(item.Equipment))
        {
            return;
        }
        if (!collection.equipmentDictionary.TryGetValue(item.TemplateEquipment, out EquipmentSettings template) || template.Prefab == null)
        {
            return;
        }

        Renderer templateRenderer = template.Prefab.GetComponentInChildren<MeshRenderer>(true);
        item.Model.InitMaterials(templateRenderer != null ? templateRenderer.sharedMaterial : null);

        if (!equipmentPrefabs.TryGetValue(item, out Equipment prefab) || prefab == null)
        {
            // Inactive template that the EquipmentManager instantiates whenever someone holds the item.
            var root = new GameObject($"{item.Key}Equipment");
            root.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(root);
            prefab = root.AddComponent<Equipment>();
            prefab.Type = item.Equipment;
            prefab.HasCosmetics = false;

            GameObject model = item.CreateHeldModel(root.transform, item.HeldScale.Value);
            Vector3 basePosition = templateRenderer != null
                ? template.Prefab.transform.InverseTransformPoint(templateRenderer.transform.position)
                : Vector3.zero;
            model.transform.localPosition = basePosition + item.HeldOffset.Value;
            model.transform.localEulerAngles = item.HeldRotation.Value;
            equipmentPrefabs[item] = prefab;
        }

        collection.equipmentDictionary[item.Equipment] = new EquipmentSettings { Type = item.Equipment, Prefab = prefab };
        Plugin.Log.LogInfo($"Registered equipment {item.DisplayName}.");
    }

    private static void RegisterNames()
    {
        if (localized || !LocalizationSettings.InitializationOperation.IsDone)
        {
            return;
        }
        localized = true;
        foreach (CustomItem item in items)
        {
            // ItemData looks names up as "ITEM_{Type}", which is e.g. "ITEM_100" for our unnamed enum values.
            LocalizationManager.AddStringToAllLocales(StringTable.Data, $"ITEM_{item.Type}", item.DisplayName);
        }
        Plugin.Log.LogInfo($"Registered names for {items.Count} items.");
    }
}

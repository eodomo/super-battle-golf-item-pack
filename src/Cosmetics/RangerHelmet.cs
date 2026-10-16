using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ItemPack;

/// <summary>The Power Ranger helmet: our own model (Assets\PowerRanger\helmet.obj) as a hat, tinted in the ranger's color.</summary>
internal static class RangerHelmet
{
    private const string TemplateSource = "Assets/Prefabs/Cosmetics/Hats/Metadata/Default Hat.asset";
    private static Once<GameObject> prefab;

    public static void Register(string pluginDir)
    {
        string dir = Path.Combine(pluginDir, "PowerRanger");
        prefab = new Once<GameObject>(done => Build(dir, done));

        const string name = "Ranger Helmet";
        string modelGuid = CosmeticCatalog.MakeGuid("model:" + name);
        Sprite icon = RangerIcons.FromFile(Path.Combine(dir, "helmet_icon.png")) ?? RangerIcons.Make(name, RangerSet.Colors[0].Suit);
        PlayerCosmeticsMetadata md = RangerSet.NewMetadata(name, PlayerCosmeticsMetadata.Category.Hat, icon);
        md.model = new AssetReference(modelGuid);
        // The helmet covers the whole head: no hair, hat, face items or ears; nothing else can be worn with it.
        md.allowedCosmetics = PlayerCosmeticObject.ModelSlot.None;
        var variations = new List<PlayerCosmeticsMetadata.Variation>();
        foreach (RangerColor color in RangerSet.Colors)
        {
            PlayerCosmeticsMetadata.Variation v = RangerSet.NewVariation(color, null);
            v.materialIndex = 0; // only the shell takes the color
            v.tintColor = color.Suit;
            variations.Add(v);
        }
        md.variations = variations.ToArray();
        CosmeticCatalog.AddModel(modelGuid, deliver => prefab.Get(deliver));
        CosmeticCatalog.AddMetadata($"ItemPack/Cosmetics/{name}", md);
    }

    public static void Prebuild() => prefab.Get(_ => { });

    private static void Build(string dir, Action<GameObject> done)
    {
        try
        {
            Addressables.LoadAssetAsync<PlayerCosmeticsMetadata>(TemplateSource).Completed += metaHandle =>
            {
                if (metaHandle.Result == null)
                {
                    done(null);
                    return;
                }
                Addressables.LoadAssetAsync<GameObject>(metaHandle.Result.model.RuntimeKey).Completed += prefabHandle =>
                {
                    try
                    {
                        MeshRenderer templateRenderer = prefabHandle.Result != null ? prefabHandle.Result.GetComponentInChildren<MeshRenderer>() : null;
                        if (templateRenderer == null)
                        {
                            Plugin.Log.LogError("Ranger helmet: template hat has no renderer.");
                            done(null);
                            return;
                        }
                        Mesh mesh = ObjLoader.Load(Path.Combine(dir, "helmet.obj"), Path.Combine(dir, "helmet.mtl"), out Color[] colors);
                        mesh.name = "Helmet_PowerRanger";
                        mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        var materials = new Material[colors.Length];
                        for (int i = 0; i < colors.Length; i++)
                        {
                            var material = new Material(templateRenderer.sharedMaterial) { name = $"Helmet_{i}" };
                            material.SetTexture("_MainTex", SolidTexture(colors[i]));
                            material.SetColor("_Color", Color.white);
                            materials[i] = material;
                        }

                        var root = new GameObject("Hat_PowerRanger");
                        root.SetActive(false);
                        root.hideFlags = HideFlags.HideAndDontSave;
                        UnityEngine.Object.DontDestroyOnLoad(root);
                        var cosmetic = root.AddComponent<PlayerCosmeticObject>();
                        cosmetic.modelDisable = PlayerCosmeticObject.ModelSlot.Hair | PlayerCosmeticObject.ModelSlot.Hat
                            | PlayerCosmeticObject.ModelSlot.Face | PlayerCosmeticObject.ModelSlot.Ears | PlayerCosmeticObject.ModelSlot.FaceLower;
                        cosmetic.allowedCosmetics = PlayerCosmeticObject.ModelSlot.None;
                        var child = new GameObject("Helmet");
                        child.transform.SetParent(root.transform, false);
                        child.AddComponent<MeshFilter>().sharedMesh = mesh;
                        child.AddComponent<MeshRenderer>().sharedMaterials = materials;
                        Plugin.Log.LogInfo($"Ranger helmet built: {mesh.vertexCount} verts, {materials.Length} materials.");
                        done(root);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError($"Ranger helmet build failed: {e}");
                        done(null);
                    }
                };
            };
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Ranger helmet load failed: {e}");
            done(null);
        }
    }

    private static Texture2D SolidTexture(Color color)
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }
        texture.SetPixels(pixels);
        texture.Apply();
        texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return texture;
    }
}

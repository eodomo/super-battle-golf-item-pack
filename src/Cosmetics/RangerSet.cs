using System;
using System.Collections.Generic;
using ItemPack.Items;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;

namespace ItemPack;

internal readonly struct RangerColor
{
    public readonly string Name;
    public readonly Color Suit;
    public readonly Color Menu;
    public readonly PlayerCosmeticsMetadata.TeamFlags Teams;

    public RangerColor(string name, Color suit, Color menu, PlayerCosmeticsMetadata.TeamFlags teams)
    {
        Name = name;
        Suit = suit;
        Menu = menu;
        Teams = teams;
    }
}

/// <summary>
/// The Power Ranger outfit: helmet, suit and boots, each in six colors. The suit and boots are built at runtime
/// from the game's own meshes (read back from the GPU) and painted with generated textures; the helmet is a model
/// of our own. See <see cref="CosmeticCatalog"/> for how they reach the outfit menu.
/// </summary>
internal static class RangerSet
{
    // Region ids of the baked textures. Region 0 is the ranger's color; the rest are fixed.
    public const byte Suit = 0, White = 1, Gold = 2, Black = 3, Silver = 4;
    private static readonly Color[] Fixed =
    {
        Color.clear, // placeholder for the suit color, replaced per variation
        new Color(0.96f, 0.96f, 0.98f),
        new Color(0.93f, 0.72f, 0.16f),
        new Color(0.07f, 0.07f, 0.09f),
        new Color(0.72f, 0.76f, 0.82f),
    };

    public static readonly RangerColor[] Colors =
    {
        new("Red", new Color(0.86f, 0.09f, 0.11f), new Color(0.88f, 0.33f, 0.35f), PlayerCosmeticsMetadata.TeamFlags.Red),
        new("Blue", new Color(0.10f, 0.26f, 0.88f), new Color(0.31f, 0.29f, 0.83f), PlayerCosmeticsMetadata.TeamFlags.Blue),
        new("Yellow", new Color(0.98f, 0.80f, 0.08f), new Color(0.94f, 0.80f, 0.38f), PlayerCosmeticsMetadata.TeamFlags.Neither),
        new("Pink", new Color(0.98f, 0.45f, 0.72f), new Color(0.86f, 0.45f, 0.85f), PlayerCosmeticsMetadata.TeamFlags.Neither),
        new("Black", new Color(0.13f, 0.13f, 0.15f), new Color(0.24f, 0.24f, 0.24f), PlayerCosmeticsMetadata.TeamFlags.Neither),
        new("Green", new Color(0.08f, 0.66f, 0.24f), new Color(0.57f, 0.87f, 0.42f), PlayerCosmeticsMetadata.TeamFlags.Neither),
    };

    private const string SuitSource = "Assets/Prefabs/Cosmetics/Clothing/Metadata/Suit.asset";
    private const string BootSource = "Assets/Prefabs/Cosmetics/Shoes/Metadata/Worker.asset";

    private static string pluginDir;
    private static Once<RangerBody> suit;
    private static Once<RangerBody> boots;

    /// <summary>A built skinned model plus the baked region map its color textures are made from.</summary>
    internal sealed class RangerBody
    {
        public GameObject Prefab;
        public byte[] Regions;
        public int Size;
    }

    public static void Register(string pluginDirectory)
    {
        pluginDir = pluginDirectory;
        suit = new Once<RangerBody>(done => BuildSkinned(SuitSource, 1024, SuitBake, "Body_PowerRanger", done));
        boots = new Once<RangerBody>(done => BuildSkinned(BootSource, 512, BootBake, "Shoes_PowerRanger", done));

        RegisterSkinned("Ranger Suit", PlayerCosmeticsMetadata.Category.Clothing, suit, "suit_icon.png");
        RegisterSkinned("Ranger Boots", PlayerCosmeticsMetadata.Category.Shoes, boots, "boots_icon.png");
        RangerHelmet.Register(pluginDir);
        RangerSword.Register(pluginDir);
    }

    /// <summary>Builds the models ahead of time: a first request that has to wait for the GPU readback loses the race with the game's loading.</summary>
    public static void Prebuild()
    {
        suit.Get(_ => { });
        boots.Get(_ => { });
        RangerHelmet.Prebuild();
        RangerSword.Prebuild();
    }

    internal static PlayerCosmeticsMetadata NewMetadata(string name, PlayerCosmeticsMetadata.Category category, Sprite icon)
    {
        var md = ScriptableObject.CreateInstance<PlayerCosmeticsMetadata>();
        md.name = name;
        md.hideFlags = HideFlags.DontUnloadUnusedAsset;
        md.category = category;
        md.icon = icon;
        md.cost = 0;
        md.allowedTeams = PlayerCosmeticsMetadata.TeamFlags.Both;
        md.allowedInFreeForAll = true;
        md.persistentGuid = CosmeticCatalog.MakeGuid("meta:" + name);
        md.allowedCosmetics = (PlayerCosmeticObject.ModelSlot)(-1);
        return md;
    }

    internal static PlayerCosmeticsMetadata.Variation NewVariation(RangerColor color, string textureGuid)
    {
        return new PlayerCosmeticsMetadata.Variation
        {
            materialIndex = -1,
            menuColor = color.Menu,
            tintColor = Color.white,
            emissiveColor = Color.black,
            textureOverride = textureGuid != null ? new AssetReferenceTexture2D(textureGuid) : null,
            allowedTeams = color.Teams,
            allowedInFreeForAll = true,
        };
    }

    private static void RegisterSkinned(string name, PlayerCosmeticsMetadata.Category category, Once<RangerBody> body, string iconFile)
    {
        string modelGuid = CosmeticCatalog.MakeGuid("model:" + name);
        PlayerCosmeticsMetadata md = NewMetadata(name, category,
            RangerIcons.FromFile(System.IO.Path.Combine(pluginDir, "PowerRanger", iconFile)) ?? RangerIcons.Make(name, Colors[0].Suit));
        md.model = new AssetReference(modelGuid);
        var variations = new List<PlayerCosmeticsMetadata.Variation>();
        for (int i = 0; i < Colors.Length; i++)
        {
            RangerColor color = Colors[i];
            string textureGuid = CosmeticCatalog.MakeGuid($"tex:{name}:{color.Name}");
            variations.Add(NewVariation(color, textureGuid));
            CosmeticCatalog.AddKey(textureGuid, typeof(Texture2D), deliver =>
                body.Get(b => deliver(b == null ? null : MakeTexture(b, color, $"{name}_{color.Name}"))));
        }
        md.variations = variations.ToArray();
        CosmeticCatalog.AddModel(modelGuid, deliver => body.Get(b => deliver(b?.Prefab)));
        CosmeticCatalog.AddMetadata($"ItemPack/Cosmetics/{name}", md);
    }

    private static Texture2D MakeTexture(RangerBody body, RangerColor color, string name)
    {
        var palette = (Color[])Fixed.Clone();
        palette[Suit] = color.Suit;
        return UvBake.ToTexture(body.Regions, body.Size, palette, name);
    }

    // ---- Building skinned models from the game's meshes ----

    private delegate Mesh Bake(Mesh source, MeshCopy copy, BoneWeight[] weights, out byte[] regions, int size);

    private static void BuildSkinned(string sourceKey, int size, Bake bake, string name, Action<RangerBody> done)
    {
        try
        {
            Addressables.LoadAssetAsync<PlayerCosmeticsMetadata>(sourceKey).Completed += metaHandle =>
            {
                if (metaHandle.Result == null)
                {
                    Plugin.Log.LogError($"Ranger: source cosmetic {sourceKey} not found.");
                    done(null);
                    return;
                }
                Addressables.LoadAssetAsync<GameObject>(metaHandle.Result.model.RuntimeKey).Completed += prefabHandle =>
                {
                    SkinnedMeshRenderer source = prefabHandle.Result != null ? prefabHandle.Result.GetComponentInChildren<SkinnedMeshRenderer>() : null;
                    if (source == null)
                    {
                        Plugin.Log.LogError($"Ranger: source model of {sourceKey} has no skinned mesh.");
                        done(null);
                        return;
                    }
                    Mesh mesh = source.sharedMesh;
                    MeshReadback.Request(mesh, copy =>
                    {
                        if (copy == null)
                        {
                            done(null);
                            return;
                        }
                        SkinReadback.Request(mesh, weights =>
                        {
                            try
                            {
                                Mesh built = bake(mesh, copy, weights, out byte[] regions, size);
                                if (built == null)
                                {
                                    done(null);
                                    return;
                                }
                                var root = new GameObject(name);
                                root.SetActive(false);
                                root.hideFlags = HideFlags.HideAndDontSave;
                                UnityEngine.Object.DontDestroyOnLoad(root);
                                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                                renderer.sharedMesh = built;
                                var material = new Material(source.sharedMaterial) { name = name };
                                material.SetTexture("_TintMask", Texture2D.blackTexture);
                                material.SetTexture("_MainTex", MakeTexture(new RangerBody { Regions = regions, Size = size }, Colors[0], name + "_default"));
                                renderer.sharedMaterial = material;
                                Plugin.Log.LogInfo($"Ranger: built {name} ({built.vertexCount} verts) from {mesh.name}.");
                                done(new RangerBody { Prefab = root, Regions = regions, Size = size });
                            }
                            catch (Exception e)
                            {
                                Plugin.Log.LogError($"Ranger: building {name} failed: {e}");
                                done(null);
                            }
                        });
                    });
                };
            };
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Ranger: loading {sourceKey} failed: {e}");
            done(null);
        }
    }

    private static Mesh MakeMesh(Mesh source, MeshCopy copy, BoneWeight[] weights, string name)
    {
        var mesh = new Mesh { name = name, hideFlags = HideFlags.DontUnloadUnusedAsset };
        mesh.vertices = copy.Positions;
        mesh.normals = copy.Normals;
        mesh.uv = copy.Uvs;
        mesh.subMeshCount = 1;
        mesh.SetTriangles(copy.Triangles[0], 0);
        if (weights != null)
        {
            mesh.boneWeights = weights;
        }
        mesh.bindposes = source.bindposes;
        mesh.bounds = source.bounds;
        return mesh;
    }

    // Body bone order (matches the player's body renderer): 9..29 left wrist + fingers, 33..53 right.
    private static bool IsHandBone(int bone) => (bone >= 9 && bone <= 29) || (bone >= 33 && bone <= 53);

    private static Mesh SuitBake(Mesh source, MeshCopy copy, BoneWeight[] weights, out byte[] regions, int size)
    {
        regions = null;
        if (weights == null || source.bindposes.Length != 64)
        {
            Plugin.Log.LogError($"Ranger: unexpected suit mesh (bindposes {source.bindposes.Length}, weights {(weights == null ? "none" : "ok")}).");
            return null;
        }
        var hand = new float[copy.Positions.Length];
        for (int v = 0; v < hand.Length; v++)
        {
            BoneWeight bw = weights[v];
            float sum = 0;
            if (IsHandBone(bw.boneIndex0)) sum += bw.weight0;
            if (IsHandBone(bw.boneIndex1)) sum += bw.weight1;
            if (IsHandBone(bw.boneIndex2)) sum += bw.weight2;
            if (IsHandBone(bw.boneIndex3)) sum += bw.weight3;
            hand[v] = sum;
        }

        regions = UvBake.Regions(copy, size, hand, RangerPatterns.Suit);
        return MakeMesh(source, copy, weights, "Body_PowerRanger");
    }

    // Shoe renderer bone order: 57 L_Ankle, 58 L_Toe, 62 R_Ankle, 63 R_Toe (index 6 is the face placement bone).
    private const int LAnkle = 57, LToe = 58, RAnkle = 62, RToe = 63;

    private static Mesh BootBake(Mesh source, MeshCopy copy, BoneWeight[] ignored, out byte[] regions, int size)
    {
        regions = null;
        if (source.bindposes.Length != 67)
        {
            Plugin.Log.LogWarning($"Ranger: boot source has {source.bindposes.Length} bindposes (expected 67); bone order may be off.");
        }
        // Drop any triangle joining the left and right boot: the source mesh has a strip between them, which stretches
        // across the gap when the legs move.
        var kept = new System.Collections.Generic.List<int>();
        int[] all = copy.Triangles[0];
        for (int t = 0; t + 2 < all.Length; t += 3)
        {
            bool anyLeft = copy.Positions[all[t]].x < 0f || copy.Positions[all[t + 1]].x < 0f || copy.Positions[all[t + 2]].x < 0f;
            bool anyRight = copy.Positions[all[t]].x >= 0f || copy.Positions[all[t + 1]].x >= 0f || copy.Positions[all[t + 2]].x >= 0f;
            if (!(anyLeft && anyRight))
            {
                kept.Add(all[t]);
                kept.Add(all[t + 1]);
                kept.Add(all[t + 2]);
            }
        }
        Plugin.Log.LogInfo($"Ranger boots: dropped {(all.Length - kept.Count) / 3} triangles joining the two boots.");
        copy = new MeshCopy { Positions = copy.Positions, Normals = copy.Normals, Uvs = copy.Uvs, Triangles = new[] { kept.ToArray() } };
        int count = copy.Positions.Length;
        var weights = new BoneWeight[count];
        for (int v = 0; v < count; v++)
        {
            Vector3 p = copy.Positions[v];
            bool left = p.x < 0;
            float toe = Mathf.InverseLerp(0.08f, 0.17f, p.z) * (1f - Mathf.InverseLerp(0.15f, 0.25f, p.y));
            weights[v] = new BoneWeight
            {
                boneIndex0 = left ? LAnkle : RAnkle,
                weight0 = 1f - toe,
                boneIndex1 = left ? LToe : RToe,
                weight1 = toe,
            };
        }
        regions = UvBake.Regions(copy, size, new float[count], RangerPatterns.Boot);
        return MakeMesh(source, copy, weights, "Shoes_PowerRanger");
    }
}

/// <summary>Where each region of the ranger look goes on the body and boots (model space, bind pose).</summary>
internal static class RangerPatterns
{
    private static readonly Vector3 Wrist = new(0.456f, 0.943f, 0.086f);
    private static readonly Vector3 Elbow = new(0.339f, 1.027f, -0.057f);
    private static readonly Vector3 ArmAxis = (Wrist - Elbow).normalized;

    public static byte Suit(Vector3 p, Vector3 n, float hand)
    {
        float ax = Mathf.Abs(p.x);

        // Gloves: white from just below the wrist up to a gold ring on the forearm.
        if (ax > 0.25f)
        {
            Vector3 d = new Vector3(ax, p.y, p.z) - Wrist;
            float t = Vector3.Dot(d, ArmAxis);
            float perp = (d - ArmAxis * t).magnitude;
            if (perp < 0.14f)
            {
                if (hand > 0.5f || t > -0.085f)
                {
                    return RangerSet.White;
                }
                if (t > -0.105f)
                {
                    return RangerSet.Gold;
                }
            }
        }

        // Belt with a buckle.
        if (ax < 0.30f && p.y > 0.80f && p.y < 0.885f)
        {
            if (ax < 0.04f && n.z > 0.2f && p.y > 0.805f && p.y < 0.88f)
            {
                return RangerSet.Silver;
            }
            if (p.y > 0.815f && p.y < 0.865f)
            {
                return RangerSet.Gold;
            }
        }

        // White chevron across the chest.
        if (n.z > -0.1f && p.y > 0.95f && p.y < 1.22f && ax < 0.2f)
        {
            float d = p.y - (0.985f + 1.25f * ax);
            if (d > -0.015f && d < 0.05f)
            {
                return RangerSet.White;
            }
        }
        return RangerSet.Suit;
    }

    public static byte Boot(Vector3 p, Vector3 n, float unused)
    {
        if (p.y > 0.255f)
        {
            return RangerSet.Suit; // cuff band in the ranger's color
        }
        if (p.y < 0.045f)
        {
            return RangerSet.Silver; // sole
        }
        return RangerSet.White;
    }
}

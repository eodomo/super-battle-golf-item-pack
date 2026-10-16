#if DEVTOOLS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ItemPack.Items;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;

namespace ItemPack;

/// <summary>Dev-only: exports a few of the game's clothing meshes/textures (OBJ + PNG + weight probe) for modelling reference.</summary>
internal static class CosmeticsProbe2
{
    public static void Dump()
    {
        string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "cosmetics_export");
        Directory.CreateDirectory(dir);
        foreach (string key in new[]
        {
            "Assets/Prefabs/Cosmetics/Clothing/Metadata/Ninja.asset",
            "Assets/Prefabs/Cosmetics/Clothing/Metadata/Darkfit.asset",
            "Assets/Prefabs/Cosmetics/Clothing/Metadata/Suit.asset",
            "Assets/Prefabs/Cosmetics/Shoes/Metadata/Ninja.asset",
            "Assets/Prefabs/Cosmetics/Shoes/Metadata/Default Shoes.asset",
            "Assets/Prefabs/Cosmetics/Shoes/Metadata/Worker.asset",
            "Assets/Prefabs/Cosmetics/Hats/Metadata/Default Hat.asset",
            "Assets/Prefabs/Cosmetics/Clubs/Metadata/Katana.asset",
            "Assets/Prefabs/Cosmetics/Clubs/Metadata/Axe.asset",
            "Assets/Prefabs/Cosmetics/Clubs/Metadata/Baseballbat.asset",
        })
        {
            try
            {
                Export(dir, key);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Export {key} failed: {e}");
            }
        }
    }

    private static void Export(string dir, string key)
    {
        PlayerCosmeticsMetadata md = Addressables.LoadAssetAsync<PlayerCosmeticsMetadata>(key).WaitForCompletion();
        string name = md.category + "_" + md.name.Replace(' ', '_');
        GameObject prefab = Addressables.LoadAssetAsync<GameObject>(md.model.RuntimeKey).WaitForCompletion();
        var log = new StringBuilder();
        SkinnedMeshRenderer smr = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
        MeshFilter mf = prefab.GetComponentInChildren<MeshFilter>();
        Mesh mesh = smr != null ? smr.sharedMesh : mf?.sharedMesh;
        Material mat = smr != null ? smr.sharedMaterial : prefab.GetComponentInChildren<Renderer>()?.sharedMaterial;
        log.AppendLine($"{name}: mesh={mesh?.name} verts={mesh?.vertexCount} bones={(smr != null ? smr.bones.Length : 0)} readable={mesh?.isReadable}");
        if (mat != null)
        {
            foreach (string prop in new[] { "_MainTex", "_TintMask" })
            {
                if (mat.HasProperty(prop) && mat.GetTexture(prop) is Texture2D tex)
                {
                    SaveTexture(tex, Path.Combine(dir, $"{name}{prop}.png"));
                    log.AppendLine($"  saved {prop} {tex.width}x{tex.height}");
                }
            }
        }
        if (mesh != null)
        {
            if (smr != null)
            {
                ProbeWeights(mesh, log);
            }
            MeshReadback.Request(mesh, copy =>
            {
                if (copy == null)
                {
                    File.AppendAllText(Path.Combine(dir, "log.txt"), $"{name}: readback failed\n");
                    return;
                }
                WriteObj(Path.Combine(dir, name + ".obj"), copy);
                File.AppendAllText(Path.Combine(dir, "log.txt"), $"{name}: wrote obj {copy.Positions.Length} verts\n");
            });
        }
        File.AppendAllText(Path.Combine(dir, "log.txt"), log.ToString());
    }

    private static void ProbeWeights(Mesh mesh, StringBuilder log)
    {
        try
        {
            GraphicsBuffer buf = mesh.GetBoneWeightBuffer(SkinWeights.FourBones);
            log.AppendLine(buf == null ? "  boneWeightBuffer(4) = null" : $"  boneWeightBuffer(4) count={buf.count} stride={buf.stride}");
            GraphicsBuffer buf1 = mesh.GetBoneWeightBuffer(SkinWeights.Unlimited);
            log.AppendLine(buf1 == null ? "  boneWeightBuffer(All) = null" : $"  boneWeightBuffer(All) count={buf1.count} stride={buf1.stride}");
            log.AppendLine($"  bonesPerVertex? {mesh.GetBonesPerVertex().Length}");
        }
        catch (Exception e)
        {
            log.AppendLine("  weight probe failed: " + e.Message);
        }
    }

    private static void SaveTexture(Texture2D tex, string path)
    {
        RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(tex, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        copy.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(path, copy.EncodeToPNG());
        UnityEngine.Object.Destroy(copy);
    }

    private static void WriteObj(string path, MeshCopy m)
    {
        var sb = new StringBuilder();
        CultureInfo c = CultureInfo.InvariantCulture;
        foreach (Vector3 p in m.Positions)
        {
            sb.AppendLine($"v {(-p.x).ToString("F5", c)} {p.y.ToString("F5", c)} {p.z.ToString("F5", c)}");
        }
        foreach (Vector2 uv in m.Uvs)
        {
            sb.AppendLine($"vt {uv.x.ToString("F5", c)} {uv.y.ToString("F5", c)}");
        }
        foreach (Vector3 n in m.Normals)
        {
            sb.AppendLine($"vn {(-n.x).ToString("F5", c)} {n.y.ToString("F5", c)} {n.z.ToString("F5", c)}");
        }
        foreach (int[] tris in m.Triangles)
        {
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                // Mirroring X flips the winding; swap two corners to keep faces outward.
                int a = tris[i] + 1, b = tris[i + 2] + 1, d = tris[i + 1] + 1;
                sb.AppendLine($"f {a}/{a}/{a} {b}/{b}/{b} {d}/{d}/{d}");
            }
        }
        File.WriteAllText(path, sb.ToString());
    }
}
#endif

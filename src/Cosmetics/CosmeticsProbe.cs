#if DEVTOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace ItemPack;

/// <summary>Dev-only: dumps what the game's cosmetics look like (bones, meshes, metadata) to BepInEx\cosmetics_probe.txt.</summary>
internal static class CosmeticsProbe
{
    public static void Dump()
    {
        var sb = new StringBuilder();
        try
        {
            DumpSwitchers(sb);
        }
        catch (Exception e)
        {
            sb.AppendLine("SWITCHERS FAILED: " + e);
        }
        try
        {
            DumpMetadata(sb);
        }
        catch (Exception e)
        {
            sb.AppendLine("METADATA FAILED: " + e);
        }
        string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "cosmetics_probe.txt");
        File.WriteAllText(path, sb.ToString());
        Plugin.Log.LogInfo("Cosmetics probe written to " + path);
    }

    private static string PathOf(Transform t, Transform stop)
    {
        var parts = new List<string>();
        for (Transform c = t; c != null && c != stop; c = c.parent)
        {
            parts.Add(c.name);
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static void DumpSwitchers(StringBuilder sb)
    {
        PlayerCosmeticsSwitcher[] all = UnityEngine.Object.FindObjectsByType<PlayerCosmeticsSwitcher>(FindObjectsSortMode.None);
        sb.AppendLine($"== {all.Length} PlayerCosmeticsSwitcher(s)");
        int n = 0;
        foreach (PlayerCosmeticsSwitcher s in all)
        {
            sb.AppendLine($"-- switcher #{n++} {PathOf(s.transform, null)} preview={s.isCharacterPreview} skin={s.CurrentSkinColorIndex}");
            DumpSkinned(sb, "body", s.bodyRenderer);
            DumpSkinned(sb, "shoes", s.shoesRenderer);
            DumpSkinned(sb, "head", s.headRenderer);
            sb.AppendLine($"headRoot: {PathOf(s.headRoot, null)} localPos={s.headRoot.localPosition} localRot={s.headRoot.localEulerAngles} lossy={s.headRoot.lossyScale}");
            DumpTree(sb, s.headRoot, 0);
            sb.AppendLine($"keys: head={s.CurrentHeadRuntimeKey?.metadataKey} hat={s.CurrentHatRuntimeKey?.metadataKey} body={s.CurrentBodyRuntimeKey?.metadataKey} shoes={s.CurrentShoesRuntimeKey?.metadataKey}");
            if (n >= 2)
            {
                break;
            }
        }
    }

    private static void DumpSkinned(StringBuilder sb, string label, SkinnedMeshRenderer r)
    {
        if (r == null)
        {
            sb.AppendLine($"{label}: null");
            return;
        }
        Mesh m = r.sharedMesh;
        sb.AppendLine($"{label} renderer {PathOf(r.transform, null)} rootBone={(r.rootBone ? r.rootBone.name : "null")} bones={r.bones.Length} localBounds={r.localBounds.center}/{r.localBounds.size} lossy={r.transform.lossyScale}");
        Matrix4x4[] bp = null;
        if (m != null)
        {
            string bind;
            try
            {
                bp = m.bindposes;
                bind = bp.Length.ToString();
            }
            catch (Exception e)
            {
                bind = "ERR " + e.Message;
            }
            sb.AppendLine($"  mesh {m.name} readable={m.isReadable} verts={m.vertexCount} subs={m.subMeshCount} bindposes={bind} blendShapes={m.blendShapeCount} bounds={m.bounds.center}/{m.bounds.size}");
        }
        foreach (Material mat in r.sharedMaterials)
        {
            DumpMaterial(sb, "  ", mat);
        }
        Transform[] bones = r.bones;
        for (int i = 0; i < bones.Length; i++)
        {
            Transform b = bones[i];
            string bindPos = bp != null && i < bp.Length ? ((Vector3)bp[i].inverse.GetColumn(3)).ToString("F3") : "-";
            sb.AppendLine($"  bone[{i}] {PathOf(b, r.rootBone != null ? r.rootBone.parent : null)} worldRelRenderer={r.transform.InverseTransformPoint(b.position).ToString("F3")} bindWorldPos={bindPos}");
        }
    }

    private static void DumpMaterial(StringBuilder sb, string indent, Material mat)
    {
        if (mat == null)
        {
            sb.AppendLine(indent + "material null");
            return;
        }
        sb.AppendLine($"{indent}material {mat.name} shader={mat.shader.name} keywords={string.Join(",", mat.shaderKeywords)}");
        Shader sh = mat.shader;
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
            string name = sh.GetPropertyName(i);
            var type = sh.GetPropertyType(i);
            string val = "";
            switch (type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    val = mat.GetColor(name).ToString("F3");
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    Texture t = mat.GetTexture(name);
                    val = t == null ? "null" : $"{t.name} {t.width}x{t.height}";
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    val = mat.GetFloat(name).ToString("F3");
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    val = mat.GetVector(name).ToString("F3");
                    break;
            }
            sb.AppendLine($"{indent}  {name} ({type}) = {val}");
        }
    }

    private static void DumpTree(StringBuilder sb, Transform t, int depth)
    {
        string indent = new string(' ', 2 + depth * 2);
        string extra = "";
        foreach (Component c in t.GetComponents<Component>())
        {
            if (c is Transform)
            {
                continue;
            }
            extra += " " + c.GetType().Name;
            if (c is MeshRenderer mr && mr.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
            {
                extra += $"[{mf.sharedMesh.name} {mf.sharedMesh.bounds.size.ToString("F3")} mats={mr.sharedMaterials.Length}]";
            }
            if (c is PlayerCosmeticObject pco)
            {
                extra += $"[disable={pco.modelDisable} allowed={pco.allowedCosmetics} skinTint={pco.requireSkinColorTint}]";
            }
        }
        sb.AppendLine($"{indent}{t.name} lp={t.localPosition.ToString("F3")} lr={t.localEulerAngles.ToString("F1")} ls={t.localScale.ToString("F3")}{extra}");
        for (int i = 0; i < t.childCount; i++)
        {
            DumpTree(sb, t.GetChild(i), depth + 1);
        }
    }

    private static void DumpMetadata(StringBuilder sb)
    {
        sb.AppendLine("== Addressables locators");
        foreach (var loc in Addressables.ResourceLocators)
        {
            sb.AppendLine($"  {loc.LocatorId} keys={new List<object>(loc.Keys).Count}");
        }
        IList<IResourceLocation> locations = Addressables.LoadResourceLocationsAsync("cosmetics-metadata").WaitForCompletion();
        sb.AppendLine($"== {locations.Count} cosmetics-metadata entries");
        var shown = new Dictionary<PlayerCosmeticsMetadata.Category, int>();
        foreach (IResourceLocation l in locations)
        {
            PlayerCosmeticsMetadata md = Addressables.LoadAssetAsync<PlayerCosmeticsMetadata>(l.PrimaryKey).WaitForCompletion();
            if (md == null)
            {
                continue;
            }
            sb.AppendLine($"{md.category} | {md.name} | key={l.PrimaryKey} guid={md.PersistentGuid} cost={md.cost} model={md.model?.RuntimeKey} allowedCosmetics={md.allowedCosmetics} teams={md.allowedTeams} ffa={md.allowedInFreeForAll} variations={md.variations?.Length}");
            if (md.variations != null)
            {
                for (int i = 0; i < md.variations.Length; i++)
                {
                    var v = md.variations[i];
                    sb.AppendLine($"     v{i}: matIdx={v.materialIndex} menu={v.menuColor.ToString("F2")} tint={v.tintColor.ToString("F2")} emis={v.emissiveColor.ToString("F2")} tex={(v.textureOverride != null ? v.textureOverride.RuntimeKey.ToString() : "-")} teams={v.allowedTeams}");
                }
            }
            shown.TryGetValue(md.category, out int count);
            bool interesting = md.category == PlayerCosmeticsMetadata.Category.Hat || md.category == PlayerCosmeticsMetadata.Category.Clothing
                || md.category == PlayerCosmeticsMetadata.Category.Shoes || md.category == PlayerCosmeticsMetadata.Category.Club;
            if (count < 2 && interesting && md.model != null && md.model.RuntimeKeyIsValid())
            {
                shown[md.category] = count + 1;
                try
                {
                    GameObject prefab = Addressables.LoadAssetAsync<GameObject>(md.model.RuntimeKey).WaitForCompletion();
                    if (prefab != null)
                    {
                        sb.AppendLine("     PREFAB " + prefab.name);
                        DumpTree(sb, prefab.transform, 3);
                        if (prefab.TryGetComponent(out SkinnedMeshRenderer smr))
                        {
                            sb.AppendLine($"     skinned: bones={smr.bones.Length} mesh={smr.sharedMesh?.name} verts={smr.sharedMesh?.vertexCount}");
                            DumpMaterial(sb, "     ", smr.sharedMaterial);
                        }
                        foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>())
                        {
                            DumpMaterial(sb, "     ", r.sharedMaterial);
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    sb.AppendLine("     prefab load failed: " + e.Message);
                }
            }
        }
    }
}
#endif

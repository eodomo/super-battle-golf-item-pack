using System.Collections.Generic;
using UnityEngine;

namespace ItemPack;

/// <summary>Short-lived glowing beams (portal shots, bullet tracers).</summary>
internal static class Beams
{
    private const float Duration = 0.18f;
    private static readonly List<(LineRenderer line, float start)> beams = new();
    private static Material material;

    public static void Show(Vector3 from, Vector3 to, Color color, float width)
    {
        material ??= UnlitMaterials.Additive(UnlitMaterials.SoftDot, Color.white);
        var go = new GameObject("ItemBeam");
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        line.startWidth = width;
        line.endWidth = width * 0.6f;
        line.startColor = color;
        line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beams.Add((line, Time.time));
    }

    public static void Tick()
    {
        for (int i = beams.Count - 1; i >= 0; i--)
        {
            (LineRenderer line, float start) = beams[i];
            float age = (Time.time - start) / Duration;
            if (line == null || age >= 1f)
            {
                if (line != null)
                {
                    Object.Destroy(line.gameObject);
                }
                beams.RemoveAt(i);
                continue;
            }
            Color c = line.startColor;
            c.a = 1f - age;
            line.startColor = c;
            line.endColor = c;
        }
    }
}

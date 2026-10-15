using System.Collections.Generic;
using UnityEngine;

namespace ItemPack;

/// <summary>Short-lived glowing beams: portal shots, bullet tracers, vapor trails.</summary>
internal static class Beams
{
    private const float DefaultDuration = 0.18f;

    private struct Beam
    {
        public LineRenderer line;
        public float start;
        public float duration;
        public Color color;
        public float width;
        public float endWidth;
    }

    private static readonly List<Beam> beams = new();
    private static Material material;

    public static void Show(Vector3 from, Vector3 to, Color color, float width) =>
        Show(from, to, color, width, width, DefaultDuration);

    /// <summary>A beam that fades out over <paramref name="duration"/>, its width going from <paramref name="width"/> to <paramref name="endWidth"/>.</summary>
    public static void Show(Vector3 from, Vector3 to, Color color, float width, float endWidth, float duration)
    {
        material ??= UnlitMaterials.Additive(UnlitMaterials.SoftDot, Color.white);
        var go = new GameObject("ItemBeam");
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beams.Add(new Beam { line = line, start = Time.time, duration = Mathf.Max(0.01f, duration), color = color, width = width, endWidth = endWidth });
    }

    public static void Tick()
    {
        for (int i = beams.Count - 1; i >= 0; i--)
        {
            Beam beam = beams[i];
            float age = (Time.time - beam.start) / beam.duration;
            if (beam.line == null || age >= 1f)
            {
                if (beam.line != null)
                {
                    Object.Destroy(beam.line.gameObject);
                }
                beams.RemoveAt(i);
                continue;
            }
            Color c = beam.color;
            c.a *= (1f - age) * (1f - age);
            beam.line.startColor = c;
            beam.line.endColor = c;
            float width = Mathf.Lerp(beam.width, beam.endWidth, age);
            beam.line.startWidth = width;
            beam.line.endWidth = width;
        }
    }
}

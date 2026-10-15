using UnityEngine;

namespace ItemPack.Items;

/// <summary>Particle fire built in code: the dragon's breath and a burning player.</summary>
internal static class FireEffects
{
    private static Material material;

    private static Material Material => material ??= UnlitMaterials.Additive(UnlitMaterials.SoftDot, Color.white);

    /// <summary>A cone of flame shooting along the parent's forward (+Z). Starts stopped.</summary>
    public static ParticleSystem CreateBreath(Transform parent, Vector3 localPosition, float scale)
    {
        ParticleSystem ps = Create("DragonBreath", parent, localPosition);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(16f * scale, 22f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f * scale, 0.9f * scale);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 600;
        var emission = ps.emission;
        emission.rateOverTime = 260f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 9f;
        shape.radius = 0.15f * scale;
        SetFireColors(ps);
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 4.5f)));
        return ps;
    }

    /// <summary>Flames licking up from a player for a few seconds, following them around.</summary>
    public static void Burn(Transform target, float duration)
    {
        ParticleSystem ps = Create("Burning", target, new Vector3(0f, 0.9f, 0f));
        var main = ps.main;
        main.duration = Mathf.Max(0.1f, duration);
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.gravityModifier = -0.6f; // flames rise
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = ps.emission;
        emission.rateOverTime = 70f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.45f;
        SetFireColors(ps);
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));
        ps.Play();
    }

    private static ParticleSystem Create(string name, Transform parent, Vector3 localPosition)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = Material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    private static void SetFireColors(ParticleSystem ps)
    {
        var colors = ps.colorOverLifetime;
        colors.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f),
                new GradientColorKey(new Color(1f, 0.55f, 0.1f), 0.3f),
                new GradientColorKey(new Color(0.85f, 0.15f, 0.05f), 0.7f),
                new GradientColorKey(new Color(0.2f, 0.1f, 0.1f), 1f),
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.08f),
                new GradientAlphaKey(0.7f, 0.6f),
                new GradientAlphaKey(0f, 1f),
            });
        colors.color = gradient;
    }
}

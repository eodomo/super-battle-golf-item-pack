using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Lives on every held lightsaber (anyone's): ignites the blade when it's drawn, hums, flickers, and plays the
/// swing sound and trail whenever the blade moves fast, which the swing animation does on every client, so swings
/// need no network messages of their own.
/// </summary>
internal class SaberBlade : MonoBehaviour
{
    private const float IgniteSeconds = 0.2f;
    private const float SwingSpeed = 13f;     // tip speed relative to the owner's body (m/s) that counts as a swing
    private const float SwingSoundGap = 0.35f;
    private const float TurnRate = 140f;      // degrees per second of turning that earns a soft whoosh
    private const float TurnSoundGap = 0.6f;
    private const float DrawSeconds = 0.8f;   // drawing the saber moves it fast; no swing sounds until it settles

    public Transform Blade;
    public Renderer Core;
    public Light Glow;
    public TrailRenderer Trail;
    public float Length;

    private float ignitedAt;
    private FMOD.Channel hum;
    private Vector3 lastTip;
    private bool hasLastTip;
    private float lastSwingSound = -10f;
    private float lastTurnSound = -10f;
    private float baseIntensity;
    private Transform owner;
    private Vector3 lastOwnerPosition;
    private float lastOwnerYaw;

    /// <summary>World position of the middle of the (fully lit) blade, where a swing connects.</summary>
    public Vector3 BladeMiddle => Blade.position + Blade.up * (Length * 0.5f * transform.lossyScale.y);

    private Vector3 Tip => Blade.position + Blade.up * (Length * transform.lossyScale.y);

    private void OnEnable()
    {
        ignitedAt = Time.time;
        hasLastTip = false;
        if (Glow != null)
        {
            baseIntensity = Glow.intensity;
        }
        Blade.localScale = new Vector3(1f, 0.001f, 1f);
        ItemAudio.Play(SaberSounds.Ignite, ItemAudio.SfxBus, Volume(5f, 35f));
        hum = ItemAudio.Play(SaberSounds.Hum, ItemAudio.SfxBus, 0f);
    }

    private void OnDisable() => StopHum();

    private void OnDestroy() => StopHum();

    private void StopHum()
    {
        ItemAudio.Stop(hum);
        hum = default;
    }

    private float Volume(float full, float silent) =>
        ItemAudio.Attenuate(transform.position, LightsaberItem.SoundVolume.Value, full, silent);

    private void LateUpdate()
    {
        float grow = Mathf.Clamp01((Time.time - ignitedAt) / IgniteSeconds);
        Blade.localScale = new Vector3(1f, Mathf.Max(0.001f, 1f - (1f - grow) * (1f - grow)), 1f);

        // How fast the tip moves relative to the body carrying it, so walking or running in a straight line doesn't
        // count as swinging; only the arm moving does. The owner's turning is tracked separately.
        owner ??= transform.GetComponentInParent<PlayerInfo>()?.transform;
        Vector3 ownerPosition = owner != null ? owner.position : transform.position;
        float ownerYaw = owner != null ? owner.eulerAngles.y : 0f;
        Vector3 tip = Tip;
        float speed = 0f;
        float turn = 0f;
        if (hasLastTip && Time.deltaTime > 0f)
        {
            speed = Vector3.Distance(tip - ownerPosition, lastTip - lastOwnerPosition) / Time.deltaTime;
            turn = Mathf.Abs(Mathf.DeltaAngle(lastOwnerYaw, ownerYaw)) / Time.deltaTime;
        }
        lastTip = tip;
        lastOwnerPosition = ownerPosition;
        lastOwnerYaw = ownerYaw;
        hasLastTip = true;

        if (Glow != null)
        {
            Glow.intensity = baseIntensity * grow * (0.9f + 0.1f * Mathf.PerlinNoise(Time.time * 12f, GetInstanceID() * 0.01f));
        }
        if (Trail != null)
        {
            Trail.emitting = grow >= 1f && speed > SwingSpeed * 0.5f;
        }

        bool visible = Core == null || !Core.forceRenderingOff; // hidden while its owner lies in two pieces
        if (hum.hasHandle())
        {
            ItemAudio.SetVolume(hum, visible ? Volume(3f, 25f) * 0.45f * grow : 0f);
            ItemAudio.SetPitch(hum, 1f + Mathf.Clamp01(speed / 40f) * 0.25f);
        }
        if (visible && Time.time - ignitedAt > DrawSeconds && speed > SwingSpeed && Time.time - lastSwingSound > SwingSoundGap)
        {
            lastSwingSound = Time.time;
            lastTurnSound = Time.time; // the swing already whooshes
            ItemAudio.Play(SaberSounds.Swing, ItemAudio.SfxBus, Volume(4f, 35f) * 0.9f);
        }
        else if (visible && Time.time - ignitedAt > DrawSeconds && turn > TurnRate && Time.time - lastTurnSound > TurnSoundGap && Time.time - lastSwingSound > SwingSoundGap)
        {
            lastTurnSound = Time.time;
            ItemAudio.Play(SaberSounds.Turn, ItemAudio.SfxBus, Volume(3f, 25f) * 0.5f);
        }
    }
}

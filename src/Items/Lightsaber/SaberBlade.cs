using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Lives on every held lightsaber (anyone's): ignites the blade when it's drawn, hums, flickers, plays the swing
/// (see <see cref="PlaySwing"/>), and plays the swing sound and trail whenever the blade moves fast.
/// </summary>
internal class SaberBlade : MonoBehaviour
{
    // The swing: a quick sideways slash. The right arm is turned (after the animator has posed it) so the hand
    // sweeps around the shoulder, and the blade points out along the arm.
    public const float WindSeconds = 0.05f;
    public const float SweepSeconds = 0.13f;
    private const float RecoverSeconds = 0.16f;

    /// <summary>Seconds from the start of a swing to when the blade crosses the front (where hits land).</summary>
    public const float StrikeSeconds = WindSeconds + SweepSeconds * 0.5f;

    // Per style: yaw of the hand around the shoulder (degrees, + is to the right) from/to, and its height
    // (degrees above level) from/to. Forehands go right to left, backhands left to right.
    private static readonly (float yaw0, float yaw1, float pitch0, float pitch1)[] Styles =
    {
        (75f, -85f, 5f, 0f),     // level forehand
        (-80f, 80f, 0f, 8f),     // level backhand
        (70f, -80f, 35f, -15f),  // forehand coming down across the body
        (-75f, 75f, -15f, 30f),  // rising backhand
    };

    public static int StyleCount => Styles.Length;

    /// <summary>Whether a style swings right to left.</summary>
    public static bool IsForehand(int style) => Styles[style % Styles.Length].yaw0 > Styles[style % Styles.Length].yaw1;

    /// <summary>The normal of the plane a style's swing sweeps through (pointing up), in the swinger's frame.</summary>
    public static Vector3 SwingPlaneNormal(int style, Vector3 forward)
    {
        Vector3 start = HandDirection(style, 0f, forward);
        Vector3 end = HandDirection(style, 1f, forward);
        Vector3 normal = Vector3.Cross(start, end).normalized;
        return normal.y < 0f ? -normal : normal;
    }

    private static Vector3 HandDirection(int style, float progress, Vector3 forward)
    {
        var s = Styles[style % Styles.Length];
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float yaw = Mathf.Lerp(s.yaw0, s.yaw1, progress);
        float pitch = Mathf.Lerp(s.pitch0, s.pitch1, progress);
        return Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(-pitch, right) * forward;
    }

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
    private PlayerInfo ownerInfo;
    private bool hiddenForThrow;
    private Vector3 lastOwnerPosition;
    private float lastOwnerYaw;

    private Quaternion restLocalRotation;
    private bool hasRest;
    private float swingStart = -10f;
    private int swingStyle;
    private Transform upperArm;
    private Transform hand;
    private bool searchedArm;

    /// <summary>World position of the middle of the (fully lit) blade, where a swing connects.</summary>
    public Vector3 BladeMiddle => Blade.position + Blade.up * (Length * 0.5f * transform.lossyScale.y);

    private Vector3 Tip => Blade.position + Blade.up * (Length * transform.lossyScale.y);

    /// <summary>Starts a swing in one of the <see cref="StyleCount"/> styles.</summary>
    public void PlaySwing(int style)
    {
        swingStart = Time.time;
        swingStyle = style;
    }

    private void OnEnable()
    {
        if (!hasRest)
        {
            restLocalRotation = transform.localRotation;
            hasRest = true;
        }
        swingStart = -10f;
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
        // Out of its owner's hand while it's thrown (the flying one is a separate copy).
        ownerInfo ??= transform.GetComponentInParent<PlayerInfo>();
        bool thrown = ownerInfo != null && SaberThrows.IsThrown(ownerInfo.netId);
        if (thrown != hiddenForThrow)
        {
            hiddenForThrow = thrown;
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = !thrown;
            }
            if (Glow != null)
            {
                Glow.enabled = !thrown;
            }
        }
        if (thrown)
        {
            ItemAudio.SetVolume(hum, 0f);
            if (Trail != null)
            {
                Trail.emitting = false;
            }
            hasLastTip = false;
            return;
        }

        float grow = Mathf.Clamp01((Time.time - ignitedAt) / IgniteSeconds);
        Blade.localScale = new Vector3(1f, Mathf.Max(0.001f, 1f - (1f - grow) * (1f - grow)), 1f);

        owner ??= ownerInfo != null ? ownerInfo.transform : null;
        PoseSwing();

        // How fast the tip moves relative to the body carrying it, so walking or running in a straight line doesn't
        // count as swinging; only the arm moving does. The owner's turning is tracked separately.
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

    /// <summary>Poses the arm and blade for the swing in progress (runs after the animator, every frame).</summary>
    private void PoseSwing()
    {
        transform.localRotation = restLocalRotation;
        float elapsed = Time.time - swingStart;
        if (owner == null || elapsed < 0f || elapsed > WindSeconds + SweepSeconds + RecoverSeconds)
        {
            return;
        }
        // Snap into the start of the swing, sweep through, then ease back into the held pose.
        float weight, progress;
        if (elapsed < WindSeconds)
        {
            weight = elapsed / WindSeconds;
            progress = 0f;
        }
        else if (elapsed < WindSeconds + SweepSeconds)
        {
            weight = 1f;
            float t = (elapsed - WindSeconds) / SweepSeconds;
            progress = t * t * (3f - 2f * t);
        }
        else
        {
            float t = (elapsed - WindSeconds - SweepSeconds) / RecoverSeconds;
            weight = 1f - t * t * (3f - 2f * t);
            progress = 1f;
        }

        Vector3 forward = Vector3.ProjectOnPlane(owner.forward, Vector3.up).normalized;
        Vector3 direction = HandDirection(swingStyle, progress, forward);

        // Swing the whole arm from the shoulder so the hand points along the swing (the elbow keeps its bend).
        if (FindArm())
        {
            Vector3 arm = hand.position - upperArm.position;
            if (arm.sqrMagnitude > 0.0001f)
            {
                Quaternion turn = Quaternion.FromToRotation(arm, direction * arm.magnitude);
                upperArm.rotation = Quaternion.Slerp(Quaternion.identity, turn, weight) * upperArm.rotation;
            }
        }

        // The blade sticks straight out from the hand, leading a little into the swing and tipped slightly up.
        Vector3 sweep = HandDirection(swingStyle, Mathf.Min(1f, progress + 0.1f), forward) - HandDirection(swingStyle, Mathf.Max(0f, progress - 0.1f), forward);
        Vector3 bladeAxis = (direction + sweep.normalized * 0.25f + Vector3.up * 0.15f).normalized;
        Quaternion pose = Quaternion.FromToRotation(Vector3.up, bladeAxis);
        transform.rotation = Quaternion.Slerp(transform.rotation, pose, weight);
    }

    /// <summary>The bones of the right arm the saber is held in: the hand, and the upper arm two bones above it.</summary>
    private bool FindArm()
    {
        if (searchedArm)
        {
            return upperArm != null;
        }
        searchedArm = true;
        var bones = new System.Collections.Generic.HashSet<Transform>();
        foreach (SkinnedMeshRenderer skinned in owner.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            foreach (Transform bone in skinned.bones)
            {
                if (bone != null)
                {
                    bones.Add(bone);
                }
            }
        }
        for (Transform t = transform.parent; t != null && t != owner; t = t.parent)
        {
            if (bones.Contains(t))
            {
                hand = t;
                break;
            }
        }
        upperArm = hand != null && hand.parent != null ? hand.parent.parent : null;
        if (upperArm == null || upperArm == owner)
        {
            upperArm = null;
            Plugin.Log.LogWarning("Lightsaber: couldn't find the arm bones; swings will only move the blade.");
        }
        else
        {
            Plugin.Log.LogInfo($"Lightsaber arm: {upperArm.name} > {hand.parent.name} > {hand.name}");
        }
        return upperArm != null;
    }
}

using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Lives on every held jellyfish net (anyone's): plays the swing by turning the right arm from the shoulder, after the
/// animator has posed it. The pole points along the arm, so the net sweeps down in front of the player.
/// </summary>
[DefaultExecutionOrder(-10)] // before HeldGrip, which keeps the pole's grip in the fist
internal class JellyfishNetHeld : MonoBehaviour
{
    public const float WindSeconds = 0.12f;
    public const float SweepSeconds = 0.16f;
    public const float RecoverSeconds = 0.25f;
    public const float StrikeSeconds = WindSeconds + SweepSeconds * 0.6f;

    // Arm pitch above level at the start (net held up behind) and the end (net down in front).
    private const float PitchStart = 75f;
    private const float PitchEnd = -20f;

    /// <summary>The empty net, hidden while the hanging one (with someone in it) is out.</summary>
    public NetCloth IdleNet;

    private static Material cordMaterial;

    internal static Material CordMaterial => cordMaterial != null ? cordMaterial : cordMaterial = UnlitMaterials.Opaque(null, new Color(0.96f, 0.96f, 0.93f));

    private void Awake()
    {
        IdleNet = new NetCloth(0.5f, 0.24f, 0.2f, 0.012f, CordMaterial);
        gameObject.AddComponent<NetClothDriver>().Held = this;
    }

    private void OnDestroy() => IdleNet?.Destroy();

    /// <summary>How far the resting pole leans forward from upright (degrees).</summary>
    public float Lean = 25f;

    /// <summary>How far the resting pole leans out to the player's right (degrees), so the swing comes across the body.</summary>
    public float SideLean = 55f;

    // The swing's heading off straight ahead (degrees, + = to the right): it starts out right and finishes a little left.
    private const float YawStart = 50f;
    private const float YawEnd = -10f;

    private Quaternion restLocalRotation;
    private bool hasRest;
    private float swingStart = -10f;
    private Transform owner;
    private Transform upperArm;
    private Transform hand;
    private Transform wrist;
    private Transform[,] fingers;
    private bool searchedArm;

    /// <summary>Where the net's mouth is: the middle of the ring, in world space.</summary>
    public Vector3 RingCenter => transform.TransformPoint(new Vector3(0f, 1.2f, 0f));

    /// <summary>The butt of the pole, in world space.</summary>
    public Vector3 PoleBase => transform.position;

    /// <summary>After everything has posed the net: runs the empty net's cloth (the ring's frame is the model's).</summary>
    public void StepCloth()
    {
        if (IdleNet == null || !IdleNet.Root.activeSelf)
        {
            return;
        }
        // The ring spans the model's X and Y; its bag opens along -Z.
        IdleNet.Step(RingCenter, -transform.right, transform.up, Vector3.Lerp(-transform.forward, Vector3.down, 0.6f).normalized, Time.deltaTime, null, null);
        IdleNet.Render();
    }

    public void PlaySwing() => swingStart = Time.time;

    private void OnEnable()
    {
        if (!hasRest)
        {
            restLocalRotation = transform.localRotation;
            hasRest = true;
        }
        swingStart = -10f;
    }

    // The empty net isn't under the model (its strings are in world space), so it has to be put away with it.
    private void OnDisable()
    {
        if (IdleNet != null)
        {
            IdleNet.SetActive(false);
            IdleNet.Reset();
        }
    }

    private void LateUpdate()
    {
        PoseNet();
        if (owner != null && FindArm())
        {
            GripPole();
        }
    }

    /// <summary>The net's pose for this frame: resting, carrying someone, or partway through a swing.</summary>
    private void PoseNet()
    {
        transform.localRotation = restLocalRotation;
        owner ??= GetComponentInParent<PlayerInfo>()?.transform;
        if (owner != null)
        {
            // At rest the borrowed animation holds the pole leaning back over the shoulder; hold it out to the right and a
            // little forward instead, the ring's mouth (model +Z) turned up as far as the pole allows.
            Vector3 front = Vector3.ProjectOnPlane(owner.forward, Vector3.up).normalized;
            Quaternion lean = Quaternion.AngleAxis(-SideLean, front) * Quaternion.AngleAxis(Lean, Vector3.Cross(Vector3.up, front));
            transform.rotation = Pose(lean * Vector3.up, Vector3.up);
        }
        bool carrying = owner != null && NetCatches.IsCarrier(owner.GetComponent<PlayerInfo>().netId);
        if (IdleNet != null && IdleNet.Root.activeSelf == carrying)
        {
            IdleNet.SetActive(!carrying);
            IdleNet.Reset();
        }
        if (carrying)
        {
            // Carrying someone: arm out and the pole reaching well ahead and up, so they hang clear of the carrier.
            Vector3 ahead = Vector3.ProjectOnPlane(owner.forward, Vector3.up).normalized;
            if (FindArm())
            {
                AimArm((ahead + Vector3.up * 0.6f).normalized, 1f);
            }
            transform.rotation = Pose((ahead * 0.8f + Vector3.up * 0.6f).normalized, Vector3.up);
            return;
        }
        float elapsed = Time.time - swingStart;
        if (elapsed < 0f || elapsed > WindSeconds + SweepSeconds + RecoverSeconds)
        {
            return;
        }
        if (owner == null || !FindArm())
        {
            return;
        }
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
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        // A diagonal sweep: from up high on the right, down and across in front.
        Vector3 SweepAt(float p) => Quaternion.AngleAxis(Mathf.Lerp(YawStart, YawEnd, p), Vector3.up)
            * (Quaternion.AngleAxis(-Mathf.Lerp(PitchStart, PitchEnd, p), right) * forward);
        Vector3 direction = SweepAt(progress);

        AimArm(direction, weight);
        // The pole carries on along the arm (a little above it, so the ring leads), the mouth scooping the way it moves.
        Vector3 axis = (direction + Vector3.up * 0.25f).normalized;
        Vector3 motion = SweepAt(Mathf.Min(progress + 0.05f, 1f)) - SweepAt(Mathf.Max(progress - 0.05f, 0f));
        transform.rotation = Quaternion.Slerp(transform.rotation, Pose(axis, motion), weight);
    }

    /// <summary>The model turned so the pole (model +Y) lies along <paramref name="pole"/> and the ring's mouth (+Z) faces as near <paramref name="mouth"/> as it can.</summary>
    private static Quaternion Pose(Vector3 pole, Vector3 mouth)
    {
        Vector3 face = Vector3.ProjectOnPlane(mouth, pole);
        if (face.sqrMagnitude < 1e-6f)
        {
            return Quaternion.FromToRotation(Vector3.up, pole);
        }
        return Quaternion.LookRotation(face, pole);
    }

    /// <summary>Turns the arm from the shoulder so the hand lies along <paramref name="direction"/> (blended by weight).</summary>
    private void AimArm(Vector3 direction, float weight)
    {
        Vector3 arm = hand.position - upperArm.position;
        if (arm.sqrMagnitude > 0.0001f)
        {
            Quaternion turn = Quaternion.FromToRotation(arm, direction * arm.magnitude);
            upperArm.rotation = Quaternion.Slerp(Quaternion.identity, turn, weight) * upperArm.rotation;
        }
    }

    /// <summary>The hand bone the net is held in (the skinned bone above it) and the shoulder above that.</summary>
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
        // Turn from the shoulder (the hand's own bones go Tool1 -> wrist -> forearm -> shoulder).
        upperArm = null;
        for (Transform t = hand != null ? hand.parent : null; t != null && t != owner; t = t.parent)
        {
            if (t.name.EndsWith("Shoulder"))
            {
                upperArm = t;
                break;
            }
        }
        upperArm ??= hand != null && hand.parent != null ? hand.parent.parent : null;
        if (upperArm == null || upperArm == owner)
        {
            upperArm = null;
        }
        // The wrist and its four fingers (three joints each), for closing the hand round the pole.
        for (Transform t = hand; t != null && t != owner; t = t.parent)
        {
            if (t.name == "R_Wrist")
            {
                wrist = t;
                break;
            }
        }
        if (wrist != null)
        {
            var named = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (Transform t in wrist.GetComponentsInChildren<Transform>(true))
            {
                named[t.name] = t;
            }
            string[] names = { "Index", "Middle", "Ring", "Pinky" };
            fingers = new Transform[names.Length, 4];
            for (int f = 0; f < names.Length; f++)
            {
                for (int j = 0; j < 4; j++)
                {
                    named.TryGetValue("R_" + names[f] + (j + 1), out fingers[f, j]);
                }
            }
        }
        return upperArm != null;
    }

    /// <summary>
    /// Closes the hand round the pole: turns the wrist so the knuckles run along it, then curls each finger the way it
    /// already bends. The borrowed animation's hand is half open (it was made for holding a ball), so on its own the pole
    /// just lies across the palm.
    /// </summary>
    private void GripPole()
    {
        if (wrist == null || fingers == null || fingers[0, 0] == null || fingers[3, 0] == null || fingers[1, 2] == null)
        {
            return;
        }
        Quaternion net = transform.rotation; // the net hangs off the wrist: keep it where it was posed
        Vector3 pole = transform.up;
        Vector3 knuckles = fingers[0, 0].position - fingers[3, 0].position; // pinky to index: out of the top of the fist
        if (knuckles.sqrMagnitude > 1e-8f)
        {
            wrist.rotation = Quaternion.FromToRotation(knuckles, pole) * wrist.rotation;
        }
        // Which way along the knuckles is "curl": the way the middle finger is already bent.
        Vector3 bend = Vector3.Cross(fingers[1, 1].position - fingers[1, 0].position, fingers[1, 2].position - fingers[1, 1].position);
        Vector3 axis = (fingers[0, 0].position - fingers[3, 0].position).normalized;
        if (Vector3.Dot(bend, axis) < 0f)
        {
            axis = -axis;
        }
        for (int f = 0; f < fingers.GetLength(0); f++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (fingers[f, j] != null)
                {
                    fingers[f, j].rotation = Quaternion.AngleAxis(CurlDegrees[j], axis) * fingers[f, j].rotation;
                }
            }
        }
        transform.rotation = net;
    }

    // Extra bend at each finger joint (knuckle first) to close the fist.
    private static readonly float[] CurlDegrees = { 55f, 70f, 45f };
}

/// <summary>Runs the net's cloth after the animator, the swing pose and the hand grip have all moved it.</summary>
[DefaultExecutionOrder(300)]
internal class NetClothDriver : MonoBehaviour
{
    public JellyfishNetHeld Held;

    private void LateUpdate() => Held.StepCloth();
}

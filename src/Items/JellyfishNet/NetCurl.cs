using System.Collections.Generic;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// On a player caught in a net (every client): after the animator, tucks their skeleton into a ball (back bent over, knees
/// to the chest, arms hugging them), has them kick and shove, and wraps the net tight around them.
/// </summary>
[DefaultExecutionOrder(200)]
internal class NetCurl : MonoBehaviour
{
    private const float CurlSeconds = 0.25f;

    private readonly Dictionary<string, Transform> bones = new();
    private NetCloth cloth;
    // Where the net is held off the body: (bone, radius) of the balls the strings drape over.
    private static readonly (string bone, float radius)[] Shape =
    {
        ("Root", 0.34f), ("Spine2", 0.34f), ("Head", 0.4f), ("L_Knee", 0.21f), ("R_Knee", 0.21f), ("L_Ankle", 0.18f), ("R_Ankle", 0.18f), ("L_Toe", 0.17f), ("R_Toe", 0.17f),
        ("L_Shoulder", 0.17f), ("R_Shoulder", 0.17f), ("L_Wrist", 0.13f), ("R_Wrist", 0.13f),
    };

    private readonly Vector3[] centers = new Vector3[Shape.Length];
    private readonly float[] radii = new float[Shape.Length];

    private const float MaxBallRadius = 0.9f;
    private const float HeadReach = 0.28f; // from the head bone to the middle of the head
    private const float PoleClearance = 0.07f; // how close the strings may come to the pole's middle

    // How far the back and the neck bend forward into the ball (degrees).
    public float SpineBend = 90f;
    public float NeckBend = 40f;
    // The net's own size against the body's (below 1 = tighter than them), and how fast they kick and shove (radians/s).
    public float Tightness = 0.6f;
    public float StruggleSpeed = 9f;
    private Vector3 bodyOffset = Vector3.up * 0.5f;
    private float ballRadius = 0.6f;

    /// <summary>The middle of the curled body, from where the bones ended up last frame (it's not where the feet are).</summary>
    public Vector3 BodyCenter => transform.position + bodyOffset;

    /// <summary>The radius of the ball round <see cref="BodyCenter"/> that holds the whole curled body.</summary>
    public float BallRadius => ballRadius;
    private float start;
    private PlayerInfo player;

    private void OnEnable()
    {
        start = Time.time;
        player = GetComponent<PlayerInfo>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            bones[t.name] = t;
        }
        cloth = new NetCloth(1.5f, 0.7f, 0.2f, 0.016f, JellyfishNetHeld.CordMaterial);
    }

    private void OnDisable()
    {
        cloth?.Destroy();
        cloth = null;
    }

    private void LateUpdate()
    {
        float weight = Mathf.SmoothStep(0f, 1f, (Time.time - start) / CurlSeconds);
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        if (bones.TryGetValue("Spine1", out Transform spine))
        {
            spine.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.AngleAxis(SpineBend, right), weight) * spine.rotation;
        }
        // Head tucked down too: the heads are big, and it would otherwise stick up out of the net's mouth.
        if (bones.TryGetValue("Neck", out Transform neckBone))
        {
            neckBone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.AngleAxis(NeckBend, right), weight) * neckBone.rotation;
        }
        // Struggling: each leg kicks and each arm shoves out in turn, pushing bulges into the net.
        float clock = (Time.time - start) * StruggleSpeed;
        foreach (string side in new[] { "L", "R" })
        {
            Vector3 outward = side == "L" ? -right : right;
            float kick = Mathf.Max(0f, Mathf.Sin(clock + (side == "L" ? 0f : 2.6f)));
            float shove = Mathf.Max(0f, Mathf.Sin(clock * 0.8f + (side == "L" ? 1.3f : 4.2f)));
            Aim(side + "_Thigh", side + "_Knee", forward * 0.85f + Vector3.up * 0.25f + outward * (0.15f + 0.45f * kick), weight);
            Aim(side + "_Knee", side + "_Ankle", -forward * 0.5f - Vector3.up * 0.85f + forward * (0.8f * kick), weight);
            Aim(side + "_Shoulder", side + "_Forearm", forward * 0.5f - Vector3.up * 0.8f - outward * 0.1f + outward * (0.7f * shove), weight);
            Aim(side + "_Forearm", side + "_Wrist", forward * 0.3f + Vector3.up * 0.2f - outward * 0.8f + outward * (1.4f * shove), weight);
        }
        Vector3[] balls = Balls();
        Measure(balls);
        bool hanging = NetCatches.TryGetRing(player.netId, out Vector3 ring, out JellyfishNetHeld net);
        if (cloth != null && cloth.Root.activeSelf != hanging)
        {
            // No net in the carrier's hand (put away for a moment before the host lets go): don't leave it hanging in the air.
            cloth.SetActive(hanging);
            cloth.Reset();
        }
        if (cloth != null && hanging)
        {
            // The net hangs from the ring in the carrier's hand, pulled in tight round the curled player: it's drawn smaller
            // than they are, so their head, knees and elbows press bulges into it.
            // The pole comes up from the carrier's hand under the ring, right where the back of the bag hangs: keep the
            // strings off it (stopping short of the ring, which the rim is pinned to).
            cloth.SetObstacle(net.PoleBase, Vector3.MoveTowards(ring, net.PoleBase, 0.24f), PoleClearance);
            cloth.Wrap(ring, -net.transform.right, net.transform.up, BodyCenter, Mathf.Max(0.35f, ballRadius * Tightness), Time.deltaTime, balls, radii);
            cloth.Render();
        }
    }

    /// <summary>The middle of the body's balls and how far the furthest one reaches from it.</summary>
    private void Measure(Vector3[] balls)
    {
        // Weighted by each ball's volume, so the middle sits in the bulk of them (head and body), not between the feet.
        Vector3 sum = Vector3.zero;
        float mass = 0f;
        for (int i = 0; i < balls.Length; i++)
        {
            float m = radii[i] * radii[i] * radii[i];
            sum += balls[i] * m;
            mass += m;
        }
        if (mass <= 0f)
        {
            return;
        }
        Vector3 middle = sum / mass;
        float reach = 0f;
        for (int i = 0; i < balls.Length; i++)
        {
            if (radii[i] > 0f)
            {
                reach = Mathf.Max(reach, Vector3.Distance(balls[i], middle) + radii[i]);
            }
        }
        // Eased, so the kicking doesn't make the whole bag twitch.
        bodyOffset = Vector3.Lerp(bodyOffset, middle - transform.position, 0.15f);
        ballRadius = Mathf.Lerp(ballRadius, Mathf.Clamp(reach, 0.45f, MaxBallRadius), 0.15f);
    }

    private Vector3[] Balls()
    {
        for (int i = 0; i < Shape.Length; i++)
        {
            radii[i] = bones.TryGetValue(Shape[i].bone, out Transform t) ? Shape[i].radius : 0f;
            centers[i] = t != null ? t.position : Vector3.zero;
            // The head bone is at the neck joint; the (big) head itself carries on past it, away from the neck.
            if (t != null && Shape[i].bone == "Head" && bones.TryGetValue("Neck", out Transform neck))
            {
                Vector3 along = t.position - neck.position;
                if (along.sqrMagnitude > 1e-6f)
                {
                    centers[i] += along.normalized * HeadReach;
                }
            }
        }
        return centers;
    }

    private void Aim(string bone, string child, Vector3 direction, float weight)
    {
        if (!bones.TryGetValue(bone, out Transform b) || !bones.TryGetValue(child, out Transform c))
        {
            return;
        }
        Vector3 current = c.position - b.position;
        if (current.sqrMagnitude < 1e-6f)
        {
            return;
        }
        Quaternion turn = Quaternion.FromToRotation(current, direction.normalized * current.magnitude);
        b.rotation = Quaternion.Slerp(Quaternion.identity, turn, weight) * b.rotation;
    }
}

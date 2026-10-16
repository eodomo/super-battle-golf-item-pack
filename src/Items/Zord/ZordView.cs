using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Turns a golf cart into a Megazord: hides the cart's own model and puts a posable giant robot on it (the
/// cart's physics, seats and networking are all still the real cart's). It stomps as the cart drives and strikes a pose when
/// asked; riders sit in its cockpit, out of sight.
/// </summary>
internal sealed class ZordView : MonoBehaviour
{
    private const float Deg = Mathf.Rad2Deg;
    private const float CryTime = 3.5f;
    private const bool MoveHitBoxes = false;

    public GolfCartInfo Cart { get; private set; }
    public ZordRig Rig { get; private set; }

    private float scale;
    private float phase;
    private float groundOffset = 0.4f;
    private float cryUntil = -1f;
    private Vector3 lastPosition, velocity;
    private bool hasLast;
    private int lastStep;
    private readonly List<Renderer> cartRenderers = new();
    private readonly List<Light> cartLights = new();
    private Collider[] passengerColliders;
    private Vector3[] passengerColliderLocal;
    private readonly Vector3[] seatShift = new Vector3[2];

    public static ZordView Create(GolfCartInfo cart)
    {
        if (!ZordParts.EnsureReady(ZordItem.Instance.AssetDir))
        {
            return null;
        }
        var view = cart.gameObject.AddComponent<ZordView>();
        view.Init(cart);
        return view;
    }

    private void Init(GolfCartInfo cart)
    {
        Cart = cart;
        scale = Mathf.Max(0.3f, ZordItem.ModelScale.Value);
        Rig = ZordRig.Build(cart.transform, scale);

        foreach (Renderer r in cart.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.transform.IsChildOf(Rig.Root))
            {
                cartRenderers.Add(r);
            }
        }
        cartLights.AddRange(cart.GetComponentsInChildren<Light>(true));

        passengerColliders = cart.passengerColliders;
        if (MoveHitBoxes && passengerColliders != null)
        {
            passengerColliderLocal = new Vector3[passengerColliders.Length];
            for (int i = 0; i < passengerColliders.Length; i++)
            {
                if (passengerColliders[i] != null)
                {
                    passengerColliderLocal[i] = cart.transform.InverseTransformPoint(passengerColliders[i].transform.position);
                }
            }
        }
        lastPosition = cart.transform.position;
        Plugin.Log.LogInfo($"Zord cart ready (scale {scale}, {cartRenderers.Count} cart renderers hidden, {(passengerColliders?.Length ?? 0)} passenger colliders).");
    }

    public void StartCry()
    {
        cryUntil = Time.time + CryTime;
        ZordSounds.Play(ZordSounds.Cry, Rig.Head.position, 1f);
    }

    /// <summary>Where a rider's hips go for a seat, in the world.</summary>
    public Vector3 SeatWorld(int seat) => Rig.Seats[Mathf.Clamp(seat, 0, 1)].position;

    /// <summary>Remembers how far a rider was moved (so the cart's hit boxes for passengers can follow).</summary>
    public void NoteSeatShift(int seat, Vector3 worldShift)
    {
        if (seat >= 0 && seat < seatShift.Length)
        {
            seatShift[seat] = worldShift;
        }
    }

    private void Update()
    {
        if (Cart == null)
        {
            return;
        }
        float dt = Time.deltaTime;
        if (dt <= 0f)
        {
            return;
        }

        Transform cart = Cart.transform;
        Vector3 position = cart.position;
        Vector3 v = hasLast ? (position - lastPosition) / dt : Vector3.zero;
        hasLast = true;
        lastPosition = position;
        if (v.sqrMagnitude > 60f * 60f)
        {
            v = Vector3.zero; // a teleport (portal, respawn), not running
        }
        velocity = Vector3.Lerp(velocity, v, 1f - Mathf.Exp(-10f * dt));
        Vector3 flat = Vector3.ProjectOnPlane(velocity, cart.up);
        float forward = Vector3.Dot(flat, cart.forward);
        float speed = flat.magnitude * (forward < -0.5f ? -1f : 1f);

        UpdateGroundOffset();
        Rig.Root.localPosition = new Vector3(0f, -groundOffset, 0f);
        Rig.Root.localRotation = Quaternion.identity;
        Pose(dt, speed / scale);
        if (!strikeDone && SwingElapsed >= StrikeAt)
        {
            strikeDone = true;
            if (DrivenByLocalPlayer())
            {
                Strike();
            }
        }
    }

    private float tiltedFor;
    private float prevSpeed;

    private void FixedUpdate()
    {
        if (Cart == null || !Cart.TryGetComponent(out Rigidbody body) || body.isKinematic)
        {
            return;
        }
        Transform t = Cart.transform;

        // Weighty movement: a lower top speed, slow build-up and a slow turning rate (whoever simulates the cart does this).
        if (Cart.TryGetComponent(out GolfCartMovement movement) && movement.authority)
        {
            Vector3 v = body.linearVelocity;
            Vector3 flat = Vector3.ProjectOnPlane(v, t.up);
            float speed = flat.magnitude;
            if (speed > prevSpeed)
            {
                float cap = Mathf.Min(ZordItem.MaxSpeed.Value, prevSpeed + ZordItem.Acceleration.Value * Time.fixedDeltaTime);
                if (speed > cap)
                {
                    body.linearVelocity = v - flat + flat * (cap / speed);
                    speed = cap;
                }
            }
            prevSpeed = speed;
            float maxTurn = ZordItem.TurnRate.Value * Mathf.Deg2Rad;
            Vector3 w = body.angularVelocity;
            float yaw = Vector3.Dot(w, t.up);
            if (Mathf.Abs(yaw) > maxTurn)
            {
                body.angularVelocity = w - t.up * yaw + t.up * (Mathf.Sign(yaw) * maxTurn);
            }
        }

        // A cart that rolled over (a crash) would leave the Megazord lying on its side: the host stands it back up.
        if (!NetworkServer.active)
        {
            return;
        }
        if (Vector3.Dot(t.up, Vector3.up) >= 0.5f)
        {
            tiltedFor = 0f;
            return;
        }
        tiltedFor += Time.fixedDeltaTime;
        if (tiltedFor > 1.2f)
        {
            tiltedFor = 0f;
            body.angularVelocity = Vector3.zero;
            body.linearVelocity = Vector3.zero;
            body.position += Vector3.up * 1.5f;
            body.rotation = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
        }
    }

    // ---- the sword swing ----

    private const float SwingTime = 1.0f, StrikeAt = 0.38f;
    private float swingStart = -10f;
    private const float SwingSpeed = 1f;
    private float SwingElapsed => (Time.time - swingStart) * SwingSpeed;
    private bool strikeDone = true;
    private bool whooshed;

    public void StartSwing()
    {
        swingStart = Time.time;
        strikeDone = false;
        whooshed = false;
    }

    /// <summary>
    /// A horizontal slash: the sword comes up level and points out along the arm, the arm swings out to the right with a
    /// quick windup, then sweeps across the front to the left in one fast stroke. Outputs the torso twist and the arm's
    /// yaw (degrees), how level the sword lies (0..1) and how high the arm is raised (degrees).
    /// </summary>
    private void SwingPose(out float twist, out float yaw, out float flat, out float raise)
    {
        float t = SwingElapsed;
        twist = yaw = flat = raise = 0f;
        if (t < 0f || t > SwingTime)
        {
            return;
        }
        if (t < 0.22f)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / 0.22f);
            twist = Mathf.Lerp(0f, 30f, k);
            yaw = Mathf.Lerp(0f, 105f, k);
            flat = k;
            raise = Mathf.Lerp(0f, -25f, k);
        }
        else if (t < 0.3f)
        {
            twist = 30f;
            yaw = 105f;
            flat = 1f;
            raise = -25f;
        }
        else if (t < 0.48f)
        {
            float k = (t - 0.3f) / 0.18f;
            k = k * k * (3f - 2f * k) * 0.5f + k * k * 0.5f;
            twist = Mathf.Lerp(30f, -35f, k);
            yaw = Mathf.Lerp(105f, -80f, k);
            flat = 1f;
            raise = -25f;
        }
        else
        {
            float k = Mathf.SmoothStep(0f, 1f, (t - 0.48f) / (SwingTime - 0.48f));
            twist = Mathf.Lerp(-35f, 0f, k);
            yaw = Mathf.Lerp(-80f, 0f, k);
            flat = 1f - k;
            raise = Mathf.Lerp(-25f, 0f, k);
        }
    }

    private bool DrivenByLocalPlayer()
    {
        PlayerInfo local = GameManager.LocalPlayerInfo;
        if (local == null)
        {
            return false;
        }
        GolfCartSeat seat = local.ActiveGolfCartSeat;
        return seat.IsValid() && seat.golfCart == Cart && seat.IsDriver();
    }

    private static readonly Collider[] overlap = new Collider[64];
    private static readonly HashSet<Hittable> struck = new();

    /// <summary>The blow lands: players in front are knocked out and the driver's own ball is knocked away.</summary>
    private void Strike()
    {
        PlayerInfo me = GameManager.LocalPlayerInfo;
        Transform cart = Cart.transform;
        Vector3 forward = Vector3.ProjectOnPlane(cart.forward, Vector3.up).normalized;
        Vector3 center = Rig.Root.position + forward * (2.2f * scale) + Vector3.up * (0.7f * scale);
        float radius = 2.4f * scale;
        bool hitSomething = false;

        LayerSettings layers = GameManager.LayerSettings;
        int count = Physics.OverlapSphereNonAlloc(center, radius, overlap, layers.PlayersMask | layers.GolfCartsMask, QueryTriggerInteraction.Ignore);
        struck.Clear();
        for (int i = 0; i < count; i++)
        {
            Hittable hittable = overlap[i].GetComponentInParent<Hittable>();
            if (hittable == null || !struck.Add(hittable) || hittable.gameObject == Cart.gameObject)
            {
                continue;
            }
            Entity entity = hittable.AsEntity;
            if (!entity.IsPlayer && !entity.IsGolfCart)
            {
                continue;
            }
            if (entity.IsPlayer ? (entity.PlayerInfo == me || entity.PlayerInfo.ActiveGolfCartSeat.IsValid()) : entity.AsGolfCart.passengers.Contains(me))
            {
                continue; // ourselves, our passenger, someone riding a cart (their cart takes the hit), or our own cart
            }
            Vector3 point = overlap[i].ClosestPoint(center);
            Vector3 direction = Vector3.ProjectOnPlane(hittable.transform.position - cart.position, Vector3.up);
            direction = direction.sqrMagnitude > 0.01f ? direction.normalized : forward;
            hittable.HitWithItem(ItemType.DuelingPistol, me.Inventory.IncrementAndGetCurrentItemUseId(ItemType.DuelingPistol),
                hittable.transform.InverseTransformPoint(point), direction, hittable.transform.InverseTransformPoint(center),
                hittable.ItemSettings.DuelingPistolMinKnockbackDistance * 2f, me.Inventory, isReflected: false, isInSpecialState: false,
                canHitWithNoUser: true, hitTimestamp: NetworkTime.time);
            hitSomething = true;
        }

        // Only the driver's own ball: other players' balls are left alone.
        GolfBall ball = me.AsGolfer.OwnBall;
        if (ball != null && ball.TryGetComponent(out Hittable ballHittable)
            && (ball.transform.position - center).sqrMagnitude < (radius + 0.6f) * (radius + 0.6f))
        {
            Vector3 direction = (forward + Vector3.up * 0.5f).normalized;
            ballHittable.HitWithGolfSwing(ballHittable.transform.InverseTransformPoint(ball.transform.position),
                ballHittable.transform.InverseTransformPoint(center), direction, false, ZordItem.BallPower.Value, 0f,
                SwingType.Regular, me.AsGolfer, null);
            VfxManager.PlayPooledVfxLocalOnly(VfxType.BasicClubHit, ball.transform.position, Quaternion.LookRotation(direction));
            hitSomething = true;
        }

        Plugin.Log.LogInfo($"Zord strike: hit={hitSomething}, own ball {(ball != null ? Vector3.Distance(ball.transform.position, center).ToString("F1") + " m away" : "none")}.");
        CameraModuleController.Shake(GameManager.CameraGameplaySettings.JumboBurgerGiantFormLandingScreenshakeSettings, center, hitSomething ? 1f : 0.5f);
        ZordSounds.Play(ZordSounds.Step, center, 1f);
    }

    private void LateUpdate()
    {
        if (Cart == null)
        {
            return;
        }
        // The game turns parts of the cart on and off (hiding, team colors); keep the cart itself invisible.
        foreach (Renderer r in cartRenderers)
        {
            if (r != null && r.enabled)
            {
                r.enabled = false;
            }
        }
        foreach (Light l in cartLights)
        {
            if (l != null && l.enabled)
            {
                l.enabled = false;
            }
        }
        if (Cart.engineSoundInstance.isValid())
        {
            Cart.engineSoundInstance.setVolume(0f);
        }
        // The hit boxes for passengers could follow the riders up into the cockpit, but the orbit camera collides with them.
        if (MoveHitBoxes && passengerColliders != null)
        {
            for (int i = 0; i < passengerColliders.Length && i < 2; i++)
            {
                if (passengerColliders[i] != null)
                {
                    passengerColliders[i].transform.position = Cart.transform.TransformPoint(passengerColliderLocal[i]) + seatShift[i];
                }
            }
        }
    }

    /// <summary>The cart origin sits some height above the ground; find it so the feet are on the floor.</summary>
    private void UpdateGroundOffset()
    {
        Transform cart = Cart.transform;
        if (Physics.Raycast(cart.position + cart.up * 0.3f, -cart.up, out RaycastHit hit, 1.5f, WorldMask.Solid, QueryTriggerInteraction.Ignore))
        {
            float measured = Vector3.Distance(cart.position, hit.point) * Mathf.Sign(Vector3.Dot(cart.position - hit.point, cart.up));
            groundOffset = Mathf.Lerp(groundOffset, Mathf.Clamp(measured, 0f, 1f), 0.2f);
        }
    }

    // ---- posing ----

    private void Pose(float dt, float speedModel)
    {
        float moving = Mathf.Clamp01(Mathf.Abs(speedModel) / 1.2f);
        float absSpeed = Mathf.Abs(speedModel);
        SwingPose(out float swingTwist, out float swingYaw, out float swingFlat, out float swingRaise);

        // Stride clock: a heavy, slow stomp that quickens with speed.
        float freq = Mathf.Clamp(0.8f + 0.12f * absSpeed, 0.8f, 2.2f);
        phase += Mathf.Sign(speedModel == 0f ? 1f : speedModel) * 2f * Mathf.PI * freq * dt * Mathf.Max(0.15f, moving);

        float stride = Mathf.Clamp(absSpeed / (2f * Mathf.PI * freq * 0.6f), 0f, 0.5f);
        float lift = Mathf.Clamp(0.04f + 0.03f * absSpeed, 0f, 0.28f) * moving;
        float drop = 0.06f + Mathf.Clamp(0.02f + 0.01f * absSpeed, 0f, 0.1f) * moving;
        float bob = 0.03f * moving * Mathf.Cos(2f * phase);
        float pelvisY = ZordRig.HipHeight - drop + bob - 0.25f * swingFlat; // crouches into a low swing
        Rig.Pelvis.localPosition = new Vector3(0f, pelvisY, 0f);

        // The pose: chest out, head back, arms flung wide and up, with a shudder.
        float cryT = cryUntil - Time.time;
        float pose = cryT > 0f ? Mathf.Sin(Mathf.Clamp01(1f - cryT / CryTime) * Mathf.PI) : 0f;
        pose = Mathf.Min(1f, pose * 1.6f);
        float shake = pose * 1.0f * Mathf.Sin(Time.time * 50f);

        if (!whooshed && SwingElapsed > 0.3f && SwingElapsed < 0.8f)
        {
            whooshed = true;
            ZordSounds.Play(ZordSounds.Whoosh, Rig.Root.position + Rig.Root.forward * 2f, 1f);
        }
        float lean = Mathf.Clamp(1f + 0.7f * absSpeed, 0f, 8f);
        Rig.Torso.localRotation = Quaternion.Euler(lean + 22f * swingFlat - 8f * pose + 1.5f * moving * Mathf.Sin(2f * phase), 4f * moving * Mathf.Sin(phase) + swingTwist, shake);
        Rig.Head.localRotation = Quaternion.Euler(-lean * 0.7f - 15f * pose, 0f, 0f);

        // Legs: two-bone IK so the feet land on the ground under it.
        for (int side = 0; side < 2; side++)
        {
            float legPhase = phase + (side == 0 ? 0f : Mathf.PI);
            float z = stride * Mathf.Cos(legPhase);
            float y = ZordRig.AnkleHeight + lift * Mathf.Max(0f, -Mathf.Sin(legPhase));
            Solve(ZordRig.Thigh, ZordRig.Shin, new Vector2(z, y - pelvisY), out float a1, out float a2);
            Rig.Thighs[side].localRotation = Quaternion.Euler(a1 * Deg, 0f, 0f);
            Rig.Shins[side].localRotation = Quaternion.Euler(a2 * Deg, 0f, 0f);
            Rig.Feet[side].localRotation = Quaternion.Euler(-(a1 + a2) * Deg, 0f, 0f); // keep the foot level
        }

        // Arms swing against the legs; in the pose they go out to the sides and up (the sword arm stays upright).
        for (int side = 0; side < 2; side++)
        {
            float legPhase = phase + (side == 0 ? Mathf.PI : 0f);
            float swing = 14f * Mathf.Cos(legPhase) * Mathf.Clamp01(absSpeed / 3f);
            float roll = (side == 0 ? -1f : 1f) * 40f * pose; // about z: swings the hanging arm away from the body
            Rig.UpperArms[side].localRotation = Quaternion.Euler(0f, side == 1 ? swingYaw : 0f, 0f) * Quaternion.Euler(-swing - 15f * pose + (side == 1 ? swingRaise : 0f), 0f, roll);
            Rig.ForeArms[side].localRotation = Quaternion.Euler(-Mathf.Max(0f, swing) * 0.4f - 8f * pose, 0f, 0f);
        }

        Rig.Sword.localRotation = Quaternion.Euler(125f * swingFlat, 0f, 0f); // level, then pitched down so the sweep is low

        // Footsteps: one per foot strike.
        int step = Mathf.FloorToInt(phase / Mathf.PI);
        if (step != lastStep)
        {
            lastStep = step;
            if (absSpeed > 0.8f)
            {
                ZordSounds.Play(ZordSounds.Step, Cart.transform.position, 0.8f);
                CameraModuleController.Shake(GameManager.CameraGameplaySettings.JumboBurgerGiantFormFootstepScreenshakeSettings, Rig.Feet[step & 1].position, 0.2f);
                VfxManager.PlayPooledVfxLocalOnly(VfxType.GolfCartJumpEnd, Rig.Feet[step & 1].position, Quaternion.identity);
            }
        }
    }

    /// <summary>
    /// Planar two-bone IK for a leg hanging from a joint: d is the target relative to the joint (x = forward, y = up).
    /// Angles are rotations about +X (positive swings the limb backward): a1 for the thigh, a2 for the shin relative to
    /// it. The knee points forward.
    /// </summary>
    private static void Solve(float l1, float l2, Vector2 d, out float a1, out float a2)
    {
        float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 0.001f, l1 + l2 - 0.001f);
        float toTarget = Mathf.Atan2(-d.x, -d.y);
        float cosKnee = (l1 * l1 + l2 * l2 - dist * dist) / (2f * l1 * l2);
        float flex = Mathf.PI - Mathf.Acos(Mathf.Clamp(cosKnee, -1f, 1f));
        float cosHip = (l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist);
        float spread = Mathf.Acos(Mathf.Clamp(cosHip, -1f, 1f));
        a1 = toTarget - spread;
        a2 = flex;
    }
}

using System.Collections;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ItemPack.Items;

/// <summary>
/// Web Slinger (Spider-Man): your first click starts a timer, and until it runs out you can sling webs. Hold
/// left click to shoot a web at what you're aiming at; it sticks and you swing from it like a pendulum, reeling
/// in a little as you go. Let go of the button to release and keep your speed. When the timer ends the item is
/// used up.
///
/// Each player moves their own golfer, so the swing is applied to the local player's rigidbody every physics
/// step (see <see cref="FixedTick"/>); everyone else just sees the strand and hears the thwip.
/// </summary>
internal class WebSlingerItem : CustomItem
{
    public static WebSlingerItem Instance { get; private set; }

    public override int Id => 112;
    public override string Key => "WebSlinger";
    public override string DisplayName => "Web Slinger";
    // Mobility: helps you catch up or get back to your ball.
    public override PoolWeights BoxWeights => new(close: 2, medium: 5, far: 6, veryFar: 6, aheadOfBall: 10, drivingRange: 20);
    // Held in one hand like the coffee cup.
    public override ItemType TemplateItem => ItemType.Coffee;
    public override EquipmentType TemplateEquipment => EquipmentType.CoffeeMug;
    public override string AssetName => "web_slinger";
    public override bool HandlesOwnInput => true;
    public override bool SuppressAiming => true;

    protected override int DefaultUses => 1;

    internal static ConfigEntry<float> SlingSeconds;
    internal static ConfigEntry<float> MaxRange;
    internal static ConfigEntry<float> ReelSpeed;
    internal static ConfigEntry<float> PullStrength;
    internal static ConfigEntry<float> MaxSpeed;
    internal static ConfigEntry<float> SoundVolume;

    private const float MinRope = 4f;
    private const float AimAssistRadius = 1.5f;

    // The timer: not started (inactive), or the seconds left. It only counts down while the glove is in hand.
    private static bool slinging;
    private static float secondsLeft;

    // The current web, if any (local player only).
    private static bool attached;
    private static Vector3 anchor;
    private static float ropeLength;

    public WebSlingerItem() => Instance = this;

    protected override void BindConfig()
    {
        SlingSeconds = Bind("SlingSeconds", 5f,"How long you can sling webs after your first click.");
        MaxRange = Bind("MaxRange", 70f, "How far a web can reach (meters).");
        ReelSpeed = Bind("ReelSpeed", 4f, "How fast the web reels you in while you swing (m/s). 0 for a plain pendulum.");
        PullStrength = Bind("PullStrength", 55f, "How hard the web pulls you toward where it stuck (m/s per second).");
        MaxSpeed = Bind("MaxSpeed", 32f, "Fastest you can go while swinging (m/s).");
        SoundVolume = Bind("SoundVolume", 1f, "Volume of the web sounds (0-1). Local setting.");
    }

    // Shooting and swinging come from Tick/FixedTick (hold left click); the game's use button is swallowed.
    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming)
    {
        yield break;
    }

    protected override void RegisterMessages() => SlingerNet.InitSerializers();
    public override void OnStartServer() => SlingerNet.RegisterServerHandlers();
    public override void OnStartClient() => SlingerNet.RegisterClientHandlers();
    public override void OnStopClient()
    {
        SlingerWebs.ClearAll();
        attached = false;
        slinging = false;
    }

    public override void Tick()
    {
        SlingerWebs.Tick();
        bool holding = OwnInput.TryGetHolder<WebSlingerItem>(out PlayerInventory inventory);
        PlayerInfo player = holding ? inventory.PlayerInfo : GameManager.LocalPlayerInfo;
        if (attached && (!holding || !CanSwing(player)))
        {
            Detach();
        }
        if (!holding || Mouse.current == null)
        {
            return;
        }

        if (slinging)
        {
            secondsLeft -= Time.deltaTime;
            if (secondsLeft <= 0f)
            {
                // Time's up: let go, and the item is used.
                slinging = false;
                Detach();
                if (OwnInput.CanUse(inventory))
                {
                    ItemUse.ConsumeUse(inventory);
                    ItemUse.Finish(inventory);
                }
                return;
            }
        }
        else if (Mouse.current.leftButton.wasPressedThisFrame && OwnInput.CanUse(inventory))
        {
            slinging = true;
            secondsLeft = SlingSeconds.Value;
        }
        if (!slinging)
        {
            return;
        }
        if (inventory.PlayerInfo.Movement.IsVisible)
        {
            TimerWheel.Show(secondsLeft / Mathf.Max(0.01f, SlingSeconds.Value));
        }

        if (Mouse.current.leftButton.wasPressedThisFrame && !attached && CanSwing(player))
        {
            Shoot(player);
        }
        else if (attached && !Mouse.current.leftButton.isPressed)
        {
            Detach();
        }
    }

    /// <summary>Whether the local player can hang from a web right now (not knocked out, in a cart or hidden).</summary>
    private static bool CanSwing(PlayerInfo player) =>
        player != null && player.Movement.IsVisible && !player.Movement.IsKnockedOutOrRecovering && !player.ActiveGolfCartSeat.IsValid();

    private static void Shoot(PlayerInfo player)
    {
        Camera cam = GameManager.Camera;
        if (cam == null)
        {
            return;
        }
        Vector3 origin = SlingerWebs.HandOf(player);
        float toPlayer = Vector3.Distance(cam.transform.position, player.transform.position);
        float distance = MaxRange.Value + toPlayer;
        int mask = WorldMask.Solid;
        bool hit = NearestHit(player, cam, 0f, distance, mask, out RaycastHit target);
        if (!hit)
        {
            // Be forgiving when aiming at thin things or the horizon.
            hit = NearestHit(player, cam, AimAssistRadius, distance, mask, out target);
        }
        if (!hit || (target.point - player.transform.position).sqrMagnitude > MaxRange.Value * MaxRange.Value)
        {
            // Missed: the web just flies off and fizzles.
            Vector3 end = origin + cam.transform.forward * Mathf.Min(MaxRange.Value, 30f);
            Beams.Show(origin, end, new Color(1f, 1f, 1f, 0.9f), 0.03f, 0.01f, 0.3f);
            ItemAudio.Play(SlingerSounds.Thwip, ItemAudio.SfxBus, SoundVolume.Value * 0.7f);
            return;
        }

        anchor = target.point;
        ropeLength = Mathf.Max(MinRope, Vector3.Distance(player.Rigidbody.worldCenterOfMass, anchor));
        attached = true;
        // A swing starts with a pull: get off the ground if the web is above us.
        if (player.Movement.IsGrounded && anchor.y > player.transform.position.y + 1f)
        {
            player.Movement.Unground();
            player.Rigidbody.linearVelocity += Vector3.up * 3f;
        }
        SlingerWebs.Attach(player.netId, anchor);
        SlingerNet.SendAttach(anchor);
    }

    private static readonly RaycastHit[] hits = new RaycastHit[32];

    /// <summary>The nearest thing along the camera's view that isn't the player's own body (a ray, or a fat one with a radius).</summary>
    private static bool NearestHit(PlayerInfo player, Camera cam, float radius, float distance, int mask, out RaycastHit best)
    {
        int count = radius > 0f
            ? Physics.SphereCastNonAlloc(cam.transform.position, radius, cam.transform.forward, hits, distance, mask, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, hits, distance, mask, QueryTriggerInteraction.Ignore);
        best = default;
        best.distance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            // Spherecasts that start inside something report a zero distance and no useful point; skip those.
            if (hits[i].distance > 0f && hits[i].distance < best.distance && !hits[i].collider.transform.IsChildOf(player.transform))
            {
                best = hits[i];
            }
        }
        return best.distance < float.MaxValue;
    }

    private static void Detach()
    {
        if (!attached)
        {
            return;
        }
        attached = false;
        PlayerInfo player = GameManager.LocalPlayerInfo;
        if (player != null)
        {
            SlingerWebs.Detach(player.netId);
        }
        SlingerNet.SendDetach();
    }

    public override void FixedTick()
    {
        if (!attached)
        {
            return;
        }
        PlayerInfo player = GameManager.LocalPlayerInfo;
        if (!CanSwing(player))
        {
            Detach();
            return;
        }
        Rigidbody body = player.Rigidbody;
        float dt = Time.fixedDeltaTime;
        Vector3 velocity = body.linearVelocity;
        Vector3 center = body.worldCenterOfMass;

        // The web reels in as you swing, which speeds the swing up (like skating in on a rope).
        if (ropeLength > MinRope)
        {
            ropeLength = Mathf.Max(MinRope, ropeLength - ReelSpeed.Value * dt);
        }

        // The game's air drag would eat the swing's momentum; give most of it back.
        float drag = GameManager.PlayerMovementSettings.AirHorizontalDrag;
        float keep = Mathf.Max(0.05f, 1f - drag * dt);
        velocity.x /= Mathf.Lerp(1f, keep, 0.9f);
        velocity.z /= Mathf.Lerp(1f, keep, 0.9f);

        // The rope: when it's taut, take away the speed that would stretch it and pull back to its length.
        Vector3 toAnchor = anchor - center;
        float distance = toAnchor.magnitude;

        // The web yanks you toward its anchor, with a bit of lift so a web stuck in the ground still launches you.
        if (distance > MinRope * 1.5f)
        {
            velocity += (toAnchor / distance + Vector3.up * 0.5f).normalized * (PullStrength.Value * dt);
            // Gravity is strong in this game: for a web stuck low, float most of the way (a zip); for a high one let
            // it hang you like a pendulum.
            float lowness = Mathf.Clamp01((8f - (anchor.y - center.y)) / 6f);
            velocity.y += -Physics.gravity.y * GameManager.PlayerMovementSettings.BaseGravityFactor * 1.1f * lowness * dt;
            if (player.Movement.IsGrounded)
            {
                player.Movement.Unground();
                velocity.y = Mathf.Max(velocity.y, 3f);
            }
        }
        if (distance > 0.01f && distance > ropeLength)
        {
            Vector3 direction = toAnchor / distance;
            float outward = -Vector3.Dot(velocity, direction);
            if (outward > 0f)
            {
                velocity += direction * outward;
            }
            velocity += direction * Mathf.Min((distance - ropeLength) / dt * 0.2f, 14f);
        }
        velocity = Vector3.ClampMagnitude(velocity, MaxSpeed.Value);
        body.linearVelocity = velocity;

        // Swinging up off the ground: let go of the floor so the game's ground snapping doesn't pin us.
        if (player.Movement.IsGrounded && velocity.y > 1f)
        {
            player.Movement.Unground();
        }
    }
}

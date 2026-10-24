using System.Collections;
using BepInEx.Configuration;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ItemPack.Items;

/// <summary>
/// Web Slinger (Spider-Man): your first click starts a timer, and until it runs out you can sling webs. Hold
/// left click to shoot a web at what you're aiming at; it sticks and you swing from it like a pendulum, reeling
/// in a little as you go. Let go of the button to release and keep your speed. When the timer ends, or you switch to
/// another item, the item is used up.
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
    protected override float DefaultHeldScale => 1.8f;

    internal static ConfigEntry<float> SlingSeconds;
    internal static ConfigEntry<float> MaxRange;
    internal static ConfigEntry<float> ReelSpeed;
    internal static ConfigEntry<float> PullStrength;
    internal static ConfigEntry<float> MaxSpeed;
    internal static ConfigEntry<float> YankStrength;
    internal static ConfigEntry<float> YankSpeed;
    internal static ConfigEntry<float> SoundVolume;

    private const float MinRope = 4f;
    private const float AimAssistRadius = 1.5f;

    // The timer: not started (inactive), or the seconds left. Switching to another item ends it.
    private static bool slinging;
    private static float secondsLeft;
    private static int slingSlot;

    // The current web, if any (local player only).
    private static bool attached;
    private static Vector3 anchor;
    private static bool anchorViaPortal; // the web crossed a portal, so the rope runs through it
    private static float ropeLength;
    // Set when the web stuck to a player, cart or item: that gets reeled in to us instead of us swinging.
    private static uint reelTarget;

    public WebSlingerItem() => Instance = this;

    protected override void BindConfig()
    {
        SlingSeconds = Bind("SlingSeconds", 5f,"How long you can sling webs after your first click.");
        MaxRange = Bind("MaxRange", 70f, "How far a web can reach (meters).");
        ReelSpeed = Bind("ReelSpeed", 4f, "How fast the web reels you in while you swing (m/s). 0 for a plain pendulum.");
        PullStrength = Bind("PullStrength", 55f, "How hard the web pulls you toward where it stuck (m/s per second).");
        MaxSpeed = Bind("MaxSpeed", 32f, "Fastest you can go while swinging (m/s).");
        YankStrength = Bind("YankStrength", 80f, "How hard the web pulls a player, cart or item you hit toward you (m/s per second).");
        YankSpeed = Bind("YankSpeed", 26f, "Fastest a player, cart or item you hit gets reeled in (m/s).");
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
        SlingerNet.Reset();
        attached = false;
        reelTarget = 0;
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
        if (slinging && !holding)
        {
            // Switched to something else: that ends the timer, and the item is used up.
            slinging = false;
            ItemUse.EndAway(player != null ? player.Inventory : null, slingSlot, Type);
            return;
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
            ItemUse.BreakStealth(inventory);
            slinging = true;
            slingSlot = inventory.EquippedItemIndex;
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
        // Something that can be dragged (a player, cart or item) closer than the scenery is reeled in to us.
        if (FindMovable(player, cam, distance, out RaycastHit movable, out uint movableId)
            && (!hit || movable.distance <= target.distance)
            && InRange(player, movable, toPlayer))
        {
            reelTarget = movableId;
            attached = true;
            bool crossed = CrossedPortal(cam, movable);
            SlingerWebs.Attach(player.netId, movable.point, movableId, crossed);
            SlingerNet.SendAttach(movable.point, movableId, crossed);
            return;
        }
        if (!hit || !InRange(player, target, toPlayer))
        {
            // Missed: the web just flies off and fizzles.
            Vector3 end = origin + cam.transform.forward * Mathf.Min(MaxRange.Value, 30f);
            Beams.Show(origin, end, new Color(1f, 1f, 1f, 0.9f), 0.03f, 0.01f, 0.3f);
            ItemAudio.Play(SlingerSounds.Thwip, ItemAudio.SfxBus, SoundVolume.Value * 0.7f);
            return;
        }

        anchor = target.point;
        anchorViaPortal = CrossedPortal(cam, target);
        ropeLength = Mathf.Max(MinRope, RopeTo(player.Rigidbody.worldCenterOfMass, anchor, anchorViaPortal, out _));
        attached = true;
        // A swing starts with a pull: get off the ground if the web is above us.
        if (player.Movement.IsGrounded && anchor.y > player.transform.position.y + 1f)
        {
            player.Movement.Unground();
            player.Rigidbody.linearVelocity += Vector3.up * 3f;
        }
        SlingerWebs.Attach(player.netId, anchor, 0, anchorViaPortal);
        SlingerNet.SendAttach(anchor, 0, anchorViaPortal);
    }

    private static readonly RaycastHit[] hits = new RaycastHit[32];

    /// <summary>Whether the web's path to a hit jumped through a portal: the distance it travelled exceeds the straight line from the camera.</summary>
    private static bool CrossedPortal(Camera cam, RaycastHit hit) =>
        Portals.All.Count > 0 && Mathf.Abs(hit.distance - (hit.point - cam.transform.position).magnitude) > 0.5f;

    /// <summary>Whether a web hit is within reach: straight from the player, or along the web's path when it went through a portal.</summary>
    private static bool InRange(PlayerInfo player, RaycastHit hit, float cameraDistance)
    {
        float straight = (hit.point - player.transform.position).magnitude;
        return Mathf.Min(straight, hit.distance - cameraDistance) <= MaxRange.Value;
    }

    /// <summary>
    /// How long the web is from <paramref name="from"/> to <paramref name="target"/>, and where it pulls toward: the
    /// target itself, or the portal it runs into first when the web came through a portal (the far piece counts too).
    /// </summary>
    internal static float RopeTo(Vector3 from, Vector3 target, bool viaPortal, out Vector3 pullToward)
    {
        pullToward = target;
        float direct = (target - from).magnitude;
        if (viaPortal && Portals.All.Count > 0 && PortalShots.TryFindPathTo(from, target, out Vector3 entry, out Vector3 exit))
        {
            float around = (entry - from).magnitude + (target - exit).magnitude;
            if (around < direct)
            {
                pullToward = entry;
                return around;
            }
        }
        return direct;
    }

    /// <summary>The nearest other player (on foot), golf cart or loose item along the view, with its network id.</summary>
    private static bool FindMovable(PlayerInfo player, Camera cam, float distance, out RaycastHit best, out uint netId)
    {
        LayerSettings layers = GameManager.LayerSettings;
        int mask = layers.PlayersMask | layers.GolfCartsMask | (1 << layers.ItemsLayer);
        netId = 0;
        if (!NearestHit(player, cam, 0.35f, distance, mask, out best))
        {
            return false;
        }
        Collider collider = best.collider;
        Entity entity = collider.GetComponentInParent<Entity>();
        NetworkIdentity identity = null;
        if (entity != null && (entity.IsGolfCart || (entity.IsPlayer && !entity.PlayerInfo.ActiveGolfCartSeat.IsValid())))
        {
            identity = entity.GetComponentInParent<NetworkIdentity>();
        }
        else if (entity == null)
        {
            PhysicalItem item = collider.GetComponentInParent<PhysicalItem>();
            identity = item != null ? item.GetComponentInParent<NetworkIdentity>() : null;
        }
        if (identity == null || identity.netId == 0 || identity == player.GetComponent<NetworkIdentity>())
        {
            return false;
        }
        netId = identity.netId;
        return true;
    }

    /// <summary>Accelerates a grabbed body toward the slinger's hand, stopping short so it arrives instead of overshooting.</summary>
    internal static void Reel(Rigidbody body, Vector3 hand, float dt, bool viaPortal)
    {
        // Through a portal (the web went through one), it's reeled toward the portal first.
        float distance = RopeTo(body.worldCenterOfMass, hand, viaPortal, out Vector3 pullToward);
        Vector3 toHand = pullToward - body.worldCenterOfMass;
        toHand = toHand.sqrMagnitude > 1e-6f ? toHand.normalized * distance : Vector3.zero;
        Vector3 velocity = body.linearVelocity;
        if (distance < 3f)
        {
            velocity *= Mathf.Max(0f, 1f - 8f * dt);
        }
        else
        {
            // A little lift keeps it off the ground (and offsets gravity) on the way in.
            velocity += (toHand / distance + Vector3.up * 0.3f).normalized * (YankStrength.Value * dt);
            velocity.y += -Physics.gravity.y * 0.8f * dt;
            velocity = Vector3.ClampMagnitude(velocity, YankSpeed.Value);
        }
        body.linearVelocity = velocity;
    }

    /// <summary>The nearest thing along the camera's view that isn't the player's own body (a ray, or a fat one with a radius).</summary>
    private static bool NearestHit(PlayerInfo player, Camera cam, float radius, float distance, int mask, out RaycastHit best)
    {
        // The web goes on through portals (distances count along the whole path, points are where it really lands).
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        int count = radius > 0f
            ? PortalShots.SphereCastNonAlloc(ray, radius, hits, distance, mask, QueryTriggerInteraction.Ignore)
            : PortalShots.RaycastNonAlloc(ray, hits, distance, mask, QueryTriggerInteraction.Ignore);
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
        reelTarget = 0;
        PlayerInfo player = GameManager.LocalPlayerInfo;
        if (player != null)
        {
            SlingerWebs.Detach(player.netId);
        }
        SlingerNet.SendDetach();
    }

    public override void FixedTick()
    {
        SlingerNet.FixedTick();
        // A web stuck to something we're reeling in doesn't move us.
        if (!attached || reelTarget != 0)
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
        // (Through a portal the web pulls toward the portal first, then on to where it stuck.)
        float distance = RopeTo(center, anchor, anchorViaPortal, out Vector3 pullToward);
        Vector3 toAnchor = pullToward - center;
        float toPull = toAnchor.magnitude;
        toAnchor = toPull > 0.001f ? toAnchor / toPull * distance : toAnchor;

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

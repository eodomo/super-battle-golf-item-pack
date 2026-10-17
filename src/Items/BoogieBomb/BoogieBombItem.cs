using System.Collections;
using BepInEx.Configuration;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Boogie Bomb (Fortnite): throw the disco ball; it flies where the crosshair points (and goes through portals) and pops on impact, or as soon as it touches another player. Everyone caught in the blast does their victory dance for a few seconds,
/// unable to move or use items (they can still look around). Electromagnet shields protect you.
/// </summary>
internal class BoogieBombItem : CustomItem
{
    public static BoogieBombItem Instance { get; private set; }

    public override int Id => 108;
    public override string Key => "BoogieBomb";
    public override string DisplayName => "Boogie Bomb";
    // Crowd control: best where players bunch up.
    public override PoolWeights BoxWeights => new(close: 8, medium: 6, far: 2, veryFar: 0, aheadOfBall: 0, drivingRange: 20);
    // Thrown one-handed like the freeze bomb.
    public override ItemType TemplateItem => ItemType.FreezeBomb;
    public override EquipmentType TemplateEquipment => EquipmentType.FreezeBomb;
    public override string AssetName => "boogie_bomb";
    public override ReticleType AimReticle => ReticleType.FreezeBomb;

    protected override int DefaultUses => 1;
    protected override float DefaultHeldScale => 1f;

    internal static ConfigEntry<float> Radius;
    internal static ConfigEntry<float> DanceSeconds;
    internal static ConfigEntry<float> ThrowSpeed;
    internal static ConfigEntry<bool> AffectThrower;
    internal static ConfigEntry<float> SoundVolume;

    public BoogieBombItem() => Instance = this;

    protected override void BindConfig()
    {
        Radius = Bind("Radius", 10f, "Blast radius (meters): everyone inside dances.");
        DanceSeconds = Bind("DanceSeconds", 5f, "How long people dance.");
        ThrowSpeed = Bind("ThrowSpeed", 26f, "Throw speed (m/s). It's thrown on the flattest arc that lands where you aim.");
        AffectThrower = Bind("AffectThrower", true, "Whether the thrower dances too if they're caught in their own blast.");
        SoundVolume = Bind("SoundVolume", 1f, "Volume of the pop and the disco music (0-1). Local setting.");
    }

    public override void ConfigureItemData(ItemData data)
    {
        data.NonAimUse = ItemNonAimingUse.Use;
        data.IsExplosive = false;
        data.CanHitProjectiles = false;
        data.CanUsageAffectBalls = false;
    }

    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming)
    {
        PlayerInfo player = inventory.PlayerInfo;
        inventory.SetCurrentItemUse(ItemUseType.Regular);
        player.Movement.AlignWithCameraImmediately();
        float throwTime = GameManager.ItemSettings.FreezeBombThrowTime;
        yield return new WaitForSeconds(throwTime > 0.05f && throwTime < 1f ? throwTime : 0.25f);

        ItemUse.ConsumeUse(inventory);
        Vector3 hand = player.RightHandEquipmentSwitcher.transform.position;
        inventory.LocalPlayerMarkThrownItem(PlayerInventory.ThrownItemHand.Right);
        BoogieNet.RequestThrow(hand, AimedVelocity(player, hand, ThrowSpeed.Value));

        yield return new WaitForSeconds(0.45f);
        ItemUse.Finish(inventory);
    }

    /// <summary>
    /// A throw from the hand that lands where the crosshair points (the flattest arc that gets there at the throw
    /// speed), or as far as it can go toward it if that's out of reach.
    /// </summary>
    internal static Vector3 AimedVelocity(PlayerInfo player, Vector3 hand, float speed)
    {
        const float MaxAim = 80f;
        Camera cam = GameManager.Camera;
        if (cam == null)
        {
            return (player.transform.forward + Vector3.up * 0.3f).normalized * speed;
        }
        Vector3 forward = cam.transform.forward;
        int mask = BoogieWorld.GroundMask | GameManager.LayerSettings.PlayersMask;
        Vector3 target = cam.transform.position + forward * MaxAim;
        int count = Physics.RaycastNonAlloc(cam.transform.position, forward, aimHits, MaxAim, mask, QueryTriggerInteraction.Ignore);
        float nearest = MaxAim;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = aimHits[i];
            if (hit.distance < nearest && hit.collider.GetComponentInParent<PlayerInfo>() != player)
            {
                nearest = hit.distance;
                target = hit.point;
            }
        }

        Vector3 delta = target - hand;
        Vector3 flat = new Vector3(delta.x, 0f, delta.z);
        float x = flat.magnitude;
        if (x < 0.5f)
        {
            return forward * speed; // straight up or down
        }
        Vector3 across = flat / x;
        float y = delta.y;
        float g = -Physics.gravity.y;

        // A short lob a little above the straight line to the target, at just the speed that lands it there...
        float lob = Mathf.Min(Mathf.Atan2(y, x) + 18f * Mathf.Deg2Rad, 60f * Mathf.Deg2Rad);
        float rise = x * Mathf.Tan(lob) - y;
        if (rise > 0.01f)
        {
            float cos = Mathf.Cos(lob);
            float needed = Mathf.Sqrt(g * x * x / (2f * cos * cos * rise));
            if (needed <= speed)
            {
                return (across * cos + Vector3.up * Mathf.Sin(lob)) * needed;
            }
        }
        // ...or for far targets, the flattest arc at full speed (or as far as it goes if it can't reach).
        float v2 = speed * speed;
        float disc = v2 * v2 - g * (g * x * x + 2f * y * v2);
        float angle = disc >= 0f
            ? Mathf.Atan((v2 - Mathf.Sqrt(disc)) / (g * x))
            : Mathf.PI * 0.25f;
        return (across * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)) * speed;
    }

    private static readonly RaycastHit[] aimHits = new RaycastHit[16];

    protected override void RegisterMessages() => BoogieNet.InitSerializers();
    public override void OnStartServer() => BoogieNet.RegisterServerHandlers();
    public override void OnStopServer() => BoogieNet.ResetServer();
    public override void OnStartClient() => BoogieNet.RegisterClientHandlers();
    public override void OnStopClient() => BoogieWorld.ClearAll();
    public override void Tick() => BoogieWorld.Tick();
}

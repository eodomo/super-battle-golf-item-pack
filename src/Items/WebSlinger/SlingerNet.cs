using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

public struct WebAttachRequest : NetworkMessage
{
    public Vector3 anchor;
    public uint targetNetId; // 0 = stuck to the world; otherwise the player, cart or item being reeled in
}

public struct WebAttach : NetworkMessage
{
    public uint swingerNetId;
    public Vector3 anchor;
    public uint targetNetId;
}

public struct WebDetachRequest : NetworkMessage
{
}

public struct WebDetach : NetworkMessage
{
    public uint swingerNetId;
}

/// <summary>
/// Tells everyone when a player's web sticks and lets go, so they can see the strand and hear the thwip.
/// The swinging itself is the swinger's own physics (players move themselves); these messages are for show.
/// </summary>
internal static class SlingerNet
{
    private const float MaxAnchorDistance = 200f;

    // Things the web grabbed are reeled in toward the slinger's hand. Carts and dropped items belong to the host, so
    // it pulls those (swinger -> target); a player moves themselves, so the pulled player's own client does it.
    private static readonly Dictionary<uint, uint> serverPulls = new();
    private static readonly List<uint> finished = new();
    private static uint pulledBy;
    private static float pulledUntil;

    public static void InitSerializers()
    {
        Writer<WebAttachRequest>.write = (w, m) => { w.WriteVector3(m.anchor); w.WriteUInt(m.targetNetId); };
        Reader<WebAttachRequest>.read = r => new WebAttachRequest { anchor = r.ReadVector3(), targetNetId = r.ReadUInt() };
        Writer<WebAttach>.write = (w, m) => { w.WriteUInt(m.swingerNetId); w.WriteVector3(m.anchor); w.WriteUInt(m.targetNetId); };
        Reader<WebAttach>.read = r => new WebAttach { swingerNetId = r.ReadUInt(), anchor = r.ReadVector3(), targetNetId = r.ReadUInt() };
        Writer<WebDetachRequest>.write = (w, m) => { };
        Reader<WebDetachRequest>.read = r => new WebDetachRequest();
        Writer<WebDetach>.write = (w, m) => w.WriteUInt(m.swingerNetId);
        Reader<WebDetach>.read = r => new WebDetach { swingerNetId = r.ReadUInt() };
    }

    public static void RegisterServerHandlers()
    {
        NetworkServer.RegisterHandler<WebAttachRequest>(OnServerAttach);
        NetworkServer.RegisterHandler<WebDetachRequest>(OnServerDetach);
    }

    public static void RegisterClientHandlers()
    {
        NetworkClient.ReplaceHandler<WebAttach>(msg =>
        {
            if (!IsLocal(msg.swingerNetId))
            {
                SlingerWebs.Attach(msg.swingerNetId, msg.anchor, msg.targetNetId);
                if (IsLocal(msg.targetNetId))
                {
                    pulledBy = msg.swingerNetId;
                    pulledUntil = Time.time + WebSlingerItem.SlingSeconds.Value + 1f;
                }
            }
        });
        NetworkClient.ReplaceHandler<WebDetach>(msg =>
        {
            if (!IsLocal(msg.swingerNetId))
            {
                SlingerWebs.Detach(msg.swingerNetId);
                if (pulledBy == msg.swingerNetId)
                {
                    pulledBy = 0;
                }
            }
        });
    }

    public static void SendAttach(Vector3 anchor, uint targetNetId = 0)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new WebAttachRequest { anchor = anchor, targetNetId = targetNetId });
        }
    }

    public static void SendDetach()
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new WebDetachRequest());
        }
    }

    private static void OnServerAttach(NetworkConnectionToClient conn, WebAttachRequest msg)
    {
        if (conn.identity != null && (conn.identity.transform.position - msg.anchor).sqrMagnitude <= MaxAnchorDistance * MaxAnchorDistance)
        {
            serverPulls.Remove(conn.identity.netId);
            if (msg.targetNetId != 0 && NetworkServer.spawned.TryGetValue(msg.targetNetId, out NetworkIdentity target) && target != null
                && !target.TryGetComponent(out PlayerInfo _) && (target.transform.position - conn.identity.transform.position).sqrMagnitude <= MaxAnchorDistance * MaxAnchorDistance)
            {
                serverPulls[conn.identity.netId] = msg.targetNetId;
            }
            NetworkServer.SendToAll(new WebAttach { swingerNetId = conn.identity.netId, anchor = msg.anchor, targetNetId = msg.targetNetId });
        }
    }

    private static void OnServerDetach(NetworkConnectionToClient conn, WebDetachRequest msg)
    {
        if (conn.identity != null)
        {
            serverPulls.Remove(conn.identity.netId);
            NetworkServer.SendToAll(new WebDetach { swingerNetId = conn.identity.netId });
        }
    }

    /// <summary>Every physics step: the host reels in the carts and items webs are stuck to, and a pulled player reels themselves in.</summary>
    public static void FixedTick()
    {
        if (NetworkServer.active && serverPulls.Count > 0)
        {
            finished.Clear();
            foreach (KeyValuePair<uint, uint> pull in serverPulls)
            {
                if (!NetworkServer.spawned.TryGetValue(pull.Key, out NetworkIdentity swinger) || swinger == null
                    || !NetworkServer.spawned.TryGetValue(pull.Value, out NetworkIdentity target) || target == null)
                {
                    finished.Add(pull.Key);
                    continue;
                }
                Rigidbody body = target.GetComponentInChildren<Rigidbody>();
                if (body != null && !body.isKinematic && swinger.TryGetComponent(out PlayerInfo player))
                {
                    WebSlingerItem.Reel(body, SlingerWebs.HandOf(player), Time.fixedDeltaTime);
                }
            }
            foreach (uint netId in finished)
            {
                serverPulls.Remove(netId);
            }
        }

        if (pulledBy != 0)
        {
            PlayerInfo local = GameManager.LocalPlayerInfo;
            if (Time.time > pulledUntil || local == null || !local.Movement.IsVisible || local.ActiveGolfCartSeat.IsValid()
                || !NetworkClient.spawned.TryGetValue(pulledBy, out NetworkIdentity swinger) || swinger == null
                || !swinger.TryGetComponent(out PlayerInfo slinger))
            {
                pulledBy = 0;
                return;
            }
            WebSlingerItem.Reel(local.Rigidbody, SlingerWebs.HandOf(slinger), Time.fixedDeltaTime);
            if (local.Movement.IsGrounded)
            {
                local.Movement.Unground();
            }
        }
    }

    public static void Reset()
    {
        serverPulls.Clear();
        pulledBy = 0;
    }

    private static bool IsLocal(uint netId) => netId != 0 && GameManager.LocalPlayerInfo != null && GameManager.LocalPlayerInfo.netId == netId;
}

/// <summary>The web strands of everyone who's swinging, on each client.</summary>
internal static class SlingerWebs
{
    private const float ShootSeconds = 0.09f;
    private const float LingerSeconds = 0.9f;

    private class View
    {
        public uint netId;
        public PlayerInfo player;
        public bool attached;
        public Vector3 anchor;
        public Transform target;
        public float attachTime;
        public float detachTime;
        public LineRenderer line;
        public GameObject splat;
    }

    private static readonly Dictionary<uint, View> views = new();
    private static readonly List<uint> gone = new();
    private static Material strandMaterial;

    public static void Attach(uint netId, Vector3 anchor, uint targetNetId = 0)
    {
        View view = GetOrCreate(netId);
        if (view == null)
        {
            return;
        }
        view.attached = true;
        view.anchor = anchor;
        view.target = targetNetId != 0 && NetworkClient.spawned.TryGetValue(targetNetId, out NetworkIdentity grabbed) && grabbed != null ? grabbed.transform : null;
        view.attachTime = Time.time;
        view.line.enabled = true;
        view.splat.SetActive(true);
        view.splat.transform.position = anchor;

        float volume = ItemAudio.Attenuate(anchor, WebSlingerItem.SoundVolume.Value, 15f, 90f);
        ItemAudio.Play(SlingerSounds.Thwip, ItemAudio.SfxBus, volume);
        ItemAudio.Play(SlingerSounds.Snap, ItemAudio.SfxBus, volume * 0.8f);
    }

    public static void Detach(uint netId)
    {
        if (views.TryGetValue(netId, out View view) && view.attached)
        {
            view.attached = false;
            view.detachTime = Time.time;
            view.line.enabled = false;
            view.splat.SetActive(false);
        }
    }

    /// <summary>Whether this player has a web out (the local swinger's own physics doesn't depend on it).</summary>
    public static bool IsAttached(uint netId) => views.TryGetValue(netId, out View view) && view.attached;

    public static void Tick()
    {
        if (views.Count == 0)
        {
            return;
        }
        gone.Clear();
        foreach (View view in views.Values)
        {
            if (view.player == null)
            {
                gone.Add(view.netId);
                continue;
            }
            if (view.attached)
            {
                Vector3 hand = HandOf(view.player);
                if (view.target != null)
                {
                    view.anchor = view.target.position + Vector3.up * 0.5f;
                    view.splat.transform.position = view.anchor;
                }
                float shot = Mathf.Clamp01((Time.time - view.attachTime) / ShootSeconds);
                view.line.SetPosition(0, hand);
                view.line.SetPosition(1, Vector3.Lerp(hand, view.anchor, shot));
                view.line.startWidth = view.line.endWidth = 0.035f;
            }
            else if (Time.time - view.detachTime > LingerSeconds)
            {
                gone.Add(view.netId);
            }
        }
        foreach (uint netId in gone)
        {
            Remove(netId);
        }
    }

    public static void ClearAll()
    {
        gone.Clear();
        gone.AddRange(views.Keys);
        foreach (uint netId in gone)
        {
            Remove(netId);
        }
    }

    /// <summary>Where the web comes out of: the right hand (the glove is held there).</summary>
    public static Vector3 HandOf(PlayerInfo player)
    {
        Transform hand = player.RightHandEquipmentSwitcher != null ? player.RightHandEquipmentSwitcher.transform : null;
        return hand != null ? hand.position : player.transform.position + Vector3.up * 1.1f;
    }

    private static void Remove(uint netId)
    {
        if (!views.TryGetValue(netId, out View view))
        {
            return;
        }
        if (view.line != null)
        {
            Object.Destroy(view.line.gameObject);
        }
        if (view.splat != null)
        {
            Object.Destroy(view.splat);
        }
        views.Remove(netId);
    }

    private static View GetOrCreate(uint netId)
    {
        if (views.TryGetValue(netId, out View existing))
        {
            return existing;
        }
        if (!NetworkClient.spawned.TryGetValue(netId, out NetworkIdentity identity) || identity == null)
        {
            return null;
        }
        PlayerInfo player = identity.GetComponent<PlayerInfo>();
        if (player == null)
        {
            return null;
        }
        strandMaterial ??= UnlitMaterials.Additive(UnlitMaterials.SoftDot, Color.white);
        var strand = new GameObject("WebStrand");
        var line = strand.AddComponent<LineRenderer>();
        line.sharedMaterial = strandMaterial;
        line.positionCount = 2;
        line.startColor = line.endColor = new Color(1f, 1f, 1f, 0.95f);
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.enabled = false;

        // A flat white blob where the web sticks.
        GameObject splat = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(splat.GetComponent<Collider>());
        splat.name = "WebSplat";
        splat.transform.localScale = Vector3.one * 0.22f;
        splat.GetComponent<MeshRenderer>().sharedMaterial = UnlitMaterials.Opaque(null, new Color(0.95f, 0.95f, 0.97f));
        splat.SetActive(false);

        var view = new View { netId = netId, player = player, line = line, splat = splat };
        views[netId] = view;
        return view;
    }
}

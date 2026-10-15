using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ItemPack.Items;

/// <summary>
/// Halo-style scope: two zoom levels (2x, then 10x with the scroll wheel or middle mouse button), mouse
/// sensitivity that scales with the zoom, and the scope overlay: black surround, crosshair with range
/// ticks, zoom readout, range finder and rounds left in the magazine.
/// </summary>
internal static class SniperScope
{
    private static float baseFov = 60f;
    private static int zoomLevel;
    private static float rangeMeters = -1f;
    private static Texture2D mask;
    private static GUIStyle textStyle;

    private static readonly Color Hud = new(0.62f, 0.93f, 1f, 0.9f);

    public static bool IsScoped
    {
        get
        {
            PlayerInventory inventory = GameManager.LocalPlayerInventory;
            return inventory != null && inventory.IsInFirstPersonAim
                && SniperRifleItem.IsSniper(inventory.GetEffectivelyEquippedItem(ignoreEquipmentHiding: true));
        }
    }

    public static float CurrentZoom => zoomLevel == 0 ? SniperRifleItem.ZoomLow.Value : SniperRifleItem.ZoomHigh.Value;

    /// <summary>Field of view that magnifies the normal view by <paramref name="zoom"/> times.</summary>
    public static float FovFor(float zoom) =>
        2f * Mathf.Atan(Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, zoom)) * Mathf.Rad2Deg;

    /// <summary>Remembers the unzoomed field of view (called just before scoping in).</summary>
    public static void CaptureBaseFov()
    {
        // The first-person camera's unzoomed view is the player's field of view setting.
        float setting = GameSettings.All.Graphics.FieldOfView;
        if (setting > 20f)
        {
            baseFov = setting;
        }
        else if (GameManager.Camera != null && GameManager.Camera.fieldOfView > 20f)
        {
            baseFov = GameManager.Camera.fieldOfView;
        }
    }

    public static void OnScopedIn()
    {
        zoomLevel = 0;
        ApplyZoom(SniperRifleItem.ZoomInDuration);
        ItemAudio.Play(SniperSounds.ZoomIn, ItemAudio.SfxBus, SniperRifleItem.SoundVolume.Value * 0.7f);
    }

    public static void OnScopedOut()
    {
        ItemAudio.Play(SniperSounds.ZoomOut, ItemAudio.SfxBus, SniperRifleItem.SoundVolume.Value * 0.7f);
    }

    /// <summary>Scoped mouse sensitivity: proportional to the zoom, like Halo.</summary>
    public static float SensitivityFactor(ItemSettings settings)
    {
        if (!IsScoped)
        {
            return settings.RailgunZoomCameraSensitivityFactor;
        }
        float ratio = Mathf.Tan(FovFor(CurrentZoom) * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad);
        return ratio * SniperRifleItem.ScopedSensitivity.Value;
    }

    public static void Tick()
    {
        bool scoped = IsScoped;
        HideHeldRifle(scoped);
        if (!scoped)
        {
            rangeMeters = -1f;
            return;
        }
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            int wanted = zoomLevel;
            if (scroll > 0.1f)
            {
                wanted = 1;
            }
            else if (scroll < -0.1f)
            {
                wanted = 0;
            }
            if (mouse.middleButton.wasPressedThisFrame)
            {
                wanted = 1 - zoomLevel;
            }
            if (wanted != zoomLevel)
            {
                zoomLevel = wanted;
                ApplyZoom(0.06f);
                ItemAudio.Play(wanted == 1 ? SniperSounds.ZoomIn : SniperSounds.ZoomOut, ItemAudio.SfxBus, SniperRifleItem.SoundVolume.Value * 0.7f);
            }
        }

        Camera cam = GameManager.Camera;
        rangeMeters = cam != null && Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit,
            SniperRifleItem.MaxRange.Value, GameManager.LayerSettings.GunHittablesMask, QueryTriggerInteraction.Ignore)
            ? hit.distance
            : -1f;
    }

    private static void ApplyZoom(float duration)
    {
        if (CameraModuleController.TryGetFirstPersonModule(out FirstPersonCameraModule firstPerson))
        {
            firstPerson.AnimateFovOffsetOverTime(FovFor(CurrentZoom), duration, BMath.EaseOut);
        }
    }

    private static readonly List<Renderer> hiddenRenderers = new();

    /// <summary>
    /// The game hides your body in first person but not what you're holding, and a rifle this size sits right
    /// in front of the scope camera. Hide it locally while scoped (other players still see it).
    /// </summary>
    private static void HideHeldRifle(bool scoped)
    {
        if (scoped && hiddenRenderers.Count == 0 && GameManager.LocalPlayerInfo != null)
        {
            foreach (Renderer renderer in GameManager.LocalPlayerInfo.RightHandEquipmentSwitcher.GetComponentsInChildren<Renderer>())
            {
                if (renderer.enabled)
                {
                    renderer.enabled = false;
                    hiddenRenderers.Add(renderer);
                }
            }
        }
        else if (!scoped && hiddenRenderers.Count > 0)
        {
            foreach (Renderer renderer in hiddenRenderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }
            hiddenRenderers.Clear();
        }
    }

    // ---- overlay ----

    public static void OnGUI()
    {
        if (!IsScoped || Event.current.type != EventType.Repaint)
        {
            return;
        }
        float w = Screen.width;
        float h = Screen.height;
        float diameter = h * 0.96f;
        float cx = w * 0.5f;
        float cy = h * 0.5f;
        float r = diameter * 0.5f;
        float px = Mathf.Max(1f, h / 540f); // line thickness

        // Black surround with a round lens.
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0f, 0f, cx - r + 1f, h), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx + r - 1f, 0f, w - (cx + r) + 1f, h), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0f, 0f, w, cy - r + 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0f, cy + r - 1f, w, h - (cy + r) + 1f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(cx - r, cy - r, diameter, diameter), Mask);

        // Crosshair: fine lines across the lens with a gap in the middle, thicker toward the edges.
        float gap = h * 0.018f;
        GUI.color = Hud;
        Line(cx - r, cy - px * 0.5f, r - gap, px);                 // left
        Line(cx + gap, cy - px * 0.5f, r - gap, px);               // right
        Line(cx - px * 0.5f, cy + gap, px, r - gap);               // down
        Line(cx - px * 0.5f, cy - r * 0.45f, px, r * 0.45f - gap); // up (short, like Halo)
        Line(cx - r, cy - px * 1.5f, r * 0.35f, px * 3f);           // heavy outer posts
        Line(cx + r * 0.65f, cy - px * 1.5f, r * 0.35f, px * 3f);
        Line(cx - px * 1.5f, cy + r * 0.65f, px * 3f, r * 0.35f);
        // Range ticks.
        for (int i = 1; i <= 6; i++)
        {
            float d = gap + i * (r * 0.5f - gap) / 6f;
            float len = (i % 3 == 0 ? 0.018f : 0.01f) * h;
            Line(cx + d - px * 0.5f, cy - len * 0.5f, px, len);
            Line(cx - d - px * 0.5f, cy - len * 0.5f, px, len);
            Line(cx - len * 0.5f, cy + d - px * 0.5f, len, px);
        }
        Line(cx - px, cy - px, px * 2f, px * 2f); // center dot

        // Readouts.
        textStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        textStyle.fontSize = Mathf.RoundToInt(h * 0.024f);
        textStyle.normal.textColor = Hud;
        GUI.color = Color.white;
        GUI.Label(new Rect(cx - 200f, cy - r * 0.62f, 400f, h * 0.04f), $"{CurrentZoom:0.#}X", textStyle);
        string range = rangeMeters >= 0f ? $"{rangeMeters:0.0} M" : "---.- M";
        GUI.Label(new Rect(cx + r * 0.12f, cy + h * 0.02f, 300f, h * 0.04f), range, textStyle);
        GUI.Label(new Rect(cx - 200f, cy + r * 0.72f, 400f, h * 0.04f), SniperRifleItem.AmmoText(), textStyle);
    }

    /// <summary>0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>, smooth in between (Mathf.SmoothStep interpolates values instead).</summary>
    private static float Edge(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    private static void Line(float x, float y, float width, float height) =>
        GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);

    /// <summary>Square texture: clear inside the lens circle, black outside, with a soft dark rim.</summary>
    private static Texture2D Mask
    {
        get
        {
            if (mask != null)
            {
                return mask;
            }
            const int size = 1024;
            mask = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false) { name = "SniperScopeMask", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float vignette = Edge(0.82f, 1f, dist) * 0.55f; // lens darkens toward its edge
                    float outside = Edge(0.985f, 1f, dist);
                    byte a = (byte)(Mathf.Clamp01(Mathf.Max(vignette, outside)) * 255f);
                    pixels[y * size + x] = new Color32(0, 0, 0, a);
                }
            }
            mask.SetPixels32(pixels);
            mask.Apply();
            return mask;
        }
    }
}

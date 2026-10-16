using System.IO;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// The "EMERGENCY MEETING!" screen: a red sunburst band across the middle of the screen with big outlined text that
/// pops in, held for a couple of seconds, then faded out. A player can put their own picture in the item's folder
/// (emergency_meeting.png) to show instead; nothing from Among Us itself is shipped.
/// </summary>
internal static class MeetingOverlay
{
    private const float Duration = 2.6f;
    private const float FadeIn = 0.12f;
    private const float FadeOut = 0.45f;

    private static float shownAt = -100f;
    private static string callerName;
    private static Texture2D custom;
    private static Texture2D burst;
    private static GUIStyle title, subtitle;

    public static void LoadCustomImage(string directory)
    {
        string path = Path.Combine(directory, "emergency_meeting.png");
        if (!File.Exists(path))
        {
            return;
        }
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontUnloadUnusedAsset };
        if (texture.LoadImage(File.ReadAllBytes(path)))
        {
            custom = texture;
            Plugin.Log.LogInfo($"Emergency Meeting: using {path}.");
        }
    }

    public static void Show(string caller)
    {
        callerName = caller;
        shownAt = Time.unscaledTime;
    }

    public static void Hide() => shownAt = -100f;

    public static void Draw()
    {
        float t = Time.unscaledTime - shownAt;
        if (t < 0f || t > Duration || Event.current.type != EventType.Repaint)
        {
            return;
        }
        float alpha = Mathf.Clamp01(t / FadeIn) * Mathf.Clamp01((Duration - t) / FadeOut);
        float w = Screen.width, h = Screen.height;
        Color old = GUI.color;

        // The whole screen goes a little red.
        GUI.color = new Color(0.45f, 0f, 0f, 0.45f * alpha);
        GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);

        if (custom != null)
        {
            GUI.color = new Color(1f, 1f, 1f, alpha);
            float scale = Mathf.Min(w / custom.width, h / custom.height);
            float cw = custom.width * scale, ch = custom.height * scale;
            GUI.DrawTexture(new Rect((w - cw) * 0.5f, (h - ch) * 0.5f, cw, ch), custom);
            GUI.color = old;
            return;
        }

        // A band across the middle that opens up from a line.
        float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.18f));
        float bandHeight = h * 0.38f * open;
        float bandTop = (h - bandHeight) * 0.5f;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(new Rect(0, bandTop, w, bandHeight), Burst, ScaleMode.ScaleAndCrop);
        float stripe = Mathf.Max(3f, h * 0.008f);
        GUI.color = new Color(1f, 0.95f, 0.85f, alpha);
        GUI.DrawTexture(new Rect(0, bandTop - stripe, w, stripe), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0, bandTop + bandHeight, w, stripe), Texture2D.whiteTexture);

        // The words pop in big and settle with a little bounce.
        float pop = Mathf.Clamp01((t - 0.08f) / 0.28f);
        float size = pop <= 0f ? 0f : 1f + 0.6f * (1f - pop) * (1f - pop) - Mathf.Sin(pop * Mathf.PI) * 0.06f;
        if (size > 0f)
        {
            title ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.BoldAndItalic, wordWrap = false };
            subtitle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
            title.fontSize = Mathf.RoundToInt(Mathf.Min(h * 0.11f, w * 0.065f) * size);
            subtitle.fontSize = Mathf.RoundToInt(h * 0.036f);
            var titleRect = new Rect(0, h * 0.5f - h * 0.13f, w, h * 0.17f);
            Outlined(titleRect, "EMERGENCY MEETING!", title, Mathf.Max(2f, h * 0.006f * size), alpha);
            if (t > 0.35f && !string.IsNullOrEmpty(callerName))
            {
                float subAlpha = alpha * Mathf.Clamp01((t - 0.35f) / 0.2f);
                Outlined(new Rect(0, h * 0.5f + h * 0.07f, w, h * 0.06f), $"{callerName} called a meeting", subtitle, Mathf.Max(1.5f, h * 0.003f), subAlpha);
            }
        }
        GUI.color = old;
    }

    private static void Outlined(Rect rect, string text, GUIStyle style, float outline, float alpha)
    {
        GUI.color = new Color(0f, 0f, 0f, alpha);
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI / 6f;
            GUI.Label(new Rect(rect.x + Mathf.Cos(a) * outline, rect.y + Mathf.Sin(a) * outline, rect.width, rect.height), text, style);
        }
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(rect, text, style);
    }

    /// <summary>Red rays bursting from the middle, darker toward the edges.</summary>
    private static Texture2D Burst
    {
        get
        {
            if (burst == null)
            {
                const int W = 512, H = 192;
                burst = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
                var pixels = new Color32[W * H];
                var light = new Color(0.86f, 0.1f, 0.1f);
                var dark = new Color(0.62f, 0.04f, 0.05f);
                for (int y = 0; y < H; y++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        float dx = (x - W * 0.5f) / (W * 0.5f), dy = (y - H * 0.5f) / (H * 0.5f) * (H / (float)W) * 2f;
                        float angle = Mathf.Atan2(dy, dx);
                        bool ray = Mathf.Repeat(angle / (Mathf.PI * 2f) * 24f, 1f) < 0.5f;
                        float fade = 1f - 0.45f * Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                        Color c = (ray ? light : dark) * fade;
                        c.a = 0.94f;
                        pixels[y * W + x] = c;
                    }
                }
                burst.SetPixels32(pixels);
                burst.Apply();
            }
            return burst;
        }
    }
}

/// <summary>The meeting klaxon (or the player's own emergency_klaxon file): three brassy rising alarm blasts.</summary>
internal static class MeetingSounds
{
    private static FMOD.Sound klaxon;

    public static void PlayKlaxon(float volume)
    {
        if (!klaxon.hasHandle())
        {
            klaxon = ItemAudio.LoadOrSynthesize(MeetingItem.Instance, "emergency_klaxon", () =>
            {
                var s = new Synth(2.1f, 171);
                for (int i = 0; i < 3; i++)
                {
                    float start = i * 0.62f;
                    // Each blast swoops up and holds, with a buzzy edge (many harmonics) and a lower voice under it.
                    s.Tone(t => Mathf.Lerp(330f, 560f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.22f))) * (1f + 0.01f * Mathf.Sin(t * 40f)),
                        Synth.Env(0.02f, 0.36f, 0.12f), 1f, harmonics: 8, start: start);
                    s.Tone(t => Mathf.Lerp(165f, 280f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.22f))),
                        Synth.Env(0.02f, 0.36f, 0.12f), 0.6f, harmonics: 6, start: start);
                }
                s.Echo(0.09f, 0.25f, 2);
                // A little grit: soft clipping.
                for (int i = 0; i < s.Samples.Length; i++)
                {
                    s.Samples[i] = (float)System.Math.Tanh(s.Samples[i] * 1.6f);
                }
                return s.Normalized(0.85f);
            });
        }
        ItemAudio.Play(klaxon, ItemAudio.SfxBus, volume);
    }
}

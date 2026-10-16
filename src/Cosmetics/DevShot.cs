#if DEVTOOLS
using System.IO;
using UnityEngine;

namespace ItemPack;

/// <summary>Dev-only: renders the local player from the front (and sides) to BepInEx\shots\*.png.</summary>
internal static class DevShot
{
    public static void Take()
    {
        PlayerMovement player = GameManager.LocalPlayerMovement;
        Camera main = Camera.main;
        if (player == null || main == null)
        {
            Plugin.Log.LogWarning("Shot: no local player or camera.");
            return;
        }
        string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "shots");
        Directory.CreateDirectory(dir);
        Vector3 focus = player.transform.position + Vector3.up * 0.8f;
        var go = new GameObject("DevShotCamera");
        Camera cam = go.AddComponent<Camera>();
        cam.CopyFrom(main);
        cam.enabled = false;
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.05f;
        var rt = new RenderTexture(1280, 1280, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        // Angles around the player: 0 = in front of the player, 180 = behind.
        int n = 0;
        foreach (float angle in new[] { 0f, 35f, -35f, 90f })
        {
            Vector3 dirFromPlayer = Quaternion.AngleAxis(angle, Vector3.up) * player.transform.forward;
            go.transform.position = focus + dirFromPlayer * 3.6f + Vector3.up * 0.25f;
            go.transform.LookAt(focus);
            cam.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(dir, $"front_{n++}_{angle:0}.png"), tex.EncodeToPNG());
            Object.Destroy(tex);
        }
        cam.targetTexture = null;
        rt.Release();
        Object.Destroy(go);
        Plugin.Log.LogInfo("Shots written to " + dir);
    }
}
#endif

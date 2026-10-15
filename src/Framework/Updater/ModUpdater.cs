using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace ItemPack;

/// <summary>
/// "Check for updates": asks GitHub for the pack's latest release; if it's newer than this one, downloads the
/// release zip and installs it (<see cref="UpdateInstaller"/>). The new version runs after a restart.
/// </summary>
internal static class ModUpdater
{
    public const string Repository = "eodomo/super-battle-golf-item-pack";
    private const string LatestReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";

    public static bool Busy { get; private set; }
    public static bool RestartNeeded { get; private set; }

    /// <summary>Short status for the settings button, e.g. "Up to date (v0.2.0)".</summary>
    public static string Status { get; private set; } = "Check for updates";

    public static event Action StatusChanged;

    public static Version CurrentVersion => UpdateInstaller.ParseVersion(Plugin.Version);

    private static string PluginDir => Path.GetDirectoryName(typeof(ModUpdater).Assembly.Location);

    /// <summary>Removes files left over from the previous update (they were in use then).</summary>
    public static void CleanupAfterUpdate()
    {
        try
        {
            int deleted = UpdateInstaller.CleanupOldFiles(PluginDir);
            if (deleted > 0)
            {
                Plugin.Log.LogInfo($"Removed {deleted} files left over from the last update.");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Couldn't clean up after the last update: {e.Message}");
        }
    }

    public static void CheckAndInstall(MonoBehaviour host)
    {
        if (Busy || RestartNeeded)
        {
            return;
        }
        host.StartCoroutine(CheckAndInstallRoutine());
    }

    private static IEnumerator CheckAndInstallRoutine()
    {
        Busy = true;
        SetStatus("Checking for updates...");
        UpdateInstaller.Release release = null;
        using (UnityWebRequest request = UnityWebRequest.Get(LatestReleaseApi))
        {
            request.SetRequestHeader("User-Agent", "SuperBattleGolfItemPack/" + Plugin.Version);
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            request.timeout = 20;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail(request.responseCode == 404 ? "No releases found" : "Couldn't reach GitHub", $"{request.responseCode} {request.error}");
                yield break;
            }
            try
            {
                release = UpdateInstaller.ParseLatestRelease(request.downloadHandler.text);
            }
            catch (Exception e)
            {
                Fail("Couldn't read the release", e.Message);
                yield break;
            }
        }

        if (release.Version <= CurrentVersion)
        {
            Busy = false;
            SetStatus($"Up to date (v{CurrentVersion})");
            yield break;
        }

        byte[] zip;
        using (UnityWebRequest download = UnityWebRequest.Get(release.ZipUrl))
        {
            download.SetRequestHeader("User-Agent", "SuperBattleGolfItemPack/" + Plugin.Version);
            download.timeout = 300;
            UnityWebRequestAsyncOperation operation = download.SendWebRequest();
            while (!operation.isDone)
            {
                SetStatus($"Downloading {release.Tag}... {Mathf.RoundToInt(download.downloadProgress * 100f)}%");
                yield return null;
            }
            if (download.result != UnityWebRequest.Result.Success)
            {
                Fail("Download failed", $"{download.responseCode} {download.error}");
                yield break;
            }
            zip = download.downloadHandler.data;
        }

        try
        {
            var written = UpdateInstaller.Install(zip, PluginDir);
            Plugin.Log.LogInfo($"Installed {release.Tag}: {written.Count} files.");
        }
        catch (Exception e)
        {
            Fail("Install failed", e.ToString());
            yield break;
        }
        Busy = false;
        RestartNeeded = true;
        SetStatus($"Updated to {release.Tag}! Restart the game");
    }

    private static void Fail(string status, string detail)
    {
        Plugin.Log.LogWarning($"Update check: {status}: {detail}");
        Busy = false;
        SetStatus(status + " - click to retry");
    }

    private static void SetStatus(string status)
    {
        if (Status == status)
        {
            return;
        }
        Status = status;
        StatusChanged?.Invoke();
    }
}

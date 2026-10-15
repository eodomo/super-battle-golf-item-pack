using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ItemPack;

/// <summary>
/// Installs a release zip over the running pack. Plain .NET (no Unity), so it can be tested outside the game.
///
/// Only files under BepInEx/plugins/ItemPack/ in the zip are installed; BepInEx itself and anything the player
/// added (e.g. their own sound files) are left alone. The loaded ItemPack.dll can't be overwritten while the
/// game runs, so it is renamed to *.old first (BepInEx only loads *.dll) and the new one written next to it;
/// the update takes effect on the next launch, which also deletes the *.old files.
/// </summary>
internal static class UpdateInstaller
{
    public const string PluginFolderInZip = "BepInEx/plugins/ItemPack/";
    public const string AssemblyFileName = "ItemPack.dll";

    public class Release
    {
        public string Tag;
        public Version Version;
        public string ZipUrl;
    }

    /// <summary>Reads tag_name and the pack zip's download URL from GitHub's "latest release" JSON.</summary>
    public static Release ParseLatestRelease(string json)
    {
        Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
        if (!tag.Success)
        {
            throw new FormatException("No tag_name in the release information.");
        }
        string zipUrl = null;
        foreach (Match url in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
        {
            string candidate = url.Groups[1].Value;
            if (candidate.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(candidate).StartsWith("SuperBattleGolfItemPack", StringComparison.OrdinalIgnoreCase))
            {
                zipUrl = candidate;
                break;
            }
        }
        if (zipUrl == null)
        {
            throw new FormatException($"Release {tag.Groups[1].Value} has no SuperBattleGolfItemPack zip.");
        }
        return new Release { Tag = tag.Groups[1].Value, Version = ParseVersion(tag.Groups[1].Value), ZipUrl = zipUrl };
    }

    /// <summary>"v1.2.3" or "1.2.3" (anything after a '-' or '+' is ignored).</summary>
    public static Version ParseVersion(string text)
    {
        string clean = text.Trim().TrimStart('v', 'V');
        int cut = clean.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0)
        {
            clean = clean.Substring(0, cut);
        }
        Version version = Version.Parse(clean);
        // Normalize "1.2" to 1.2.0 so it compares equal to "1.2.0".
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    /// <summary>Installs the pack's files from <paramref name="zipBytes"/> into <paramref name="pluginDir"/>; returns the files written.</summary>
    public static List<string> Install(byte[] zipBytes, string pluginDir)
    {
        string root = Path.GetFullPath(pluginDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var written = new List<string>();
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);

        // Check the whole zip before touching anything.
        var entries = new List<(ZipArchiveEntry entry, string target)>();
        bool hasAssembly = false;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(PluginFolderInZip, StringComparison.OrdinalIgnoreCase) || name.EndsWith("/"))
            {
                continue; // BepInEx itself, folders
            }
            string relative = name.Substring(PluginFolderInZip.Length);
            string target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Refusing to write outside the plugin folder: {entry.FullName}");
            }
            hasAssembly |= relative.Equals(AssemblyFileName, StringComparison.OrdinalIgnoreCase);
            entries.Add((entry, target));
        }
        if (!hasAssembly)
        {
            throw new InvalidDataException($"The download doesn't contain {PluginFolderInZip}{AssemblyFileName}.");
        }

        foreach ((ZipArchiveEntry entry, string target) in entries)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (File.Exists(target))
            {
                // Files in use (the loaded DLL) can be renamed but not overwritten.
                File.Move(target, $"{target}.{DateTime.UtcNow.Ticks}.old");
            }
            using (Stream input = entry.Open())
            using (FileStream output = File.Create(target))
            {
                input.CopyTo(output);
            }
            written.Add(target);
        }
        return written;
    }

    /// <summary>Deletes leftovers from earlier updates (called at startup, when they're no longer loaded).</summary>
    public static int CleanupOldFiles(string pluginDir)
    {
        int deleted = 0;
        if (!Directory.Exists(pluginDir))
        {
            return 0;
        }
        foreach (string file in Directory.GetFiles(pluginDir, "*.old", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception)
            {
                // still in use (e.g. a second game instance): try again next launch
            }
        }
        return deleted;
    }
}

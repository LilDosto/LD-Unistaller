using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using LDUninstaller.Core.Models;
using Microsoft.Win32;

namespace LDUninstaller.Core.Discovery
{
    public class SteamFactory : IUninstallerFactory
    {
        public string SourceName => "Steam Games";

        public IEnumerable<ApplicationUninstallerEntry> GetUninstallerEntries()
        {
            var results = new List<ApplicationUninstallerEntry>();

            try
            {
                string? steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
                {
                    steamPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string;
                }

                if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
                    return results;

                var libraryFolders = new List<string> { steamPath };

                // Parse libraryfolders.vdf
                string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdfPath))
                {
                    string content = File.ReadAllText(vdfPath);
                    var matches = Regex.Matches(content, @"""path""\s+""([^""]+)""");
                    foreach (Match m in matches)
                    {
                        string p = m.Groups[1].Value.Replace(@"\\", @"\");
                        if (Directory.Exists(p) && !libraryFolders.Contains(p))
                            libraryFolders.Add(p);
                    }
                }

                // Check all connected drives for SteamLibrary
                try
                {
                    foreach (var d in DriveInfo.GetDrives())
                    {
                        if (!d.IsReady) continue;
                        string extraLib = Path.Combine(d.RootDirectory.FullName, "SteamLibrary");
                        if (Directory.Exists(extraLib) && !libraryFolders.Contains(extraLib, StringComparer.OrdinalIgnoreCase))
                        {
                            libraryFolders.Add(extraLib);
                        }
                    }
                }
                catch { }

                foreach (var lib in libraryFolders)
                {
                    string steamApps = Path.Combine(lib, "steamapps");
                    if (!Directory.Exists(steamApps)) continue;

                    var manifests = Directory.GetFiles(steamApps, "appmanifest_*.acf");
                    foreach (var manifest in manifests)
                    {
                        try
                        {
                            string text = File.ReadAllText(manifest);
                            string? appId = Regex.Match(text, @"""appid""\s+""(\d+)""").Groups[1].Value;
                            string? name = Regex.Match(text, @"""name""\s+""([^""]+)""").Groups[1].Value;
                            string? installDir = Regex.Match(text, @"""installdir""\s+""([^""]+)""").Groups[1].Value;
                            string? sizeOnDiskStr = Regex.Match(text, @"""SizeOnDisk""\s+""(\d+)""").Groups[1].Value;

                            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name))
                                continue;

                            // Skip common steam redistributables
                            if (appId == "228980" || name.Contains("Steamworks Shared"))
                                continue;

                            long sizeOnDisk = 0;
                            if (long.TryParse(sizeOnDiskStr, out var sz))
                                sizeOnDisk = sz;

                            string gamePath = Path.Combine(steamApps, "common", installDir ?? name);

                            var entry = new ApplicationUninstallerEntry
                            {
                                DisplayName = name,
                                RawDisplayName = name,
                                Publisher = "Steam",
                                RegistryKeyName = $"Steam App {appId}",
                                InstallLocation = Directory.Exists(gamePath) ? gamePath : string.Empty,
                                UninstallString = $"steam://uninstall/{appId}",
                                QuietUninstallString = $"steam://uninstall/{appId}",
                                UninstallerKind = UninstallerType.Steam,
                                EstimatedSize = FileSize.FromBytes(sizeOnDisk),
                                IsRegistered = true
                            };

                            results.Add(entry);
                        }
                        catch
                        {
                            // Skip corrupted manifest
                        }
                    }
                }
            }
            catch
            {
                // Steam not installed or inaccessible
            }

            return results;
        }
    }
}

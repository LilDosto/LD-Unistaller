using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LDUninstaller.Core.Models;

namespace LDUninstaller.Core.Discovery
{
    public class DirectoryFactory : IUninstallerFactory
    {
        public string SourceName => "Directory Orphan Scanner";

        private readonly HashSet<string> _knownInstallLocations;

        public DirectoryFactory(IEnumerable<string> knownInstallLocations)
        {
            _knownInstallLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var loc in knownInstallLocations)
            {
                if (string.IsNullOrWhiteSpace(loc)) continue;
                try
                {
                    _knownInstallLocations.Add(Path.GetFullPath(loc).TrimEnd('\\'));
                }
                catch { }
            }
        }

        public IEnumerable<ApplicationUninstallerEntry> GetUninstallerEntries()
        {
            var results = new List<ApplicationUninstallerEntry>();

            var searchRoots = new List<string>();

            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(pf) && Directory.Exists(pf)) searchRoots.Add(pf);

            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(pf86) && Directory.Exists(pf86) && !pf86.Equals(pf, StringComparison.OrdinalIgnoreCase))
                searchRoots.Add(pf86);

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                string localProg = Path.Combine(localAppData, "Programs");
                if (Directory.Exists(localProg)) searchRoots.Add(localProg);
            }

            // Scan all connected drives (e.g. D:\Games, E:\Apps, etc.)
            try
            {
                var drives = DriveInfo.GetDrives();
                string[] candidates = {
                    "Games", "Oyunlar", "Program Files", "Program Files (x86)",
                    "Programs", "Programlar", "Apps", "Yazılımlar",
                    "Epic Games", "Riot Games", "GOG Games", "Ubisoft Games"
                };

                foreach (var d in drives)
                {
                    if (!d.IsReady) continue;
                    string rootPath = d.RootDirectory.FullName;

                    foreach (var c in candidates)
                    {
                        string candidatePath = Path.Combine(rootPath, c);
                        if (Directory.Exists(candidatePath) && !searchRoots.Contains(candidatePath, StringComparer.OrdinalIgnoreCase))
                        {
                            searchRoots.Add(candidatePath);
                        }
                    }
                }
            }
            catch { }

            var systemBlacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Common Files", "Windows Defender", "Windows Defender Advanced Threat Protection",
                "Windows Mail", "Windows Media Player", "Windows NT", "Windows Photo Viewer",
                "Windows Portable Devices", "WindowsPowerShell", "Microsoft", "PackageManagement",
                "Temp", "Packages", "Microsoft Help"
            };

            foreach (var root in searchRoots)
            {
                try
                {
                    var dirs = Directory.GetDirectories(root);
                    foreach (var dir in dirs)
                    {
                        string dirName = Path.GetFileName(dir);
                        if (systemBlacklist.Contains(dirName)) continue;

                        // Check if this path or a subfolder is known
                        if (IsAlreadyRegistered(dir)) continue;

                        // 1. If this directory itself has executables, it is the app!
                        var selfEntry = AnalyzeFolder(dir, dirName);
                        if (selfEntry != null)
                        {
                            results.Add(selfEntry);
                            continue;
                        }

                        // 2. Otherwise, check subdirectories (e.g. Program Files/Vendor/App)
                        var subBlacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            "vc", "redist", "_redist", "support", "directx", "installer",
                            "prerequisites", "engine", "crashreports", "docs", "logs"
                        };

                        try
                        {
                            var subDirs = Directory.GetDirectories(dir);
                            foreach (var sub in subDirs)
                            {
                                string subName = Path.GetFileName(sub);
                                if (subBlacklist.Contains(subName)) continue;
                                if (IsAlreadyRegistered(sub)) continue;

                                var subEntry = AnalyzeFolder(sub, $"{dirName} - {subName}");
                                if (subEntry != null)
                                {
                                    results.Add(subEntry);
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch
                {
                    // Access error on root
                }
            }

            return results;
        }

        private bool IsAlreadyRegistered(string path)
        {
            try
            {
                string normalized = Path.GetFullPath(path).TrimEnd('\\');
                if (_knownInstallLocations.Contains(normalized)) return true;
                foreach (var known in _knownInstallLocations)
                {
                    if (normalized.StartsWith(known + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static ApplicationUninstallerEntry? AnalyzeFolder(string dir, string defaultName)
        {
            try
            {
                var files = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
                if (files.Length == 0) return null;

                // Look for dedicated uninstaller
                string? uninstaller = files.FirstOrDefault(f =>
                {
                    string name = Path.GetFileName(f).ToLowerInvariant();
                    return name.Contains("unins") || name.Contains("uninstall") || name.Contains("setup");
                });

                string mainExe = uninstaller ?? files[0];
                var fileInfo = new FileInfo(mainExe);

                var kind = UninstallerType.SimpleDelete;
                string uninsLower = Path.GetFileName(mainExe).ToLowerInvariant();
                if (uninsLower.StartsWith("unins000")) kind = UninstallerType.InnoSetup;
                else if (uninsLower.Equals("uninstall.exe")) kind = UninstallerType.Nsis;

                return new ApplicationUninstallerEntry
                {
                    DisplayName = defaultName + " [Yetim/Orphaned]",
                    RawDisplayName = defaultName,
                    InstallLocation = dir,
                    UninstallerLocation = Path.GetDirectoryName(mainExe) ?? dir,
                    UninstallerFullFilename = mainExe,
                    UninstallString = uninstaller != null ? $"\"{uninstaller}\"" : string.Empty,
                    DisplayIcon = mainExe,
                    IsOrphaned = true,
                    IsRegistered = false,
                    UninstallerKind = kind,
                    InstallDate = fileInfo.CreationTime
                };
            }
            catch
            {
                return null;
            }
        }
    }
}

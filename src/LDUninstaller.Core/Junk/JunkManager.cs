using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LDUninstaller.Core.Algorithms;
using LDUninstaller.Core.Models;
using Microsoft.Win32;

namespace LDUninstaller.Core.Junk
{
    public class JunkManager
    {
        public static string BackupDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LDUninstaller", "Backups");

        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "for", "with", "app", "application", "software", "corporation", "corp",
            "inc", "ltd", "gmbh", "edition", "desktop", "client", "runtime", "support",
            "installer", "setup", "unins", "uninstall", "windows", "microsoft", "x86", "x64",
            "32bit", "64bit", "package", "driver", "update", "updater", "service"
        };

        public List<IJunkResult> ScanJunk(ApplicationUninstallerEntry entry)
        {
            var results = new List<IJunkResult>();

            // 1. Install Location leftover scanner
            ScanInstallLocation(entry, results);

            // 2. Common drive junk scanner (AppData, LocalAppData, LocalLow, ProgramData, Documents, Saved Games)
            ScanCommonDriveJunk(entry, results);

            // 3. User root dot-folders & config folders (.appname, .config, .cache)
            ScanUserProfileDotFolders(entry, results);

            // 4. Common Files and Temporary setup caches
            ScanCommonFilesAndTemp(entry, results);

            // 5. Multi-drive analysis & Empty parent publisher folder scanner
            ScanAllDrivesForAppAndEmptyParent(entry, results);

            // 6. Software Registry Keys (HKLM, HKCU & WOW6432Node)
            ScanSoftwareRegistryKeys(entry, results);

            // 7. Windows Services & Kernel/System Drivers
            ScanServicesAndDrivers(entry, results);

            // 8. Windows Scheduled Tasks (Görev Zamanlayıcı)
            ScanScheduledTasks(entry, results);

            // 9. Application Launch Paths (App Paths)
            ScanAppPaths(entry, results);

            // 10. Startup Registry Keys (Run & RunOnce)
            ScanStartupRunKeys(entry, results);

            // 11. COM Objects, CLSIDs & Context Menu Shell Extensions
            ScanShellExtensionsAndCom(entry, results);

            // 12. Windows Shell MuiCache
            ScanMuiCache(entry, results);

            // 13. Application Compatibility & AppCompatFlags
            ScanAppCompatFlags(entry, results);

            // 14. Windows Firewall Rules
            ScanFirewallRules(entry, results);

            // 15. Windows Prefetch scanner
            ScanPrefetch(entry, results);

            // 16. Windows Error Reporting / CrashDumps
            ScanWerDumps(entry, results);

            // 17. UserAssist ROT13 execution history scanner
            ScanUserAssist(entry, results);

            // 18. Shortcuts & Recent items (Desktop, Start Menu, Startup, Recent)
            ScanShortcutsAndRecent(entry, results);

            // 19. Windows Installer (MSI) Orphaned Products
            ScanMsiOrphanedProducts(entry, results);

            // 20. Registry Uninstaller Entry Check (if uninstall key still exists)
            ScanRegistryUninstallKey(entry, results);

            // Apply Safety Blacklist filter and adjust Confidence
            foreach (var item in results)
            {
                if (item.Type == JunkType.Directory && SafetyBlacklist.IsDangerousDirectory(item.Path))
                {
                    if (item is FileSystemJunkResult fs)
                    {
                        fs.Confidence = ConfidenceLevel.Bad;
                        fs.Selected = false;
                    }
                }
                else if (item.Type == JunkType.RegistryKey && SafetyBlacklist.IsDangerousRegistryPath(item.Path))
                {
                    if (item is RegistryJunkResult rj)
                    {
                        rj.Confidence = ConfidenceLevel.Bad;
                        rj.Selected = false;
                    }
                }
            }

            return results;
        }

        public List<IJunkResult> ScanMultiple(IEnumerable<ApplicationUninstallerEntry> entries)
        {
            var all = new List<IJunkResult>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                var list = ScanJunk(entry);
                foreach (var j in list)
                {
                    string uniqueKey = $"{j.Type}:{j.Path}";
                    if (j is RegistryJunkResult r && !string.IsNullOrEmpty(r.ValueName))
                        uniqueKey += $":{r.ValueName}";

                    if (!seenPaths.Contains(uniqueKey))
                    {
                        seenPaths.Add(uniqueKey);
                        all.Add(j);
                    }
                }
            }

            return all;
        }

        private static (string cleanName, string cleanPublisher, List<string> tokens) GetMatchTokens(ApplicationUninstallerEntry entry)
        {
            string rawName = entry.DisplayName ?? string.Empty;
            string stripped = Regex.Replace(rawName, @"(\bv?\d+(\.\d+)+|\(x86\)|\(x64\)|\(amd64\)|sürümü|version)", " ", RegexOptions.IgnoreCase);
            string cleanName = Sift4.NormalizeName(stripped).ToLowerInvariant().Trim();
            string cleanPub = Sift4.NormalizeName(entry.Publisher ?? string.Empty).ToLowerInvariant().Trim();

            var tokens = new List<string>();

            if (cleanName.Length >= 2 && !StopWords.Contains(cleanName))
                tokens.Add(cleanName);

            if (cleanPub.Length >= 3 && !StopWords.Contains(cleanPub))
                tokens.Add(cleanPub);

            foreach (var word in cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string w = word.Trim().ToLowerInvariant();
                if (w.Length >= 3 && !StopWords.Contains(w) && !tokens.Contains(w))
                    tokens.Add(w);
            }

            if (!string.IsNullOrWhiteSpace(entry.UninstallerFullFilename))
            {
                try
                {
                    string exeName = Path.GetFileNameWithoutExtension(entry.UninstallerFullFilename).ToLowerInvariant();
                    if (exeName.Length >= 3 && !exeName.Contains("unins") && !exeName.Contains("setup") && !StopWords.Contains(exeName) && !tokens.Contains(exeName))
                        tokens.Add(exeName);
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(entry.InstallLocation))
            {
                try
                {
                    string folderName = Path.GetFileName(entry.InstallLocation.TrimEnd('\\')).ToLowerInvariant();
                    if (folderName.Length >= 3 && !StopWords.Contains(folderName) && !tokens.Contains(folderName))
                        tokens.Add(folderName);
                }
                catch { }
            }

            return (cleanName, cleanPub, tokens);
        }

        private void ScanInstallLocation(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            if (string.IsNullOrWhiteSpace(entry.InstallLocation) || !Directory.Exists(entry.InstallLocation))
                return;

            if (SafetyBlacklist.IsDangerousDirectory(entry.InstallLocation))
                return;

            results.Add(new FileSystemJunkResult
            {
                EntryName = Path.GetFileName(entry.InstallLocation.TrimEnd('\\')),
                Path = entry.InstallLocation,
                IsDirectory = true,
                Confidence = ConfidenceLevel.VeryGood,
                Reason = "Kaldırma sonrası diskte kalan ana kurulum klasörü",
                Selected = true
            });
        }

        private void ScanCommonDriveJunk(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            var (cleanName, cleanPub, tokens) = GetMatchTokens(entry);
            if (tokens.Count == 0) return;

            var targetRoots = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games")
            };

            foreach (var root in targetRoots)
            {
                if (!Directory.Exists(root)) continue;

                try
                {
                    foreach (var dir in Directory.GetDirectories(root))
                    {
                        string dirName = Path.GetFileName(dir).ToLowerInvariant();
                        if (SafetyBlacklist.IsDangerousDirectory(dir)) continue;

                        bool matched = false;
                        ConfidenceLevel conf = ConfidenceLevel.Good;

                        if (dirName.Equals(cleanName, StringComparison.OrdinalIgnoreCase))
                        {
                            matched = true;
                            conf = ConfidenceLevel.VeryGood;
                        }
                        else if (!string.IsNullOrEmpty(cleanPub) && dirName.Equals(cleanPub, StringComparison.OrdinalIgnoreCase))
                        {
                            // Publisher folder: search subdirectories!
                            try
                            {
                                foreach (var sub in Directory.GetDirectories(dir))
                                {
                                    string subName = Path.GetFileName(sub).ToLowerInvariant();
                                    if (tokens.Any(t => subName.Contains(t) || Sift4.GetSimilarity(subName, t) >= 0.75))
                                    {
                                        results.Add(new FileSystemJunkResult
                                        {
                                            EntryName = $"{dirName}\\{subName}",
                                            Path = sub,
                                            IsDirectory = true,
                                            Confidence = ConfidenceLevel.VeryGood,
                                            Reason = "Yayıncı klasörü altındaki uygulama veri kalıntısı",
                                            Selected = true
                                        });
                                    }
                                }
                            }
                            catch { }
                            continue;
                        }
                        else if (tokens.Any(t => dirName.Contains(t) || Sift4.GetSimilarity(dirName, t) >= 0.80))
                        {
                            matched = true;
                            conf = ConfidenceLevel.Good;
                        }

                        if (matched)
                        {
                            results.Add(new FileSystemJunkResult
                            {
                                EntryName = Path.GetFileName(dir),
                                Path = dir,
                                IsDirectory = true,
                                Confidence = conf,
                                Reason = "Kullanıcı verisi ve yapılandırma klasörü kalıntısı",
                                Selected = true
                            });
                        }
                    }
                }
                catch { }
            }
        }

        private void ScanUserProfileDotFolders(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            var (_, _, tokens) = GetMatchTokens(entry);
            if (tokens.Count == 0) return;

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!Directory.Exists(userProfile)) return;

            try
            {
                // %USERPROFILE%\.{token}
                foreach (var dir in Directory.GetDirectories(userProfile, ".*"))
                {
                    string dirName = Path.GetFileName(dir);
                    if (SafetyBlacklist.IsDangerousDirectory(dir)) continue;

                    string stripped = dirName.TrimStart('.').ToLowerInvariant();
                    if (tokens.Any(t => stripped.Equals(t, StringComparison.OrdinalIgnoreCase) || stripped.StartsWith(t, StringComparison.OrdinalIgnoreCase)))
                    {
                        results.Add(new FileSystemJunkResult
                        {
                            EntryName = dirName,
                            Path = dir,
                            IsDirectory = true,
                            Confidence = ConfidenceLevel.VeryGood,
                            Reason = "Kullanıcı profil dizinindeki gizli yapılandırma klasörü (Dot-folder)",
                            Selected = true
                        });
                    }
                }

                // %USERPROFILE%\.config and %USERPROFILE%\.cache
                string[] modernConfigs = { Path.Combine(userProfile, ".config"), Path.Combine(userProfile, ".cache"), Path.Combine(userProfile, ".local", "share") };
                foreach (var parent in modernConfigs)
                {
                    if (!Directory.Exists(parent)) continue;
                    foreach (var dir in Directory.GetDirectories(parent))
                    {
                        string dirName = Path.GetFileName(dir).ToLowerInvariant();
                        if (SafetyBlacklist.IsDangerousDirectory(dir)) continue;

                        if (tokens.Any(t => dirName.Contains(t) || Sift4.GetSimilarity(dirName, t) >= 0.80))
                        {
                            results.Add(new FileSystemJunkResult
                            {
                                EntryName = Path.GetFileName(dir),
                                Path = dir,
                                IsDirectory = true,
                                Confidence = ConfidenceLevel.VeryGood,
                                Reason = "Kullanıcı modern yapılandırma/önbellek klasörü (.config/.cache)",
                                Selected = true
                            });
                        }
                    }
                }
            }
            catch { }
        }

        private void ScanCommonFilesAndTemp(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            var (cleanName, cleanPub, tokens) = GetMatchTokens(entry);
            if (tokens.Count == 0) return;

            // Common Files
            var commonDirs = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Common Files"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Common Files")
            };

            foreach (var common in commonDirs)
            {
                if (!Directory.Exists(common)) continue;
                try
                {
                    foreach (var dir in Directory.GetDirectories(common))
                    {
                        string dirName = Path.GetFileName(dir).ToLowerInvariant();
                        if (SafetyBlacklist.IsDangerousDirectory(dir)) continue;

                        if (!string.IsNullOrEmpty(cleanPub) && dirName.Equals(cleanPub, StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (var sub in Directory.GetDirectories(dir))
                            {
                                string subName = Path.GetFileName(sub).ToLowerInvariant();
                                if (tokens.Any(t => subName.Contains(t) || Sift4.GetSimilarity(subName, t) >= 0.75))
                                {
                                    results.Add(new FileSystemJunkResult
                                    {
                                        EntryName = $"Common Files\\{dirName}\\{subName}",
                                        Path = sub,
                                        IsDirectory = true,
                                        Confidence = ConfidenceLevel.VeryGood,
                                        Reason = "Ortak program bileşenleri ve kütüphane kalıntısı (Common Files)",
                                        Selected = true
                                    });
                                }
                            }
                        }
                        else if (tokens.Any(t => dirName.Contains(t) || Sift4.GetSimilarity(dirName, t) >= 0.80))
                        {
                            results.Add(new FileSystemJunkResult
                            {
                                EntryName = $"Common Files\\{dirName}",
                                Path = dir,
                                IsDirectory = true,
                                Confidence = ConfidenceLevel.Good,
                                Reason = "Ortak program bileşenleri klasörü (Common Files)",
                                Selected = true
                            });
                        }
                    }
                }
                catch { }
            }

            // Temp Setup / Uninstaller Caches
            try
            {
                string tempDir = Path.GetTempPath();
                if (Directory.Exists(tempDir))
                {
                    foreach (var dir in Directory.GetDirectories(tempDir))
                    {
                        string dirName = Path.GetFileName(dir).ToLowerInvariant();
                        if (SafetyBlacklist.IsDangerousDirectory(dir)) continue;

                        if (tokens.Any(t => dirName.Contains(t)))
                        {
                            results.Add(new FileSystemJunkResult
                            {
                                EntryName = dirName,
                                Path = dir,
                                IsDirectory = true,
                                Confidence = ConfidenceLevel.VeryGood,
                                Reason = "Geçici kurulum veya uninstaller önbellek klasörü (Temp)",
                                Selected = true
                            });
                        }
                    }
                }
            }
            catch { }
        }

        private void ScanAllDrivesForAppAndEmptyParent(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            var (_, _, tokens) = GetMatchTokens(entry);

            // Empty publisher folder cleanup
            if (!string.IsNullOrWhiteSpace(entry.InstallLocation))
            {
                try
                {
                    var parent = Directory.GetParent(entry.InstallLocation.TrimEnd('\\'));
                    if (parent != null && parent.Exists && !SafetyBlacklist.IsDangerousDirectory(parent.FullName))
                    {
                        var entries = parent.GetFileSystemInfos();
                        // Check if parent only contains the install location being removed or is completely empty
                        bool isEmptyOrOnlyTarget = entries.Length == 0 ||
                            (entries.Length == 1 && entries[0].FullName.Equals(Path.GetFullPath(entry.InstallLocation).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

                        if (isEmptyOrOnlyTarget)
                        {
                            results.Add(new FileSystemJunkResult
                            {
                                EntryName = parent.Name,
                                Path = parent.FullName,
                                IsDirectory = true,
                                Confidence = ConfidenceLevel.VeryGood,
                                Reason = "Uygulama kaldırıldıktan sonra boş kalan ana yayıncı klasörü",
                                Selected = true
                            });
                        }
                    }
                }
                catch { }
            }

            // Secondary Ready Drives (e.g. D:\, E:\)
            if (tokens.Count > 0)
            {
                try
                {
                    string systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\";

                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                        if (drive.RootDirectory.FullName.Equals(systemRoot, StringComparison.OrdinalIgnoreCase)) continue;

                        string root = drive.RootDirectory.FullName;
                        string[] candidateFolders = { "Games", "Oyunlar", "Program Files", "Programs", "SteamLibrary\\steamapps\\common" };

                        foreach (var cand in candidateFolders)
                        {
                            string target = Path.Combine(root, cand);
                            if (!Directory.Exists(target)) continue;

                            foreach (var sub in Directory.GetDirectories(target))
                            {
                                string subName = Path.GetFileName(sub).ToLowerInvariant();
                                if (SafetyBlacklist.IsDangerousDirectory(sub)) continue;

                                if (tokens.Any(t => subName.Equals(t, StringComparison.OrdinalIgnoreCase) || Sift4.GetSimilarity(subName, t) >= 0.85))
                                {
                                    results.Add(new FileSystemJunkResult
                                    {
                                        EntryName = $"{cand}\\{Path.GetFileName(sub)}",
                                        Path = sub,
                                        IsDirectory = true,
                                        Confidence = ConfidenceLevel.VeryGood,
                                        Reason = $"İkincil disk ({drive.Name.TrimEnd('\\')}) üzerindeki oyun veya uygulama kalıntısı",
                                        Selected = true
                                    });
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private void ScanSoftwareRegistryKeys(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            var (cleanName, cleanPub, tokens) = GetMatchTokens(entry);
            if (tokens.Count == 0) return;

            var hives = new[]
            {
                (Name: "HKCU", Key: Registry.CurrentUser.OpenSubKey("Software")),
                (Name: "HKLM", Key: Registry.LocalMachine.OpenSubKey("SOFTWARE")),
                (Name: "HKLM_32", Key: Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node"))
            };

            foreach (var (hiveName, rootKey) in hives)
            {
                if (rootKey == null) continue;
                using (rootKey)
                {
                    foreach (var subName in rootKey.GetSubKeyNames())
                    {
                        if (SafetyBlacklist.IsDangerousRegistryPath($"{hiveName}\\Software\\{subName}"))
                            continue;

                        string subLower = subName.ToLowerInvariant();

                        if (subLower.Equals(cleanName, StringComparison.OrdinalIgnoreCase) || tokens.Any(t => subLower.Equals(t, StringComparison.OrdinalIgnoreCase)))
                        {
                            results.Add(new RegistryJunkResult
                            {
                                EntryName = subName,
                                Path = $@"{hiveName}\Software\{subName}",
                                Confidence = ConfidenceLevel.VeryGood,
                                Reason = "Kayıt defteri yazılım anahtarı kalıntısı",
                                Selected = true
                            });
                        }
                        else if (!string.IsNullOrEmpty(cleanPub) && (subLower.Equals(cleanPub, StringComparison.OrdinalIgnoreCase) || Sift4.GetSimilarity(subLower, cleanPub) >= 0.80))
                        {
                            // Publisher registry key -> scan app subkeys
                            try
                            {
                                using var pubKey = rootKey.OpenSubKey(subName);
                                if (pubKey != null)
                                {
                                    foreach (var appSub in pubKey.GetSubKeyNames())
                                    {
                                        string appSubLower = appSub.ToLowerInvariant();
                                        if (tokens.Any(t => appSubLower.Contains(t) || Sift4.GetSimilarity(appSubLower, t) >= 0.75))
                                        {
                                            results.Add(new RegistryJunkResult
                                            {
                                                EntryName = $"{subName}\\{appSub}",
                                                Path = $@"{hiveName}\Software\{subName}\{appSub}",
                                                Confidence = ConfidenceLevel.VeryGood,
                                                Reason = "Yayıncı altındaki uygulama kayıt defteri anahtarı kalıntısı",
                                                Selected = true
                                            });
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        else if (tokens.Any(t => Sift4.GetSimilarity(subLower, t) >= 0.85))
                        {
                            results.Add(new RegistryJunkResult
                            {
                                EntryName = subName,
                                Path = $@"{hiveName}\Software\{subName}",
                                Confidence = ConfidenceLevel.Good,
                                Reason = "Eşleşen kayıt defteri yazılım anahtarı",
                                Selected = true
                            });
                        }
                    }
                }
            }
        }

        private void ScanServicesAndDrivers(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            var (_, _, tokens) = GetMatchTokens(entry);
            string installNorm = string.IsNullOrWhiteSpace(entry.InstallLocation) ? "" : Path.GetFullPath(entry.InstallLocation).TrimEnd('\\').ToLowerInvariant();

            try
            {
                using var servicesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
                if (servicesKey == null) return;

                foreach (var svcName in servicesKey.GetSubKeyNames())
                {
                    try
                    {
                        using var svc = servicesKey.OpenSubKey(svcName);
                        if (svc == null) continue;

                        string? imagePath = svc.GetValue("ImagePath")?.ToString();
                        string? displayName = svc.GetValue("DisplayName")?.ToString();
                        string svcLower = svcName.ToLowerInvariant();

                        bool match = false;
                        string matchReason = "";

                        if (!string.IsNullOrEmpty(imagePath))
                        {
                            string cleanImagePath = imagePath.Replace("\"", "").Trim().ToLowerInvariant();
                            if (cleanImagePath.StartsWith(@"\??\"))
                                cleanImagePath = cleanImagePath.Substring(4);

                            // Ignore OS core binaries
                            if (!cleanImagePath.Contains(@"\windows\system32\svchost.exe") &&
                                !cleanImagePath.Contains(@"\windows\system32\services.exe") &&
                                !cleanImagePath.Contains(@"\windows\system32\lsass.exe"))
                            {
                                if (!string.IsNullOrEmpty(installNorm) && cleanImagePath.Contains(installNorm))
                                {
                                    match = true;
                                    matchReason = "Hizmet yolu kaldırılan uygulamanın kurulum klasörünü işaret ediyor";
                                }
                                else if (tokens.Any(t => cleanImagePath.Contains(t) && !cleanImagePath.Contains("windows")))
                                {
                                    match = true;
                                    matchReason = "Hizmet yolu kaldırılan uygulamanın dosyasını işaret ediyor";
                                }
                            }
                        }

                        if (!match && tokens.Count > 0)
                        {
                            if (tokens.Any(t => svcLower.Equals(t, StringComparison.OrdinalIgnoreCase)) ||
                                (!string.IsNullOrEmpty(displayName) && tokens.Any(t => displayName.ToLowerInvariant().Contains(t))))
                            {
                                match = true;
                                matchReason = "Hizmet adı veya açıklaması kaldırılan yazılımla eşleşiyor";
                            }
                        }

                        if (match)
                        {
                            results.Add(new GenericJunkResult
                            {
                                EntryName = svcName,
                                Path = svcName,
                                Type = JunkType.Service,
                                Confidence = ConfidenceLevel.VeryGood,
                                Reason = $"Uygulamaya ait arkaplan Windows Hizmeti ({displayName ?? svcName}) - {matchReason}",
                                Selected = true,
                                CustomAction = () =>
                                {
                                    try
                                    {
                                        var stopInfo = new ProcessStartInfo("sc.exe", $"stop \"{svcName}\"") { CreateNoWindow = true, UseShellExecute = false };
                                        using var pStop = Process.Start(stopInfo);
                                        pStop?.WaitForExit(3000);

                                        var delInfo = new ProcessStartInfo("sc.exe", $"delete \"{svcName}\"") { CreateNoWindow = true, UseShellExecute = false };
                                        using var pDel = Process.Start(delInfo);
                                        pDel?.WaitForExit(3000);

                                        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
                                        baseKey.DeleteSubKeyTree($@"SYSTEM\CurrentControlSet\Services\{svcName}", false);
                                        return true;
                                    }
                                    catch
                                    {
                                        return false;
                                    }
                                }
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void ScanScheduledTasks(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            var (_, cleanPub, tokens) = GetMatchTokens(entry);
            string installNorm = string.IsNullOrWhiteSpace(entry.InstallLocation) ? "" : Path.GetFullPath(entry.InstallLocation).TrimEnd('\\').ToLowerInvariant();

            string tasksDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");
            if (!Directory.Exists(tasksDir)) return;

            try
            {
                foreach (var taskFile in Directory.GetFiles(tasksDir, "*", SearchOption.AllDirectories))
                {
                    // Never touch Microsoft OS internal tasks
                    if (taskFile.Contains(@"\Tasks\Microsoft\Windows", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string relativeTaskPath = taskFile.Substring(tasksDir.Length).Replace('/', '\\');
                    if (!relativeTaskPath.StartsWith("\\"))
                        relativeTaskPath = "\\" + relativeTaskPath;

                    string fileName = Path.GetFileName(taskFile).ToLowerInvariant();
                    bool match = false;
                    string matchReason = "";

                    // 1. Check path/filename tokens
                    if (tokens.Any(t => fileName.Contains(t)) || (!string.IsNullOrEmpty(cleanPub) && relativeTaskPath.ToLowerInvariant().Contains(cleanPub)))
                    {
                        match = true;
                        matchReason = "Görev adı veya klasörü kaldırılan yazılımla eşleşiyor";
                    }

                    // 2. Read task XML command
                    if (!match)
                    {
                        try
                        {
                            string content = File.ReadAllText(taskFile).ToLowerInvariant();
                            if (!string.IsNullOrEmpty(installNorm) && content.Contains(installNorm))
                            {
                                match = true;
                                matchReason = "Görev eylemi kaldırılan uygulamanın kurulum klasöründeki dosyayı çalıştırıyor";
                            }
                            else if (tokens.Any(t => content.Contains($"\\{t}.exe") || content.Contains($"\\{t}updater.exe")))
                            {
                                match = true;
                                matchReason = "Görev komut satırı uygulamanın çalıştırılabilir dosyasını hedefliyor";
                            }
                        }
                        catch { }
                    }

                    if (match)
                    {
                        results.Add(new GenericJunkResult
                        {
                            EntryName = Path.GetFileName(taskFile),
                            Path = relativeTaskPath,
                            Type = JunkType.ScheduledTask,
                            Confidence = ConfidenceLevel.VeryGood,
                            Reason = $"Windows Görev Zamanlayıcı (Scheduled Task) kalıntısı - {matchReason}",
                            Selected = true,
                            CustomAction = () =>
                            {
                                try
                                {
                                    var psi = new ProcessStartInfo("schtasks.exe", $"/Delete /TN \"{relativeTaskPath}\" /F")
                                    {
                                        CreateNoWindow = true,
                                        UseShellExecute = false
                                    };
                                    using var p = Process.Start(psi);
                                    p?.WaitForExit(3000);

                                    if (File.Exists(taskFile))
                                        File.Delete(taskFile);

                                    return true;
                                }
                                catch
                                {
                                    return false;
                                }
                            }
                        });
                    }
                }
            }
            catch { }
        }

        private void ScanAppPaths(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            var (_, _, tokens) = GetMatchTokens(entry);
            string installNorm = string.IsNullOrWhiteSpace(entry.InstallLocation) ? "" : Path.GetFullPath(entry.InstallLocation).TrimEnd('\\').ToLowerInvariant();

            var roots = new[]
            {
                (Hive: "HKLM", Key: Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths")),
                (Hive: "HKCU", Key: Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths")),
                (Hive: "HKLM_32", Key: Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"))
            };

            foreach (var (hive, root) in roots)
            {
                if (root == null) continue;
                using (root)
                {
                    foreach (var sub in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var appKey = root.OpenSubKey(sub);
                            if (appKey == null) continue;

                            string? defaultVal = appKey.GetValue(null)?.ToString();
                            string subLower = sub.ToLowerInvariant();

                            bool match = false;
                            if (!string.IsNullOrEmpty(defaultVal) && !string.IsNullOrEmpty(installNorm) && defaultVal.ToLowerInvariant().Contains(installNorm))
                            {
                                match = true;
                            }
                            else if (tokens.Any(t => subLower.Contains(t)))
                            {
                                match = true;
                            }

                            if (match)
                            {
                                string basePath = hive.StartsWith("HKCU") ? "HKCU" : "HKLM";
                                string path = hive == "HKLM_32" 
                                    ? $@"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\{sub}"
                                    : $@"{basePath}\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{sub}";

                                results.Add(new RegistryJunkResult
                                {
                                    EntryName = sub,
                                    Path = path,
                                    Confidence = ConfidenceLevel.VeryGood,
                                    Reason = "Windows Uygulama Başlatma Yolu (App Path) kalıntısı",
                                    Selected = true
                                });
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        private void ScanStartupRunKeys(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            var (_, _, tokens) = GetMatchTokens(entry);
            string installNorm = string.IsNullOrWhiteSpace(entry.InstallLocation) ? "" : Path.GetFullPath(entry.InstallLocation).TrimEnd('\\').ToLowerInvariant();

            var locations = new[]
            {
                (Hive: "HKLM", Path: @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                (Hive: "HKLM", Path: @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
                (Hive: "HKCU", Path: @"Software\Microsoft\Windows\CurrentVersion\Run"),
                (Hive: "HKCU", Path: @"Software\Microsoft\Windows\CurrentVersion\RunOnce"),
                (Hive: "HKLM", Path: @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run")
            };

            foreach (var loc in locations)
            {
                try
                {
                    var baseKey = loc.Hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                    using var key = baseKey.OpenSubKey(loc.Path);
                    if (key == null) continue;

                    foreach (var valName in key.GetValueNames())
                    {
                        string? valData = key.GetValue(valName)?.ToString();
                        if (string.IsNullOrEmpty(valData)) continue;

                        string valLower = valData.ToLowerInvariant();
                        bool match = false;

                        if (!string.IsNullOrEmpty(installNorm) && valLower.Contains(installNorm))
                            match = true;
                        else if (tokens.Any(t => valName.ToLowerInvariant().Contains(t) || valLower.Contains($"\\{t}.exe")))
                            match = true;

                        if (match)
                        {
                            results.Add(new RegistryJunkResult
                            {
                                EntryName = valName,
                                Path = $@"{loc.Hive}\{loc.Path}",
                                ValueName = valName,
                                Confidence = ConfidenceLevel.VeryGood,
                                Reason = "Başlangıçta otomatik çalıştırma kayıt defteri girdisi (Run/RunOnce)",
                                Selected = true
                            });
                        }
                    }
                }
                catch { }
            }
        }

        private void ScanShellExtensionsAndCom(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            var (_, _, tokens) = GetMatchTokens(entry);
            string installNorm = string.IsNullOrWhiteSpace(entry.InstallLocation) ? "" : Path.GetFullPath(entry.InstallLocation).TrimEnd('\\').ToLowerInvariant();

            // 1. ContextMenuHandlers
            string[] contextMenuKeys =
            {
                @"*\shellex\ContextMenuHandlers",
                @"Directory\shellex\ContextMenuHandlers",
                @"Directory\Background\shellex\ContextMenuHandlers",
                @"Drive\shellex\ContextMenuHandlers",
                @"Folder\shellex\ContextMenuHandlers"
            };

            foreach (var cmKey in contextMenuKeys)
            {
                try
                {
                    using var root = Registry.ClassesRoot.OpenSubKey(cmKey);
                    if (root == null) continue;

                    foreach (var sub in root.GetSubKeyNames())
                    {
                        string subLower = sub.ToLowerInvariant();
                        if (tokens.Any(t => subLower.Contains(t)))
                        {
                            results.Add(new RegistryJunkResult
                            {
                                EntryName = sub,
                                Path = $@"HKCR\{cmKey}\{sub}",
                                Confidence = ConfidenceLevel.VeryGood,
                                Reason = "Windows Gezgini sağ tık içerik menüsü eklentisi (Shell Extension)",
                                Selected = true
                            });
                        }
                    }
                }
                catch { }
            }

            // 2. Approved Shell Extensions
            try
            {
                using var approved = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved");
                if (approved != null)
                {
                    foreach (var valName in approved.GetValueNames())
                    {
                        string? desc = approved.GetValue(valName)?.ToString();
                        if (!string.IsNullOrEmpty(desc) && tokens.Any(t => desc.ToLowerInvariant().Contains(t)))
                        {
                            results.Add(new RegistryJunkResult
                            {
                                EntryName = desc,
                                Path = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved",
                                ValueName = valName,
                                Confidence = ConfidenceLevel.VeryGood,
                                Reason = "Onaylanmış kabuk uzantısı listesi girdisi (Shell Extension Approved)",
                                Selected = true
                            });
                        }
                    }
                }
            }
            catch { }

            // 3. Orphaned CLSIDs pointing into InstallLocation
            if (!string.IsNullOrEmpty(installNorm))
            {
                try
                {
                    using var clsidKey = Registry.ClassesRoot.OpenSubKey("CLSID");
                    if (clsidKey != null)
                    {
                        foreach (var clsid in clsidKey.GetSubKeyNames())
                        {
                            try
                            {
                                using var sub = clsidKey.OpenSubKey(clsid);
                                if (sub == null) continue;

                                foreach (var serverKey in new[] { "InprocServer32", "LocalServer32" })
                                {
                                    using var sKey = sub.OpenSubKey(serverKey);
                                    if (sKey == null) continue;

                                    string? serverPath = sKey.GetValue(null)?.ToString();
                                    if (!string.IsNullOrEmpty(serverPath) && serverPath.ToLowerInvariant().Contains(installNorm))
                                    {
                                        results.Add(new RegistryJunkResult
                                        {
                                            EntryName = clsid,
                                            Path = $@"HKCR\CLSID\{clsid}",
                                            Confidence = ConfidenceLevel.VeryGood,
                                            Reason = "Kaldırılan uygulamanın DLL/EXE dosyasına bağlı artık COM Nesnesi (CLSID)",
                                            Selected = true
                                        });
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }

        private void ScanMuiCache(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            string installNorm = string.IsNullOrWhiteSpace(entry.InstallLocation) ? "" : Path.GetFullPath(entry.InstallLocation).TrimEnd('\\').ToLowerInvariant();
            if (string.IsNullOrEmpty(installNorm)) return;

            string[] muiPaths =
            {
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
                @"Local Settings\Software\Microsoft\Windows\Shell\MuiCache"
            };

            foreach (var mui in muiPaths)
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(mui);
                    if (key == null) continue;

                    foreach (var val in key.GetValueNames())
                    {
                        if (val.ToLowerInvariant().Contains(installNorm))
                        {
                            results.Add(new RegistryJunkResult
                            {
                                EntryName = Path.GetFileName(val),
                                Path = $@"HKCU\{mui}",
                                ValueName = val,
                                Confidence = ConfidenceLevel.VeryGood,
                                Reason = "Windows Shell MuiCache uygulama ve simge önbellek kaydı",
                                Selected = true
                            });
                        }
                    }
                }
                catch { }
            }
        }

        private void ScanAppCompatFlags(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            string installNorm = string.IsNullOrWhiteSpace(entry.InstallLocation) ? "" : Path.GetFullPath(entry.InstallLocation).TrimEnd('\\').ToLowerInvariant();
            if (string.IsNullOrEmpty(installNorm)) return;

            var paths = new[]
            {
                (Hive: "HKCU", Path: @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"),
                (Hive: "HKLM", Path: @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"),
                (Hive: "HKCU", Path: @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store")
            };

            foreach (var p in paths)
            {
                try
                {
                    var baseKey = p.Hive == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                    using var key = baseKey.OpenSubKey(p.Path);
                    if (key == null) continue;

                    foreach (var val in key.GetValueNames())
                    {
                        if (val.ToLowerInvariant().Contains(installNorm))
                        {
                            results.Add(new RegistryJunkResult
                            {
                                EntryName = Path.GetFileName(val),
                                Path = $@"{p.Hive}\{p.Path}",
                                ValueName = val,
                                Confidence = ConfidenceLevel.VeryGood,
                                Reason = "Windows Uyumluluk ve AppCompat geçmiş girdisi",
                                Selected = true
                            });
                        }
                    }
                }
                catch { }
            }
        }

        private void ScanFirewallRules(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            if (string.IsNullOrWhiteSpace(entry.InstallLocation)) return;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules", true);
                if (key == null) return;

                string installNorm = Path.GetFullPath(entry.InstallLocation).TrimEnd('\\').ToLowerInvariant();

                foreach (var valName in key.GetValueNames())
                {
                    string? val = key.GetValue(valName)?.ToString();
                    if (!string.IsNullOrWhiteSpace(val) && val.ToLowerInvariant().Contains(installNorm))
                    {
                        results.Add(new RegistryJunkResult
                        {
                            EntryName = valName,
                            Path = @"HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules",
                            ValueName = valName,
                            Confidence = ConfidenceLevel.VeryGood,
                            Reason = "Uygulamaya ait geçersiz Windows Güvenlik Duvarı kuralı",
                            Selected = true
                        });
                    }
                }
            }
            catch { }
        }

        private void ScanPrefetch(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            try
            {
                string prefetchDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
                if (!Directory.Exists(prefetchDir)) return;

                var (_, _, tokens) = GetMatchTokens(entry);

                foreach (var token in tokens.Take(3))
                {
                    if (token.Length < 3) continue;
                    var pfFiles = Directory.GetFiles(prefetchDir, $"{token.ToUpperInvariant()}*.pf");
                    foreach (var pf in pfFiles)
                    {
                        results.Add(new FileSystemJunkResult
                        {
                            EntryName = Path.GetFileName(pf),
                            Path = pf,
                            IsDirectory = false,
                            Confidence = ConfidenceLevel.VeryGood,
                            Reason = "Windows Prefetch çalıştırma hızlandırma önbellek dosyası",
                            Selected = true
                        });
                    }
                }
            }
            catch { }
        }

        private void ScanWerDumps(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            var (_, _, tokens) = GetMatchTokens(entry);
            if (tokens.Count == 0) return;

            try
            {
                string crashDumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
                if (Directory.Exists(crashDumps))
                {
                    foreach (var file in Directory.GetFiles(crashDumps, "*.dmp"))
                    {
                        string fname = Path.GetFileName(file).ToLowerInvariant();
                        if (tokens.Any(t => fname.Contains(t)))
                        {
                            results.Add(new FileSystemJunkResult
                            {
                                EntryName = Path.GetFileName(file),
                                Path = file,
                                IsDirectory = false,
                                Confidence = ConfidenceLevel.Good,
                                Reason = "Windows Hata Raporlama çökme döküm dosyası (Crash Dump)",
                                Selected = true
                            });
                        }
                    }
                }

                // WER ReportArchive
                string werArchive = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "WER", "ReportArchive");
                if (Directory.Exists(werArchive))
                {
                    foreach (var dir in Directory.GetDirectories(werArchive))
                    {
                        string dname = Path.GetFileName(dir).ToLowerInvariant();
                        if (tokens.Any(t => dname.Contains(t)))
                        {
                            results.Add(new FileSystemJunkResult
                            {
                                EntryName = Path.GetFileName(dir),
                                Path = dir,
                                IsDirectory = true,
                                Confidence = ConfidenceLevel.Good,
                                Reason = "Windows Hata Raporu arşiv klasörü (WER Report)",
                                Selected = true
                            });
                        }
                    }
                }
            }
            catch { }
        }

        private void ScanUserAssist(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            try
            {
                using var uaKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist");
                if (uaKey == null) return;

                var (_, _, tokens) = GetMatchTokens(entry);

                foreach (var sub in uaKey.GetSubKeyNames())
                {
                    using var countKey = uaKey.OpenSubKey($"{sub}\\Count", true);
                    if (countKey == null) continue;

                    foreach (var valName in countKey.GetValueNames())
                    {
                        string decoded = Rot13(valName).ToLowerInvariant();
                        if (tokens.Any(t => decoded.Contains(t)))
                        {
                            results.Add(new RegistryJunkResult
                            {
                                EntryName = Rot13(valName),
                                Path = $@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{sub}\Count",
                                ValueName = valName,
                                Confidence = ConfidenceLevel.VeryGood,
                                Reason = "UserAssist uygulama çalıştırma geçmişi girdisi",
                                Selected = true
                            });
                        }
                    }
                }
            }
            catch { }
        }

        private void ScanShortcutsAndRecent(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            var shortcutDirs = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent")
            };

            var (_, _, tokens) = GetMatchTokens(entry);

            foreach (var dir in shortcutDirs)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (var file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext != ".lnk" && ext != ".url") continue;

                        string fname = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                        if (tokens.Any(t => fname.Contains(t) || Sift4.GetSimilarity(fname, t) >= 0.85))
                        {
                            results.Add(new FileSystemJunkResult
                            {
                                EntryName = Path.GetFileName(file),
                                Path = file,
                                IsDirectory = false,
                                Confidence = ConfidenceLevel.VeryGood,
                                Reason = "Masaüstü, Başlat menüsü veya Son Belgeler kısayol kalıntısı",
                                Selected = true
                            });
                        }
                    }
                }
                catch { }
            }
        }

        private void ScanMsiOrphanedProducts(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            var (cleanName, _, _) = GetMatchTokens(entry);
            if (cleanName.Length < 3) return;

            try
            {
                // HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products
                using var productsKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products");
                if (productsKey != null)
                {
                    foreach (var prodCode in productsKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var installProps = productsKey.OpenSubKey($@"{prodCode}\InstallProperties");
                            if (installProps != null)
                            {
                                string? dispName = installProps.GetValue("DisplayName")?.ToString();
                                string? installLoc = installProps.GetValue("InstallLocation")?.ToString();

                                bool match = false;
                                if (!string.IsNullOrEmpty(dispName) && dispName.Equals(cleanName, StringComparison.OrdinalIgnoreCase))
                                    match = true;
                                else if (!string.IsNullOrWhiteSpace(entry.InstallLocation) && !string.IsNullOrEmpty(installLoc) &&
                                         Path.GetFullPath(installLoc).TrimEnd('\\').Equals(Path.GetFullPath(entry.InstallLocation).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                                    match = true;

                                if (match)
                                {
                                    results.Add(new RegistryJunkResult
                                    {
                                        EntryName = dispName ?? prodCode,
                                        Path = $@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products\{prodCode}",
                                        Confidence = ConfidenceLevel.VeryGood,
                                        Reason = "Windows Installer (MSI) ürün kayıt kalıntısı",
                                        Selected = true
                                    });

                                    results.Add(new RegistryJunkResult
                                    {
                                        EntryName = prodCode,
                                        Path = $@"HKCR\Installer\Products\{prodCode}",
                                        Confidence = ConfidenceLevel.VeryGood,
                                        Reason = "Windows Installer (MSI) ürün referans kalıntısı",
                                        Selected = true
                                    });
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void ScanRegistryUninstallKey(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            if (string.IsNullOrWhiteSpace(entry.RegistryPath)) return;

            try
            {
                RegistryKey? root = null;
                string subPath = entry.RegistryPath;

                if (subPath.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
                {
                    root = Registry.LocalMachine;
                    subPath = subPath.Substring("HKEY_LOCAL_MACHINE\\".Length);
                }
                else if (subPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
                {
                    root = Registry.CurrentUser;
                    subPath = subPath.Substring("HKEY_CURRENT_USER\\".Length);
                }

                if (root != null)
                {
                    using var key = root.OpenSubKey(subPath);
                    if (key != null)
                    {
                        results.Add(new RegistryJunkResult
                        {
                            EntryName = Path.GetFileName(entry.RegistryPath),
                            Path = entry.RegistryPath,
                            Confidence = ConfidenceLevel.VeryGood,
                            Reason = "Kaldırma sonrası sistemde kalan uninstaller kayıt defteri girdisi",
                            Selected = true
                        });
                    }
                }
            }
            catch { }
        }

        public static int CleanJunk(IEnumerable<IJunkResult> items, string backupDir)
        {
            int cleanedCount = 0;
            Directory.CreateDirectory(backupDir);

            foreach (var item in items.Where(x => x.Selected && x.Confidence > ConfidenceLevel.Bad))
            {
                try
                {
                    if (item.Delete(backupDir))
                    {
                        cleanedCount++;
                    }
                }
                catch { }
            }
            return cleanedCount;
        }

        public static string Rot13(string input)
        {
            char[] array = input.ToCharArray();
            for (int i = 0; i < array.Length; i++)
            {
                int number = (int)array[i];
                if (number >= 'a' && number <= 'z')
                {
                    if (number > 'm') number -= 13;
                    else number += 13;
                }
                else if (number >= 'A' && number <= 'Z')
                {
                    if (number > 'M') number -= 13;
                    else number += 13;
                }
                array[i] = (char)number;
            }
            return new string(array);
        }
    }
}

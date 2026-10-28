using System;
using System.Collections.Generic;
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

        public List<IJunkResult> ScanJunk(ApplicationUninstallerEntry entry)
        {
            var results = new List<IJunkResult>();

            // 1. Install Location leftover scanner
            ScanInstallLocation(entry, results);

            // 2. Common drive junk scanner (AppData, LocalAppData, ProgramData, Documents, Saved Games)
            ScanCommonDriveJunk(entry, results);

            // 3. Software Registry Keys (HKLM & HKCU)
            ScanSoftwareRegistryKeys(entry, results);

            // 4. Windows Prefetch scanner
            ScanPrefetch(entry, results);

            // 5. Windows Error Reporting / CrashDumps
            ScanWerDumps(entry, results);

            // 6. Firewall Rules
            ScanFirewallRules(entry, results);

            // 7. UserAssist ROT13 scanner
            ScanUserAssist(entry, results);

            // 8. Shortcuts (Desktop, Start Menu, Quick Launch)
            ScanShortcuts(entry, results);

            // 9. Registry Uninstaller Entry Check (if uninstall key still exists)
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
                    if (!seenPaths.Contains(j.Path))
                    {
                        seenPaths.Add(j.Path);
                        all.Add(j);
                    }
                }
            }

            return all;
        }

        private static (string cleanName, string cleanPublisher, List<string> tokens) GetMatchTokens(ApplicationUninstallerEntry entry)
        {
            string rawName = entry.DisplayName;
            string stripped = Regex.Replace(rawName, @"(\bv?\d+(\.\d+)+|\(x86\)|\(x64\)|\(amd64\)|sürümü|version)", " ", RegexOptions.IgnoreCase);
            string cleanName = Sift4.NormalizeName(stripped).ToLowerInvariant();
            string cleanPub = Sift4.NormalizeName(entry.Publisher).ToLowerInvariant();

            var tokens = new List<string>();
            if (cleanName.Length >= 2) tokens.Add(cleanName);
            if (cleanPub.Length >= 3) tokens.Add(cleanPub);

            foreach (var word in cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length >= 3 && !tokens.Contains(word))
                    tokens.Add(word);
            }

            if (!string.IsNullOrWhiteSpace(entry.UninstallerFullFilename))
            {
                string exeName = Path.GetFileNameWithoutExtension(entry.UninstallerFullFilename).ToLowerInvariant();
                if (exeName.Length >= 3 && !exeName.Contains("unins") && !exeName.Contains("setup") && !tokens.Contains(exeName))
                    tokens.Add(exeName);
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
                EntryName = Path.GetFileName(entry.InstallLocation),
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

        private void ScanPrefetch(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            try
            {
                string prefetchDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
                if (!Directory.Exists(prefetchDir)) return;

                var (cleanName, _, tokens) = GetMatchTokens(entry);

                foreach (var token in tokens.Take(2))
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
                            Reason = "Windows Prefetch çalıştırma önbellek dosyası",
                            Selected = true
                        });
                    }
                }
            }
            catch { }
        }

        private void ScanWerDumps(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            try
            {
                string crashDumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
                if (!Directory.Exists(crashDumps)) return;

                var (_, _, tokens) = GetMatchTokens(entry);

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
            catch { }
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

        private void ScanUserAssist(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            try
            {
                using var uaKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist");
                if (uaKey == null) return;

                var (cleanName, _, tokens) = GetMatchTokens(entry);

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

        private void ScanShortcuts(ApplicationUninstallerEntry entry, List<IJunkResult> results)
        {
            var shortcutDirs = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
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
                                Reason = "Masaüstü veya Başlat menüsü kısayol kalıntısı",
                                Selected = true
                            });
                        }
                    }
                }
                catch { }
            }
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

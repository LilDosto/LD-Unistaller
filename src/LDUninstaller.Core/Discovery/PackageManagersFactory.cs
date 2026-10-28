using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LDUninstaller.Core.Models;

namespace LDUninstaller.Core.Discovery
{
    public class ChocolateyFactory : IUninstallerFactory
    {
        public string SourceName => "Chocolatey";

        public IEnumerable<ApplicationUninstallerEntry> GetUninstallerEntries()
        {
            var results = new List<ApplicationUninstallerEntry>();
            try
            {
                var psi = new ProcessStartInfo("choco.exe", "list -l --limit-output")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return results;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);

                using var reader = new StringReader(output);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[0]))
                    {
                        string id = parts[0].Trim();
                        string version = parts[1].Trim();
                        results.Add(new ApplicationUninstallerEntry
                        {
                            DisplayName = $"{id} (Chocolatey)",
                            RawDisplayName = id,
                            DisplayVersion = version,
                            Publisher = "Chocolatey Package",
                            UninstallString = $"choco uninstall {id} -y",
                            QuietUninstallString = $"choco uninstall {id} -y",
                            UninstallerKind = UninstallerType.Chocolatey,
                            IsRegistered = true
                        });
                    }
                }
            }
            catch
            {
                // choco not in path
            }
            return results;
        }
    }

    public class ScoopFactory : IUninstallerFactory
    {
        public string SourceName => "Scoop";

        public IEnumerable<ApplicationUninstallerEntry> GetUninstallerEntries()
        {
            var results = new List<ApplicationUninstallerEntry>();
            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string scoopApps = Path.Combine(userProfile, "scoop", "apps");
                if (Directory.Exists(scoopApps))
                {
                    foreach (var appDir in Directory.GetDirectories(scoopApps))
                    {
                        string appName = Path.GetFileName(appDir);
                        if (appName.Equals("scoop", StringComparison.OrdinalIgnoreCase)) continue;

                        string currentSymlink = Path.Combine(appDir, "current");
                        string version = Directory.Exists(currentSymlink) ? "latest" : "";

                        results.Add(new ApplicationUninstallerEntry
                        {
                            DisplayName = $"{appName} (Scoop)",
                            RawDisplayName = appName,
                            DisplayVersion = version,
                            Publisher = "Scoop App",
                            InstallLocation = appDir,
                            UninstallString = $"scoop uninstall {appName}",
                            QuietUninstallString = $"scoop uninstall {appName}",
                            UninstallerKind = UninstallerType.Scoop,
                            IsRegistered = true
                        });
                    }
                }
            }
            catch
            {
                // Scoop directory not found
            }
            return results;
        }
    }

    public class WindowsFeatureFactory : IUninstallerFactory
    {
        public string SourceName => "Windows Features";

        public IEnumerable<ApplicationUninstallerEntry> GetUninstallerEntries()
        {
            var results = new List<ApplicationUninstallerEntry>();
            try
            {
                var psi = new ProcessStartInfo("dism.exe", "/Online /Get-Features /Format:Table")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return results;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                using var reader = new StringReader(output);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Contains("Enabled", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split('|');
                        if (parts.Length >= 2)
                        {
                            string featureName = parts[0].Trim();
                            if (!string.IsNullOrWhiteSpace(featureName) && !featureName.StartsWith("-"))
                            {
                                results.Add(new ApplicationUninstallerEntry
                                {
                                    DisplayName = featureName,
                                    RawDisplayName = featureName,
                                    Publisher = "Microsoft Corporation",
                                    UninstallString = $"dism.exe /Online /Disable-Feature /FeatureName:{featureName} /NoRestart",
                                    QuietUninstallString = $"dism.exe /Online /Disable-Feature /FeatureName:{featureName} /NoRestart",
                                    UninstallerKind = UninstallerType.WindowsFeature,
                                    SystemComponent = true,
                                    IsRegistered = true
                                });
                            }
                        }
                    }
                }
            }
            catch
            {
                // DISM access limitation
            }
            return results;
        }
    }
}

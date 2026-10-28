using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using LDUninstaller.Core.Models;

namespace LDUninstaller.Core.Discovery
{
    public class StoreAppFactory : IUninstallerFactory
    {
        public string SourceName => "Windows Store (UWP/AppX)";

        public IEnumerable<ApplicationUninstallerEntry> GetUninstallerEntries()
        {
            var results = new List<ApplicationUninstallerEntry>();

            try
            {
                // Run PowerShell to fetch NonDevelopment AppX packages
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable } | Select-Object Name, PackageFullName, Publisher, InstallLocation, Version | ConvertTo-Csv -NoTypeInformation\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return results;

                string output = process.StandardOutput.ReadToEnd();

                // FAULT TOLERANCE & PROCESS LIFECYCLE:
                // Ensure process is never abandoned as a zombie task on timeout.
                if (!process.WaitForExit(7000))
                {
                    try
                    {
                        process.Kill(true); // Kill process tree immediately
                    }
                    catch { }
                    return results;
                }

                using var reader = new StringReader(output);
                string? line;
                bool isHeader = true;

                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (isHeader)
                    {
                        isHeader = false;
                        continue;
                    }

                    // CSV simple split
                    var parts = SplitCsvLine(line);
                    if (parts.Count < 5) continue;

                    string name = parts[0];
                    string packageFullName = parts[1];
                    string publisher = parts[2];
                    string installLocation = parts[3];
                    string version = parts[4];

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(packageFullName))
                        continue;

                    // Clean publisher string CN=...
                    if (publisher.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                    {
                        int comma = publisher.IndexOf(',');
                        publisher = comma > 3 ? publisher[3..comma] : publisher[3..];
                    }

                    var entry = new ApplicationUninstallerEntry
                    {
                        DisplayName = name,
                        RawDisplayName = name,
                        DisplayVersion = version,
                        Publisher = publisher,
                        InstallLocation = installLocation,
                        RegistryKeyName = packageFullName,
                        UninstallString = $"powershell.exe -NoProfile -Command \"Remove-AppxPackage -Package '{packageFullName}'\"",
                        QuietUninstallString = $"powershell.exe -NoProfile -Command \"Remove-AppxPackage -Package '{packageFullName}'\"",
                        UninstallerKind = UninstallerType.StoreApp,
                        IsRegistered = true,
                        IsOrphaned = false,
                        SystemComponent = false
                    };

                    results.Add(entry);
                }
            }
            catch
            {
                // PowerShell unavailable or permissions restricted
            }

            return results;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString().Trim('\"', ' '));
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString().Trim('\"', ' '));

            return result;
        }
    }
}

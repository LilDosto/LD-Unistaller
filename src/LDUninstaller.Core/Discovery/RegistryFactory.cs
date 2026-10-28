using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LDUninstaller.Core.Models;
using Microsoft.Win32;

namespace LDUninstaller.Core.Discovery
{
    public interface IUninstallerFactory
    {
        string SourceName { get; }
        IEnumerable<ApplicationUninstallerEntry> GetUninstallerEntries();
    }

    public class RegistryFactory : IUninstallerFactory
    {
        public string SourceName => "Windows Registry";

        private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        public IEnumerable<ApplicationUninstallerEntry> GetUninstallerEntries()
        {
            var results = new List<ApplicationUninstallerEntry>();

            // 1. HKLM 64-bit
            ScanHive(RegistryHive.LocalMachine, RegistryView.Registry64, UninstallPath, results, MachineType.X64);

            // 2. HKLM 32-bit (Wow6432Node)
            if (Environment.Is64BitOperatingSystem)
            {
                ScanHive(RegistryHive.LocalMachine, RegistryView.Registry32, UninstallPath, results, MachineType.X86);
            }

            // 3. HKCU (Current User)
            ScanHive(RegistryHive.CurrentUser, RegistryView.Default, UninstallPath, results, MachineType.Unknown);

            return results;
        }

        private void ScanHive(RegistryHive hive, RegistryView view, string subKeyPath, List<ApplicationUninstallerEntry> results, MachineType machineType)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(subKeyPath);
                if (key == null) return;

                foreach (string subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = key.OpenSubKey(subKeyName);
                        if (sub == null) continue;

                        string? displayName = sub.GetValue("DisplayName") as string;
                        string? uninstallString = sub.GetValue("UninstallString") as string;
                        string? quietUninstallString = sub.GetValue("QuietUninstallString") as string;

                        // Need at least DisplayName or UninstallString
                        if (string.IsNullOrWhiteSpace(displayName) && string.IsNullOrWhiteSpace(uninstallString))
                            continue;

                        // Check SystemComponent
                        object? sysCompVal = sub.GetValue("SystemComponent");
                        bool isSystemComponent = sysCompVal is int i && i == 1;

                        // ParentKeyName (sub-components)
                        string? parentKeyName = sub.GetValue("ParentKeyName") as string;
                        bool isUpdate = !string.IsNullOrWhiteSpace(parentKeyName);

                        string? publisher = sub.GetValue("Publisher") as string ?? string.Empty;
                        string? displayVersion = sub.GetValue("DisplayVersion") as string ?? string.Empty;
                        string? installLocation = sub.GetValue("InstallLocation") as string ?? string.Empty;
                        string? displayIcon = sub.GetValue("DisplayIcon") as string ?? string.Empty;
                        string? modifyPath = sub.GetValue("ModifyPath") as string ?? string.Empty;
                        string? aboutUrl = sub.GetValue("URLInfoAbout") as string ?? sub.GetValue("HelpLink") as string ?? string.Empty;

                        // Size
                        long estimatedSize = 0;
                        if (sub.GetValue("EstimatedSize") is int sizeInKb)
                            estimatedSize = (long)sizeInKb * 1024;
                        else if (sub.GetValue("EstimatedSize") is long sizeLong)
                            estimatedSize = sizeLong * 1024;

                        // InstallDate
                        DateTime installDate = DateTime.MinValue;
                        if (sub.GetValue("InstallDate") is string dateStr && !string.IsNullOrWhiteSpace(dateStr))
                        {
                            if (DateTime.TryParseExact(dateStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                                installDate = dt;
                            else if (DateTime.TryParse(dateStr, out var dt2))
                                installDate = dt2;
                        }

                        // WindowsInstaller flag / MSI Product code
                        object? winInstallerVal = sub.GetValue("WindowsInstaller");
                        bool isMsi = winInstallerVal is int wi && wi == 1;
                        Guid bundleGuid = Guid.Empty;
                        if (Guid.TryParse(subKeyName, out var guid))
                        {
                            bundleGuid = guid;
                            isMsi = true;
                        }

                        var entry = new ApplicationUninstallerEntry
                        {
                            DisplayName = displayName ?? subKeyName,
                            RawDisplayName = displayName ?? subKeyName,
                            DisplayVersion = displayVersion,
                            Publisher = publisher,
                            InstallLocation = installLocation.Trim('\"'),
                            UninstallString = uninstallString ?? string.Empty,
                            QuietUninstallString = quietUninstallString ?? string.Empty,
                            ModifyPath = modifyPath,
                            DisplayIcon = displayIcon.Trim('\"'),
                            AboutUrl = aboutUrl,
                            RegistryKeyName = subKeyName,
                            RegistryPath = $"{hive}\\{subKeyPath}\\{subKeyName}",
                            BundleProviderKey = bundleGuid,
                            EstimatedSize = FileSize.FromBytes(estimatedSize),
                            InstallDate = installDate,
                            Is64Bit = machineType,
                            SystemComponent = isSystemComponent,
                            IsUpdate = isUpdate,
                            IsRegistered = true,
                            IsOrphaned = false,
                            UninstallerKind = isMsi ? UninstallerType.Msiexec : UninstallerType.Unknown
                        };

                        results.Add(entry);
                    }
                    catch
                    {
                        // Skip unreadable entry
                    }
                }
            }
            catch
            {
                // Access error or registry view not supported
            }
        }
    }
}

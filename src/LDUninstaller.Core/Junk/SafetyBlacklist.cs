using System;
using System.Collections.Generic;
using System.IO;

namespace LDUninstaller.Core.Junk
{
    public static class SafetyBlacklist
    {
        private static readonly HashSet<string> BlacklistedDirs = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> BlacklistedRegPaths = new(StringComparer.OrdinalIgnoreCase);

        static SafetyBlacklist()
        {
            // Windows & System
            AddDir(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            AddDir(Environment.GetFolderPath(Environment.SpecialFolder.System));
            AddDir(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86));
            AddDir(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            AddDir(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            AddDir(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            AddDir(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            AddDir(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            AddDir(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

            // Drives roots
            foreach (var drive in DriveInfo.GetDrives())
            {
                AddDir(drive.RootDirectory.FullName);
            }

            // Registry Blacklists
            BlacklistedRegPaths.Add(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion");
            BlacklistedRegPaths.Add(@"HKLM\SYSTEM\CurrentControlSet");
            BlacklistedRegPaths.Add(@"HKLM\SYSTEM");
            BlacklistedRegPaths.Add(@"HKLM\SOFTWARE\Microsoft");
            BlacklistedRegPaths.Add(@"HKLM\SOFTWARE\Classes");
            BlacklistedRegPaths.Add(@"HKCU\SOFTWARE\Microsoft");
            BlacklistedRegPaths.Add(@"HKCU\SOFTWARE\Microsoft\Windows");
            BlacklistedRegPaths.Add(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion");
        }

        private static void AddDir(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    BlacklistedDirs.Add(Path.GetFullPath(path).TrimEnd('\\'));
                }
                catch { }
            }
        }

        public static bool IsDangerousDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            try
            {
                string norm = Path.GetFullPath(path).TrimEnd('\\');
                if (BlacklistedDirs.Contains(norm))
                    return true;

                // Parent must not be directly root or users root without specific subfolder
                var di = new DirectoryInfo(norm);
                if (di.Parent == null) return true; // C:\
                if (di.Parent.FullName.Equals(@"C:\Users", StringComparison.OrdinalIgnoreCase)) return true;

                return false;
            }
            catch
            {
                return true;
            }
        }

        public static bool IsDangerousRegistryPath(string regPath)
        {
            if (string.IsNullOrWhiteSpace(regPath)) return true;
            string norm = regPath.Replace("/", "\\").TrimEnd('\\');
            foreach (var bl in BlacklistedRegPaths)
            {
                if (norm.Equals(bl, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            // Root hives
            if (!norm.Contains('\\')) return true;
            return false;
        }
    }
}

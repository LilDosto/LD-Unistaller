using System;
using System.IO;
using Microsoft.Win32;

namespace LDUninstaller.Core.Models
{
    public enum JunkType
    {
        RegistryKey,
        RegistryValue,
        Directory,
        File,
        Service,
        FirewallRule,
        UserAssist,
        Shortcut
    }

    public interface IJunkResult
    {
        string EntryName { get; }
        string Path { get; }
        JunkType Type { get; }
        ConfidenceLevel Confidence { get; }
        string Reason { get; }
        bool Selected { get; set; }
        bool Delete(string backupDir);
    }

    public class RegistryJunkResult : IJunkResult
    {
        public string EntryName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty; // e.g. HKLM\SOFTWARE\Vendor\AppName
        public string? ValueName { get; set; }           // if null, entire key is deleted
        public JunkType Type => ValueName == null ? JunkType.RegistryKey : JunkType.RegistryValue;
        public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Good;
        public string Reason { get; set; } = string.Empty;
        public bool Selected { get; set; } = true;

        public bool Delete(string backupDir)
        {
            try
            {
                BackupToReg(backupDir);
                var (hive, subKey) = ParseRegistryPath(Path);
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                if (ValueName == null)
                {
                    baseKey.DeleteSubKeyTree(subKey, false);
                }
                else
                {
                    using var opened = baseKey.OpenSubKey(subKey, true);
                    opened?.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void BackupToReg(string backupDir)
        {
            try
            {
                Directory.CreateDirectory(backupDir);
                string sanitized = System.Text.RegularExpressions.Regex.Replace(Path, @"[\\/:\*\?""<>\|]", "_");
                string file = System.IO.Path.Combine(backupDir, $"reg_backup_{DateTime.Now:yyyyMMdd_HHmmss}_{sanitized}.reg");
                var psi = new System.Diagnostics.ProcessStartInfo("reg.exe", $"export \"{Path}\" \"{file}\" /y")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = System.Diagnostics.Process.Start(psi);
                p?.WaitForExit(3000);
            }
            catch
            {
                // Non-fatal if backup tool fails
            }
        }

        private static (RegistryHive hive, string subKey) ParseRegistryPath(string fullPath)
        {
            int firstSlash = fullPath.IndexOf('\\');
            string hiveStr = firstSlash > 0 ? fullPath[..firstSlash] : fullPath;
            string sub = firstSlash > 0 ? fullPath[(firstSlash + 1)..] : "";

            RegistryHive hive = hiveStr.ToUpperInvariant() switch
            {
                "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
                "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
                "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
                "HKU" or "HKEY_USERS" => RegistryHive.Users,
                _ => RegistryHive.LocalMachine
            };
            return (hive, sub);
        }
    }

    public class FileSystemJunkResult : IJunkResult
    {
        public string EntryName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public JunkType Type => IsDirectory ? JunkType.Directory : JunkType.File;
        public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Good;
        public string Reason { get; set; } = string.Empty;
        public bool Selected { get; set; } = true;

        public bool Delete(string backupDir)
        {
            try
            {
                if (IsDirectory)
                {
                    if (Directory.Exists(Path))
                    {
                        Directory.Delete(Path, true);
                        return true;
                    }
                }
                else
                {
                    if (File.Exists(Path))
                    {
                        try
                        {
                            File.Delete(Path);
                            return true;
                        }
                        catch
                        {
                            // If file is locked, queue delete on reboot
                            return Native.Win32.ScheduleDeleteOnReboot(Path);
                        }
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public class GenericJunkResult : IJunkResult
    {
        public string EntryName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public JunkType Type { get; set; }
        public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Good;
        public string Reason { get; set; } = string.Empty;
        public bool Selected { get; set; } = true;
        public Func<bool>? CustomAction { get; set; }

        public bool Delete(string backupDir)
        {
            try
            {
                return CustomAction?.Invoke() ?? true;
            }
            catch
            {
                return false;
            }
        }
    }
}

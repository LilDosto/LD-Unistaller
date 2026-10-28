using System;
using System.Collections.Generic;
using System.IO;
using System.ServiceProcess;
using LDUninstaller.Core.Models;
using Microsoft.Win32;

namespace LDUninstaller.Core.Startup
{
    public class StartupManager
    {
        public static List<StartupEntry> GetAllStartupEntries()
        {
            var list = new List<StartupEntry>();

            // 1. Registry HKCU Run
            ScanRegistryRun(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", StartupType.RegistryRun, list);
            // 2. Registry HKLM Run
            ScanRegistryRun(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", StartupType.RegistryRun, list);
            // 3. Registry HKLM 32-bit (Wow6432Node)
            if (Environment.Is64BitOperatingSystem)
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                if (key != null)
                {
                    foreach (var name in key.GetValueNames())
                    {
                        var val = key.GetValue(name)?.ToString() ?? string.Empty;
                        list.Add(new StartupEntry
                        {
                            Name = name,
                            Command = val,
                            Type = StartupType.RegistryRun,
                            Location = @"HKLM\Software\Wow6432Node\...\Run",
                            IsEnabled = CheckStartupApproved(name)
                        });
                    }
                }
            }

            // 4. Startup Folders
            ScanFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "User Startup Folder", list);
            ScanFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "All Users Startup Folder", list);

            // 5. Services (3rd party automatic)
            ScanServices(list);

            return list;
        }

        private static void ScanRegistryRun(RegistryKey root, string subKey, StartupType type, List<StartupEntry> list)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                if (key == null) return;

                foreach (var name in key.GetValueNames())
                {
                    string cmd = key.GetValue(name)?.ToString() ?? string.Empty;
                    list.Add(new StartupEntry
                    {
                        Name = name,
                        Command = cmd,
                        Type = type,
                        Location = $"{root.Name}\\{subKey}",
                        IsEnabled = CheckStartupApproved(name)
                    });
                }
            }
            catch { }
        }

        private static void ScanFolder(string path, string locationDesc, List<StartupEntry> list)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            try
            {
                foreach (var file in Directory.GetFiles(path))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    list.Add(new StartupEntry
                    {
                        Name = name,
                        Command = file,
                        Type = StartupType.StartupFolder,
                        Location = locationDesc,
                        IsEnabled = true
                    });
                }
            }
            catch { }
        }

        private static void ScanServices(List<StartupEntry> list)
        {
            try
            {
                var services = ServiceController.GetServices();
                foreach (var sc in services)
                {
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{sc.ServiceName}");
                        if (key != null)
                        {
                            int startType = (int)(key.GetValue("Start") ?? 3);
                            // 2 = Automatic
                            if (startType == 2)
                            {
                                string imgPath = key.GetValue("ImagePath")?.ToString() ?? "";
                                if (!imgPath.ToLowerInvariant().Contains(@"windows\system32"))
                                {
                                    list.Add(new StartupEntry
                                    {
                                        Name = sc.DisplayName,
                                        Command = imgPath,
                                        Type = StartupType.Service,
                                        Location = "Windows Service",
                                        IsEnabled = sc.Status == ServiceControllerStatus.Running
                                    });
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static bool CheckStartupApproved(string entryName)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
                if (key?.GetValue(entryName) is byte[] data && data.Length > 0)
                {
                    // 0x02 is enabled, 0x03 or other is disabled
                    return data[0] == 0x02;
                }
            }
            catch { }
            return true;
        }

        public static bool ToggleStartupEntry(StartupEntry entry, bool enable)
        {
            try
            {
                if (entry.Type == StartupType.RegistryRun)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
                    if (key != null)
                    {
                        byte[] data = enable
                            ? new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }
                            : new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                        key.SetValue(entry.Name, data, RegistryValueKind.Binary);
                        entry.IsEnabled = enable;
                        return true;
                    }
                }
                else if (entry.Type == StartupType.Service)
                {
                    using var sc = new ServiceController(entry.Name);
                    if (enable && sc.Status != ServiceControllerStatus.Running)
                    {
                        sc.Start();
                        entry.IsEnabled = true;
                    }
                    else if (!enable && sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        entry.IsEnabled = false;
                    }
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}

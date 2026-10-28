using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            ScanRegistryRun(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", StartupType.RegistryRun, list, isHklm: false, isWow64: false);

            // 2. Registry HKLM Run
            ScanRegistryRun(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", StartupType.RegistryRun, list, isHklm: true, isWow64: false);

            // 3. Registry HKLM 32-bit (Wow6432Node)
            if (Environment.Is64BitOperatingSystem)
            {
                try
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
                                IsEnabled = CheckStartupApproved(name, isHklm: true, isWow64: true)
                            });
                        }
                    }
                }
                catch { }
            }

            // 4. Startup Folders
            ScanFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Kullanıcı Başlangıç Klasörü", list, isCommon: false);
            ScanFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Tüm Kullanıcılar Başlangıç Klasörü", list, isCommon: true);

            // 5. Services (3rd party automatic)
            ScanServices(list);

            return list;
        }

        private static void ScanRegistryRun(RegistryKey root, string subKey, StartupType type, List<StartupEntry> list, bool isHklm, bool isWow64)
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
                        IsEnabled = CheckStartupApproved(name, isHklm, isWow64)
                    });
                }
            }
            catch { }
        }

        private static void ScanFolder(string path, string locationDesc, List<StartupEntry> list, bool isCommon)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            try
            {
                foreach (var file in Directory.GetFiles(path))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".lnk" && ext != ".url" && ext != ".disabled") continue;

                    string fileName = Path.GetFileName(file);
                    string displayName = Path.GetFileNameWithoutExtension(file);
                    bool isEnabled = !ext.Equals(".disabled", StringComparison.OrdinalIgnoreCase);

                    // Check StartupApproved for startup folder
                    if (isEnabled)
                    {
                        isEnabled = CheckStartupFolderApproved(fileName, isCommon);
                    }

                    list.Add(new StartupEntry
                    {
                        Name = displayName,
                        Command = file,
                        Type = StartupType.StartupFolder,
                        Location = locationDesc,
                        IsEnabled = isEnabled
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
                            string imgPath = key.GetValue("ImagePath")?.ToString() ?? "";
                            string imgLower = imgPath.ToLowerInvariant();

                            // 2 = Automatic, 3 = Manual, 4 = Disabled
                            // Exclude internal OS services
                            if (!imgLower.Contains(@"\windows\system32\svchost.exe") &&
                                !imgLower.Contains(@"\windows\system32\services.exe") &&
                                !imgLower.Contains(@"\windows\system32\lsass.exe"))
                            {
                                if (startType == 2 || startType == 4)
                                {
                                    list.Add(new StartupEntry
                                    {
                                        Name = sc.DisplayName,
                                        ServiceName = sc.ServiceName,
                                        Command = imgPath,
                                        Type = StartupType.Service,
                                        Location = "Windows Servisi",
                                        IsEnabled = (startType == 2)
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

        public static bool CheckStartupApproved(string entryName, bool isHklm = false, bool isWow64 = false)
        {
            try
            {
                string approvedSub = isWow64 ? @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"
                                             : @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

                // First check HKCU
                using (var key = Registry.CurrentUser.OpenSubKey(approvedSub))
                {
                    if (key?.GetValue(entryName) is byte[] data && data.Length > 0)
                    {
                        return data[0] == 0x02;
                    }
                }

                // If HKLM, also check HKLM StartupApproved
                if (isHklm)
                {
                    using var hklmKey = Registry.LocalMachine.OpenSubKey(approvedSub);
                    if (hklmKey?.GetValue(entryName) is byte[] data && data.Length > 0)
                    {
                        return data[0] == 0x02;
                    }
                }
            }
            catch { }
            return true;
        }

        public static bool CheckStartupFolderApproved(string fileName, bool isCommon)
        {
            try
            {
                const string approvedSub = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

                using (var key = Registry.CurrentUser.OpenSubKey(approvedSub))
                {
                    if (key?.GetValue(fileName) is byte[] data && data.Length > 0)
                    {
                        return data[0] == 0x02;
                    }
                }

                if (isCommon)
                {
                    using var hklmKey = Registry.LocalMachine.OpenSubKey(approvedSub);
                    if (hklmKey?.GetValue(fileName) is byte[] data && data.Length > 0)
                    {
                        return data[0] == 0x02;
                    }
                }
            }
            catch { }
            return true;
        }

        public static bool ToggleStartupEntry(StartupEntry entry, bool enable)
        {
            try
            {
                byte[] binaryData = enable
                    ? new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }
                    : new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

                if (entry.Type == StartupType.RegistryRun)
                {
                    bool isWow64 = entry.Location.Contains("Wow6432Node", StringComparison.OrdinalIgnoreCase);
                    bool isHklm = entry.Location.Contains("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) || entry.Location.Contains("HKLM", StringComparison.OrdinalIgnoreCase);

                    string approvedSub = isWow64 ? @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"
                                                 : @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

                    // Always write to HKCU
                    using (var key = Registry.CurrentUser.CreateSubKey(approvedSub))
                    {
                        key?.SetValue(entry.Name, binaryData, RegistryValueKind.Binary);
                    }

                    // If it was in HKLM, also write to HKLM
                    if (isHklm)
                    {
                        try
                        {
                            using var key = Registry.LocalMachine.CreateSubKey(approvedSub);
                            key?.SetValue(entry.Name, binaryData, RegistryValueKind.Binary);
                        }
                        catch { }
                    }

                    entry.IsEnabled = enable;
                    return true;
                }
                else if (entry.Type == StartupType.StartupFolder)
                {
                    string fileName = Path.GetFileName(entry.Command);
                    bool isCommon = entry.Location.Contains("Tüm Kullanıcılar", StringComparison.OrdinalIgnoreCase) || entry.Location.Contains("All Users", StringComparison.OrdinalIgnoreCase);

                    const string approvedFolderSub = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

                    // Write to StartupApproved\StartupFolder in HKCU
                    using (var key = Registry.CurrentUser.CreateSubKey(approvedFolderSub))
                    {
                        key?.SetValue(fileName, binaryData, RegistryValueKind.Binary);
                    }

                    if (isCommon)
                    {
                        try
                        {
                            using var key = Registry.LocalMachine.CreateSubKey(approvedFolderSub);
                            key?.SetValue(fileName, binaryData, RegistryValueKind.Binary);
                        }
                        catch { }
                    }

                    // Fallback rename if StartupApproved is not respected
                    try
                    {
                        if (!enable && File.Exists(entry.Command) && !entry.Command.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        {
                            string newPath = entry.Command + ".disabled";
                            File.Move(entry.Command, newPath);
                            entry.Command = newPath;
                        }
                        else if (enable && File.Exists(entry.Command) && entry.Command.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        {
                            string newPath = entry.Command.Substring(0, entry.Command.Length - ".disabled".Length);
                            File.Move(entry.Command, newPath);
                            entry.Command = newPath;
                        }
                    }
                    catch { }

                    entry.IsEnabled = enable;
                    return true;
                }
                else if (entry.Type == StartupType.Service)
                {
                    string svcName = !string.IsNullOrEmpty(entry.ServiceName) ? entry.ServiceName : entry.Name;
                    int targetStartType = enable ? 2 : 4; // 2 = Auto, 4 = Disabled

                    // 1. Update Registry Start type directly
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{svcName}", true);
                        if (key != null)
                        {
                            key.SetValue("Start", targetStartType, RegistryValueKind.DWord);
                        }
                    }
                    catch { }

                    // 2. Run sc.exe config start= auto / disabled
                    try
                    {
                        string mode = enable ? "auto" : "disabled";
                        var psi = new ProcessStartInfo("sc.exe", $"config \"{svcName}\" start= {mode}")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var p = Process.Start(psi);
                        p?.WaitForExit(3000);
                    }
                    catch { }

                    // 3. Stop or Start the service
                    try
                    {
                        using var sc = new ServiceController(svcName);
                        if (!enable && sc.Status == ServiceControllerStatus.Running)
                        {
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(3));
                        }
                        else if (enable && sc.Status == ServiceControllerStatus.Stopped)
                        {
                            sc.Start();
                        }
                    }
                    catch { }

                    entry.IsEnabled = enable;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ToggleStartupEntry error: {ex.Message}");
            }
            return false;
        }
    }
}

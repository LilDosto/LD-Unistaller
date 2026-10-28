using System;
using System.Collections.Generic;
using System.ServiceProcess;
using LDUninstaller.Core.Models;
using Microsoft.Win32;

namespace LDUninstaller.Core.Tweaks
{
    public static class WindowsTweaks
    {
        public static List<DebloatTweak> GetDefaultTweaks()
        {
            return new List<DebloatTweak>
            {
                new DebloatTweak
                {
                    Id = "telemetry",
                    Title = "Windows Telemetri ve Veri Toplamayı Kapat",
                    Category = "Gizlilik & Güvenlik",
                    Description = "DiagTrack ve dmwappushservice servislerini durdurur ve kayıt defteri telemetri bayraklarını 0 yapar.",
                    IsApplied = CheckTelemetryDisabled(),
                    ApplyAction = () =>
                    {
                        StopAndDisableService("DiagTrack");
                        StopAndDisableService("dmwappushservice");

                        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
                        key?.SetValue("AllowTelemetry", 0, RegistryValueKind.DWord);

                        using var userKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Diagnostics\DiagTrack");
                        userKey?.SetValue("ShowedToastAtLevel", 0, RegistryValueKind.DWord);
                    },
                    RevertAction = () =>
                    {
                        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
                        key?.SetValue("AllowTelemetry", 1, RegistryValueKind.DWord);
                    }
                },
                new DebloatTweak
                {
                    Id = "cortana_bing",
                    Title = "Cortana ve Web Aramalarını Kapat",
                    Category = "Sistem & Başlat",
                    Description = "Başlat menüsünde Bing web aramalarını ve Cortana asistanını devre dışı bırakır.",
                    IsApplied = CheckCortanaDisabled(),
                    ApplyAction = () =>
                    {
                        using var searchKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Search");
                        searchKey?.SetValue("DisableWebSearch", 1, RegistryValueKind.DWord);
                        searchKey?.SetValue("ConnectedSearchUseWeb", 0, RegistryValueKind.DWord);
                        searchKey?.SetValue("BingSearchEnabled", 0, RegistryValueKind.DWord);

                        using var policyKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search");
                        policyKey?.SetValue("AllowCortana", 0, RegistryValueKind.DWord);
                        policyKey?.SetValue("DisableWebSearch", 1, RegistryValueKind.DWord);
                        policyKey?.SetValue("ConnectedSearchUseWeb", 0, RegistryValueKind.DWord);
                    },
                    RevertAction = () =>
                    {
                        using var searchKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Search");
                        searchKey?.SetValue("DisableWebSearch", 0, RegistryValueKind.DWord);
                        searchKey?.SetValue("ConnectedSearchUseWeb", 1, RegistryValueKind.DWord);
                        searchKey?.SetValue("BingSearchEnabled", 1, RegistryValueKind.DWord);
                    }
                },
                new DebloatTweak
                {
                    Id = "silent_apps",
                    Title = "Önerilen ve Sponsorlu Uygulamaların Otomatik Yüklenmesini Engelle",
                    Category = "Windows Mağaza",
                    Description = "Candy Crush ve benzeri sponsorlu 3. parti uygulamaların arka planda yüklenmesini engeller.",
                    IsApplied = CheckSilentAppsDisabled(),
                    ApplyAction = () =>
                    {
                        using var cdmKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager");
                        cdmKey?.SetValue("SilentInstalledAppsEnabled", 0, RegistryValueKind.DWord);
                        cdmKey?.SetValue("SubscribedContent-338388Enabled", 0, RegistryValueKind.DWord);
                        cdmKey?.SetValue("SubscribedContent-338389Enabled", 0, RegistryValueKind.DWord);
                        cdmKey?.SetValue("SystemPaneSuggestionsEnabled", 0, RegistryValueKind.DWord);
                    },
                    RevertAction = () =>
                    {
                        using var cdmKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager");
                        cdmKey?.SetValue("SilentInstalledAppsEnabled", 1, RegistryValueKind.DWord);
                    }
                },
                new DebloatTweak
                {
                    Id = "sticky_keys",
                    Title = "Yapışkan Tuşlar (Sticky Keys) Açılır Penceresini Engelle",
                    Category = "Erişilebilirlik",
                    Description = "5 kez Shift tuşuna basıldığında açılan yapışkan tuşlar uyarı penceresini sessizleştirir.",
                    IsApplied = CheckStickyKeysDisabled(),
                    ApplyAction = () =>
                    {
                        using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Accessibility\StickyKeys");
                        key?.SetValue("Flags", "506", RegistryValueKind.String);
                    },
                    RevertAction = () =>
                    {
                        using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Accessibility\StickyKeys");
                        key?.SetValue("Flags", "510", RegistryValueKind.String);
                    }
                }
            };
        }

        private static bool CheckTelemetryDisabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection");
                int val = (int)(key?.GetValue("AllowTelemetry") ?? 1);
                return val == 0;
            }
            catch { return false; }
        }

        private static bool CheckCortanaDisabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Search");
                int val = (int)(key?.GetValue("DisableWebSearch") ?? 0);
                return val == 1;
            }
            catch { return false; }
        }

        private static bool CheckSilentAppsDisabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager");
                int val = (int)(key?.GetValue("SilentInstalledAppsEnabled") ?? 1);
                return val == 0;
            }
            catch { return false; }
        }

        private static bool CheckStickyKeysDisabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Accessibility\StickyKeys");
                string val = key?.GetValue("Flags")?.ToString() ?? "";
                return val == "506";
            }
            catch { return false; }
        }

        private static void StopAndDisableService(string serviceName)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Running)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(3));
                }

                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", true);
                key?.SetValue("Start", 4, RegistryValueKind.DWord); // 4 = Disabled
            }
            catch { }
        }
    }
}

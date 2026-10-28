using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LDUninstaller.Core.Native;

namespace LDUninstaller.Automatizer
{
    internal class Program
    {
        private static readonly ConcurrentDictionary<int, DateTime> MonitoredProcesses = new();
        private static readonly string[] GoodButtonTexts = new[]
        {
            "next", "uninstall", "continue", "finish", "close", "i agree", "ok", "yes",
            "ileri", "kaldır", "devam", "bitir", "kapat", "kabul ediyorum", "tamam", "evet",
            "suivant", "weiter", "fertigstellen", "schließen", "désinstaller"
        };

        private static readonly string[] BadButtonTexts = new[]
        {
            "install bloatware", "change homepage", "reboot now", "ankete katıl",
            "survey", "reboot computer now", "şimdi yeniden başlat"
        };

        private static readonly string[] BadCheckboxTexts = new[]
        {
            "reboot now", "şimdi yeniden başlat", "install", "toolbar", "browser", "search engine",
            "anket", "survey", "homepage", "launch", "çalıştır"
        };

        [STAThread]
        static void Main(string[] args)
        {
            bool isDaemon = args.Contains("/d") || args.Contains("--daemon") || args.Length == 0;
            Console.WriteLine("=== LD Uninstaller Automation Daemon ===");

            if (args.Length > 0 && int.TryParse(args[0], out int singlePid))
            {
                // Direct PID mode
                MonitorProcess(singlePid, stealth: false);
                return;
            }

            // Start Named Pipe listener loop in background
            var cts = new CancellationTokenSource();
            _ = Task.Run(() => StartPipeServer(cts.Token));

            // Start Automation Monitoring Loop
            using var automation = new UIA3Automation();
            Console.WriteLine("Daemon aktif. Named Pipe '\\\\.\\pipe\\LDUninstallerDaemon' dinleniyor...");

            while (true)
            {
                try
                {
                    foreach (var kvp in MonitoredProcesses.ToArray())
                    {
                        int pid = kvp.Key;
                        DateTime startTime = kvp.Value;

                        // Check watchdog timeout (5 minutes)
                        if ((DateTime.Now - startTime).TotalMinutes > 5)
                        {
                            Console.WriteLine($"[Watchdog] PID {pid} zaman aşımına uğradı, sonlandırılıyor.");
                            try
                            {
                                var p = Process.GetProcessById(pid);
                                p.Kill();
                            }
                            catch { }
                            MonitoredProcesses.TryRemove(pid, out _);
                            continue;
                        }

                        // Check if process still exists
                        try
                        {
                            var proc = Process.GetProcessById(pid);
                            if (proc.HasExited)
                            {
                                MonitoredProcesses.TryRemove(pid, out _);
                                continue;
                            }

                            // Automate UI windows for this PID
                            AutomateProcessWindows(automation, proc);
                        }
                        catch (ArgumentException)
                        {
                            MonitoredProcesses.TryRemove(pid, out _);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Hata PID {pid}] {ex.Message}");
                        }
                    }
                }
                catch { }

                Thread.Sleep(800);
            }
        }

        private static void StartPipeServer(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream("LDUninstallerDaemon", PipeDirection.In);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    string? line = reader.ReadLine();

                    if (!string.IsNullOrWhiteSpace(line) && line.StartsWith("ATTACH:", StringComparison.OrdinalIgnoreCase))
                    {
                        string pidStr = line["ATTACH:".Length..].Trim();
                        if (int.TryParse(pidStr, out int pid))
                        {
                            Console.WriteLine($"[IPC] Takip edilecek PID alındı: {pid}");
                            MonitoredProcesses.TryAdd(pid, DateTime.Now);
                        }
                    }
                }
                catch { }
            }
        }

        private static void MonitorProcess(int pid, bool stealth)
        {
            using var automation = new UIA3Automation();
            Console.WriteLine($"PID {pid} izleniyor...");

            try
            {
                var proc = Process.GetProcessById(pid);
                while (!proc.HasExited)
                {
                    AutomateProcessWindows(automation, proc, stealth);
                    Thread.Sleep(800);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"İzleme tamamlandı: {ex.Message}");
            }
        }

        private static void AutomateProcessWindows(UIA3Automation automation, Process proc, bool stealth = false)
        {
            var desktop = automation.GetDesktop();
            var windows = desktop.FindAllChildren(cf => cf.ByProcessId(proc.Id));

            foreach (var win in windows)
            {
                try
                {
                    IntPtr hwnd = win.Properties.NativeWindowHandle.ValueOrDefault;
                    if (stealth && hwnd != IntPtr.Zero)
                    {
                        Win32.SetWindowPos(hwnd, IntPtr.Zero, -32000, -32000, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOZORDER);
                    }

                    // Check radio buttons: NSIS 1204 (Reboot Later) vs 1203 (Reboot Now)
                    var radioButtons = win.FindAllDescendants(cf => cf.ByControlType(ControlType.RadioButton));
                    foreach (var rb in radioButtons)
                    {
                        string autoId = rb.AutomationId ?? "";
                        string name = rb.Name?.ToLowerInvariant() ?? "";

                        if (autoId == "1204" || name.Contains("later") || name.Contains("sonra"))
                        {
                            var r = rb.AsRadioButton();
                            if (r != null) r.IsChecked = true;
                            Console.WriteLine($"[AutoUI] 'Daha sonra yeniden başlat' seçildi (ID {autoId})");
                        }
                    }

                    // Check checkboxes for bloatware / surveys / reboot
                    var checkBoxes = win.FindAllDescendants(cf => cf.ByControlType(ControlType.CheckBox));
                    foreach (var cb in checkBoxes)
                    {
                        string cbName = cb.Name?.ToLowerInvariant() ?? "";
                        foreach (var bad in BadCheckboxTexts)
                        {
                            if (cbName.Contains(bad))
                            {
                                var checkBox = cb.AsCheckBox();
                                if (checkBox != null && checkBox.IsChecked == true)
                                {
                                    checkBox.IsChecked = false;
                                    Console.WriteLine($"[AutoUI] İstenmeyen onay kutusu temizlendi: {cb.Name}");
                                }
                            }
                        }
                    }

                    // Check standard buttons
                    var buttons = win.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
                    foreach (var btn in buttons)
                    {
                        string btnId = btn.AutomationId ?? "";
                        string btnText = btn.Name?.Trim().ToLowerInvariant() ?? "";

                        // Prioritize NSIS standard Next button ID 1 or Close/Finish
                        if (btnId == "1" || IsGoodButton(btnText))
                        {
                            if (!IsBadButton(btnText) && btn.IsEnabled)
                            {
                                Console.WriteLine($"[AutoUI] Buton tıklandı: '{btn.Name}' (ID: {btnId})");
                                btn.AsButton()?.Invoke();
                                Thread.Sleep(300);
                                break;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private static bool IsGoodButton(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return GoodButtonTexts.Any(g => text.Equals(g, StringComparison.OrdinalIgnoreCase) || text.Contains(g, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsBadButton(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return BadButtonTexts.Any(b => text.Contains(b, StringComparison.OrdinalIgnoreCase));
        }
    }
}

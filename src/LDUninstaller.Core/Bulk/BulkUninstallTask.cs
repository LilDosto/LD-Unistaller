using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using LDUninstaller.Core.Algorithms;
using LDUninstaller.Core.Models;
using LDUninstaller.Core.Native;

namespace LDUninstaller.Core.Bulk
{
    public enum BulkStatus
    {
        Queued,
        Running,
        Succeeded,
        Failed,
        Skipped,
        Cancelled
    }

    public class BulkUninstallEntry
    {
        public ApplicationUninstallerEntry Entry { get; }
        public BulkStatus Status { get; set; } = BulkStatus.Queued;
        public string StatusMessage { get; set; } = "Kuyrukta";
        public bool PreferQuiet { get; set; } = true;
        public int ExitCode { get; set; } = -1;

        public BulkUninstallEntry(ApplicationUninstallerEntry entry, bool preferQuiet = true)
        {
            Entry = entry;
            PreferQuiet = preferQuiet;
        }
    }

    public class BulkUninstallTask
    {
        public List<BulkUninstallEntry> Items { get; }
        public int MaxConcurrentQuiet { get; set; } = 2;
        public bool CreateRestorePoint { get; set; } = true;
        public bool EnableAutomatizer { get; set; } = true;

        public event Action<BulkUninstallEntry>? OnItemStarted;
        public event Action<BulkUninstallEntry>? OnItemCompleted;
        public event Action<string>? OnLog;

        private readonly SemaphoreSlim _msiLock = new(1, 1);
        private readonly SemaphoreSlim _loudLimitLock = new(1, 1);
        private readonly object _stateLock = new();
        private readonly HashSet<string> _activePublishers = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _activeDirectories = new(StringComparer.OrdinalIgnoreCase);

        public BulkUninstallTask(IEnumerable<ApplicationUninstallerEntry> entries, bool preferQuiet = true)
        {
            Items = entries.Select(e => new BulkUninstallEntry(e, preferQuiet)).ToList();
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            if (CreateRestorePoint)
            {
                OnLog?.Invoke("Sistem Geri Yükleme Noktası oluşturuluyor...");
                await Task.Run(() => Win32.CreateRestorePoint("LD Uninstaller Bulk Pre-Restore Point", out _));
            }

            var throttler = new SemaphoreSlim(MaxConcurrentQuiet, MaxConcurrentQuiet);
            var runningTasks = new List<Task>();

            foreach (var item in Items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    item.Status = BulkStatus.Cancelled;
                    item.StatusMessage = "İptal edildi";
                    continue;
                }

                // Check serialized types
                bool isSerialized = IsSerializedType(item.Entry.UninstallerKind);

                if (isSerialized)
                {
                    // Wait for all currently running background tasks to finish
                    await Task.WhenAll(runningTasks);
                    runningTasks.Clear();

                    await RunSingleItemAsync(item, cancellationToken);
                }
                else
                {
                    await throttler.WaitAsync(cancellationToken);

                    var t = Task.Run(async () =>
                    {
                        try
                        {
                            await RunSingleItemAsync(item, cancellationToken);
                        }
                        finally
                        {
                            throttler.Release();
                        }
                    }, cancellationToken);

                    runningTasks.Add(t);
                }
            }

            await Task.WhenAll(runningTasks);
            OnLog?.Invoke("Tüm toplu kaldırma işlemleri tamamlandı.");
        }

        private async Task RunSingleItemAsync(BulkUninstallEntry item, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                item.Status = BulkStatus.Cancelled;
                item.StatusMessage = "İptal edildi";
                return;
            }

            // Concurrency Collision Prevention:
            // 1. Publisher Lock
            string pub = Sift4.NormalizeName(item.Entry.Publisher);
            bool pubAcquired = false;

            // 2. Directory Hierarchy Lock
            string dir = !string.IsNullOrWhiteSpace(item.Entry.InstallLocation)
                ? Path.GetFullPath(item.Entry.InstallLocation).TrimEnd('\\')
                : string.Empty;

            while (!pubAcquired && !cancellationToken.IsCancellationRequested)
            {
                lock (_stateLock)
                {
                    bool pubConflict = !string.IsNullOrWhiteSpace(pub) && _activePublishers.Contains(pub);
                    bool dirConflict = !string.IsNullOrWhiteSpace(dir) && _activeDirectories.Any(d =>
                        d.Equals(dir, StringComparison.OrdinalIgnoreCase) ||
                        dir.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase) ||
                        d.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase));

                    if (!pubConflict && !dirConflict)
                    {
                        if (!string.IsNullOrWhiteSpace(pub)) _activePublishers.Add(pub);
                        if (!string.IsNullOrWhiteSpace(dir)) _activeDirectories.Add(dir);
                        pubAcquired = true;
                    }
                }

                if (!pubAcquired)
                {
                    await Task.Delay(500, cancellationToken);
                }
            }

            // 3. MSI Lock
            bool msiLocked = false;
            if (item.Entry.UninstallerKind == UninstallerType.Msiexec)
            {
                await _msiLock.WaitAsync(cancellationToken);
                msiLocked = true;
                await WaitForActiveMsiProcesses(cancellationToken);
            }

            // 4. One Loud Limit (if uninstaller requires GUI)
            bool isQuiet = item.PreferQuiet && !string.IsNullOrWhiteSpace(item.Entry.QuietUninstallString);
            bool loudLocked = false;
            if (!isQuiet)
            {
                await _loudLimitLock.WaitAsync(cancellationToken);
                loudLocked = true;
            }

            try
            {
                item.Status = BulkStatus.Running;
                item.StatusMessage = "Çalıştırılıyor...";
                OnItemStarted?.Invoke(item);
                OnLog?.Invoke($"Kaldırma başladı: {item.Entry.DisplayName}");

                // Pre-kill locking processes in install folder
                PreKillLockingProcesses(item.Entry);

                // Stop services in install folder
                PreStopServices(item.Entry);

                // Execute Uninstaller Process
                int exitCode = await ExecuteUninstallerProcessAsync(item, cancellationToken);
                item.ExitCode = exitCode;

                if (exitCode == 0 || exitCode == 3010) // 3010 is ERROR_SUCCESS_REBOOT_REQUIRED
                {
                    item.Status = BulkStatus.Succeeded;
                    item.StatusMessage = exitCode == 3010 ? "Başarılı (Yeniden başlatma gerekli)" : "Başarıyla kaldırıldı";
                }
                else
                {
                    item.Status = BulkStatus.Failed;
                    item.StatusMessage = $"Hata (Çıkış kodu: {exitCode})";
                }
            }
            catch (Exception ex)
            {
                item.Status = BulkStatus.Failed;
                item.StatusMessage = $"Hata: {ex.Message}";
            }
            finally
            {
                lock (_stateLock)
                {
                    if (!string.IsNullOrWhiteSpace(pub)) _activePublishers.Remove(pub);
                    if (!string.IsNullOrWhiteSpace(dir)) _activeDirectories.Remove(dir);
                }

                if (msiLocked) _msiLock.Release();
                if (loudLocked) _loudLimitLock.Release();

                OnItemCompleted?.Invoke(item);
                OnLog?.Invoke($"Tamamlandı [{item.Status}]: {item.Entry.DisplayName}");
            }
        }

        private async Task<int> ExecuteUninstallerProcessAsync(BulkUninstallEntry item, CancellationToken token)
        {
            string command = item.PreferQuiet && !string.IsNullOrWhiteSpace(item.Entry.QuietUninstallString)
                ? item.Entry.QuietUninstallString
                : item.Entry.UninstallString;

            if (string.IsNullOrWhiteSpace(command))
            {
                // Simple delete fallback for orphaned software
                if (item.Entry.IsOrphaned && Directory.Exists(item.Entry.InstallLocation))
                {
                    Directory.Delete(item.Entry.InstallLocation, true);
                    return 0;
                }
                return -1;
            }

            var (fileName, args) = SplitCommand(command);

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = true,
                CreateNoWindow = item.PreferQuiet
            };

            if (!string.IsNullOrWhiteSpace(item.Entry.InstallLocation) && Directory.Exists(item.Entry.InstallLocation))
            {
                psi.WorkingDirectory = item.Entry.InstallLocation;
            }

            using var process = Process.Start(psi);
            if (process == null) return -1;

            if (EnableAutomatizer)
            {
                NotifyAutomatizerDaemon(process.Id);
            }

            await process.WaitForExitAsync(token);
            return process.ExitCode;
        }

        private static void NotifyAutomatizerDaemon(int pid)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", "LDUninstallerDaemon", PipeDirection.Out);
                    client.Connect(1000);
                    using var writer = new StreamWriter(client);
                    writer.WriteLine($"ATTACH:{pid}");
                    writer.Flush();
                }
                catch { }
            });
        }

        private static void PreKillLockingProcesses(ApplicationUninstallerEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.InstallLocation) || !Directory.Exists(entry.InstallLocation))
                return;

            try
            {
                string norm = Path.GetFullPath(entry.InstallLocation).TrimEnd('\\');
                var procs = Process.GetProcesses();
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.Id == Process.GetCurrentProcess().Id) continue;
                        string? mainModule = p.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(mainModule))
                        {
                            if (Path.GetFullPath(mainModule).StartsWith(norm, StringComparison.OrdinalIgnoreCase))
                            {
                                Win32.GentleKillProcess(p, 2000);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void PreStopServices(ApplicationUninstallerEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.InstallLocation) || !Directory.Exists(entry.InstallLocation))
                return;

            try
            {
                string norm = Path.GetFullPath(entry.InstallLocation).TrimEnd('\\');
                var services = ServiceController.GetServices();
                foreach (var sc in services)
                {
                    try
                    {
                        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{sc.ServiceName}");
                        string? path = key?.GetValue("ImagePath")?.ToString();
                        if (!string.IsNullOrWhiteSpace(path) && path.StartsWith(norm, StringComparison.OrdinalIgnoreCase))
                        {
                            if (sc.Status == ServiceControllerStatus.Running)
                            {
                                sc.Stop();
                                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(3));
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static async Task WaitForActiveMsiProcesses(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var msiProcs = Process.GetProcessesByName("msiexec");
                if (msiProcs.Length <= 1)
                    break;
                await Task.Delay(1000, token);
            }
        }

        private static bool IsSerializedType(UninstallerType type)
        {
            return type is UninstallerType.WindowsFeature
                or UninstallerType.WindowsUpdate
                or UninstallerType.InstallShield
                or UninstallerType.SdbInst
                or UninstallerType.PowerShell;
        }

        private static (string fileName, string arguments) SplitCommand(string command)
        {
            string trimmed = command.Trim();
            if (trimmed.StartsWith("\""))
            {
                int endQuote = trimmed.IndexOf('\"', 1);
                if (endQuote > 0)
                {
                    string file = trimmed.Substring(1, endQuote - 1);
                    string args = endQuote + 1 < trimmed.Length ? trimmed[(endQuote + 1)..].Trim() : "";
                    return (file, args);
                }
            }

            int space = trimmed.IndexOf(' ');
            if (space > 0)
            {
                return (trimmed[..space], trimmed[(space + 1)..].Trim());
            }

            return (trimmed, "");
        }
    }
}

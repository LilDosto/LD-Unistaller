using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LDUninstaller.Core.Bulk;
using LDUninstaller.Core.Discovery;
using LDUninstaller.Core.InfoAdders;
using LDUninstaller.Core.Junk;
using LDUninstaller.Core.Models;
using LDUninstaller.Core.Startup;
using LDUninstaller.Core.Tweaks;
using LDUninstaller.UI.Dialogs;

namespace LDUninstaller.UI
{
    public partial class MainWindow : Window
    {
        private List<ApplicationUninstallerEntry> _allEntries = new();
        private List<ApplicationUninstallerEntry> _filteredEntries = new();
        private readonly HashSet<ApplicationUninstallerEntry> _selectedApps = new();
        private List<DrivePartitionInfo> _allDrives = new();
        private string? _selectedDriveFilter = null;
        private readonly System.Windows.Threading.DispatcherTimer _searchDebounceTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(160)
        };

        public MainWindow()
        {
            TraceLog("1. MainWindow ctor");
            InitializeComponent();

            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (ver != null && txtAppVersion != null)
                txtAppVersion.Text = $"v{ver.Major}.{ver.Minor}.{ver.Build}";

            Closing += (s, e) => TraceLog($"WINDOW CLOSING! Reason: {e.Cancel}");
            Closed += (s, e) => TraceLog("WINDOW CLOSED!");
            Loaded += MainWindow_Loaded;

            _searchDebounceTimer.Tick += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                ApplyFilter();
            };

            miniTreeMap.OnItemSelected += SelectAppFromTreemap;
            fullTreeMap.OnItemSelected += SelectAppFromTreemap;
            TraceLog("2. MainWindow ctor finished");
        }

        private static void TraceLog(string msg)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trace.log");
                File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            }
            catch { }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            TraceLog("3. MainWindow_Loaded start");
            try
            {
                await RefreshDataAsync();
                TraceLog("4. RefreshDataAsync finished");
                _ = LoadStartupEntriesAsync();
                _ = LoadDebloatTweaksAsync();
            }
            catch (Exception ex)
            {
                TraceLog($"LOADED EXCEPTION: {ex}");
            }
        }

        private async Task RefreshDataAsync()
        {
            TraceLog("RefreshDataAsync: DiscoverAllAsync starting");
            txtFooterStatus.Text = "Sistemdeki tüm yazılımlar taranıyor...";

            var pipeline = new DiscoveryPipeline();
            pipeline.OnProgress += (msg, pct) =>
            {
                Dispatcher.Invoke(() => txtFooterStatus.Text = $"{msg} (%{pct})");
            };

            var list = await pipeline.DiscoverAllAsync();
            TraceLog($"RefreshDataAsync: DiscoverAllAsync returned {list.Count} items");

            txtFooterStatus.Text = "Eksik bilgiler, boyutlar ve sertifikalar tamamlanıyor...";
            await Task.Run(() =>
            {
                var adder = new InfoAdderManager();
                adder.ProcessAll(list);
            });
            TraceLog("RefreshDataAsync: ProcessAll finished");

            _allEntries = list;
            _selectedApps.Clear();
            _allDrives = DriveAnalyzer.AnalyzeDrives(_allEntries);
            UpdateDriveTabs();
            ApplyFilter();
            TraceLog("RefreshDataAsync: ApplyFilter finished");

            txtFooterStatus.Text = "Tarama tamamlandı. Sistem hazır.";
        }

        private void UpdateDriveTabs()
        {
            if (wpDriveTabs == null || pnlDriveTabs == null) return;

            // Only show drive tabs if there is more than 1 drive
            if (_allDrives.Count <= 1)
            {
                pnlDriveTabs.Visibility = Visibility.Collapsed;
                _selectedDriveFilter = null;
                return;
            }

            pnlDriveTabs.Visibility = Visibility.Visible;
            wpDriveTabs.Children.Clear();

            // 1. "Tüm Diskler" Tab
            long totalUsed = _allDrives.Sum(d => d.UsedBytes);
            long totalCap = _allDrives.Sum(d => d.TotalBytes);

            var rbAll = new RadioButton
            {
                Content = $"🌐 Tüm Diskler ({_allDrives.Count} Sürücü)",
                IsChecked = string.IsNullOrEmpty(_selectedDriveFilter),
                Style = (Style)FindResource("SidebarNavBtn"),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 12
            };
            rbAll.Checked += (s, e) =>
            {
                _selectedDriveFilter = null;
                ApplyFilter();
            };
            wpDriveTabs.Children.Add(rbAll);

            // 2. Each Connected Drive Tab
            foreach (var drive in _allDrives)
            {
                var rb = new RadioButton
                {
                    Content = $"{drive.DisplayTitle}  •  {drive.DisplayStats}",
                    IsChecked = string.Equals(_selectedDriveFilter, drive.DriveLetter, StringComparison.OrdinalIgnoreCase),
                    Style = (Style)FindResource("SidebarNavBtn"),
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 0, 8, 0),
                    FontSize = 12,
                    Tag = drive.DriveLetter
                };
                rb.Checked += (s, e) =>
                {
                    _selectedDriveFilter = drive.DriveLetter;
                    ApplyFilter();
                };
                wpDriveTabs.Children.Add(rb);
            }
        }

        private void ApplyFilter()
        {
            if (dgApps == null || miniTreeMap == null || fullTreeMap == null || txtFooterStats == null)
                return;

            string query = txtSearch?.Text?.Trim().ToLowerInvariant() ?? "";

            bool onlyDesktop = chipDesktop?.IsChecked == true;
            bool onlyStore = chipStore?.IsChecked == true;
            bool onlySteam = chipSteam?.IsChecked == true;
            bool onlyOrphan = chipOrphan?.IsChecked == true;
            bool onlySystem = chipSystem?.IsChecked == true;

            _filteredEntries = _allEntries.Where(app =>
            {
                // Drive filter (C:, D:, etc.)
                if (!string.IsNullOrEmpty(_selectedDriveFilter))
                {
                    if (string.IsNullOrWhiteSpace(app.InstallLocation)) return false;
                    try
                    {
                        string? root = Path.GetPathRoot(app.InstallLocation)?.TrimEnd('\\');
                        if (!string.Equals(root, _selectedDriveFilter, StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                    catch { return false; }
                }

                if (!string.IsNullOrEmpty(query))
                {
                    bool matchName = app.DisplayName.ToLowerInvariant().Contains(query);
                    bool matchPub = app.Publisher.ToLowerInvariant().Contains(query);
                    if (!matchName && !matchPub) return false;
                }

                if (onlyDesktop && (app.UninstallerKind == UninstallerType.StoreApp || app.SystemComponent || app.IsOrphaned)) return false;
                if (onlyStore && app.UninstallerKind != UninstallerType.StoreApp) return false;
                if (onlySteam && app.UninstallerKind != UninstallerType.Steam) return false;
                if (onlyOrphan && !app.IsOrphaned) return false;
                if (onlySystem && !app.SystemComponent && !app.IsUpdate) return false;

                return true;
            }).ToList();

            dgApps.ItemsSource = _filteredEntries;
            miniTreeMap.SetData(_filteredEntries);
            fullTreeMap.SetData(_filteredEntries);

            long totalBytes = _filteredEntries.Sum(x => x.EstimatedSize.Bytes);

            if (!string.IsNullOrEmpty(_selectedDriveFilter))
            {
                var drive = _allDrives.FirstOrDefault(d => string.Equals(d.DriveLetter, _selectedDriveFilter, StringComparison.OrdinalIgnoreCase));
                if (drive != null)
                {
                    txtFooterStats.Text = $"{drive.DisplayTitle} | {_filteredEntries.Count} Uygulama ({FileSize.FromBytes(totalBytes)}) | {FileSize.FromBytes(drive.FreeBytes)} Boş Alan";
                    if (txtDriveSelectedInfo != null)
                        txtDriveSelectedInfo.Text = $"{drive.DisplayTitle}: {drive.DisplayStats}";
                }
                else
                {
                    txtFooterStats.Text = $"{_filteredEntries.Count} Uygulama | {FileSize.FromBytes(totalBytes)} Toplam Boyut";
                }
            }
            else
            {
                if (_allDrives.Count > 1)
                {
                    long totalUsedAll = _allDrives.Sum(d => d.UsedBytes);
                    long totalCapAll = _allDrives.Sum(d => d.TotalBytes);
                    txtFooterStats.Text = $"{_filteredEntries.Count} Uygulama | {FileSize.FromBytes(totalBytes)} Toplam Boyut | {_allDrives.Count} Sürücü";
                    if (txtDriveSelectedInfo != null)
                        txtDriveSelectedInfo.Text = $"Tüm Sürücüler ({FileSize.FromBytes(totalUsedAll)} / {FileSize.FromBytes(totalCapAll)})";
                }
                else
                {
                    txtFooterStats.Text = $"{_filteredEntries.Count} Uygulama | {FileSize.FromBytes(totalBytes)} Toplam Boyut";
                }
            }
        }

        private void SelectAppFromTreemap(ApplicationUninstallerEntry app)
        {
            navApps.IsChecked = true;
            dgApps.SelectedItem = app;
            dgApps.ScrollIntoView(app);
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }
        private void FilterChip_Checked(object sender, RoutedEventArgs e) => ApplyFilter();

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (viewApps == null) return;

            viewApps.Visibility = navApps.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            viewTreemap.Visibility = navTreemap.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            viewStartup.Visibility = navStartup.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            viewDebloat.Visibility = navDebloat.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private List<ApplicationUninstallerEntry> GetSelectedOrCurrentApps()
        {
            var checkedItems = _filteredEntries.Where(x => x.IsSelected).ToList();
            if (checkedItems.Count > 0) return checkedItems;

            var list = dgApps.SelectedItems.OfType<ApplicationUninstallerEntry>().ToList();
            if (list.Count > 0) return list;

            if (dgApps.SelectedItem is ApplicationUninstallerEntry single)
                return new List<ApplicationUninstallerEntry> { single };

            return new List<ApplicationUninstallerEntry>();
        }

        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            var targets = GetSelectedOrCurrentApps();
            if (targets.Count == 0)
            {
                MessageBox.Show("Lütfen listeden kaldırmak istediğiniz yazılımı seçin.", "Yazılım Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new BulkWizardWindow(targets) { Owner = this };
            bool? result = dlg.ShowDialog();

            if (result == true)
            {
                txtFooterStatus.Text = "Kaldırma tamamlandı. Kalıntılar taranıyor...";

                var jm = new JunkManager();
                var junks = await Task.Run(() => jm.ScanMultiple(targets));

                var titleEntry = targets.Count == 1 
                    ? targets[0] 
                    : new ApplicationUninstallerEntry { DisplayName = $"{targets.Count} Kaldırılan Yazılım" };

                var cleanWin = new LeftoverCleanWindow(titleEntry, junks)
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                cleanWin.ShowDialog();
                await RefreshDataAsync();
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON Dosyası (*.json)|*.json|CSV Dosyası (*.csv)|*.csv|BCU Listesi (*.bcul)|*.bcul",
                FileName = $"installed_apps_{DateTime.Now:yyyyMMdd}"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    string ext = Path.GetExtension(sfd.FileName).ToLowerInvariant();
                    if (ext == ".json")
                    {
                        File.WriteAllText(sfd.FileName, JsonSerializer.Serialize(_allEntries, new JsonSerializerOptions { WriteIndented = true }));
                    }
                    else if (ext == ".csv")
                    {
                        using var sw = new StreamWriter(sfd.FileName);
                        sw.WriteLine("DisplayName,Publisher,DisplayVersion,EstimatedSize,UninstallerKind");
                        foreach (var a in _allEntries)
                            sw.WriteLine($"\"{a.DisplayName}\",\"{a.Publisher}\",\"{a.DisplayVersion}\",\"{a.EstimatedSize}\",\"{a.UninstallerKind}\"");
                    }
                    else
                    {
                        File.WriteAllLines(sfd.FileName, _allEntries.Select(a => a.DisplayName));
                    }
                    MessageBox.Show("Yazılım listesi başarıyla kaydedildi.", "Dışa Aktarma Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Hata: {ex.Message}", "Dışa Aktarılamadı", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshDataAsync();

        private void BtnHideMiniTreemap_Click(object sender, RoutedEventArgs e)
        {
            pnlMiniTreemap.Visibility = Visibility.Collapsed;
        }

        private void MenuOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var targets = GetSelectedOrCurrentApps();
            if (targets.Count > 0 && !string.IsNullOrWhiteSpace(targets[0].InstallLocation) && Directory.Exists(targets[0].InstallLocation))
            {
                Process.Start("explorer.exe", targets[0].InstallLocation);
            }
        }

        private void MenuOpenReg_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("regedit.exe");
        }

        private void MenuSearchWeb_Click(object sender, RoutedEventArgs e)
        {
            var targets = GetSelectedOrCurrentApps();
            if (targets.Count > 0)
            {
                string q = Uri.EscapeDataString(targets[0].DisplayName);
                Process.Start(new ProcessStartInfo($"https://www.google.com/search?q={q}") { UseShellExecute = true });
            }
        }

        // Startup Tab Handlers
        private async Task LoadStartupEntriesAsync()
        {
            var entries = await Task.Run(() => StartupManager.GetAllStartupEntries());
            dgStartup.ItemsSource = entries;
        }

        private async void BtnToggleStartup_Click(object sender, RoutedEventArgs e)
        {
            if (dgStartup.SelectedItem is StartupEntry entry)
            {
                bool newState = !entry.IsEnabled;
                bool ok = await Task.Run(() => StartupManager.ToggleStartupEntry(entry, newState));
                if (ok)
                {
                    await LoadStartupEntriesAsync();
                }
                else
                {
                    MessageBox.Show("Başlangıç girdisi durumu değiştirilemedi. Yönetici yetkisi gerekebilir.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        // Debloat Tab Handlers
        private async Task LoadDebloatTweaksAsync()
        {
            var tweaks = await Task.Run(() => WindowsTweaks.GetDefaultTweaks());
            icDebloat.ItemsSource = tweaks;
        }

        private async void BtnToggleDebloatItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DebloatTweak tweak)
            {
                bool apply = !tweak.IsApplied;
                bool ok = await Task.Run(() => apply ? tweak.Apply() : tweak.Revert());
                if (ok)
                {
                    await LoadDebloatTweaksAsync();
                }
            }
        }

        private async void BtnApplyAllDebloat_Click(object sender, RoutedEventArgs e)
        {
            var tweaks = await Task.Run(() =>
            {
                var list = WindowsTweaks.GetDefaultTweaks();
                foreach (var t in list) t.Apply();
                return list;
            });

            await LoadDebloatTweaksAsync();
            MessageBox.Show("Tüm önerilen Windows ince ayarları ve telemetri kapatmaları uygulandı!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Window TitleBar Chrome Handlers
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnMaximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}

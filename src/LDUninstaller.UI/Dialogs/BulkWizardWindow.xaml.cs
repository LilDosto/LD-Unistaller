using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using LDUninstaller.Core.Bulk;
using LDUninstaller.Core.Models;

namespace LDUninstaller.UI.Dialogs
{
    public partial class BulkWizardWindow : Window
    {
        private readonly List<ApplicationUninstallerEntry> _targetApps;
        private BulkUninstallTask? _bulkTask;
        private CancellationTokenSource? _cts;

        public BulkWizardWindow(List<ApplicationUninstallerEntry> apps)
        {
            InitializeComponent();
            _targetApps = apps;
            Loaded += BulkWizardWindow_Loaded;
        }

        private void BulkWizardWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_targetApps.Count == 1)
            {
                txtTitle.Text = $"Kaldırma - {_targetApps[0].DisplayName}";
                txtSubHeader.Text = $"'{_targetApps[0].DisplayName}' kaldırılacak ve işlem bitiminde sistem kalıntıları otomatik olarak ekrana getirilecektir.";
                pnlConcurrent.Visibility = Visibility.Collapsed;
                btnStart.Content = "🗑️ Şimdi Kaldır";
            }
            else
            {
                txtTitle.Text = $"Toplu Kaldırma - {_targetApps.Count} Yazılım";
                txtSubHeader.Text = $"{_targetApps.Count} adet yazılım akıllı çakışma matrisiyle sırayla kaldırılacak ve işlem bitiminde tüm kalıntılar ekrana getirilecektir.";
                pnlConcurrent.Visibility = Visibility.Visible;
                btnStart.Content = $"🚀 Kaldırmayı Başlat ({_targetApps.Count} Yazılım)";
            }

            var items = _targetApps.Select(a => new BulkUninstallEntry(a, true)).ToList();
            dgQueue.ItemsSource = items;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            CancelOrClose();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            CancelOrClose();
        }

        private void CancelOrClose()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
            else
            {
                Close();
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            btnStart.IsEnabled = false;
            btnCancel.Content = "İptal Et";
            chkRestorePoint.IsEnabled = false;
            chkPreferQuiet.IsEnabled = false;
            cmbConcurrent.IsEnabled = false;

            _cts = new CancellationTokenSource();
            int concurrent = cmbConcurrent.SelectedIndex + 1;

            _bulkTask = new BulkUninstallTask(_targetApps, chkPreferQuiet.IsChecked == true)
            {
                CreateRestorePoint = chkRestorePoint.IsChecked == true,
                MaxConcurrentQuiet = concurrent,
                EnableAutomatizer = true
            };

            dgQueue.ItemsSource = _bulkTask.Items;
            pbProgress.Maximum = _targetApps.Count;
            pbProgress.Value = 0;

            _bulkTask.OnItemStarted += item =>
            {
                Dispatcher.Invoke(() => dgQueue.Items.Refresh());
            };

            _bulkTask.OnItemCompleted += item =>
            {
                Dispatcher.Invoke(() =>
                {
                    dgQueue.Items.Refresh();
                    pbProgress.Value = Math.Min(pbProgress.Maximum, pbProgress.Value + 1);
                });
            };

            _bulkTask.OnLog += msg =>
            {
                Dispatcher.Invoke(() => txtStatus.Text = msg);
            };

            try
            {
                await _bulkTask.ExecuteAsync(_cts.Token);
                txtStatus.Text = "Kaldırma tamamlandı!";
                await Task.Delay(400);

                DialogResult = true;
                Close();
            }
            catch (OperationCanceledException)
            {
                txtStatus.Text = "Kullanıcı tarafından iptal edildi.";
            }
            catch (Exception ex)
            {
                txtStatus.Text = $"Hata: {ex.Message}";
            }
            finally
            {
                btnCancel.Content = "Kapat";
            }
        }
    }
}

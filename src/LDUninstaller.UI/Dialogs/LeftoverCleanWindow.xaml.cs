using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using LDUninstaller.Core.Junk;
using LDUninstaller.Core.Models;

namespace LDUninstaller.UI.Dialogs
{
    public partial class LeftoverCleanWindow : Window
    {
        private readonly List<IJunkResult> _junkItems;

        public LeftoverCleanWindow(ApplicationUninstallerEntry app, List<IJunkResult> junkItems)
        {
            _junkItems = junkItems ?? new List<IJunkResult>();
            InitializeComponent();

            txtTitle.Text = $"'{app.DisplayName}' Artık ve Kalıntı Dosyaları";

            if (_junkItems.Count > 0)
            {
                dgJunk.Visibility = Visibility.Visible;
                pnlNoJunk.Visibility = Visibility.Collapsed;
                btnClean.Visibility = Visibility.Visible;
                chkSelectAll.Visibility = Visibility.Visible;

                foreach (var j in _junkItems)
                {
                    if (j.Confidence > ConfidenceLevel.Bad)
                        j.Selected = true;
                }

                dgJunk.ItemsSource = _junkItems;
                chkSelectAll.IsChecked = true;
                UpdateCount();
            }
            else
            {
                dgJunk.Visibility = Visibility.Collapsed;
                pnlNoJunk.Visibility = Visibility.Visible;
                btnClean.Visibility = Visibility.Collapsed;
                chkSelectAll.Visibility = Visibility.Collapsed;
                txtCount.Text = "0 kalıntı bulundu (Sistem tertemiz)";
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ChkSelectAll_Changed(object sender, RoutedEventArgs e)
        {
            if (_junkItems == null || dgJunk == null || txtCount == null) return;

            bool sel = chkSelectAll?.IsChecked == true;
            foreach (var j in _junkItems)
            {
                if (j.Confidence > ConfidenceLevel.Bad)
                    j.Selected = sel;
            }
            dgJunk.Items.Refresh();
            UpdateCount();
        }

        private void JunkItemCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateCount();
        }

        private void UpdateCount()
        {
            if (_junkItems == null || txtCount == null || btnClean == null) return;

            int count = _junkItems.Count(x => x.Selected);
            txtCount.Text = $"{count} / {_junkItems.Count} öğe seçildi";
            btnClean.IsEnabled = count > 0;
        }

        private void BtnClean_Click(object sender, RoutedEventArgs e)
        {
            var toDelete = _junkItems.Where(x => x.Selected && x.Confidence > ConfidenceLevel.Bad).ToList();
            if (toDelete.Count == 0) return;

            var res = MessageBox.Show(
                $"{toDelete.Count} adet kalıntı öğe güvenle silinecek. Silme öncesinde .reg yedeği alınacaktır.\nDevam edilsin mi?",
                "Kalıntı Temizleme Onayı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            int cleaned = JunkManager.CleanJunk(toDelete, JunkManager.BackupDirectory);
            MessageBox.Show(
                $"{cleaned} adet kalıntı dosya ve kayıt defteri anahtarı başarıyla temizlendi!\nKayıt defteri yedekleri:\n{JunkManager.BackupDirectory}",
                "Temizlik Tamamlandı",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Close();
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;

namespace LDUninstaller.Launcher
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();

            // 1. Architecture check
            var arch = RuntimeInformation.OSArchitecture;

            // 2. Administrator check
            if (!IsAdministrator())
            {
                try
                {
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath;
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = string.Join(" ", args),
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    Process.Start(psi);
                    return;
                }
                catch (Exception)
                {
                    MessageBox.Show(
                        "Bu uygulamanın tüm yazılımları eksiksiz tespit edebilmesi ve kalıntıları temizleyebilmesi için Yönetici (Administrator) yetkilerine ihtiyacı vardır.",
                        "Yönetici İzni Gerekli - LD Uninstaller",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            // 3. Locate LDUninstaller.UI.exe
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string uiPath = Path.Combine(baseDir, "LDUninstaller.UI.exe");

            if (!File.Exists(uiPath))
            {
                // In Debug / Development environment check
                string siblingUi = Path.GetFullPath(Path.Combine(baseDir, "..", "LDUninstaller.UI", "LDUninstaller.UI.exe"));
                if (File.Exists(siblingUi))
                {
                    uiPath = siblingUi;
                }
            }

            if (File.Exists(uiPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = uiPath,
                    Arguments = string.Join(" ", args),
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(uiPath)
                };
                Process.Start(psi);
            }
            else
            {
                MessageBox.Show(
                    $"Ana arayüz yürütülebilir dosyası bulunamadı:\n{uiPath}",
                    "Başlatma Hatası",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}

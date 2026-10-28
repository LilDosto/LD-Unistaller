using System;
using System.IO;
using System.Windows;

namespace LDUninstaller.UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "trace.log");
            AppDomain.CurrentDomain.ProcessExit += (s, args) =>
            {
                try { File.AppendAllText(logPath, $"PROCESS EXIT TRIGGERED! Environment.StackTrace:\n{Environment.StackTrace}\n"); } catch { }
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try { File.AppendAllText(logPath, $"UNHANDLED: {args.ExceptionObject}\n"); } catch { }
            };

            DispatcherUnhandledException += (s, args) =>
            {
                try { File.WriteAllText(logPath, args.Exception.ToString()); } catch { }
                MessageBox.Show(
                    $"Arayüz Hatası:\n{args.Exception.Message}\n\n{args.Exception}",
                    "Hata - LD Uninstaller",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}

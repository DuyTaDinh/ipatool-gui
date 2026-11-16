using System;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Threading;
using IpaToolGui.Services;

namespace IpaToolGui
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            base.OnStartup(e);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.AppendAllText(Path.Combine(AppPaths.Root, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\r\n\r\n");
            }
            catch
            {
                // ignore logging failures
            }

            MessageBox.Show(Loc.F("crash_message", e.Exception.Message, AppPaths.Root),
                "IPA Tool GUI", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}

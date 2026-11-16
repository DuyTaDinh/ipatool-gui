using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace IpaToolGui.Services
{
    /// <summary>Swaps the color dictionary (Themes/Colors.*.xaml); every brush is used via DynamicResource.</summary>
    public static class ThemeManager
    {
        public const string Dark = "dark";
        public const string Light = "light";
        public const string System = "system";

        public static string Mode { get; private set; } = System;
        public static bool IsDark { get; private set; } = true;

        public static void Apply(string mode)
        {
            Mode = mode == Light || mode == Dark ? mode : System;
            IsDark = Mode == System ? SystemPrefersDark() : Mode == Dark;

            var dict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/IpaToolGui;component/Themes/Colors.{(IsDark ? "Dark" : "Light")}.xaml"),
            };

            var merged = Application.Current.Resources.MergedDictionaries;
            var existing = merged.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Colors."));
            if (existing != null) merged[merged.IndexOf(existing)] = dict;
            else merged.Insert(0, dict);

            foreach (Window w in Application.Current.Windows) ApplyTitleBar(w);
        }

        public static bool SystemPrefersDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Dark / light window title bar (Windows 10 20H1+ and Windows 11).</summary>
        public static void ApplyTitleBar(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                int value = IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int)); // older Windows 10 builds
            }
            catch
            {
                // not supported: keep the default title bar
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}

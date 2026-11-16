using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace IpaToolGui.Services
{
    public static class AppPaths
    {
        public static string Root { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IpaToolGui");

        public static string BinDir => Path.Combine(Root, "bin");
        public static string IpaToolExe => Path.Combine(BinDir, "ipatool.exe");
        public static string VersionFile => Path.Combine(BinDir, "version.txt");
        public static string SettingsFile => Path.Combine(Root, "settings.json");

        /// <summary>Where ipatool keeps its encrypted session (keyring files + cookies).</summary>
        public static string IpaToolStateDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ipatool");

        public static string DefaultDownloadDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "IPA");
    }

    public sealed class AppSettings
    {
        public string DownloadFolder { get; set; }
        public bool AutoPurchase { get; set; } = true;
        public bool AutoUpdate { get; set; } = true;
        public string LastEmail { get; set; }
        public string Country { get; set; }
        public string ProtectedPassphrase { get; set; }

        /// <summary>"auto" (follow Windows) or a code from <see cref="Loc.Languages"/>.</summary>
        public string Language { get; set; } = Loc.Auto;

        /// <summary>"system" (default: follow Windows), "dark" or "light".</summary>
        public string Theme { get; set; } = ThemeManager.System;

        public static AppSettings Load()
        {
            AppSettings s = null;
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                    s = Json.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8));
            }
            catch
            {
                // corrupted settings: start fresh
            }

            s = s ?? new AppSettings();
            if (string.IsNullOrWhiteSpace(s.DownloadFolder)) s.DownloadFolder = AppPaths.DefaultDownloadDir;
            if (string.IsNullOrWhiteSpace(s.Country)) s.Country = DefaultCountry();
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Root);
                File.WriteAllText(AppPaths.SettingsFile, Json.Serialize(this), Encoding.UTF8);
            }
            catch
            {
                // non-fatal
            }
        }

        /// <summary>
        /// On Windows ipatool keeps the session in an encrypted file keyring that needs a passphrase.
        /// We generate a random one and protect it with DPAPI (only this Windows user can read it).
        /// </summary>
        public string GetPassphrase()
        {
            if (!string.IsNullOrEmpty(ProtectedPassphrase))
            {
                try
                {
                    var raw = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedPassphrase), null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(raw);
                }
                catch
                {
                    // fall through and regenerate
                }
            }

            var bytes = new byte[24];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var pass = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            SetPassphrase(pass);
            return pass;
        }

        public void SetPassphrase(string pass)
        {
            var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(pass), null, DataProtectionScope.CurrentUser);
            ProtectedPassphrase = Convert.ToBase64String(enc);
            Save();
        }

        private static string DefaultCountry()
        {
            try
            {
                var c = RegionInfo.CurrentRegion.TwoLetterISORegionName.ToLowerInvariant();
                return c.Length == 2 ? c : "us";
            }
            catch
            {
                return "us";
            }
        }
    }

    public static class Json
    {
        private static readonly JavaScriptSerializer S = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static Dictionary<string, object> ParseObject(string s)
        {
            try { return S.DeserializeObject(s) as Dictionary<string, object>; }
            catch { return null; }
        }

        public static string Serialize(object o) => S.Serialize(o);
        public static T Deserialize<T>(string s) => S.Deserialize<T>(s);

        private static object Get(Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? v : null;

        public static string Str(this Dictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            return v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static long Long(this Dictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            if (v == null) return 0;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        public static double Dbl(this Dictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            if (v == null) return 0;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        public static bool? Bool(this Dictionary<string, object> d, string key) => Get(d, key) as bool?;

        public static DateTime? Date(this Dictionary<string, object> d, string key)
        {
            var s = d.Str(key);
            if (string.IsNullOrEmpty(s)) return null;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                : (DateTime?)null;
        }

        public static IEnumerable<Dictionary<string, object>> Objects(this Dictionary<string, object> d, string key) =>
            (Get(d, key) as IEnumerable)?.OfType<Dictionary<string, object>>() ?? Enumerable.Empty<Dictionary<string, object>>();

        public static IEnumerable<string> Strings(this Dictionary<string, object> d, string key)
        {
            var v = Get(d, key);
            if (v is string || !(v is IEnumerable e)) return Enumerable.Empty<string>();
            return e.Cast<object>().Where(x => x != null).Select(x => Convert.ToString(x, CultureInfo.InvariantCulture));
        }
    }

    public static class Format
    {
        public static string Size(long bytes)
        {
            if (bytes <= 0) return "";
            string[] units = { "B", "KB", "MB", "GB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return v.ToString(i >= 2 ? "0.0" : "0", CultureInfo.InvariantCulture) + " " + units[i];
        }

        public static string Count(long n)
        {
            if (n >= 1_000_000) return (n / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (n >= 1_000) return (n / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return n.ToString(CultureInfo.InvariantCulture);
        }

        public static string Duration(TimeSpan t) =>
            t.TotalMinutes >= 1 ? Loc.F("duration_min", (int)t.TotalMinutes, t.Seconds) : Loc.F("duration_sec", Math.Max(1, t.Seconds));
    }

    /// <summary>Turns raw ipatool errors into localized guidance.</summary>
    public static class Errors
    {
        public static bool IsNotLoggedIn(string err)
        {
            if (string.IsNullOrEmpty(err)) return false;
            var e = err.ToLowerInvariant();
            return e.Contains("failed to get account") || e.Contains("integrity check failed") ||
                   e.Contains("not found in the keyring") || e.Contains("password token is expired");
        }

        private static readonly (Func<string, bool> Match, string Key)[] Rules =
        {
            (e => e.Contains("failed to get account") || e.Contains("integrity check failed") ||
                  e.Contains("not found in the keyring") || e.Contains("password token is expired"), "err_not_logged_in"),
            (e => e.Contains("license is required"), "err_license"),
            (e => e.Contains("purchasing paid apps is not supported"), "err_paid"),
            (e => e.Contains("subscription required"), "err_subscription"),
            (e => e.Contains("app not found"), "err_not_found"),
            (e => e.Contains("2fa code must contain"), "err_2fa_digits"),
            (e => e.Contains("fresh 2fa code"), "err_2fa_fresh"),
            (e => e.Contains("account is disabled"), "err_disabled"),
            (e => e.Contains("temporarily unavailable"), "err_unavailable"),
            (e => e.Contains("429") || e.Contains("too many requests"), "err_429"),
            (e => e.Contains("403"), "err_403"),
            (e => e.Contains("no such host") || e.Contains("dial tcp") || e.Contains("timeout") || e.Contains("connection"), "err_network"),
            (e => e.Contains("keychain passphrase"), "err_passphrase"),
        };

        public static string Friendly(string err)
        {
            if (string.IsNullOrWhiteSpace(err)) return Loc.T("err_unknown");
            var e = err.ToLowerInvariant();
            var rule = Rules.FirstOrDefault(r => r.Match(e));
            return rule.Key == null ? err : Loc.T(rule.Key) + Environment.NewLine + Environment.NewLine + Loc.F("err_details", err);
        }
    }
}

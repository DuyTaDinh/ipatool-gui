using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IpaToolGui.Services
{
    public sealed class ReleaseInfo
    {
        public string Tag { get; set; }
        public string Version { get; set; }
        public string AssetName { get; set; }
        public string AssetUrl { get; set; }
        public string ShaUrl { get; set; }
        public string Notes { get; set; }
        public string PageUrl { get; set; }
    }

    /// <summary>Finds, downloads, verifies and installs the latest ipatool release from GitHub.</summary>
    public static class Updater
    {
        public const string Repo = "majd/ipatool";
        public static readonly string ReleasesPage = $"https://github.com/{Repo}/releases";

        /// <summary>This GUI's own repository (used to tell users about new GUI releases).</summary>
        public const string GuiRepo = "DuyTaDinh/ipatool-gui";
        public static readonly string GuiRepoUrl = $"https://github.com/{GuiRepo}";

        /// <summary>Latest published release of this GUI, or null when there is none yet.</summary>
        public static async Task<ReleaseInfo> GetLatestGuiAsync()
        {
            using (var resp = await Http.GetAsync($"https://api.github.com/repos/{GuiRepo}/releases/latest").ConfigureAwait(false))
            {
                if (!resp.IsSuccessStatusCode) return null;
                var d = Json.ParseObject(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
                var tag = d.Str("tag_name");
                if (string.IsNullOrEmpty(tag)) return null;
                var exe = d.Objects("assets").FirstOrDefault(a => (a.Str("name") ?? "").EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                return new ReleaseInfo
                {
                    Tag = tag,
                    Version = tag.TrimStart('v', 'V'),
                    AssetName = exe?.Str("name"),
                    AssetUrl = exe?.Str("browser_download_url"),
                    Notes = d.Str("body"),
                    PageUrl = d.Str("html_url") ?? GuiRepoUrl + "/releases",
                };
            }
        }

        private static readonly HttpClient Http = CreateClient(true);
        private static readonly HttpClient NoRedirect = CreateClient(false);

        private static HttpClient CreateClient(bool redirect)
        {
            var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = redirect }) { Timeout = TimeSpan.FromMinutes(15) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("IpaToolGui/1.0");
            return c;
        }

        public static string Arch => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "amd64";

        public static string InstalledVersion()
        {
            if (!File.Exists(AppPaths.IpaToolExe)) return null;
            try
            {
                var v = File.Exists(AppPaths.VersionFile) ? File.ReadAllText(AppPaths.VersionFile).Trim() : "";
                return v.Length > 0 ? v : "?";
            }
            catch
            {
                return "?";
            }
        }

        public static bool IsNewer(string latest, string installed)
        {
            if (string.IsNullOrEmpty(installed) || installed == "?") return true;
            return Parse(latest) > Parse(installed);
        }

        private static Version Parse(string s)
        {
            s = (s ?? "").Trim().TrimStart('v', 'V');
            var cut = s.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut > 0) s = s.Substring(0, cut);
            return System.Version.TryParse(s, out var v) ? v : new Version(0, 0);
        }

        public static async Task<ReleaseInfo> GetLatestAsync()
        {
            Exception apiError;
            try
            {
                var json = await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest").ConfigureAwait(false);
                var d = Json.ParseObject(json) ?? throw new Exception(Loc.T("upd_bad_response"));
                var tag = d.Str("tag_name");
                var version = tag.TrimStart('v');
                var assets = d.Objects("assets").ToList();
                var wanted = $"ipatool-{version}-windows-{Arch}.tar.gz";
                var asset = assets.FirstOrDefault(a => a.Str("name") == wanted)
                            ?? assets.FirstOrDefault(a => (a.Str("name") ?? "").Contains("windows-" + Arch) && (a.Str("name") ?? "").EndsWith(".tar.gz"));
                if (asset == null) throw new Exception(Loc.F("upd_no_asset", tag, Arch));
                var sha = assets.FirstOrDefault(a => a.Str("name") == asset.Str("name") + ".sha256sum");
                return new ReleaseInfo
                {
                    Tag = tag,
                    Version = version,
                    AssetName = asset.Str("name"),
                    AssetUrl = asset.Str("browser_download_url"),
                    ShaUrl = sha?.Str("browser_download_url"),
                    Notes = d.Str("body"),
                    PageUrl = d.Str("html_url") ?? ReleasesPage,
                };
            }
            catch (Exception ex)
            {
                apiError = ex;
            }

            // Fallback when the GitHub API is rate-limited: follow the /releases/latest redirect.
            try
            {
                using (var resp = await NoRedirect.GetAsync($"https://github.com/{Repo}/releases/latest").ConfigureAwait(false))
                {
                    var location = resp.Headers.Location?.ToString();
                    if (string.IsNullOrEmpty(location) || !location.Contains("/tag/")) throw new Exception("no redirect");
                    var tag = location.Substring(location.LastIndexOf('/') + 1);
                    var version = tag.TrimStart('v');
                    var name = $"ipatool-{version}-windows-{Arch}.tar.gz";
                    var baseUrl = $"https://github.com/{Repo}/releases/download/{tag}/";
                    return new ReleaseInfo
                    {
                        Tag = tag, Version = version, AssetName = name,
                        AssetUrl = baseUrl + name, ShaUrl = baseUrl + name + ".sha256sum",
                        PageUrl = location,
                    };
                }
            }
            catch
            {
                throw new Exception(Loc.F("upd_no_github", apiError.Message), apiError);
            }
        }

        public static async Task InstallAsync(ReleaseInfo rel, IProgress<double> progress, CancellationToken ct = default)
        {
            Directory.CreateDirectory(AppPaths.BinDir);
            var tmp = Path.Combine(Path.GetTempPath(), "IpaToolGui_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                var archive = Path.Combine(tmp, rel.AssetName);
                using (var resp = await Http.GetAsync(rel.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    var total = resp.Content.Headers.ContentLength ?? -1;
                    using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = File.Create(archive))
                    {
                        var buf = new byte[81920];
                        long read = 0;
                        int n;
                        while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await dst.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                            read += n;
                            progress?.Report(total > 0 ? (double)read / total : -1);
                        }
                    }
                }

                progress?.Report(-1);
                if (!string.IsNullOrEmpty(rel.ShaUrl))
                {
                    string expected = null;
                    try
                    {
                        var text = await Http.GetStringAsync(rel.ShaUrl).ConfigureAwait(false);
                        expected = text.Split(new[] { ' ', '\t', '\r', '\n', '*' }, StringSplitOptions.RemoveEmptyEntries)
                                       .FirstOrDefault()?.ToLowerInvariant();
                    }
                    catch
                    {
                        // checksum file missing: continue without it
                    }

                    if (!string.IsNullOrEmpty(expected) && !string.Equals(Sha256(archive), expected, StringComparison.OrdinalIgnoreCase))
                        throw new Exception(Loc.T("upd_checksum"));
                }

                var exe = Path.Combine(tmp, "ipatool.exe");
                if (!TarGz.Extract(archive, name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), exe))
                    throw new Exception(Loc.T("upd_no_exe"));

                Replace(exe, rel.Version);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { /* ignore */ }
            }
        }

        /// <summary>Installs a user-provided ipatool.exe (offline fallback).</summary>
        public static string InstallFromFile(string path)
        {
            var psi = new ProcessStartInfo(path, "--version")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            string output;
            using (var p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
            }

            var m = Regex.Match(output, @"ipatool version (\S+)", RegexOptions.IgnoreCase);
            if (!m.Success) throw new Exception(Loc.T("upd_invalid_file"));
            Directory.CreateDirectory(AppPaths.BinDir);
            Replace(path, m.Groups[1].Value);
            return m.Groups[1].Value;
        }

        private static void Replace(string newExe, string version)
        {
            var target = AppPaths.IpaToolExe;
            if (File.Exists(target))
            {
                // A running exe can be renamed but not overwritten.
                var old = target + ".old";
                try { File.Delete(old); } catch { /* still locked from a previous run */ }
                if (File.Exists(old)) old = target + "." + DateTime.Now.Ticks + ".old";
                File.Move(target, old);
                try { File.Delete(old); } catch { /* deleted on next start */ }
            }

            File.Copy(newExe, target, true);
            File.WriteAllText(AppPaths.VersionFile, version, Encoding.ASCII);
        }

        public static void CleanupOldFiles()
        {
            try
            {
                if (!Directory.Exists(AppPaths.BinDir)) return;
                foreach (var f in Directory.GetFiles(AppPaths.BinDir, "*.old"))
                {
                    try { File.Delete(f); } catch { /* ignore */ }
                }
            }
            catch
            {
                // ignore
            }
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>Minimal reader for .tar.gz archives (ustar / GNU / pax headers).</summary>
    public static class TarGz
    {
        public static bool Extract(string archive, Func<string, bool> match, string destination)
        {
            using (var fs = File.OpenRead(archive))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress))
            {
                var header = new byte[512];
                string longName = null;
                while (ReadExactly(gz, header, 512) == 512)
                {
                    if (header.All(b => b == 0)) break;

                    var name = Field(header, 0, 100);
                    var prefix = Field(header, 345, 155);
                    if (!string.IsNullOrEmpty(prefix)) name = prefix + "/" + name;
                    if (longName != null) { name = longName; longName = null; }

                    var sizeText = Field(header, 124, 12).Trim();
                    var size = sizeText.Length == 0 ? 0 : Convert.ToInt64(sizeText, 8);
                    var type = (char)header[156];
                    var padded = (size + 511) / 512 * 512;

                    if (type == 'L')
                    {
                        var data = new byte[padded];
                        ReadExactly(gz, data, (int)padded);
                        longName = Encoding.UTF8.GetString(data, 0, (int)size).TrimEnd('\0');
                        continue;
                    }

                    if ((type == '0' || type == '\0') && match(name))
                    {
                        using (var output = File.Create(destination)) Copy(gz, output, size);
                        Skip(gz, padded - size);
                        return true;
                    }

                    Skip(gz, padded);
                }
            }
            return false;
        }

        private static string Field(byte[] b, int offset, int length)
        {
            var end = offset;
            while (end < offset + length && b[end] != 0) end++;
            return Encoding.UTF8.GetString(b, offset, end - offset);
        }

        private static int ReadExactly(Stream s, byte[] buf, int count)
        {
            int total = 0;
            while (total < count)
            {
                var n = s.Read(buf, total, count - total);
                if (n == 0) break;
                total += n;
            }
            return total;
        }

        private static void Copy(Stream src, Stream dst, long count)
        {
            var buf = new byte[81920];
            while (count > 0)
            {
                var n = src.Read(buf, 0, (int)Math.Min(buf.Length, count));
                if (n == 0) throw new EndOfStreamException();
                dst.Write(buf, 0, n);
                count -= n;
            }
        }

        private static void Skip(Stream s, long count)
        {
            var buf = new byte[81920];
            while (count > 0)
            {
                var n = s.Read(buf, 0, (int)Math.Min(buf.Length, count));
                if (n == 0) break;
                count -= n;
            }
        }
    }
}

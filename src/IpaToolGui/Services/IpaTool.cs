using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IpaToolGui.Models;

namespace IpaToolGui.Services
{
    public sealed class ToolResult
    {
        public int ExitCode { get; set; }
        public bool Cancelled { get; set; }
        public List<Dictionary<string, object>> Events { get; } = new List<Dictionary<string, object>>();
        public List<string> PlainOutput { get; } = new List<string>();

        public string Error
        {
            get
            {
                var err = Events.Where(e => e.Str("level") == "error")
                                .Select(e => e.Str("error") ?? e.Str("message"))
                                .LastOrDefault(s => !string.IsNullOrEmpty(s));
                if (err != null) return err;
                if (ExitCode != 0 && !Cancelled)
                    return PlainOutput.LastOrDefault() ?? Loc.F("err_exit_code", ExitCode);
                return null;
            }
        }

        public bool Ok => !Cancelled && ExitCode == 0 && Error == null;

        /// <summary>Last JSON event containing the given key.</summary>
        public Dictionary<string, object> Last(string key) => Events.LastOrDefault(e => e.ContainsKey(key));

        public bool HasMessage(string text) =>
            Events.Any(e => (e.Str("message") ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>Runs the ipatool CLI with JSON output and parses its events.</summary>
    public sealed class IpaTool
    {
        private static readonly Regex Ansi = new Regex(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);
        private static readonly HashSet<string> SecretFlags = new HashSet<string> { "-p", "--password", "--keychain-passphrase", "--auth-code" };

        private readonly Func<string> _passphrase;
        private int _running;

        public IpaTool(Func<string> passphrase) => _passphrase = passphrase;

        /// <summary>Raised (on a background thread) for every line worth showing in the log.</summary>
        public event Action<string> Output;

        public bool IsInstalled => File.Exists(AppPaths.IpaToolExe);
        public int RunningCount => Volatile.Read(ref _running);
        public volatile bool Updating;

        public async Task<ToolResult> RunAsync(IList<string> args, CancellationToken ct = default,
                                               Action<string> progress = null, bool interactive = false)
        {
            while (Updating && !ct.IsCancellationRequested) await Task.Delay(250).ConfigureAwait(false);

            var full = new List<string>(args) { "--format", "json", "--keychain-passphrase", _passphrase() };
            // Interactive mode is only used for downloads, so ipatool renders its progress bar.
            // All inputs are passed as flags and stdin is closed, so it can never block on a prompt.
            if (!interactive) full.Add("--non-interactive");

            Interlocked.Increment(ref _running);
            try
            {
                return await Task.Run(() => RunCore(full, ct, progress), CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }

        private ToolResult RunCore(List<string> args, CancellationToken ct, Action<string> progress)
        {
            var result = new ToolResult();
            if (ct.IsCancellationRequested) { result.Cancelled = true; return result; }

            var psi = new ProcessStartInfo(AppPaths.IpaToolExe, string.Join(" ", args.Select(Quote)))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = AppPaths.BinDir,
            };

            Emit("> ipatool " + Redact(args));

            using (var p = new Process { StartInfo = psi })
            {
                try
                {
                    p.Start();
                }
                catch (Exception ex)
                {
                    result.ExitCode = -1;
                    var msg = Loc.F("err_cannot_run", ex.Message);
                    result.PlainOutput.Add(msg);
                    Emit(msg);
                    return result;
                }

                try { p.StandardInput.Close(); } catch { /* ignore */ }

                using (ct.Register(() =>
                {
                    result.Cancelled = true;
                    try { if (!p.HasExited) p.Kill(); } catch { /* ignore */ }
                }))
                {
                    var errTask = Task.Run(() => Pump(p.StandardError, result, null));
                    Pump(p.StandardOutput, result, progress);
                    errTask.Wait();
                    p.WaitForExit();
                    result.ExitCode = p.ExitCode;
                }
            }

            if (result.Cancelled) Emit("(cancelled)");
            return result;
        }

        /// <summary>Reads a stream char by char so that '\r' progress-bar redraws are seen as separate segments.</summary>
        private void Pump(StreamReader reader, ToolResult result, Action<string> progress)
        {
            var sb = new StringBuilder();
            var buf = new char[4096];
            int n;
            try
            {
                while ((n = reader.Read(buf, 0, buf.Length)) > 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        var c = buf[i];
                        if (c == '\r' || c == '\n')
                        {
                            if (sb.Length > 0) { Handle(sb.ToString(), result, progress); sb.Clear(); }
                        }
                        else sb.Append(c);
                    }
                }
            }
            catch
            {
                // stream closed when the process was killed
            }

            if (sb.Length > 0) Handle(sb.ToString(), result, progress);
        }

        private void Handle(string segment, ToolResult result, Action<string> progress)
        {
            var t = Ansi.Replace(segment, "").Trim();
            if (t.Length == 0) return;

            if (t.StartsWith("{"))
            {
                var obj = Json.ParseObject(t);
                if (obj != null)
                {
                    lock (result) result.Events.Add(obj);
                    Emit(Describe(obj));
                    return;
                }
            }

            if (progress != null && t.Contains("%"))
            {
                progress(t);
                return;
            }

            lock (result) result.PlainOutput.Add(t);
            Emit(t);
        }

        private void Emit(string line) => Output?.Invoke(line);

        private static string Describe(Dictionary<string, object> e)
        {
            var level = (e.Str("level") ?? "info").ToUpperInvariant();
            var parts = e.Where(kv => kv.Key != "level" && kv.Key != "time")
                         .Select(kv => kv.Key + "=" + (kv.Value is string s ? s : Json.Serialize(kv.Value)));
            var text = string.Join("  ", parts);
            return (level.Length > 3 ? level.Substring(0, 3) : level) + "  " + (text.Length > 4000 ? text.Substring(0, 4000) + " …" : text);
        }

        private static string Redact(List<string> args)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(i > 0 && SecretFlags.Contains(args[i - 1]) ? "••••" : Quote(args[i]));
            }
            return sb.ToString();
        }

        /// <summary>Quotes an argument using the Windows command-line rules that Go's runtime parses.</summary>
        public static string Quote(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"', '\n', '\v' }) < 0) return a;
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (var c in a)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1).Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes).Append(c);
                }
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2).Append('"');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ commands

        public static List<string> AppArgs(AppItem app, bool includeBoth)
        {
            var a = new List<string>();
            if (includeBoth)
            {
                if (!string.IsNullOrEmpty(app.BundleId)) a.AddRange(new[] { "-b", app.BundleId });
                if (app.Id > 0) a.AddRange(new[] { "-i", app.Id.ToString() });
            }
            else if (app.Id > 0) a.AddRange(new[] { "-i", app.Id.ToString() });
            else a.AddRange(new[] { "-b", app.BundleId });

            if (!string.IsNullOrEmpty(app.Platform)) a.AddRange(new[] { "--platform", app.Platform });
            return a;
        }

        public Task<ToolResult> AuthInfo() => RunAsync(new[] { "auth", "info" });

        public Task<ToolResult> Login(string email, string password, string code, CancellationToken ct)
        {
            var a = new List<string> { "auth", "login", "-e", email, "-p", password };
            if (!string.IsNullOrEmpty(code)) a.AddRange(new[] { "--auth-code", code });
            return RunAsync(a, ct);
        }

        public Task<ToolResult> Revoke() => RunAsync(new[] { "auth", "revoke" });

        public Task<ToolResult> Search(string term, string platform, int limit)
        {
            var a = new List<string> { "search", term, "--limit", limit.ToString() };
            if (!string.IsNullOrEmpty(platform)) a.AddRange(new[] { "--platform", platform });
            return RunAsync(a);
        }

        public Task<ToolResult> ListPurchases(int page, int limit, string platform)
        {
            var a = new List<string> { "list-purchases", "--page", page.ToString(), "--max-results", limit.ToString() };
            if (!string.IsNullOrEmpty(platform)) a.AddRange(new[] { "--platform", platform });
            return RunAsync(a);
        }

        public Task<ToolResult> ListVersions(AppItem app, CancellationToken ct)
        {
            var a = new List<string> { "list-versions" };
            a.AddRange(AppArgs(app, false));
            return RunAsync(a, ct);
        }

        public Task<ToolResult> VersionMetadata(AppItem app, string versionId, CancellationToken ct)
        {
            var a = new List<string> { "get-version-metadata", "--external-version-id", versionId };
            a.AddRange(AppArgs(app, false));
            return RunAsync(a, ct);
        }

        public Task<ToolResult> Download(AppItem app, string outDir, bool purchase, string versionId,
                                         Action<string> progress, CancellationToken ct)
        {
            var a = new List<string> { "download", "-o", outDir };
            a.AddRange(AppArgs(app, true));
            if (purchase) a.Add("--purchase");
            if (!string.IsNullOrEmpty(versionId)) a.AddRange(new[] { "--external-version-id", versionId });
            return RunAsync(a, ct, progress, interactive: true);
        }
    }
}

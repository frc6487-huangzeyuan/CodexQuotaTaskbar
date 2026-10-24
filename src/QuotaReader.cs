using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexQuotaTaskbar
{
    public sealed class QuotaWindow
    {
        public double Remaining;
        public int Minutes;
        public DateTimeOffset? ResetsAt;
    }

    public sealed class QuotaSnapshot
    {
        public QuotaWindow FiveHour;
        public QuotaWindow Weekly;
        public string Plan;
        public DateTimeOffset UpdatedAt;
    }

    // Authentication remains inside the installed Codex CLI. No auth file is opened here.
    public sealed class QuotaReader : IDisposable
    {
        private Process active;
        private readonly object gate = new object();
        private bool disposed;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();

        public static string LocateCodex()
        {
            string overrideFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "codex-path.txt");
            if (File.Exists(overrideFile))
            {
                string configured = File.ReadAllText(overrideFile).Trim();
                if (File.Exists(configured) && configured.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return configured;
                throw new InvalidOperationException("codex-path.txt 中的 Codex EXE 路径无效。");
            }
            string localBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            if (Directory.Exists(localBin))
            {
                string latest = Directory.GetFiles(localBin, "codex.exe", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (latest != null) return latest;
            }
            foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                if (String.IsNullOrWhiteSpace(folder)) continue;
                string candidate = Path.Combine(folder.Trim().Trim('"'), "codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
            string npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai");
            if (Directory.Exists(npm))
            {
                string candidate = Directory.GetFiles(npm, "codex.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (candidate != null) return candidate;
            }
            throw new InvalidOperationException("未找到 Codex CLI。可在程序旁的 codex-path.txt 填入 codex.exe 的完整路径。");
        }

        public async Task<QuotaSnapshot> ReadAsync(CancellationToken token)
        {
            var process = new Process();
            process.StartInfo = new ProcessStartInfo(LocateCodex(), "app-server --stdio") {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };
            // Drain diagnostics without storing their contents or blocking the child process.
            process.ErrorDataReceived += delegate { };
            try
            {
                lock (gate)
                {
                    if (disposed) throw new OperationCanceledException();
                    process.Start();
                    active = process;
                }
                process.BeginErrorReadLine();
                DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                process.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"codex_quota_taskbar\",\"title\":\"Codex Quota Taskbar\",\"version\":\"1.0.0\"}}}");
                await Receive(process, 1, deadline, token).ConfigureAwait(false);
                process.StandardInput.WriteLine("{\"method\":\"initialized\"}");
                process.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
                var result = await Receive(process, 2, deadline, token).ConfigureAwait(false);
                var bucket = Map(Get(result, "rateLimits"));
                var buckets = Map(Get(result, "rateLimitsByLimitId"));
                if (buckets != null && Map(Get(buckets, "codex")) != null) bucket = Map(buckets["codex"]);
                if (bucket == null) throw new InvalidOperationException("Codex 未返回账户额度。请确认已使用 ChatGPT 账户登录 Codex。");
                var snapshot = new QuotaSnapshot { Plan = Convert.ToString(Get(bucket, "planType")), UpdatedAt = DateTimeOffset.Now };
                foreach (string key in new[] { "primary", "secondary" })
                {
                    var window = Map(Get(bucket, key));
                    if (window == null || Get(window, "usedPercent") == null || Get(window, "windowDurationMins") == null) continue;
                    int minutes = Convert.ToInt32(window["windowDurationMins"]);
                    var quota = new QuotaWindow {
                        Remaining = Math.Max(0, Math.Min(100, 100 - Convert.ToDouble(window["usedPercent"]))),
                        Minutes = minutes
                    };
                    if (Get(window, "resetsAt") != null)
                        quota.ResetsAt = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(Convert.ToDouble(window["resetsAt"]));
                    if (minutes == 300) snapshot.FiveHour = quota;
                    if (minutes == 10080) snapshot.Weekly = quota;
                }
                if (snapshot.FiveHour == null && snapshot.Weekly == null)
                    throw new InvalidOperationException("账户未提供 5 小时或每周额度窗口（API Key 账户可能不支持）。");
                return snapshot;
            }
            finally
            {
                lock (gate)
                {
                    if (active == process) active = null;
                    try { if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); } } catch (InvalidOperationException) { }
                    process.Dispose();
                }
            }
        }

        private async Task<Dictionary<string, object>> Receive(Process process, int id, DateTime deadline, CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var read = process.StandardOutput.ReadLineAsync();
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) throw new TimeoutException("查询额度超时，请检查网络后刷新。");
                if (await Task.WhenAny(read, Task.Delay(remaining, token)).ConfigureAwait(false) != read)
                {
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException("查询额度超时，请检查网络后刷新。");
                }
                string line = await read.ConfigureAwait(false);
                if (line == null) throw new InvalidOperationException("Codex 额度接口已关闭。请确认 Codex 能正常登录。");
                var reply = Map(json.DeserializeObject(line));
                if (reply == null || Get(reply, "id") == null || Convert.ToString(reply["id"]) != id.ToString()) continue;
                if (Get(reply, "error") != null)
                {
                    var error = Map(reply["error"]);
                    throw new InvalidOperationException("Codex 查询失败（" + Convert.ToString(Get(error, "code")) + "），请检查登录状态与网络后刷新。");
                }
                return Map(Get(reply, "result"));
            }
        }

        private static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object>; }
        private static object Get(Dictionary<string, object> map, string key) { object value; return map != null && map.TryGetValue(key, out value) ? value : null; }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                if (active != null) { try { if (!active.HasExited) active.Kill(); } catch (InvalidOperationException) { } }
            }
        }
    }
}

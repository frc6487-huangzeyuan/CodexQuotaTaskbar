using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexQuotaTaskbar
{
    internal static class Program
    {
        public static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        [STAThread]
        private static int Main(string[] args)
        {
            LaunchLog.FromStartup = Array.IndexOf(args, "--startup") >= 0;
            try
            {
                if (Array.IndexOf(args, "--enable-startup") >= 0)
                {
                    StartupRegistration.Set(true);
                    return 0;
                }
                Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
                bool created;
                using (var mutex = new Mutex(true, @"Local\CodexQuotaTaskbar.v1", out created))
                {
                    if (!created) return 0;
                    LaunchLog.Write("Starting");
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (var context = new QuotaApplication())
                    {
                        LaunchLog.Write("Running");
                        Application.Run(context);
                    }
                    LaunchLog.Write("Stopped");
                    mutex.ReleaseMutex();
                }
                return 0;
            }
            catch (Exception exception) { LaunchLog.Write("Failed", exception); return 1; }
        }
    }

    internal sealed class EventWindow : NativeWindow, IDisposable
    {
        public event Action<uint> Received;
        public EventWindow() { CreateHandle(new CreateParams { Caption = "CodexQuotaTaskbar.Messages", ExStyle = 0x80 }); }
        protected override void WndProc(ref Message message) { if (Received != null) Received((uint)message.Msg); base.WndProc(ref message); }
        public void Dispose() { DestroyHandle(); }
    }

    internal sealed class QuotaApplication : ApplicationContext
    {
        private readonly QuotaReader reader = new QuotaReader();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly NotifyIcon tray;
        private Icon currentIcon;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 2000 };
        private readonly EventWindow events = new EventWindow();
        private readonly uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        private readonly ToolStripMenuItem values, updateTime, errorItem;
        private QuotaSnapshot snapshot;
        private string error = "";
        private bool refreshing, exiting;
        private DateTime nextRefresh = DateTime.MinValue;
        private int interval = 60;
        private readonly string dataFolder = Path.Combine(Program.Root, "data");

        public QuotaApplication()
        {
            Directory.CreateDirectory(dataFolder);
            LoadSettings();
            menu.Font = new Font("Microsoft YaHei UI", 9);
            menu.Items.Add("外圈：5 小时 · 内圈：每周 · 余量").Enabled = false;
            values = new ToolStripMenuItem("正在查询额度") { Enabled = false }; menu.Items.Add(values);
            updateTime = new ToolStripMenuItem("等待更新") { Enabled = false }; menu.Items.Add(updateTime);
            errorItem = new ToolStripMenuItem { Enabled = false, Visible = false }; menu.Items.Add(errorItem);
            menu.Items.Add(new ToolStripSeparator());
            var refresh = new ToolStripMenuItem("立即刷新");
            refresh.Click += delegate { RefreshQuota(); }; menu.Items.Add(refresh);
            var frequency = new ToolStripMenuItem("刷新间隔");
            foreach (int seconds in new[] { 60, 120, 300 })
            {
                int selected = seconds;
                var item = new ToolStripMenuItem((seconds / 60) + " 分钟") { Tag = seconds };
                item.Click += delegate { interval = selected; nextRefresh = DateTime.UtcNow.AddSeconds(interval); SaveSettings(); };
                frequency.DropDownItems.Add(item);
            }
            menu.Items.Add(frequency);
            var startup = new ToolStripMenuItem("开机启动");
            startup.Click += delegate {
                try { StartupRegistration.Set(!StartupRegistration.IsEnabled()); }
                catch (Exception exception) {
                    tray.ShowBalloonTip(10000, "开机启动设置失败", exception.Message, ToolTipIcon.Error);
                }
            };
            menu.Items.Add(startup);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            menu.Opening += delegate {
                foreach (ToolStripMenuItem item in frequency.DropDownItems) item.Checked = (int)item.Tag == interval;
                startup.Checked = StartupRegistration.IsEnabled(); refresh.Enabled = !refreshing;
                UpdateMenu();
            };
            tray = new NotifyIcon { Text = "Codex：正在查询额度", ContextMenuStrip = menu };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) RefreshQuota(); };
            events.Received += delegate(uint message) {
                if (message == taskbarCreated) { tray.Visible = false; RenderIcon(); tray.Visible = true; }
            };
            RenderIcon(); tray.Visible = true;
            timer.Tick += delegate { if (!refreshing && DateTime.UtcNow >= nextRefresh) RefreshQuota(); };
            timer.Start();
            // This context has no Form. Install a UI dispatcher before the async query starts.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            RefreshQuota();
        }

        private async void RefreshQuota()
        {
            if (refreshing || exiting) return;
            refreshing = true; UpdateMenu();
            try
            {
                snapshot = await Task.Run(() => reader.ReadAsync(cancellation.Token));
                error = "";
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                error = exception is InvalidOperationException || exception is TimeoutException ? exception.Message : "读取失败，请检查 Codex 登录和网络后刷新。";
            }
            finally
            {
                refreshing = false;
                nextRefresh = DateTime.UtcNow.AddSeconds(interval);
                if (!exiting) { RenderIcon(); UpdateMenu(); SaveStatus(); }
            }
        }

        private void RenderIcon()
        {
            Icon replacement = TrayRenderer.Create(snapshot, error.Length > 0);
            tray.Icon = replacement;
            Icon previous = currentIcon; currentIcon = replacement;
            if (previous != null) previous.Dispose();
            string text;
            if (snapshot == null)
                text = error.Length == 0 ? "Codex 额度正在查询\n外圈：5小时余量\n内圈：每周余量" : "Codex 额度暂不可用\n请检查 Codex 登录与网络\n右键可重新刷新";
            else
            {
                // NotifyIcon on .NET Framework accepts 63 characters. Keep both dates complete.
                string resetLabel = error.Length > 0 ? "重置：" : "重置时间：";
                text = (error.Length > 0 ? "旧值\n" : "")
                    + "5小时额度：剩余" + QuotaText.Percent(snapshot.FiveHour)
                    + "\n" + resetLabel + QuotaText.Reset(snapshot.FiveHour)
                    + "\n\n每周额度：剩余" + QuotaText.Percent(snapshot.Weekly)
                    + "\n" + resetLabel + QuotaText.Reset(snapshot.Weekly);
            }
            tray.Text = text;
        }

        private void UpdateMenu()
        {
            values.Text = snapshot == null ? "额度暂不可用" : "5 小时剩余 " + QuotaText.Percent(snapshot.FiveHour) + " / 每周剩余 " + QuotaText.Percent(snapshot.Weekly);
            updateTime.Text = refreshing ? "正在刷新…" : snapshot == null ? "尚未更新" : "更新于 " + snapshot.UpdatedAt.ToString("HH:mm:ss") + " · 每 " + interval / 60 + " 分钟刷新";
            errorItem.Text = error; errorItem.Visible = error.Length > 0;
        }

        private void LoadSettings()
        {
            string path = Path.Combine(dataFolder, "settings.json");
            if (!File.Exists(path)) return;
            try
            {
                var settings = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path));
                if (settings.IntervalSeconds == 60 || settings.IntervalSeconds == 120 || settings.IntervalSeconds == 300) interval = settings.IntervalSeconds;
            }
            catch (ArgumentException) { }
        }
        private void SaveSettings()
        {
            File.WriteAllText(Path.Combine(dataFolder, "settings.json"), new JavaScriptSerializer().Serialize(new Settings { IntervalSeconds = interval }));
        }
        private void SaveStatus()
        {
            var status = new {
                FiveHourRemaining = snapshot == null || snapshot.FiveHour == null ? (double?)null : snapshot.FiveHour.Remaining,
                WeeklyRemaining = snapshot == null || snapshot.Weekly == null ? (double?)null : snapshot.Weekly.Remaining,
                FiveHourReset = snapshot == null || snapshot.FiveHour == null || !snapshot.FiveHour.ResetsAt.HasValue ? null : snapshot.FiveHour.ResetsAt.Value.ToLocalTime().ToString("o"),
                WeeklyReset = snapshot == null || snapshot.Weekly == null || !snapshot.Weekly.ResetsAt.HasValue ? null : snapshot.Weekly.ResetsAt.Value.ToLocalTime().ToString("o"),
                UpdatedAt = snapshot == null ? null : snapshot.UpdatedAt.ToString("o"),
                Error = error, Display = "SingleTrayIconConcentricRings", IconSize = TrayRenderer.IconSize(), IntervalSeconds = interval
            };
            try
            {
                File.WriteAllText(Path.Combine(dataFolder, "status.json"), new JavaScriptSerializer().Serialize(status));
                using (var bitmap = TrayRenderer.Draw(snapshot, error.Length > 0)) bitmap.Save(Path.Combine(dataFolder, "tray-icon.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        protected override void ExitThreadCore()
        {
            exiting = true; timer.Stop(); cancellation.Cancel(); reader.Dispose(); tray.Visible = false;
            base.ExitThreadCore();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); tray.Dispose(); if (currentIcon != null) currentIcon.Dispose(); menu.Dispose(); events.Dispose(); cancellation.Dispose(); reader.Dispose(); }
            base.Dispose(disposing);
        }
    }
    public sealed class Settings { public int IntervalSeconds = 60; }
}

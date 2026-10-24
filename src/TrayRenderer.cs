using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace CodexQuotaTaskbar
{
    internal static class TrayRenderer
    {
        public static int IconSize()
        {
            IntPtr taskbar = Native.FindWindow("Shell_TrayWnd", null);
            uint dpi = taskbar == IntPtr.Zero ? 96 : Native.GetDpiForWindow(taskbar);
            return Math.Max(16, Native.GetSystemMetricsForDpi(49, Math.Max(96, dpi)));
        }
        public static Icon Create(QuotaSnapshot snapshot, bool stale)
        {
            using (var bitmap = Draw(snapshot, stale))
            {
                IntPtr handle = bitmap.GetHicon();
                try { using (var borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
                finally { Native.DestroyIcon(handle); }
            }
        }
        public static Bitmap Draw(QuotaSnapshot snapshot, bool stale)
        {
            int size = IconSize();
            var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // Render in a 24-unit coordinate space so the gaps remain balanced at each DPI.
                graphics.ScaleTransform(size / 24f, size / 24f);
                bool light = LightTaskbar();
                Color track = light ? Color.FromArgb(48, 48, 60, 78) : Color.FromArgb(68, 172, 184, 205);
                Color green = light ? Color.FromArgb(0, 126, 91) : Color.FromArgb(95, 230, 181);
                Color purple = light ? Color.FromArgb(105, 77, 195) : Color.FromArgb(185, 165, 255);
                DrawRing(graphics, snapshot == null ? null : snapshot.FiveHour, new RectangleF(1.65f, 1.65f, 20.7f, 20.7f), 2.5f, green, track, stale);
                DrawRing(graphics, snapshot == null ? null : snapshot.Weekly, new RectangleF(6.25f, 6.25f, 11.5f, 11.5f), 2.35f, purple, track, stale);
                if (stale)
                {
                    using (var backing = new SolidBrush(light ? Color.FromArgb(246, 247, 250) : Color.FromArgb(27, 30, 37))) graphics.FillEllipse(backing, 17.5f, .25f, 6, 6);
                    using (var warning = new SolidBrush(Color.FromArgb(255, 188, 77))) graphics.FillEllipse(warning, 19, 1.75f, 3, 3);
                }
            }
            return bitmap;
        }

        private static void DrawRing(Graphics graphics, QuotaWindow window, RectangleF rect, float thickness, Color color, Color track, bool stale)
        {
            using (var background = new Pen(track, thickness))
            {
                if (window == null) background.DashStyle = DashStyle.Dot;
                graphics.DrawEllipse(background, rect);
            }
            if (window == null || window.Remaining <= 0) return;
            if (window.Remaining <= 10) color = Color.FromArgb(255, 112, 124);
            else if (window.Remaining <= 20) color = Color.FromArgb(255, 195, 90);
            if (stale) color = Color.FromArgb(145, color);
            using (var arc = new Pen(color, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                if (window.Remaining >= 100) graphics.DrawEllipse(arc, rect);
                else graphics.DrawArc(arc, rect, -90, (float)(window.Remaining * 3.6));
            }
        }

        private static bool LightTaskbar()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return key != null && Convert.ToInt32(key.GetValue("SystemUsesLightTheme", 0)) == 1;
        }
    }
    internal static class QuotaText
    {
        public static string Percent(QuotaWindow window) { return window == null ? "--" : window.Remaining.ToString("0.#") + "%"; }
        public static string Reset(QuotaWindow window) { return window == null || !window.ResetsAt.HasValue ? "未提供" : window.ResetsAt.Value.ToLocalTime().ToString("MM月dd日 HH:mm"); }
    }
}

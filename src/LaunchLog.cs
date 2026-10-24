using System;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;

namespace CodexQuotaTaskbar
{
    internal static class LaunchLog
    {
        public static bool FromStartup;

        public static void Write(string phase, Exception error = null)
        {
            try
            {
                string folder = Path.Combine(Program.Root, "data");
                Directory.CreateDirectory(folder);
                var record = new {
                    StartedAt = new DateTimeOffset(Process.GetCurrentProcess().StartTime).ToString("o"),
                    RecordedAt = DateTimeOffset.Now.ToString("o"),
                    ProcessId = Process.GetCurrentProcess().Id,
                    Source = FromStartup ? "WindowsLogonTask" : "Manual",
                    Phase = phase,
                    Error = error == null ? "" : error.GetType().Name + ": " + error.Message
                };
                File.WriteAllText(Path.Combine(folder, "launch.json"), new JavaScriptSerializer().Serialize(record));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

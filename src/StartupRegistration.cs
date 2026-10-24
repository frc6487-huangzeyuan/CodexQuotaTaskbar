using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CodexQuotaTaskbar
{
    internal static class StartupRegistration
    {
        public const string TaskName = "CodexQuotaTaskbar";
        private const int NotFound = unchecked((int)0x80070002);

        private static dynamic Scheduler()
        {
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            service.Connect();
            return service;
        }

        public static bool IsEnabled()
        {
            try
            {
                dynamic service = Scheduler();
                dynamic task = service.GetFolder(@"\").GetTask(TaskName);
                dynamic definition = task.Definition;
                if (!task.Enabled || definition.Actions.Count != 1) return false;
                dynamic action = definition.Actions.Item(1);
                bool correctAction = String.Equals((string)action.Path, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                    && (string)action.Arguments == "--startup"
                    && String.Equals(((string)action.WorkingDirectory).TrimEnd('\\'), Program.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                bool logonTrigger = false;
                for (int index = 1; index <= definition.Triggers.Count; index++)
                {
                    dynamic trigger = definition.Triggers.Item(index);
                    if (trigger.Type == 9 && trigger.Enabled) logonTrigger = true;
                }
                return correctAction && logonTrigger && definition.Principal.LogonType == 3;
            }
            catch (COMException) { return false; }
        }

        public static void Set(bool enabled)
        {
            dynamic service = Scheduler();
            dynamic folder = service.GetFolder(@"\");
            if (enabled)
            {
                string userSid = WindowsIdentity.GetCurrent().User.Value;
                dynamic definition = service.NewTask(0);
                definition.RegistrationInfo.Description = "登录 Windows 后启动 Codex 额度托盘，显示 5 小时和每周剩余额度。";
                definition.Principal.UserId = userSid;
                definition.Principal.LogonType = 3; // Interactive token: show the tray in this user's desktop.
                definition.Principal.RunLevel = 0; // Normal user privileges, no elevation or stored password.
                dynamic trigger = definition.Triggers.Create(9);
                trigger.UserId = userSid;
                trigger.Delay = "PT15S";
                trigger.Enabled = true;
                dynamic action = definition.Actions.Create(0);
                action.Path = Application.ExecutablePath;
                action.Arguments = "--startup";
                action.WorkingDirectory = Program.Root;
                dynamic settings = definition.Settings;
                settings.Enabled = true;
                settings.StartWhenAvailable = true;
                settings.DisallowStartIfOnBatteries = false;
                settings.StopIfGoingOnBatteries = false;
                settings.ExecutionTimeLimit = "PT0S";
                settings.MultipleInstances = 2; // Ignore duplicate launch requests.
                settings.RestartCount = 3;
                settings.RestartInterval = "PT1M";
                folder.RegisterTaskDefinition(TaskName, definition, 6, userSid, null, 3, null);
                if (!IsEnabled()) throw new InvalidOperationException("登录启动任务保存后校验未通过。");
            }
            else
            {
                try { folder.DeleteTask(TaskName, 0); }
                catch (COMException exception) { if (exception.ErrorCode != NotFound) throw; }
            }
            // Migrate the old app-owned Run entry so there is only one startup mechanism.
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                if (key != null) key.DeleteValue(TaskName, false);
        }
    }
}

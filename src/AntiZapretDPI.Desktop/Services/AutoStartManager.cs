using System.Diagnostics;
using AntiZapretDPI.Contracts;

namespace AntiZapretDPI.Services
{
    public class AutoStartManager : IAutoStartManager
    {
        private const string TaskName = "AntiZapretDPI";

        public bool IsEnabled
        {
            get
            {
                try
                {
                    using var p = RunSchTasks($"/Query /TN \"{TaskName}\"");
                    return p != null && p.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        public void Enable()
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                return;
            }

            var action = $"\"\\\"{exePath}\\\" --autostart\"";
            using var p = RunSchTasks($"/Create /F /TN \"{TaskName}\" /SC ONLOGON /TR {action} /RL HIGHEST");
        }

        public void Disable()
        {
            using var p = RunSchTasks($"/Delete /TN \"{TaskName}\" /F");
        }

        private static Process? RunSchTasks(string args)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var p = Process.Start(psi);
            p?.WaitForExit(10000);
            return p;
        }
    }
}

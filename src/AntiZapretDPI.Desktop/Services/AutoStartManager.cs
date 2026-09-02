using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
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

            string? userId = null;
            try
            {
                userId = WindowsIdentity.GetCurrent().User?.Value;
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(userId))
            {
                userId = $@"{Environment.UserDomainName}\{Environment.UserName}";
            }

            var xmlPath = Path.Combine(Path.GetTempPath(), "AntiZapretDPI_autostart.xml");
            try
            {
                File.WriteAllText(xmlPath, BuildTaskXml(exePath, userId), Encoding.Unicode);
                using var p = RunSchTasks($"/Create /F /TN \"{TaskName}\" /XML \"{xmlPath}\"");
            }
            finally
            {
                try { File.Delete(xmlPath); } catch { }
            }
        }

        public void Disable()
        {
            using var p = RunSchTasks($"/Delete /TN \"{TaskName}\" /F");
        }

        // Задача регистрируется через XML, чтобы отключить ограничения по питанию
        // (DisallowStartIfOnBatteries / StopIfGoingOnBatteries), из-за которых на
        // ноутбуке при входе от батареи автозапуск не срабатывал.
        private static string BuildTaskXml(string exePath, string userId)
        {
            string exe = SecurityElement.Escape(exePath);
            string user = SecurityElement.Escape(userId);
            string now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

            return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Date>{now}</Date>
    <Author>{user}</Author>
    <URI>\{TaskName}</URI>
  </RegistrationInfo>
  <Principals>
    <Principal id=""Author"">
      <UserId>{user}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <IdleSettings>
      <Duration>PT10M</Duration>
      <WaitTimeout>PT1H</WaitTimeout>
      <StopOnIdleEnd>true</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
  </Settings>
  <Triggers>
    <LogonTrigger>
      <StartBoundary>{now}</StartBoundary>
    </LogonTrigger>
  </Triggers>
  <Actions Context=""Author"">
    <Exec>
      <Command>""{exe}""</Command>
      <Arguments>--autostart</Arguments>
    </Exec>
  </Actions>
</Task>";
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

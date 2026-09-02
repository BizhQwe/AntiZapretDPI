using System.Diagnostics;
using System.Threading;
using AntiZapretDPI.Contracts;

namespace AntiZapretDPI.Services
{
    public class VpnPauseCoordinator : IVpnPauseCoordinator
    {
        private const string WatchArgument = "--vpnwatch";

        private readonly AppSettingsService _settingsService;

        public VpnPauseCoordinator(AppSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public bool IsRunning
        {
            get
            {
                try
                {
                    using var _ = EventWaitHandle.OpenExisting(VpnPauseEvents.MarkerEventName);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool EnsureStarted()
        {
            try
            {
                if (!_settingsService.Load().PauseOnVpn)
                {
                    return false;
                }

                if (IsRunning)
                {
                    return true;
                }

                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    return false;
                }

                Process.Start(new ProcessStartInfo(exePath, WatchArgument)
                {
                    WorkingDirectory = AppContext.BaseDirectory,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void EnsureStopped(int timeoutMs = 0)
        {
            try
            {
                using var shutdown = EventWaitHandle.OpenExisting(VpnPauseEvents.ShutdownEventName);
                shutdown.Set();
            }
            catch
            {
            }

            if (timeoutMs <= 0)
            {
                return;
            }

            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline && IsRunning)
            {
                Thread.Sleep(100);
            }
        }
    }
}

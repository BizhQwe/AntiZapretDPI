using System.Threading;
using AntiZapretDPI.Contracts;

namespace AntiZapretDPI.Services
{
    public class VpnPauseWatcher
    {
        private const int PollIntervalMs = 2000;

        private readonly IAntiZapretManager _manager;
        private readonly AppSettingsService _settingsService;
        private readonly IVpnDetector _vpnDetector;

        public VpnPauseWatcher(
            IAntiZapretManager manager,
            AppSettingsService settingsService,
            IVpnDetector vpnDetector)
        {
            _manager = manager;
            _settingsService = settingsService;
            _vpnDetector = vpnDetector;
        }

        public async Task RunAsync()
        {
            bool createdNew;
            using var marker = new EventWaitHandle(
                false,
                EventResetMode.ManualReset,
                VpnPauseEvents.MarkerEventName,
                out createdNew);

            if (!createdNew)
            {
                return;
            }

            using var shutdown = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                VpnPauseEvents.ShutdownEventName,
                out _);

            while (!shutdown.WaitOne(PollIntervalMs))
            {
                var settings = _settingsService.Load();
                if (!settings.PauseOnVpn)
                {
                    continue;
                }

                bool vpnActive = _vpnDetector.IsVpnActive();
                bool running = _manager.IsRunning();

                if (vpnActive)
                {
                    if (running)
                    {
                        _manager.StopZapret();
                    }
                }
                else if (!running)
                {
                    _manager.StartZapret(out _, settings.SelectedStrategy ?? "general.bat", settings.HiddenMode);
                }
            }
        }
    }
}

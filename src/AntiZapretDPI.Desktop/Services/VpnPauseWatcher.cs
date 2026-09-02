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
        private readonly IStrategyAutoSelector _autoSelector;

        public VpnPauseWatcher(
            IAntiZapretManager manager,
            AppSettingsService settingsService,
            IVpnDetector vpnDetector,
            IStrategyAutoSelector autoSelector)
        {
            _manager = manager;
            _settingsService = settingsService;
            _vpnDetector = vpnDetector;
            _autoSelector = autoSelector;
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
                    if (settings.AutoSelectStrategy && settings.AutoSelectPending)
                    {
                        await StartWithAutoSelectAsync(settings);
                    }
                    else
                    {
                        _manager.StartZapret(out _, settings.SelectedStrategy ?? "general.bat", settings.HiddenMode);
                    }
                }
            }
        }

        private async Task StartWithAutoSelectAsync(AppSettings settings)
        {
            var profiles = _manager.GetAvailablePresets();

            if (profiles.Count == 0)
            {
                settings.AutoSelectPending = false;
                _settingsService.Save(settings);
                _manager.StartZapret(out _, settings.SelectedStrategy ?? "general.bat", settings.HiddenMode);
                return;
            }

            var outcome = await _autoSelector.TrySelectAsync(
                profiles,
                settings.SelectedStrategy ?? "general.bat",
                settings.HiddenMode);

            if (outcome.IsSuccess)
            {
                settings.SelectedStrategy = outcome.Profile;
            }

            settings.AutoSelectPending = false;
            _settingsService.Save(settings);

            if (outcome.IsSuccess)
            {
                return;
            }

            _manager.StartZapret(out _, settings.SelectedStrategy ?? "general.bat", settings.HiddenMode);
        }
    }
}

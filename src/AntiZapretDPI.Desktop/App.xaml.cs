using AntiZapretDPI.Contracts;
using AntiZapretDPI.Services;
using AntiZapretDPI.Services.StateMachine;
using AntiZapretDPI.Services.StateMachine.States;
using AntiZapretDPI.ViewModels.Windows;
using AntiZapretDPI.Views.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;

namespace AntiZapretDPI
{
    public partial class App : Application
    {
        private readonly IHost _host;

        public App()
        {
            var builder = Host.CreateApplicationBuilder();

            ConfigureServices(builder.Services);

            _host = builder.Build();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IAntiZapretManager, AntiZapretManager>();
            services.AddSingleton<IConnectivityProbe, ConnectivityProbe>();
            services.AddSingleton<IStrategyAutoSelector, StrategyAutoSelector>();
            services.AddSingleton<IAutoStartManager, AutoStartManager>();
            services.AddSingleton<IVpnDetector, VpnDetector>();
            services.AddSingleton<IVpnPauseCoordinator, VpnPauseCoordinator>();
            services.AddSingleton<VpnPauseWatcher>();
            services.AddSingleton<AppSettingsService>();
            services.AddSingleton<IAppState, NotInstalledState>();
            services.AddSingleton<IAppState, IdleState>();
            services.AddSingleton<IAppState, RunningState>();
            services.AddSingleton<IAppState, BusyState>();
            services.AddSingleton<AppStateMachine>();
            services.AddSingleton<IAppStateMachine>(sp => sp.GetRequiredService<AppStateMachine>());
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Contains("--vpnwatch", StringComparer.OrdinalIgnoreCase))
            {
                await RunVpnWatchAsync();
                Shutdown();
                return;
            }

            if (e.Args.Contains("--autostart", StringComparer.OrdinalIgnoreCase))
            {
                await StartServiceOnlyAsync();
                Shutdown();
                return;
            }

            await _host.StartAsync();
            _host.Services.GetRequiredService<MainWindow>().Show();

            base.OnStartup(e);
        }

        private async Task RunVpnWatchAsync()
        {
            var watcher = _host.Services.GetRequiredService<VpnPauseWatcher>();
            await watcher.RunAsync();
        }

        private async Task StartServiceOnlyAsync()
        {
            var settingsService = _host.Services.GetRequiredService<AppSettingsService>();
            var settings = settingsService.Load();
            var manager = _host.Services.GetRequiredService<IAntiZapretManager>();

            // При автозапуске с уже включённым VPN сервис не запускаем и стратегию
            // не подбираем: подбор выполнит watcher после выключения VPN.
            bool vpnActive = _host.Services.GetRequiredService<IVpnDetector>().IsVpnActive();
            if (settings.PauseOnVpn && vpnActive)
            {
                if (settings.AutoSelectStrategy)
                {
                    settings.AutoSelectPending = true;
                    settingsService.Save(settings);
                }

                _host.Services.GetRequiredService<IVpnPauseCoordinator>().EnsureStarted();
                return;
            }

            string? startedProfile = settings.AutoSelectStrategy
                ? await AutoSelectProfileAsync(settings, settingsService)
                : null;

            bool started = startedProfile != null;
            if (!started)
            {
                started = manager.StartZapret(out _, settings.SelectedStrategy ?? "general.bat", settings.HiddenMode);
            }

            if (started)
            {
                settings.AutoSelectPending = false;
                settingsService.Save(settings);
                _host.Services.GetRequiredService<IVpnPauseCoordinator>().EnsureStarted();
            }
        }

        private async Task<string?> AutoSelectProfileAsync(AppSettings settings, AppSettingsService settingsService)
        {
            var manager = _host.Services.GetRequiredService<IAntiZapretManager>();
            var selector = _host.Services.GetRequiredService<IStrategyAutoSelector>();

            var profiles = manager.GetAvailablePresets();
            if (profiles.Count == 0)
            {
                return null;
            }

            var outcome = await selector.TrySelectAsync(
                profiles,
                settings.SelectedStrategy ?? "general.bat",
                settings.HiddenMode);

            if (!outcome.IsSuccess)
            {
                return null;
            }

            settings.SelectedStrategy = outcome.Profile;
            settingsService.Save(settings);
            return outcome.Profile;
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            using (_host)
            {
                await _host.StopAsync();
            }

            base.OnExit(e);
        }
    }
}

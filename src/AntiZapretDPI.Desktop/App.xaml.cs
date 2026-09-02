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
            services.AddSingleton<IAutoStartManager, AutoStartManager>();
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
            if (e.Args.Contains("--autostart", StringComparer.OrdinalIgnoreCase))
            {
                StartServiceOnly();
                return;
            }

            await _host.StartAsync();

            _host.Services.GetRequiredService<MainWindow>().Show();

            base.OnStartup(e);
        }

        private void StartServiceOnly()
        {
            var settings = _host.Services.GetRequiredService<AppSettingsService>().Load();
            var manager = _host.Services.GetRequiredService<IAntiZapretManager>();

            manager.StartZapret(out _, settings.SelectedStrategy ?? "general.bat", settings.HiddenMode);

            Shutdown();
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

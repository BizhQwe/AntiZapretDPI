using AntiZapretDPI.Contracts;
using AntiZapretDPI.Services;
using AntiZapretDPI.Services.StateMachine;
using AntiZapretDPI.Services.StateMachine.States;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using WpfUiMessageBox = Wpf.Ui.Controls.MessageBox;
using WpfUiMessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace AntiZapretDPI.ViewModels.Windows
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IAntiZapretManager _manager;
        private readonly IAppStateMachine _machine;
        private readonly AppSettingsService _settingsService;
        private readonly IAutoStartManager _autoStartManager;
        private readonly DispatcherTimer _stateSyncTimer;

        [ObservableProperty] private string _version = "Версия не найдена";
        [ObservableProperty] private bool _isVersionVisible = true;

        [ObservableProperty] private string _mainActionText = "Запустить";
        [ObservableProperty] private ICommand? _mainActionCommand;
        [ObservableProperty] private bool _isMainActionEnabled;
        [ObservableProperty] private SymbolRegular _mainActionIcon = SymbolRegular.Play24;

        [ObservableProperty] private string _secondaryActionText = "Загрузить";
        [ObservableProperty] private ICommand? _secondaryActionCommand;
        [ObservableProperty] private bool _isSecondaryActionEnabled;
        [ObservableProperty] private SymbolRegular _secondaryActionIcon = SymbolRegular.ArrowDownload24;

        [ObservableProperty] private bool _isDeleteButtonEnabled;

        [ObservableProperty] private bool _areSettingsEnabled = true;

        [ObservableProperty] private string _statusText = "Готов к работе";
        [ObservableProperty] private Brush _statusColor = Brushes.Gray;

        [ObservableProperty] private ObservableCollection<string> _strategies = new();
        [ObservableProperty] private string? _selectedStrategy;

        [ObservableProperty] private bool _isHiddenMode = true;
        [ObservableProperty] private bool _isAutoUpdateEnabled = true;
        [ObservableProperty] private bool _isAutoStartEnabled;
        [ObservableProperty] private bool _isAutoSelectStrategy;

        public MainViewModel(
            IAntiZapretManager manager,
            IAppStateMachine machine,
            AppSettingsService settingsService,
            IAutoStartManager autoStartManager)
        {
            _manager = manager;
            _machine = machine;
            _settingsService = settingsService;
            _autoStartManager = autoStartManager;
            _machine.StateChanged += OnStateChanged;

            LoadSettings();
            SyncWithServiceState();
            UpdateUiState();

            _stateSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _stateSyncTimer.Tick += (_, _) => SyncWithServiceState();
            _stateSyncTimer.Start();

            _ = InitializeAsync();
        }

        private void OnStateChanged(IAppState state) => UpdateUiState();

        private async Task InitializeAsync()
        {
            var latest = await CheckForUpdatesAsync(silent: true);

            if (!IsAutoUpdateEnabled)
            {
                return;
            }

            if (_machine.CanExecute(new DownloadAction()))
            {
                StatusText = "Загрузка с GitHub...";
                await InstallOrUpdateAsync();
            }
            else if (_machine.CanExecute(new UpdateAction())
                && latest != null
                && !string.Equals(latest, _manager.GetLocalVersion(), StringComparison.OrdinalIgnoreCase))
            {
                StatusText = $"Доступно обновление: {latest}";
                await InstallOrUpdateAsync();
            }
        }

        private void SyncWithServiceState()
        {
            if (_machine.CurrentState is BusyState)
            {
                return;
            }

            bool installed = _manager.IsInstalled();
            bool running = _manager.IsRunning();

            if (!installed)
            {
                if (_machine.CurrentState is not NotInstalledState)
                {
                    _machine.MoveTo<NotInstalledState>();
                    StatusText = "Zapret не установлен";
                }
            }
            else if (running)
            {
                if (_machine.CurrentState is not RunningState)
                {
                    _machine.MoveTo<RunningState>();
                    StatusText = "Сервис уже запущен";
                }
            }
            else if (_machine.CurrentState is not IdleState)
            {
                _machine.MoveTo<IdleState>();
                StatusText = "Готов к работе";
            }
        }

        [RelayCommand]
        private void Refresh() => SyncWithServiceState();

        [RelayCommand]
        private async Task StartAsync()
        {
            if (!_machine.CanExecute(new StartAction()))
            {
                return;
            }

            _machine.MoveTo<BusyState>();

            bool ok;
            if (IsAutoSelectStrategy)
            {
                ok = await TryStartWithAutoSelectAsync();
            }
            else
            {
                ok = await StartSingleAsync();
            }

            if (ok)
            {
                _machine.MoveTo<RunningState>();
            }
            else
            {
                _machine.MoveTo<IdleState>();
            }
        }

        private async Task<bool> StartSingleAsync()
        {
            string errorDetails = string.Empty;
            bool started = await Task.Run(() => _manager.StartZapret(out errorDetails, SelectedStrategy ?? "general.bat", IsHiddenMode));

            if (!started)
            {
                StatusText = Shorten($"Ошибка запуска: {errorDetails}", 90);
                return false;
            }

            _machine.MoveTo<RunningState>();

                bool access = await _manager.IsAccessRestoredAsync();
            if (_machine.CurrentState is RunningState)
            {
                StatusText = access ? "Запущен. Доступ восстановлен" : "Запущен, но доступ не восстановлен";
            }

            return true;
        }

        private async Task<bool> TryStartWithAutoSelectAsync()
        {
            var profiles = _manager.GetAvailablePresets();
            if (profiles.Count == 0)
            {
                StatusText = "Профили не найдены";
                return false;
            }

            var preferred = SelectedStrategy ?? "general.bat";
            var ordered = profiles
                .OrderBy(p => !string.Equals(p, preferred, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var profile in ordered)
            {
                StatusText = Shorten($"Пробую: {profile}", 90);
                await Task.Delay(50);

                string startError = string.Empty;
                bool started = await Task.Run(() => _manager.StartZapret(out startError, profile, IsHiddenMode));
                if (!started)
                {
                    continue;
                }

                await Task.Delay(1000);
            bool access = await _manager.IsAccessRestoredAsync();
                if (access)
                {
                    SelectedStrategy = profile;
                    SaveSettings();
                    StatusText = Shorten($"Рабочий профиль: {profile}", 90);
                    return true;
                }

                await Task.Run(_manager.StopZapret);
            }

            await Task.Run(_manager.StopZapret);
            StatusText = "Ни один профиль не восстановил доступ";
            return false;
        }

        private static string Shorten(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text;
            }
            return text.Substring(0, maxLength - 3) + "...";
        }

        [RelayCommand]
        private async Task StopAsync()
        {
            if (!_machine.CanExecute(new StopAction()))
            {
                return;
            }

            _machine.MoveTo<BusyState>();
            await Task.Run(_manager.StopZapret);
            _machine.MoveTo<IdleState>();
            StatusText = "Остановлен";
        }

        [RelayCommand]
        private async Task DownloadAsync() => await InstallOrUpdateAsync();

        [RelayCommand]
        private async Task UpdateAsync()
        {
            await CheckForUpdatesAsync(silent: false);
            await InstallOrUpdateAsync();
        }

        private async Task InstallOrUpdateAsync()
        {
            if (!_machine.CanExecute(new DownloadAction()) && !_machine.CanExecute(new UpdateAction()))
            {
                return;
            }

            _machine.MoveTo<BusyState>();

            var progress = new Progress<string>(msg => StatusText = msg);
            bool success = await Task.Run(() => _manager.DownloadAndInstallAsync(progress));

            StatusText = success
                ? "Установка завершена"
                : "Ошибка установки";

            if (_manager.IsInstalled())
            {
                _machine.MoveTo<IdleState>();
            }
            else
            {
                _machine.MoveTo<NotInstalledState>();
            }
        }

        [RelayCommand]
        private async Task DeleteAsync()
        {
            if (!_machine.CanExecute(new DeleteAction()))
            {
                return;
            }

            IsDeleteButtonEnabled = false;

            var messageBox = new WpfUiMessageBox
            {
                Title = "Удаление",
                Content = "Удалить установку zapret discord youtube? Все файлы будут удалены.",
                PrimaryButtonText = "Да",
                SecondaryButtonText = "Нет",
                IsCloseButtonEnabled = false
            };

            var result = await messageBox.ShowDialogAsync();
            if (result != WpfUiMessageBoxResult.Primary)
            {
                UpdateUiState();
                return;
            }

            _machine.MoveTo<BusyState>();
            bool ok = await Task.Run(_manager.DeleteInstallation);
            _machine.MoveTo<NotInstalledState>();
            StatusText = ok ? "Установка удалена" : "Не удалось удалить установку";
        }

        [RelayCommand]
        private void EditRoutingFile()
        {
            if (!_machine.CanExecute(new EditAction()))
            {
                return;
            }

            var path = _manager.GetRoutingFilePath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                StatusText = "Файл list-general.txt не найден";
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusText = Shorten($"Не удалось открыть файл: {ex.Message}", 90);
            }
        }

        private async Task<string?> CheckForUpdatesAsync(bool silent)
        {
            var latest = await _manager.CheckLatestVersionAsync();

            if (!silent)
            {
                StatusText = latest != null
                    ? $"Последняя версия на GitHub: {latest}"
                    : "Не удалось проверить обновления (нет сети)";
            }

            return latest;
        }

        private void UpdateUiState()
        {
            bool notInstalled = _machine.CurrentState is NotInstalledState;
            bool idle = _machine.CurrentState is IdleState;
            bool running = _machine.CurrentState is RunningState;
            bool busy = _machine.CurrentState is BusyState;
            bool installed = _manager.IsInstalled();
            bool runningNow = _manager.IsRunning();

            Version = _manager.GetLocalVersion();
            IsVersionVisible = installed;
            StatusColor = runningNow ? Brushes.LimeGreen : Brushes.Gray;

            MainActionText = running ? "Остановить" : "Запустить";
            MainActionCommand = running ? StopCommand : StartCommand;
            MainActionIcon = running ? SymbolRegular.Stop24 : SymbolRegular.Play24;
            IsMainActionEnabled = (running || idle) && !busy;

            SecondaryActionText = installed ? "Обновить" : "Загрузить";
            SecondaryActionCommand = installed ? UpdateCommand : DownloadCommand;
            SecondaryActionIcon = installed ? SymbolRegular.ArrowClockwise24 : SymbolRegular.ArrowDownload24;
            IsSecondaryActionEnabled = installed ? idle && !busy : notInstalled && !busy;

            IsDeleteButtonEnabled = idle && !busy;

            AreSettingsEnabled = installed && !running && !runningNow && !busy;

            if (busy)
            {
                return;
            }

            if (installed)
            {
                var presets = _manager.GetAvailablePresets();
                Strategies = new ObservableCollection<string>(presets);
                if (Strategies.Count > 0 && (SelectedStrategy == null || !Strategies.Contains(SelectedStrategy)))
                {
                    SelectedStrategy = Strategies.Contains("general.bat") ? "general.bat" : Strategies[0];
                }
            }
            else
            {
                Strategies = new ObservableCollection<string>();
                SelectedStrategy = null;
            }
        }

        private void LoadSettings()
        {
            var settings = _settingsService.Load();
            IsHiddenMode = settings.HiddenMode;
            IsAutoUpdateEnabled = settings.AutoUpdate;
            IsAutoStartEnabled = settings.AutoStart;
            IsAutoSelectStrategy = settings.AutoSelectStrategy;
            SelectedStrategy = settings.SelectedStrategy;
        }

        private void SaveSettings()
        {
            _settingsService.Save(new AppSettings
            {
                HiddenMode = IsHiddenMode,
                AutoUpdate = IsAutoUpdateEnabled,
                AutoStart = IsAutoStartEnabled,
                AutoSelectStrategy = IsAutoSelectStrategy,
                SelectedStrategy = SelectedStrategy
            });
        }

        partial void OnIsHiddenModeChanged(bool value) => SaveSettings();

        partial void OnIsAutoUpdateEnabledChanged(bool value) => SaveSettings();

        partial void OnSelectedStrategyChanged(string? value) => SaveSettings();

        partial void OnIsAutoSelectStrategyChanged(bool value) => SaveSettings();

        partial void OnIsAutoStartEnabledChanged(bool value)
        {
            SaveSettings();

            if (value)
            {
                _autoStartManager.Enable();
            }
            else
            {
                _autoStartManager.Disable();
            }
        }
    }
}

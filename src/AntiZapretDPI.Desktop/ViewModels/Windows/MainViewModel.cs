using AntiZapretDPI.Contracts;
using AntiZapretDPI.Helpers;
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
        private const int StateSyncIntervalMs = 2000;

        private readonly IAntiZapretManager _manager;
        private readonly IConnectivityProbe _probe;
        private readonly IStrategyAutoSelector _autoSelector;
        private readonly IAppStateMachine _machine;
        private readonly AppSettingsService _settingsService;
        private readonly IAutoStartManager _autoStartManager;
        private readonly IVpnPauseCoordinator _vpnPauseCoordinator;
        private readonly IVpnDetector _vpnDetector;
        private readonly DispatcherTimer _stateSyncTimer;

        private CancellationTokenSource? _autoSelectCts;
        private bool _autoSelectActive;
        private bool _autoSelectPending;
        private bool _isVpnPaused;

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

        [ObservableProperty] private bool _isPauseOnVpn = true;

        public MainViewModel(
            IAntiZapretManager manager,
            IConnectivityProbe probe,
            IStrategyAutoSelector autoSelector,
            IAppStateMachine machine,
            AppSettingsService settingsService,
            IAutoStartManager autoStartManager,
            IVpnPauseCoordinator vpnPauseCoordinator,
            IVpnDetector vpnDetector)
        {
            _manager = manager;
            _probe = probe;
            _autoSelector = autoSelector;
            _machine = machine;
            _settingsService = settingsService;
            _autoStartManager = autoStartManager;
            _vpnPauseCoordinator = vpnPauseCoordinator;
            _vpnDetector = vpnDetector;
            _machine.StateChanged += OnStateChanged;

            LoadSettings();
            ReconcileAutoStartTask();
            SyncWithServiceState();
            UpdateUiState();

            if (_machine.CurrentState is RunningState)
            {
                _vpnPauseCoordinator.EnsureStarted();
            }

            _stateSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(StateSyncIntervalMs) };
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
            bool managedByVpnPause = installed && IsPauseOnVpn && _vpnPauseCoordinator.IsRunning;

            if (!installed)
            {
                _isVpnPaused = false;
                if (_machine.CurrentState is not NotInstalledState)
                {
                    _machine.MoveTo<NotInstalledState>();
                    StatusText = "Zapret не установлен";
                }
                return;
            }

            if (managedByVpnPause)
            {
                bool wasRunningState = _machine.CurrentState is RunningState;
                if (!wasRunningState)
                {
                    _machine.MoveTo<RunningState>();
                }

                bool vpnActive = _vpnDetector.IsVpnActive();

                if (running)
                {
                    if (!wasRunningState || _isVpnPaused)
                    {
                        StatusText = "Сервис запущен";
                        ReloadStrategyFromDisk();
                    }
                    StatusColor = Brushes.LimeGreen;
                    _isVpnPaused = false;
                }
                else
                {
                    StatusText = vpnActive
                        ? "Сервис остановлен: работает VPN"
                        : "Сервис запускается";
                    StatusColor = Brushes.Orange;
                    _isVpnPaused = true;
                }
                return;
            }

            if (running)
            {
                if (_machine.CurrentState is not RunningState)
                {
                    _machine.MoveTo<RunningState>();
                    StatusText = "Сервис запущен";
                }
                StatusColor = Brushes.LimeGreen;
                _isVpnPaused = false;
                return;
            }

            _isVpnPaused = false;
            StatusColor = Brushes.Gray;
            if (_machine.CurrentState is not IdleState)
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

            // VPN уже активен: не подбираем стратегию и не запускаем сервис сейчас —
            // пробы через VPN бессмысленны. Уходим в обычное состояние VPN-паузы,
            // а подбор выполнит watcher после выключения VPN.
            if (IsPauseOnVpn && _vpnDetector.IsVpnActive())
            {
                _autoSelectPending = IsAutoSelectStrategy;
                SaveSettings();

                _vpnPauseCoordinator.EnsureStarted();
                for (int i = 0; i < 20 && !_vpnPauseCoordinator.IsRunning; i++)
                {
                    await Task.Delay(100);
                }

                SyncWithServiceState();
                return;
            }

            _machine.MoveTo<BusyState>();

            bool ok;
            if (IsAutoSelectStrategy)
            {
                _autoSelectActive = true;
                _autoSelectCts = new CancellationTokenSource();
                UpdateUiState();

                try
                {
                    ok = await TryAutoSelectAsync(_autoSelectCts.Token);
                }
                finally
                {
                    _autoSelectActive = false;
                    _autoSelectCts.Dispose();
                    _autoSelectCts = null;
                    UpdateUiState();
                }
            }
            else
            {
                ok = await StartSingleAsync();
            }

            if (ok)
            {
                _autoSelectPending = false;
                SaveSettings();

                _machine.MoveTo<RunningState>();
                _vpnPauseCoordinator.EnsureStarted();
            }
            else
            {
                _machine.MoveTo<IdleState>();
            }
        }

        [RelayCommand]
        private void CancelAutoSelect()
        {
            _autoSelectCts?.Cancel();
        }

        private async Task<bool> TryAutoSelectAsync(CancellationToken ct)
        {
            var profiles = _manager.GetAvailablePresets();
            if (profiles.Count == 0)
            {
                StatusText = "Профили не найдены";
                return false;
            }

            var progress = new Progress<string>(message => StatusText = message);
            var outcome = await _autoSelector.TrySelectAsync(
                profiles,
                SelectedStrategy ?? "general.bat",
                IsHiddenMode,
                progress,
                ct);

            return ApplySelectionOutcome(outcome);
        }

        private bool ApplySelectionOutcome(StrategySelectionResult outcome)
        {
            if (!outcome.IsSuccess)
            {
                StatusText = outcome.Status == StrategySelectionStatus.Cancelled
                    ? "Подбор отменён"
                    : "Ни один профиль не восстановил доступ";
                return false;
            }

            SelectedStrategy = outcome.Profile;

            StatusText = outcome.Status switch
            {
                StrategySelectionStatus.Partial =>
                    TextHelper.Truncate($"Профиль: {outcome.Profile} (видеосерверы доступны, полное подтверждение не получено)", 105),
                StrategySelectionStatus.UnconfirmedFallback =>
                    TextHelper.Truncate($"Профиль: {outcome.Profile} (автопроверка не подтвердила — проверьте вручную)", 110),
                _ => TextHelper.Truncate($"Рабочий профиль: {outcome.Profile}", 90)
            };

            return true;
        }

        private async Task<bool> StartSingleAsync()
        {
            string errorDetails = string.Empty;
            bool started = await Task.Run(() => _manager.StartZapret(out errorDetails, SelectedStrategy ?? "general.bat", IsHiddenMode));

            if (!started)
            {
                StatusText = TextHelper.Truncate($"Ошибка запуска: {errorDetails}", 90);
                return false;
            }

            _machine.MoveTo<RunningState>();

            bool access = await _probe.IsBasicAccessRestoredAsync();
            if (_machine.CurrentState is RunningState)
            {
                StatusText = access ? "Запущен. Доступ восстановлен" : "Запущен, но доступ не восстановлен";
            }

            return true;
        }

        [RelayCommand]
        private async Task StopAsync()
        {
            if (!_machine.CanExecute(new StopAction()))
            {
                return;
            }

            _machine.MoveTo<BusyState>();
            await Task.Run(() =>
            {
                _vpnPauseCoordinator.EnsureStopped(5000);
                _manager.StopZapret();
            });
            _autoSelectPending = false;
            SaveSettings();
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
            await Task.Run(() => _vpnPauseCoordinator.EnsureStopped(5000));

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
            bool ok = await Task.Run(() =>
            {
                _vpnPauseCoordinator.EnsureStopped(5000);
                return _manager.DeleteInstallation();
            });
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
                StatusText = TextHelper.Truncate($"Не удалось открыть файл: {ex.Message}", 90);
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
            StatusColor = runningNow
                ? Brushes.LimeGreen
                : (running && _isVpnPaused) ? Brushes.Orange : Brushes.Gray;

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

            if (busy && _autoSelectActive)
            {
                MainActionText = "Отменить";
                MainActionCommand = CancelAutoSelectCommand;
                MainActionIcon = SymbolRegular.Dismiss24;
                IsMainActionEnabled = true;
                StatusColor = Brushes.Orange;
                return;
            }

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
            _autoSelectPending = settings.AutoSelectPending;
            IsPauseOnVpn = settings.PauseOnVpn;
            SelectedStrategy = settings.SelectedStrategy;
        }

        // Watcher (отдельный процесс) после выключения VPN мог сам подобрать
        // профиль и записать его в settings.json. Подхватываем результат,
        // чтобы комбобокс и флаг отложенного подбора совпадали с диском.
        private void ReloadStrategyFromDisk()
        {
            try
            {
                var settings = _settingsService.Load();
                _autoSelectPending = settings.AutoSelectPending;

                string? profile = settings.SelectedStrategy;
                if (!string.IsNullOrEmpty(profile)
                    && Strategies.Contains(profile)
                    && !string.Equals(profile, SelectedStrategy, StringComparison.OrdinalIgnoreCase))
                {
                    SelectedStrategy = profile;
                }
            }
            catch
            {
            }
        }

        // Приводит задачу Планировщика в соответствие с сохранённой настройкой
        // при каждом запуске приложения, чтобы автозапуск работал даже без
        // повторного переключения тумблера (например, после чисток Windows).
        // Перерегистрация при включённой настройке также перезаписывает старые
        // задачи, созданные с ограничениями по питанию от батареи.
        private void ReconcileAutoStartTask()
        {
            try
            {
                bool wantsEnabled = IsAutoStartEnabled;
                bool isEnabled = _autoStartManager.IsEnabled;

                if (wantsEnabled)
                {
                    _autoStartManager.Enable();
                }
                else if (isEnabled)
                {
                    _autoStartManager.Disable();
                }
            }
            catch
            {
            }
        }

        private void SaveSettings()
        {
            _settingsService.Save(new AppSettings
            {
                HiddenMode = IsHiddenMode,
                AutoUpdate = IsAutoUpdateEnabled,
                AutoStart = IsAutoStartEnabled,
                AutoSelectStrategy = IsAutoSelectStrategy,
                AutoSelectPending = _autoSelectPending,
                SelectedStrategy = SelectedStrategy,
                PauseOnVpn = IsPauseOnVpn
            });
        }

        partial void OnIsHiddenModeChanged(bool value) => SaveSettings();

        partial void OnIsAutoUpdateEnabledChanged(bool value) => SaveSettings();

        partial void OnSelectedStrategyChanged(string? value) => SaveSettings();

        partial void OnIsAutoSelectStrategyChanged(bool value)
        {
            if (!value)
            {
                _autoSelectPending = false;
            }
            SaveSettings();
        }

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

        partial void OnIsPauseOnVpnChanged(bool value)
        {
            SaveSettings();

            if (value)
            {
                if (_manager.IsRunning())
                {
                    _vpnPauseCoordinator.EnsureStarted();
                }
            }
            else
            {
                _vpnPauseCoordinator.EnsureStopped();
            }
        }
    }
}

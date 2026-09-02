# AntiZapretDPI

Удобная WPF-оболочка для [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube). Приложение автоматизирует скачивание, установку и управление профилями утилиты `zapret`, обеспечивая быстрое восстановление доступа к заблокированным сервисам (YouTube, Discord и др.) в нативном интерфейсе Windows.

Версия: **1.0.2** | Платформа: **Windows 10/11 x64** | Требуется **.NET 10**

## Возможности

- **Управление в один клик:** установка и обновление компонентов `zapret`.
- **Гибкий запуск:** включение/остановка службы с выбором профиля (`general.bat`, `discord.bat` и др.) либо режимом автоподбора параметров.
- **Автоподбор стратегии:** при запуске программа проверяет доступность YouTube и сама подбирает рабочий профиль; результат запоминается в настройках.
- **Проверка доступности:** контроль фактического восстановления доступа (зонд YouTube) с отображением текущего состояния.
- **Интеграция с системой:** настройка автозапуска при входе в Windows (через Планировщик задач).
- **Пауза при VPN:** фоновый процесс-наблюдатель (`--vpnwatch`) отслеживает активные VPN-туннели, временно останавливает `winws` и автоматически возобновляет его после отключения VPN. Работает и при закрытой программе; в интерфейсе это отражается только статусом — состояние службы при этом не меняется.
- **Продвинутая настройка:** встроенный редактор маршрутов (`list-general.txt`) и корректное удаление службы из системы.
- **Скрытый режим:** запуск `winws` без видимого окна консоли.

## Структура проекта

```text
AntiZapretDPI.slnx                 # Решение для .NET 10
dist/                              # Готовые установочные файлы (результат сборки)
src/
  AntiZapretDPI.Desktop/           # Исходный код WPF-приложения
    Contracts/                     # Абстракции: менеджер, машина состояний, VPN-детектор и т.д.
    Services/                      # Реализации: AntiZapretManager, ConnectivityProbe,
                                   #   StrategyAutoSelector, AutoStartManager, VpnDetector,
                                   #   VpnPauseWatcher/Coordinator, AppSettingsService
    Services/StateMachine/         # Конечный автомат состояний (NotInstalled/Idle/Running/Busy)
    ViewModels/Windows/            # MainViewModel
    Views/Windows/                 # MainWindow (XAML + code-behind)
    Helpers/                       # Утилиты и attached-behaviors
  AntiZapretDPI.Installer/         # Сборка установщика (installer.iss, build-installer.bat)
```

## Сборка

Для компиляции исходного кода потребуется **.NET 10 SDK**, для сборки инсталлятора — **[Inno Setup](https://jrsoftware.org/isinfo.php) 6.0+**.

```powershell
# Сборка WPF-приложения
dotnet build AntiZapretDPI.slnx -c Release

# Сборка установщика с указанием версии (Inno Setup в PATH или Program Files)
src\AntiZapretDPI.Installer\build-installer.bat 1.0.2
```

Готовый установщик появится в `dist\AntiZapretDPI-Setup-1.0.2.exe`.

## Тихая установка и удаление

Поддерживается автоматическое развертывание без графического интерфейса пользователя:

```powershell
# Установка
AntiZapretDPI-Setup-1.0.2.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

# Удаление
"C:\Program Files\AntiZapretDPI\Uninstall.exe" /VERYSILENT /SUPPRESSMSGBOXES
```

## Запуск из командной строки

```text
AntiZapretDPI.exe                 # обычный запуск с графическим интерфейсом
AntiZapretDPI.exe --autostart     # запустить службу и выйти (без окна); используется
                                  #   планировщиком задач при входе в Windows
AntiZapretDPI.exe --vpnwatch       # режим фонового наблюдателя паузы при VPN (без окна)
```

## Благодарности

Этот проект существует благодаря открытым разработкам сообщества. Огромная благодарность авторам следующих инструментов и библиотек:

- **[zapret](https://github.com/bol-van/zapret)** — за создание мощного и гибкого ядра для обхода систем DPI.
- **[zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube)** — за отличные готовые конфигурации, скрипты и профили.
- **[WPF-UI](https://github.com/lepoco/wpfui)** — за красивые современные компоненты и Fluent Design для классического WPF.
- **[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)** — за легкую, быструю и удобную реализацию паттерна MVVM.

## Лицензия

[GPL-3.0](LICENSE)

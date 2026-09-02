# AntiZapretDPI

Удобная WPF-оболочка для [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube). Приложение автоматизирует скачивание, установку и управление профилями утилиты `zapret`, обеспечивая быстрое восстановление доступа к заблокированным сервисам (YouTube, Discord и др.) в нативном интерфейсе Windows.

## Возможности

- **Управление в один клик:** установка и обновление компонентов `zapret`.
- **Гибкий запуск:** включение/остановка службы с возможностью выбора профиля или режима автоподбора параметров.
- **Интеграция с системой:** настройка автозапуска при входе в Windows (через Планировщик задач).
- **Продвинутая настройка:** встроенный редактор маршрутов (`list-general.txt`) и функция корректного удаления сервиса из системы.

## Структура проекта

```text
src/
  AntiZapretDPI.Desktop.slnx     # WPF-приложение
  AntiZapretDPI.Desktop/         # Исходный код GUI
  AntiZapretDPI.Installer.slnx   # Сборка установщика
  AntiZapretDPI.Installer/       # Скрипты Inno Setup (installer.iss, build-installer.bat)
dist/                            # Готовые установочные файлы (результат сборки)
```

## Сборка

Для компиляции исходного кода потребуется **.NET 10 SDK**, для сборки инсталлятора — **[Inno Setup](https://jrsoftware.org/isinfo.php) 6.0+**.

```powershell
# Сборка WPF-приложения
dotnet build src\AntiZapretDPI.Desktop.slnx -c Release

# Сборка установщика с указанием версии
dotnet build src\AntiZapretDPI.Installer.slnx -c Release -p:InstallerVersion=1.0.0
```

## Тихая установка и удаление

Поддерживается автоматическое развертывание без графического интерфейса пользователя:

```powershell
# Установка
AntiZapretDPI-Setup-1.0.0.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

# Удаление
"C:\Program Files\AntiZapretDPI\Uninstall.exe" /VERYSILENT /SUPPRESSMSGBOXES
```

## Благодарности

Этот проект существует благодаря открытым разработкам сообщества. Огромная благодарность авторам следующих инструментов и библиотек:

- **[zapret](https://github.com/bol-van/zapret)** — за создание мощного и гибкого ядра для обхода систем DPI.
- **[zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube)** — за отличные готовые конфигурации, скрипты и профили.
- **[WPF-UI](https://github.com/lepoco/wpfui)** — за красивые современные компоненты и Fluent Design для классического WPF.
- **[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)** — за легкую, быструю и удобную реализацию паттерна MVVM.

## Лицензия

[GPL-3.0](LICENSE)
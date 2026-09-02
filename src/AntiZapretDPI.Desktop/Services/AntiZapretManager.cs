using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using AntiZapretDPI.Contracts;

namespace AntiZapretDPI.Services
{
    public class AntiZapretManager : IAntiZapretManager
    {
        private const string ReleaseUrl = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";

        private static readonly string RootFolder = Path.Combine(
            AppContext.BaseDirectory,
            "zapret"
        );

        private static readonly string VersionFile = Path.Combine(RootFolder, "version.txt");

        private static readonly string ErrorLogFile = Path.Combine(RootFolder, "winws_error.log");

        private const int MoveFileDelayUntilReboot = 0x4;

        private static readonly HttpClient _httpClient = new()
        {
            DefaultRequestHeaders = { { "User-Agent", "AntiZapretDPI" } }
        };

        private Process? _runningProcess;

        public bool IsInstalled()
        {
            return Directory.Exists(RootFolder)
                && Directory.GetFiles(RootFolder, "winws.exe", SearchOption.AllDirectories).Length > 0;
        }

        public bool IsRunning()
        {
            return Process.GetProcessesByName("winws").Length > 0;
        }

        public string GetLocalVersion()
        {
            if (File.Exists(VersionFile))
            {
                return File.ReadAllText(VersionFile).Trim();
            }
            return "Не установлено";
        }

        public List<string> GetAvailablePresets()
        {
            if (!IsInstalled()) return new();
            return Directory.GetFiles(RootFolder, "*.bat", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n)
                    && !n.StartsWith("service", StringComparison.OrdinalIgnoreCase))
                .Cast<string>()
                .ToList();
        }

        public string GetRoutingFilePath()
        {
            if (!IsInstalled()) return string.Empty;

            var listsDir = GetPaths().ListsDir;
            var general = Path.Combine(listsDir, "list-general.txt");
            if (File.Exists(general))
            {
                return general;
            }

            return Directory.GetFiles(RootFolder, "list-general.txt", SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;
        }

        public async Task<string?> CheckLatestVersionAsync()
        {
            try
            {
                var release = await _httpClient.GetFromJsonAsync<GithubRelease>(ReleaseUrl);
                return release?.TagName;
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> DownloadAndInstallAsync(IProgress<string>? progress = null)
        {
            try
            {
                progress?.Report("Получение информации о релизе...");
                var release = await _httpClient.GetFromJsonAsync<GithubRelease>(ReleaseUrl);
                if (release == null || string.IsNullOrEmpty(release.TagName))
                {
                    throw new Exception("Не удалось получить данные о последней версии с GitHub.");
                }

                var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                          ?? release.Assets.FirstOrDefault();

                if (asset == null)
                {
                    throw new Exception("В релизе не найден архив для скачивания.");
                }

                progress?.Report($"Скачивание версии {release.TagName}...");
                var zipBytes = await _httpClient.GetByteArrayAsync(asset.BrowserDownloadUrl);
                var tempZip = Path.Combine(Path.GetTempPath(), "antizapret_update.zip");
                await File.WriteAllBytesAsync(tempZip, zipBytes);

                progress?.Report("Распаковка файлов...");
                if (Directory.Exists(RootFolder))
                {
                    StopZapret();
                    try { Directory.Delete(RootFolder, true); } catch { }
                }

                Directory.CreateDirectory(RootFolder);

                ZipFile.ExtractToDirectory(tempZip, RootFolder, true);
                File.Delete(tempZip);

                var subDirs = Directory.GetDirectories(RootFolder);
                if (subDirs.Length == 1 && Directory.GetFiles(RootFolder, "*.exe", SearchOption.TopDirectoryOnly).Length == 0)
                {
                    var subDir = subDirs[0];
                    foreach (var file in Directory.GetFiles(subDir))
                    {
                        var dest = Path.Combine(RootFolder, Path.GetFileName(file));
                        File.Move(file, dest, true);
                    }
                    foreach (var dir in Directory.GetDirectories(subDir))
                    {
                        var dirName = Path.GetFileName(dir);
                        var dest = Path.Combine(RootFolder, dirName);
                        if (Directory.Exists(dest)) Directory.Delete(dest, true);
                        Directory.Move(dir, dest);
                    }
                    try { Directory.Delete(subDir, true); } catch { }
                }

                File.WriteAllText(VersionFile, release.TagName);
                progress?.Report("Установка завершена успешно!");
                return true;
            }
            catch (Exception ex)
            {
                progress?.Report($"Ошибка: {ex.Message}");
                return false;
            }
        }

        public bool DeleteInstallation()
        {
            try
            {
                StopZapret();
                RemoveWinDivertServices();
                RunSc("stop", "zapret");
                RunSc("delete", "zapret");

                if (!Directory.Exists(RootFolder))
                {
                    return true;
                }

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        Directory.Delete(RootFolder, true);
                        return true;
                    }
                    catch
                    {
                        System.Threading.Thread.Sleep(500);
                    }
                }

                ScheduleDeleteAtReboot(RootFolder);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool StartZapret(out string errorDetails, string presetName = "general.bat", bool hiddenMode = true)
        {
            errorDetails = string.Empty;

            if (!IsInstalled())
            {
                errorDetails = "Zapret не установлен.";
                return false;
            }

            StopZapret();

            var (winwsPath, binDir, listsDir) = GetPaths();
            if (string.IsNullOrEmpty(winwsPath) || !File.Exists(winwsPath))
            {
                errorDetails = "Файл winws.exe не найден.";
                return false;
            }

            string arguments = BuildWinwsArguments(presetName, binDir, listsDir);
            EnsureReferencedFilesExist(arguments);

            var psi = new ProcessStartInfo
            {
                FileName = winwsPath,
                Arguments = arguments,
                WorkingDirectory = binDir,
                CreateNoWindow = hiddenMode,
                UseShellExecute = false,
                WindowStyle = hiddenMode ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
                RedirectStandardError = hiddenMode,
                RedirectStandardOutput = hiddenMode
            };

            try
            {
                _runningProcess = Process.Start(psi);
                System.Threading.Thread.Sleep(700);

                if (_runningProcess != null && !_runningProcess.HasExited)
                {
                    return true;
                }

                int exitCode = _runningProcess?.ExitCode ?? -1;
                string err = string.Empty;
                try { err = _runningProcess?.StandardError.ReadToEnd() ?? ""; } catch { }
                errorDetails = $"winws завершился с кодом {exitCode}. {err} Подробности в логе: {ErrorLogFile}";
                WriteErrorLog($"Код завершения {exitCode}, Ошибка: {err}\nФайл: {winwsPath}\nПапка bin: {binDir}\nПапка lists: {listsDir}\nАргументы: {arguments}");
                return false;
            }
            catch (Exception ex)
            {
                WriteErrorLog(ex.ToString());
                errorDetails = DescribeStartError(ex, winwsPath);
                return false;
            }
        }

        public void StopZapret()
        {
            try
            {
                RunSc("stop", "zapret");
            }
            catch { }

            try
            {
                if (_runningProcess != null && !_runningProcess.HasExited)
                {
                    _runningProcess.Kill(true);
                }
                _runningProcess?.Dispose();
                _runningProcess = null;
            }
            catch { }

            try
            {
                foreach (var p in Process.GetProcessesByName("winws"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }

            try
            {
                if (Process.GetProcessesByName("winws").Length > 0)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "taskkill.exe",
                        Arguments = "/F /T /IM winws.exe",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psi)?.WaitForExit(3000);
                }
            }
            catch { }
        }

        private void RemoveWinDivertServices()
        {
            foreach (var name in new[] { "WinDivert14", "WinDivert" })
            {
                try { RunSc("stop", name); } catch { }
                try { RunSc("delete", name); } catch { }
            }
        }

        private static void RunSc(string action, string serviceName)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"{action} {serviceName}",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }

        private static void ScheduleDeleteAtReboot(string root)
        {
            foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
            {
                MoveFileEx(dir, null, MoveFileDelayUntilReboot);
            }
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                MoveFileEx(file, null, MoveFileDelayUntilReboot);
            }
            MoveFileEx(root, null, MoveFileDelayUntilReboot);
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, int dwFlags);

        private static string DescribeStartError(Exception ex, string file)
        {
            string logHint = $"Подробности в логе: {ErrorLogFile}";
            if (ex is Win32Exception win32)
            {
                if (win32.NativeErrorCode == 5)
                {
                    return $"Не удалось запустить процесс: доступ запрещён. Запустите программу от имени администратора или разблокируйте файл: {file} ({logHint})";
                }
                return $"Не удалось запустить процесс: {win32.Message} (код {win32.NativeErrorCode}). Файл: {file} ({logHint})";
            }
            return $"Не удалось запустить процесс: {ex.Message}. Файл: {file} ({logHint})";
        }

        private void WriteErrorLog(string message)
        {
            try
            {
                Directory.CreateDirectory(RootFolder);
                File.AppendAllText(ErrorLogFile, $"{DateTime.Now}: {message}\n");
            }
            catch { }
        }

        private void EnsureReferencedFilesExist(string arguments)
        {
            try
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    arguments,
                    @"--(?:hostlist|hostlist-exclude|ipset|ipset-exclude)=""([^""]+)""");

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var path = match.Groups[1].Value;
                    if (string.IsNullOrEmpty(path) || File.Exists(path))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, string.Empty);
                    WriteErrorLog($"Создан отсутствующий файл списка: {path}");
                }
            }
            catch
            {
            }
        }

        private (string WinwsPath, string BinDir, string ListsDir) GetPaths()
        {
            var winwsPath = Directory.GetFiles(RootFolder, "winws.exe", SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;
            var binDir = string.IsNullOrEmpty(winwsPath) ? RootFolder : (Path.GetDirectoryName(winwsPath) ?? RootFolder);
            var listsDir = Path.Combine(RootFolder, "lists");
            if (!Directory.Exists(listsDir))
            {
                listsDir = Path.GetFullPath(Path.Combine(binDir, "..", "lists"));
            }
            return (winwsPath, binDir, listsDir);
        }

        private string BuildWinwsArguments(string presetName, string binDir, string listsDir)
        {
            string fallback = $"--wf-tcp=80,443,2053,2083,2087,2096,8443,12 --wf-udp=443,19294-19344,50000-50100,12 --filter-udp=443 --hostlist=\"{Path.Combine(listsDir, "list-general.txt")}\" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{Path.Combine(binDir, "quic_initial_www_google_com.bin")}\" --new";

            var batFiles = Directory.GetFiles(RootFolder, "*.bat", SearchOption.AllDirectories);
            var targetScript = batFiles.FirstOrDefault(f => Path.GetFileName(f).Equals(presetName, StringComparison.OrdinalIgnoreCase))
                        ?? batFiles.FirstOrDefault(f => Path.GetFileName(f).Contains("general", StringComparison.OrdinalIgnoreCase))
                        ?? batFiles.FirstOrDefault();

            if (targetScript == null || !File.Exists(targetScript))
                return fallback;

            try
            {
                var lines = File.ReadAllLines(targetScript);
                int startIdx = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        startIdx = i;
                        break;
                    }
                }

                if (startIdx < 0)
                    return fallback;

                var sb = new StringBuilder();
                for (int i = startIdx; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (i == startIdx)
                    {
                        int idx = line.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase);
                        line = idx >= 0 ? line.Substring(idx + "winws.exe".Length) : line;
                        line = line.TrimStart(' ', '"');
                    }
                    sb.Append(line);
                    if (line.TrimEnd().EndsWith("^"))
                    {
                        sb.Length -= 1;
                        sb.Append(' ');
                    }
                    else
                    {
                        break;
                    }
                }

                var args = sb.ToString();
                args = args.Replace("%BIN%", binDir + "\\")
                           .Replace("%LISTS%", listsDir + "\\")
                           .Replace("%GameFilterTCP%", "12")
                           .Replace("%GameFilterUDP%", "12")
                           .Replace("%GameFilter%", "12")
                           .Replace("^", "");
                args = System.Text.RegularExpressions.Regex.Replace(args, @"\s+", " ").Trim();
                return args;
            }
            catch
            {
                return fallback;
            }
        }

        private sealed class GithubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonPropertyName("assets")]
            public List<GithubAsset> Assets { get; set; } = new();
        }

        private sealed class GithubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;
        }
    }
}

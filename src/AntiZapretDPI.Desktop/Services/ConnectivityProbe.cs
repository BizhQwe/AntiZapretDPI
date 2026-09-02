using System.Diagnostics;
using System.IO;
using AntiZapretDPI.Contracts;
using AntiZapretDPI.Helpers;

namespace AntiZapretDPI.Services
{
    public class ConnectivityProbe : IConnectivityProbe
    {
        private const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        private const string DiscordGatewayUrl = "https://discord.com/api/v10/gateway";

        private const string YoutubeProbeUrl = "https://www.youtube.com/generate_204";

        private const string YoutubeSiteUrl = "https://www.youtube.com";

        private const string YoutubeCdnUrl = "https://redirector.googlevideo.com";

        private static readonly string LogFile = Path.Combine(
            AppContext.BaseDirectory,
            "zapret",
            "check.log"
        );

        private static readonly object LogLock = new();

        public async Task<bool> IsBasicAccessRestoredAsync(CancellationToken cancellationToken = default)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (await TryReachDiscordAsync(cancellationToken))
                {
                    LogProbe("Базовый доступ: Discord OK");
                    return true;
                }

                if (await TryReachYouTubeAsync(cancellationToken))
                {
                    LogProbe("Базовый доступ: YouTube 204 OK");
                    return true;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                await Task.Delay(800, cancellationToken);
            }

            LogProbe("Базовый доступ: не восстановлен");
            return false;
        }

        // Проверка как во встроенном тесте zapret (utils/test zapret.ps1): curl.exe HEAD
        // по ключевым целям. Сайт + вход в видеосервер (CDN) => полноценное воспроизведение.
        public async Task<YouTubeAccessVerdict> ProbeYouTubeVideoAsync(CancellationToken cancellationToken = default)
        {
            bool anyReachable = false;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return anyReachable ? YouTubeAccessVerdict.StreamReachable : YouTubeAccessVerdict.NoAccess;
                }

                LogProbe($"--- Попытка {attempt + 1}: проверка curl ---");

                var results = await Task.WhenAll(
                    IsUrlReachableByCurlAsync(YoutubeSiteUrl, cancellationToken),
                    IsUrlReachableByCurlAsync(YoutubeCdnUrl, cancellationToken));
                bool site = results[0];
                bool cdn = results[1];

                anyReachable |= site || cdn;

                if (site && cdn)
                {
                    LogProbe("Вердикт: PlaybackConfirmed (сайт и CDN отвечают)");
                    return YouTubeAccessVerdict.PlaybackConfirmed;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return anyReachable ? YouTubeAccessVerdict.StreamReachable : YouTubeAccessVerdict.NoAccess;
                }

                // Полностью недоступный профиль не даст результата со второй попытки —
                // сразу NoAccess. Дополнительный шанс даём только при частичном ответе.
                if (!anyReachable)
                {
                    LogProbe("Вердикт: NoAccess (нет ответа от сайта и CDN)");
                    return YouTubeAccessVerdict.NoAccess;
                }

                LogProbe("Отвечает не всё, короткая пауза перед повтором...");
                await Task.Delay(1000, cancellationToken);
            }

            LogProbe("Вердикт: StreamReachable (частичный доступ)");
            return YouTubeAccessVerdict.StreamReachable;
        }

        private static async Task<bool> TryReachDiscordAsync(CancellationToken cancellationToken)
        {
            return await IsUrlReachableByCurlAsync(DiscordGatewayUrl, cancellationToken);
        }

        private static async Task<bool> TryReachYouTubeAsync(CancellationToken cancellationToken)
        {
            return await IsUrlReachableByCurlAsync(YoutubeProbeUrl, cancellationToken);
        }

        // Сначала HTTP/1.1, затем TLS1.3 — как в стандартном тесте связки.
        private static async Task<bool> IsUrlReachableByCurlAsync(string url, CancellationToken ct)
        {
            string[] variants = { "--http1.1", "--tlsv1.3 --tls-max 1.3" };
            foreach (var variant in variants)
            {
                if (ct.IsCancellationRequested)
                {
                    return false;
                }

                int code = await CurlStatusCodeAsync(url, variant, ct);
                LogProbe($"curl {url} [{variant}] -> {code}");
                if (code > 0)
                {
                    return true;
                }
            }

            return false;
        }

        // Как встроенный тест zapret ("utils/test zapret.ps1"): HEAD-запрос curl.exe,
        // код ответа пишем в stdout, тело — в NUL.
        private static async Task<int> CurlStatusCodeAsync(string url, string tlsArgs, CancellationToken ct)
        {
            string args = $"-s --show-error -m 3 --connect-timeout 2 -I -o NUL -w \"%{{http_code}}\" " +
                          $"{tlsArgs} -A \"{BrowserUserAgent}\" \"{url}\"";
            var output = await RunCurlAsync(args, ct);
            return int.TryParse(output.Trim(), out int code) && code > 0 ? code : 0;
        }

        private static async Task<string> RunCurlAsync(string arguments, CancellationToken ct)
        {
            try
            {
                var psi = new ProcessStartInfo("curl.exe", arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    return string.Empty;
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
                var stderrTask = process.StandardError.ReadToEndAsync(ct);

                try
                {
                    await process.WaitForExitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch { }
                    throw;
                }

                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(stdout) && !string.IsNullOrWhiteSpace(stderr))
                {
                    LogProbe($"curl: {TextHelper.Truncate(stderr, 200)}");
                }

                return stdout ?? string.Empty;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogProbe($"curl: {ex.GetType().Name}");
                return string.Empty;
            }
        }

        private static void LogProbe(string message)
        {
            try
            {
                lock (LogLock)
                {
                    File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
                }
            }
            catch
            {
            }
        }
    }
}

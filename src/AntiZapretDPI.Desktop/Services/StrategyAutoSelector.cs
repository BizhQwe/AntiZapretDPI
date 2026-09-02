using AntiZapretDPI.Contracts;
using AntiZapretDPI.Helpers;

namespace AntiZapretDPI.Services
{
    public class StrategyAutoSelector : IStrategyAutoSelector
    {
        private const int ProfileWarmupDelayMs = 1500;

        private readonly IAntiZapretManager _manager;
        private readonly IConnectivityProbe _probe;

        public StrategyAutoSelector(IAntiZapretManager manager, IConnectivityProbe probe)
        {
            _manager = manager;
            _probe = probe;
        }

        public async Task<StrategySelectionResult> TrySelectAsync(
            IReadOnlyList<string> profiles,
            string? preferred,
            bool hiddenMode,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (profiles.Count == 0)
            {
                return new StrategySelectionResult(StrategySelectionStatus.None, null);
            }

            var ordered = profiles
                .OrderBy(p => !string.Equals(p, preferred, StringComparison.OrdinalIgnoreCase))
                .ToList();

            string? reachableProfile = null;
            string? firstStartedProfile = null;

            foreach (var profile in ordered)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                progress?.Report(TextHelper.Truncate($"Проверяю: {profile}", 90));

                string startError = string.Empty;
                bool started = await Task.Run(() => _manager.StartZapret(out startError, profile, hiddenMode));
                if (!started)
                {
                    continue;
                }
                firstStartedProfile ??= profile;

                if (!await WaitForProfileReadyAsync(cancellationToken)
                    || cancellationToken.IsCancellationRequested)
                {
                    await Task.Run(_manager.StopZapret);
                    break;
                }

                var verdict = await _probe.ProbeYouTubeVideoAsync(cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    await Task.Run(_manager.StopZapret);
                    break;
                }

                if (verdict == YouTubeAccessVerdict.PlaybackConfirmed)
                {
                    return new StrategySelectionResult(StrategySelectionStatus.Confirmed, profile);
                }

                if (reachableProfile == null && verdict == YouTubeAccessVerdict.StreamReachable)
                {
                    reachableProfile = profile;
                }

                await Task.Run(_manager.StopZapret);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                await Task.Run(_manager.StopZapret);
                return new StrategySelectionResult(StrategySelectionStatus.Cancelled, null);
            }

            if (reachableProfile != null
                && await TryStartAsync(reachableProfile, hiddenMode))
            {
                return new StrategySelectionResult(StrategySelectionStatus.Partial, reachableProfile);
            }

            if (firstStartedProfile != null
                && await TryStartAsync(firstStartedProfile, hiddenMode))
            {
                return new StrategySelectionResult(StrategySelectionStatus.UnconfirmedFallback, firstStartedProfile);
            }

            await Task.Run(_manager.StopZapret);
            return new StrategySelectionResult(StrategySelectionStatus.None, null);
        }

        private async Task<bool> TryStartAsync(string profile, bool hiddenMode)
        {
            string error = string.Empty;
            return await Task.Run(() => _manager.StartZapret(out error, profile, hiddenMode));
        }

        private static async Task<bool> WaitForProfileReadyAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(ProfileWarmupDelayMs, ct);
                return !ct.IsCancellationRequested;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}

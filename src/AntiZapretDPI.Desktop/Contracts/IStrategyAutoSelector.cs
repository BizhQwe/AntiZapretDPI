namespace AntiZapretDPI.Contracts
{
    public enum StrategySelectionStatus
    {
        None = 0,
        Cancelled,
        Confirmed,
        Partial,
        UnconfirmedFallback
    }

    public sealed record StrategySelectionResult(StrategySelectionStatus Status, string? Profile)
    {
        public bool IsSuccess => Profile is not null;
    }

    public interface IStrategyAutoSelector
    {
        Task<StrategySelectionResult> TrySelectAsync(
            IReadOnlyList<string> profiles,
            string? preferred,
            bool hiddenMode,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default);
    }
}

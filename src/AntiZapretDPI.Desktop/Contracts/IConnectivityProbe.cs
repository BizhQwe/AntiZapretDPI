namespace AntiZapretDPI.Contracts
{
    public interface IConnectivityProbe
    {
        Task<bool> IsBasicAccessRestoredAsync(CancellationToken cancellationToken = default);

        Task<YouTubeAccessVerdict> ProbeYouTubeVideoAsync(CancellationToken cancellationToken = default);
    }
}

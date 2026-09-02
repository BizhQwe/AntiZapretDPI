namespace AntiZapretDPI.Contracts
{
    public interface IAntiZapretManager
    {
        string GetInstallPath();

        bool IsInstalled();

        bool IsRunning();

        string GetLocalVersion();

        string GetRoutingFilePath();

        List<string> GetAvailablePresets();

        Task<string?> CheckLatestVersionAsync();

        Task<bool> DownloadAndInstallAsync(IProgress<string>? progress = null);

        bool StartZapret(out string errorDetails, string presetName = "general.bat", bool hiddenMode = true);

        void StopZapret();

        bool DeleteInstallation();

        Task<bool> IsAccessRestoredAsync();
    }
}

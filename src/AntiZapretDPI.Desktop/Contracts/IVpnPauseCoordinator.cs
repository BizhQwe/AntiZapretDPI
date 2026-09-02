namespace AntiZapretDPI.Contracts
{
    public interface IVpnPauseCoordinator
    {
        bool IsRunning { get; }

        bool EnsureStarted();

        void EnsureStopped(int timeoutMs = 0);
    }
}

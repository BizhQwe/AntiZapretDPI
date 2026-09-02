namespace AntiZapretDPI.Contracts
{
    public interface IAutoStartManager
    {
        bool IsEnabled { get; }

        void Enable();

        void Disable();
    }
}

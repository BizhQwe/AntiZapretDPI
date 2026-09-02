namespace AntiZapretDPI.Contracts
{
    public interface IAppState
    {
        bool CanExecute(AppAction action);
    }
}

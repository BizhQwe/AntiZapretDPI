using AntiZapretDPI.Services.StateMachine;

namespace AntiZapretDPI.Contracts
{
    public interface IAppState
    {
        bool CanExecute(AppAction action);
    }
}

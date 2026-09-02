using AntiZapretDPI.Contracts;

namespace AntiZapretDPI.Services.StateMachine
{
    public abstract class AppStateBase : IAppState
    {
        public abstract bool CanExecute(AppAction action);
    }
}

namespace AntiZapretDPI.Contracts
{
    public interface IAppStateMachine
    {
        IAppState CurrentState { get; }

        event Action<IAppState>? StateChanged;

        bool CanExecute(AppAction action);

        void MoveTo<TState>() where TState : IAppState;
    }
}

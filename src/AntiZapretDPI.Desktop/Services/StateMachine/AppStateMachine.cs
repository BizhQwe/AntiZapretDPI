using AntiZapretDPI.Contracts;
using AntiZapretDPI.Services.StateMachine.States;

namespace AntiZapretDPI.Services.StateMachine
{
    public class AppStateMachine : IAppStateMachine
    {
        private readonly IReadOnlyDictionary<Type, IAppState> _states;
        private IAppState _current;

        public IAppState CurrentState => _current;

        public event Action<IAppState>? StateChanged;

        public AppStateMachine(IEnumerable<IAppState> states)
        {
            _states = states.ToDictionary(s => s.GetType());
            _current = _states[typeof(NotInstalledState)];
        }

        public bool CanExecute(AppAction action) => _current.CanExecute(action);

        public void MoveTo<TState>() where TState : IAppState
        {
            if (!_states.TryGetValue(typeof(TState), out var next))
            {
                return;
            }

            if (ReferenceEquals(_current, next))
            {
                return;
            }

            _current = next;
            StateChanged?.Invoke(next);
        }
    }
}

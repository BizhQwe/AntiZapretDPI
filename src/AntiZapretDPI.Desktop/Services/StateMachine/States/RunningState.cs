using AntiZapretDPI.Contracts;

namespace AntiZapretDPI.Services.StateMachine.States
{
    public class RunningState : AppStateBase
    {
        public override bool CanExecute(AppAction action) =>
            action is StopAction or RefreshAction;
    }
}

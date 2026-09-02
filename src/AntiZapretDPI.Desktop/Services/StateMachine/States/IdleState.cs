using AntiZapretDPI.Contracts;

namespace AntiZapretDPI.Services.StateMachine.States
{
    public class IdleState : AppStateBase
    {
        public override bool CanExecute(AppAction action) =>
            action is StartAction or UpdateAction or DeleteAction or EditAction or RefreshAction;
    }
}

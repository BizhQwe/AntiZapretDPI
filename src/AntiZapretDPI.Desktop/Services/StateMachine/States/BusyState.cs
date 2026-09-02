namespace AntiZapretDPI.Services.StateMachine.States
{
    public class BusyState : AppStateBase
    {
        public override bool CanExecute(AppAction action) => false;
    }
}

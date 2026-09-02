namespace AntiZapretDPI.Services.StateMachine.States
{
    public class NotInstalledState : AppStateBase
    {
        public override bool CanExecute(AppAction action) =>
            action is DownloadAction or RefreshAction;
    }
}

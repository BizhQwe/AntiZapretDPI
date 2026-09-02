using AntiZapretDPI.ViewModels.Windows;
using Wpf.Ui.Controls;

namespace AntiZapretDPI.Views.Windows
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow(MainViewModel mainViewModel)
        {
            InitializeComponent();

            DataContext = mainViewModel;
        }
    }
}

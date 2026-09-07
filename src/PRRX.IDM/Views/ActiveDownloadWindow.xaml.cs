using System.Windows;
using System.Windows.Input;
using PRRX.IDM.ViewModels;

namespace PRRX.IDM.Views
{
    public partial class ActiveDownloadWindow : Window
    {
        public ActiveDownloadWindow(ActiveDownloadViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.RequestClose += () => Close();
            Loaded += (_, _) => viewModel.Start();
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

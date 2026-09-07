using System.Windows;
using System.Windows.Input;
using PRRX.IDM.ViewModels;

namespace PRRX.IDM.Views
{
    public partial class DownloadFileInfoDialog : Window
    {
        public DownloadFileInfoDialog(DownloadFileInfoViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.RequestClose += () => Close();
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

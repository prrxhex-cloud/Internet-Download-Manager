using System;
using System.Windows.Media.Imaging;
using PRRX.IDM.ViewModels;
using Wpf.Ui.Controls;

namespace PRRX.IDM.Views
{
    public partial class MainWindow : FluentWindow
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            try
            {
                Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/app_icon.png", UriKind.Absolute));
            }
            catch
            {
                // Ignore icon fallback
            }
        }
    }
}

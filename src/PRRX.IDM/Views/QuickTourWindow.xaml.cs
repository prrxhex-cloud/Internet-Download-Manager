using System;
using System.Windows.Media.Imaging;
using PRRX.IDM.ViewModels;
using Wpf.Ui.Controls;

namespace PRRX.IDM.Views
{
    public partial class QuickTourWindow : FluentWindow
    {
        public QuickTourWindow(QuickTourViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            try
            {
                Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/app_icon.png", UriKind.Absolute));
            }
            catch
            {
                // Fallback
            }

            viewModel.RequestClose += () =>
            {
                Close();
            };
        }
    }
}

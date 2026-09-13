// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System.Windows;
using System.Windows.Input;
using PRRX.IDM.Services;
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
            MemoryOptimizer.HookWindow(this);
            Closed += (_, _) => MemoryOptimizer.TrimMemory();
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

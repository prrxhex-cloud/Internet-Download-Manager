// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Windows.Media.Imaging;
using PRRX.IDM.ViewModels;
using Wpf.Ui.Controls;

namespace PRRX.IDM.Views
{
    public partial class OnboardingWindow : FluentWindow
    {
        public OnboardingWindow(OnboardingViewModel viewModel)
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

            viewModel.OnboardingCompleted += () =>
            {
                DialogResult = true;
                Close();
            };
        }
    }
}

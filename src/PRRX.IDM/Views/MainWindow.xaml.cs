// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.IO;
using System.Windows;
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

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || 
                e.Data.GetDataPresent(DataFormats.UnicodeText) || 
                e.Data.GetDataPresent(DataFormats.Text))
            {
                e.Effects = DragDropEffects.Copy;
                DropOverlay.Visibility = Visibility.Visible;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || 
                e.Data.GetDataPresent(DataFormats.UnicodeText) || 
                e.Data.GetDataPresent(DataFormats.Text))
            {
                e.Effects = DragDropEffects.Copy;
                if (DropOverlay.Visibility != Visibility.Visible)
                {
                    DropOverlay.Visibility = Visibility.Visible;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            try
            {
                if (e.Data.GetDataPresent("UniformResourceLocatorW"))
                {
                    var data = e.Data.GetData("UniformResourceLocatorW");
                    string? url = data switch
                    {
                        string s => s,
                        MemoryStream ms => System.Text.Encoding.Unicode.GetString(ms.ToArray()).TrimEnd('\0'),
                        _ => null
                    };
                    if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out _))
                    {
                        App.LaunchDownloadPrompt(url.Trim());
                        e.Handled = true;
                        return;
                    }
                }
                else if (e.Data.GetDataPresent("UniformResourceLocator"))
                {
                    var data = e.Data.GetData("UniformResourceLocator");
                    string? url = data switch
                    {
                        string s => s,
                        MemoryStream ms => System.Text.Encoding.Default.GetString(ms.ToArray()).TrimEnd('\0'),
                        _ => null
                    };
                    if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out _))
                    {
                        App.LaunchDownloadPrompt(url.Trim());
                        e.Handled = true;
                        return;
                    }
                }

                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                    {
                        foreach (var file in files)
                        {
                            if (string.IsNullOrWhiteSpace(file)) continue;
                            var fileName = Path.GetFileName(file);
                            if (file.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                            {
                                App.LaunchDownloadPrompt(file, fileName, 0, fileName);
                            }
                            else if (File.Exists(file))
                            {
                                App.LaunchDownloadPrompt(file, fileName, new FileInfo(file).Length, fileName);
                            }
                        }
                    }
                }
                else if (e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text))
                {
                    var text = (e.Data.GetData(DataFormats.UnicodeText) as string) ?? (e.Data.GetData(DataFormats.Text) as string);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var rawLine in lines)
                        {
                            var line = rawLine.Trim();
                            if (Uri.TryCreate(line, UriKind.Absolute, out var uri) && 
                                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "magnet" || uri.Scheme == Uri.UriSchemeFtp))
                            {
                                App.LaunchDownloadPrompt(line);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Drop error: {ex.Message}");
            }
            e.Handled = true;
        }
    }
}

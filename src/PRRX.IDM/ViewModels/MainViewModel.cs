// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Windows.Input;
using PRRX.IDM.Services;

namespace PRRX.IDM.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IConfigurationService _configService;
        private readonly IThemeService _themeService;
        private readonly IMediaEngineService _mediaEngine;
        private readonly IThumbnailService _thumbnailService;
        private readonly IUpdateService _updateService;
        private readonly IHistoryService _historyService;

        private ViewModelBase _currentTabViewModel;
        private string _selectedTabKey = "Video";

        public VideoDownloaderViewModel VideoViewModel { get; }
        public AudioConverterViewModel AudioViewModel { get; }
        public ThumbnailViewModel ThumbnailViewModel { get; }
        public SettingsViewModel SettingsViewModel { get; }

        public ViewModelBase CurrentTabViewModel
        {
            get => _currentTabViewModel;
            set => SetProperty(ref _currentTabViewModel, value);
        }

        public string SelectedTabKey
        {
            get => _selectedTabKey;
            set => SetProperty(ref _selectedTabKey, value);
        }

        public ICommand NavigateTabCommand { get; }

        public MainViewModel(
            IConfigurationService configService,
            IThemeService themeService,
            IMediaEngineService mediaEngine,
            IThumbnailService thumbnailService,
            IUpdateService updateService,
            IHistoryService historyService)
        {
            _configService = configService;
            _themeService = themeService;
            _mediaEngine = mediaEngine;
            _thumbnailService = thumbnailService;
            _updateService = updateService;
            _historyService = historyService;

            VideoViewModel = new VideoDownloaderViewModel(_mediaEngine, _configService, _historyService);
            AudioViewModel = new AudioConverterViewModel(_mediaEngine, _configService);
            ThumbnailViewModel = new ThumbnailViewModel(_thumbnailService, _configService);
            SettingsViewModel = new SettingsViewModel(_configService, _themeService, _updateService, _mediaEngine);

            _currentTabViewModel = VideoViewModel;

            NavigateTabCommand = new RelayCommand(param =>
            {
                if (param is string tabKey)
                {
                    SelectedTabKey = tabKey;
                    var prevTab = CurrentTabViewModel;
                    CurrentTabViewModel = tabKey switch
                    {
                        "Video" => VideoViewModel,
                        "Audio" => AudioViewModel,
                        "Thumbnail" => ThumbnailViewModel,
                        "Settings" => SettingsViewModel,
                        _ => VideoViewModel
                    };

                    if (prevTab is ThumbnailViewModel thumbVm)
                    {
                        thumbVm.UnloadResources();
                    }

                    // Free unused tab resources and compact working set
                    MemoryOptimizer.TrimMemory();
                }
            });
        }
    }
}

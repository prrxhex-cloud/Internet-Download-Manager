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
        private readonly IBrowserIntegrationService _browserService;
        private readonly ISiteSpiderGrabberService _spiderService;
        private readonly IScheduleService _scheduleService;

        private ViewModelBase _currentTabViewModel;
        private string _selectedTabKey = "Video";

        // Lazy-loaded tab view models for instant cold start
        private VideoDownloaderViewModel? _videoViewModel;
        private IdmDownloadsViewModel? _idmDownloadsViewModel;
        private AudioConverterViewModel? _audioViewModel;
        private ThumbnailViewModel? _thumbnailViewModel;
        private SettingsViewModel? _settingsViewModel;
        private SiteGrabberViewModel? _siteGrabberViewModel;

        public VideoDownloaderViewModel VideoViewModel =>
            _videoViewModel ??= new VideoDownloaderViewModel(_mediaEngine, _configService, _historyService);

        public IdmDownloadsViewModel IdmDownloadsViewModel =>
            _idmDownloadsViewModel ??= new IdmDownloadsViewModel(_configService, _historyService, _browserService);

        public AudioConverterViewModel AudioViewModel =>
            _audioViewModel ??= new AudioConverterViewModel(_mediaEngine, _configService);

        public ThumbnailViewModel ThumbnailViewModel =>
            _thumbnailViewModel ??= new ThumbnailViewModel(_thumbnailService, _configService);

        public SettingsViewModel SettingsViewModel =>
            _settingsViewModel ??= new SettingsViewModel(_configService, _themeService, _updateService, _mediaEngine);

        public SiteGrabberViewModel SiteGrabberViewModel =>
            _siteGrabberViewModel ??= new SiteGrabberViewModel(_spiderService, _scheduleService, _configService);

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
            IHistoryService historyService,
            IBrowserIntegrationService browserService,
            ISiteSpiderGrabberService? spiderService = null,
            IScheduleService? scheduleService = null)
        {
            _configService = configService;
            _themeService = themeService;
            _mediaEngine = mediaEngine;
            _thumbnailService = thumbnailService;
            _updateService = updateService;
            _historyService = historyService;
            _browserService = browserService;
            _spiderService = spiderService ?? new SiteSpiderGrabberService(configService);
            _scheduleService = scheduleService ?? new ScheduleService();

            // Start with VideoViewModel as default active tab
            _currentTabViewModel = VideoViewModel;

            NavigateTabCommand = new RelayCommand(param =>
            {
                if (param is string tabKey)
                {
                    SelectedTabKey = tabKey;
                    var prevTab = CurrentTabViewModel;
                    CurrentTabViewModel = tabKey switch
                    {
                        "IdmDownloads" => IdmDownloadsViewModel,
                        "Video" => VideoViewModel,
                        "Audio" => AudioViewModel,
                        "Thumbnail" => ThumbnailViewModel,
                        "SiteGrabber" => SiteGrabberViewModel,
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

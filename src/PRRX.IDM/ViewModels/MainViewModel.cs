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
        private ApkHubViewModel? _apkHubViewModel;

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

        public ApkHubViewModel ApkHubViewModel =>
            _apkHubViewModel ??= new ApkHubViewModel(new CloudResolverService(), _configService, _historyService);

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

        private string _telegramStatusDotBrush = "#8A8886";
        private string _telegramStatusBackgroundBrush = "#108A8886";
        private string _telegramStatusBorderBrush = "#308A8886";
        private string _telegramStatusShortText = "Offline";
        private string _telegramStatusTooltip = "Telegram Bot Remote Sync is not connected.";

        public string TelegramStatusDotBrush
        {
            get => _telegramStatusDotBrush;
            set => SetProperty(ref _telegramStatusDotBrush, value);
        }

        public string TelegramStatusBackgroundBrush
        {
            get => _telegramStatusBackgroundBrush;
            set => SetProperty(ref _telegramStatusBackgroundBrush, value);
        }

        public string TelegramStatusBorderBrush
        {
            get => _telegramStatusBorderBrush;
            set => SetProperty(ref _telegramStatusBorderBrush, value);
        }

        public string TelegramStatusShortText
        {
            get => _telegramStatusShortText;
            set => SetProperty(ref _telegramStatusShortText, value);
        }

        public string TelegramStatusTooltip
        {
            get => _telegramStatusTooltip;
            set => SetProperty(ref _telegramStatusTooltip, value);
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
                        "ApkHub" => ApkHubViewModel,
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

            if (TelegramBotSyncService.Current != null)
            {
                TelegramBotSyncService.Current.StatusChanged += (s, e) =>
                {
                    App.Current?.Dispatcher?.BeginInvoke(new Action(UpdateTelegramStatusPill));
                };
            }
            UpdateTelegramStatusPill();
        }

        private void UpdateTelegramStatusPill()
        {
            var service = TelegramBotSyncService.Current;
            if (service == null || !service.IsEnabled)
            {
                TelegramStatusDotBrush = "#8A8886";
                TelegramStatusBackgroundBrush = "#108A8886";
                TelegramStatusBorderBrush = "#308A8886";
                TelegramStatusShortText = "Disabled";
                TelegramStatusTooltip = "Telegram Bot Remote Sync is disabled. Click to configure in Settings.";
                return;
            }

            switch (service.Status)
            {
                case TelegramBotConnectionStatus.Connected:
                    TelegramStatusDotBrush = "#107C41";
                    TelegramStatusBackgroundBrush = "#15107C41";
                    TelegramStatusBorderBrush = "#40107C41";
                    TelegramStatusShortText = "Online";
                    var user = !string.IsNullOrEmpty(service.LinkedUsername) ? "@" + service.LinkedUsername.TrimStart('@') : (!string.IsNullOrEmpty(service.LinkedChatId) ? service.LinkedChatId : "Active");
                    TelegramStatusTooltip = $"PRRX IDM Bot Online & Synced ({user}). Click to view Settings.";
                    break;
                case TelegramBotConnectionStatus.Connecting:
                    TelegramStatusDotBrush = "#CA5010";
                    TelegramStatusBackgroundBrush = "#15CA5010";
                    TelegramStatusBorderBrush = "#40CA5010";
                    TelegramStatusShortText = "Polling";
                    TelegramStatusTooltip = "Connecting to Cloudflare & Telegram Bot...";
                    break;
                case TelegramBotConnectionStatus.PairingRequired:
                    TelegramStatusDotBrush = "#D83B01";
                    TelegramStatusBackgroundBrush = "#15D83B01";
                    TelegramStatusBorderBrush = "#40D83B01";
                    TelegramStatusShortText = "Pair Required";
                    TelegramStatusTooltip = $"Pairing required. Code: {service.PairingCode}. Click to pair.";
                    break;
                case TelegramBotConnectionStatus.Disconnected:
                default:
                    TelegramStatusDotBrush = "#E81123";
                    TelegramStatusBackgroundBrush = "#15E81123";
                    TelegramStatusBorderBrush = "#40E81123";
                    TelegramStatusShortText = "Offline";
                    TelegramStatusTooltip = "Disconnected from Cloudflare / Telegram Bot service. Click to check settings.";
                    break;
            }
        }
    }
}

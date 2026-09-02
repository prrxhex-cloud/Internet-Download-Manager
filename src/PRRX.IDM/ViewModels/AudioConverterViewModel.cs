using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PRRX.IDM.Models;
using PRRX.IDM.Services;

namespace PRRX.IDM.ViewModels
{
    public class AudioTargetFormat
    {
        public string Key { get; set; } = "mp3";
        public string DisplayName { get; set; } = "MP3 (MPEG-3)";
        public string Tag { get; set; } = "Universal";
        public string Description { get; set; } = "Compatible with every media player and device";
        public string IconSymbol { get; set; } = "MusicNote224";
        public string BadgeColor { get; set; } = "#0078D4";
    }

    public class AudioQualityOption
    {
        public string Bitrate { get; set; } = "320k";
        public string Label { get; set; } = "320 kbps (Studio Quality)";
        public string Description { get; set; } = "Maximum audio fidelity and dynamic range";
        public string EstimatedSize { get; set; } = "~ 9.6 MB";
        
        public string DisplayText => $"{Label} • {EstimatedSize}";
    }

    public class AudioConverterViewModel : ViewModelBase
    {
        private readonly IMediaEngineService _mediaEngine;
        private readonly IConfigurationService _configService;

        private string _sourceInput = string.Empty;
        private string _detectedSourceFormat = "No file selected";
        private bool _isSourceDetected;
        private bool _isConverting;
        private bool _hasError;
        private double _conversionProgress;
        private string _statusMessage = "Ready. Select any audio/video file or paste an online link to convert.";
        
        private AudioTargetFormat _selectedTargetFormat;
        private AudioQualityOption _selectedQuality = new();
        private bool _trimRingtone = true;
        private CancellationTokenSource? _conversionCts;

        public string SourceInput
        {
            get => _sourceInput;
            set
            {
                if (SetProperty(ref _sourceInput, value))
                {
                    HasError = false;
                    DetectSourceFormat(value);
                }
            }
        }

        public string DetectedSourceFormat
        {
            get => _detectedSourceFormat;
            set => SetProperty(ref _detectedSourceFormat, value);
        }

        public bool IsSourceDetected
        {
            get => _isSourceDetected;
            set => SetProperty(ref _isSourceDetected, value);
        }

        public bool IsConverting
        {
            get => _isConverting;
            set => SetProperty(ref _isConverting, value);
        }

        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        public double ConversionProgress
        {
            get => _conversionProgress;
            set => SetProperty(ref _conversionProgress, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public AudioTargetFormat SelectedTargetFormat
        {
            get => _selectedTargetFormat;
            set
            {
                if (SetProperty(ref _selectedTargetFormat, value) && value != null)
                {
                    UpdateQualityOptionsForFormat(value.Key);
                }
            }
        }

        public AudioQualityOption SelectedQuality
        {
            get => _selectedQuality;
            set => SetProperty(ref _selectedQuality, value);
        }

        public bool TrimRingtone
        {
            get => _trimRingtone;
            set => SetProperty(ref _trimRingtone, value);
        }

        public ObservableCollection<AudioTargetFormat> TargetFormats { get; } = new();
        public ObservableCollection<AudioQualityOption> QualityOptions { get; } = new();
        public ObservableCollection<DownloadItem> ConvertedHistory { get; } = new();

        public ICommand PasteClipboardCommand { get; }
        public ICommand BrowseLocalFileCommand { get; }
        public ICommand StartConversionCommand { get; }
        public ICommand CancelConversionCommand { get; }
        public ICommand OpenOutputFolderCommand { get; }
        public ICommand PlayConvertedFileCommand { get; }
        public ICommand ShowInFolderCommand { get; }

        public AudioConverterViewModel(IMediaEngineService mediaEngine, IConfigurationService configService)
        {
            _mediaEngine = mediaEngine;
            _configService = configService;

            // Initialize comprehensive audio target formats
            TargetFormats.Add(new AudioTargetFormat { Key = "mp3", DisplayName = "MP3 (MPEG-3)", Tag = "Universal", Description = "Most popular, universally supported audio format", IconSymbol = "MusicNote224", BadgeColor = "#0078D4" });
            TargetFormats.Add(new AudioTargetFormat { Key = "wav", DisplayName = "WAV (Lossless PCM)", Tag = "Lossless", Description = "Uncompressed studio master quality audio", IconSymbol = "SoundWaveCircle24", BadgeColor = "#107C41" });
            TargetFormats.Add(new AudioTargetFormat { Key = "m4r", DisplayName = "M4R (iPhone Ringtone)", Tag = "Apple iOS", Description = "Standard iOS custom ringtone format", IconSymbol = "Phone24", BadgeColor = "#D83B01" });
            TargetFormats.Add(new AudioTargetFormat { Key = "m4a", DisplayName = "M4A (Apple AAC)", Tag = "Apple / High-Def", Description = "High-efficiency AAC audio for iTunes/iPhone", IconSymbol = "Speaker224", BadgeColor = "#8764B8" });
            TargetFormats.Add(new AudioTargetFormat { Key = "flac", DisplayName = "FLAC (Audiophile Lossless)", Tag = "Hi-Res Master", Description = "Lossless compressed audio for audiophile fidelity", IconSymbol = "Sparkle24", BadgeColor = "#008272" });
            TargetFormats.Add(new AudioTargetFormat { Key = "ogg", DisplayName = "OGG (Vorbis)", Tag = "Open Source", Description = "High-quality streaming format for web and games", IconSymbol = "Globe24", BadgeColor = "#E3008C" });
            TargetFormats.Add(new AudioTargetFormat { Key = "mp2", DisplayName = "MP2 (Broadcast Layer II)", Tag = "Broadcast", Description = "MPEG Audio Layer II for radio & broadcast systems", IconSymbol = "Radio24", BadgeColor = "#B4009E" });
            TargetFormats.Add(new AudioTargetFormat { Key = "amr", DisplayName = "AMR (Voice Recording)", Tag = "Voice", Description = "Ultra-compact format optimized for speech", IconSymbol = "Mic24", BadgeColor = "#744DA9" });
            TargetFormats.Add(new AudioTargetFormat { Key = "aac", DisplayName = "AAC (Raw Audio)", Tag = "Standard AAC", Description = "Advanced audio coding stream", IconSymbol = "Headphones24", BadgeColor = "#004E8C" });
            TargetFormats.Add(new AudioTargetFormat { Key = "opus", DisplayName = "OPUS (Ultra-Low Latency)", Tag = "Modern Voice", Description = "State-of-the-art interactive speech and music", IconSymbol = "Flash24", BadgeColor = "#FF8C00" });

            _selectedTargetFormat = TargetFormats[0];
            UpdateQualityOptionsForFormat("mp3");

            PasteClipboardCommand = new RelayCommand(() =>
            {
                if (Clipboard.ContainsText())
                {
                    SourceInput = Clipboard.GetText().Trim();
                }
            });

            BrowseLocalFileCommand = new RelayCommand(() =>
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Select Any Audio or Video File to Convert",
                    Filter = "All Supported Media (*.mp3;*.wav;*.m4a;*.flac;*.ogg;*.mp4;*.mkv;*.mov;*.webm;*.avi;*.flv;*.aac;*.wma;*.amr;*.opus)|*.mp3;*.wav;*.m4a;*.flac;*.ogg;*.mp4;*.mkv;*.mov;*.webm;*.avi;*.flv;*.aac;*.wma;*.amr;*.opus|Audio Files (*.mp3;*.wav;*.m4a;*.flac;*.ogg;*.aac;*.wma;*.amr;*.opus)|*.mp3;*.wav;*.m4a;*.flac;*.ogg;*.aac;*.wma;*.amr;*.opus|Video Files (*.mp4;*.mkv;*.mov;*.webm;*.avi;*.flv)|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.flv|All Files (*.*)|*.*"
                };

                if (dialog.ShowDialog() == true)
                {
                    SourceInput = dialog.FileName;
                }
            });

            StartConversionCommand = new AsyncRelayCommand(StartConversionAsync, () => !IsConverting && !string.IsNullOrWhiteSpace(SourceInput));
            CancelConversionCommand = new RelayCommand(CancelConversion, () => IsConverting);
            OpenOutputFolderCommand = new RelayCommand(OpenOutputFolder);

            PlayConvertedFileCommand = new RelayCommand(param =>
            {
                if (param is DownloadItem item && !string.IsNullOrWhiteSpace(item.TargetFilePath) && File.Exists(item.TargetFilePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = item.TargetFilePath, UseShellExecute = true });
                    }
                    catch { }
                }
            });

            ShowInFolderCommand = new RelayCommand(param =>
            {
                if (param is DownloadItem item && !string.IsNullOrWhiteSpace(item.TargetFilePath) && File.Exists(item.TargetFilePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{item.TargetFilePath}\"", UseShellExecute = true });
                    }
                    catch { }
                }
            });
        }

        private void DetectSourceFormat(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                DetectedSourceFormat = "No file selected";
                IsSourceDetected = false;
                return;
            }

            if (File.Exists(input))
            {
                var ext = Path.GetExtension(input).ToLowerInvariant();
                var sizeMb = new FileInfo(input).Length / (1024.0 * 1024.0);
                
                DetectedSourceFormat = ext switch
                {
                    ".mp3" => $"MP3 Audio File • {sizeMb:F1} MB",
                    ".wav" => $"WAV Lossless PCM Audio • {sizeMb:F1} MB",
                    ".flac" => $"FLAC High-Res Audio • {sizeMb:F1} MB",
                    ".m4a" => $"M4A Apple Audio • {sizeMb:F1} MB",
                    ".m4r" => $"M4R iPhone Ringtone • {sizeMb:F1} MB",
                    ".ogg" => $"OGG Vorbis Audio • {sizeMb:F1} MB",
                    ".mp4" => $"MP4 Video Container • {sizeMb:F1} MB",
                    ".mkv" => $"MKV Matroska Video • {sizeMb:F1} MB",
                    ".webm" => $"WebM Media Stream • {sizeMb:F1} MB",
                    ".mov" => $"QuickTime MOV Video • {sizeMb:F1} MB",
                    ".amr" => $"AMR Voice Recording • {sizeMb:F1} MB",
                    ".mp2" => $"MP2 Broadcast Audio • {sizeMb:F1} MB",
                    _ => $"{ext.ToUpperInvariant().TrimStart('.')} Media File • {sizeMb:F1} MB"
                };
                IsSourceDetected = true;
            }
            else if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                DetectedSourceFormat = "Online Media Stream (YouTube, TikTok, Vimeo, SoundCloud)";
                IsSourceDetected = true;
            }
            else
            {
                DetectedSourceFormat = "Unrecognized path format";
                IsSourceDetected = false;
            }
        }

        private void UpdateQualityOptionsForFormat(string formatKey)
        {
            QualityOptions.Clear();

            switch (formatKey.ToLowerInvariant())
            {
                case "wav":
                case "flac":
                    QualityOptions.Add(new AudioQualityOption { Bitrate = "lossless", Label = "Lossless Master (16-bit / 44.1 kHz)", Description = "Bit-perfect uncompressed PCM stream", EstimatedSize = "~ 35 MB" });
                    QualityOptions.Add(new AudioQualityOption { Bitrate = "hi-res", Label = "Studio Master (24-bit / 48 kHz)", Description = "Audiophile grade dynamic recording", EstimatedSize = "~ 55 MB" });
                    break;

                case "m4r":
                    QualityOptions.Add(new AudioQualityOption { Bitrate = "256k", Label = "iPhone 30s Ringtone (256 kbps AAC)", Description = "Optimized for Apple iOS ringtone standards", EstimatedSize = "~ 1.2 MB" });
                    QualityOptions.Add(new AudioQualityOption { Bitrate = "320k", Label = "iPhone Max Quality (320 kbps AAC)", Description = "Maximum clarity for iOS alerts and ringtones", EstimatedSize = "~ 1.5 MB" });
                    break;

                case "amr":
                    QualityOptions.Add(new AudioQualityOption { Bitrate = "12.2k", Label = "AMR-NB High (12.2 kbps, 8kHz Mono)", Description = "Speech codec for cellular & voice logs", EstimatedSize = "~ 350 KB" });
                    QualityOptions.Add(new AudioQualityOption { Bitrate = "7.4k", Label = "AMR-NB Standard (7.4 kbps)", Description = "Ultra-compact voice archive", EstimatedSize = "~ 200 KB" });
                    break;

                case "mp2":
                    QualityOptions.Add(new AudioQualityOption { Bitrate = "384k", Label = "384 kbps (Studio Broadcast)", Description = "DAB / DVB Broadcast standard", EstimatedSize = "~ 11.5 MB" });
                    QualityOptions.Add(new AudioQualityOption { Bitrate = "256k", Label = "256 kbps (Radio Standard)", Description = "High-clarity broadcast standard", EstimatedSize = "~ 7.7 MB" });
                    QualityOptions.Add(new AudioQualityOption { Bitrate = "192k", Label = "192 kbps (Standard Layer II)", Description = "Standard MPEG-1 Layer II stream", EstimatedSize = "~ 5.8 MB" });
                    break;

                default: // mp3, m4a, ogg, aac, opus
                    QualityOptions.Add(new AudioQualityOption { Bitrate = "320k", Label = "320 kbps (Studio / Extreme)", Description = "Maximum audio fidelity and full dynamic range", EstimatedSize = "~ 9.6 MB" });
                    QualityOptions.Add(new AudioQualityOption { Bitrate = "256k", Label = "256 kbps (High Quality)", Description = "Excellent dynamic balance and rich bass", EstimatedSize = "~ 7.7 MB" });
                    QualityOptions.Add(new AudioQualityOption { Bitrate = "192k", Label = "192 kbps (Standard CD)", Description = "Standard audio fidelity for portable devices", EstimatedSize = "~ 5.8 MB" });
                    QualityOptions.Add(new AudioQualityOption { Bitrate = "128k", Label = "128 kbps (Compact)", Description = "Smaller file size, ideal for voice & podcasts", EstimatedSize = "~ 3.8 MB" });
                    break;
            }

            if (QualityOptions.Count > 0)
            {
                SelectedQuality = QualityOptions[0];
            }
        }

        private async Task StartConversionAsync()
        {
            if (string.IsNullOrWhiteSpace(SourceInput))
            {
                StatusMessage = "Please select a local media file or paste an online video link.";
                HasError = true;
                return;
            }

            try
            {
                IsConverting = true;
                HasError = false;
                ConversionProgress = 0;
                var targetExt = SelectedTargetFormat?.Key ?? "mp3";
                StatusMessage = $"Converting media to {targetExt.ToUpperInvariant()} format...";
                _conversionCts = new CancellationTokenSource();

                var progressReporter = new Progress<DownloadProgressReport>(report =>
                {
                    ConversionProgress = report.Percentage;
                    StatusMessage = report.StatusMessage;
                    if (report.IsError) HasError = true;
                });

                var outputFolder = _configService.CurrentConfig.DownloadDirectory;
                var quality = SelectedQuality?.Bitrate ?? "320k";

                var success = await _mediaEngine.ConvertAudioAsync(
                    SourceInput,
                    targetExt,
                    quality,
                    outputFolder,
                    TrimRingtone,
                    progressReporter,
                    _conversionCts.Token);

                if (success)
                {
                    ConversionProgress = 100;
                    StatusMessage = $"Converted and saved successfully as {targetExt.ToUpperInvariant()}!";
                    HasError = false;

                    var title = File.Exists(SourceInput) 
                        ? Path.GetFileNameWithoutExtension(SourceInput) 
                        : $"Audio_{DateTime.Now:yyyyMMdd_HHmmss}";

                    var targetFilePath = Path.Combine(outputFolder, $"{title}.{targetExt}");

                    ConvertedHistory.Insert(0, new DownloadItem
                    {
                        Url = SourceInput,
                        Title = title,
                        TargetFilePath = targetFilePath,
                        SelectedQuality = $"{targetExt.ToUpperInvariant()} • {SelectedQuality?.Label ?? quality}",
                        Type = MediaType.Audio,
                        Status = DownloadStatus.Completed,
                        FormattedTime = DateTime.Now.ToString("MMM dd, h:mm tt"),
                        CompletedAt = DateTime.Now
                    });
                }
                else
                {
                    if (!HasError)
                    {
                        HasError = true;
                        StatusMessage = "Conversion failed. Please verify that the file/link is valid and accessible.";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Conversion cancelled by user.";
            }
            catch (Exception ex)
            {
                HasError = true;
                StatusMessage = $"Conversion error: {ex.Message}";
            }
            finally
            {
                IsConverting = false;
            }
        }

        private void CancelConversion()
        {
            _conversionCts?.Cancel();
        }

        private void OpenOutputFolder()
        {
            var folder = _configService.CurrentConfig.DownloadDirectory;
            if (Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }
    }
}

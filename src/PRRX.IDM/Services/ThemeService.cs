using System;
using System.Windows;
using System.Windows.Media;
using PRRX.IDM.Models;
using PRRX.IDM.Native;
using Wpf.Ui.Appearance;

namespace PRRX.IDM.Services
{
    public interface IThemeService
    {
        AppThemeMode CurrentThemeMode { get; }
        double CurrentTransparencyFactor { get; }
        bool IsWindows11 { get; }
        void ApplyTheme(AppThemeMode themeMode, Window? window = null);
        void SetTransparency(double factor, Window? window = null);
    }

    public class ThemeService : IThemeService
    {
        private readonly IConfigurationService _configService;
        public AppThemeMode CurrentThemeMode => _configService.CurrentConfig.ThemeMode;
        public double CurrentTransparencyFactor => _configService.CurrentConfig.TransparencyFactor;
        public bool IsWindows11 => DwmApi.IsWindows11();

        public ThemeService(IConfigurationService configService)
        {
            _configService = configService;
        }

        public void ApplyTheme(AppThemeMode themeMode, Window? window = null)
        {
            _configService.CurrentConfig.ThemeMode = themeMode;

            bool isDark = false;
            switch (themeMode)
            {
                case AppThemeMode.Light:
                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    isDark = false;
                    break;
                case AppThemeMode.Dark:
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    isDark = true;
                    break;
                case AppThemeMode.System:
                default:
                    var sysTheme = ApplicationThemeManager.GetSystemTheme();
                    ApplicationThemeManager.ApplySystemTheme();
                    isDark = sysTheme == SystemTheme.Dark;
                    break;
            }

            var targetWindow = window ?? Application.Current?.MainWindow;
            if (targetWindow != null)
            {
                DwmApi.ApplyDarkMode(targetWindow, isDark);

                if (IsWindows11)
                {
                    DwmApi.ApplySystemBackdrop(targetWindow, DwmApi.BackdropType.MainWindow);
                }

                SetTransparency(_configService.CurrentConfig.TransparencyFactor, targetWindow);
            }

            _configService.SaveConfig();
        }

        public void SetTransparency(double factor, Window? window = null)
        {
            var clamped = Math.Clamp(factor, 0.20, 1.0);
            _configService.CurrentConfig.TransparencyFactor = clamped;

            var targetWindow = window ?? Application.Current?.MainWindow;
            if (targetWindow != null)
            {
                if (IsWindows11 && _configService.CurrentConfig.EnableTransparency)
                {
                    // Dynamic alpha background color for Mica/Acrylic blending
                    byte alpha = (byte)(clamped * 255);
                    bool isDark = CurrentThemeMode == AppThemeMode.Dark || 
                                  (CurrentThemeMode == AppThemeMode.System && ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark);

                    var baseColor = isDark ? Color.FromArgb(alpha, 24, 24, 28) : Color.FromArgb(alpha, 250, 250, 252);
                    targetWindow.Background = new SolidColorBrush(baseColor);
                }
                else
                {
                    targetWindow.ClearValue(Window.BackgroundProperty);
                }
            }

            _configService.SaveConfig();
        }
    }
}

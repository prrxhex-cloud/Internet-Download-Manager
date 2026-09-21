// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PRRX.IDM.Models;

namespace PRRX.IDM.Services
{
    public interface ISystemTrayService : IDisposable
    {
        bool IsVisible { get; }
        void Initialize(Window mainWindow, IConfigurationService? configService = null);
        void ShowTrayIcon();
        void HideTrayIcon();
        void ShowBalloonNotification(string title, string text, int timeoutMs = 3000);
        void RestoreMainWindow();
    }

    public class SystemTrayService : ISystemTrayService
    {
        public static SystemTrayService? Current { get; private set; }

        private const int TrayIconId = 1001;
        private const int WM_USER = 0x0400;
        private const int WM_TRAYICON = WM_USER + 101;

        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;
        private const int NIM_SETVERSION = 0x00000004;

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;
        private const int NIF_INFO = 0x00000010;

        private const int NIIF_INFO = 0x00000001;

        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;

        private const uint MF_STRING = 0x0000;
        private const uint MF_SEPARATOR = 0x0800;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint TPM_RETURNCMD = 0x0100;

        private const int CMD_OPEN = 2001;
        private const int CMD_SETTINGS = 2002;
        private const int CMD_EXIT = 2003;

        private Window? _mainWindow;
        private IConfigurationService? _configService;
        private IntPtr _hwnd = IntPtr.Zero;
        private IntPtr _hIcon = IntPtr.Zero;
        private bool _isDisposed;
        private bool _iconAdded;

        public bool IsVisible => _iconAdded;

        public SystemTrayService()
        {
            Current = this;
        }

        public void Initialize(Window mainWindow, IConfigurationService? configService = null)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _configService = configService;

            var helper = new WindowInteropHelper(_mainWindow);
            _hwnd = helper.EnsureHandle();

            var source = HwndSource.FromHwnd(_hwnd);
            source?.AddHook(WndProc);

            // Extract application main icon
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    ExtractIconEx(exePath, 0, out _, out _hIcon, 1);
                }

                if (_hIcon == IntPtr.Zero)
                {
                    _hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION
                }
            }
            catch
            {
                _hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512);
            }

            ShowTrayIcon();
        }

        public void ShowTrayIcon()
        {
            if (_hwnd == IntPtr.Zero || _hIcon == IntPtr.Zero) return;

            var nid = CreateNotifyIconData();
            nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            nid.szTip = "PRRX Internet Download Manager v1.7.0";

            if (!_iconAdded)
            {
                _iconAdded = Shell_NotifyIcon(NIM_ADD, ref nid);
            }
            else
            {
                Shell_NotifyIcon(NIM_MODIFY, ref nid);
            }
        }

        public void HideTrayIcon()
        {
            if (!_iconAdded || _hwnd == IntPtr.Zero) return;

            var nid = CreateNotifyIconData();
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _iconAdded = false;
        }

        public void ShowBalloonNotification(string title, string text, int timeoutMs = 3000)
        {
            if (_hwnd == IntPtr.Zero || _hIcon == IntPtr.Zero) return;
            if (!_iconAdded) ShowTrayIcon();

            var nid = CreateNotifyIconData();
            nid.uFlags = NIF_INFO;
            nid.szInfoTitle = title.Length > 63 ? title.Substring(0, 60) + "..." : title;
            nid.szInfo = text.Length > 255 ? text.Substring(0, 252) + "..." : text;
            nid.dwInfoFlags = NIIF_INFO;
            nid.uTimeoutOrVersion = timeoutMs;

            Shell_NotifyIcon(NIM_MODIFY, ref nid);
        }

        public void RestoreMainWindow()
        {
            if (_mainWindow == null) return;

            _mainWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_mainWindow.IsVisible)
                {
                    _mainWindow.Show();
                }

                if (_mainWindow.WindowState == WindowState.Minimized)
                {
                    _mainWindow.WindowState = WindowState.Normal;
                }

                _mainWindow.Activate();
                _mainWindow.Focus();

                try
                {
                    SetForegroundWindow(_hwnd);
                }
                catch { }
            }));
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAYICON)
            {
                int eventId = lParam.ToInt32();
                if (eventId == WM_LBUTTONDBLCLK || eventId == WM_LBUTTONUP)
                {
                    RestoreMainWindow();
                    handled = true;
                }
                else if (eventId == WM_RBUTTONUP)
                {
                    ShowContextMenu();
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        private void ShowContextMenu()
        {
            if (_hwnd == IntPtr.Zero) return;

            GetCursorPos(out POINT pt);
            IntPtr hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) return;

            try
            {
                AppendMenuW(hMenu, MF_STRING, (uint)CMD_OPEN, "Open PRRX IDM");
                AppendMenuW(hMenu, MF_STRING, (uint)CMD_SETTINGS, "Telegram Bot Status (@PRRX_IDM_Bot)");
                AppendMenuW(hMenu, MF_SEPARATOR, 0, string.Empty);
                AppendMenuW(hMenu, MF_STRING, (uint)CMD_EXIT, "Exit PRRX IDM");

                SetForegroundWindow(_hwnd);
                uint cmd = (uint)TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, _hwnd, IntPtr.Zero);
                PostMessage(_hwnd, 0, IntPtr.Zero, IntPtr.Zero);

                ExecuteMenuCommand(cmd);
            }
            finally
            {
                DestroyMenu(hMenu);
            }
        }

        private void ExecuteMenuCommand(uint cmd)
        {
            switch (cmd)
            {
                case CMD_OPEN:
                    RestoreMainWindow();
                    break;

                case CMD_SETTINGS:
                    RestoreMainWindow();
                    if (_mainWindow is Views.MainWindow mw)
                    {
                        mw.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                // Trigger Settings View in Main Window if accessible
                                if (mw.DataContext is ViewModels.MainViewModel vm)
                                {
                                    vm.NavigateTabCommand?.Execute("Settings");
                                }
                            }
                            catch { }
                        }));
                    }
                    break;

                case CMD_EXIT:
                    ExitApplication();
                    break;
            }
        }

        public bool IsExplicitExitRequested { get; private set; }

        public void ExitApplication()
        {
            IsExplicitExitRequested = true;
            HideTrayIcon();
            _mainWindow?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    Application.Current?.Shutdown();
                }
                catch
                {
                    Environment.Exit(0);
                }
            }));
        }

        private NOTIFYICONDATAW CreateNotifyIconData()
        {
            return new NOTIFYICONDATAW
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _hwnd,
                uID = TrayIconId,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = string.Empty,
                szInfo = string.Empty,
                szInfoTitle = string.Empty
            };
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            HideTrayIcon();

            if (_hIcon != IntPtr.Zero)
            {
                try { DestroyIcon(_hIcon); } catch { }
                _hIcon = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATAW
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATAW lpData);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int ExtractIconEx(string szFileName, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, int nIcons);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lpTPMParams);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    }
}

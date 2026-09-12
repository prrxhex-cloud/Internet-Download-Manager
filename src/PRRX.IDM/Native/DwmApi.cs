// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PRRX.IDM.Native
{
    public static class DwmApi
    {
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        public const int DWMWA_MICA_EFFECT = 1029;

        public enum BackdropType
        {
            Auto = 0,
            None = 1,
            MainWindow = 2, // Mica
            TransientWindow = 3, // Acrylic
            TabbedWindow = 4 // Mica Alt
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int dwAttribute,
            ref int pvAttribute,
            int cbAttribute);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;

        public static bool IsWindows11()
        {
            return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;
        }

        public static bool ApplyDarkMode(Window window, bool isDark)
        {
            try
            {
                var handle = new WindowInteropHelper(window).EnsureHandle();
                int darkMode = isDark ? 1 : 0;
                int result = DwmSetWindowAttribute(
                    handle,
                    DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref darkMode,
                    sizeof(int));
                return result == 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool ApplySystemBackdrop(Window window, BackdropType backdropType)
        {
            if (!IsWindows11()) return false;

            try
            {
                var handle = new WindowInteropHelper(window).EnsureHandle();
                int type = (int)backdropType;
                int result = DwmSetWindowAttribute(
                    handle,
                    DWMWA_SYSTEMBACKDROP_TYPE,
                    ref type,
                    sizeof(int));
                return result == 0;
            }
            catch
            {
                return false;
            }
        }

        public static void DragMoveWindow(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                ReleaseCapture();
                SendMessage(handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }
    }
}

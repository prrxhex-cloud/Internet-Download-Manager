// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PRRX.IDM.Services
{
    public static class MemoryOptimizer
    {
        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        private static DispatcherTimer? _idleTrimTimer;

        /// <summary>
        /// Start automatic periodic background memory trimmer when idle
        /// </summary>
        public static void InitializeAutoTrimmer()
        {
            if (_idleTrimTimer != null) return;

            _idleTrimTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            _idleTrimTimer.Tick += (_, _) =>
            {
                TrimMemory();
            };
            _idleTrimTimer.Start();
        }

        public static void HookWindow(Window window)
        {
            window.Loaded += (_, _) =>
            {
                Task.Delay(400).ContinueWith(_ => TrimMemory());
            };

            window.StateChanged += (_, _) =>
            {
                if (window.WindowState == WindowState.Minimized)
                {
                    TrimMemory();
                }
            };

            window.Deactivated += (_, _) =>
            {
                Task.Delay(500).ContinueWith(_ => TrimMemory());
            };

            window.Closed += (_, _) =>
            {
                TrimMemory();
            };
        }

        /// <summary>
        /// Aggressively collect garbage, compact LOH, and flush unused working set memory to OS
        /// </summary>
        public static void TrimMemory()
        {
            Task.Run(() =>
            {
                try
                {
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

                    var handle = Process.GetCurrentProcess().Handle;
                    EmptyWorkingSet(handle);
                    SetProcessWorkingSetSize(handle, new IntPtr(-1), new IntPtr(-1));
                }
                catch
                {
                    // Silent fallback
                }
            });
        }
    }
}

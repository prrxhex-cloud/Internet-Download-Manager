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
using PRRX.IDM.Models;

namespace PRRX.IDM.Services
{
    public interface ISystemPowerService
    {
        void ExecuteCompletionAction(CompletionAction action, bool forceProcesses = false);
    }

    public class SystemPowerService : ISystemPowerService
    {
        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        public void ExecuteCompletionAction(CompletionAction action, bool forceProcesses = false)
        {
            switch (action)
            {
                case CompletionAction.ExitApplication:
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        Application.Current.Shutdown();
                    });
                    break;

                case CompletionAction.SleepComputer:
                    try
                    {
                        SetSuspendState(false, forceProcesses, false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error putting PC to sleep: {ex.Message}");
                    }
                    break;

                case CompletionAction.ShutdownComputer:
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "shutdown.exe",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        psi.ArgumentList.Add("/s");
                        if (forceProcesses)
                        {
                            psi.ArgumentList.Add("/f");
                        }
                        psi.ArgumentList.Add("/t");
                        psi.ArgumentList.Add("10");

                        Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error shutting down PC: {ex.Message}");
                    }
                    break;

                case CompletionAction.RestartComputer:
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "shutdown.exe",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        psi.ArgumentList.Add("/r");
                        if (forceProcesses)
                        {
                            psi.ArgumentList.Add("/f");
                        }
                        psi.ArgumentList.Add("/t");
                        psi.ArgumentList.Add("10");

                        Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error restarting PC: {ex.Message}");
                    }
                    break;

                case CompletionAction.ShowDialog:
                case CompletionAction.None:
                default:
                    // Nothing or handled in UI
                    break;
            }
        }
    }
}

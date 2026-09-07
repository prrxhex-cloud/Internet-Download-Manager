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
                        var args = forceProcesses ? "/s /f /t 10" : "/s /t 10";
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "shutdown.exe",
                            Arguments = args,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error shutting down PC: {ex.Message}");
                    }
                    break;

                case CompletionAction.RestartComputer:
                    try
                    {
                        var args = forceProcesses ? "/r /f /t 10" : "/r /t 10";
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "shutdown.exe",
                            Arguments = args,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
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

// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PRRX.IDM.Models;

namespace PRRX.IDM.Services
{
    public interface IScheduleService : IDisposable
    {
        void ScheduleGrabber(GrabberProject project, Func<GrabberProject, Task> executeAction);
        void UnscheduleGrabber(string projectId);
        bool IsGrabberScheduled(string projectId);
        DateTime? GetNextRunTime(string projectId);
    }

    public class ScheduleService : IScheduleService
    {
        private class ScheduledGrabberTask
        {
            public GrabberProject Project { get; set; } = null!;
            public Func<GrabberProject, Task> ExecuteAction { get; set; } = null!;
            public DateTime NextRunTime { get; set; }
            public bool IsRunning { get; set; }
        }

        private readonly ConcurrentDictionary<string, ScheduledGrabberTask> _scheduledTasks = new();
        private readonly Timer _timer;
        private bool _disposed;

        public ScheduleService()
        {
            // Evaluate schedule deadlines every 5 seconds
            _timer = new Timer(OnTimerTick, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        public void ScheduleGrabber(GrabberProject project, Func<GrabberProject, Task> executeAction)
        {
            if (project == null || string.IsNullOrWhiteSpace(project.Id)) return;

            DateTime nextRun;
            if (project.ScheduleMode == GrabberScheduleMode.RunOnceAtTime && project.ScheduleStartTime.HasValue)
            {
                nextRun = project.ScheduleStartTime.Value;
            }
            else if (project.ScheduleMode == GrabberScheduleMode.PeriodicEveryNHours && project.PeriodicHours > 0)
            {
                nextRun = project.ScheduleStartTime.HasValue && project.ScheduleStartTime.Value > DateTime.Now
                    ? project.ScheduleStartTime.Value
                    : DateTime.Now.AddHours(project.PeriodicHours);
            }
            else
            {
                // Immediate or default
                nextRun = DateTime.Now;
            }

            var task = new ScheduledGrabberTask
            {
                Project = project,
                ExecuteAction = executeAction,
                NextRunTime = nextRun,
                IsRunning = false
            };

            _scheduledTasks[project.Id] = task;
            project.Status = GrabberStatus.Scheduled;
            project.CurrentActivity = $"Scheduled to run at {nextRun:HH:mm:ss}";
        }

        public void UnscheduleGrabber(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId)) return;
            if (_scheduledTasks.TryRemove(projectId, out var task))
            {
                if (task.Project.Status == GrabberStatus.Scheduled)
                {
                    task.Project.Status = GrabberStatus.Idle;
                    task.Project.CurrentActivity = "Schedule cancelled";
                }
            }
        }

        public bool IsGrabberScheduled(string projectId)
        {
            return !string.IsNullOrWhiteSpace(projectId) && _scheduledTasks.ContainsKey(projectId);
        }

        public DateTime? GetNextRunTime(string projectId)
        {
            if (!string.IsNullOrWhiteSpace(projectId) && _scheduledTasks.TryGetValue(projectId, out var task))
            {
                return task.NextRunTime;
            }
            return null;
        }

        private void OnTimerTick(object? state)
        {
            if (_disposed) return;

            var now = DateTime.Now;
            foreach (var kvp in _scheduledTasks)
            {
                var task = kvp.Value;
                if (task.IsRunning) continue;

                // Check stop time boundary
                if (task.Project.ScheduleStopTime.HasValue && now > task.Project.ScheduleStopTime.Value)
                {
                    UnscheduleGrabber(task.Project.Id);
                    task.Project.Status = GrabberStatus.Completed;
                    task.Project.CurrentActivity = "Schedule reached stop time limit";
                    continue;
                }

                // Check if trigger time has arrived
                if (now >= task.NextRunTime)
                {
                    task.IsRunning = true;
                    task.Project.Status = GrabberStatus.Crawling;
                    task.Project.CurrentActivity = "Triggered by scheduler";

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await task.ExecuteAction(task.Project);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[ScheduleService] Error executing scheduled project {task.Project.ProjectName}: {ex.Message}");
                            task.Project.Status = GrabberStatus.Failed;
                            task.Project.CurrentActivity = $"Schedule error: {ex.Message}";
                        }
                        finally
                        {
                            task.IsRunning = false;
                            if (task.Project.ScheduleMode == GrabberScheduleMode.PeriodicEveryNHours && task.Project.PeriodicHours > 0)
                            {
                                task.NextRunTime = DateTime.Now.AddHours(task.Project.PeriodicHours);
                                task.Project.Status = GrabberStatus.Scheduled;
                                task.Project.CurrentActivity = $"Next run at {task.NextRunTime:HH:mm:ss}";
                            }
                            else
                            {
                                UnscheduleGrabber(task.Project.Id);
                            }
                        }
                    });
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            _scheduledTasks.Clear();
            GC.SuppressFinalize(this);
        }
    }
}

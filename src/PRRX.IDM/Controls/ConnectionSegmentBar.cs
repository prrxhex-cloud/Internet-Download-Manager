// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using PRRX.IDM.Models;

namespace PRRX.IDM.Controls
{
    public class ConnectionSegmentBar : FrameworkElement
    {
        public static readonly DependencyProperty ThreadsProperty = DependencyProperty.Register(
            nameof(Threads),
            typeof(IEnumerable<DownloadConnectionThread>),
            typeof(ConnectionSegmentBar),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnThreadsChanged));

        public static readonly DependencyProperty TotalBytesProperty = DependencyProperty.Register(
            nameof(TotalBytes),
            typeof(long),
            typeof(ConnectionSegmentBar),
            new FrameworkPropertyMetadata(1L, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsCompletedProperty = DependencyProperty.Register(
            nameof(IsCompleted),
            typeof(bool),
            typeof(ConnectionSegmentBar),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public IEnumerable<DownloadConnectionThread>? Threads
        {
            get => (IEnumerable<DownloadConnectionThread>?)GetValue(ThreadsProperty);
            set => SetValue(ThreadsProperty, value);
        }

        public long TotalBytes
        {
            get => (long)GetValue(TotalBytesProperty);
            set => SetValue(TotalBytesProperty, value);
        }

        public bool IsCompleted
        {
            get => (bool)GetValue(IsCompletedProperty);
            set => SetValue(IsCompletedProperty, value);
        }

        private static readonly Brush BackgroundTrackBrush = new SolidColorBrush(Color.FromRgb(14, 18, 26));
        private static readonly Brush SlotBackgroundBrush = new SolidColorBrush(Color.FromArgb(45, 26, 36, 52));
        private static readonly Pen BorderPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 42, 54, 76)), 1);
        private static readonly Pen SlotSeparatorPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 50, 65, 90)), 1);

        private static readonly LinearGradientBrush ActiveChunkBrush;
        private static readonly LinearGradientBrush CompletedChunkBrush;
        private static readonly Brush HeadMarkerBrush = new SolidColorBrush(Color.FromRgb(0, 245, 255));

        static ConnectionSegmentBar()
        {
            ActiveChunkBrush = new LinearGradientBrush(
                Color.FromRgb(0, 140, 255),
                Color.FromRgb(0, 235, 255),
                new Point(0, 0),
                new Point(1, 0));
            ActiveChunkBrush.Freeze();

            CompletedChunkBrush = new LinearGradientBrush(
                Color.FromRgb(0, 195, 140),
                Color.FromRgb(0, 240, 180),
                new Point(0, 0),
                new Point(1, 0));
            CompletedChunkBrush.Freeze();

            BackgroundTrackBrush.Freeze();
            SlotBackgroundBrush.Freeze();
            BorderPen.Freeze();
            SlotSeparatorPen.Freeze();
            HeadMarkerBrush.Freeze();
        }

        private readonly HashSet<DownloadConnectionThread> _hookedThreads = new();
        private bool _renderPending = false;

        public ConnectionSegmentBar()
        {
            ClipToBounds = true;
            MinHeight = 16;
        }

        private void HookThread(DownloadConnectionThread? thread)
        {
            if (thread != null && _hookedThreads.Add(thread))
            {
                thread.PropertyChanged += OnThreadPropertyChanged;
            }
        }

        private void UnhookThread(DownloadConnectionThread? thread)
        {
            if (thread != null && _hookedThreads.Remove(thread))
            {
                thread.PropertyChanged -= OnThreadPropertyChanged;
            }
        }

        private void UnhookAllThreads()
        {
            foreach (var t in _hookedThreads)
            {
                t.PropertyChanged -= OnThreadPropertyChanged;
            }
            _hookedThreads.Clear();
        }

        private static void OnThreadsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ConnectionSegmentBar bar)
            {
                if (e.OldValue is INotifyCollectionChanged oldIncc)
                {
                    oldIncc.CollectionChanged -= bar.OnCollectionChanged;
                }
                bar.UnhookAllThreads();

                if (e.NewValue is INotifyCollectionChanged newIncc)
                {
                    newIncc.CollectionChanged += bar.OnCollectionChanged;
                }
                if (e.NewValue is IEnumerable<DownloadConnectionThread> newThreads)
                {
                    foreach (var item in newThreads)
                    {
                        bar.HookThread(item);
                    }
                }

                bar.RequestRender();
            }
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                UnhookAllThreads();
                if (Threads != null)
                {
                    foreach (var item in Threads)
                    {
                        HookThread(item);
                    }
                }
            }
            else
            {
                if (e.OldItems != null)
                {
                    foreach (DownloadConnectionThread item in e.OldItems)
                    {
                        UnhookThread(item);
                    }
                }
                if (e.NewItems != null)
                {
                    foreach (DownloadConnectionThread item in e.NewItems)
                    {
                        HookThread(item);
                    }
                }
            }

            RequestRender();
        }

        private void OnThreadPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(DownloadConnectionThread.DownloadedBytes) or
                                 nameof(DownloadConnectionThread.ProgressPercentage) or
                                 nameof(DownloadConnectionThread.IsActive) or
                                 nameof(DownloadConnectionThread.StartByte) or
                                 nameof(DownloadConnectionThread.EndByte))
            {
                RequestRender();
            }
        }

        private void RequestRender()
        {
            if (_renderPending) return;
            _renderPending = true;
            Dispatcher.InvokeAsync(() =>
            {
                _renderPending = false;
                InvalidateVisual();
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            // 1. Draw track container
            dc.DrawRoundedRectangle(BackgroundTrackBrush, BorderPen, bounds, 4, 4);

            if (IsCompleted)
            {
                var fullRect = new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2));
                dc.DrawRoundedRectangle(CompletedChunkBrush, null, fullRect, 3, 3);
                return;
            }

            var total = TotalBytes;
            var threads = Threads?.ToList();
            if (threads == null || threads.Count == 0) return;

            // Clip inner progress to track curvature
            var clipRect = new Rect(1, 1, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 2));
            dc.PushClip(new RectangleGeometry(clipRect, 3, 3));

            double width = ActualWidth;
            double height = ActualHeight;

            if (total <= 0 || (threads.Count == 1 && threads[0].EndByte < threads[0].StartByte))
            {
                // Single stream or unknown total size: render active stream proportionally
                var single = threads[0];
                var singleSlot = new Rect(0, 1.0, width, Math.Max(1.0, height - 2));
                dc.DrawRectangle(SlotBackgroundBrush, null, singleSlot);

                if (single.DownloadedBytes > 0)
                {
                    double pct = (single.EndByte >= single.StartByte && single.StartByte >= 0 && single.ProgressPercentage > 0)
                        ? Math.Clamp(single.ProgressPercentage / 100.0, 0.05, 1.0)
                        : (single.IsActive ? 0.35 : 1.0);
                    var chunkRect = new Rect(0, 1.0, width * pct, Math.Max(1.0, height - 2));
                    dc.DrawRectangle(single.IsActive ? ActiveChunkBrush : CompletedChunkBrush, null, chunkRect);
                }
                dc.Pop();
                return;
            }

            // 2. Draw each thread's segment block with visual boundary and progress
            for (int i = 0; i < threads.Count; i++)
            {
                var thread = threads[i];
                if (thread.StartByte < 0 || thread.EndByte < thread.StartByte) continue;

                double startRatio = Math.Clamp((double)thread.StartByte / total, 0.0, 1.0);
                double endRatio = Math.Clamp((double)(thread.EndByte + 1) / total, 0.0, 1.0);

                double xStart = startRatio * width;
                double xEnd = endRatio * width;
                double segmentWidth = Math.Max(1.0, xEnd - xStart);

                // Slot background & thread boundary separator
                var slotRect = new Rect(xStart, 1.0, segmentWidth, Math.Max(1.0, height - 2));
                dc.DrawRectangle(SlotBackgroundBrush, SlotSeparatorPen, slotRect);

                // Downloaded bytes portion within this thread's segment
                long threadTotal = Math.Max(1, thread.EndByte - thread.StartByte + 1);
                double progress = Math.Clamp((double)thread.DownloadedBytes / threadTotal, 0.0, 1.0);

                if (progress > 0)
                {
                    double fillWidth = Math.Min(segmentWidth, Math.Max(1.5, segmentWidth * progress));
                    var chunkRect = new Rect(xStart, 1.0, fillWidth, Math.Max(1.0, height - 2));
                    var brush = (!thread.IsActive || progress >= 1.0) ? CompletedChunkBrush : ActiveChunkBrush;
                    dc.DrawRectangle(brush, null, chunkRect);

                    // Vibrant active head marker
                    if (thread.IsActive && progress < 1.0)
                    {
                        double markerX = Math.Min(xStart + fillWidth - 1.5, xEnd - 2.0);
                        var markerRect = new Rect(Math.Max(xStart, markerX), 1.0, 2.5, Math.Max(1.0, height - 2));
                        dc.DrawRectangle(HeadMarkerBrush, null, markerRect);
                    }
                }
            }

            dc.Pop(); // End clip
        }
    }
}

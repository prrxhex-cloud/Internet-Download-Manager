using System;
using System.Collections.Generic;
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
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TotalBytesProperty = DependencyProperty.Register(
            nameof(TotalBytes),
            typeof(long),
            typeof(ConnectionSegmentBar),
            new FrameworkPropertyMetadata(1L, FrameworkPropertyMetadataOptions.AffectsRender));

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

        private static readonly Brush BackgroundTrackBrush = new SolidColorBrush(Color.FromRgb(15, 20, 28));
        private static readonly Pen BorderPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1);

        private static readonly LinearGradientBrush ActiveChunkBrush;
        private static readonly LinearGradientBrush CompletedChunkBrush;
        private static readonly Brush HeadMarkerBrush = new SolidColorBrush(Color.FromRgb(0, 229, 255));

        static ConnectionSegmentBar()
        {
            ActiveChunkBrush = new LinearGradientBrush(
                Color.FromRgb(0, 162, 255),
                Color.FromRgb(0, 230, 255),
                new Point(0, 0),
                new Point(1, 0));
            ActiveChunkBrush.Freeze();

            CompletedChunkBrush = new LinearGradientBrush(
                Color.FromRgb(30, 130, 230),
                Color.FromRgb(0, 180, 216),
                new Point(0, 0),
                new Point(1, 0));
            CompletedChunkBrush.Freeze();

            BackgroundTrackBrush.Freeze();
            BorderPen.Freeze();
            HeadMarkerBrush.Freeze();
        }

        public ConnectionSegmentBar()
        {
            ClipToBounds = true;
            MinHeight = 16;
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            // 1. Draw sleek track
            dc.DrawRoundedRectangle(BackgroundTrackBrush, BorderPen, bounds, 3, 3);

            var threads = Threads;
            var total = TotalBytes;
            if (threads == null || total <= 0) return;

            // 2. Draw each thread's segment block
            foreach (var thread in threads)
            {
                if (thread.StartByte < 0 || thread.EndByte < thread.StartByte) continue;

                double startRatio = Math.Clamp((double)thread.StartByte / total, 0.0, 1.0);
                double currentRatio = Math.Clamp((double)thread.CurrentByte / total, 0.0, 1.0);

                double xStart = startRatio * ActualWidth;
                double xCurrent = currentRatio * ActualWidth;
                double chunkWidth = Math.Max(2.0, xCurrent - xStart);

                var chunkRect = new Rect(xStart, 1.5, chunkWidth, Math.Max(1.0, ActualHeight - 3));
                var brush = thread.IsActive ? ActiveChunkBrush : CompletedChunkBrush;

                dc.DrawRoundedRectangle(brush, null, chunkRect, 2, 2);

                // Draw active connection head pulse
                if (thread.IsActive && thread.ProgressPercentage < 100.0)
                {
                    var markerRect = new Rect(Math.Max(0, xCurrent - 1.5), 1.0, 3.0, Math.Max(1.0, ActualHeight - 2));
                    dc.DrawRoundedRectangle(HeadMarkerBrush, null, markerRect, 1, 1);
                }
            }
        }
    }
}

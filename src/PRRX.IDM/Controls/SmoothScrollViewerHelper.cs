// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PRRX.IDM.Controls
{
    public static class SmoothScroll
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(SmoothScroll),
                new PropertyMetadata(false, OnIsEnabledChanged));

        private static readonly ConditionalWeakTable<ScrollViewer, SmoothScrollEngine> EngineTable = new();

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
            {
                if ((bool)e.NewValue)
                {
                    if (!EngineTable.TryGetValue(scrollViewer, out _))
                    {
                        var engine = new SmoothScrollEngine(scrollViewer);
                        EngineTable.Add(scrollViewer, engine);
                    }
                }
            }
        }
    }

    /// <summary>
    /// High-performance 120Hz physics-based momentum smooth scroll engine.
    /// Runs directly on CompositionTarget.Rendering for zero-lag silky smooth scrolling.
    /// </summary>
    internal class SmoothScrollEngine
    {
        private readonly ScrollViewer _scrollViewer;
        private double _targetOffset;
        private bool _isRendering;

        public SmoothScrollEngine(ScrollViewer scrollViewer)
        {
            _scrollViewer = scrollViewer;
            _scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Do not intercept if content does not need scrolling
            if (_scrollViewer.ScrollableHeight <= 0) return;

            e.Handled = true;

            // Sensitivity factor: 1.0 (smooth, responsive, modern momentum)
            double delta = -e.Delta * 0.85;

            // Sync target with current if rendering was idle
            if (!_isRendering)
            {
                _targetOffset = _scrollViewer.VerticalOffset;
            }

            // Accumulate target offset with bounds clamping
            _targetOffset = Math.Clamp(_targetOffset + delta, 0, _scrollViewer.ScrollableHeight);

            StartRendering();
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // If user dragged scrollbar thumb directly, sync target
            if (!_isRendering)
            {
                _targetOffset = _scrollViewer.VerticalOffset;
            }
        }

        private void StartRendering()
        {
            if (!_isRendering)
            {
                _isRendering = true;
                CompositionTarget.Rendering += OnCompositionRendering;
            }
        }

        private void StopRendering()
        {
            if (_isRendering)
            {
                CompositionTarget.Rendering -= OnCompositionRendering;
                _isRendering = false;
            }
        }

        private void OnCompositionRendering(object? sender, EventArgs e)
        {
            double current = _scrollViewer.VerticalOffset;
            double diff = _targetOffset - current;

            // Check if we reached the target threshold
            if (Math.Abs(diff) < 0.4 || _scrollViewer.ScrollableHeight <= 0)
            {
                _scrollViewer.ScrollToVerticalOffset(_targetOffset);
                StopRendering();
                return;
            }

            // 120Hz / 60Hz exponential ease-out interpolation step (0.20 factor for silky fluid response)
            double next = current + (diff * 0.20);
            _scrollViewer.ScrollToVerticalOffset(next);
        }
    }
}

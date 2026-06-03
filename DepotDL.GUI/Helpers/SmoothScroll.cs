using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DepotDL.GUI.Models;
using DepotDL.GUI.Services;

namespace DepotDL.GUI.Helpers
{
    public static class SmoothScroll
    {
        private static AppSettings? _cachedSettings;
        private static readonly Dictionary<ScrollViewer, ScrollState> _states = new();

        private class ScrollState
        {
            public double TargetOffset;
            public bool IsAnimating;
        }

        private static AppSettings GetSettings()
        {
            if (_cachedSettings != null) return _cachedSettings;
            try
            {
                return _cachedSettings = new SettingsService().Load();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static void ResetCache()
        {
            _cachedSettings = null;
            _states.Clear();
            _fadeCache.Clear();
            _animTop.Clear();
            _animBot.Clear();
        }

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled", typeof(bool), typeof(SmoothScroll),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScrollViewer sv) return;
            if ((bool)e.NewValue)
            {
                sv.PreviewMouseWheel += OnPreviewMouseWheel;
                sv.PreviewKeyDown += OnPreviewKeyDown;
                sv.Loaded += OnScrollViewerLoaded;
            }
            else
            {
                sv.PreviewMouseWheel -= OnPreviewMouseWheel;
                sv.PreviewKeyDown -= OnPreviewKeyDown;
                sv.Loaded -= OnScrollViewerLoaded;
                sv.ScrollChanged -= OnScrollChanged;
                sv.SizeChanged -= OnScrollViewerSizeChanged;
                sv.RemoveHandler(ScrollBar.ScrollEvent, new ScrollEventHandler(OnScrollBarScrolled));
                _fadeCache.Remove(sv);
                _animTop.Remove(sv);
                _animBot.Remove(sv);
                sv.OpacityMask = null;
            }
        }

        private static void OnScrollViewerLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;
            sv.AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler(OnScrollBarScrolled));
            sv.ScrollChanged += OnScrollChanged;
            sv.SizeChanged += OnScrollViewerSizeChanged;
            UpdateEdgeFade(sv);
        }

        private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;
            UpdateEdgeFade(sv);
        }

        private static void OnScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;
            _fadeCache.Remove(sv);
            UpdateEdgeFade(sv);
        }

        private class FadeCache
        {
            public required DrawingBrush Brush { get; set; }
            public required GradientStop Top { get; set; }
            public required GradientStop Bottom { get; set; }
        }

        private static readonly Dictionary<ScrollViewer, FadeCache> _fadeCache = new();
        private static readonly Dictionary<ScrollViewer, double> _animTop = new();
        private static readonly Dictionary<ScrollViewer, double> _animBot = new();

        private static void EnsureFadeBrush(ScrollViewer sv)
        {
            if (_fadeCache.ContainsKey(sv)) return;

            var topStop = new GradientStop(Colors.Black, 0);
            var midTop = new GradientStop(Colors.Black, 0.12);
            var midBot = new GradientStop(Colors.Black, 0.88);
            var botStop = new GradientStop(Colors.Black, 1);

            var grad = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            grad.GradientStops.Add(topStop);
            grad.GradientStops.Add(midTop);
            grad.GradientStops.Add(midBot);
            grad.GradientStops.Add(botStop);

            var drawing = new DrawingGroup();
            var sbW = SystemParameters.VerticalScrollBarWidth;

            var contentRect = new RectangleGeometry(new Rect(0, 0, Math.Max(0, sv.ActualWidth - sbW), sv.ActualHeight));
            drawing.Children.Add(new GeometryDrawing(grad, null, contentRect));

            var sbRect = new RectangleGeometry(new Rect(Math.Max(0, sv.ActualWidth - sbW), 0, sbW, sv.ActualHeight));
            drawing.Children.Add(new GeometryDrawing(Brushes.Black, null, sbRect));

            var brush = new DrawingBrush(drawing)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };

            _fadeCache[sv] = new FadeCache { Brush = brush, Top = topStop, Bottom = botStop };
            sv.OpacityMask = brush;
        }

        private static void UpdateEdgeFade(ScrollViewer sv)
        {
            if (sv.ScrollableHeight <= 0)
            {
                _animTop.Remove(sv);
                _animBot.Remove(sv);
                if (_fadeCache.Remove(sv))
                    sv.OpacityMask = null;
                return;
            }

            if (sv.ActualWidth <= 0 || sv.ActualHeight <= 0) return;

            double targetTop = Math.Clamp(sv.VerticalOffset / 30.0, 0, 1);
            double targetBot = Math.Clamp((sv.ScrollableHeight - sv.VerticalOffset) / 30.0, 0, 1);

            double curTop = _animTop.GetValueOrDefault(sv, targetTop);
            double curBot = _animBot.GetValueOrDefault(sv, targetBot);

            const double lerp = 0.18;
            curTop += (targetTop - curTop) * lerp;
            curBot += (targetBot - curBot) * lerp;

            if (Math.Abs(curTop - targetTop) < 0.001) curTop = targetTop;
            if (Math.Abs(curBot - targetBot) < 0.001) curBot = targetBot;

            _animTop[sv] = curTop;
            _animBot[sv] = curBot;

            if (curTop <= 0 && curBot <= 0)
            {
                _animTop.Remove(sv);
                _animBot.Remove(sv);
                if (_fadeCache.Remove(sv))
                    sv.OpacityMask = null;
                return;
            }

            EnsureFadeBrush(sv);

            if (_fadeCache.TryGetValue(sv, out var cache))
            {
                cache.Top.Color = Color.FromArgb((byte)((1 - curTop) * 255), 0, 0, 0);
                cache.Bottom.Color = Color.FromArgb((byte)((1 - curBot) * 255), 0, 0, 0);
            }
        }

        private static void OnScrollBarScrolled(object sender, ScrollEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;
            if (_states.TryGetValue(sv, out var state))
                state.TargetOffset = sv.VerticalOffset;
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var sv = (ScrollViewer)sender;
            if (sv.ScrollableHeight <= 0) return;

            var s = GetSettings();
            double delta = e.Key switch
            {
                Key.Up => -80,
                Key.Down => 80,
                Key.PageUp => -sv.ViewportHeight * 0.9,
                Key.PageDown => sv.ViewportHeight * 0.9,
                Key.Home => -sv.ScrollableHeight,
                Key.End => sv.ScrollableHeight,
                _ => 0
            };

            if (delta == 0) return;

            EnsureState(sv).TargetOffset = Math.Clamp(
                EnsureState(sv).TargetOffset + delta, 0, sv.ScrollableHeight);

            if (!EnsureState(sv).IsAnimating)
                _ = AnimateScrollAsync(sv, EnsureState(sv));

            e.Handled = true;
        }

        private static ScrollState EnsureState(ScrollViewer sv)
        {
            if (!_states.TryGetValue(sv, out var state))
            {
                state = new ScrollState { TargetOffset = sv.VerticalOffset };
                _states[sv] = state;
            }
            return state;
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = (ScrollViewer)sender;
            if (sv.ScrollableHeight <= 0) return;

            var s = GetSettings();
            var state = EnsureState(sv);

            state.TargetOffset = Math.Clamp(
                state.TargetOffset - e.Delta * s.ScrollSensitivity,
                0, sv.ScrollableHeight);

            if (!state.IsAnimating)
                _ = AnimateScrollAsync(sv, state);

            e.Handled = true;
        }

        private static async System.Threading.Tasks.Task AnimateScrollAsync(ScrollViewer sv, ScrollState state)
        {
            state.IsAnimating = true;

            try
            {
                var s = GetSettings();

                while (true)
                {
                    double current = sv.VerticalOffset;
                    double diff = state.TargetOffset - current;

                    if (Math.Abs(diff) < 0.5)
                        break;

                    double factor = 1 - Math.Pow(0.05, 16.0 / s.ScrollDurationMs);
                    sv.ScrollToVerticalOffset(current + diff * factor);
                    await System.Threading.Tasks.Task.Delay(16);
                }

                sv.ScrollToVerticalOffset(state.TargetOffset);
            }
            finally
            {
                state.IsAnimating = false;
                _states.Remove(sv);
            }
        }
    }
}

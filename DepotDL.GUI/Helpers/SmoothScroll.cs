using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
                sv.PreviewMouseWheel += OnPreviewMouseWheel;
            else
                sv.PreviewMouseWheel -= OnPreviewMouseWheel;
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = (ScrollViewer)sender;
            if (sv.ScrollableHeight <= 0) return;

            var s = GetSettings();

            if (!_states.TryGetValue(sv, out var state))
            {
                state = new ScrollState { TargetOffset = sv.VerticalOffset };
                _states[sv] = state;
            }

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
                double startOffset = sv.VerticalOffset;
                double startTarget = state.TargetOffset;
                var s = GetSettings();
                var startTime = DateTime.UtcNow;

                while (true)
                {
                    if (state.TargetOffset != startTarget)
                    {
                        startOffset = sv.VerticalOffset;
                        startTarget = state.TargetOffset;
                        startTime = DateTime.UtcNow;
                        s = GetSettings();
                    }

                    double elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    double t = Math.Min(elapsed / s.ScrollDurationMs, 1.0);
                    double eased = 1 - Math.Pow(1 - t, 3);
                    double offset = startOffset + (startTarget - startOffset) * eased;
                    sv.ScrollToVerticalOffset(offset);

                    if (t >= 1.0)
                        break;

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

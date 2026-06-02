using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using DepotDL.GUI.Models;
using DepotDL.GUI.Services;

namespace DepotDL.GUI.Helpers
{
    public static class SmoothScroll
    {
        private static AppSettings? _cachedSettings;

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

        public static void ResetCache() => _cachedSettings = null;
        // Animatable proxy — ScrollViewer.VerticalOffset is read-only, so we drive
        // ScrollToVerticalOffset through this attached property instead.
        public static readonly DependencyProperty VerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "VerticalOffset", typeof(double), typeof(SmoothScroll),
                new UIPropertyMetadata(0.0, OnVerticalOffsetChanged));

        public static double GetVerticalOffset(DependencyObject obj) =>
            (double)obj.GetValue(VerticalOffsetProperty);
        public static void SetVerticalOffset(DependencyObject obj, double value) =>
            obj.SetValue(VerticalOffsetProperty, value);

        private static void OnVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
                sv.ScrollToVerticalOffset((double)e.NewValue);
        }

        // IsEnabled — attach to a ScrollViewer in XAML to opt in.
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

            double current = sv.VerticalOffset;
            var s = GetSettings();
            double target = Math.Clamp(
                current - e.Delta * s.ScrollSensitivity,
                0, sv.ScrollableHeight);

            var anim = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(s.ScrollDurationMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            sv.BeginAnimation(VerticalOffsetProperty, anim);
            e.Handled = true;
        }
    }
}

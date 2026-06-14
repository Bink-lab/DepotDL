using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using DepotDL.GUI.Models;
using DepotDL.GUI.Services;

namespace DepotDL.GUI.Helpers
{
    public class SmoothScroll
    {
        private static AppSettings? _cachedSettings;
        private static AppSettings GetSettings() =>
            _cachedSettings ??= TryLoad();

        private static AppSettings TryLoad()
        {
            try { return new SettingsService().Load(); }
            catch { return new AppSettings(); }
        }

        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<SmoothScroll, ScrollViewer, bool>("IsEnabled");

        public static bool GetIsEnabled(AvaloniaObject obj) => obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(AvaloniaObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        static SmoothScroll()
        {
            IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnIsEnabledChanged);
        }

        private static void OnIsEnabledChanged(ScrollViewer sv, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
                sv.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged,
                    Avalonia.Interactivity.RoutingStrategies.Tunnel);
            else
            {
                sv.RemoveHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);
                if (_active.TryGetValue(sv, out var st))
                {
                    st.Timer.Stop();
                    _active.Remove(sv);
                }
            }
        }

        private record AnimState(DispatcherTimer Timer, double TargetY);
        private static readonly Dictionary<ScrollViewer, AnimState> _active = new();

        private static void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;
            if (sv.ScrollBarMaximum.Y <= 0) return;

            var s = GetSettings();
            var delta = e.Delta.Y * 40 * s.ScrollSensitivity;

            // Chain from current target if animation in progress, otherwise from current offset
            var baseY = _active.TryGetValue(sv, out var existing) ? existing.TargetY : sv.Offset.Y;
            var targetY = Math.Clamp(baseY - delta, 0, sv.ScrollBarMaximum.Y);

            AnimateScrollTo(sv, targetY, TimeSpan.FromMilliseconds(s.ScrollDurationMs));
            e.Handled = true;
        }

        private static void AnimateScrollTo(ScrollViewer sv, double targetY, TimeSpan duration)
        {
            // Cancel existing animation
            if (_active.TryGetValue(sv, out var existing))
                existing.Timer.Stop();

            var startY = sv.Offset.Y;
            var startTime = DateTime.UtcNow;

            DispatcherTimer? timer = null;
            timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Render, (_, _) =>
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                var progress = Math.Min(1.0, elapsed / duration.TotalMilliseconds);
                sv.Offset = sv.Offset.WithY(startY + (targetY - startY) * CubicEaseOut(progress));
                if (progress >= 1.0)
                {
                    timer!.Stop();
                    _active.Remove(sv);
                }
            });

            _active[sv] = new AnimState(timer, targetY);
            timer.Start();
        }

        private static double CubicEaseOut(double t) => 1 - Math.Pow(1 - t, 3);
    }
}

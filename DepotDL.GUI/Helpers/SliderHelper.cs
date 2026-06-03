using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace DepotDL.GUI.Helpers
{
    public static class SliderHelper
    {
        public static readonly DependencyProperty SmoothSnapProperty =
            DependencyProperty.RegisterAttached(
                "SmoothSnap", typeof(bool), typeof(SliderHelper),
                new PropertyMetadata(false, OnSmoothSnapChanged));

        public static bool GetSmoothSnap(DependencyObject obj) => (bool)obj.GetValue(SmoothSnapProperty);
        public static void SetSmoothSnap(DependencyObject obj, bool value) => obj.SetValue(SmoothSnapProperty, value);

        private class Handlers
        {
            public required DragCompletedEventHandler DragCompleted { get; set; }
            public required DragStartedEventHandler DragStarted { get; set; }
        }

        private static readonly Dictionary<Slider, Handlers> _handlerCache = new();

        private static Handlers GetHandlers(Slider slider)
        {
            if (!_handlerCache.TryGetValue(slider, out var h))
            {
                h = new Handlers
                {
                    DragCompleted = new DragCompletedEventHandler(OnDragCompleted),
                    DragStarted = new DragStartedEventHandler(OnDragStarted)
                };
                _handlerCache[slider] = h;
            }
            return h;
        }

        private static void RemoveHandlers(Slider slider)
        {
            if (_handlerCache.TryGetValue(slider, out var h))
            {
                slider.RemoveHandler(Thumb.DragCompletedEvent, h.DragCompleted);
                slider.RemoveHandler(Thumb.DragStartedEvent, h.DragStarted);
                _handlerCache.Remove(slider);
            }
        }

        private static void OnSmoothSnapChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Slider slider) return;
            if ((bool)e.NewValue)
            {
                slider.Loaded += OnSliderLoaded;
                slider.Unloaded += OnSliderUnloaded;
            }
            else
            {
                slider.Loaded -= OnSliderLoaded;
                slider.Unloaded -= OnSliderUnloaded;
                slider.BeginAnimation(Slider.ValueProperty, null);
                RemoveHandlers(slider);
            }
        }

        private static void OnSliderLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Slider slider) return;
            var h = GetHandlers(slider);
            slider.AddHandler(Thumb.DragCompletedEvent, h.DragCompleted);
            slider.AddHandler(Thumb.DragStartedEvent, h.DragStarted);
            slider.PreviewKeyDown += OnSliderPreviewKeyDown;
        }

        private static void OnSliderUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Slider slider) return;
            RemoveHandlers(slider);
            slider.PreviewKeyDown -= OnSliderPreviewKeyDown;
            slider.BeginAnimation(Slider.ValueProperty, null);
        }

        private static void OnDragStarted(object sender, DragStartedEventArgs e)
        {
            if (sender is not Slider slider) return;
            slider.BeginAnimation(Slider.ValueProperty, null);
        }

        private static void OnSliderPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not Slider slider) return;
            slider.BeginAnimation(Slider.ValueProperty, null);
        }

        private static void OnDragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (sender is not Slider slider) return;
            if (e.Canceled) return;

            // Snap to nearest tick only if TickFrequency is set
            double tick = slider.TickFrequency > 0
                ? Math.Round(slider.Value / slider.TickFrequency) * slider.TickFrequency
                : slider.Value;
            tick = Math.Clamp(tick, slider.Minimum, slider.Maximum);

            if (Math.Abs(tick - slider.Value) < 0.001) return;

            var anim = new DoubleAnimation(tick, TimeSpan.FromMilliseconds(120));
            anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            anim.Completed += (_, _) =>
            {
                slider.BeginAnimation(Slider.ValueProperty, null);
                slider.Value = tick;
            };
            slider.BeginAnimation(Slider.ValueProperty, anim);
        }
    }
}

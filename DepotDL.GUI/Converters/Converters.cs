// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DepotDL.GUI.Models;

namespace DepotDL.GUI.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            var b = v is bool bv && bv;
            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => v is Visibility vis && vis == Visibility.Visible;
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is bool b && !b;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => v is bool b && !b;
    }

    public class DepotStatusToColorConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            if (v is DepotStatus s)
            {
                return s switch
                {
                    DepotStatus.Done => new SolidColorBrush(Color.FromRgb(92, 139, 92)),
                    DepotStatus.Failed => new SolidColorBrush(Color.FromRgb(192, 57, 43)),
                    DepotStatus.Downloading => new SolidColorBrush(Color.FromRgb(200, 151, 90)),
                    DepotStatus.Validating => new SolidColorBrush(Color.FromRgb(107, 93, 79)),
                    DepotStatus.Cancelled => new SolidColorBrush(Color.FromRgb(160, 144, 128)),
                    DepotStatus.Skipped => new SolidColorBrush(Color.FromRgb(160, 144, 128)),
                    _ => new SolidColorBrush(Color.FromRgb(160, 144, 128))
                };
            }
            return new SolidColorBrush(Color.FromRgb(160, 144, 128));
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
    }

    public class DepotStatusToTextConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            if (v is DepotStatus s)
            {
                return s switch
                {
                    DepotStatus.Idle => "Idle",
                    DepotStatus.Queued => "Queued",
                    DepotStatus.Connecting => "Connecting",
                    DepotStatus.PreAllocating => "Pre-Allocating",
                    DepotStatus.Downloading => "Downloading",
                    DepotStatus.Validating => "Validating",
                    DepotStatus.Done => "Complete",
                    DepotStatus.Failed => "Failed",
                    DepotStatus.Cancelled => "Cancelled",
                    DepotStatus.Skipped => "Skipped",
                    _ => "Unknown"
                };
            }
            return "Unknown";
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
    }

    public class PercentToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type t, object p, CultureInfo c)
        {
            if (values.Length >= 2 &&
                values[0] is double pct &&
                values[1] is double totalWidth)
            {
                return Math.Max(0, Math.Min(totalWidth, totalWidth * pct / 100.0));
            }
            return 0.0;
        }
        public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c)
            => Array.Empty<object>();
    }

    public class StringEmptyToVisibilityConverter : IValueConverter
    {
        public bool ShowWhenEmpty { get; set; }
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            var empty = string.IsNullOrWhiteSpace(v as string);
            var show = ShowWhenEmpty ? empty : !empty;
            return show ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
    }

    public class EqualityToVisibilityConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            var equal = Equals(v, p) || (v != null && v.ToString() == p?.ToString());
            return equal ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
    }

    public class EqualityToBoolConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
            => Equals(v, p) || (v != null && v.ToString() == p?.ToString());
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => DependencyProperty.UnsetValue;
    }
}

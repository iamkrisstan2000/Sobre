using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Sobre.Models;

namespace Sobre.Converters
{
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool b = value is bool v && v;
            if (Invert) b = !b;
            return b ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => value is Visibility vis && (vis == Visibility.Visible) != Invert;
    }

    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) => v is bool b && !b;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => v is bool b && !b;
    }

    public sealed class NullToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool has = value != null;
            if (value is string s) has = !string.IsNullOrWhiteSpace(s);
            if (Invert) has = !has;
            return has ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    public sealed class CountToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            int n = value is int i ? i : 0;
            bool show = n > 0;
            if (Invert) show = !show;
            return show ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Unread badge: returns Collapsed when 0, "99+" beyond 99.</summary>
    public sealed class UnreadBadgeConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            int n = value is int i ? i : 0;
            if (n <= 0) return string.Empty;
            return n > 99 ? "99+" : n.ToString(c);
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    public sealed class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    public sealed class AccountColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            if (value is string hex)
            {
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
                catch { }
            }
            return new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Picks FontWeight SemiBold for unread messages.</summary>
    public sealed class UnreadWeightConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => value is bool b && b ? FontWeights.SemiBold : FontWeights.Normal;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    /// <summary>Picks the primary/secondary text brush for unread vs read rows (theme aware).</summary>
    public sealed class UnreadBrushConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
        {
            bool unread = value is bool b && b;
            var key = unread ? "TextPrimary" : "TextSecondary";
            if (System.Windows.Application.Current?.TryFindResource(key) is Brush brush) return brush;
            return unread ? Brushes.Black : Brushes.Gray;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }

    public sealed class FolderKindToIconConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => FolderGlyphs.For(value as FolderKind?);
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => Binding.DoNothing;
    }
}

namespace Sobre.Converters
{
    /// <summary>True when both bindings reference the same object (used for selection styling).</summary>
    public sealed class EqualsRefConverter : IMultiValueConverter
    {
        public object Convert(object[] values, System.Type t, object p, CultureInfo c)
            => values.Length == 2 && values[0] != null && ReferenceEquals(values[0], values[1]);
        public object[] ConvertBack(object v, System.Type[] t, object p, CultureInfo c) => Array.Empty<object>();
    }
}

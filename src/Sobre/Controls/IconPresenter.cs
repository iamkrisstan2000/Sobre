using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Sobre.Controls
{
    /// <summary>
    /// Renders a Fluent System Icon from Themes/Icons.xaml on a 24x24 design grid,
    /// scaled to any pixel size and tinted with any brush.
    /// Set <see cref="Filled"/> for the filled variant, or <see cref="Glyph"/> for a raw path.
    /// </summary>
    public class IconPresenter : FrameworkElement
    {
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(string), typeof(IconPresenter),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnIconChanged));

        public static readonly DependencyProperty SizeProperty =
            DependencyProperty.Register(nameof(Size), typeof(double), typeof(IconPresenter),
                new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ForegroundProperty =
            DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(IconPresenter),
                new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FilledProperty =
            DependencyProperty.Register(nameof(Filled), typeof(bool), typeof(IconPresenter),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnIconChanged));

        private Geometry? _geometry;

        public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
        public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
        public bool Filled { get => (bool)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }

        private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((IconPresenter)d).ResolveGeometry();

        private void ResolveGeometry()
        {
            _geometry = null;
            if (Icon is string key && !string.IsNullOrWhiteSpace(key))
            {
                var lookup = Filled ? "Icon." + key + ".Filled" : "Icon." + key;
                if (TryFindResource(lookup) is Geometry g) _geometry = g;
                else if (TryFindResource("Icon." + key) is Geometry r) _geometry = r;
            }
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

        protected override void OnRender(DrawingContext dc)
        {
            if (_geometry == null) return;
            double s = Size / 24.0;
            dc.PushTransform(new ScaleTransform(s, s));
            dc.DrawGeometry(Foreground, null, _geometry);
            dc.Pop();
        }
    }
}

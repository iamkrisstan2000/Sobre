using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Sobre.Services
{
    public enum AppTheme { System, Light, Dark }

    /// <summary>
    /// Owns every colour token in Sobre. Values are Fluent-2 design tokens
    /// (layer fills, control fills, text fills) tinted by the live Windows accent.
    /// Brushes land in Application.Resources so XAML can bind them with DynamicResource.
    /// </summary>
    public sealed class ThemeService : IDisposable
    {
        public static ThemeService Current { get; } = new ThemeService();

        public event Action? Changed;

        private AppTheme _mode = AppTheme.System;
        private Color _accent = Color.FromRgb(0x00, 0x78, 0xD4);
        private bool _acrylic = true;
        private bool _transparency = true;

        public bool IsDark { get; private set; }
        public bool UseAcrylic => _acrylic;
        public AppTheme Mode => _mode;
        public Color Accent => _accent;

        private ThemeService() { }

        private bool _started;

        public void Start()
        {
            if (_started) { Apply(_mode, force: true); return; }
            _started = true;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged += OnUserPreferenceChanged;
            Apply(_mode, force: true);
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => RefreshSystem();
        private void OnUserPreferenceChanged(object sender, EventArgs e) => RefreshSystem();

        /// <summary>Re-reads accent + light/dark from Windows (used while in System mode).</summary>
        public void RefreshSystem()
        {
            if (_mode != AppTheme.System) return;
            Application.Current?.Dispatcher?.Invoke(() => Apply(_mode, force: true));
        }

        public void SetMode(AppTheme mode) => Apply(mode, force: true);

        public void SetAcrylic(bool on)
        {
            _acrylic = on;
            Apply(_mode, force: true);
        }

        public void Refresh() => Apply(_mode, force: true);

        private void Apply(AppTheme mode, bool force)
        {
            _mode = mode;
            _accent = Native.GetSystemAccentColor();
            _transparency = Native.TransparencyEnabled();

            IsDark = mode switch
            {
                AppTheme.Dark => true,
                AppTheme.Light => false,
                _ => !Native.IsSystemLightTheme()
            };

            BuildTokens();
            Changed?.Invoke();
        }

        // ---------- token construction ----------

        private static Color S(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        private static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

        private static Color Blend(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        /// <summary>Lighten (positive) or darken (negative) a colour in HSL space.</summary>
        public static Color Shift(Color c, double amount)
        {
            var (h, s, l) = ToHsl(c);
            l = Math.Clamp(l + amount, 0, 1);
            return FromHsl(h, s, l);
        }

        private static (double h, double s, double l) ToHsl(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double h = 0, l = (max + min) / 2, d = max - min;
            double s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
            if (d != 0)
            {
                if (max == r) h = ((g - b) / d) % 6;
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h *= 60; if (h < 0) h += 360;
            }
            return (h, s, l);
        }

        private static Color FromHsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = l - c / 2;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        private static double RelLum(Color c)
        {
            double f(double v) { v /= 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            return 0.2126 * f(c.R) + 0.7152 * f(c.G) + 0.0722 * f(c.B);
        }

        /// <summary>
        /// Safe resource lookup used by code-behind. A missing token returns the fallback instead of
        /// throwing InvalidOperationException out of FindResource.
        /// </summary>
        public static Brush BrushOrDefault(string key, Brush fallback)
            => Application.Current?.Resources[key] as Brush ?? fallback;

        private void Set(string key, Color color) => SetBrush(key, color);

        /// <summary>
        /// Publishes a colour token as a frozen, shared brush. Brushes are the only thing XAML may
        /// consume through {DynamicResource}; SolidColorBrush.Color cannot take a resource
        /// reference and throws at load time if you try.
        /// </summary>
        private void SetBrush(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }

        private void BuildTokens()
        {
            var res = Application.Current.Resources;
            bool dark = IsDark;

            // accent family — derived from the live Windows accent
            var accent = _accent;
            var accentHover = Shift(accent, dark ? 0.07 : 0.05);
            var accentPressed = Shift(accent, dark ? -0.07 : -0.09);
            var accentDisabled = WithAlpha(accent, 0.4);
            var onAccent = RelLum(accent) > 0.45 ? S("#101010") : Colors.White;

            Set("AccentFill", accent);
            Set("AccentFillHover", accentHover);
            Set("AccentFillPressed", accentPressed);
            Set("AccentFillDisabled", accentDisabled);
            Set("OnAccentText", onAccent);
            Set("AccentText", dark ? Shift(accent, 0.16) : Shift(accent, -0.06));
            Set("AccentSubtleFill", WithAlpha(accent, dark ? 0.22 : 0.14));
            Set("AccentSubtleFillHover", WithAlpha(accent, dark ? 0.30 : 0.20));
            Set("AccentStroke", WithAlpha(accent, 0.55));

            if (dark)
            {
                Set("RootFill", _transparency ? WithAlpha(S("#1A1A1A"), 0.62) : S("#202020"));
                Set("RootFillSolid", S("#202020"));
                Set("LayerFill", WithAlpha(S("#FFFFFF"), 0.035));
                Set("LayerFillAlt", WithAlpha(S("#FFFFFF"), 0.055));
                Set("CardFill", WithAlpha(S("#FFFFFF"), 0.05));
                Set("CardFillHover", WithAlpha(S("#FFFFFF"), 0.075));
                Set("CardStroke", WithAlpha(S("#FFFFFF"), 0.075));
                Set("CardStrokeHover", WithAlpha(S("#FFFFFF"), 0.14));
                Set("FlyoutFill", S("#2C2C2C"));
                Set("FlyoutStroke", WithAlpha(S("#FFFFFF"), 0.09));
                Set("TextPrimary", WithAlpha(Colors.White, 0.98));
                Set("TextSecondary", WithAlpha(Colors.White, 0.78));
                Set("TextTertiary", WithAlpha(Colors.White, 0.56));
                Set("Divider", WithAlpha(S("#FFFFFF"), 0.08));
                Set("ControlFill", WithAlpha(S("#FFFFFF"), 0.06));
                Set("ControlFillHover", WithAlpha(S("#FFFFFF"), 0.09));
                Set("ControlFillPressed", WithAlpha(S("#FFFFFF"), 0.04));
                Set("ControlStroke", WithAlpha(S("#FFFFFF"), 0.09));
                Set("SelectionFill", WithAlpha(accent, 0.26));
                Set("SelectionBar", accent);
                Set("ReadPaneFill", _transparency ? WithAlpha(S("#FFFFFF"), 0.02) : S("#242424"));
                Set("SuccessFill", S("#6CCB5F"));
                Set("WarnFill", S("#FCE100"));
                Set("ErrorFill", S("#FF99A4"));
                SetBrush("ScrimFill", WithAlpha(S("#101010"), 0.96));
            }
            else
            {
                Set("RootFill", _transparency ? WithAlpha(S("#F5F5F5"), 0.66) : S("#F3F3F3"));
                Set("RootFillSolid", S("#F3F3F3"));
                Set("LayerFill", WithAlpha(Colors.White, 0.62));
                Set("LayerFillAlt", WithAlpha(Colors.White, 0.80));
                Set("CardFill", WithAlpha(Colors.White, 0.88));
                Set("CardFillHover", Colors.White);
                Set("CardStroke", WithAlpha(Colors.Black, 0.07));
                Set("CardStrokeHover", WithAlpha(Colors.Black, 0.12));
                Set("FlyoutFill", S("#FCFCFC"));
                Set("FlyoutStroke", WithAlpha(Colors.Black, 0.10));
                Set("TextPrimary", WithAlpha(Colors.Black, 0.90));
                Set("TextSecondary", WithAlpha(Colors.Black, 0.62));
                Set("TextTertiary", WithAlpha(Colors.Black, 0.44));
                Set("Divider", WithAlpha(Colors.Black, 0.08));
                Set("ControlFill", WithAlpha(Colors.White, 0.72));
                Set("ControlFillHover", WithAlpha(Colors.White, 0.95));
                Set("ControlFillPressed", WithAlpha(Colors.Black, 0.03));
                Set("ControlStroke", WithAlpha(Colors.Black, 0.09));
                Set("SelectionFill", WithAlpha(accent, 0.16));
                Set("SelectionBar", accent);
                Set("ReadPaneFill", WithAlpha(Colors.White, 0.66));
                Set("SuccessFill", S("#0F7B0F"));
                Set("WarnFill", S("#9D5D00"));
                Set("ErrorFill", S("#C42B1C"));
                SetBrush("ScrimFill", WithAlpha(S("#F3F3F3"), 0.96));
            }

            Application.Current.Resources["IsDarkTheme"] = dark;
            Application.Current.Resources["ThemeModeValue"] = _mode;
            Application.Current.Resources["AccentColorValue"] = accent;
        }

        public void Dispose()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnUserPreferenceChanged;
        }
    }
}

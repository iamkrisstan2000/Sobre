using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using Sobre.Models;
using Sobre.Services;
using Sobre.ViewModels;

namespace Sobre.Views
{
    public partial class MainWindow : Window
    {
        private ShellViewModel? _vm;

        public MainWindow()
        {
            InitializeComponent();

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            StateChanged += (_, _) => UpdateMaximizeGlyph();
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            // Caption handling is driven by ApplyTheme (glass path only).

            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook(WndProc);

            Native.ApplyWindowMaterial(this, ThemeService.Current.IsDark, ThemeService.Current.UseAcrylic,
                Native.TransparencyEnabled());
            UpdateMaximizeGlyph();
        }

        /// <summary>Keeps a maximised window inside the monitor work area once WS_CAPTION is gone.</summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0024) // WM_GETMINMAXINFO
            {
                Native.ConstrainToWorkArea(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _vm = DataContext as ShellViewModel;
            if (_vm != null)
            {
                _vm.PropertyChanged += OnVmPropertyChanged;
                _vm.Accounts.CollectionChanged += (_, _) => { };
            }

            ThemeService.Current.Changed += ApplyTheme;
            ApplyTheme();

            RestorePaneWidths();

            HookHyperlinks();
            UpdateReader();

            PreviewKeyDown += OnPreviewKeyDown;
        }

        // ---------------- pane widths ----------------

        private DispatcherTimer? _paneSaveTimer;

        private void RestorePaneWidths()
        {
            if (_vm == null) return;
            if (_vm.Settings.SidebarWidth is >= 196 and <= 460)
                SidebarColumn.Width = new GridLength(_vm.Settings.SidebarWidth);
            if (_vm.Settings.ListWidth is >= 300 and <= 680)
                ListColumn.Width = new GridLength(_vm.Settings.ListWidth);
        }

        private void OnPaneResized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
            => SchedulePaneSave();

        private void SchedulePaneSave()
        {
            _paneSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _paneSaveTimer.Stop();
            _paneSaveTimer.Tick -= OnPaneSaveTick;
            _paneSaveTimer.Tick += OnPaneSaveTick;
            _paneSaveTimer.Start();
        }

        private void OnPaneSaveTick(object? sender, EventArgs e)
        {
            _paneSaveTimer?.Stop();
            if (_vm == null) return;
            if (SidebarColumn.ActualWidth > 0) _vm.Settings.SidebarWidth = SidebarColumn.ActualWidth;
            if (ListColumn.ActualWidth > 0) _vm.Settings.ListWidth = ListColumn.ActualWidth;
            Store.SaveSettings(_vm.Settings);
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ShellViewModel.MessageDetail):
                case nameof(ShellViewModel.SelectedMessage):
                    UpdateReader();
                    break;
                case nameof(ShellViewModel.Settings):
                    UpdateReader();
                    break;
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_vm == null) return;

            if (e.Key == Key.Escape)
            {
                if (_vm.IsSettingsOpen) { _vm.CloseSettingsCommand.Execute(null); e.Handled = true; }
                else if (_vm.IsAddAccountOpen) { _vm.IsAddAccountOpen = false; e.Handled = true; }
                else if (_vm.IsComposeOpen) { _vm.IsComposeOpen = false; e.Handled = true; }
                else if (_vm.HasSearch) { _vm.SearchText = ""; e.Handled = true; }
                return;
            }

            // Shortcuts that must work anywhere in the app.
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F5)
            {
                _vm.SyncCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma)
            {
                _vm.IsSettingsOpen = true;
                e.Handled = true;
                return;
            }

            // Message shortcuts only when the user isn't typing in a field or overlay.
            if (e.OriginalSource is TextBoxBase || e.OriginalSource is PasswordBox) return;
            if (_vm.IsComposeOpen || _vm.IsSettingsOpen || _vm.IsAddAccountOpen || _vm.HasSearch) return;

            switch (e.Key)
            {
                case Key.N when Keyboard.Modifiers == ModifierKeys.Control:
                    _vm.ComposeNewCommand.Execute(null); e.Handled = true; break;
                case Key.R when Keyboard.Modifiers == ModifierKeys.Control:
                    _vm.ReplyCommand.Execute(null); e.Handled = true; break;
                case Key.R when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                    _vm.ReplyAllCommand.Execute(null); e.Handled = true; break;
                case Key.L when Keyboard.Modifiers == ModifierKeys.Control:
                    _vm.FlagCommand.Execute(null); e.Handled = true; break;
                case Key.Delete when Keyboard.Modifiers == ModifierKeys.None && _vm.HasSelection:
                    _vm.DeleteCommand.Execute(null); e.Handled = true; break;
            }
        }

        // ---------------- theme ----------------

        private void ApplyTheme()
        {
            var dark = ThemeService.Current.IsDark;

            // Mica/Acrylic needs a transparent client area plus a tint layer on top.
            // Without OS support (or with transparency switched off) we paint a solid Fluent layer.
            bool glass = Native.SupportsSystemBackdrop
                         && _vm?.Settings.UseAcrylic == true
                         && Native.TransparencyEnabled();

            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
            if (chrome != null)
                chrome.GlassFrameThickness = glass ? new Thickness(-1) : new Thickness(0);

            // With glass over the whole client area, DWM would draw a second set of caption
            // buttons on top of ours. Suppress the native caption on that path only; the
            // opaque path keeps the standard frame, which is what gives it its drop shadow.
            if (glass) Native.HideNativeCaption(this);
            else Native.RestoreNativeCaption(this);

            Background = glass ? Brushes.Transparent : ThemeService.BrushOrDefault("RootFillSolid", Brushes.Black);

            Native.ApplyWindowMaterial(this, dark, ThemeService.Current.UseAcrylic,
                Native.TransparencyEnabled() && glass);

            UpdateReader();
        }

        // ---------------- window chrome ----------------

        private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnMaximize(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            UpdateMaximizeGlyph();
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private void UpdateMaximizeGlyph()
        {
            if (MaxGlyph == null) return;
            MaxGlyph.Icon = WindowState == WindowState.Maximized ? "SquareMultiple" : "Square";
        }

        // ---------------- reader ----------------

        private void UpdateReader()
        {
            if (ReaderViewer == null) return;

            var detail = _vm?.MessageDetail;
            if (detail == null || _vm?.SelectedMessage == null)
            {
                ReaderViewer.Document = new FlowDocument();
                return;
            }

            var theme = ThemeService.Current;
            var accent = theme.Accent;
            double size = 15;

            var plain = string.IsNullOrWhiteSpace(detail.BodyHtml);
            var doc = HtmlToFlowDocument.Convert(detail.BodyHtml, plain, theme.IsDark, size, accent);
            ReaderViewer.Document = doc;
            ReaderViewer.Zoom = 100;
            HookHyperlinks();
        }

        private void HookHyperlinks()
        {
            if (ReaderViewer?.Document == null) return;
            foreach (var link in FindHyperlinks(ReaderViewer.Document))
            {
                link.RequestNavigate -= OnHyperlinkNavigate;
                link.RequestNavigate += OnHyperlinkNavigate;
            }
        }

        private static System.Collections.Generic.IEnumerable<Hyperlink> FindHyperlinks(FlowDocument doc)
        {
            foreach (var block in doc.Blocks)
                foreach (var link in FindIn(block))
                    yield return link;
        }

        private static System.Collections.Generic.IEnumerable<Hyperlink> FindIn(Block block)
        {
            if (block is Paragraph p)
            {
                foreach (var inline in p.Inlines)
                {
                    if (inline is Hyperlink h) yield return h;
                    else if (inline is Span s)
                        foreach (var nested in FindInSpan(s)) yield return nested;
                }
            }
            else if (block is List list)
            {
                foreach (var item in list.ListItems)
                    foreach (var b in item.Blocks)
                        foreach (var nested in FindIn(b))
                            yield return nested;
            }
            else if (block is Table table)
            {
                foreach (var group in table.RowGroups)
                    foreach (var row in group.Rows)
                        foreach (var cell in row.Cells)
                            foreach (var b in cell.Blocks)
                                foreach (var nested in FindIn(b))
                                    yield return nested;
            }
            else if (block is Section section)
            {
                foreach (var b in section.Blocks)
                    foreach (var nested in FindIn(b))
                        yield return nested;
            }
        }

        private static System.Collections.Generic.IEnumerable<Hyperlink> FindInSpan(Span span)
        {
            foreach (var inline in span.Inlines)
            {
                if (inline is Hyperlink h) yield return h;
                else if (inline is Span s)
                    foreach (var nested in FindInSpan(s)) yield return nested;
            }
        }

        private void OnHyperlinkNavigate(object sender, RequestNavigateEventArgs e)
        {
            // Never navigate inside the app; hand off to the user's default browser.
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
            }
            catch { }
            e.Handled = true;
        }
    }
}

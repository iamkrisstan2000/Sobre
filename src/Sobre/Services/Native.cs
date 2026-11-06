using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Sobre.Services
{
    /// <summary>
    /// Thin, dependency-free layer over the Win32 / DWM / UxTheme APIs Sobre needs:
    /// system accent colour, Mica &amp; Acrylic backdrops, immersive dark titlebar,
    /// rounded corners, and Windows light/dark preference.
    /// </summary>
    internal static class Native
    {
        // ---- accent / personalisation ----
        [DllImport("user32.dll")] private static extern bool GetSysColor(IntPtr nIndex);
        [DllImport("user32.dll")] private static extern int GetSysColor(int nIndex);

        private const int COLOR_HIGHLIGHT = 13;

        // ---- DWM window attributes ----
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
        [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled(out bool enabled);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int Left, Top, Right, Bottom; }

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        private enum BackdropType { Auto = 0, None = 1, Mica = 2, Acrylic = 3, Tabbed = 4 }
        private enum CornerPreference { Default = 0, DoNotRound = 1, Round = 2, RoundSmall = 3 }

        /// <summary>Windows build number (0 if it can't be read).</summary>
        public static int OsBuild
        {
            get
            {
                try { return Environment.OSVersion.Version.Build; }
                catch { return 0; }
            }
        }

        /// <summary>
        /// DWMWA_SYSTEMBACKDROP_TYPE (Mica / Acrylic) only exists from Windows 11 22H2.
        /// Older builds use the solid-layer fallback.
        /// </summary>
        public static bool SupportsSystemBackdrop => OsBuild >= 22621;

        /// <summary>The user's Windows accent colour (Settings &gt; Personalisation &gt; Colours).</summary>
        public static Color GetSystemAccentColor()
        {
            // Prefer the documented registry value (DWORD, ABGR) — matches what Settings shows.
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (key?.GetValue("AccentColor") is int abgr)
                {
                    // Stored as 0xAABBGGRR
                    byte r = (byte)(abgr & 0xFF);
                    byte g = (byte)((abgr >> 8) & 0xFF);
                    byte b = (byte)((abgr >> 16) & 0xFF);
                    return Color.FromRgb(r, g, b);
                }
            }
            catch { /* fall through */ }

            try
            {
                int bgr = GetSysColor(COLOR_HIGHLIGHT);
                return Color.FromRgb((byte)(bgr & 0xFF), (byte)((bgr >> 8) & 0xFF), (byte)((bgr >> 16) & 0xFF));
            }
            catch
            {
                return Color.FromRgb(0x00, 0x78, 0xD4); // Windows default blue
            }
        }

        /// <summary>True when Windows is set to the light app theme.</summary>
        public static bool IsSystemLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int v) return v != 0;
            }
            catch { }
            return false;
        }

        /// <summary>True when transparency effects are enabled in Windows.</summary>
        public static bool TransparencyEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("EnableTransparency") is int v) return v != 0;
            }
            catch { }
            return true;
        }

        /// <summary>Applies Mica / Acrylic / none, rounded corners and the dark titlebar.</summary>
        public static void ApplyWindowMaterial(Window window, bool dark, bool useAcrylic, bool transparency)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            try { DwmIsCompositionEnabled(out bool composed); if (!composed) return; } catch { }

            // Immersive dark titlebar (works on Win10 1809+ and Win11)
            int darkFlag = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkFlag, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref darkFlag, sizeof(int));

            // Rounded corners (Win11)
            int corner = (int)CornerPreference.Round;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            // Backdrop: Acrylic when the user wants depth, Mica otherwise.
            int backdrop = (int)(transparency ? (useAcrylic ? BackdropType.Acrylic : BackdropType.Mica) : BackdropType.None);
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

            // Let the WPF surface sit on the system backdrop.
            if (transparency && !useAcrylic)
            {
                var margins = new MARGINS { Left = -1, Top = -1, Right = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
            }
        }

        /// <summary>Extends the client area over the whole window so our custom titlebar is the caption.</summary>
        public static void ExtendFrame(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var margins = new MARGINS { Left = -1, Top = -1, Right = -1, Bottom = -1 };
            try { DwmExtendFrameIntoClientArea(hwnd, ref margins); } catch { }
        }

        /// <summary>Repaints the frame when the WindowChrome is in use, so drop shadows stay right.</summary>
        public static void RefreshFrame(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            const int WM_NCACTIVATE = 0x0086;
            SendMessage(hwnd, WM_NCACTIVATE, new IntPtr(1), IntPtr.Zero);
        }

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // ---- native caption suppression ----
        // When we extend glass over the whole window, DWM also paints its own caption buttons.
        // Clearing WS_CAPTION leaves WindowChrome's resize border and snap behaviour intact
        // while making the system buttons disappear entirely.
        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;
        private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_FRAMECHANGED = 0x0020;

        [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        /// <summary>Hides the OS-drawn caption buttons so only Sobre's own chrome is visible.</summary>
        public static void HideNativeCaption(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int style = GetWindowLong(hwnd, GWL_STYLE);
                if ((style & WS_CAPTION) != 0)
                {
                    SetWindowLong(hwnd, GWL_STYLE, style & ~WS_CAPTION);
                    SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
                }
            }
            catch { }
        }

        /// <summary>Re-adds WS_CAPTION, used when the window switches back to the opaque path.</summary>
        public static void RestoreNativeCaption(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int style = GetWindowLong(hwnd, GWL_STYLE);
                if ((style & WS_CAPTION) == 0)
                {
                    SetWindowLong(hwnd, GWL_STYLE, style | WS_CAPTION);
                    SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
                }
            }
            catch { }
        }

        // ---- maximise must respect the taskbar once WS_CAPTION is gone ----
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        private const int WM_GETMINMAXINFO = 0x0024;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        /// <summary>Clamps a maximised window to the monitor's working area (taskbar stays visible).</summary>
        public static void ConstrainToWorkArea(IntPtr hwnd, IntPtr lParam)
        {
            try
            {
                var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero) return;

                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(monitor, ref info)) return;

                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                var work = info.rcWork;
                var mon = info.rcMonitor;

                mmi.ptMaxPosition.X = work.Left - mon.Left;
                mmi.ptMaxPosition.Y = work.Top - mon.Top;
                mmi.ptMaxSize.X = work.Right - work.Left;
                mmi.ptMaxSize.Y = work.Bottom - work.Top;

                Marshal.StructureToPtr(mmi, lParam, true);
            }
            catch { }
        }

        public static int GetMessageId() => WM_GETMINMAXINFO;

    }
}

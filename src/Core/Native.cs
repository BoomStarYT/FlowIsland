using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Island.Core;

/// <summary>Direkte Windows-Funktionen (nur lesen / Fensterstil, nichts Gefährliches).</summary>
internal static class Native
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080; // nicht in Alt+Tab
    private const int WS_EX_NOACTIVATE = 0x08000000; // klaut keinen Fokus
    private const uint MONITOR_DEFAULTTONULL = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    // ------------------------------------------------------------------ Akku / CPU / RAM (nur lesen)

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    /// <summary>Akkustand in Prozent und ob er lädt. null = kein Akku (z. B. Gaming-PC).</summary>
    public static (int Percent, bool Charging)? GetBattery()
    {
        if (!GetSystemPowerStatus(out var s)) return null;
        if ((s.BatteryFlag & 128) != 0 || s.BatteryLifePercent > 100) return null;
        return (s.BatteryLifePercent, s.ACLineStatus == 1);
    }

    private static long _lastIdle, _lastTotal;

    /// <summary>CPU-Auslastung in Prozent seit dem letzten Aufruf.</summary>
    public static int GetCpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var total = kernel + user; // kernel enthält idle schon
        var dIdle = idle - _lastIdle;
        var dTotal = total - _lastTotal;
        _lastIdle = idle;
        _lastTotal = total;
        if (dTotal <= 0) return 0;
        return (int)Math.Clamp(Math.Round(100.0 * (1 - (double)dIdle / dTotal)), 0, 100);
    }

    public static int GetRamPercent()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? (int)m.dwMemoryLoad : 0;
    }

    // ------------------------------------------------------------------ Echter Glas-Blur

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);

    /// <summary>Windows-Unschärfe hinter dem Fenster an/aus (wirkt nur innerhalb der Fensterform).</summary>
    public static void SetBlur(IntPtr hwnd, bool on)
    {
        try
        {
            var accent = new AccentPolicy { AccentState = on ? 3 /* BLURBEHIND */ : 0 };
            var size = Marshal.SizeOf(accent);
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData { Attribute = 19 /* ACCENT_POLICY */, Data = ptr, SizeOfData = size };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch { /* ältere Windows-Version */ }
    }

    /// <summary>Fensterform = abgerundetes Rechteck (in Pixeln). flat: "Oben"/"Unten"/"Links"/"Rechts" = diese Seite gerade.</summary>
    public static void SetRoundRegion(IntPtr hwnd, int left, int top, int right, int bottom, int diameter, string flat)
    {
        var rgn = CreateRoundRectRgn(left, top, right + 1, bottom + 1, diameter, diameter);
        if (flat.Length > 0)
        {
            int midX = (left + right) / 2, midY = (top + bottom) / 2;
            var square = flat switch
            {
                "Oben" => CreateRectRgn(left, top, right, midY),
                "Unten" => CreateRectRgn(left, midY, right, bottom),
                "Links" => CreateRectRgn(left, top, midX, bottom),
                _ => CreateRectRgn(midX, top, right, bottom),
            };
            CombineRgn(rgn, rgn, square, 2 /* RGN_OR */);
            DeleteObject(square);
        }
        if (SetWindowRgn(hwnd, rgn, true) == 0) DeleteObject(rgn);
    }

    public static void ClearRegion(IntPtr hwnd) => SetWindowRgn(hwnd, IntPtr.Zero, true);

    // ------------------------------------------------------------------ Tasten senden (für Kontrollzentrum)

    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    /// <summary>Drückt eine Tastenkombination, z. B. (0x5B, 0x44) = Win + D.</summary>
    public static void SendKeys(params byte[] keys)
    {
        foreach (var k in keys) keybd_event(k, 0, 0, UIntPtr.Zero);
        for (int i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], 0, 2 /* KEYUP */, UIntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    /// <summary>Hell/Dunkel-Modus von Windows umschalten. Gibt den neuen Zustand zurück (true = dunkel).</summary>
    public static bool? ToggleDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: true);
            if (key == null) return null;
            var light = (key.GetValue("AppsUseLightTheme") as int? ?? 1) == 1;
            var value = light ? 0 : 1;
            key.SetValue("AppsUseLightTheme", value, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", value, Microsoft.Win32.RegistryValueKind.DWord);
            SendMessageTimeout((IntPtr)0xFFFF, 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, "ImmersiveColorSet", 2, 200, out _);
            return light; // war hell → jetzt dunkel
        }
        catch { return null; }
    }

    public static bool? IsDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key == null ? null : (key.GetValue("AppsUseLightTheme") as int? ?? 1) == 0;
        }
        catch { return null; }
    }

    /// <summary>Klicks gehen durch das Fenster hindurch.</summary>
    public static void MakeClickThrough(IntPtr hwnd)
    {
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | 0x20 /* WS_EX_TRANSPARENT */);
    }

    /// <summary>Kein Alt+Tab-Eintrag, kein Fokus-Klauen.</summary>
    public static void MakeToolWindow(IntPtr hwnd)
    {
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    /// <summary>Arbeitsbereich (ohne Taskleiste) des Bildschirms an diesem Punkt, in Bildschirm-Pixeln.
    /// null, wenn an dem Punkt kein Bildschirm ist und <paramref name="nearest"/> false ist.</summary>
    public static Rect? GetWorkArea(double xPx, double yPx, bool nearest)
    {
        var monitor = MonitorFromPoint(new POINT { X = (int)xPx, Y = (int)yPx }, nearest ? MONITOR_DEFAULTTONEAREST : MONITOR_DEFAULTTONULL);
        if (monitor == IntPtr.Zero) return null;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref mi)) return null;
        var w = mi.rcWork;
        return new Rect(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top);
    }

    /// <summary>Läuft gerade etwas im Vollbild auf dem gleichen Bildschirm wie die Insel?</summary>
    public static bool IsForegroundFullscreen(IntPtr own)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == own) return false;

        var sb = new StringBuilder(64);
        GetClassName(fg, sb, sb.Capacity);
        var cls = sb.ToString();
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        var monitor = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
        if (monitor != MonitorFromWindow(own, MONITOR_DEFAULTTONEAREST)) return false;
        if (!GetWindowRect(fg, out var r)) return false;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref mi)) return false;

        return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top
            && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
    }

    // ------------------------------------------------------------------ Grosse, scharfe Symbole (ohne Verknüpfungspfeil)

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION { public BITMAP dsBm; public BITMAPINFOHEADER dsBmih; public uint b0, b1, b2; public IntPtr dshSection; public uint dsOffset; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? item);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(IntPtr h, int size, out DIBSECTION ds);

    /// <summary>
    /// Symbol in hoher Auflösung über die Windows-Shell – klappt für Dateien, Ordner, Verknüpfungen (ohne Pfeil)
    /// und Store-Apps ("shell:AppsFolder\..."). null, wenn es nicht geht.
    /// </summary>
    public static BitmapSource? GetShellImage(string parsingName, int size)
    {
        IShellItemImageFactory? factory = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out factory) != 0 || factory == null) return null;
            if (factory.GetImage(new SIZE { cx = size, cy = size }, 0x1 /* BIGGERSIZEOK */ | 0x4 /* ICONONLY */, out var hbm) != 0 || hbm == IntPtr.Zero) return null;
            try
            {
                if (GetObject(hbm, Marshal.SizeOf<DIBSECTION>(), out var ds) == 0 || ds.dsBm.bmBits == IntPtr.Zero || ds.dsBm.bmBitsPixel != 32)
                {
                    var fallback = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    fallback.Freeze();
                    return fallback;
                }
                int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight, stride = ds.dsBm.bmWidthBytes;
                var buf = new byte[stride * h];
                Marshal.Copy(ds.dsBm.bmBits, buf, 0, buf.Length);

                // DIBs liegen oft "auf dem Kopf" (unterste Zeile zuerst)
                if (ds.dsBmih.biHeight > 0)
                {
                    var row = new byte[stride];
                    for (int y = 0; y < h / 2; y++)
                    {
                        Buffer.BlockCopy(buf, y * stride, row, 0, stride);
                        Buffer.BlockCopy(buf, (h - 1 - y) * stride, buf, y * stride, stride);
                        Buffer.BlockCopy(row, 0, buf, (h - 1 - y) * stride, stride);
                    }
                }
                // Ohne Alpha-Kanal → alles deckend
                var anyAlpha = false;
                for (int i = 3; i < buf.Length; i += 4) if (buf[i] != 0) { anyAlpha = true; break; }
                if (!anyAlpha) for (int i = 3; i < buf.Length; i += 4) buf[i] = 255;

                var bmp = BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null, buf, stride);
                bmp.Freeze();
                return bmp;
            }
            finally { DeleteObject(hbm); }
        }
        catch { return null; }
        finally { if (factory != null) Marshal.ReleaseComObject(factory); }
    }

    /// <summary>Holt das Symbol einer Datei / eines Ordners / einer .exe.</summary>
    public static BitmapSource? GetIcon(string path)
    {
        var info = new SHFILEINFO();
        var result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var bmp = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        finally { DestroyIcon(info.hIcon); }
    }
}

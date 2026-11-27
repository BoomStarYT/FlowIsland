using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace Island.Core;

/// <summary>
/// Unsichtbares Hilfsfenster direkt HINTER der Insel, das nur den Windows-Blur zeigt.
/// Es hat genau die Form der Insel (abgerundet) – so wird nur hinter der Insel weichgezeichnet,
/// nicht das ganze (grössere, durchsichtige) Inselfenster. Klicks gehen durch.
/// </summary>
internal sealed class GlassBackdrop : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;

    private readonly HwndSource _source;
    private bool _visible;
    private (int X, int Y, int W, int H, int D, string Flat) _last = (0, 0, 0, 0, -1, "");

    public IntPtr Handle => _source.Handle;

    public GlassBackdrop()
    {
        var p = new HwndSourceParameters("FlowIslandGlas")
        {
            WindowStyle = unchecked((int)0x80000000),                 // WS_POPUP (ohne Rahmen)
            ExtendedWindowStyle = 0x80 | 0x08000000 | 0x20 | 0x8,     // Toolfenster, kein Fokus, klick-durchlässig, immer oben
            PositionX = -32000,
            PositionY = -32000,
            Width = 1,
            Height = 1,
            UsesPerPixelOpacity = false,
        };
        _source = new HwndSource(p);
        if (_source.CompositionTarget != null) _source.CompositionTarget.BackgroundColor = Colors.Transparent;

        // Durchsichtiger Inhalt → dahinter zeigt Windows den Blur
        var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        try { DwmExtendFrameIntoClientArea(Handle, ref m); } catch { /* egal */ }
        Native.SetBlur(Handle, true);
    }

    /// <summary>Die Insel gehört zu diesem Fenster → Windows hält sie immer direkt darüber.</summary>
    public void Own(IntPtr island) => SetWindowLongPtr(island, -8 /* GWLP_HWNDPARENT */, Handle);

    /// <summary>Position/Grösse in Bildschirm-Pixeln, Rundung als Durchmesser.</summary>
    public void Place(int x, int y, int w, int h, int diameter, string flat)
    {
        if (w < 2 || h < 2) { Hide(); return; }
        var key = (x, y, w, h, diameter, flat);
        if (key == _last) return;
        var sizeChanged = w != _last.W || h != _last.H || diameter != _last.D || flat != _last.Flat;
        _last = key;
        SetWindowPos(Handle, IntPtr.Zero, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        if (sizeChanged) Native.SetRoundRegion(Handle, 0, 0, w - 1, h - 1, diameter, flat);
    }

    public void Show()
    {
        if (_visible) return;
        _visible = true;
        ShowWindow(Handle, 4 /* SW_SHOWNOACTIVATE */);
    }

    public void Hide()
    {
        if (!_visible) return;
        _visible = false;
        ShowWindow(Handle, 0 /* SW_HIDE */);
    }

    public void Dispose() => _source.Dispose();
}

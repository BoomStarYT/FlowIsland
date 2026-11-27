using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Island.Core;

namespace Island;

/// <summary>
/// Kleines Lyrics-Feld neben der Insel – im gleichen Look. Zeigt die aktuelle und die nächste Zeile,
/// auch wenn die Insel zu ist. Klicks gehen durch (stört nie).
/// </summary>
public sealed class LyricsWindow : Window
{
    private const double Gap = 10;
    private readonly Border _card;
    private readonly StackPanel _stack;
    private readonly TextBlock _now, _next;
    private readonly TranslateTransform _slide = new();
    private GlassBackdrop? _glass;
    private bool _onRight;
    private Rect _anchor;
    private double _anchorHeight = 36;

    public LyricsWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        Left = -10000;
        Top = -10000;

        _now = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 340 };
        _next = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 340, Margin = new Thickness(0, 2, 0, 0) };
        _now.SetResourceReference(TextBlock.ForegroundProperty, "IslandText");
        _next.SetResourceReference(TextBlock.ForegroundProperty, "IslandSubText");

        _stack = new StackPanel { RenderTransform = _slide };
        _stack.Children.Add(_now);
        _stack.Children.Add(_next);

        _card = new Border
        {
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(16, 8, 16, 9),
            BorderThickness = new Thickness(1),
            Child = _stack,
            MinWidth = 120,
            ClipToBounds = true,
        };
        Content = _card;
        ApplyLook();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeToolWindow(hwnd);
            Native.MakeClickThrough(hwnd);
        };
        SizeChanged += (_, _) => { Reposition(); PlaceGlass(); };
        LocationChanged += (_, _) => PlaceGlass();
        Closed += (_, _) => _glass?.Dispose();
    }

    /// <summary>Farben/Glas wie die Insel.</summary>
    public void ApplyLook()
    {
        _card.Background = Theme.CurrentFill;
        _card.BorderBrush = Theme.CurrentRim;
        FontFamily = Application.Current.MainWindow?.FontFamily ?? FontFamily;
    }

    public void SetBlur(bool on)
    {
        if (on && _glass == null && IsLoaded)
        {
            try
            {
                _glass = new GlassBackdrop();
                _glass.Own(new WindowInteropHelper(this).Handle);
            }
            catch (Exception ex) { App.Log(ex); _glass = null; }
        }
        if (_glass == null) return;
        if (on && IsVisible) { PlaceGlass(); _glass.Show(); }
        else _glass.Hide();
    }

    private void PlaceGlass()
    {
        if (_glass == null || !IsVisible || PresentationSource.FromVisual(_card) == null) return;
        var a = _card.PointToScreen(new Point(0, 0));
        var b = _card.PointToScreen(new Point(_card.ActualWidth, _card.ActualHeight));
        var d = PresentationSource.FromVisual(_card)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        _glass.Place((int)a.X, (int)a.Y, (int)(b.X - a.X), (int)(b.Y - a.Y), (int)(18 * 2 * d), "");
    }

    /// <summary>Neue Zeile: gleitet weich von unten rein.</summary>
    public void SetText(string now, string next, bool playing)
    {
        var changed = _now.Text != now;
        _now.Text = now;
        _next.Text = next;
        _next.Visibility = next.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Anim.To(_card, OpacityProperty, playing ? 1 : 0.6, 260, Anim.Smooth);
        if (!changed) return;
        Anim.To(_slide, TranslateTransform.YProperty, 0, 420, Anim.Spring, from: 10);
        Anim.To(_stack, OpacityProperty, 1, 240, Anim.Smooth, from: 0.15);
    }

    /// <summary>Neben die Insel setzen (pill = Bildschirm-Pixel der Insel). Links, wenn Platz – sonst rechts.</summary>
    public void Anchor(Rect pillPx, double compactHeightPx, double deviceScale)
    {
        _anchor = new Rect(pillPx.X / deviceScale, pillPx.Y / deviceScale, pillPx.Width / deviceScale, pillPx.Height / deviceScale);
        _anchorHeight = compactHeightPx / deviceScale;
        Reposition();
    }

    private void Reposition()
    {
        if (_anchor.IsEmpty || ActualWidth <= 0) return;
        var work = SystemParameters.VirtualScreenLeft;
        var leftX = _anchor.Left - Gap - ActualWidth;
        _onRight = leftX < work + 4;
        var x = _onRight ? _anchor.Right + Gap : leftX;
        var y = _anchor.Top + Math.Max(0, (_anchorHeight - ActualHeight) / 2);
        if (_anchor.Top < 4) y = Math.Max(y, 4); // nicht am Bildschirmrand kleben
        _now.TextAlignment = _next.TextAlignment = _onRight ? TextAlignment.Left : TextAlignment.Right;
        Left = x;
        Top = y;
    }
}

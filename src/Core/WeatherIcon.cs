using System;
using System.Windows;
using System.Windows.Media;

namespace Island.Core;

/// <summary>Selbst gezeichnete Wetter-Symbole (Sonne, Mond, Wolke, Regen, Schnee, Blitz, Nebel).</summary>
public sealed class WeatherIcon : FrameworkElement
{
    private static readonly SolidColorBrush Sun = Ui.Brush("#FFFFD60A");
    private static readonly SolidColorBrush Moon = Ui.Brush("#FFE5E5EA");
    private static readonly SolidColorBrush Rain = Ui.Brush("#FF64D2FF");
    private static readonly SolidColorBrush Snow = Ui.Brush("#FFF2F2F7");

    private int _code;
    private bool _day = true;

    public WeatherIcon(double size = 24)
    {
        Width = size;
        Height = size;
        IsHitTestVisible = false;
    }

    public int Code { get => _code; set { _code = value; InvalidateVisual(); } }
    public bool IsDay { get => _day; set { _day = value; InvalidateVisual(); } }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        dc.PushTransform(new ScaleTransform(size / 24, size / 24));
        var cloud = (TryFindResource("IslandText") as Brush) ?? Brushes.White;
        var c = _code;

        if (c is 0 or 1)
        {
            if (_day) DrawSun(dc, 12, 12, 5);
            else DrawMoon(dc, 12, 12, 7);
        }
        else if (c == 2)
        {
            if (_day) DrawSun(dc, 9, 9, 4);
            else DrawMoon(dc, 9, 8.5, 5);
            DrawCloud(dc, cloud, 13.5, 15, 0.75);
        }
        else
        {
            var y = c is 3 or 45 or 48 ? 13 : 11;
            dc.PushOpacity(c == 3 ? 0.9 : 1);
            DrawCloud(dc, cloud, 12, y, 0.95);
            dc.Pop();
            if (c is 45 or 48) Fog(dc, cloud);
            else if (c is >= 51 and <= 67 or >= 80 and <= 82) Drops(dc, c >= 63);
            else if (c is >= 71 and <= 77 or 85 or 86) Flakes(dc);
            else if (c >= 95) Bolt(dc);
        }
        dc.Pop();
    }

    private static void DrawSun(DrawingContext dc, double cx, double cy, double r)
    {
        var pen = new Pen(Sun, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (int i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            dc.DrawLine(pen, new Point(cx + Math.Cos(a) * (r + 2), cy + Math.Sin(a) * (r + 2)),
                             new Point(cx + Math.Cos(a) * (r + 4.2), cy + Math.Sin(a) * (r + 4.2)));
        }
        dc.DrawEllipse(Sun, null, new Point(cx, cy), r, r);
    }

    private static void DrawMoon(DrawingContext dc, double cx, double cy, double r)
    {
        var full = new EllipseGeometry(new Point(cx, cy), r, r);
        var cut = new EllipseGeometry(new Point(cx + r * 0.55, cy - r * 0.35), r * 0.85, r * 0.85);
        dc.DrawGeometry(Moon, null, Geometry.Combine(full, cut, GeometryCombineMode.Exclude, null));
    }

    /// <summary>Wolke aus drei Kreisen und einem runden Sockel. (cx, cy) = Mitte, s = Grösse.</summary>
    private static void DrawCloud(DrawingContext dc, Brush brush, double cx, double cy, double s)
    {
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        g.Children.Add(new EllipseGeometry(new Point(cx - 4.5 * s, cy + 1 * s), 4 * s, 4 * s));
        g.Children.Add(new EllipseGeometry(new Point(cx + 0.5 * s, cy - 2 * s), 5.5 * s, 5.5 * s));
        g.Children.Add(new EllipseGeometry(new Point(cx + 5.5 * s, cy + 1.2 * s), 3.8 * s, 3.8 * s));
        g.Children.Add(new RectangleGeometry(new Rect(cx - 8.5 * s, cy + 0.5 * s, 17.8 * s, 4.5 * s), 2.2 * s, 2.2 * s));
        dc.DrawGeometry(brush, null, g);
    }

    private static void Drops(DrawingContext dc, bool heavy)
    {
        var pen = new Pen(Rain, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var xs = heavy ? new[] { 7.0, 11.0, 15.0, 19.0 } : new[] { 8.0, 12.5, 17.0 };
        foreach (var x in xs) dc.DrawLine(pen, new Point(x, 18), new Point(x - 1.5, 22));
    }

    private static void Flakes(DrawingContext dc)
    {
        foreach (var (x, y) in new[] { (8.0, 19.0), (12.5, 21.5), (17.0, 19.0) })
            dc.DrawEllipse(Snow, null, new Point(x, y), 1.4, 1.4);
    }

    private static void Bolt(DrawingContext dc)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(13, 15), true, true);
            c.LineTo(new Point(9.5, 20), false, false);
            c.LineTo(new Point(12, 20), false, false);
            c.LineTo(new Point(10.5, 24), false, false);
            c.LineTo(new Point(15, 18.5), false, false);
            c.LineTo(new Point(12.5, 18.5), false, false);
        }
        g.Freeze();
        dc.DrawGeometry(Sun, null, g);
    }

    private static void Fog(DrawingContext dc, Brush brush)
    {
        var pen = new Pen(brush, 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.PushOpacity(0.7);
        dc.DrawLine(pen, new Point(5, 20), new Point(17, 20));
        dc.DrawLine(pen, new Point(8, 22.5), new Point(20, 22.5));
        dc.Pop();
    }
}

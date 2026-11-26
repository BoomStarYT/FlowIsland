using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Island.Core;

/// <summary>Baut aus den Einstellungen das Aussehen der Insel: Einfarbig, Verlauf oder Liquid Glass.</summary>
public static class Theme
{
    public const string Solid = "Einfarbig";
    public const string Gradient = "Verlauf";
    public const string Glass = "Liquid Glass";
    public const string PureGlass = "Klarglas";
    public const string Cover = "Cover";

    /// <summary>Kräftige Farbe aus dem Cover des laufenden Songs (null = kein Cover).</summary>
    public static Color? CoverColor { get; private set; }

    /// <summary>Wird ausgelöst, wenn ein neues Cover eine andere Farbe hat.</summary>
    public static event Action? CoverChanged;

    /// <summary>Aktueller Hintergrund der Insel (für Fenster im gleichen Look, z. B. Aufgaben).</summary>
    public static Brush CurrentFill { get; private set; } = new SolidColorBrush(Color.FromRgb(11, 11, 12));
    public static Brush CurrentRim { get; private set; } = Brushes.Transparent;
    public static bool CurrentIsLight { get; private set; }

    public static void SetCoverColor(Color? color)
    {
        if (CoverColor == color) return;
        CoverColor = color;
        try { CoverChanged?.Invoke(); }
        catch (Exception ex) { App.Log(ex); }
    }

    public static void Apply(Settings s, Border background, Border sheen, Border rim, UIElement shadow)
    {
        var c1 = ParseColor(s.Farbe1, Color.FromRgb(11, 11, 12));
        var c2 = ParseColor(s.Farbe2, Color.FromRgb(58, 28, 113));
        var opacity = Math.Clamp(s.Deckkraft, 0.3, 1.0);
        var style = s.Stil ?? Solid;

        Brush fill, sheenBrush, rimBrush;
        double rimThickness = 1;
        Color reference; // für die Entscheidung helle oder dunkle Schrift

        if (style == PureGlass)
        {
            // Reines Glas ohne Farbe: wolkig = milchig-weiss, klar = fast durchsichtig.
            // Bei klarem Glas liegt ein ganz leichter dunkler Kern darunter, damit Text lesbar bleibt.
            var clear = Math.Clamp(s.GlasKlarheit, 0, 1);
            var dark = Lerp(0.0, 0.24, clear);
            var top = Over(Colors.White, Lerp(0.62, 0.10, clear), Colors.Black, dark);
            var bottom = Over(Colors.White, Lerp(0.46, 0.04, clear), Colors.Black, dark);
            fill = Freeze(Linear(90, (top, 0), (bottom, 1)));

            sheenBrush = Freeze(Linear(90,
                (WithAlpha(Colors.White, (byte)Lerp(70, 120, clear)), 0.00),
                (WithAlpha(Colors.White, (byte)Lerp(18, 34, clear)), 0.30),
                (WithAlpha(Colors.White, 0), 0.52),
                (WithAlpha(Colors.White, 0), 0.80),
                (WithAlpha(Colors.White, (byte)Lerp(16, 40, clear)), 1.00)));

            rimBrush = Freeze(Linear(120,
                (WithAlpha(Colors.White, 230), 0.00),
                (WithAlpha(Colors.White, 50), 0.35),
                (WithAlpha(Colors.White, 20), 0.65),
                (WithAlpha(Colors.White, 160), 1.00)));
            rimThickness = 1.4;
            // Wolkig (hell) → dunkle Schrift, klar → helle Schrift
            reference = clear < 0.45 ? Colors.White : Colors.Black;
        }
        else if (style == Glass)
        {
            // Getöntes, durchsichtiges Glas mit Lichtkante oben und Spiegelung
            var alphaTop = (byte)(255 * (0.30 + 0.42 * opacity));
            var alphaBottom = (byte)(255 * (0.40 + 0.45 * opacity));
            fill = Freeze(Linear(90,
                (WithAlpha(Lighten(c1, 0.10), alphaTop), 0),
                (WithAlpha(c1, alphaBottom), 1)));

            sheenBrush = Freeze(Linear(90,
                (WithAlpha(Colors.White, 82), 0.00),
                (WithAlpha(Colors.White, 26), 0.32),
                (WithAlpha(Colors.White, 0), 0.55),
                (WithAlpha(Colors.White, 0), 0.82),
                (WithAlpha(Colors.White, 18), 1.00)));

            rimBrush = Freeze(Linear(120,
                (WithAlpha(Colors.White, 190), 0.00),
                (WithAlpha(Colors.White, 40), 0.35),
                (WithAlpha(Colors.White, 14), 0.65),
                (WithAlpha(Colors.White, 110), 1.00)));
            rimThickness = 1.2;
            reference = c1;
        }
        else if (style == Cover)
        {
            // Dunkel und edel, mit einem Hauch der Cover-Farbe
            var tint = CoverColor ?? ParseColor(s.Akzent, Color.FromRgb(48, 209, 88));
            var dark = Color.FromRgb(12, 12, 15);
            var a = (byte)(255 * opacity);
            fill = Freeze(Linear(120,
                (WithAlpha(Blend(dark, tint, 0.26), a), 0),
                (WithAlpha(Blend(dark, tint, 0.10), a), 0.55),
                (WithAlpha(dark, a), 1)));
            sheenBrush = Freeze(Layers(
                Radial(new Point(0.12, 0.0), 0.9, 1.6, (WithAlpha(tint, 70), 0), (WithAlpha(tint, 0), 1)),
                Linear(90, (WithAlpha(Colors.White, 26), 0), (WithAlpha(Colors.White, 0), 0.45))));
            rimBrush = Freeze(Linear(100,
                (WithAlpha(Blend(Colors.White, tint, 0.4), 90), 0),
                (WithAlpha(Colors.White, 14), 0.5),
                (WithAlpha(tint, 50), 1)));
            rimThickness = 1.1;
            reference = dark;
        }
        else if (style == Gradient)
        {
            // Drei Stufen + weicher Lichtschein → wirkt tiefer als ein einfacher Verlauf
            var a = (byte)(255 * opacity);
            var middle = Saturate(Blend(c1, c2, 0.5), 0.12);
            fill = Freeze(Linear(s.VerlaufWinkel, (WithAlpha(c1, a), 0), (WithAlpha(middle, a), 0.52), (WithAlpha(c2, a), 1)));
            sheenBrush = Freeze(Layers(
                Radial(new Point(0.9, 0.0), 0.8, 1.5, (WithAlpha(Lighten(c2, 0.35), 64), 0), (WithAlpha(c2, 0), 1)),
                Linear(90, (WithAlpha(Colors.White, 40), 0), (WithAlpha(Colors.White, 6), 0.4), (WithAlpha(Colors.White, 0), 0.6))));
            rimBrush = Freeze(Linear(90, (WithAlpha(Colors.White, 80), 0), (WithAlpha(Colors.White, 18), 0.5), (WithAlpha(Colors.White, 34), 1)));
            rimThickness = 1.1;
            reference = Blend(c1, c2, 0.5);
        }
        else
        {
            // Einfarbig: ganz leichte Wölbung (oben heller) + feine Lichtkante
            var a = (byte)(255 * opacity);
            fill = Freeze(Linear(90, (WithAlpha(Lighten(c1, 0.07), a), 0), (WithAlpha(c1, a), 0.55), (WithAlpha(Darken(c1, 0.06), a), 1)));
            sheenBrush = Freeze(Linear(90, (WithAlpha(Colors.White, 22), 0), (WithAlpha(Colors.White, 4), 0.35), (WithAlpha(Colors.White, 0), 0.5)));
            rimBrush = Freeze(Linear(90, (WithAlpha(Colors.White, 64), 0), (WithAlpha(Colors.White, 14), 0.45), (WithAlpha(Colors.White, 22), 1)));
            reference = c1;
        }

        CurrentFill = fill;
        CurrentRim = rimBrush;
        CurrentIsLight = IsLight(reference);
        background.Background = fill;
        sheen.Background = sheenBrush;
        rim.BorderBrush = rimBrush;
        rim.BorderThickness = new Thickness(rimThickness);

        // Kein echter Weichzeichner-Schatten (DropShadowEffect) – der macht Animationen ruckelig.
        background.Effect = null;
        shadow.Visibility = s.Schatten ? Visibility.Visible : Visibility.Collapsed;
        shadow.Opacity = style == Glass ? 0.6 : style == PureGlass ? 0.45 : 1.0;

        ApplyTextColors(IsLight(reference), AccentFor(s));
    }

    /// <summary>Akzent aus den Einstellungen – oder vom Cover, wenn so eingestellt.</summary>
    public static Color AccentFor(Settings s)
    {
        var own = ParseColor(s.Akzent, Color.FromRgb(48, 209, 88));
        if ((s.AkzentVomCover || s.Stil == Cover) && CoverColor is Color c) return c;
        return own;
    }

    /// <summary>Helle Insel → dunkle Schrift, dunkle Insel → helle Schrift.</summary>
    private static void ApplyTextColors(bool light, Color accent)
    {
        var res = Application.Current.Resources;
        var baseColor = light ? Color.FromRgb(28, 28, 30) : Colors.White;
        res["IslandText"] = SolidBrush(baseColor, 255);
        res["IslandSubText"] = SolidBrush(baseColor, light ? (byte)140 : (byte)166);
        res["IslandHover"] = SolidBrush(baseColor, light ? (byte)22 : (byte)38);
        res["IslandPressed"] = SolidBrush(baseColor, light ? (byte)40 : (byte)64);
        res["IslandTrack"] = SolidBrush(baseColor, light ? (byte)38 : (byte)51);
        res["IslandPlaceholder"] = SolidBrush(baseColor, light ? (byte)26 : (byte)38);
        res["IslandChip"] = SolidBrush(baseColor, light ? (byte)22 : (byte)38);
        // Akzent wechselt sanft (z. B. beim nächsten Song mit anderer Cover-Farbe)
        if (res["IslandAccent"] is SolidColorBrush current && !current.IsFrozen)
        {
            if (current.Color != accent)
                current.BeginAnimation(SolidColorBrush.ColorProperty,
                    new ColorAnimation(accent, TimeSpan.FromMilliseconds(650)) { EasingFunction = Anim.Smooth });
        }
        else res["IslandAccent"] = new SolidColorBrush(accent);
    }

    // ------------------------------------------------------------------ Farb-Helfer

    public static Color ParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            var text = hex.Trim();
            if (!text.StartsWith('#')) text = "#" + text;
            return (Color)ColorConverter.ConvertFromString(text);
        }
        catch { return fallback; }
    }

    public static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var text = hex.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        if (text.Length is not (4 or 7 or 9)) return false;
        try { color = (Color)ColorConverter.ConvertFromString(text); return true; }
        catch { return false; }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Farbe aus Farbton (0–360), Sättigung (0–1) und Helligkeit (0–1).</summary>
    public static Color FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    /// <summary>
    /// Erfindet eine harmonische Farbkombi: zwei benachbarte Farbtöne (wirkt ruhig und edel)
    /// plus passende Akzentfarbe. Mal dunkel und edel, mal pastellig und hell.
    /// </summary>
    public static (string Farbe1, string Farbe2, double Winkel, string Akzent) RandomPalette()
    {
        var rnd = Random.Shared;
        var hue = rnd.NextDouble() * 360;
        var shift = (rnd.Next(2) == 0 ? 1 : -1) * (25 + rnd.NextDouble() * 40);
        var angles = new[] { 115.0, 135.0, 160.0, 200.0 };
        var angle = angles[rnd.Next(angles.Length)];

        switch (rnd.Next(3))
        {
            case 0: // Pastell
                return (ToHex(FromHsl(hue, 0.65, 0.84)), ToHex(FromHsl(hue + shift, 0.60, 0.74)), angle,
                        ToHex(FromHsl(hue + shift, 0.70, 0.38)));
            case 1: // Kräftig
                return (ToHex(FromHsl(hue, 0.75, 0.50)), ToHex(FromHsl(hue + shift, 0.80, 0.42)), angle, "#FFFFFF");
            default: // Dunkel & edel
                return (ToHex(FromHsl(hue, 0.45, 0.13)), ToHex(FromHsl(hue + shift, 0.50, 0.28)), angle,
                        ToHex(FromHsl(hue + shift + 150, 0.85, 0.68)));
        }
    }

    public static bool IsLight(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0 > 0.62;

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>Farbe "oben" (mit Deckkraft) über Farbe "unten" (mit Deckkraft) gelegt → eine Farbe mit Alpha.</summary>
    private static Color Over(Color top, double topAlpha, Color bottom, double bottomAlpha)
    {
        var a = topAlpha + bottomAlpha * (1 - topAlpha);
        if (a <= 0.0001) return Color.FromArgb(0, 0, 0, 0);
        byte Mix(byte t, byte b) => (byte)Math.Round((t * topAlpha + b * bottomAlpha * (1 - topAlpha)) / a);
        return Color.FromArgb((byte)Math.Round(a * 255), Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
    }

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));

    private static Color Darken(Color c, double amount) => Color.FromRgb(
        (byte)(c.R * (1 - amount)), (byte)(c.G * (1 - amount)), (byte)(c.B * (1 - amount)));

    /// <summary>a → b, t = 0..1</summary>
    public static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static Color Saturate(Color c, double amount)
    {
        var (h, sat, l) = ToHsl(c);
        return FromHsl(h, Math.Clamp(sat + amount, 0, 1), l);
    }

    public static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-6) return (0, 0, l);
        var d = max - min;
        var sat = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        return (h * 60, sat, l);
    }

    private static RadialGradientBrush Radial(Point center, double rx, double ry, params (Color Color, double Offset)[] stops)
    {
        var brush = new RadialGradientBrush { Center = center, GradientOrigin = center, RadiusX = rx, RadiusY = ry };
        foreach (var (color, offset) in stops) brush.GradientStops.Add(new GradientStop(color, offset));
        return brush;
    }

    /// <summary>Mehrere Pinsel übereinander (z. B. Lichtschein + Glanz).</summary>
    private static DrawingBrush Layers(params Brush[] layers)
    {
        var group = new DrawingGroup();
        var rect = new RectangleGeometry(new Rect(0, 0, 1, 1));
        foreach (var layer in layers) group.Children.Add(new GeometryDrawing(layer, null, rect));
        return new DrawingBrush(group) { Stretch = Stretch.Fill };
    }

    private static Color Mix(Color a, Color b) => Color.FromRgb(
        (byte)((a.R + b.R) / 2), (byte)((a.G + b.G) / 2), (byte)((a.B + b.B) / 2));

    private static SolidColorBrush SolidBrush(Color c, byte alpha)
    {
        var brush = new SolidColorBrush(WithAlpha(c, alpha));
        brush.Freeze();
        return brush;
    }

    /// <summary>Verlauf in einem Winkel (0° = links→rechts, 90° = oben→unten).</summary>
    public static LinearGradientBrush Linear(double angleDegrees, params (Color Color, double Offset)[] stops)
    {
        var rad = angleDegrees * Math.PI / 180;
        var dx = Math.Cos(rad) / 2;
        var dy = Math.Sin(rad) / 2;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5 - dx, 0.5 - dy),
            EndPoint = new Point(0.5 + dx, 0.5 + dy),
        };
        foreach (var (color, offset) in stops) brush.GradientStops.Add(new GradientStop(color, offset));
        return brush;
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}

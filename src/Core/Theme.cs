using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Island.Core;

/// <summary>Baut aus den Einstellungen das Aussehen der Insel: Einfarbig, Verlauf oder Liquid Glass.</summary>
public static class Theme
{
    public const string Solid = "Einfarbig";
    public const string Gradient = "Verlauf";
    public const string Glass = "Liquid Glass";

    public static void Apply(Settings s, Border background, Border sheen, Border rim, UIElement shadow)
    {
        var c1 = ParseColor(s.Farbe1, Color.FromRgb(11, 11, 12));
        var c2 = ParseColor(s.Farbe2, Color.FromRgb(58, 28, 113));
        var opacity = Math.Clamp(s.Deckkraft, 0.3, 1.0);
        var style = s.Stil ?? Solid;

        Brush fill, sheenBrush, rimBrush;
        double rimThickness = 1;
        Color reference; // für die Entscheidung helle oder dunkle Schrift

        if (style == Glass)
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
        else if (style == Gradient)
        {
            var a = (byte)(255 * opacity);
            fill = Freeze(Linear(s.VerlaufWinkel, (WithAlpha(c1, a), 0), (WithAlpha(c2, a), 1)));
            sheenBrush = Freeze(Linear(90, (WithAlpha(Colors.White, 34), 0), (WithAlpha(Colors.White, 0), 0.5)));
            rimBrush = Freeze(Linear(90, (WithAlpha(Colors.White, 60), 0), (WithAlpha(Colors.White, 14), 1)));
            reference = Mix(c1, c2);
        }
        else
        {
            fill = Freeze(new SolidColorBrush(WithAlpha(c1, (byte)(255 * opacity))));
            sheenBrush = Freeze(Linear(90, (WithAlpha(Colors.White, 14), 0), (WithAlpha(Colors.White, 0), 0.45)));
            rimBrush = Freeze(Linear(90, (WithAlpha(Colors.White, 40), 0), (WithAlpha(Colors.White, 10), 1)));
            reference = c1;
        }

        background.Background = fill;
        sheen.Background = sheenBrush;
        rim.BorderBrush = rimBrush;
        rim.BorderThickness = new Thickness(rimThickness);

        // Kein echter Weichzeichner-Schatten (DropShadowEffect) – der macht Animationen ruckelig.
        background.Effect = null;
        shadow.Visibility = s.Schatten ? Visibility.Visible : Visibility.Collapsed;
        shadow.Opacity = style == Glass ? 0.6 : 1.0;

        ApplyTextColors(IsLight(reference), ParseColor(s.Akzent, Color.FromRgb(48, 209, 88)));
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
        res["IslandAccent"] = SolidBrush(accent, 255);
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

    private static Color Lighten(Color c, double amount) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * amount),
        (byte)(c.G + (255 - c.G) * amount),
        (byte)(c.B + (255 - c.B) * amount));

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

using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island.Core;

/// <summary>Kleine Helfer für Farben, Schrift und Styles.</summary>
public static class Ui
{
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public static Style Style(string key) => (Style)Application.Current.FindResource(key);

    /// <summary>Verknüpft eine Eigenschaft mit einer Theme-Farbe (ändert sich live mit den Einstellungen).
    /// Schlüssel: IslandText, IslandSubText, IslandHover, IslandTrack, IslandPlaceholder, IslandChip, IslandAccent</summary>
    public static T Themed<T>(this T element, DependencyProperty property, string key) where T : FrameworkElement
    {
        element.SetResourceReference(property, key);
        return element;
    }
}

/// <summary>Animationen. Laufen über die Grafikkarte und nur, solange sie gebraucht werden.</summary>
public static class Anim
{
    /// <summary>Federnd wie bei iOS (ca. 5 % Überschwingen).</summary>
    public static readonly IEasingFunction Spring = Freeze(new SpringEase { Damping = 0.72, Frequency = 1.55 });

    /// <summary>Weiche Feder fast ohne Überschwingen (zum Zuklappen).</summary>
    public static readonly IEasingFunction SpringSoft = Freeze(new SpringEase { Damping = 0.9, Frequency = 1.35 });

    public static readonly IEasingFunction Smooth = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

    /// <summary>1.0 = normal, 0.5 = doppelt so schnell. Kommt aus den Einstellungen.</summary>
    public static double Tempo { get; set; } = 1.0;

    public static void To(IAnimatable target, DependencyProperty property, double to, int ms,
                          IEasingFunction? ease = null, Action? done = null, int delayMs = 0, double? from = null)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(Math.Max(1, ms * Tempo)))
        {
            EasingFunction = ease,
            BeginTime = TimeSpan.FromMilliseconds(delayMs * Tempo),
        };
        if (from.HasValue) a.From = from.Value;
        if (done != null) a.Completed += (_, _) => done();
        target.BeginAnimation(property, a, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Bricht eine Animation ab und setzt den Wert direkt.</summary>
    public static void Set(IAnimatable target, DependencyProperty property, double value)
    {
        target.BeginAnimation(property, null);
        ((DependencyObject)target).SetValue(property, value);
    }

    private static IEasingFunction Freeze(EasingFunctionBase e)
    {
        e.Freeze();
        return e;
    }
}

/// <summary>Echte Feder-Physik (gedämpfte Schwingung) – fühlt sich natürlicher an als normale Kurven.</summary>
public sealed class SpringEase : EasingFunctionBase
{
    public static readonly DependencyProperty DampingProperty =
        DependencyProperty.Register(nameof(Damping), typeof(double), typeof(SpringEase), new PropertyMetadata(0.7));

    public static readonly DependencyProperty FrequencyProperty =
        DependencyProperty.Register(nameof(Frequency), typeof(double), typeof(SpringEase), new PropertyMetadata(1.4));

    public SpringEase() => EasingMode = EasingMode.EaseIn; // EaseInCore wird direkt benutzt

    /// <summary>0.1 = sehr wackelig, 1 = gar kein Überschwingen.</summary>
    public double Damping { get => (double)GetValue(DampingProperty); set => SetValue(DampingProperty, value); }

    /// <summary>Wie oft sie (theoretisch) schwingt während der Animation.</summary>
    public double Frequency { get => (double)GetValue(FrequencyProperty); set => SetValue(FrequencyProperty, value); }

    protected override double EaseInCore(double t)
    {
        if (t >= 1) return 1;
        var zeta = Math.Clamp(Damping, 0.05, 0.99);
        var omega = 2 * Math.PI * Frequency;
        var wd = omega * Math.Sqrt(1 - zeta * zeta);
        return 1 - Math.Exp(-zeta * omega * t) * (Math.Cos(wd * t) + zeta * omega / wd * Math.Sin(wd * t));
    }

    protected override Freezable CreateInstanceCore() => new SpringEase();
}

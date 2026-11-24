using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Island.Core;

namespace Island.Modules;

/// <summary>
/// Ruheanzeige: Was die zugeklappte Insel zeigt, wenn keine Musik läuft.
/// Datum, Uhr, Akku, Leistung (CPU/RAM) oder Minimal. Mit dem Mausrad über der Insel wechseln.
/// Aktualisiert nur, solange sie sichtbar ist (spart Akku).
/// </summary>
public sealed class ClockModule : IslandModule
{
    public static readonly string[] Modes = { "Datum", "Uhr", "Akku", "Leistung", "Minimal" };
    private static readonly CultureInfo German = new("de-CH");
    private static readonly SolidColorBrush LowBattery = Ui.Brush("#FF453A");

    public override string Name => "Ruheanzeige";
    public override string Icon => "";
    public override int Order => 100;          // ganz hinten = nur, wenn sonst nichts angezeigt wird
    public override bool WantsCompact => true;
    public override FrameworkElement? CompactView => _root;

    public override double CompactWidth => _mode switch
    {
        "Uhr" => 110,
        "Akku" => 170,
        "Leistung" => 214,
        "Minimal" => 64,
        _ => 176,
    };

    private readonly DispatcherTimer _timer = new();
    private string _mode = "Datum";

    private Grid _root = null!;
    private StackPanel _datePanel = null!, _batteryPanel = null!, _perfPanel = null!;
    private TextBlock _date = null!, _time = null!, _bigTime = null!;
    private TextBlock _batteryText = null!, _batteryBolt = null!, _batteryTime = null!, _cpu = null!, _ram = null!;
    private Border _batteryFill = null!;
    private Ellipse _dot = null!;

    protected override void OnInitialize()
    {
        _root = new Grid
        {
            Background = Brushes.Transparent, // damit das Mausrad überall auf der Insel greift
            ToolTip = "Mausrad: Anzeige wechseln",
        };

        // --- Datum + Uhr
        _date = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center }.Themed(TextBlock.ForegroundProperty, "IslandSubText");
        _time = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _datePanel = Row(_date, _time);

        // --- Nur Uhr
        _bigTime = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        // --- Akku (selbst gezeichnet, passt sich den Farben an)
        _batteryFill = new Border { CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 10 }
            .Themed(Border.BackgroundProperty, "IslandAccent");
        var batteryBody = new Border
        {
            Width = 25, Height = 13, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1.2),
            Padding = new Thickness(1.6), Child = _batteryFill, VerticalAlignment = VerticalAlignment.Center,
        }.Themed(Border.BorderBrushProperty, "IslandSubText");
        var batteryTip = new Border { Width = 2, Height = 5, CornerRadius = new CornerRadius(0, 1, 1, 0), Margin = new Thickness(1, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }
            .Themed(Border.BackgroundProperty, "IslandSubText");
        _batteryBolt = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 11, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, "IslandAccent");
        _batteryText = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _batteryTime = new TextBlock { FontSize = 12.5, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }.Themed(TextBlock.ForegroundProperty, "IslandSubText");
        _batteryPanel = Row(batteryBody, batteryTip, _batteryBolt, _batteryText, _batteryTime);

        // --- Leistung
        _cpu = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(5, 0, 0, 0), MinWidth = 36, VerticalAlignment = VerticalAlignment.Center };
        _ram = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(5, 0, 0, 0), MinWidth = 36, VerticalAlignment = VerticalAlignment.Center };
        _perfPanel = Row(Label("CPU"), _cpu, Label("RAM", 14), _ram);

        // --- Minimal: nur ein kleiner Punkt
        _dot = new Ellipse { Width = 8, Height = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _dot.SetResourceReference(Shape.FillProperty, "IslandAccent");

        _root.Children.Add(_datePanel);
        _root.Children.Add(_bigTime);
        _root.Children.Add(_batteryPanel);
        _root.Children.Add(_perfPanel);
        _root.Children.Add(_dot);

        _root.MouseWheel += OnMouseWheel;
        _root.IsVisibleChanged += (_, _) => RestartTimer();
        _timer.Tick += (_, _) => RestartTimer(); // RestartTimer aktualisiert auch die Anzeige

        ApplyMode(Context.Settings.RuheWidget, refresh: false);
    }

    public override void OnSettingsChanged()
    {
        if (Context.Settings.RuheWidget != _mode) ApplyMode(Context.Settings.RuheWidget, refresh: true);
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var index = Array.IndexOf(Modes, _mode);
        index = (index + (e.Delta < 0 ? 1 : -1) + Modes.Length) % Modes.Length;
        Context.Settings.RuheWidget = Modes[index];
        Context.Settings.SaveSoon();
        ApplyMode(Modes[index], refresh: true);
        e.Handled = true;
    }

    private void ApplyMode(string mode, bool refresh)
    {
        _mode = Array.IndexOf(Modes, mode) >= 0 ? mode : "Datum";
        _datePanel.Visibility = _mode == "Datum" ? Visibility.Visible : Visibility.Collapsed;
        _bigTime.Visibility = _mode == "Uhr" ? Visibility.Visible : Visibility.Collapsed;
        _batteryPanel.Visibility = _mode == "Akku" ? Visibility.Visible : Visibility.Collapsed;
        _perfPanel.Visibility = _mode == "Leistung" ? Visibility.Visible : Visibility.Collapsed;
        _dot.Visibility = _mode == "Minimal" ? Visibility.Visible : Visibility.Collapsed;

        if (_mode == "Leistung") Native.GetCpuPercent(); // erster Messwert als Startpunkt
        Update();
        RestartTimer();
        if (refresh)
        {
            Anim.To(_root, UIElement.OpacityProperty, 1, 260, Anim.Smooth, from: 0);
            Context.RequestCompactRefresh(); // Breite der Insel anpassen
        }
    }

    private void Update()
    {
        var now = DateTime.Now;
        switch (_mode)
        {
            case "Uhr":
                _bigTime.Text = now.ToString("HH:mm", German);
                break;

            case "Akku":
                _batteryTime.Text = now.ToString("HH:mm", German);
                if (Native.GetBattery() is { } battery)
                {
                    var (percent, charging) = battery;
                    _batteryText.Text = $"{percent} %";
                    _batteryFill.Width = Math.Max(2, 19.2 * percent / 100.0);
                    _batteryBolt.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
                    if (percent <= 20 && !charging) _batteryFill.Background = LowBattery;
                    else _batteryFill.SetResourceReference(Border.BackgroundProperty, "IslandAccent");
                }
                else
                {
                    _batteryText.Text = "kein Akku";
                    _batteryFill.Width = 0;
                    _batteryBolt.Visibility = Visibility.Collapsed;
                }
                break;

            case "Leistung":
                _cpu.Text = $"{Native.GetCpuPercent()} %";
                _ram.Text = $"{Native.GetRamPercent()} %";
                break;

            case "Minimal":
                break;

            default:
                _date.Text = now.ToString("ddd d. MMM", German);
                _time.Text = now.ToString("HH:mm", German);
                break;
        }
    }

    /// <summary>Nur aktualisieren, wenn man es sieht. Uhr: genau zur vollen Minute.</summary>
    private void RestartTimer()
    {
        _timer.Stop();
        if (!_root.IsVisible || _mode == "Minimal") return;

        _timer.Interval = _mode switch
        {
            "Leistung" => TimeSpan.FromSeconds(2),
            "Akku" => TimeSpan.FromSeconds(30),
            _ => TimeSpan.FromSeconds(60 - DateTime.Now.Second) + TimeSpan.FromMilliseconds(100),
        };
        Update();
        _timer.Start();
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in children) panel.Children.Add(c);
        return panel;
    }

    private static TextBlock Label(string text, double leftMargin = 0) => new TextBlock
    {
        Text = text,
        FontSize = 11.5,
        Margin = new Thickness(leftMargin, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
    }.Themed(TextBlock.ForegroundProperty, "IslandSubText");

    public override void Dispose() => _timer.Stop();
}

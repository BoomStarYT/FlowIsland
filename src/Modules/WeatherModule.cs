using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Island.Core;

namespace Island.Modules;

/// <summary>Wetter von heute + kleine Vorschau (Stunden und Tage). Daten: open-meteo.com, ohne Konto.</summary>
public sealed class WeatherModule : IslandModule
{
    private static readonly CultureInfo German = new("de-CH");

    public override string Name => "Wetter";
    public override string Icon => "";
    public override int Order => 20;
    public override double ExpandedHeight => 124;
    public override FrameworkElement? ExpandedView => _root;

    private Grid _root = null!;

    protected override void OnInitialize()
    {
        _root = new Grid();
        WeatherService.Updated += Rebuild;
        WeatherService.Start(Context.Settings);
        Rebuild();
    }

    public override void OnShown()
    {
        WeatherService.Start(Context.Settings); // ist es älter als 25 Min → neu laden
        Rebuild();
    }

    public override void OnSettingsChanged()
    {
        if (WeatherService.Configured(Context.Settings) && WeatherService.Current == null) WeatherService.Start(Context.Settings);
        Rebuild();
    }

    private void Rebuild()
    {
        _root.Children.Clear();
        _root.ColumnDefinitions.Clear();
        var s = Context.Settings;
        var data = WeatherService.Current;

        if (!WeatherService.Configured(s) || data == null)
        {
            var info = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var icon = new WeatherIcon(34) { Code = 2, HorizontalAlignment = HorizontalAlignment.Center };
            info.Children.Add(icon);
            var configured = WeatherService.Configured(s);
            info.Children.Add(new TextBlock
            {
                Text = !configured ? "Für welchen Ort?" : WeatherService.Error ?? "Wetter wird geladen …",
                FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 8),
            });
            if (!configured || WeatherService.Error != null)
            {
                var b = new Button { Content = configured ? "Nochmal versuchen" : "Ort festlegen", Style = Ui.Style("ChipButton"), HorizontalAlignment = HorizontalAlignment.Center };
                b.Click += (_, _) =>
                {
                    if (configured) _ = WeatherService.RefreshAsync();
                    else Context.OpenSettings("Verbindungen");
                };
                info.Children.Add(b);
            }
            _root.Children.Add(info);
            return;
        }

        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(146) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ---------- Links: jetzt
        var now = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(new WeatherIcon(38) { Code = data.Code, IsDay = data.IsDay, VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(new TextBlock
        {
            Text = $"{T(data.Temp)}°", FontSize = 36, FontWeight = FontWeights.Light,
            Margin = new Thickness(8, -4, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        now.Children.Add(top);
        now.Children.Add(new TextBlock { Text = WeatherService.Describe(data.Code), FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0) });
        var today = data.Days.FirstOrDefault();
        var detail = today != null ? $"H {T(today.Max)}°  T {T(today.Min)}°" : $"Gefühlt {T(data.Feels)}°";
        now.Children.Add(new TextBlock { Text = detail, FontSize = 11.5 }.Themed(TextBlock.ForegroundProperty, "IslandSubText"));
        var place = string.IsNullOrWhiteSpace(s.WetterOrtName) ? s.WetterOrt : s.WetterOrtName.Split(',')[0];
        var placeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
        placeRow.Children.Add(new TextBlock { Text = "\uE707", FontFamily = Ui.IconFont, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 4, 0) }
            .Themed(TextBlock.ForegroundProperty, "IslandSubText"));
        placeRow.Children.Add(new TextBlock { Text = place, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 120, VerticalAlignment = VerticalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, "IslandSubText"));
        now.Children.Add(placeRow);
        _root.Children.Add(now);

        // ---------- Rechts: nächste Stunden + Tage
        var right = new Grid { Margin = new Thickness(6, 0, 0, 0) };
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var hours = new UniformGridLite(5);
        foreach (var h in data.Hours.Take(5))
            hours.Add(Cell(h.Time.ToString("HH", German) + " Uhr", h.Code, h.IsDay, $"{T(h.Temp)}°"));
        right.Children.Add(hours.Panel);

        var line = new Border { Height = 1, Margin = new Thickness(4, 3, 4, 3) }.Themed(Border.BackgroundProperty, "IslandTrack");
        Grid.SetRow(line, 1);
        right.Children.Add(line);

        var days = new UniformGridLite(3);
        foreach (var d in data.Days.Skip(1).Take(3))
            days.Add(DayCell(d));
        Grid.SetRow(days.Panel, 2);
        right.Children.Add(days.Panel);

        Grid.SetColumn(right, 1);
        _root.Children.Add(right);
    }

    private static FrameworkElement Cell(string label, int code, bool day, string temp)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = label, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center }.Themed(TextBlock.ForegroundProperty, "IslandSubText"));
        stack.Children.Add(new WeatherIcon(20) { Code = code, IsDay = day, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 1) });
        stack.Children.Add(new TextBlock { Text = temp, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        return stack;
    }

    private static FrameworkElement DayCell(DayForecast d)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock { Text = d.Date.ToString("ddd", German), FontSize = 11, Width = 24, VerticalAlignment = VerticalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, "IslandSubText"));
        row.Children.Add(new WeatherIcon(18) { Code = d.Code, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        row.Children.Add(new TextBlock { Text = $"{T(d.Max)}°", FontSize = 11.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = $" {T(d.Min)}°", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, "IslandSubText"));
        return row;
    }

    /// <summary>Temperatur ohne "-0".</summary>
    private static string T(double v)
    {
        var r = (int)Math.Round(v, MidpointRounding.AwayFromZero);
        return r.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Einfache Zeile mit gleich breiten Spalten.</summary>
    private sealed class UniformGridLite
    {
        public Grid Panel { get; } = new();
        private int _i;
        public UniformGridLite(int columns)
        {
            for (int c = 0; c < columns; c++) Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        public void Add(FrameworkElement e)
        {
            Grid.SetColumn(e, _i++);
            Panel.Children.Add(e);
        }
    }

    public override void Dispose() => WeatherService.Updated -= Rebuild;
}

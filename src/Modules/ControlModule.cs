using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Island.Core;
using WRadios = global::Windows.Devices.Radios;

namespace Island.Modules;

/// <summary>Kleines Kontrollzentrum: Lautstärke, Helligkeit und Schnell-Knöpfe – im Look der Insel.</summary>
public sealed class ControlModule : IslandModule
{
    /// <summary>Alle Knöpfe, die man ins Kontrollzentrum legen kann (Einstellungen → Funktionen).</summary>
    public static readonly (string Id, string Glyph, string Label)[] Actions =
    {
        ("WLAN", "\uE701", "WLAN"), ("Bluetooth", "\uE702", "Bluetooth"), ("Flugmodus", "\uE709", "Flugmodus"),
        ("Stumm", "\uE74F", "Stumm"), ("Dunkelmodus", "\uE708", "Dunkelmodus"), ("Kamera", "\uE722", "Kamera"),
        ("Screenshot", "\uE91B", "Screenshot"), ("Aufnahme", "\uE714", "Aufnahme"), ("Sperren", "\uE72E", "Sperren"),
        ("NichtStoeren", "\uEA8F", "Nicht stören"), ("Nachtlicht", "\uE706", "Nachtlicht"), ("Energiesparen", "\uE83F", "Energie"),
        ("Desktop", "\uE80F", "Desktop"), ("Zwischenablage", "\uE77F", "Ablage"), ("Emojis", "\uE76E", "Emojis"),
        ("Projizieren", "\uE7F4", "Projizieren"), ("Rechner", "\uE8EF", "Rechner"), ("Explorer", "\uEC50", "Explorer"),
        ("Papierkorb", "\uE74D", "Papierkorb"), ("Einstellungen", "\uE713", "Einstellungen"),
    };

    public const int MaxButtons = 12;

    public override string Name => "Kontrollzentrum";
    public override string Icon => "\uE9E9";
    public override int Order => 25;
    public override double ExpandedHeight => Math.Max(Context.Settings.KontrollRegler ? 124 : 60, Rows * 60 + 4);
    public override FrameworkElement? ExpandedView => _root;

    private int Columns => Context.Settings.KontrollRegler ? 4 : 6;
    private int Rows => Math.Max(1, (int)Math.Ceiling(ButtonIds().Count / (double)Columns));

    private Grid _root = null!;
    private PillSlider _volume = null!, _brightness = null!;
    private readonly Dictionary<string, Toggle> _buttons = new();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private bool _shown;
    private string _signature = "";

    protected override void OnInitialize()
    {
        _root = new Grid { Background = Brushes.Transparent };
        // Mausrad irgendwo im Kontrollzentrum = Lautstärke
        _root.MouseWheel += (_, e) =>
        {
            if (_brightness != null && _brightness.IsMouseOver) return;
            var v = Math.Clamp((AudioControl.Volume ?? 0) + (e.Delta > 0 ? 0.04 : -0.04), 0, 1);
            AudioControl.Volume = v;
            if (_volume != null) _volume.Value = v;
            RefreshAudio();
            e.Handled = true;
        };
        _poll.Tick += (_, _) => RefreshAudio();
        Rebuild();
    }

    private List<string> ButtonIds() => Context.Settings.KontrollKnoepfe
        .Where(id => Actions.Any(a => a.Id == id)).Distinct().Take(MaxButtons).ToList();

    public override void OnSettingsChanged()
    {
        var sig = string.Join(",", ButtonIds()) + Context.Settings.KontrollRegler;
        if (sig == _signature) return;
        Rebuild();
        Context.RequestLayoutRefresh();
        if (_shown) OnShown();
    }

    private void Rebuild()
    {
        _signature = string.Join(",", ButtonIds()) + Context.Settings.KontrollRegler;
        _root.Children.Clear();
        _root.ColumnDefinitions.Clear();
        _buttons.Clear();
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // ---------- Knöpfe
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        for (int c = 0; c < Columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < Rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });

        var ids = ButtonIds();
        for (int i = 0; i < ids.Count; i++)
        {
            var (id, glyph, label) = Actions.First(a => a.Id == ids[i]);
            var t = new Toggle(glyph, label, () => Run(id));
            t.RightClick = SettingsPage(id) is string page ? () => IslandContext.Open(page) : null;
            Grid.SetColumn(t, i % Columns);
            Grid.SetRow(t, i / Columns);
            grid.Children.Add(t);
            _buttons[id] = t;
        }
        if (ids.Count == 0)
            grid.Children.Add(new TextBlock { Text = "Knöpfe wählst du in den Einstellungen → Funktionen", HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap }
                .Themed(TextBlock.ForegroundProperty, "IslandSubText"));
        _root.Children.Add(grid);

        // ---------- Regler
        if (Context.Settings.KontrollRegler)
        {
            var height = Math.Max(112, Rows * 60 - 8);
            var sliders = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            _volume = new PillSlider("\uE767", "Lautstärke") { Height = height };
            _volume.ValueChanged += v => { AudioControl.Volume = v; RefreshAudio(); };
            _brightness = new PillSlider("\uE706", "Helligkeit") { Margin = new Thickness(8, 0, 0, 0), Height = height };
            _brightness.ValueChanged += v => BrightnessControl.Set((int)Math.Round(v * 100));
            sliders.Children.Add(_volume);
            sliders.Children.Add(_brightness);
            Grid.SetColumn(sliders, 1);
            _root.Children.Add(sliders);
        }
        else
        {
            _volume = null!;
            _brightness = null!;
        }
    }

    private static string? SettingsPage(string id) => id switch
    {
        "WLAN" => "ms-settings:network-wifi",
        "Bluetooth" => "ms-settings:bluetooth",
        "Flugmodus" => "ms-settings:network-airplanemode",
        "Stumm" => "ms-settings:sound",
        "Dunkelmodus" => "ms-settings:colors",
        "Nachtlicht" => "ms-settings:nightlight",
        "Energiesparen" => "ms-settings:batterysaver",
        "NichtStoeren" => "ms-settings:notifications",
        "Projizieren" => "ms-settings:display",
        _ => null,
    };

    private void Run(string id)
    {
        const byte WIN = 0x5B, ALT = 0x12;
        switch (id)
        {
            case "WLAN": _ = ToggleRadio(WRadios.RadioKind.WiFi, "ms-settings:network-wifi"); break;
            case "Bluetooth": _ = ToggleRadio(WRadios.RadioKind.Bluetooth, "ms-settings:bluetooth"); break;
            case "Flugmodus": _ = ToggleFlight(); break;
            case "Stumm": AudioControl.Muted = !(AudioControl.Muted ?? false); RefreshAudio(); break;
            case "Dunkelmodus":
                var dark = Native.ToggleDarkMode();
                if (dark == null) IslandContext.Open("ms-settings:colors");
                else if (_buttons.TryGetValue("Dunkelmodus", out var b)) b.On = dark.Value;
                break;
            case "Kamera": IslandContext.Open("microsoft.windows.camera:"); break;
            case "Screenshot": IslandContext.Open("ms-screenclip:"); break;
            case "Aufnahme": Native.SendKeys(WIN, ALT, 0x52); break;           // Win + Alt + R
            case "Sperren": LockWorkStation(); break;
            case "NichtStoeren": IslandContext.Open("ms-settings:quiethours"); break;
            case "Nachtlicht": IslandContext.Open("ms-settings:nightlight"); break;
            case "Energiesparen": IslandContext.Open("ms-settings:batterysaver"); break;
            case "Desktop": Native.SendKeys(WIN, 0x44); break;                  // Win + D
            case "Zwischenablage": Native.SendKeys(WIN, 0x56); break;           // Win + V
            case "Emojis": Native.SendKeys(WIN, 0xBE); break;                   // Win + .
            case "Projizieren": Native.SendKeys(WIN, 0x50); break;              // Win + P
            case "Rechner": IslandContext.Open("calc.exe"); break;
            case "Explorer": IslandContext.Open("explorer.exe"); break;
            case "Papierkorb": IslandContext.Open("shell:RecycleBinFolder"); break;
            case "Einstellungen": IslandContext.Open("ms-settings:"); break;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    public override void OnShown()
    {
        _shown = true;
        RefreshAudio();
        if (_buttons.TryGetValue("Dunkelmodus", out var dm)) dm.On = Native.IsDarkMode() ?? false;
        if (_buttons.ContainsKey("WLAN") || _buttons.ContainsKey("Bluetooth") || _buttons.ContainsKey("Flugmodus")) _ = RefreshRadios();
        if (_brightness != null) _ = RefreshBrightness();
        _poll.Start(); // falls die Lautstärke woanders geändert wird
    }

    public override void OnHidden()
    {
        _shown = false;
        _poll.Stop();
    }

    private void RefreshAudio()
    {
        var v = AudioControl.Volume;
        var muted = AudioControl.Muted ?? false;
        if (_buttons.TryGetValue("Stumm", out var mute)) mute.On = muted;
        if (_volume == null) return;
        if (!_volume.Dragging) _volume.Value = v ?? 0;
        _volume.IsEnabled = v != null;
        _volume.Glyph = muted || (v ?? 0) < 0.01 ? "\uE74F" : (v ?? 0) < 0.34 ? "\uE993" : (v ?? 0) < 0.67 ? "\uE994" : "\uE995";
    }

    private async Task RefreshBrightness()
    {
        var b = await BrightnessControl.GetAsync();
        if (!_shown || _brightness == null) return;
        _brightness.IsEnabled = b != null;
        _brightness.ToolTip = b == null ? "Dieser Bildschirm lässt sich nicht über Windows dimmen" : "Helligkeit";
        if (b is int value && !_brightness.Dragging) _brightness.Value = value / 100.0;
    }

    private async Task RefreshRadios()
    {
        var radios = await RadioControl.GetAsync();
        WRadios.Radio? wifi = null, bt = null;
        foreach (var r in radios)
        {
            if (r.Kind == WRadios.RadioKind.WiFi) wifi ??= r;
            if (r.Kind == WRadios.RadioKind.Bluetooth) bt ??= r;
        }
        if (_buttons.TryGetValue("WLAN", out var w)) w.On = wifi?.State == WRadios.RadioState.On;
        if (_buttons.TryGetValue("Bluetooth", out var bb)) bb.On = bt?.State == WRadios.RadioState.On;
        if (_buttons.TryGetValue("Flugmodus", out var f)) f.On = await RadioControl.FlightActiveAsync();
    }

    private async Task ToggleRadio(WRadios.RadioKind kind, string fallback)
    {
        if (!await RadioControl.ToggleAsync(kind)) IslandContext.Open(fallback);
        await Task.Delay(300);
        await RefreshRadios();
    }

    private async Task ToggleFlight()
    {
        if (!await RadioControl.ToggleFlightAsync()) IslandContext.Open("ms-settings:network-airplanemode");
        await Task.Delay(300);
        await RefreshRadios();
    }

    public override void Dispose() => _poll.Stop();

    // ================================================================== Bausteine

    /// <summary>Runder Knopf mit Beschriftung. On = gefüllt in der Akzentfarbe.</summary>
    private sealed class Toggle : Button
    {
        private readonly Border _circle;
        private readonly TextBlock _icon;
        private bool _on;
        public Action? RightClick { get; set; }

        public Toggle(string glyph, string label, Action click)
        {
            Style = Ui.Style("TileButton");
            Width = 62;
            Height = 58;
            ToolTip = label;
            _icon = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _circle = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Child = _icon, HorizontalAlignment = HorizontalAlignment.Center }
                .Themed(Border.BackgroundProperty, "IslandChip");
            var text = new TextBlock { Text = label, FontSize = 9.5, Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 60 }
                .Themed(TextBlock.ForegroundProperty, "IslandSubText");
            var stack = new StackPanel();
            stack.Children.Add(_circle);
            stack.Children.Add(text);
            Content = stack;
            Click += (_, _) => click();
            MouseRightButtonUp += (_, e) =>
            {
                if (RightClick == null) return;
                e.Handled = true;
                RightClick();
            };
        }

        public bool On
        {
            get => _on;
            set
            {
                _on = value;
                if (value)
                {
                    _circle.SetResourceReference(Border.BackgroundProperty, "IslandAccent");
                    _icon.Foreground = Brushes.White;
                }
                else
                {
                    _circle.SetResourceReference(Border.BackgroundProperty, "IslandChip");
                    _icon.SetResourceReference(TextBlock.ForegroundProperty, "IslandText");
                }
            }
        }
    }
}

/// <summary>Senkrechter, dicker Regler wie im iPhone-Kontrollzentrum (ziehen, klicken oder Mausrad).</summary>
public sealed class PillSlider : Border
{
    private readonly Border _fill;
    private readonly TextBlock _icon;
    private double _value;

    public event Action<double>? ValueChanged;
    public bool Dragging { get; private set; }

    public PillSlider(string glyph, string tooltip)
    {
        Width = 46;
        Height = 112;
        CornerRadius = new CornerRadius(16);
        ToolTip = tooltip;
        Cursor = Cursors.Hand;
        this.Themed(BackgroundProperty, "IslandChip");

        _fill = new Border { VerticalAlignment = VerticalAlignment.Bottom, Height = 0 }.Themed(Border.BackgroundProperty, "IslandAccent");
        _icon = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 10) };
        var grid = new Grid();
        grid.Children.Add(_fill);
        grid.Children.Add(_icon);
        Child = grid;

        SizeChanged += (_, _) =>
        {
            Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 16, 16);
            Layout(false);
        };
        MouseLeftButtonDown += (_, e) =>
        {
            Dragging = true;
            CaptureMouse();
            SetFromMouse(e);
            e.Handled = true;
        };
        MouseMove += (_, e) => { if (Dragging) SetFromMouse(e); };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!Dragging) return;
            Dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        };
        LostMouseCapture += (_, _) => Dragging = false;
        MouseWheel += (_, e) =>
        {
            Value = Math.Clamp(_value + (e.Delta > 0 ? 0.05 : -0.05), 0, 1);
            ValueChanged?.Invoke(_value);
            e.Handled = true;
        };
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.35;
    }

    public string Glyph { set => _icon.Text = value; }

    public double Value
    {
        get => _value;
        set
        {
            var animate = Math.Abs(value - _value) > 0.02 && !Dragging;
            _value = Math.Clamp(value, 0, 1);
            Layout(animate);
        }
    }

    private void Layout(bool animate)
    {
        var target = ActualHeight * _value;
        if (animate) Anim.To(_fill, HeightProperty, target, 300, Anim.Smooth);
        else Anim.Set(_fill, HeightProperty, target);
    }

    private void SetFromMouse(MouseEventArgs e)
    {
        var h = ActualHeight;
        if (h <= 0) return;
        _value = Math.Clamp(1 - e.GetPosition(this).Y / h, 0, 1);
        Layout(false);
        ValueChanged?.Invoke(_value);
    }
}

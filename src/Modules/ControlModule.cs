using System;
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
    public override string Name => "Kontrollzentrum";
    public override string Icon => "";
    public override int Order => 25;
    public override double ExpandedHeight => 124;
    public override FrameworkElement? ExpandedView => _root;

    private Grid _root = null!;
    private PillSlider _volume = null!, _brightness = null!;
    private Toggle _wifi = null!, _bluetooth = null!, _flight = null!, _mute = null!;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private bool _shown;

    protected override void OnInitialize()
    {
        _root = new Grid();
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // ---------- Knöpfe (4 × 2)
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        for (int c = 0; c < 4; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());

        _wifi = new Toggle("", "WLAN", () => _ = ToggleRadio(WRadios.RadioKind.WiFi, "ms-settings:network-wifi"));
        _bluetooth = new Toggle("", "Bluetooth", () => _ = ToggleRadio(WRadios.RadioKind.Bluetooth, "ms-settings:bluetooth"));
        _flight = new Toggle("", "Flugmodus", () => _ = ToggleFlight());
        _mute = new Toggle("", "Stumm", () =>
        {
            AudioControl.Muted = !(AudioControl.Muted ?? false);
            RefreshAudio();
        });
        var camera = new Toggle("", "Kamera", () => IslandContext.Open("microsoft.windows.camera:"));
        var shot = new Toggle("", "Screenshot", () => IslandContext.Open("ms-screenclip:"));
        var focus = new Toggle("", "Fokus", () => IslandContext.Open("ms-settings:quiethours"));
        var lockPc = new Toggle("", "Sperren", () => LockWorkStation());

        // Rechtsklick = passende Windows-Einstellungen
        _wifi.RightClick = () => IslandContext.Open("ms-settings:network-wifi");
        _bluetooth.RightClick = () => IslandContext.Open("ms-settings:bluetooth");
        _flight.RightClick = () => IslandContext.Open("ms-settings:network-airplanemode");
        _mute.RightClick = () => IslandContext.Open("ms-settings:sound");

        var all = new[] { _wifi, _bluetooth, _flight, _mute, camera, shot, focus, lockPc };
        for (int i = 0; i < all.Length; i++)
        {
            Grid.SetColumn(all[i], i % 4);
            Grid.SetRow(all[i], i / 4);
            grid.Children.Add(all[i]);
        }
        _root.Children.Add(grid);

        // ---------- Regler
        var sliders = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 2, 0) };
        _volume = new PillSlider("", "Lautstärke");
        _volume.ValueChanged += v => AudioControl.Volume = v;
        _brightness = new PillSlider("", "Helligkeit") { Margin = new Thickness(8, 0, 0, 0) };
        _brightness.ValueChanged += v => BrightnessControl.Set((int)Math.Round(v * 100));
        sliders.Children.Add(_volume);
        sliders.Children.Add(_brightness);
        Grid.SetColumn(sliders, 1);
        _root.Children.Add(sliders);

        // Mausrad irgendwo im Kontrollzentrum = Lautstärke
        _root.Background = Brushes.Transparent;
        _root.MouseWheel += (_, e) =>
        {
            if (_brightness.IsMouseOver) return;
            var v = Math.Clamp((AudioControl.Volume ?? 0) + (e.Delta > 0 ? 0.04 : -0.04), 0, 1);
            AudioControl.Volume = v;
            _volume.Value = v;
            e.Handled = true;
        };

        _poll.Tick += (_, _) => RefreshAudio();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    public override void OnShown()
    {
        _shown = true;
        RefreshAudio();
        _ = RefreshRadios();
        _ = RefreshBrightness();
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
        if (!_volume.Dragging) _volume.Value = v ?? 0;
        _volume.IsEnabled = v != null;
        var muted = AudioControl.Muted ?? false;
        _mute.On = muted;
        _volume.Glyph = muted || (v ?? 0) < 0.01 ? "" : (v ?? 0) < 0.34 ? "" : (v ?? 0) < 0.67 ? "" : "";
    }

    private async Task RefreshBrightness()
    {
        var b = await BrightnessControl.GetAsync();
        if (!_shown) return;
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
        _wifi.On = wifi?.State == WRadios.RadioState.On;
        _bluetooth.On = bt?.State == WRadios.RadioState.On;
        _flight.On = await RadioControl.FlightActiveAsync();
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

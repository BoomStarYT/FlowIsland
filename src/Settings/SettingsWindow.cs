using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Island.Core;
using Microsoft.Win32;

namespace Island;

/// <summary>
/// Einstellungsfenster im Stil der macOS-Systemeinstellungen.
/// Fast alles wird sofort an der Insel übernommen (Live-Vorschau).
/// </summary>
public sealed class SettingsWindow : Window
{
    // Farbpaletten in Gruppen – alle Töne passen innerhalb einer Reihe zueinander
    private static readonly (string Name, string[] Colors)[] SwatchGroups =
    {
        ("Neutral", new[] { "#0A0A0C", "#1C1C1E", "#2C2C2E", "#48484A", "#8E8E93", "#E5E5EA", "#F5F5F7" }),
        ("Pastell", new[] { "#FFC8DD", "#FFD6A5", "#FDFFB6", "#CAFFBF", "#9BF6FF", "#A0C4FF", "#BDB2FF" }),
        ("Kräftig", new[] { "#FF453A", "#FF9F0A", "#FFD60A", "#30D158", "#64D2FF", "#0A84FF", "#BF5AF2" }),
        ("Tief", new[] { "#2B1055", "#3A1C71", "#0F2027", "#134E5E", "#1A2980", "#3E2723", "#5B0E2D" }),
    };

    // Abgestimmte Farbkombis (Hintergrund + Akzent)
    private static readonly (string Name, string Stil, string F1, string F2, double Winkel, double Deckkraft, string Akzent)[] Presets =
    {
        ("Mitternacht", Theme.Solid, "#0A0A0C", "#0A0A0C", 135, 1.0, "#30D158"),
        ("Liquid Glass", Theme.Glass, "#16161D", "#16161D", 135, 0.5, "#64D2FF"),
        ("Frost", Theme.Glass, "#F2F2F7", "#F2F2F7", 135, 0.6, "#0A84FF"),
        ("Graphit", Theme.Gradient, "#232526", "#414345", 135, 1.0, "#FFD60A"),
        ("Nordlicht", Theme.Gradient, "#0F2027", "#2C5364", 135, 1.0, "#5EEAD4"),
        ("Matcha", Theme.Gradient, "#134E5E", "#71B280", 135, 1.0, "#E8FFD6"),
        ("Aubergine", Theme.Gradient, "#2B1055", "#7597DE", 135, 1.0, "#FFC8DD"),
        ("Lavendel", Theme.Gradient, "#A18CD1", "#FBC2EB", 135, 1.0, "#5E5CE6"),
        ("Pfirsich", Theme.Gradient, "#FFDDE1", "#EE9CA7", 135, 1.0, "#C2185B"),
        ("Rosé Gold", Theme.Gradient, "#B76E79", "#E8C4B8", 135, 1.0, "#7A2E3A"),
        ("Sunset", Theme.Gradient, "#FF5E62", "#FF9966", 135, 1.0, "#FFFFFF"),
        ("Ozean", Theme.Gradient, "#2E3192", "#1BFFFF", 135, 1.0, "#FFFFFF"),
        ("Xiaomi", Theme.Gradient, "#FF6900", "#FF3D00", 135, 1.0, "#FFFFFF"),
        ("Espresso", Theme.Gradient, "#3E2723", "#8D6E63", 135, 1.0, "#FFCC80"),
    };

    private static readonly Dictionary<string, string> ModuleDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Musik"] = "Song, Cover, Fortschritt und Steuerung",
        ["Apps"] = "Schnellstart-Kacheln für Apps und Ordner",
        ["Tasten"] = "Spickzettel für Tastenkürzel",
        ["Ruheanzeige"] = "Datum, Uhr, Akku oder Leistung, wenn keine Musik läuft",
    };

    private readonly Settings _s;
    private readonly MainWindow _island;
    private readonly ContentControl _page = new() { Focusable = false };
    private readonly ScrollViewer _scroll;
    private readonly Border _restartBanner;
    private readonly StackPanel _nav = new();
    private string _currentPage = "Aussehen";
    private int _groupCounter;

    // Sanftes, langsameres Scrollen
    private const double WheelFactor = 0.3;   // 1 Mausrad-Raste ≈ 36 Pixel (Windows-Standard wäre ≈ 48 und ruckartig)
    private const double ArrowStep = 56;      // Pfeiltaste hoch/runter
    private double _scrollTarget;
    private bool _scrollAnimating;

    public SettingsWindow(Settings settings, MainWindow island)
    {
        _s = settings;
        _island = island;

        Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
            new Uri("/DynamicIsland;component/Settings/SettingsStyles.xaml", UriKind.Relative)));

        Title = "Dynamic Island – Einstellungen";
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/icon.ico")); }
        catch (Exception ex) { App.Log(ex); }
        Width = 800;
        Height = 620;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Foreground = Brushes.White;
        UseLayoutRounding = true;

        // ---------- Seitenleiste
        var side = new Border { CornerRadius = new CornerRadius(14, 0, 0, 14) }.Themed(Border.BackgroundProperty, "S.Side");
        side.MouseLeftButtonDown += DragWindow;
        var sideStack = new StackPanel();

        var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 16, 0, 20) };
        dots.Children.Add(WindowDot("#FF5F57", "", Close));
        dots.Children.Add(WindowDot("#FEBC2E", "", () => WindowState = WindowState.Minimized));
        dots.Children.Add(WindowDot("#3A3A3C", "", null));
        sideStack.Children.Add(dots);

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 0, 0, 18) };
        brand.Children.Add(new Border
        {
            Width = 30, Height = 12, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Black, BorderBrush = Ui.Brush("#55FFFFFF"), BorderThickness = new Thickness(1),
        });
        brand.Children.Add(new TextBlock { Text = "Dynamic Island", FontSize = 14.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0) });
        sideStack.Children.Add(brand);

        AddNav("Aussehen", "");
        AddNav("Verhalten", "");
        AddNav("Funktionen", "");
        AddNav("Verbindungen", "");
        AddNav("Apps", "");
        AddNav("Tastenkürzel", "");
        AddNav("Info", "");
        sideStack.Children.Add(_nav);
        side.Child = sideStack;

        // ---------- Inhalt
        var dragBar = new Border { Background = Brushes.Transparent, Height = 40 };
        dragBar.MouseLeftButtonDown += DragWindow;

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, // Mausrad geht trotzdem
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _page,
            Padding = new Thickness(30, 0, 30, 24),
            Focusable = false,
        };

        _restartBanner = BuildRestartBanner();

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_scroll, 1);
        Grid.SetRow(_restartBanner, 2);
        content.Children.Add(dragBar);
        content.Children.Add(_scroll);
        content.Children.Add(_restartBanner);

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(214) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(content, 1);
        layout.Children.Add(side);
        layout.Children.Add(content);

        Content = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            BorderBrush = Ui.Brush("#FF3A3A3C"),
            Child = layout,
        }.Themed(Border.BackgroundProperty, "S.Bg");

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closing += (_, _) => { StopScrollAnimation(); _s.Save(); };

        // Mausrad: langsamer und weich gleitend
        _scroll.PreviewMouseWheel += OnScrollWheel;
        // Pfeiltasten, Bild auf/ab, Pos1/Ende
        PreviewKeyDown += OnScrollKeys;
        // Klick auf freie Fläche → Tastatur zurück ans Fenster (damit die Pfeiltasten scrollen)
        _scroll.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (FindParent<TextBox>(e.OriginalSource as DependencyObject) == null) Focus();
        };
        Focusable = true;
        Loaded += (_, _) => Focus();

        // Neue RAM-Messwerte → Seite aktualisieren
        Action onMeasured = () => { if (_currentPage == "Verhalten") RebuildPage(); };
        _island.Ram.Measured += onMeasured;
        Closed += (_, _) => _island.Ram.Measured -= onMeasured;

        ShowPage("Aussehen");
    }

    // ================================================================== Navigation

    private void AddNav(string name, string glyph)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 14, Width = 22, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = name, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });

        var item = new RadioButton { Content = content, GroupName = "nav", Tag = name }.Themed(StyleProperty, "NavItem");
        item.Checked += (_, _) => { if (_currentPage != name) ShowPage(name); };
        _nav.Children.Add(item);
    }

    private void ShowPage(string name, bool keepScroll = false)
    {
        var offset = _scroll.VerticalOffset;
        _currentPage = name;
        foreach (RadioButton rb in _nav.Children) rb.IsChecked = (string)rb.Tag == name;

        var panel = new StackPanel();
        try
        {
            switch (name)
            {
                case "Aussehen": BuildAppearance(panel); break;
                case "Verhalten": BuildBehavior(panel); break;
                case "Funktionen": BuildFeatures(panel); break;
                case "Verbindungen": BuildConnections(panel); break;
                case "Apps": BuildApps(panel); break;
                case "Tastenkürzel": BuildKeys(panel); break;
                default: BuildInfo(panel); break;
            }
        }
        catch (Exception ex) { App.Log(ex); }

        _page.Content = panel;

        if (keepScroll)
        {
            Dispatcher.InvokeAsync(() => _scroll.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
        }
        else
        {
            StopScrollAnimation();
            _scroll.ScrollToTop();
            var slide = new TranslateTransform(0, 10);
            panel.RenderTransform = slide;
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
    }

    private void RebuildPage() => ShowPage(_currentPage, keepScroll: true);

    // ================================================================== Scrollen

    private void OnScrollWheel(object sender, MouseWheelEventArgs e)
    {
        // Die Schriftarten-Liste scrollt selbst
        if (FindParent<ListBox>(e.OriginalSource as DependencyObject) != null) return;
        e.Handled = true;
        ScrollBy(-e.Delta * WheelFactor);
    }

    private void OnScrollKeys(object sender, KeyEventArgs e)
    {
        // Beim Tippen in Textfeldern oder in der Schriftliste nicht dazwischenfunken
        if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ListBoxItem || Keyboard.FocusedElement is ListBox) return;

        switch (e.Key)
        {
            case Key.Down: ScrollBy(ArrowStep); break;
            case Key.Up: ScrollBy(-ArrowStep); break;
            case Key.PageDown: case Key.Space: ScrollBy(_scroll.ViewportHeight * 0.85); break;
            case Key.PageUp: ScrollBy(-_scroll.ViewportHeight * 0.85); break;
            case Key.Home: ScrollBy(-_scroll.ScrollableHeight - 1000); break;
            case Key.End: ScrollBy(_scroll.ScrollableHeight + 1000); break;
            case Key.Right: SwitchPage(+1); break;   // Bonus: ← → wechselt den Bereich
            case Key.Left: SwitchPage(-1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void SwitchPage(int direction)
    {
        var names = _nav.Children.OfType<RadioButton>().Select(rb => (string)rb.Tag).ToList();
        var index = names.IndexOf(_currentPage);
        var next = Math.Clamp(index + direction, 0, names.Count - 1);
        if (next != index) ShowPage(names[next]);
    }

    /// <summary>Ziel setzen; die Ansicht gleitet weich dorthin (jedes Bild ein Stück näher).</summary>
    private void ScrollBy(double delta)
    {
        if (!_scrollAnimating) _scrollTarget = _scroll.VerticalOffset;
        _scrollTarget = Math.Clamp(_scrollTarget + delta, 0, _scroll.ScrollableHeight);
        if (_scrollAnimating) return;
        _scrollAnimating = true;
        CompositionTarget.Rendering += OnScrollFrame;
    }

    private void OnScrollFrame(object? sender, EventArgs e)
    {
        var current = _scroll.VerticalOffset;
        var diff = _scrollTarget - current;
        if (Math.Abs(diff) < 0.5)
        {
            _scroll.ScrollToVerticalOffset(_scrollTarget);
            StopScrollAnimation();
            return;
        }
        _scroll.ScrollToVerticalOffset(current + diff * 0.18);
    }

    private void StopScrollAnimation()
    {
        if (!_scrollAnimating) return;
        _scrollAnimating = false;
        CompositionTarget.Rendering -= OnScrollFrame;
    }

    private static T? FindParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T match) return match;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }

    // ================================================================== Seiten

    private void BuildAppearance(StackPanel p)
    {
        p.Children.Add(Header("Aussehen"));
        p.Children.Add(Sub("Änderungen siehst du sofort an der Insel – fahr mit der Maus drüber zum Testen."));

        p.Children.Add(SectionTitle("Vorlagen"));
        var presets = new WrapPanel { Margin = new Thickness(8, 10, 8, 4) };
        foreach (var preset in Presets) presets.Children.Add(PresetButton(preset));
        var random = Btn("\uE8B1  Überrasch mich", "Secondary", () =>
        {
            var (f1, f2, angle, accent) = Theme.RandomPalette();
            _s.Stil = Theme.Gradient;
            _s.Farbe1 = f1;
            _s.Farbe2 = f2;
            _s.VerlaufWinkel = angle;
            _s.Deckkraft = 1.0;
            _s.Akzent = accent;
            _s.NotifyChanged();
            RebuildPage();
        });
        random.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Variable Text, Segoe UI");
        p.Children.Add(Card(presets, Row("Zufällige Farbkombi", "Erfindet eine harmonische Kombi – so oft klicken, bis sie dir gefällt", random)));

        p.Children.Add(SectionTitle("Stil"));
        var style = new List<UIElement>
        {
            Row("Design", null, Segmented(new[] { Theme.Solid, Theme.Gradient, Theme.Glass }, _s.Stil, v =>
            {
                _s.Stil = v;
                _s.NotifyChanged();
                RebuildPage();
            }), glyph: ""),
        };
        if (_s.Stil == Theme.Glass)
        {
            style.Add(Info("Glas-Look mit Lichtkante und Spiegelung. Ein echter Unschärfe-Effekt (Blur) hinter der Insel " +
                           "ist in Windows zusammen mit flüssigen Animationen leider nicht sauber machbar."));
        }

        var firstColorLabel = _s.Stil == Theme.Gradient ? "Farbe 1" : _s.Stil == Theme.Glass ? "Glas-Tönung" : "Farbe";
        style.Add(Row(firstColorLabel, null, null, below: ColorPicker(_s.Farbe1, v => { _s.Farbe1 = v; _s.NotifyChanged(); })));
        if (_s.Stil == Theme.Gradient)
        {
            style.Add(Row("Farbe 2", null, null, below: ColorPicker(_s.Farbe2, v => { _s.Farbe2 = v; _s.NotifyChanged(); })));
            style.Add(Row("Winkel", null, SliderControl(0, 360, _s.VerlaufWinkel, v => $"{v:0}°",
                v => { _s.VerlaufWinkel = v; _s.NotifyChanged(); }, 5)));
        }
        style.Add(Row(_s.Stil == Theme.Glass ? "Glas-Dichte" : "Deckkraft", null,
            SliderControl(30, 100, _s.Deckkraft * 100, v => $"{v:0} %", v => { _s.Deckkraft = v / 100; _s.NotifyChanged(); }, 5)));
        p.Children.Add(Card(style.ToArray()));

        p.Children.Add(SectionTitle("Details"));
        p.Children.Add(Card(
            Row("Akzentfarbe", "Equalizer und Fortschrittsbalken", null,
                below: ColorPicker(_s.Akzent, v => { _s.Akzent = v; _s.NotifyChanged(); })),
            Row("Grösse", null, SliderControl(80, 130, _s.Groesse * 100, v => $"{v:0} %",
                v => { _s.Groesse = v / 100; _s.NotifyChanged(); }, 5)),
            Row("Rundung", "Ecken, wenn aufgeklappt", SliderControl(10, 36, _s.Rundung, v => $"{v:0}",
                v => { _s.Rundung = v; _s.NotifyChanged(); }, 1)),
            Row("Schatten", null, Toggle(_s.Schatten, v => { _s.Schatten = v; _s.NotifyChanged(); }))));

        p.Children.Add(SectionTitle("Schrift"));
        p.Children.Add(Card(Row("Schriftart", "Gilt für alle Texte der Insel", null, below: FontPicker(), glyph: "")));
    }

    private void BuildBehavior(StackPanel p)
    {
        p.Children.Add(Header("Verhalten"));
        p.Children.Add(Sub("Wie die Insel aufgeht, wie schnell sie reagiert und wo sie sitzt."));

        p.Children.Add(SectionTitle("Öffnen & Schliessen"));
        p.Children.Add(Card(
            Row("Öffnen mit", null, Segmented(new[] { "Maus drüber", "Klick" }, _s.OeffnenPerKlick ? "Klick" : "Maus drüber",
                v => { _s.OeffnenPerKlick = v == "Klick"; _s.NotifyChanged(); })),
            Row("Verzögerung beim Öffnen", null, SliderControl(0, 600, _s.OeffnenVerzoegerungMs, v => $"{v:0} ms",
                v => { _s.OeffnenVerzoegerungMs = (int)v; _s.NotifyChanged(); }, 10)),
            Row("Verzögerung beim Schliessen", null, SliderControl(100, 1500, _s.SchliessenVerzoegerungMs, v => $"{v:0} ms",
                v => { _s.SchliessenVerzoegerungMs = (int)v; _s.NotifyChanged(); }, 50)),
            Row("Doppelklick", "Öffnet das Menü mit Einstellungen und Schliessen", null)));

        p.Children.Add(SectionTitle("Animationen"));
        p.Children.Add(Card(
            Row("Tempo", "Weniger = schneller", SliderControl(50, 150, _s.AnimationsTempo * 100, v => $"{v:0} %",
                v => { _s.AnimationsTempo = v / 100; _s.NotifyChanged(); }, 5)),
            Row("Equalizer animiert", "Aus = spart noch etwas mehr Akku", Toggle(_s.EqualizerAnimiert,
                v => { _s.EqualizerAnimiert = v; _s.NotifyChanged(); }))));

        p.Children.Add(SectionTitle("Bildschirmrand"));
        p.Children.Add(Card(
            Row("Am Rand andocken", "Zieh die Insel nah an einen Rand – sie klebt bündig dran und geht zur Mitte hin auf.", null, glyph: "\uE8A0"),
            Row("In den Rand eindrücken", "Schieb sie ganz in den Rand → nur ein schmaler Streifen bleibt. Klick drauf oder rausziehen holt sie zurück.",
                Toggle(_s.AmRandVerstecken, v => { _s.AmRandVerstecken = v; _s.NotifyChanged(); }), glyph: "\uE73F")));

        p.Children.Add(SectionTitle("Ruheanzeige"));
        p.Children.Add(Card(
            Row("Wenn keine Musik läuft", "Tipp: Mausrad über der zugeklappten Insel wechselt auch", null,
                below: Segmented(new[] { "Datum", "Uhr", "Akku", "Leistung", "Minimal" }, _s.RuheWidget,
                    v => { _s.RuheWidget = v; _s.NotifyChanged(); }), glyph: "\uE823"),
            Row("Bei Pause wechseln nach", "Pausierte Musik verschwindet nach dieser Zeit aus der zugeklappten Insel",
                SliderControl(0, 300, _s.PauseZuRuheSekunden, v => v < 1 ? "nie" : v < 60 ? $"{v:0} s" : $"{v / 60:0.#} min",
                    v => { _s.PauseZuRuheSekunden = (int)v; _s.NotifyChanged(); }, 15))));

        p.Children.Add(SectionTitle("Arbeitsspeicher (RAM)"));
        p.Children.Add(Card(BuildRamRows()));

        p.Children.Add(SectionTitle("System"));
        p.Children.Add(Card(
            Row("Mit Windows starten", null, Toggle(Autostart.IsEnabled, v =>
            {
                try { Autostart.Set(v); }
                catch (Exception ex) { App.Log(ex); }
            }), glyph: ""),
            Row("Bei Vollbild ausblenden", "Spiele und Videos im Vollbild", Toggle(_s.BeiVollbildAusblenden,
                v => { _s.BeiVollbildAusblenden = v; _s.NotifyChanged(); }), glyph: ""),
            Row("Position", "Zieh die Insel einfach mit der Maus, wohin du willst. Oben in der Mitte rastet sie ein.",
                Btn("Zurücksetzen", "Secondary", () => _island.ResetPosition()), glyph: "")));
    }

    private UIElement[] BuildRamRows()
    {
        var ram = _island.Ram;
        var rows = new List<UIElement>
        {
            Row("RAM begrenzen", "Die Insel darf höchstens so viel Arbeitsspeicher benutzen, wie du unten einstellst.",
                Toggle(_s.RamBegrenzen, v => { _s.RamBegrenzen = v; _s.NotifyChanged(); RebuildPage(); }), glyph: "\uE950"),
        };

        if (_s.RamMinMb <= 0 || ram.IsMeasuring)
        {
            rows.Add(Info("Noch nicht gemessen. Die Insel misst selbst, wie viel RAM sie mindestens braucht – " +
                          "das passiert automatisch, sobald du dieses Fenster schliesst (dauert ca. 10 Sekunden)."));
        }
        else
        {
            var min = ram.MinMb;
            var max = ram.MaxMb;
            var value = _s.RamLimitMb > 0 ? Math.Clamp(_s.RamLimitMb, min, max) : max;
            rows.Add(Row("Grenze", $"Minimum {min} MB = absolut nötig · Maximum {max} MB = mehr bringt nichts", null,
                below: RamSlider(min, max, value, _s.RamBegrenzen)));
            rows.Add(Info($"Gerade benutzt: {RamLimiter.CurrentMb()} MB (solange dieses Fenster offen ist, gilt keine Grenze). " +
                          "Je tiefer die Grenze, desto mehr lagert Windows aus – die Insel kann dann etwas träger reagieren."));
        }

        rows.Add(Row("Neu messen", "Z. B. nachdem du Funktionen ein- oder ausgeschaltet hast. Wird nach dem Schliessen dieses Fensters gemessen.",
            Btn("Neu messen", "Secondary", () => { ram.ResetMeasurement(); RebuildPage(); })));
        return rows.ToArray();
    }

    /// <summary>Schieberegler, der nur zwischen gemessenem Minimum und Maximum geht.</summary>
    private FrameworkElement RamSlider(int min, int max, int value, bool enabled)
    {
        var current = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            SmallChange = 1,
            LargeChange = 5,
            Margin = new Thickness(10, 0, 10, 0),
        }.Themed(StyleProperty, "DarkSlider");

        void Show(double v) => current.Text = v >= max ? $"{v:0} MB  (Maximum)" : v <= min ? $"{v:0} MB  (Minimum)" : $"{v:0} MB";
        Show(value);
        slider.ValueChanged += (_, e) =>
        {
            Show(e.NewValue);
            _s.RamLimitMb = (int)e.NewValue;
            _s.NotifyChangedSoon();
        };

        var minLabel = new TextBlock { Text = $"Min\n{min} MB", FontSize = 11, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, "S.Sub");
        var maxLabel = new TextBlock { Text = $"Max\n{max} MB", FontSize = 11, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, "S.Sub");

        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(maxLabel, 2);
        line.Children.Add(minLabel);
        line.Children.Add(slider);
        line.Children.Add(maxLabel);

        var panel = new StackPanel { IsEnabled = enabled, Opacity = enabled ? 1 : 0.4 };
        panel.Children.Add(current);
        panel.Children.Add(line);
        if (!enabled)
        {
            panel.Children.Add(new TextBlock { Text = "Schalte „RAM begrenzen“ ein, um die Grenze zu ändern.", FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) }
                .Themed(TextBlock.ForegroundProperty, "S.Sub"));
        }
        return panel;
    }

    private void BuildFeatures(StackPanel p)
    {
        p.Children.Add(Header("Funktionen"));
        p.Children.Add(Sub("Schalte Bereiche der Insel ein oder aus."));

        var rows = new List<UIElement>();
        foreach (var (name, icon) in _island.AllModules)
        {
            var moduleName = name;
            var enabled = !_s.DeaktivierteModule.Contains(moduleName, StringComparer.OrdinalIgnoreCase);
            ModuleDescriptions.TryGetValue(moduleName, out var description);
            rows.Add(Row(moduleName, description, Toggle(enabled, v =>
            {
                _s.DeaktivierteModule.RemoveAll(x => x.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
                if (!v) _s.DeaktivierteModule.Add(moduleName);
                _s.SaveSoon();
                ShowRestartBanner();
            }), glyph: icon));
        }
        p.Children.Add(Card(rows.ToArray()));
        p.Children.Add(Sub("Eigene Funktionen kannst du als Modul hinzufügen – wie, steht in LIES-MICH.md."));
    }

    private void BuildConnections(StackPanel p)
    {
        p.Children.Add(Header("Verbindungen"));
        p.Children.Add(Sub("Woher die Insel ihre Infos bekommt."));

        p.Children.Add(SectionTitle("Musik"));
        p.Children.Add(Card(
            Row("Musikquelle", null, Segmented(new[] { "Nur Spotify", "Alle Apps" }, _s.NurSpotify ? "Nur Spotify" : "Alle Apps",
                v => { _s.NurSpotify = v == "Nur Spotify"; _s.NotifyChanged(); }), glyph: ""),
            Info("Läuft über die eingebaute Windows-Mediensteuerung: kein Login, kein Spotify Premium, kein Internet nötig. " +
                 "„Alle Apps“ zeigt auch YouTube, Browser, Apple Music usw. Spotify hat immer Vorrang.")));
    }

    private void BuildApps(StackPanel p)
    {
        p.Children.Add(Header("Apps"));
        p.Children.Add(Sub("Kacheln im Apps-Tab. Ziel kann ein Programm, ein Ordner, eine Webseite oder ein Link wie spotify: sein."));

        var rows = new List<UIElement>();
        foreach (var entry in _s.Schnellstart.ToList())
        {
            var item = entry;
            var grid = new Grid { Margin = new Thickness(12, 8, 8, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = Box(item.Name, v => { item.Name = v; _s.NotifyChangedSoon(); });
            var target = Box(item.Ziel, v => { item.Ziel = v; _s.NotifyChangedSoon(); });
            target.Margin = new Thickness(8, 0, 4, 0);

            var browse = IconBtn("", "Programm auswählen", () =>
            {
                var dialog = new OpenFileDialog { Filter = "Programme (*.exe)|*.exe|Alle Dateien (*.*)|*.*" };
                if (dialog.ShowDialog(this) != true) return;
                item.Ziel = dialog.FileName;
                if (string.IsNullOrWhiteSpace(item.Name) || item.Name == "Neu")
                    item.Name = Path.GetFileNameWithoutExtension(dialog.FileName);
                _s.NotifyChanged();
                RebuildPage();
            });
            var delete = IconBtn("", "Entfernen", () =>
            {
                _s.Schnellstart.Remove(item);
                _s.NotifyChanged();
                RebuildPage();
            });

            Grid.SetColumn(target, 1);
            Grid.SetColumn(browse, 2);
            Grid.SetColumn(delete, 3);
            grid.Children.Add(name);
            grid.Children.Add(target);
            grid.Children.Add(browse);
            grid.Children.Add(delete);
            rows.Add(grid);
        }
        if (rows.Count == 0) rows.Add(Info("Noch keine Apps."));
        p.Children.Add(Card(rows.ToArray()));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Btn("+ App hinzufügen", "Primary", () =>
        {
            _s.Schnellstart.Add(new SchnellstartEintrag { Name = "Neu", Ziel = "" });
            _s.SaveSoon();
            RebuildPage();
        }));
        var folder = Btn("+ Ordner hinzufügen", "Secondary", () =>
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog(this) != true) return;
            _s.Schnellstart.Add(new SchnellstartEintrag { Name = Path.GetFileName(dialog.FolderName.TrimEnd('\\')), Ziel = dialog.FolderName });
            _s.NotifyChanged();
            RebuildPage();
        });
        folder.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(folder);
        p.Children.Add(buttons);
    }

    private void BuildKeys(StackPanel p)
    {
        p.Children.Add(Header("Tastenkürzel"));
        p.Children.Add(Sub("Dein Spickzettel im Tasten-Tab. Tasten mit + trennen, z. B. „Win + Shift + S“."));

        var rows = new List<UIElement>();
        foreach (var entry in _s.Tastenkuerzel.ToList())
        {
            var item = entry;
            var grid = new Grid { Margin = new Thickness(12, 8, 8, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var keys = Box(item.Tasten, v => { item.Tasten = v; _s.NotifyChangedSoon(); });
            var text = Box(item.Beschreibung, v => { item.Beschreibung = v; _s.NotifyChangedSoon(); });
            text.Margin = new Thickness(8, 0, 4, 0);
            var delete = IconBtn("", "Entfernen", () =>
            {
                _s.Tastenkuerzel.Remove(item);
                _s.NotifyChanged();
                RebuildPage();
            });

            Grid.SetColumn(text, 1);
            Grid.SetColumn(delete, 2);
            grid.Children.Add(keys);
            grid.Children.Add(text);
            grid.Children.Add(delete);
            rows.Add(grid);
        }
        if (rows.Count == 0) rows.Add(Info("Noch keine Tastenkürzel."));
        p.Children.Add(Card(rows.ToArray()));

        p.Children.Add(Btn("+ Tastenkürzel hinzufügen", "Primary", () =>
        {
            _s.Tastenkuerzel.Add(new TastenEintrag { Tasten = "Win + ", Beschreibung = "" });
            _s.SaveSoon();
            RebuildPage();
        }));
    }

    private void BuildInfo(StackPanel p)
    {
        p.Children.Add(Header("Info"));
        p.Children.Add(Sub("Selbst gebaut, Open Source – keine Daten werden irgendwohin gesendet."));

        p.Children.Add(Card(
            Row("Dynamic Island", "Version 1.4", null, glyph: ""),
            Row("Einstellungsdatei", "Alles als Text bearbeiten (für Profis)",
                Btn("Öffnen", "Secondary", () => IslandContext.Open("notepad.exe", $"\"{Settings.FilePath}\""))),
            Row("Fehlerprotokoll", "Falls etwas nicht klappt – Inhalt an Claude schicken",
                Btn("Öffnen", "Secondary", () =>
                {
                    var log = Path.Combine(AppContext.BaseDirectory, "fehler.log");
                    if (File.Exists(log)) IslandContext.Open("notepad.exe", $"\"{log}\"");
                    else MessageBox.Show(this, "Keine Fehler bisher. 🎉", "Dynamic Island");
                }))));

        p.Children.Add(Card(
            Row("Neu starten", null, Btn("Neu starten", "Secondary", App.Restart)),
            Row("Dynamic Island beenden", "Schliesst die Insel ganz", Btn("Beenden", "Danger", () => Application.Current.Shutdown()))));
    }

    // ================================================================== Bausteine

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 26,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 0, 0, 4),
    };

    private static TextBlock Sub(string text) => new TextBlock
    {
        Text = text,
        FontSize = 12.5,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 18),
    }.Themed(TextBlock.ForegroundProperty, "S.Sub");

    private static TextBlock SectionTitle(string text) => new TextBlock
    {
        Text = text.ToUpperInvariant(),
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(14, 0, 0, 6),
    }.Themed(TextBlock.ForegroundProperty, "S.Sub");

    private static TextBlock Info(string text) => new TextBlock
    {
        Text = text,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(14, 10, 14, 12),
    }.Themed(TextBlock.ForegroundProperty, "S.Sub");

    private static Border Card(params UIElement[] rows)
    {
        var stack = new StackPanel();
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0) stack.Children.Add(new Border { Height = 1, Margin = new Thickness(14, 0, 0, 0) }.Themed(Border.BackgroundProperty, "S.Line"));
            stack.Children.Add(rows[i]);
        }
        return new Border
        {
            CornerRadius = new CornerRadius(11),
            Child = stack,
            Margin = new Thickness(0, 0, 0, 20),
        }.Themed(Border.BackgroundProperty, "S.Card");
    }

    private static FrameworkElement Row(string label, string? hint, FrameworkElement? control,
                                        FrameworkElement? below = null, string? glyph = null)
    {
        var grid = new Grid { Margin = new Thickness(14, 10, 14, below != null ? 6 : 10), MinHeight = 30 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (glyph != null)
        {
            left.Children.Add(new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = glyph, FontFamily = Ui.IconFont, FontSize = 13, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            }.Themed(Border.BackgroundProperty, "S.Accent"));
        }
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = label, FontSize = 13.5 });
        if (hint != null)
        {
            texts.Children.Add(new TextBlock
            {
                Text = hint, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 300, Margin = new Thickness(0, 2, 0, 0),
            }.Themed(TextBlock.ForegroundProperty, "S.Sub"));
        }
        left.Children.Add(texts);
        grid.Children.Add(left);

        if (control != null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            control.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        if (below == null) return grid;
        var outer = new StackPanel();
        below.Margin = new Thickness(14, 0, 14, 12);
        outer.Children.Add(grid);
        outer.Children.Add(below);
        return outer;
    }

    private static CheckBox Toggle(bool value, Action<bool> set)
    {
        var toggle = new CheckBox { IsChecked = value }.Themed(StyleProperty, "Toggle");
        toggle.Checked += (_, _) => set(true);
        toggle.Unchecked += (_, _) => set(false);
        return toggle;
    }

    private Border Segmented(string[] options, string current, Action<string> set)
    {
        var group = "seg" + ++_groupCounter;
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var option in options)
        {
            var value = option;
            var rb = new RadioButton { Content = value, GroupName = group, IsChecked = value == current }.Themed(StyleProperty, "Segment");
            rb.Checked += (_, _) => set(value);
            stack.Children.Add(rb);
        }
        return new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(1), Child = stack }
            .Themed(Border.BackgroundProperty, "S.Control");
    }

    private static StackPanel SliderControl(double min, double max, double value, Func<double, string> format,
                                            Action<double> set, double step)
    {
        var label = new TextBlock { Width = 56, TextAlignment = TextAlignment.Right, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, "S.Sub");
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = 210,
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            SmallChange = step,
            LargeChange = step * 5,
        }.Themed(StyleProperty, "DarkSlider");
        label.Text = format(slider.Value);
        slider.ValueChanged += (_, e) =>
        {
            label.Text = format(e.NewValue);
            set(e.NewValue);
        };

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(slider);
        panel.Children.Add(label);
        return panel;
    }

    private static StackPanel ColorPicker(string current, Action<string> set)
    {
        var preview = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 10, 0),
            BorderBrush = Ui.Brush("#33FFFFFF"), BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Theme.ParseColor(current, Colors.Black)),
        };
        var box = new TextBox { Text = current, Width = 100 }.Themed(StyleProperty, "DarkBox");

        void Apply(string hex, bool fromBox)
        {
            if (!Theme.TryParseColor(hex, out var color)) return;
            preview.Background = new SolidColorBrush(color);
            var normalized = Theme.ToHex(color);
            if (!fromBox) box.Text = normalized;
            set(normalized);
        }

        var swatches = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
        foreach (var (groupName, colors) in SwatchGroups)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = groupName, Width = 56, FontSize = 11, Margin = new Thickness(0, 6, 0, 0), VerticalAlignment = VerticalAlignment.Top }
                .Themed(TextBlock.ForegroundProperty, "S.Sub"));
            foreach (var hex in colors)
            {
                var color = hex;
                var swatch = new Button { Background = Ui.Brush(color), ToolTip = color }.Themed(StyleProperty, "Swatch");
                swatch.Click += (_, _) => Apply(color, fromBox: false);
                row.Children.Add(swatch);
            }
            swatches.Children.Add(row);
        }
        box.TextChanged += (_, _) => Apply(box.Text, fromBox: true);

        var custom = new StackPanel { Orientation = Orientation.Horizontal };
        custom.Children.Add(preview);
        custom.Children.Add(box);
        custom.Children.Add(new TextBlock
        {
            Text = "eigene Farbe (Hex)", FontSize = 11.5, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        }.Themed(TextBlock.ForegroundProperty, "S.Sub"));

        var panel = new StackPanel();
        panel.Children.Add(swatches);
        panel.Children.Add(custom);
        return panel;
    }

    private Button PresetButton((string Name, string Stil, string F1, string F2, double Winkel, double Deckkraft, string Akzent) p)
    {
        var c1 = Theme.ParseColor(p.F1, Colors.Black);
        var c2 = Theme.ParseColor(p.F2, Colors.Black);
        Brush fill = p.Stil switch
        {
            Theme.Gradient => Theme.Linear(p.Winkel, (c1, 0), (c2, 1)),
            Theme.Glass => Theme.Linear(90, (Color.FromArgb(150, 255, 255, 255), 0), (Color.FromArgb(200, c1.R, c1.G, c1.B), 0.5), (Color.FromArgb(170, c1.R, c1.G, c1.B), 1)),
            _ => new SolidColorBrush(c1),
        };

        var preview = new Border
        {
            Width = 74, Height = 26, CornerRadius = new CornerRadius(13), Background = fill,
            BorderBrush = Ui.Brush("#40FFFFFF"), BorderThickness = new Thickness(1),
            Child = new Border
            {
                Width = 16, Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 10, 0), Background = new SolidColorBrush(Theme.ParseColor(p.Akzent, Colors.White)),
            },
        };
        var label = new TextBlock { Text = p.Name, FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, "S.Sub");

        var stack = new StackPanel();
        stack.Children.Add(preview);
        stack.Children.Add(label);

        var button = new Button { Content = stack, Margin = new Thickness(0, 0, 4, 4) }.Themed(StyleProperty, "Plain");
        button.Click += (_, _) =>
        {
            _s.Stil = p.Stil;
            _s.Farbe1 = p.F1;
            _s.Farbe2 = p.F2;
            _s.VerlaufWinkel = p.Winkel;
            _s.Deckkraft = p.Deckkraft;
            _s.Akzent = p.Akzent;
            _s.NotifyChanged();
            RebuildPage();
        };
        return button;
    }

    private FrameworkElement FontPicker()
    {
        var names = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(n => n).ToList();
        var view = CollectionViewSource.GetDefaultView(names);

        var search = new TextBox { Margin = new Thickness(0, 0, 0, 8) }.Themed(StyleProperty, "DarkBox");
        var list = new ListBox { Height = 200, ItemsSource = names }.Themed(StyleProperty, "FontList");
        list.SelectedItem = names.FirstOrDefault(n => n.Equals(_s.Schriftart, StringComparison.OrdinalIgnoreCase));

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not string name || name == _s.Schriftart) return;
            _s.Schriftart = name;
            _s.NotifyChanged();
        };
        search.TextChanged += (_, _) =>
        {
            var query = search.Text.Trim();
            if (query.Length == 0) view.Filter = null;
            else view.Filter = o => o is string s && s.Contains(query, StringComparison.OrdinalIgnoreCase);
        };
        list.Loaded += (_, _) => { if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem); };

        var hint = new TextBlock { Text = "Suchen …", FontSize = 11.5, Margin = new Thickness(2, 0, 0, 4) }
            .Themed(TextBlock.ForegroundProperty, "S.Sub");

        var panel = new StackPanel();
        panel.Children.Add(hint);
        panel.Children.Add(search);
        panel.Children.Add(list);
        return panel;
    }

    private static TextBox Box(string value, Action<string> set)
    {
        var box = new TextBox { Text = value }.Themed(StyleProperty, "DarkBox");
        box.TextChanged += (_, _) => set(box.Text);
        return box;
    }

    private static Button Btn(string text, string styleKey, Action action)
    {
        var button = new Button { Content = text }.Themed(StyleProperty, styleKey);
        button.Click += (_, _) => action();
        return button;
    }

    private static Button IconBtn(string glyph, string tooltip, Action action)
    {
        var button = new Button { Content = glyph, ToolTip = tooltip }.Themed(StyleProperty, "SmallIcon");
        button.Click += (_, _) => action();
        return button;
    }

    private static Button WindowDot(string color, string glyph, Action? action)
    {
        var button = new Button { Background = Ui.Brush(color), Content = glyph }.Themed(StyleProperty, "WindowDot");
        if (action != null) button.Click += (_, _) => action();
        return button;
    }

    private Border BuildRestartBanner()
    {
        var text = new TextBlock
        {
            Text = "Diese Änderung wird nach einem Neustart der Insel aktiv.",
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        var button = Btn("Jetzt neu starten", "Primary", App.Restart);
        button.Margin = new Thickness(12, 0, 0, 0);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(button, 1);
        grid.Children.Add(text);
        grid.Children.Add(button);

        return new Border
        {
            Background = Ui.Brush("#260A84FF"),
            BorderBrush = Ui.Brush("#400A84FF"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, 14, 0),
            Padding = new Thickness(24, 12, 20, 12),
            Child = grid,
            Visibility = Visibility.Collapsed,
        };
    }

    private void ShowRestartBanner()
    {
        if (_restartBanner.Visibility == Visibility.Visible) return;
        _restartBanner.Visibility = Visibility.Visible;
        _restartBanner.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); }
        catch (InvalidOperationException) { /* egal */ }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        ("Sanft", new[] { "#E07A6F", "#E3A36B", "#D9C27A", "#8FC9A0", "#7FB8C9", "#7F9CD6", "#B49BD9" }),
        ("Tief", new[] { "#2B1055", "#3A1C71", "#0F2027", "#134E5E", "#1A2980", "#3E2723", "#5B0E2D" }),
    };

    // Abgestimmte Farbkombis (Hintergrund + Akzent). Rundung/Schrift bleiben beim Anwenden wie sie sind.
    private static DesignEintrag P(string name, string stil, string f1, string f2, string akzent, double deckkraft = 1.0, double klarheit = 0.6)
        => new() { Name = name, Stil = stil, Farbe1 = f1, Farbe2 = f2, Akzent = akzent, Deckkraft = deckkraft, GlasKlarheit = klarheit };

    private static readonly DesignEintrag[] Presets =
    {
        P("Mitternacht", Theme.Solid, "#0A0A0C", "#0A0A0C", "#8FD3A8"),
        P("Cover-Farben", Theme.Cover, "#0C0C0F", "#0C0C0F", "#E58FA0"),
        P("Klarglas", Theme.PureGlass, "#FFFFFF", "#FFFFFF", "#FFFFFF", klarheit: 1.0),
        P("Milchglas", Theme.PureGlass, "#FFFFFF", "#FFFFFF", "#7F9CD6", klarheit: 0.05),
        P("Liquid Glass", Theme.Glass, "#16161D", "#16161D", "#9CC8E0", 0.5, klarheit: 0.35),
        P("Frost", Theme.Glass, "#F2F2F7", "#F2F2F7", "#6E8FC7", 0.6, klarheit: 0.15),
        P("Graphit", Theme.Gradient, "#232526", "#414345", "#E2C98A"),
        P("Nordlicht", Theme.Gradient, "#0F2027", "#2C5364", "#9ED9CC"),
        P("Matcha", Theme.Gradient, "#134E5E", "#71B280", "#E8FFD6"),
        P("Aubergine", Theme.Gradient, "#2B1055", "#7597DE", "#FFC8DD"),
        P("Lavendel", Theme.Gradient, "#A18CD1", "#FBC2EB", "#5E5CE6"),
        P("Pfirsich", Theme.Gradient, "#FFDDE1", "#EE9CA7", "#C2185B"),
        P("Rosé Gold", Theme.Gradient, "#B76E79", "#E8C4B8", "#7A2E3A"),
        P("Sunset", Theme.Gradient, "#FF5E62", "#FF9966", "#FFFFFF"),
        P("Ozean", Theme.Gradient, "#2E3192", "#3FB8C8", "#F5F5F7"),
        P("Xiaomi", Theme.Gradient, "#FF6900", "#FF3D00", "#FFFFFF"),
        P("Espresso", Theme.Gradient, "#3E2723", "#8D6E63", "#FFCC80"),
        P("Mitternachtsblau", Theme.Gradient, "#0B132B", "#1C2541", "#A8D8CF"),
        P("Pflaume", Theme.Gradient, "#2D0B3A", "#7B2869", "#FFB3C6"),
        P("Wald", Theme.Gradient, "#0B1D13", "#1F4D36", "#B7E4C7"),
        P("Champagner", Theme.Gradient, "#F7E7CE", "#E6C79C", "#8A5A2B"),
        P("Nachtviolett", Theme.Gradient, "#1B1040", "#5B2A86", "#D7A6E8"),
        P("Eisblau", Theme.Glass, "#BFE3FF", "#BFE3FF", "#5F86B8", 0.55, klarheit: 0.4),
    };

    private static readonly Dictionary<string, string> ModuleDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Musik"] = "Song, Cover, Fortschritt, Lyrics und Steuerung",
        ["Aufgaben"] = "To-dos mit Erinnerung – die Insel meldet sich, wenn etwas fällig ist",
        ["Wetter"] = "Wetter von heute mit kleiner Vorschau",
        ["Kontrollzentrum"] = "Lautstärke, Helligkeit, WLAN, Bluetooth, Kamera …",
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

    // Apps: Auswahl aus installierten Programmen
    private bool _appPickerOpen;
    private string _appSearch = "";
    private static List<(string Name, string Path)>? _installedApps;

    // Tastenkürzel aufnehmen
    private TextBox? _recordBox;
    private string _recordOld = "";
    private TastenEintrag? _recordNext;

    /// <summary>Beliebte Windows-Kürzel zum Antippen.</summary>
    private static readonly (string Keys, string Text)[] KeyLibrary =
    {
        ("Win + V", "Zwischenablage-Verlauf"), ("Win + Shift + S", "Screenshot (Ausschnitt)"), ("Win + .", "Emojis"),
        ("Win + D", "Desktop zeigen"), ("Win + L", "PC sperren"), ("Win + E", "Explorer öffnen"), ("Win + I", "Einstellungen"),
        ("Win + A", "Schnelleinstellungen"), ("Win + N", "Benachrichtigungen"), ("Win + Tab", "Alle Fenster & Desktops"),
        ("Win + Pfeil", "Fenster andocken"), ("Win + Strg + D", "Neuer Desktop"), ("Win + Strg + Pfeil", "Desktop wechseln"),
        ("Win + G", "Game Bar"), ("Win + Alt + R", "Bildschirm aufnehmen"), ("Win + H", "Diktieren"), ("Win + P", "Bildschirm projizieren"),
        ("Alt + Tab", "Zwischen Fenstern wechseln"), ("Alt + F4", "Fenster schliessen"), ("Strg + Shift + Esc", "Task-Manager"),
        ("Strg + Shift + T", "Geschlossenen Tab öffnen"), ("Strg + Shift + V", "Ohne Formatierung einfügen"), ("Strg + Z", "Rückgängig"),
        ("Strg + Y", "Wiederholen"), ("Strg + F", "Suchen"), ("F2", "Umbenennen"), ("Druck", "Screenshot ganzer Bildschirm"),
    };

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
        AddNav("Aufgaben", "");
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
        Closing += (_, _) =>
        {
            StopScrollAnimation();
            if (_tasksListener != null) _s.Changed -= _tasksListener;
            _s.Save();
        };

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

        // Apps: Programme, Verknüpfungen oder Ordner einfach ins Fenster ziehen
        AllowDrop = true;
        PreviewDragOver += (_, e) =>
        {
            e.Effects = _currentPage == "Apps" && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        PreviewDrop += (_, e) =>
        {
            if (_currentPage != "Apps" || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
            foreach (var f in files) AddApp(f);
            _s.NotifyChanged();
            RebuildPage();
            e.Handled = true;
        };

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
        if (name != _currentPage) _appPickerOpen = false;
        _recordBox = null;
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
                case "Aufgaben": BuildTasks(panel); break;
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

    /// <summary>Von aussen direkt zu einer Seite springen (z. B. "Apps", "Aufgaben").</summary>
    public void GoTo(string page)
    {
        foreach (var child in _nav.Children)
            if (child is RadioButton r && Equals(r.Tag, page)) { r.IsChecked = true; return; }
        ShowPage(page);
    }

    // ================================================================== Scrollen

    private void OnScrollWheel(object sender, MouseWheelEventArgs e)
    {
        // Innere Listen (Schriftarten, installierte Apps) scrollen selbst
        if (FindParent<ListBox>(e.OriginalSource as DependencyObject) != null) return;
        var inner = FindParent<ScrollViewer>(e.OriginalSource as DependencyObject);
        if (inner != null && Equals(inner.Tag, "inner")) return;
        e.Handled = true;
        ScrollBy(-e.Delta * WheelFactor);
    }

    private void OnScrollKeys(object sender, KeyEventArgs e)
    {
        if (HandleRecording(e)) return;

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

        // ---------- Vorlagen
        p.Children.Add(SectionTitle("Vorlagen"));
        var presets = new WrapPanel { Margin = new Thickness(8, 10, 8, 4) };
        foreach (var preset in _showAllPresets ? Presets : Presets.Take(7)) presets.Children.Add(DesignTile(preset, null));
        var morePresets = MoreButton(_showAllPresets, Presets.Length, () => { _showAllPresets = !_showAllPresets; RebuildPage(); });
        var random = IconTextBtn("\uE8B1", "Überrasch mich", "Secondary", () =>
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
        p.Children.Add(Card(presets, morePresets, Row("Zufällige Farbkombi", "Erfindet eine harmonische Kombi – so oft klicken, bis sie dir gefällt", random)));

        // ---------- Meine Designs
        p.Children.Add(SectionTitle("Meine Designs"));
        var nameBox = new TextBox { Width = 230 }.Themed(StyleProperty, "DarkBox");
        var save = Btn("Speichern", "Primary", () =>
        {
            var name = nameBox.Text.Trim();
            if (name.Length == 0) name = $"Mein Design {_s.EigeneDesigns.Count + 1}";
            _s.EigeneDesigns.RemoveAll(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            _s.EigeneDesigns.Add(DesignEintrag.From(_s, name));
            _s.SaveSoon();
            RebuildPage();
        });
        save.Margin = new Thickness(8, 0, 0, 0);
        nameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); };
        var saveRow = new StackPanel { Orientation = Orientation.Horizontal };
        saveRow.Children.Add(nameBox);
        saveRow.Children.Add(save);

        var mine = new WrapPanel { Margin = new Thickness(8, 6, 8, 4) };
        foreach (var design in _s.EigeneDesigns.ToList())
        {
            var d = design;
            mine.Children.Add(DesignTile(d, () =>
            {
                _s.EigeneDesigns.Remove(d);
                _s.SaveSoon();
                RebuildPage();
            }));
        }
        p.Children.Add(Card(
            Row("Aktuelles Design speichern", "Merkt sich Farben, Stil, Glas, Rundung, Schatten und Schrift unter einem Namen (nur auf diesem PC).",
                null, below: saveRow, glyph: "\uE74E"),
            mine.Children.Count > 0 ? (UIElement)mine : Info("Noch keine eigenen Designs. Stell oben alles ein und speichere es hier.")));

        // ---------- Stil
        p.Children.Add(SectionTitle("Stil"));
        var style = new List<UIElement>
        {
            Row("Design", null, null, below: Segmented(new[] { Theme.Solid, Theme.Gradient, Theme.Glass, Theme.PureGlass, Theme.Cover }, _s.Stil, v =>
            {
                _s.Stil = v;
                _s.NotifyChanged();
                RebuildPage();
            }), glyph: "\uE790"),
        };

        if (_s.Stil == Theme.Cover)
        {
            style.Add(Info("Dunkel und edel – Akzentfarbe und ein leichter Farbschimmer kommen automatisch vom Cover des Songs, der gerade läuft. " +
                           "Ohne Musik gilt deine eigene Akzentfarbe unten."));
            style.Add(Row("Deckkraft", null,
                SliderControl(30, 100, _s.Deckkraft * 100, v => $"{v:0} %", v => { _s.Deckkraft = v / 100; _s.NotifyChanged(); }, 5)));
        }
        else if (Theme.IsGlass(_s))
        {
            style.Add(Info(_s.Stil == Theme.PureGlass
                ? "Reines Glas ohne Farbe – wie bei Apple. Links: Milchglas mit echter Unschärfe dahinter. Rechts: komplett klar und durchsichtig."
                : "Getöntes Glas. Links: mattiert mit echter Unschärfe dahinter. Rechts: klar und durchsichtig, nur leicht getönt."));
            style.Add(Row("Glas", null, null, below: RangeSlider(0, 100, _s.GlasKlarheit * 100, "Wolkig", "Klar",
                v => v >= 85 ? "Ganz klar" : v <= 15 ? "Milchglas" : $"{v:0} % klar",
                v => { _s.GlasKlarheit = v / 100; _s.NotifyChanged(); }, 5, enabled: true)));
            if (_s.Stil == Theme.Glass)
                style.Add(Row("Glas-Tönung", null, null, below: ColorPicker(_s.Farbe1, v => { _s.Farbe1 = v; _s.NotifyChanged(); })));
            style.Add(Row("Echte Unschärfe", "Windows macht hinter der Insel alles weich (wie im Foto). Aus = nur durchsichtig.",
                Toggle(_s.EchterBlur, v => { _s.EchterBlur = v; _s.NotifyChanged(); }), glyph: "\uE753"));
            style.Add(Row("Cover im Glas-Look", "Album-Cover farblos und leicht durchsichtig – alles wirkt einheitlich",
                Toggle(_s.CoverGlas, v => { _s.CoverGlas = v; _s.NotifyChanged(); }), glyph: "\uE8B9"));
        }
        else
        {
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
        }
        p.Children.Add(Card(style.ToArray()));

        // ---------- Details
        p.Children.Add(SectionTitle("Details"));
        p.Children.Add(Card(
            Row("Akzentfarbe", "Equalizer und Fortschrittsbalken", null,
                below: ColorPicker(_s.Akzent, v => { _s.Akzent = v; _s.NotifyChanged(); })),
            Row("Akzent vom Song-Cover", "Nimmt automatisch die schönste Farbe aus dem Cover, das gerade läuft",
                Toggle(_s.AkzentVomCover, v => { _s.AkzentVomCover = v; _s.NotifyChanged(); }), glyph: "\uE790"),
            Row("Grösse", null, SliderControl(80, 130, _s.Groesse * 100, v => $"{v:0} %",
                v => { _s.Groesse = v / 100; _s.NotifyChanged(); }, 5)),
            Row("Rundung", "Ecken, wenn aufgeklappt", SliderControl(10, 36, _s.Rundung, v => $"{v:0}",
                v => { _s.Rundung = v; _s.NotifyChanged(); }, 1)),
            Row("Schatten", null, Toggle(_s.Schatten, v => { _s.Schatten = v; _s.NotifyChanged(); }))));

        p.Children.Add(SectionTitle("Fortschrittsbalken"));
        var ownColor = Theme.TryParseColor(_s.FortschrittFarbe, out _);
        p.Children.Add(Card(
            BuildProgressGallery(),
            Row("Eigene Balkenfarbe", "Aus = gleiche Farbe wie der Akzent", Toggle(ownColor, v =>
            {
                _s.FortschrittFarbe = v ? _s.Akzent : "";
                _s.NotifyChanged();
                RebuildPage();
            }), ownColor ? ColorPicker(_s.FortschrittFarbe, v => { _s.FortschrittFarbe = v; _s.NotifyChanged(); }) : null, glyph: "\uE790")));

        p.Children.Add(SectionTitle("Schrift"));
        p.Children.Add(Card(Row("Schriftart", "Gilt für alle Texte der Insel", null, below: FontPicker(), glyph: "\uE8D2")));
    }

    /// <summary>Alle Balken-Designs als lebendige Vorschau zum Antippen.</summary>
    private FrameworkElement BuildProgressGallery()
    {
        var root = new StackPanel { Margin = new Thickness(14, 10, 10, 8) };
        var start = DateTime.Now;
        var favorites = new[] { "Welle", "Linie", "Verlauf", "Katze", "Herzschlag" };
        var catalog = _showAllProgress
            ? ProgressTrack.Catalog
            : new[] { ("Beliebt", ProgressTrack.Catalog.SelectMany(c => c.Items)
                        .Where(i => favorites.Contains(i.Id) || i.Id == _s.Fortschritt).ToArray()) };
        foreach (var (category, items) in catalog)
        {
            root.Children.Add(new TextBlock { Text = category, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 6, 0, 6) }
                .Themed(TextBlock.ForegroundProperty, "S.Sub"));
            var wrap = new WrapPanel();
            foreach (var (id, name) in items)
            {
                var styleId = id;
                var selected = _s.Fortschritt == id;
                var track = new ProgressTrack
                {
                    StyleId = id, Height = 22, Live = true, Playing = true,
                    Source = () => 0.15 + 0.7 * ((DateTime.Now - start).TotalSeconds / 14 % 1),
                };
                var preview = new Border
                {
                    Width = 148, Height = 40, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 0, 12, 0),
                    Background = Theme.CurrentFill, Child = track,
                    BorderThickness = new Thickness(selected ? 2 : 1),
                };
                if (selected) preview.SetResourceReference(Border.BorderBrushProperty, "S.Accent");
                else preview.BorderBrush = Ui.Brush("#33FFFFFF");

                var label = new TextBlock { Text = name, FontSize = 11.5, Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
                if (selected) label.FontWeight = FontWeights.SemiBold;
                else label.Themed(TextBlock.ForegroundProperty, "S.Sub");

                var stack = new StackPanel();
                stack.Children.Add(preview);
                stack.Children.Add(label);
                var button = new Button { Content = stack, Margin = new Thickness(0, 0, 8, 10), ToolTip = name }.Themed(StyleProperty, "Plain");
                button.Click += (_, _) =>
                {
                    _s.Fortschritt = styleId;
                    _s.NotifyChanged();
                    RebuildPage();
                };
                wrap.Children.Add(button);
            }
            root.Children.Add(wrap);
        }
        var total = ProgressTrack.Catalog.Sum(c => c.Items.Length);
        root.Children.Add(MoreButton(_showAllProgress, total, () => { _showAllProgress = !_showAllProgress; RebuildPage(); }, "Designs"));

        // Vorschauen anhalten, wenn das Fenster zu ist
        root.Unloaded += (_, _) =>
        {
            foreach (var t in FindAll<ProgressTrack>(root)) t.Live = false;
        };
        return root;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            if (FindChild<T>(child) is T found) return found;
        }
        return null;
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var x in FindAll<T>(child)) yield return x;
        }
    }

    private void BuildBehavior(StackPanel p)
    {
        p.Children.Add(Header("Verhalten"));
        p.Children.Add(Sub("Wie die Insel aufgeht, wie schnell sie reagiert und wo sie sitzt."));

        p.Children.Add(SectionTitle("Wenn du nichts machst"));
        var restNames = new Dictionary<string, string> { ["Zu"] = "Zugeklappt", ["Halb"] = "Halb offen", ["Offen"] = "Ganz offen" };
        p.Children.Add(Card(
            Row("Die Insel ist …", "Halb offen: grösser mit Cover/Uhr. Ganz offen: bleibt immer aufgeklappt.", null,
                below: Segmented(restNames.Values.ToArray(), restNames.TryGetValue(_s.Ruhezustand, out var rn) ? rn : "Zugeklappt", v =>
                {
                    _s.Ruhezustand = restNames.First(x => x.Value == v).Key;
                    _s.NotifyChanged();
                }), glyph: "\uE8A0")));

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
            Row("Oben in der Mitte ankleben", "Zieh sie ungefähr nach oben in die Mitte – sie fliegt hin und klebt an, wie beim MacBook.",
                Toggle(_s.MagnetOben, v => { _s.MagnetOben = v; _s.NotifyChanged(); }), glyph: "\uE718"),
            Row("Notch-Look", "Oben bündig am Rand mit geraden Ecken – wie die Kerbe beim MacBook. Aus = schwebt mit kleinem Abstand.",
                Toggle(_s.NotchLook, v => { _s.NotchLook = v; _s.NotifyChanged(); }), glyph: "\uE7F4"),
            Row("Am Rand andocken", "Zieh die Insel nah an einen Rand – sie klebt bündig dran und geht zur Mitte hin auf.", null, glyph: "\uE8A0"),
            Row("In den Rand eindrücken", "Schieb sie ganz in den Rand → nur ein schmaler Streifen bleibt. Klick drauf oder rausziehen holt sie zurück.",
                Toggle(_s.AmRandVerstecken, v => { _s.AmRandVerstecken = v; _s.NotifyChanged(); }), glyph: "\uE73F")));

        p.Children.Add(SectionTitle("Ruheanzeige"));
        p.Children.Add(Card(
            Row("Wenn keine Musik läuft", "Tipp: Mausrad über der zugeklappten Insel wechselt auch", null,
                below: Segmented(new[] { "Datum", "Uhr", "Wetter", "Akku", "Leistung", "Minimal" }, _s.RuheWidget,
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

        // ---------- Kontrollzentrum
        p.Children.Add(SectionTitle("Kontrollzentrum – Knöpfe wählen"));
        var chosen = _s.KontrollKnoepfe;
        var ccRows = new List<UIElement>
        {
            Row("Regler für Lautstärke & Helligkeit", null, Toggle(_s.KontrollRegler, v => { _s.KontrollRegler = v; _s.NotifyChanged(); }), glyph: "\uE767"),
        };
        // Zuerst die gewählten (in ihrer Reihenfolge), dann der Rest
        var ordered = chosen.Where(id => Island.Modules.ControlModule.Actions.Any(a => a.Id == id))
            .Concat(Island.Modules.ControlModule.Actions.Select(a => a.Id).Where(id => !chosen.Contains(id))).ToList();
        foreach (var id in ordered)
        {
            var action = Island.Modules.ControlModule.Actions.First(a => a.Id == id);
            var on = chosen.Contains(id);
            var g = new Grid { Margin = new Thickness(12, 6, 8, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new TextBlock { Text = action.Glyph, FontFamily = Ui.IconFont, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = action.Label, VerticalAlignment = VerticalAlignment.Center, FontSize = 13.5 };
            if (!on) { icon.Opacity = 0.45; label.Themed(TextBlock.ForegroundProperty, "S.Sub"); }
            Grid.SetColumn(label, 1);
            g.Children.Add(icon);
            g.Children.Add(label);
            if (on)
            {
                var index = chosen.IndexOf(id);
                var up = IconBtn("\uE70E", "Nach vorne", () => { Move(chosen, index, -1); _s.NotifyChanged(); RebuildPage(); });
                var down = IconBtn("\uE70D", "Nach hinten", () => { Move(chosen, index, 1); _s.NotifyChanged(); RebuildPage(); });
                Grid.SetColumn(up, 2);
                Grid.SetColumn(down, 3);
                g.Children.Add(up);
                g.Children.Add(down);
            }
            var toggle = Toggle(on, v =>
            {
                if (v)
                {
                    if (chosen.Count >= Island.Modules.ControlModule.MaxButtons) { RebuildPage(); return; }
                    chosen.Add(id);
                }
                else chosen.Remove(id);
                _s.NotifyChanged();
                RebuildPage();
            });
            toggle.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(toggle, 4);
            g.Children.Add(toggle);
            ccRows.Add(g);
        }
        ccRows.Add(Info($"Bis zu {Island.Modules.ControlModule.MaxButtons} Knöpfe. Tipp: Rechtsklick auf einen Knopf in der Insel öffnet die passende Windows-Einstellung."));
        p.Children.Add(Card(ccRows.ToArray()));
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
                 "„Alle Apps“ zeigt auch YouTube, Browser, Apple Music usw. Spotify hat immer Vorrang.\n" +
                 "Tipp: In der offenen Insel oben rechts auf „•••“ klicken → dort wechselst du jederzeit zwischen Spotify, Browser & Co.")));

        p.Children.Add(SectionTitle("Lyrics"));
        p.Children.Add(Card(
            Row("Unter dem Song", "Zeitgenau mitlaufend in der offenen Insel", Toggle(_s.Lyrics, v => { _s.Lyrics = v; _s.NotifyChanged(); }), glyph: "\uE8D2"),
            Row("In der kleinen Insel", "Zugeklappt läuft die aktuelle Zeile statt dem Titel", Toggle(_s.LyricsKompakt,
                v => { _s.LyricsKompakt = v; _s.NotifyChanged(); }), glyph: "\uE8A9"),
            Row("Neben der Insel", "Eigenes kleines Feld links daneben – lesbar, auch wenn die Insel zu ist", Toggle(_s.LyricsSeite,
                v => { _s.LyricsSeite = v; _s.NotifyChanged(); }), glyph: "\uE76B"),
            Info("Lyrics kommen von lrclib.net (kostenlos, ohne Konto). Dafür werden nur Songtitel, Künstler und Länge gesendet – sonst nichts.")));

        p.Children.Add(SectionTitle("Wetter"));
        var place = new TextBox { Text = _s.WetterOrt, Width = 220 }.Themed(StyleProperty, "DarkBox");
        var status = new TextBlock { FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap }.Themed(TextBlock.ForegroundProperty, "S.Sub");
        status.Text = string.IsNullOrWhiteSpace(_s.WetterOrtName) ? "Noch kein Ort gewählt." : "Gefunden: " + _s.WetterOrtName;
        var search = Btn("Suchen", "Primary", async () =>
        {
            status.Text = "Suche …";
            var ok = await WeatherService.SetPlaceAsync(_s, place.Text);
            status.Text = ok ? "Gefunden: " + _s.WetterOrtName : "Nichts gefunden – versuch es mit einer Stadt, z. B. „Basel“.";
        });
        search.Margin = new Thickness(8, 0, 0, 0);
        place.KeyDown += (_, e) => { if (e.Key == Key.Enter) search.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); };
        var placeRow = new StackPanel { Orientation = Orientation.Horizontal };
        placeRow.Children.Add(place);
        placeRow.Children.Add(search);
        var placePanel = new StackPanel();
        placePanel.Children.Add(placeRow);
        placePanel.Children.Add(status);
        p.Children.Add(Card(
            Row("Ort", "Für das Wetter-Widget und die Ruheanzeige „Wetter“", null, below: placePanel, glyph: "\uE707"),
            Info("Wetter kommt von open-meteo.com (kostenlos, ohne Konto). Gesendet wird nur der Ort – alle 30 Minuten, und nur wenn das Wetter benutzt wird.")));
    }

    // ================================================================== Aufgaben

    private void BuildTasks(StackPanel p)
    {
        p.Children.Add(Header("Aufgaben"));
        p.Children.Add(Sub("Deine To-dos. Ist eine fällig, klappt die Insel auf und erinnert dich. Alles bleibt auf diesem PC."));

        var rows = new List<UIElement>();
        var open = _s.Aufgaben.Where(a => !a.Erledigt).OrderBy(a => a.Faellig ?? DateTime.MaxValue).ToList();
        foreach (var task in open)
        {
            var t = task;
            var grid = new Grid { Margin = new Thickness(12, 8, 8, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = Box(t.Text, v => { t.Text = v; _s.NotifyChangedSoon(); });
            var due = new TextBlock
            {
                Text = t.Faellig is DateTime d ? Island.Modules.TasksModule.FormatDue(d) : "ohne Zeit",
                FontSize = 12, Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            }.Themed(TextBlock.ForegroundProperty, "S.Sub");
            var edit = IconBtn("\uE70F", "Zeit ändern", () => Island.Modules.TaskEditorWindow.Open(_s, t));
            var done = IconBtn("\uE73E", "Erledigt", () => { t.Erledigt = true; _s.NotifyChanged(); RebuildPage(); });
            Grid.SetColumn(due, 1);
            Grid.SetColumn(edit, 2);
            Grid.SetColumn(done, 3);
            grid.Children.Add(text);
            grid.Children.Add(due);
            grid.Children.Add(edit);
            grid.Children.Add(done);
            rows.Add(grid);
        }
        if (rows.Count == 0) rows.Add(Info("Keine offenen Aufgaben."));
        p.Children.Add(Card(rows.ToArray()));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Btn("+ Neue Aufgabe", "Primary", () => Island.Modules.TaskEditorWindow.Open(_s, null)));
        var clear = Btn("Erledigte löschen", "Secondary", () => { _s.Aufgaben.RemoveAll(a => a.Erledigt); _s.NotifyChanged(); RebuildPage(); });
        clear.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(clear);
        p.Children.Add(buttons);

        p.Children.Add(SectionTitle("Erinnerung"));
        p.Children.Add(Card(
            Row("Ton abspielen", "Kurzer Windows-Ton, wenn die Insel dich erinnert", Toggle(_s.AufgabenTon, v => { _s.AufgabenTon = v; _s.NotifyChanged(); }), glyph: "\uE767"),
            Info("Schneller geht's direkt in der Insel: Tab „Aufgaben“ → +. Kreis antippen = erledigt.")));

        // Liste neu zeigen, wenn im kleinen Fenster etwas geändert wurde
        void Changed() { if (_currentPage == "Aufgaben") Dispatcher.InvokeAsync(RebuildPage, DispatcherPriority.Background); }
        _s.Changed -= _tasksListener;
        _tasksListener = Changed;
        _s.Changed += _tasksListener;
    }

    private Action? _tasksListener;

    private void BuildApps(StackPanel p)
    {
        p.Children.Add(Header("Apps"));
        p.Children.Add(Sub("Kacheln im Apps-Tab. Am schnellsten: aus deinen installierten Apps wählen – " +
                           "oder Programme, Verknüpfungen und Ordner einfach aus dem Explorer in dieses Fenster ziehen."));

        // ---------- Schnell hinzufügen
        p.Children.Add(SectionTitle("Schnell hinzufügen"));
        var quick = new WrapPanel();
        var pick = Btn(_appPickerOpen ? "Liste schliessen" : "Aus installierten Apps wählen", "Primary", () =>
        {
            _appPickerOpen = !_appPickerOpen;
            RebuildPage();
        });
        quick.Children.Add(pick);
        quick.Children.Add(Spaced(Btn("+ Programm (.exe)", "Secondary", () =>
        {
            var dialog = new OpenFileDialog { Filter = "Programme (*.exe;*.lnk)|*.exe;*.lnk|Alle Dateien (*.*)|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            AddApp(dialog.FileName);
            _s.NotifyChanged();
            RebuildPage();
        })));
        quick.Children.Add(Spaced(Btn("+ Ordner", "Secondary", () =>
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog(this) != true) return;
            AddApp(dialog.FolderName);
            _s.NotifyChanged();
            RebuildPage();
        })));
        quick.Children.Add(Spaced(Btn("+ Webseite", "Secondary", () =>
        {
            _s.Schnellstart.Add(new SchnellstartEintrag { Name = "Webseite", Ziel = "https://" });
            _s.SaveSoon();
            RebuildPage();
        })));
        p.Children.Add(quick);
        p.Children.Add(new Border { Height = 14 });
        if (_appPickerOpen) p.Children.Add(Card(BuildAppPicker()));

        // ---------- Ordner
        p.Children.Add(SectionTitle("Ordner"));
        var folderRows = new List<UIElement>();
        for (int fi = 0; fi < _s.AppOrdner.Count; fi++)
        {
            var index = fi;
            var oldName = _s.AppOrdner[fi];
            var count = _s.Schnellstart.Count(a => string.Equals(a.Ordner, oldName, StringComparison.OrdinalIgnoreCase));
            var g = new Grid { Margin = new Thickness(12, 8, 8, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new TextBlock { Text = "\uE8B7", FontFamily = Ui.IconFont, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            var name = Box(oldName, v =>
            {
                var current = _s.AppOrdner[index];
                var newName = v.Trim();
                if (newName.Length == 0 || newName == current) return;
                foreach (var a in _s.Schnellstart.Where(a => string.Equals(a.Ordner, current, StringComparison.OrdinalIgnoreCase))) a.Ordner = newName;
                _s.AppOrdner[index] = newName;
                _s.NotifyChangedSoon();
            });
            var info = new TextBlock { Text = count == 1 ? "1 App" : $"{count} Apps", FontSize = 12, Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }
                .Themed(TextBlock.ForegroundProperty, "S.Sub");
            var up = IconBtn("\uE70E", "Nach vorne", () => { Move(_s.AppOrdner, index, -1); _s.NotifyChanged(); RebuildPage(); });
            var down = IconBtn("\uE70D", "Nach hinten", () => { Move(_s.AppOrdner, index, 1); _s.NotifyChanged(); RebuildPage(); });
            var del = IconBtn("\uE74D", "Ordner löschen (Apps bleiben)", () =>
            {
                var current = _s.AppOrdner[index];
                foreach (var a in _s.Schnellstart.Where(a => string.Equals(a.Ordner, current, StringComparison.OrdinalIgnoreCase))) a.Ordner = null;
                _s.AppOrdner.RemoveAt(index);
                _s.NotifyChanged();
                RebuildPage();
            });
            Grid.SetColumn(name, 1); Grid.SetColumn(info, 2); Grid.SetColumn(up, 3); Grid.SetColumn(down, 4); Grid.SetColumn(del, 5);
            g.Children.Add(icon); g.Children.Add(name); g.Children.Add(info); g.Children.Add(up); g.Children.Add(down); g.Children.Add(del);
            folderRows.Add(g);
        }
        var newFolder = new TextBox { Width = 200 }.Themed(StyleProperty, "DarkBox");
        var createFolder = Btn("Ordner erstellen", "Primary", () =>
        {
            var n = newFolder.Text.Trim();
            if (n.Length == 0) n = $"Ordner {_s.AppOrdner.Count + 1}";
            if (_s.AppOrdner.Contains(n, StringComparer.OrdinalIgnoreCase)) return;
            _s.AppOrdner.Add(n);
            _s.NotifyChanged();
            RebuildPage();
        });
        createFolder.Margin = new Thickness(8, 0, 0, 0);
        newFolder.KeyDown += (_, e) => { if (e.Key == Key.Enter) createFolder.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); };
        var createRow = new StackPanel { Orientation = Orientation.Horizontal };
        createRow.Children.Add(newFolder);
        createRow.Children.Add(createFolder);
        folderRows.Add(Row("Neuer Ordner", "z. B. „Schule“, „Games“, „Arbeit“ – Apps ordnest du unten mit dem Ordner-Knopf zu.", null, below: createRow, glyph: "\uE8F4"));
        p.Children.Add(Card(folderRows.ToArray()));

        // ---------- Deine Apps
        p.Children.Add(SectionTitle("Deine Apps"));
        var rows = new List<UIElement>();
        var list = _s.Schnellstart;
        for (int ai = 0; ai < list.Count; ai++)
        {
            var item = list[ai];
            var appIndex = ai;
            var grid = new Grid { Margin = new Thickness(12, 8, 8, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new Image { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
            icon.Source = AppIcons.Get(item.Ziel, item.Icon, item.Name, src => icon.Source = src);
            var name = Box(item.Name, v => { item.Name = v; _s.NotifyChangedSoon(); });
            var target = Box(item.Ziel, v => { item.Ziel = v; _s.NotifyChangedSoon(); });
            target.Margin = new Thickness(8, 0, 4, 0);
            var delete = IconBtn("", "Entfernen", () =>
            {
                _s.Schnellstart.Remove(item);
                _s.NotifyChanged();
                RebuildPage();
            });

            // Ordner wählen (kleines Menü)
            var folderBtn = IconBtn(string.IsNullOrWhiteSpace(item.Ordner) ? "\uE8B7" : "\uE838",
                string.IsNullOrWhiteSpace(item.Ordner) ? "In einen Ordner legen" : "Ordner: " + item.Ordner, () => { });
            folderBtn.Click += (_, _) =>
            {
                var menu = new ContextMenu { PlacementTarget = folderBtn };
                var none = new MenuItem { Header = "Kein Ordner", IsCheckable = true, IsChecked = string.IsNullOrWhiteSpace(item.Ordner) };
                none.Click += (_, _) => { item.Ordner = null; _s.NotifyChanged(); RebuildPage(); };
                menu.Items.Add(none);
                foreach (var folder in _s.AppOrdner)
                {
                    var f = folder;
                    var mi = new MenuItem { Header = f, IsCheckable = true, IsChecked = string.Equals(item.Ordner, f, StringComparison.OrdinalIgnoreCase) };
                    mi.Click += (_, _) => { item.Ordner = f; _s.NotifyChanged(); RebuildPage(); };
                    menu.Items.Add(mi);
                }
                if (_s.AppOrdner.Count == 0) menu.Items.Add(new MenuItem { Header = "(Erstell oben zuerst einen Ordner)", IsEnabled = false });
                menu.IsOpen = true;
            };
            var up = IconBtn("\uE70E", "Nach vorne", () => { Move(_s.Schnellstart, appIndex, -1); _s.NotifyChanged(); RebuildPage(); });
            var down = IconBtn("\uE70D", "Nach hinten", () => { Move(_s.Schnellstart, appIndex, 1); _s.NotifyChanged(); RebuildPage(); });

            Grid.SetColumn(name, 1);
            Grid.SetColumn(target, 2);
            Grid.SetColumn(folderBtn, 3);
            Grid.SetColumn(up, 4);
            Grid.SetColumn(down, 5);
            Grid.SetColumn(delete, 6);
            grid.Children.Add(icon);
            grid.Children.Add(name);
            grid.Children.Add(target);
            grid.Children.Add(folderBtn);
            grid.Children.Add(up);
            grid.Children.Add(down);
            grid.Children.Add(delete);
            rows.Add(grid);
        }
        if (rows.Count == 0) rows.Add(Info("Noch keine Apps. Wähl oben welche aus oder zieh sie hier hinein."));
        p.Children.Add(Card(rows.ToArray()));
    }

    private bool _showAllPresets, _showAllProgress;

    /// <summary>"Mehr anzeigen" / "Weniger anzeigen".</summary>
    private static FrameworkElement MoreButton(bool showingAll, int total, Action toggle, string what = "Vorlagen")
    {
        var b = IconTextBtn(showingAll ? "\uE70E" : "\uE70D", showingAll ? "Weniger anzeigen" : $"Alle {total} {what} zeigen", "Secondary", toggle);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.Margin = new Thickness(14, 2, 0, 12);
        return b;
    }

    /// <summary>Knopf mit Symbol + Text (Symbol-Schrift nur fürs Symbol, sonst gibt's Kästchen).</summary>
    private static Button IconTextBtn(string glyph, string text, string styleKey, Action action)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content }.Themed(StyleProperty, styleKey);
        button.Click += (_, _) => action();
        return button;
    }

    private static void Move<T>(List<T> list, int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || index >= list.Count || target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]);
    }

    private static Button Spaced(Button b)
    {
        b.Margin = new Thickness(8, 0, 0, 8);
        return b;
    }

    /// <summary>Fügt eine App, Verknüpfung, Datei oder einen Ordner hinzu (ohne Doppelte).</summary>
    private void AddApp(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (_s.Schnellstart.Any(x => string.Equals(x.Ziel, path, StringComparison.OrdinalIgnoreCase))) return;
        var name = Directory.Exists(path) ? Path.GetFileName(path.TrimEnd('\\')) : Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name)) name = path;
        _s.Schnellstart.Add(new SchnellstartEintrag { Name = name, Ziel = path });
    }

    /// <summary>Durchsuchbare Liste aller Programme aus dem Startmenü.</summary>
    private FrameworkElement BuildAppPicker()
    {
        var apps = InstalledApps();
        var panel = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        // Store-Apps (z. B. Spotify, WhatsApp aus dem Microsoft Store) laden im Hintergrund nach
        if (_storeApps == null)
        {
            _ = Task.Run(async () =>
            {
                var store = await AppIcons.StoreAppsAsync();
                await Dispatcher.InvokeAsync(() =>
                {
                    _storeApps = store.Select(a => (a.Name, a.Target)).ToList();
                    if (_appPickerOpen && _currentPage == "Apps") RebuildPage();
                });
            });
        }
        panel.Children.Add(new TextBlock { Text = "Suchen …", FontSize = 11.5, Margin = new Thickness(2, 0, 0, 4) }.Themed(TextBlock.ForegroundProperty, "S.Sub"));
        var search = new TextBox { Text = _appSearch }.Themed(StyleProperty, "DarkBox");
        panel.Children.Add(search);

        var list = new StackPanel();
        var scroll = new ScrollViewer
        {
            Height = 300,
            Margin = new Thickness(0, 8, 0, 0),
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Tag = "inner",
        };
        SmoothScroller.Attach(scroll, 0.32);
        panel.Children.Add(scroll);

        void Fill()
        {
            list.Children.Clear();
            var q = _appSearch.Trim();
            var shown = 0;
            foreach (var (name, path) in apps)
            {
                if (q.Length > 0 && !name.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
                list.Children.Add(AppPickerRow(name, path));
                if (++shown >= 200) break;
            }
            if (shown == 0) list.Children.Add(Info(apps.Count == 0 ? "Keine Programme gefunden." : "Nichts gefunden."));
            if (_storeApps == null) list.Children.Add(Info("Store-Apps werden geladen …"));
        }

        search.TextChanged += (_, _) => { _appSearch = search.Text; Fill(); };
        Fill();
        return panel;
    }

    private FrameworkElement AppPickerRow(string name, string path)
    {
        var added = _s.Schnellstart.Any(x => string.Equals(x.Ziel, path, StringComparison.OrdinalIgnoreCase));
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Image { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        // Symbole erst nach dem Anzeigen laden → Liste erscheint sofort
        Dispatcher.InvokeAsync(() => icon.Source ??= AppIcons.Get(path, null, name, src => icon.Source = src), DispatcherPriority.Background);
        var text = new TextBlock { Text = name, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var state = new TextBlock
        {
            Text = added ? "" : "", FontFamily = Ui.IconFont, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0),
        }.Themed(TextBlock.ForegroundProperty, added ? "S.Green" : "S.Accent");
        Grid.SetColumn(text, 1);
        Grid.SetColumn(state, 2);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(state);

        var row = new Border { Child = grid, Padding = new Thickness(8, 6, 4, 6), CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "S.Control");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (added)
                _s.Schnellstart.RemoveAll(x => string.Equals(x.Ziel, path, StringComparison.OrdinalIgnoreCase));
            else
                _s.Schnellstart.Add(new SchnellstartEintrag { Name = name, Ziel = path });
            _s.NotifyChanged();
            RebuildPage();
        };
        row.ToolTip = added ? "Klicken zum Entfernen" : "Klicken zum Hinzufügen";
        return row;
    }

    /// <summary>Alle Verknüpfungen aus dem Startmenü (für dich und für alle Benutzer).</summary>
    private static List<(string Name, string Path)>? _storeApps;

    /// <summary>Verknüpfungen aus dem Startmenü + Microsoft-Store-Apps (sobald geladen), ohne Doppelte.</summary>
    private static List<(string Name, string Path)> InstalledApps()
    {
        var all = LinkApps().ToList();
        if (_storeApps != null)
        {
            var names = new HashSet<string>(all.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
            all.AddRange(_storeApps.Where(a => names.Add(a.Name)));
        }
        return all.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static List<(string Name, string Path)> LinkApps()
    {
        if (_installedApps != null) return _installedApps;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var skip = new[] { "uninstall", "deinstall", "entfern", "readme", "hilfe", "help", "website", "documentation", "dokumentation", "release notes" };
        var dirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        };
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var dir in dirs)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*.lnk", options))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (skip.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
                    result.TryAdd(name, file);
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }
        _installedApps = result.OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase).Select(x => (x.Key, x.Value)).ToList();
        return _installedApps;
    }


    private void BuildKeys(StackPanel p)
    {
        p.Children.Add(Header("Tastenkürzel"));
        p.Children.Add(Sub("Dein Spickzettel im Tasten-Tab. Am einfachsten: beliebte Kürzel antippen – " +
                           "oder eigene mit dem Aufnahme-Knopf direkt über die Tastatur eintragen."));

        // ---------- Beliebte Kürzel
        p.Children.Add(SectionTitle("Beliebte Windows-Kürzel – antippen zum Hinzufügen"));
        var lib = new WrapPanel { Margin = new Thickness(12, 12, 6, 6) };
        foreach (var (keys, text) in KeyLibrary)
        {
            var k = keys;
            var t = text;
            var has = _s.Tastenkuerzel.Any(x => NormalizeKeys(x.Tasten) == NormalizeKeys(k));
            var chip = Btn($"{(has ? "✓" : "+")}  {k}  ·  {t}", has ? "Primary" : "Secondary", () =>
            {
                if (has) _s.Tastenkuerzel.RemoveAll(x => NormalizeKeys(x.Tasten) == NormalizeKeys(k));
                else _s.Tastenkuerzel.Add(new TastenEintrag { Tasten = k, Beschreibung = t });
                _s.NotifyChanged();
                RebuildPage();
            });
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.FontSize = 12;
            lib.Children.Add(chip);
        }
        p.Children.Add(Card(lib));

        // ---------- Deine Kürzel
        p.Children.Add(SectionTitle("Deine Kürzel"));
        var rows = new List<UIElement>();
        TextBox? startRecordingOn = null;
        foreach (var entry in _s.Tastenkuerzel.ToList())
        {
            var item = entry;
            var grid = new Grid { Margin = new Thickness(12, 8, 8, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBox keys = null!;
            keys = Box(item.Tasten, v =>
            {
                if (ReferenceEquals(_recordBox, keys)) return; // während der Aufnahme nichts speichern
                item.Tasten = v;
                _s.NotifyChangedSoon();
            });
            var record = IconBtn("", "Tasten aufnehmen (einfach drücken)", () => StartRecording(keys));
            record.Margin = new Thickness(4, 0, 0, 0);
            var text = Box(item.Beschreibung, v => { item.Beschreibung = v; _s.NotifyChangedSoon(); });
            text.Margin = new Thickness(6, 0, 4, 0);
            var delete = IconBtn("", "Entfernen", () =>
            {
                _s.Tastenkuerzel.Remove(item);
                _s.NotifyChanged();
                RebuildPage();
            });

            Grid.SetColumn(record, 1);
            Grid.SetColumn(text, 2);
            Grid.SetColumn(delete, 3);
            grid.Children.Add(keys);
            grid.Children.Add(record);
            grid.Children.Add(text);
            grid.Children.Add(delete);
            rows.Add(grid);
            if (ReferenceEquals(item, _recordNext)) startRecordingOn = keys;
        }
        if (rows.Count == 0) rows.Add(Info("Noch keine Tastenkürzel. Tipp oben welche an oder nimm eigene auf."));
        p.Children.Add(Card(rows.ToArray()));

        p.Children.Add(Btn("⏺  Neues Kürzel aufnehmen", "Primary", () =>
        {
            var entry = new TastenEintrag { Tasten = "", Beschreibung = "Neues Kürzel" };
            _s.Tastenkuerzel.Add(entry);
            _recordNext = entry;
            _s.SaveSoon();
            RebuildPage();
        }));
        p.Children.Add(new Border { Height = 10 });
        p.Children.Add(Info("Hinweis: Manche Win-Kürzel (z. B. Win + V) fängt Windows selbst ab, bevor sie hier ankommen. " +
                            "Die findest du oben in der Liste – oder tipp sie einfach ins Feld."));

        if (startRecordingOn != null)
        {
            _recordNext = null;
            var box = startRecordingOn;
            Dispatcher.InvokeAsync(() => StartRecording(box), DispatcherPriority.Loaded);
        }
    }

    private static string NormalizeKeys(string keys) => string.Join("+",
        keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToLowerInvariant();

    private void StartRecording(TextBox box)
    {
        if (_recordBox != null && !ReferenceEquals(_recordBox, box)) _recordBox.Text = _recordOld;
        _recordOld = box.Text;
        _recordBox = box;
        box.Text = "Drück jetzt die Tasten …";
        Focus(); // Tasten sollen beim Fenster ankommen, nicht im Textfeld
    }

    /// <summary>Während der Aufnahme: gedrückte Tasten als „Strg + Shift + T“ eintragen. Esc = abbrechen.</summary>
    private bool HandleRecording(KeyEventArgs e)
    {
        if (_recordBox == null) return false;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            var b = _recordBox;
            _recordBox = null;
            b.Text = _recordOld;
            return true;
        }

        var parts = new List<string>();
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) parts.Add("Win");
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Strg");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            _recordBox.Text = parts.Count > 0 ? string.Join(" + ", parts) + " + …" : "Drück jetzt die Tasten …";
            return true;
        }

        parts.Add(KeyName(key));
        var box = _recordBox;
        _recordBox = null;
        box.Text = string.Join(" + ", parts); // löst das Speichern aus
        return true;
    }

    private static string KeyName(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9) return ((int)(key - Key.D0)).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return "Num " + (int)(key - Key.NumPad0);
        return key switch
        {
            Key.Left => "Pfeil links",
            Key.Right => "Pfeil rechts",
            Key.Up => "Pfeil hoch",
            Key.Down => "Pfeil runter",
            Key.Space => "Leertaste",
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.Back => "Rücktaste",
            Key.Delete => "Entf",
            Key.Insert => "Einfg",
            Key.Home => "Pos1",
            Key.End => "Ende",
            Key.PageUp => "Bild hoch",
            Key.PageDown => "Bild runter",
            Key.PrintScreen => "Druck",
            Key.OemPeriod => ".",
            Key.OemComma => ",",
            Key.OemMinus => "-",
            Key.OemPlus => "Plus",
            Key.Add => "Num Plus",
            Key.Subtract => "Num Minus",
            Key.Multiply => "Num *",
            Key.Divide => "Num /",
            _ => key.ToString(),
        };
    }

    private void BuildInfo(StackPanel p)
    {
        p.Children.Add(Header("Info"));
        p.Children.Add(Sub("Selbst gebaut, Open Source – keine Daten werden irgendwohin gesendet."));

        p.Children.Add(Card(
            Row("Dynamic Island", "Version 1.7", null, glyph: ""),
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

    /// <summary>Kachel mit Vorschau. delete != null → kleines ✕ zum Löschen (eigene Designs).</summary>
    private FrameworkElement DesignTile(DesignEintrag d, Action? delete)
    {
        var c1 = Theme.ParseColor(d.Farbe1, Colors.Black);
        var c2 = Theme.ParseColor(d.Farbe2, Colors.Black);
        Brush fill = d.Stil switch
        {
            Theme.Gradient => Theme.Linear(d.VerlaufWinkel, (c1, 0), (c2, 1)),
            Theme.Glass => Theme.Linear(90, (Color.FromArgb(150, 255, 255, 255), 0), (Color.FromArgb(200, c1.R, c1.G, c1.B), 0.5), (Color.FromArgb(170, c1.R, c1.G, c1.B), 1)),
            Theme.Cover => Theme.Linear(120, (Theme.Blend(Color.FromRgb(12, 12, 15), Theme.ParseColor(d.Akzent, Colors.HotPink), 0.3), 0), (Color.FromRgb(12, 12, 15), 1)),
            Theme.PureGlass => Theme.Linear(90,
                (Color.FromArgb((byte)(200 - 140 * d.GlasKlarheit), 255, 255, 255), 0),
                (Color.FromArgb((byte)(120 - 90 * d.GlasKlarheit), 200, 205, 215), 1)),
            _ => new SolidColorBrush(c1),
        };

        var preview = new Border
        {
            Width = 74, Height = 26, CornerRadius = new CornerRadius(13), Background = fill,
            BorderBrush = Ui.Brush(d.Stil == Theme.PureGlass ? "#B0FFFFFF" : "#40FFFFFF"), BorderThickness = new Thickness(d.Stil == Theme.PureGlass ? 1.5 : 1),
            Child = new Border
            {
                Width = 16, Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 10, 0), Background = new SolidColorBrush(Theme.ParseColor(d.Akzent, Colors.White)),
            },
        };
        var label = new TextBlock
        {
            Text = d.Name, FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 90, TextTrimming = TextTrimming.CharacterEllipsis,
        }.Themed(TextBlock.ForegroundProperty, "S.Sub");

        var stack = new StackPanel();
        stack.Children.Add(preview);
        stack.Children.Add(label);

        var button = new Button { Content = stack, Margin = new Thickness(0, 0, 4, 4), ToolTip = d.Name }.Themed(StyleProperty, "Plain");
        button.Click += (_, _) =>
        {
            d.ApplyTo(_s);
            _s.NotifyChanged();
            RebuildPage();
        };
        if (delete == null) return button;

        var remove = IconBtn("\uE711", "Löschen", delete);
        remove.Width = remove.Height = 22;
        remove.FontSize = 9;
        remove.HorizontalAlignment = HorizontalAlignment.Right;
        remove.VerticalAlignment = VerticalAlignment.Top;
        var grid = new Grid();
        grid.Children.Add(button);
        grid.Children.Add(remove);
        return grid;
    }

    /// <summary>Schieberegler mit Beschriftung an beiden Enden (z. B. Wolkig ↔ Klar, Min ↔ Max).</summary>
    private static FrameworkElement RangeSlider(double min, double max, double value, string leftLabel, string rightLabel,
                                                Func<double, string> format, Action<double> set, double step, bool enabled)
    {
        var current = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max),
            TickFrequency = step, IsSnapToTickEnabled = true, SmallChange = step, LargeChange = step * 5,
            Margin = new Thickness(12, 0, 12, 0),
        }.Themed(StyleProperty, "DarkSlider");
        current.Text = format(slider.Value);
        slider.ValueChanged += (_, e) => { current.Text = format(e.NewValue); set(e.NewValue); };

        TextBlock End(string t) => new TextBlock { Text = t, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, "S.Sub");
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = End(leftLabel);
        var r = End(rightLabel);
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(r, 2);
        line.Children.Add(l);
        line.Children.Add(slider);
        line.Children.Add(r);

        var panel = new StackPanel { IsEnabled = enabled, Opacity = enabled ? 1 : 0.4 };
        panel.Children.Add(current);
        panel.Children.Add(line);
        return panel;
    }

    private FrameworkElement FontPicker()
    {
        var names = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(n => n).ToList();
        var view = CollectionViewSource.GetDefaultView(names);

        var search = new TextBox { Margin = new Thickness(0, 0, 0, 8) }.Themed(StyleProperty, "DarkBox");
        var list = new ListBox { Height = 200, ItemsSource = names }.Themed(StyleProperty, "FontList");
        // Pixelweise + weich gleitend scrollen statt Zeile für Zeile springen
        VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel);
        VirtualizingPanel.SetIsVirtualizing(list, true);
        list.Loaded += (_, _) =>
        {
            var sv = FindChild<ScrollViewer>(list);
            if (sv != null) SmoothScroller.Attach(sv, 0.32);
        };
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

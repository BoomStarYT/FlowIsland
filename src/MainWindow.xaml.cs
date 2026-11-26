using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Island.Core;
using Microsoft.Win32;

namespace Island;

public partial class MainWindow : Window, IIslandHost
{
    private const double CompactHeight = 36;
    private const double CompactRadius = CompactHeight / 2; // genau halbe Höhe = perfekte Pillenform
    private const double ExpandedWidth = 440;
    private const double ExpandedPadding = 24;   // Rand oben + unten im aufgeklappten Zustand
    private const double TabBarHeight = 36;
    private const double ActionsHeight = 78;
    private const double PillMargin = 18;        // Platz für den Schatten rund um die Insel
    private const double SnapDistance = 16;      // so nah am Rand → bündig andocken
    private const double HandleThickness = 6;    // eingedrückt: Breite des Streifens
    private const double HandlePeek = 12;        // eingedrückt + Maus drüber
    private const double HandleLength = 70;
    private const double MaxAlertHeight = 118;

    private static readonly Brush TuckHitBrush = Ui.Brush("#01000000"); // unsichtbar, aber anklickbar

    private readonly Settings _settings;
    private readonly IslandContext _context;
    private readonly List<IIslandModule> _modules = new();
    private readonly List<(string Name, string Icon)> _allModules = new();
    private readonly List<IIslandModule> _tabs = new();
    private readonly Dictionary<IIslandModule, Button> _tabButtons = new();

    private readonly DispatcherTimer _expandTimer;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _fullscreenTimer;
    private readonly DispatcherTimer _cacheTimer;
    private readonly DispatcherTimer _alertTimer = new();

    private IIslandModule? _compactModule;
    private IIslandModule? _activeTab;
    private SettingsWindow? _settingsWindow;
    private bool _expanded;
    private bool _medium;          // halb offen
    private bool _alertVisible;
    private double _alertHeight = 90;
    private bool _actionsVisible;
    private bool _hiddenForFullscreen;
    private bool _maybeDrag;
    private bool _isDragging;
    private bool _ignoreHoverUntilLeave; // nach dem Ziehen nicht sofort aufklappen
    private Point _downPos;
    private HorizontalAlignment _alignH = HorizontalAlignment.Center;
    private VerticalAlignment _alignV = VerticalAlignment.Top;
    private IntPtr _hwnd;

    /// <summary>RAM-Begrenzer (wird in den Einstellungen bedient).</summary>
    public RamLimiter Ram { get; }

    /// <summary>Alle verfügbaren Module (auch ausgeschaltete) – für die Einstellungen.</summary>
    public IReadOnlyList<(string Name, string Icon)> AllModules => _allModules;

    private string Edge => _settings.AmRand ?? "";
    private bool Tucked => _settings.Versteckt && Edge.Length > 0;
    private bool VerticalEdge => Edge is "Links" or "Rechts";
    private string RestMode => _settings.Ruhezustand ?? "Zu";

    public MainWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        _context = new IslandContext(settings, Dispatcher, this);
        Ram = new RamLimiter(settings);

        _expandTimer = new DispatcherTimer();
        _expandTimer.Tick += (_, _) => { _expandTimer.Stop(); Expand(); };

        _collapseTimer = new DispatcherTimer();
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (_alertVisible) return; // Meldung schliesst sich selbst
            if (!Root.IsMouseOver && Mouse.Captured == null && !(Root.ContextMenu?.IsOpen ?? false)) Collapse();
        };

        _alertTimer.Tick += (_, _) =>
        {
            if (Root.IsMouseOver) return; // erst zu, wenn die Maus weg ist
            _alertTimer.Stop();
            HideAlert();
        };

        // Alle 1.5 s kurz schauen, ob ein Spiel/Video im Vollbild läuft (kostet praktisch nichts)
        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();

        // Nach einer Animation den Bild-Zwischenspeicher wieder abschalten (gestochen scharfer Text in Ruhe)
        _cacheTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _cacheTimer.Tick += (_, _) =>
        {
            _cacheTimer.Stop();
            ExpandedRoot.CacheMode = null;
            CompactHost.CacheMode = null;
            MediumHost.CacheMode = null;
        };

        // Maus-Ereignisse am ganzen Fenster: normal ist nur die Insel "anfassbar",
        // eingedrückt zusätzlich ein unsichtbarer Bereich um den Streifen (leichter zu treffen).
        Root.MouseEnter += OnRootMouseEnter;
        Root.MouseLeave += OnRootMouseLeave;
        Root.AddHandler(Mouse.LostMouseCaptureEvent, new MouseEventHandler((_, _) =>
        {
            if (_expanded && !Root.IsMouseOver) _collapseTimer.Start();
        }));
        Root.MouseLeftButtonDown += OnPillMouseDown;
        Root.MouseMove += OnPillMouseMove;
        Root.MouseLeftButtonUp += OnPillMouseUp;
        Root.ContextMenu = BuildMenu();

        // Dateien/Programme auf die Insel ziehen → landen bei den Apps
        Root.AllowDrop = true;
        Root.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Root.Drop += OnFilesDropped;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeToolWindow(_hwnd);
            ApplyPosition();
        };
        Loaded += (_, _) =>
        {
            if (Tucked) ShowTucked(animate: false);
            else ApplyPosition();
            UpdateFullscreenTimer();
            if (RestMode == "Offen") Dispatcher.InvokeAsync(() => Expand(), DispatcherPriority.Background);
        };
        Theme.CoverChanged += OnCoverChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Closed += OnClosed;
        _settings.Changed += OnSettingsChanged;

        LoadModules();
        BuildTabs();
        BuildActions();
        ApplySettings();
        RefreshCompact();
    }

    // ================================================================== Module

    private void LoadModules()
    {
        var types = typeof(MainWindow).Assembly.GetTypes()
            .Where(t => typeof(IIslandModule).IsAssignableFrom(t)
                        && t is { IsAbstract: false, IsInterface: false }
                        && t.GetConstructor(Type.EmptyTypes) != null);

        foreach (var type in types)
        {
            try
            {
                var module = (IIslandModule)Activator.CreateInstance(type)!;
                if (!module.Enabled) { module.Dispose(); continue; }

                _allModules.Add((module.Name, module.Icon));
                if (_settings.DeaktivierteModule.Contains(module.Name, StringComparer.OrdinalIgnoreCase))
                {
                    module.Dispose();
                    continue;
                }

                module.Initialize(_context);
                _modules.Add(module);
            }
            catch (Exception ex) { App.Log(ex); }
        }

        _modules.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    private void BuildTabs()
    {
        foreach (var module in _modules)
        {
            FrameworkElement? view;
            try { view = module.ExpandedView; }
            catch (Exception ex) { App.Log(ex); continue; }
            if (view == null) continue;

            _tabs.Add(module);
            var button = new Button { Content = module.Icon, Style = Ui.Style("TabButton"), ToolTip = module.Name, Opacity = 0.4 };
            var m = module;
            button.Click += (_, _) => SelectTab(m);
            _tabButtons[module] = button;
            TabBar.Children.Add(button);
        }

        TabBar.Visibility = _tabs.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectTab(IIslandModule module)
    {
        if (ReferenceEquals(module, _activeTab)) return;

        var oldIndex = _activeTab == null ? -1 : _tabs.IndexOf(_activeTab);
        var newIndex = _tabs.IndexOf(module);
        var visible = _expanded && !_actionsVisible;

        if (visible) SafeCall(_activeTab, m => m.OnHidden());
        _activeTab = module;
        ExpandedHost.Content = module.ExpandedView;

        foreach (var (m, button) in _tabButtons)
            Anim.To(button, OpacityProperty, ReferenceEquals(m, module) ? 1.0 : 0.4, 180, Anim.Smooth);

        if (visible)
        {
            BeginTransition();
            SetContentHeight(ExpandedPillHeight(module));
            // Neuer Inhalt gleitet von der Seite rein
            var direction = newIndex >= oldIndex ? 1 : -1;
            Anim.To(ExpandedSlide, TranslateTransform.XProperty, 0, 420, Anim.Spring, from: 22 * direction);
            Anim.To(ExpandedHost, OpacityProperty, 1, 200, Anim.Smooth, from: 0);
            Anim.To(Pill, HeightProperty, ExpandedPillHeight(module), 440, Anim.Spring);
            SafeCall(module, m => m.OnShown());
        }
    }

    /// <summary>Entscheidet, welches Modul in der zugeklappten Insel zu sehen ist.</summary>
    public void RefreshCompact()
    {
        IIslandModule? winner = null;
        foreach (var m in _modules)
        {
            try { if (m.WantsCompact && m.CompactView != null) { winner = m; break; } }
            catch (Exception ex) { App.Log(ex); }
        }

        _compactModule = winner;
        CompactHost.Width = Math.Max(20, CompactPillWidth() - 18);
        var view = winner?.CompactView;
        if (!ReferenceEquals(CompactHost.Content, view))
        {
            CompactHost.Content = view;
            if (!_expanded && !Tucked) Anim.To(CompactHost, OpacityProperty, 1, 280, Anim.Smooth, from: 0);
        }

        // Halb offen: grössere Ansicht vom gleichen Modul
        var medium = WantMedium();
        var mediumView = medium ? winner!.MediumView : null;
        if (medium)
        {
            MediumHost.Width = Math.Max(20, winner!.MediumWidth - 22);
            MediumHost.Height = Math.Max(20, winner.MediumHeight - 12);
        }
        if (!ReferenceEquals(MediumHost.Content, mediumView)) MediumHost.Content = mediumView;

        if (!_expanded && !Tucked && !_isDragging)
        {
            if (medium != _medium) SwapRestHosts(medium);
            Anim.To(Pill, WidthProperty, RestWidth(), 500, Anim.Spring);
            Anim.To(Pill, HeightProperty, RestHeight(), 500, Anim.Spring);
            ApplyCorners();
        }
        else _medium = medium && !Tucked;
    }

    /// <summary>"Halb offen" nur, wenn eingestellt und das Modul eine halbe Ansicht hat.</summary>
    private bool WantMedium() => RestMode == "Halb" && _compactModule?.MediumView != null && !Tucked;

    private double RestWidth() => _medium && _compactModule != null ? _compactModule.MediumWidth : CompactPillWidth();
    private double RestHeight() => _medium && _compactModule != null ? _compactModule.MediumHeight : CompactHeight;

    /// <summary>Zwischen zugeklappt und halb offen überblenden.</summary>
    private void SwapRestHosts(bool medium)
    {
        _medium = medium;
        BeginTransition();
        var show = medium ? (FrameworkElement)MediumHost : CompactHost;
        var hide = medium ? (FrameworkElement)CompactHost : MediumHost;
        Anim.To(hide, OpacityProperty, 0, 120, null, () => { if (_medium == medium && !_expanded) hide.Visibility = Visibility.Collapsed; });
        show.Visibility = Visibility.Visible;
        Anim.To(show, OpacityProperty, 1, 260, Anim.Smooth, delayMs: 80);
    }

    /// <summary>Tab-Höhe hat sich geändert (z. B. Lyrics an/aus).</summary>
    public void RefreshLayout()
    {
        UpdateWindowSize();
        if (_expanded && !_actionsVisible && !_alertVisible && _activeTab != null)
        {
            BeginTransition();
            SetContentHeight(ExpandedPillHeight(_activeTab));
            Anim.To(Pill, HeightProperty, ExpandedPillHeight(_activeTab), 440, Anim.Spring);
        }
    }

    // ================================================================== Auf / Zu

    /// <summary>
    /// Während Animationen wird der Inhalt einmal als Bild zwischengespeichert. Die Grafikkarte
    /// verschiebt/blendet dann nur noch dieses Bild → deutlich flüssiger.
    /// </summary>
    private void BeginTransition()
    {
        var scale = Math.Max(1, Scale * DeviceScale());
        ExpandedRoot.CacheMode ??= new BitmapCache(scale) { SnapsToDevicePixels = true };
        CompactHost.CacheMode ??= new BitmapCache(scale) { SnapsToDevicePixels = true };
        MediumHost.CacheMode ??= new BitmapCache(scale) { SnapsToDevicePixels = true };
        _cacheTimer.Stop();
        _cacheTimer.Start();
    }

    private void Expand(bool forActions = false, bool forAlert = false)
    {
        if (_expanded || _hiddenForFullscreen || _isDragging || Tucked) return;
        if (_tabs.Count == 0 && !forActions && !forAlert) return;

        IIslandModule? tab = null;
        if (_tabs.Count > 0)
        {
            // Läuft Musik, öffnet sich der Musik-Tab. Sonst der zuletzt benutzte.
            tab = _compactModule != null && _tabs.Contains(_compactModule) ? _compactModule : _activeTab ?? _tabs[0];
            SelectTab(tab);
        }

        _expanded = true;
        _alertVisible = forAlert;
        _actionsVisible = !forAlert && (forActions || tab == null);
        var overlay = _actionsVisible || _alertVisible;
        BeginTransition();
        ApplyCorners();

        TabContent.Visibility = overlay ? Visibility.Hidden : Visibility.Visible;
        Anim.Set(TabContent, OpacityProperty, overlay ? 0 : 1);
        Anim.Set(ExpandedHost, OpacityProperty, 1);
        Anim.Set(ExpandedSlide, TranslateTransform.XProperty, 0);
        ActionsPanel.Visibility = _actionsVisible ? Visibility.Visible : Visibility.Collapsed;
        Anim.Set(ActionsPanel, OpacityProperty, _actionsVisible ? 1 : 0);
        AlertHost.Visibility = _alertVisible ? Visibility.Visible : Visibility.Collapsed;
        Anim.Set(AlertHost, OpacityProperty, _alertVisible ? 1 : 0);

        var height = _alertVisible ? _alertHeight + ExpandedPadding
                   : _actionsVisible ? ActionsHeight + ExpandedPadding
                   : ExpandedPillHeight(tab!);
        SetContentHeight(height);
        Anim.To(Pill, WidthProperty, ExpandedWidth, 480, Anim.Spring);
        Anim.To(Pill, HeightProperty, height, 480, Anim.Spring);

        Anim.To(CompactHost, OpacityProperty, 0, 90, null, () => { if (_expanded) CompactHost.Visibility = Visibility.Collapsed; });
        Anim.To(MediumHost, OpacityProperty, 0, 90, null, () => { if (_expanded) MediumHost.Visibility = Visibility.Collapsed; });
        ExpandedRoot.Visibility = Visibility.Visible;
        Anim.To(ExpandedRoot, OpacityProperty, 1, 220, Anim.Smooth, delayMs: 60);
        Anim.To(ExpandedScale, ScaleTransform.ScaleXProperty, 1, 480, Anim.Spring, from: 0.92);
        Anim.To(ExpandedScale, ScaleTransform.ScaleYProperty, 1, 480, Anim.Spring, from: 0.92);

        if (!_actionsVisible && !_alertVisible) SafeCall(tab, m => m.OnShown());
    }

    /// <summary>Zuklappen. Bei "ganz offen" bleibt sie offen (ausser force, z. B. Vollbild).</summary>
    private void Collapse(bool force = false)
    {
        if (!_expanded) return;
        if (!force && RestMode == "Offen" && _tabs.Count > 0 && !Tucked)
        {
            if (_actionsVisible) HideActions();
            if (_alertVisible) HideAlert();
            return;
        }
        _expanded = false;
        if (!_actionsVisible && !_alertVisible) SafeCall(_activeTab, m => m.OnHidden());
        _actionsVisible = false;
        _alertVisible = false;
        _alertTimer.Stop();
        _medium = WantMedium();
        BeginTransition();
        ApplyCorners();

        Anim.To(Pill, WidthProperty, RestWidth(), 420, Anim.SpringSoft);
        Anim.To(Pill, HeightProperty, RestHeight(), 420, Anim.SpringSoft);

        Anim.To(ExpandedRoot, OpacityProperty, 0, 100, null, () =>
        {
            if (_expanded) return;
            ExpandedRoot.Visibility = Visibility.Collapsed;
            ActionsPanel.Visibility = Visibility.Collapsed;
            AlertHost.Visibility = Visibility.Collapsed;
            AlertHost.Content = null;
            TabContent.Visibility = Visibility.Visible;
        });
        Anim.To(ExpandedScale, ScaleTransform.ScaleXProperty, 0.94, 200, Anim.Smooth);
        Anim.To(ExpandedScale, ScaleTransform.ScaleYProperty, 0.94, 200, Anim.Smooth);

        var host = _medium ? (FrameworkElement)MediumHost : CompactHost;
        (_medium ? (FrameworkElement)CompactHost : MediumHost).Visibility = Visibility.Collapsed;
        host.Visibility = Visibility.Visible;
        Anim.To(host, OpacityProperty, 1, 220, Anim.Smooth, delayMs: 140);
    }

    // ================================================================== Meldungen (IIslandHost)

    public void ShowAlert(FrameworkElement view, double height, int seconds)
    {
        if (_hiddenForFullscreen || _isDragging) return;
        if (Tucked) Untuck();
        _alertHeight = Math.Clamp(height, 40, MaxAlertHeight);
        AlertHost.Content = view;
        AlertHost.Height = _alertHeight;
        _alertTimer.Stop();
        if (seconds > 0)
        {
            _alertTimer.Interval = TimeSpan.FromSeconds(seconds);
            _alertTimer.Start();
        }

        if (!_expanded)
        {
            Expand(forAlert: true);
        }
        else
        {
            if (!_actionsVisible && !_alertVisible) SafeCall(_activeTab, m => m.OnHidden());
            _actionsVisible = false;
            _alertVisible = true;
            BeginTransition();
            SetContentHeight(_alertHeight + ExpandedPadding);
            TabContent.Visibility = Visibility.Hidden;
            ActionsPanel.Visibility = Visibility.Collapsed;
            AlertHost.Visibility = Visibility.Visible;
            Anim.To(AlertHost, OpacityProperty, 1, 220, Anim.Smooth, delayMs: 60, from: 0);
            Anim.To(Pill, HeightProperty, _alertHeight + ExpandedPadding, 440, Anim.Spring);
        }
        // kleines "Hallo"-Wackeln
        Anim.To(PillSquash, ScaleTransform.ScaleXProperty, 1, 600, Anim.Spring, from: 1.06);
    }

    public void HideAlert()
    {
        if (!_alertVisible) return;
        _alertTimer.Stop();
        var stayOpen = _activeTab != null && (Root.IsMouseOver || RestMode == "Offen");
        if (!stayOpen)
        {
            Collapse(force: true);
            return;
        }
        // Maus ist drüber (oder "ganz offen") → zurück zum Tab
        _alertVisible = false;
        BeginTransition();
        SetContentHeight(ExpandedPillHeight(_activeTab!));
        Anim.To(AlertHost, OpacityProperty, 0, 100, null, () => { if (!_alertVisible) { AlertHost.Visibility = Visibility.Collapsed; AlertHost.Content = null; } });
        TabContent.Visibility = Visibility.Visible;
        Anim.To(TabContent, OpacityProperty, 1, 200, Anim.Smooth, delayMs: 60);
        Anim.To(Pill, HeightProperty, ExpandedPillHeight(_activeTab!), 440, Anim.Spring);
        SafeCall(_activeTab, m => m.OnShown());
    }

    /// <summary>Zweiter Start der App oder "wo ist sie?" → Insel kommt sichtbar zurück.</summary>
    public void BringBack()
    {
        if (_hiddenForFullscreen) { _hiddenForFullscreen = false; Show(); }
        if (Tucked) Untuck();
        var device = DeviceScale();
        if (Native.GetWorkArea((Left + Width / 2) * device, (Top + Height / 2) * device, nearest: false) == null) ResetPosition();
        Topmost = false;
        Topmost = true;
        Anim.To(PillSquash, ScaleTransform.ScaleXProperty, 1, 700, Anim.Spring, from: 1.15);
        Anim.To(PillSquash, ScaleTransform.ScaleYProperty, 1, 700, Anim.Spring, from: 0.85);
        if (!_expanded) Expand();
        if (!Root.IsMouseOver) _collapseTimer.Start();
    }

    /// <summary>Zuklappen ohne Animation (z. B. beim Ziehen).</summary>
    private void CollapseInstant()
    {
        if (_expanded && !_actionsVisible) SafeCall(_activeTab, m => m.OnHidden());
        _expanded = false;
        _actionsVisible = false;
        _alertVisible = false;
        _alertTimer.Stop();
        _medium = false;

        Anim.Set(Pill, WidthProperty, CompactPillWidth());
        Anim.Set(Pill, HeightProperty, CompactHeight);
        Anim.Set(ExpandedRoot, OpacityProperty, 0);
        ExpandedRoot.Visibility = Visibility.Collapsed;
        ActionsPanel.Visibility = Visibility.Collapsed;
        AlertHost.Visibility = Visibility.Collapsed;
        AlertHost.Content = null;
        MediumHost.Visibility = Visibility.Collapsed;
        Anim.Set(MediumHost, OpacityProperty, 0);
        TabContent.Visibility = Visibility.Visible;
        Anim.Set(TabContent, OpacityProperty, 1);
        CompactHost.Visibility = Visibility.Visible;
        Anim.Set(CompactHost, OpacityProperty, 1);
        if (Root.Background != null) ApplyTuckLook(false);
        Root.Background = null;
        ApplyCorners();
    }

    /// <summary>Eingedrückt leuchtet der Streifen in der Akzentfarbe, damit man ihn sieht.</summary>
    private void ApplyTuckLook(bool tucked)
    {
        if (tucked)
        {
            PillSheen.SetResourceReference(Border.BackgroundProperty, "IslandAccent");
            PillSheen.Opacity = 0.9;
        }
        else
        {
            PillSheen.Opacity = 1;
            Theme.Apply(_settings, PillBg, PillSheen, PillRim, PillShadow);
        }
    }

    // ================================================================== Doppelklick-Menü

    private void BuildActions()
    {
        ActionsPanel.Children.Add(ActionButton("", "Einstellungen", null, OpenSettings));
        ActionsPanel.Children.Add(ActionButton("", "Zurück", null, HideActions));
        ActionsPanel.Children.Add(ActionButton("", "Schliessen", "#FF453A", () => Application.Current.Shutdown()));
    }

    private static Button ActionButton(string glyph, string label, string? colorHex, Action action)
    {
        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = Ui.IconFont,
            FontSize = 17,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var circle = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Child = icon };
        if (colorHex != null)
        {
            circle.Background = Ui.Brush(colorHex);
            icon.Foreground = Brushes.White;
        }
        else circle.Themed(Border.BackgroundProperty, "IslandChip");

        var text = new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, "IslandSubText");

        var stack = new StackPanel();
        stack.Children.Add(circle);
        stack.Children.Add(text);

        var button = new Button { Content = stack, Style = Ui.Style("TileButton"), Width = 88, Height = 78, Margin = new Thickness(4, 0, 4, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private void ShowActions()
    {
        if (_isDragging) return;
        _expandTimer.Stop();
        _collapseTimer.Stop();
        if (Tucked) { Untuck(); return; }

        if (!_expanded) { Expand(forActions: true); return; }
        if (_actionsVisible) return;

        _actionsVisible = true;
        SafeCall(_activeTab, m => m.OnHidden());
        BeginTransition();
        SetContentHeight(ActionsHeight + ExpandedPadding);
        Anim.To(TabContent, OpacityProperty, 0, 120, null, () => { if (_actionsVisible) TabContent.Visibility = Visibility.Hidden; });
        ActionsPanel.Visibility = Visibility.Visible;
        Anim.To(ActionsPanel, OpacityProperty, 1, 220, Anim.Smooth, delayMs: 60, from: 0);
        Anim.To(Pill, HeightProperty, ActionsHeight + ExpandedPadding, 440, Anim.Spring);
    }

    private void HideActions()
    {
        if (!_actionsVisible) return;
        if (_activeTab == null) { Collapse(force: true); return; }

        _actionsVisible = false;
        BeginTransition();
        SetContentHeight(ExpandedPillHeight(_activeTab));
        Anim.To(ActionsPanel, OpacityProperty, 0, 100, null, () => { if (!_actionsVisible) ActionsPanel.Visibility = Visibility.Collapsed; });
        TabContent.Visibility = Visibility.Visible;
        Anim.To(TabContent, OpacityProperty, 1, 200, Anim.Smooth, delayMs: 60);
        Anim.To(Pill, HeightProperty, ExpandedPillHeight(_activeTab), 440, Anim.Spring);
        SafeCall(_activeTab, m => m.OnShown());
    }

    // ================================================================== Maus

    private void OnRootMouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        if (_isDragging) return;

        if (Tucked)
        {
            // Streifen kommt ein bisschen raus ("guck mal, ich bin hier")
            Anim.To(Pill, VerticalEdge ? WidthProperty : HeightProperty, HandlePeek, 320, Anim.Spring);
            return;
        }

        if (_ignoreHoverUntilLeave) return;
        if (!_expanded && !_settings.OeffnenPerKlick) _expandTimer.Start();
    }

    private void OnRootMouseLeave(object sender, MouseEventArgs e)
    {
        _expandTimer.Stop();
        _ignoreHoverUntilLeave = false;

        if (Tucked)
        {
            if (!_isDragging) Anim.To(Pill, VerticalEdge ? WidthProperty : HeightProperty, HandleThickness, 320, Anim.Spring);
            return;
        }

        if (_expanded && Mouse.Captured == null && !_alertVisible) _collapseTimer.Start();
    }

    private void OnPillMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            _maybeDrag = false;
            ShowActions();
            e.Handled = true;
            return;
        }
        _maybeDrag = true;
        _downPos = e.GetPosition(this);
    }

    private void OnPillMouseMove(object sender, MouseEventArgs e)
    {
        if (!_maybeDrag || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _downPos.X) < 4 && Math.Abs(p.Y - _downPos.Y) < 4) return;
        _maybeDrag = false;
        StartDrag(p);
    }

    private void OnPillMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_maybeDrag) return;
        _maybeDrag = false;

        if (Tucked) { Untuck(); return; }               // Klick auf den Streifen = rausholen
        if (_settings.OeffnenPerKlick && !_expanded) Expand();
    }

    // ================================================================== Ziehen

    /// <summary>
    /// Verschieben: Insel sofort zuklappen und das Fenster auf die kleine Insel verkleinern.
    /// Dann übernimmt Windows das Verschieben selbst → so flüssig wie ein normales Fenster.
    /// </summary>
    private void StartDrag(Point cursorInWindow)
    {
        _isDragging = true;
        _expandTimer.Stop();
        _collapseTimer.Stop();

        var cursorX = Left + cursorInWindow.X;
        var cursorY = Top + cursorInWindow.Y;

        // Vom Rand lösen
        _settings.Versteckt = false;
        _settings.AmRand = "";
        CollapseInstant();

        var scale = Scale;
        _alignH = HorizontalAlignment.Center;
        _alignV = VerticalAlignment.Top;
        Pill.Margin = new Thickness(PillMargin);
        ApplyAlignment();
        Width = Math.Ceiling(CompactPillWidth() * scale + 2 * PillMargin);
        Height = Math.Ceiling(CompactHeight * scale + 2 * PillMargin);
        Left = cursorX - Width / 2;
        Top = cursorY - Height / 2;

        try { DragMove(); }
        catch (InvalidOperationException) { /* Maus schon losgelassen */ }

        _ignoreHoverUntilLeave = true;
        if (!Reanchor()) FinishMove();
    }

    /// <summary>Nach dem Verschieben/Ankleben: wieder in den gewünschten Ruhezustand.</summary>
    private void FinishMove()
    {
        _isDragging = false;
        if (Tucked) return;
        RefreshCompact();
        if (RestMode == "Offen") Expand();
    }

    /// <summary>
    /// Nach dem Verschieben entscheiden:
    ///  • ganz in den Rand gedrückt → eindrücken (schmaler Streifen)
    ///  • nah am Rand → bündig andocken (Ecken auf der Randseite werden gerade)
    ///  • oben in der Nähe der Mitte → einrasten
    ///  • sonst: frei schwebend
    /// Die Insel geht immer zur Bildschirmmitte hin auf.
    /// </summary>
    private bool Reanchor()
    {
        var scale = Scale;
        var cw = CompactPillWidth() * scale;
        var ch = CompactHeight * scale;
        var x = Left + (Width - cw) / 2; // Ziehen-Fenster: Insel mittig, oben mit PillMargin
        var y = Top + PillMargin;

        var wa = WorkAreaAt(x + cw / 2, y + ch / 2);

        // 1) Wurde sie über den Rand hinaus geschoben? (Maus am Bildschirmrand → halbe Insel draussen)
        var overLeft = wa.Left - x;
        var overRight = x + cw - wa.Right;
        var overTop = wa.Top - y;
        var overBottom = y + ch - wa.Bottom;
        string pushed = "";
        double best = 0;
        if (overLeft > cw * 0.3 && overLeft > best) { pushed = "Links"; best = overLeft; }
        if (overRight > cw * 0.3 && overRight > best) { pushed = "Rechts"; best = overRight; }
        if (overTop > ch * 0.4 && overTop > best) { pushed = "Oben"; best = overTop; }
        if (overBottom > ch * 0.4 && overBottom > best) { pushed = "Unten"; }

        if (pushed.Length > 0 && _settings.AmRandVerstecken)
        {
            Tuck(pushed, x + cw / 2, y + ch / 2, wa);
            return false;
        }

        // 2a) Magnet wie beim MacBook: in die Nähe von oben-Mitte gezogen → fliegt sanft hin und klebt an
        var centerX = wa.Left + wa.Width / 2;
        if (_settings.MagnetOben && (pushed is "" or "Oben")
            && Math.Abs(x + cw / 2 - centerX) < 150 && y - wa.Top < 90)
        {
            var notch = _settings.NotchLook;
            var tx = centerX - cw / 2;
            var ty = notch ? wa.Top : wa.Top + Math.Max(0, _settings.AbstandOben);
            FlyTo(x, y, tx, ty, () =>
            {
                PlaceCompact(tx, ty, cw, ch, wa, notch ? "Oben" : "");
                // "Plopp" beim Ankleben
                Anim.To(PillSquash, ScaleTransform.ScaleXProperty, 1, 620, Anim.Spring, from: 1.12);
                Anim.To(PillSquash, ScaleTransform.ScaleYProperty, 1, 620, Anim.Spring, from: 0.78);
                FinishMove();
            });
            return true;
        }

        // 2) Im Bildschirm halten
        x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - cw));
        y = Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - ch));

        // 3) Nah am Rand → bündig andocken (der nächste Rand gewinnt)
        var distances = new (string Edge, double Distance)[]
        {
            ("Links", x - wa.Left), ("Rechts", wa.Right - (x + cw)),
            ("Oben", y - wa.Top), ("Unten", wa.Bottom - (y + ch)),
        };
        var nearest = distances.OrderBy(d => d.Distance).First();
        var attached = pushed.Length > 0 || nearest.Distance <= SnapDistance ? (pushed.Length > 0 ? pushed : nearest.Edge) : "";
        switch (attached)
        {
            case "Links": x = wa.Left; break;
            case "Rechts": x = wa.Right - cw; break;
            case "Oben": y = wa.Top; break;
            case "Unten": y = wa.Bottom - ch; break;
        }

        PlaceCompact(x, y, cw, ch, wa, attached);
        return false;
    }

    /// <summary>Lässt das (kleine) Zieh-Fenster weich zum Ziel gleiten – mit Federung wie bei macOS.</summary>
    private void FlyTo(double x, double y, double tx, double ty, Action done)
    {
        var startLeft = Left;
        var startTop = Top;
        var endLeft = startLeft + (tx - x);
        var endTop = startTop + (ty - y);
        var duration = 420 * Anim.Tempo;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var ease = new SpringEase { Damping = 0.78, Frequency = 1.4 };

        // Beim Fliegen leicht "zusammenfalten"
        Anim.To(PillSquash, ScaleTransform.ScaleXProperty, 0.9, 200, Anim.Smooth);
        Anim.To(PillSquash, ScaleTransform.ScaleYProperty, 0.92, 200, Anim.Smooth);

        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) =>
        {
            var t = Math.Min(1, watch.Elapsed.TotalMilliseconds / duration);
            var e = ease.Ease(t);
            Left = startLeft + (endLeft - startLeft) * e;
            Top = startTop + (endTop - startTop) * e;
            if (t < 1) return;
            timer.Stop();
            done();
        };
        timer.Start();
    }

    /// <summary>Setzt die zugeklappte Insel an (x, y) und merkt sich alles für den nächsten Start.</summary>
    private void PlaceCompact(double x, double y, double cw, double ch, Rect wa, string attached)
    {
        var half = ExpandedWidth * Scale / 2 + 8;
        var cx = x + cw / 2;
        _alignH = attached == "Links" ? HorizontalAlignment.Left
                : attached == "Rechts" ? HorizontalAlignment.Right
                : cx - wa.Left < half ? HorizontalAlignment.Left
                : wa.Right - cx < half ? HorizontalAlignment.Right
                : HorizontalAlignment.Center;
        _alignV = attached == "Oben" ? VerticalAlignment.Top
                : attached == "Unten" ? VerticalAlignment.Bottom
                : y + ch / 2 > wa.Top + wa.Height / 2 ? VerticalAlignment.Bottom : VerticalAlignment.Top;

        _settings.AmRand = attached;
        _settings.Versteckt = false;
        _settings.AusrichtungH = _alignH switch { HorizontalAlignment.Left => "Links", HorizontalAlignment.Right => "Rechts", _ => "Mitte" };
        _settings.AusrichtungV = _alignV == VerticalAlignment.Bottom ? "Unten" : "Oben";
        _settings.PosX = _alignH switch { HorizontalAlignment.Left => x, HorizontalAlignment.Right => x + cw, _ => x + cw / 2 };
        _settings.PosY = _alignV == VerticalAlignment.Top ? y : y + ch;

        UpdateWindowSize(); // volle Grösse + an die neue Stelle setzen
        ApplyCorners();
        _settings.SaveSoon();
    }

    // ================================================================== Eindrücken / Rausholen

    /// <summary>Insel in den Rand drücken: nur noch ein schmaler Streifen bleibt sichtbar.</summary>
    private void Tuck(string edge, double centerX, double centerY, Rect wa)
    {
        var length = HandleLength * Scale;
        _settings.AmRand = edge;
        _settings.Versteckt = true;

        if (edge is "Links" or "Rechts")
        {
            _settings.PosX = edge == "Links" ? wa.Left : wa.Right;
            _settings.PosY = Math.Clamp(centerY, wa.Top + length / 2, wa.Bottom - length / 2);
        }
        else
        {
            _settings.PosX = Math.Clamp(centerX, wa.Left + length / 2, wa.Right - length / 2);
            _settings.PosY = edge == "Oben" ? wa.Top : wa.Bottom;
        }

        ShowTucked(animate: true);
        _settings.SaveSoon();
    }

    private void ShowTucked(bool animate)
    {
        CollapseInstant();
        CompactHost.Visibility = Visibility.Collapsed;
        Root.Background = TuckHitBrush;
        ApplyTuckLook(true);
        UpdateWindowSize();

        var vertical = VerticalEdge;
        Anim.Set(Pill, WidthProperty, vertical ? (animate ? 0 : HandleThickness) : HandleLength);
        Anim.Set(Pill, HeightProperty, vertical ? HandleLength : (animate ? 0 : HandleThickness));
        if (animate) Anim.To(Pill, vertical ? WidthProperty : HeightProperty, HandleThickness, 420, Anim.Spring);
        ApplyCorners();
    }

    /// <summary>Streifen angeklickt → Insel springt wieder raus (bleibt am Rand angedockt).</summary>
    private void Untuck()
    {
        if (!Tucked) return;
        var edge = Edge;
        var scale = Scale;
        var cw = CompactPillWidth() * scale;
        var ch = CompactHeight * scale;
        var px = _settings.PosX ?? 0;
        var py = _settings.PosY ?? 0;
        var wa = WorkAreaAt(px + (edge == "Rechts" ? -2 : 2), py + (edge == "Unten" ? -2 : 2));

        double x, y;
        switch (edge)
        {
            case "Links": x = wa.Left; y = py - ch / 2; break;
            case "Rechts": x = wa.Right - cw; y = py - ch / 2; break;
            case "Oben": x = px - cw / 2; y = wa.Top; break;
            default: x = px - cw / 2; y = wa.Bottom - ch; break;
        }
        x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - cw));
        y = Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - ch));

        var vertical = VerticalEdge;
        _settings.Versteckt = false;
        Root.Background = null;
        ApplyTuckLook(false);
        PlaceCompact(x, y, cw, ch, wa, edge);

        // Aus dem Streifen herauswachsen
        Anim.Set(Pill, WidthProperty, vertical ? HandleThickness : HandleLength);
        Anim.Set(Pill, HeightProperty, vertical ? HandleLength : HandleThickness);
        Anim.To(Pill, WidthProperty, CompactPillWidth(), 480, Anim.Spring);
        Anim.To(Pill, HeightProperty, CompactHeight, 480, Anim.Spring);
        CompactHost.Visibility = Visibility.Visible;
        Anim.To(CompactHost, OpacityProperty, 1, 260, Anim.Smooth, delayMs: 120, from: 0);
        _ignoreHoverUntilLeave = true;
        RefreshCompact();
        if (RestMode == "Offen") Expand();
    }

    // ================================================================== Position

    private double Scale => Math.Clamp(_settings.Groesse, 0.7, 1.5);

    private double DeviceScale()
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    /// <summary>Arbeitsbereich (ohne Taskleiste) des Bildschirms an diesem Punkt, in WPF-Einheiten.</summary>
    private Rect WorkAreaAt(double x, double y)
    {
        var device = DeviceScale();
        if (Native.GetWorkArea(x * device, y * device, nearest: true) is Rect px)
            return new Rect(px.X / device, px.Y / device, px.Width / device, px.Height / device);
        return SystemParameters.WorkArea;
    }

    /// <summary>Am angedockten Rand kein Abstand (bündig), sonst Platz für den Schatten.</summary>
    private Thickness PillMargins() => new(
        Edge == "Links" ? 0 : PillMargin,
        Edge == "Oben" ? 0 : PillMargin,
        Edge == "Rechts" ? 0 : PillMargin,
        Edge == "Unten" ? 0 : PillMargin);

    /// <summary>Setzt das Fenster so, dass die Insel am gespeicherten Punkt sitzt.</summary>
    private void ApplyPosition()
    {
        var device = DeviceScale();
        Pill.Margin = PillMargins();
        var m = Pill.Margin;

        if (Tucked)
        {
            var px = _settings.PosX ?? 0;
            var py = _settings.PosY ?? 0;
            var onScreen = Native.GetWorkArea((px + (Edge == "Rechts" ? -2 : 2)) * device, (py + (Edge == "Unten" ? -2 : 2)) * device, nearest: false) != null;
            if (onScreen)
            {
                switch (Edge)
                {
                    case "Links": _alignH = HorizontalAlignment.Left; _alignV = VerticalAlignment.Center; Left = px; Top = py - Height / 2; break;
                    case "Rechts": _alignH = HorizontalAlignment.Right; _alignV = VerticalAlignment.Center; Left = px - Width; Top = py - Height / 2; break;
                    case "Oben": _alignH = HorizontalAlignment.Center; _alignV = VerticalAlignment.Top; Left = px - Width / 2; Top = py; break;
                    default: _alignH = HorizontalAlignment.Center; _alignV = VerticalAlignment.Bottom; Left = px - Width / 2; Top = py - Height; break;
                }
                ApplyAlignment();
                return;
            }
            // Bildschirm gibt's nicht mehr → normal oben in der Mitte
            _settings.Versteckt = false;
            _settings.AmRand = "";
            _settings.PosX = _settings.PosY = null;
            Root.Background = null;
            CompactHost.Visibility = Visibility.Visible;
            Anim.Set(Pill, WidthProperty, CompactPillWidth());
            Anim.Set(Pill, HeightProperty, CompactHeight);
            UpdateWindowSize();
            return;
        }

        _alignH = _settings.AusrichtungH switch { "Links" => HorizontalAlignment.Left, "Rechts" => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        _alignV = _settings.AusrichtungV == "Unten" ? VerticalAlignment.Bottom : VerticalAlignment.Top;

        double ax, ay;
        var saved = _settings.PosX is double sx && _settings.PosY is double sy
                    && Native.GetWorkArea(
                        (sx + (_alignH == HorizontalAlignment.Left ? 5 : _alignH == HorizontalAlignment.Right ? -5 : 0)) * device,
                        (sy + (_alignV == VerticalAlignment.Top ? 5 : -5)) * device, nearest: false) != null;

        if (saved)
        {
            ax = _settings.PosX!.Value;
            ay = _settings.PosY!.Value;
        }
        else
        {
            // Standard: oben in der Mitte vom Hauptbildschirm
            _alignH = HorizontalAlignment.Center;
            _alignV = VerticalAlignment.Top;
            if (Edge.Length > 0) { _settings.AmRand = ""; Pill.Margin = m = PillMargins(); ApplyCorners(); }
            ax = SystemParameters.PrimaryScreenWidth / 2;
            ay = Math.Max(0, _settings.AbstandOben);
        }

        ApplyAlignment();
        Left = _alignH switch
        {
            HorizontalAlignment.Left => ax - m.Left,
            HorizontalAlignment.Right => ax + m.Right - Width,
            _ => ax - Width / 2,
        };
        Top = _alignV == VerticalAlignment.Top ? ay - m.Top : ay + m.Bottom - Height;
    }

    /// <summary>Insel und Inhalt wachsen in die gleiche Richtung (zur Bildschirmmitte hin).</summary>
    private void ApplyAlignment()
    {
        Pill.HorizontalAlignment = _alignH;
        Pill.VerticalAlignment = _alignV;
        var contentV = _alignV == VerticalAlignment.Bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        ExpandedRoot.HorizontalAlignment = _alignH;
        ExpandedRoot.VerticalAlignment = contentV;
        ExpandedRoot.RenderTransformOrigin = new Point(0.5, contentV == VerticalAlignment.Top ? 0 : 1);
        CompactHost.HorizontalAlignment = _alignH;
    }

    /// <summary>
    /// Ecken: zugeklappt / eingedrückt ganz rund, aufgeklappt wie eingestellt.
    /// Am angedockten Rand sind die Ecken gerade – die Insel "klebt" am Rand.
    /// </summary>
    private void ApplyCorners()
    {
        if (PillBg == null) return;
        // Wichtig: nie mehr als die halbe Höhe, sonst macht WPF eine spitze Ellipse statt einer Pille
        var r = Tucked ? HandlePeek / 2
              : _expanded ? Math.Clamp(_settings.Rundung, 8, 40)
              : _medium ? Math.Min(RestHeight() / 2, Math.Clamp(_settings.Rundung + 4, 14, 30))
              : CompactRadius;
        double tl = r, tr = r, br = r, bl = r;
        switch (Edge)
        {
            case "Links": tl = bl = 0; break;
            case "Rechts": tr = br = 0; break;
            case "Oben": tl = tr = 0; break;
            case "Unten": bl = br = 0; break;
        }
        var cr = new CornerRadius(tl, tr, br, bl);
        PillBg.CornerRadius = cr;
        PillSheen.CornerRadius = cr;
        PillRim.CornerRadius = cr;
        Shadow1.CornerRadius = Grow(cr, 1.5);
        Shadow2.CornerRadius = Grow(cr, 3);
    }

    private static CornerRadius Grow(CornerRadius c, double d) => new(
        c.TopLeft > 0 ? c.TopLeft + d : 0, c.TopRight > 0 ? c.TopRight + d : 0,
        c.BottomRight > 0 ? c.BottomRight + d : 0, c.BottomLeft > 0 ? c.BottomLeft + d : 0);

    public void ResetPosition()
    {
        _settings.PosX = null;
        _settings.PosY = null;
        _settings.AusrichtungH = "Mitte";
        _settings.AusrichtungV = "Oben";
        _settings.AmRand = "";
        _settings.Versteckt = false;
        Root.Background = null;
        CollapseInstant();
        UpdateWindowSize();
        ApplyCorners();
        _settings.SaveSoon();
        RefreshCompact();
        if (RestMode == "Offen") Expand();
    }

    private void UpdateWindowSize()
    {
        var m = PillMargins();
        if (Tucked)
        {
            // Eingedrückt: winziges Fenster → kostet praktisch nichts
            var vertical = VerticalEdge;
            Width = Math.Ceiling((vertical ? HandlePeek : HandleLength) * Scale + m.Left + m.Right);
            Height = Math.Ceiling((vertical ? HandleLength : HandlePeek) * Scale + m.Top + m.Bottom);
        }
        else
        {
            var maxHeight = Math.Max(ActionsHeight, MaxAlertHeight) + ExpandedPadding;
            foreach (var tab in _tabs) maxHeight = Math.Max(maxHeight, ExpandedPillHeight(tab));

            // + 7 % Platz fürs Nachfedern der Animation
            Width = Math.Ceiling(ExpandedWidth * 1.07 * Scale + m.Left + m.Right);
            Height = Math.Ceiling(maxHeight * 1.07 * Scale + m.Top + m.Bottom);
        }
        if (IsLoaded) ApplyPosition();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(ApplyPosition);

    // ================================================================== Einstellungen

    private void ApplySettings()
    {
        Theme.Apply(_settings, PillBg, PillSheen, PillRim, PillShadow);
        if (Tucked) ApplyTuckLook(true);
        try { FontFamily = new FontFamily($"{_settings.Schriftart}, Segoe UI"); }
        catch (Exception ex) { App.Log(ex); }

        PillScale.ScaleX = PillScale.ScaleY = Scale;
        ExpandedRoot.Width = ExpandedWidth - 32;
        Anim.Tempo = Math.Clamp(_settings.AnimationsTempo, 0.4, 2.0);
        _expandTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.OeffnenVerzoegerungMs, 1, 2000));
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.SchliessenVerzoegerungMs, 50, 5000));
        ApplyCorners();

        UpdateWindowSize();
    }

    private void OnSettingsChanged()
    {
        if (Tucked && !_settings.AmRandVerstecken) Untuck();
        ApplySettings();
        foreach (var m in _modules) SafeCall(m, x => x.OnSettingsChanged());
        RefreshCompact();
        UpdateFullscreenTimer();
        Ram.Apply();
        if (RestMode == "Offen" && !_expanded && !Tucked && _settingsWindow == null) Expand();
    }

    private void OnCoverChanged()
    {
        if (!_settings.AkzentVomCover && _settings.Stil != Theme.Cover) return;
        Theme.Apply(_settings, PillBg, PillSheen, PillRim, PillShadow);
        if (Tucked) ApplyTuckLook(true);
    }

    public void OpenSettings() => OpenSettings(null);

    public void OpenSettings(string? page)
    {
        Collapse(force: true);
        if (_settingsWindow != null)
        {
            if (page != null) _settingsWindow.GoTo(page);
            _settingsWindow.Activate();
            return;
        }
        Ram.Suspend(true); // das Einstellungsfenster braucht mehr RAM → Grenze solange aufheben
        _settingsWindow = new SettingsWindow(_settings, this);
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow = null;
            Ram.Suspend(false);
        };
        if (page != null) _settingsWindow.GoTo(page);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    // ================================================================== Dateien auf die Insel ziehen

    private void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var added = 0;
        foreach (var path in files)
        {
            if (_settings.Schnellstart.Any(x => string.Equals(x.Ziel, path, StringComparison.OrdinalIgnoreCase))) continue;
            var name = System.IO.Directory.Exists(path) ? System.IO.Path.GetFileName(path.TrimEnd('\\')) : System.IO.Path.GetFileNameWithoutExtension(path);
            _settings.Schnellstart.Add(new SchnellstartEintrag { Name = string.IsNullOrWhiteSpace(name) ? path : name, Ziel = path });
            added++;
        }
        e.Handled = true;
        if (added == 0) return;
        _settings.NotifyChanged();

        var text = new TextBlock
        {
            Text = added == 1 ? "\u2713  Zu deinen Apps hinzugefügt" : $"\u2713  {added} Apps hinzugefügt",
            FontSize = 14, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        ShowAlert(text, 44, 3);
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var settings = new MenuItem { Header = "Einstellungen …" };
        settings.Click += (_, _) => OpenSettings();

        var autostart = new MenuItem { Header = "Mit Windows starten", IsCheckable = true };
        autostart.Click += (_, _) =>
        {
            try { Autostart.Set(autostart.IsChecked); }
            catch (Exception ex) { App.Log(ex); }
            autostart.IsChecked = Autostart.IsEnabled;
        };
        menu.Opened += (_, _) => autostart.IsChecked = Autostart.IsEnabled;

        var reset = new MenuItem { Header = "Oben in die Mitte setzen" };
        reset.Click += (_, _) => ResetPosition();

        var restart = new MenuItem { Header = "Neu starten" };
        restart.Click += (_, _) => App.Restart();

        var quit = new MenuItem { Header = "Beenden" };
        quit.Click += (_, _) => Application.Current.Shutdown();

        menu.Items.Add(settings);
        menu.Items.Add(autostart);
        menu.Items.Add(reset);
        menu.Items.Add(new Separator());
        menu.Items.Add(restart);
        menu.Items.Add(quit);
        return menu;
    }

    // ================================================================== Vollbild

    private void UpdateFullscreenTimer()
    {
        if (_settings.BeiVollbildAusblenden)
        {
            if (!_fullscreenTimer.IsEnabled) _fullscreenTimer.Start();
            return;
        }
        _fullscreenTimer.Stop();
        if (_hiddenForFullscreen)
        {
            _hiddenForFullscreen = false;
            Show();
        }
    }

    private void CheckFullscreen()
    {
        if (_isDragging) return;
        bool fullscreen;
        try { fullscreen = Native.IsForegroundFullscreen(_hwnd); }
        catch { return; }

        if (fullscreen && !_hiddenForFullscreen)
        {
            _hiddenForFullscreen = true;
            Collapse(force: true);
            Hide();
        }
        else if (!fullscreen && _hiddenForFullscreen)
        {
            _hiddenForFullscreen = false;
            Show();
            if (RestMode == "Offen") Expand();
        }
    }

    // ================================================================== Helfer

    /// <summary>Inhalt bekommt sofort seine Endgrösse – die Insel wächst dann darum herum.</summary>
    private void SetContentHeight(double pillHeight) => ExpandedRoot.Height = Math.Max(10, pillHeight - ExpandedPadding);

    private double CompactPillWidth() => _compactModule?.CompactWidth ?? 120;

    private double ExpandedPillHeight(IIslandModule module)
        => module.ExpandedHeight + ExpandedPadding + (_tabs.Count > 1 ? TabBarHeight : 0);

    private void OnClosed(object? sender, EventArgs e)
    {
        _expandTimer.Stop();
        _collapseTimer.Stop();
        _fullscreenTimer.Stop();
        _cacheTimer.Stop();
        _alertTimer.Stop();
        Theme.CoverChanged -= OnCoverChanged;
        _settings.Changed -= OnSettingsChanged;
        _settings.Save();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _settingsWindow?.Close();
        Ram.Dispose();
        foreach (var m in _modules) SafeCall(m, x => x.Dispose());
    }

    private static void SafeCall(IIslandModule? module, Action<IIslandModule> action)
    {
        if (module == null) return;
        try { action(module); }
        catch (Exception ex) { App.Log(ex); }
    }
}

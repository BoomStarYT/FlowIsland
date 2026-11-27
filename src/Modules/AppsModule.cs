using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.Core;
using Microsoft.Win32;

namespace Island.Modules;

/// <summary>
/// Schnellstart-Kacheln für Apps, Ordner und Links – mit eigenen Ordnern wie am Handy.
/// Bearbeiten: Einstellungen → Apps, oder Dateien einfach auf die Insel ziehen.
/// </summary>
public sealed class AppsModule : IslandModule
{
    public override string Name => "Apps";
    public override string Icon => "";
    public override int Order => 10;
    public override double ExpandedHeight => 140;
    public override FrameworkElement? ExpandedView => _root;

    private Grid _root = null!;
    private ScrollViewer _scroll = null!;
    private Grid _header = null!;
    private TextBlock _headerTitle = null!;
    private TranslateTransform _slide = null!;
    private string _signature = "";
    private string? _openFolder;

    protected override void OnInitialize()
    {
        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Kopfzeile, wenn ein Ordner offen ist
        _header = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, -4, 0, 2) };
        var back = new Button { Content = "", Style = Ui.Style("IconButton"), Width = 26, Height = 26, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Zurück" };
        back.Click += (_, _) => OpenFolder(null);
        _headerTitle = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _header.Children.Add(back);
        _header.Children.Add(_headerTitle);

        _slide = new TranslateTransform();
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, // Mausrad geht trotzdem
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            RenderTransform = _slide,
        };
        Grid.SetRow(_scroll, 1);
        _root.Children.Add(_header);
        _root.Children.Add(_scroll);
        Rebuild();
    }

    /// <summary>Nur neu bauen, wenn sich die Liste wirklich geändert hat.</summary>
    public override void OnSettingsChanged()
    {
        if (Signature() != _signature) Rebuild();
    }

    public override void OnHidden()
    {
        // Beim nächsten Öffnen wieder auf der Startseite
        if (_openFolder == null) return;
        _openFolder = null;
        Rebuild();
    }

    private string Signature() => string.Join("\n", Context.Settings.Schnellstart.Select(e => $"{e.Name}|{e.Ziel}|{e.Icon}|{e.Argumente}|{e.Ordner}"))
                                  + "#" + string.Join("|", Context.Settings.AppOrdner);

    private void OpenFolder(string? folder)
    {
        var forward = folder != null;
        _openFolder = folder;
        Rebuild();
        Anim.To(_slide, TranslateTransform.XProperty, 0, 420, Anim.Spring, from: forward ? 26 : -26);
        Anim.To(_scroll, UIElement.OpacityProperty, 1, 220, Anim.Smooth, from: 0);
    }

    private void Rebuild()
    {
        _signature = Signature();
        var s = Context.Settings;
        var folders = s.AppOrdner.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (_openFolder != null && !folders.Contains(_openFolder, StringComparer.OrdinalIgnoreCase)) _openFolder = null;

        _header.Visibility = _openFolder != null ? Visibility.Visible : Visibility.Collapsed;
        _headerTitle.Text = _openFolder ?? "";

        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        bool InFolder(SchnellstartEintrag e, string? f) =>
            f == null ? string.IsNullOrWhiteSpace(e.Ordner) || !folders.Contains(e.Ordner!, StringComparer.OrdinalIgnoreCase)
                      : string.Equals(e.Ordner, f, StringComparison.OrdinalIgnoreCase);

        if (_openFolder == null)
        {
            foreach (var folder in folders)
            {
                var apps = s.Schnellstart.Where(e => InFolder(e, folder) && !string.IsNullOrWhiteSpace(e.Ziel)).ToList();
                wrap.Children.Add(CreateFolderTile(folder, apps));
            }
        }

        foreach (var entry in s.Schnellstart)
        {
            if (string.IsNullOrWhiteSpace(entry.Ziel) || !InFolder(entry, _openFolder)) continue;
            wrap.Children.Add(CreateTile(entry));
        }

        if (_openFolder == null) wrap.Children.Add(CreateAddTile());
        _scroll.Content = wrap;
    }

    private Button CreateTile(SchnellstartEintrag entry)
    {
        var button = Tile(AppIcon(entry), entry.Name);
        button.ToolTip = entry.Ziel;
        button.Click += (_, _) => IslandContext.Open(entry.Ziel, entry.Argumente);
        return button;
    }

    /// <summary>Ordner wie am Handy: kleine Vorschau der ersten vier Apps.</summary>
    private Button CreateFolderTile(string folder, List<SchnellstartEintrag> apps)
    {
        var grid = new UniformGrid { Rows = 2, Columns = 2, Width = 26, Height = 26 };
        foreach (var app in apps.Take(4))
        {
            var mini = AppIcon(app, 11.5);
            mini.Margin = new Thickness(0.5);
            grid.Children.Add(mini);
        }
        var box = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Padding = new Thickness(5), Child = grid,
        }.Themed(Border.BackgroundProperty, "IslandChip");

        var button = Tile(box, folder);
        button.ToolTip = $"{folder} ({apps.Count})";
        button.Click += (_, _) => OpenFolder(folder);
        return button;
    }

    private Button CreateAddTile()
    {
        var plus = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.2),
            Child = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                .Themed(TextBlock.ForegroundProperty, "IslandSubText"),
        }.Themed(Border.BorderBrushProperty, "IslandTrack");
        var button = Tile(plus, "Hinzufügen");
        button.ToolTip = "Apps & Ordner bearbeiten – oder Dateien einfach auf die Insel ziehen";
        button.Click += (_, _) => Context.OpenSettings("Apps");
        return button;
    }

    private static Button Tile(FrameworkElement icon, string name)
    {
        var content = new StackPanel();
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(icon);
        content.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 10.5,
            Margin = new Thickness(0, 5, 0, 0),
            MaxWidth = 68,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        return new Button { Content = content, Style = Ui.Style("TileButton") };
    }

    /// <summary>Symbol der App als Glas-Kachel. Erst ein Buchstabe, sobald das echte Symbol da ist → Symbol.</summary>
    private static FrameworkElement AppIcon(SchnellstartEintrag entry, double size = 36)
    {
        var tile = new GlassIcon(size, entry.Name.Length > 0 ? entry.Name[..1].ToUpperInvariant() : "?");
        var icon = AppIcons.Get(entry.Ziel, entry.Icon, entry.Name, src => tile.Source = src);
        if (icon != null) tile.Source = icon;
        return tile;
    }
}

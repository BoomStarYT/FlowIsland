using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Island.Core;
using Microsoft.Win32;

namespace Island.Modules;

/// <summary>Schnellstart-Kacheln für Apps, Ordner und Links. Bearbeiten: Einstellungen → Apps.</summary>
public sealed class AppsModule : IslandModule
{
    public override string Name => "Apps";
    public override string Icon => "";
    public override int Order => 10;
    public override double ExpandedHeight => 140;
    public override FrameworkElement? ExpandedView => _root;

    private ScrollViewer _root = null!;
    private string _signature = "";

    protected override void OnInitialize()
    {
        _root = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, // Mausrad geht trotzdem
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        Rebuild();
    }

    /// <summary>Nur neu bauen, wenn sich die Liste wirklich geändert hat.</summary>
    public override void OnSettingsChanged()
    {
        if (Signature() != _signature) Rebuild();
    }

    private string Signature() => string.Join("\n", Context.Settings.Schnellstart.Select(e => $"{e.Name}|{e.Ziel}|{e.Icon}|{e.Argumente}"));

    private void Rebuild()
    {
        _signature = Signature();
        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };

        foreach (var entry in Context.Settings.Schnellstart)
        {
            if (string.IsNullOrWhiteSpace(entry.Ziel)) continue;
            wrap.Children.Add(CreateTile(entry));
        }

        if (wrap.Children.Count == 0)
        {
            wrap.Children.Add(new TextBlock
            {
                Text = "Noch keine Apps.\nDoppelklick auf die Insel → Einstellungen → Apps",
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 34, 0, 0),
            }.Themed(TextBlock.ForegroundProperty, "IslandSubText"));
        }

        _root.Content = wrap;
    }

    private static Button CreateTile(SchnellstartEintrag entry)
    {
        FrameworkElement icon;
        var bitmap = TryGetIcon(entry);
        if (bitmap != null)
        {
            icon = new Image { Source = bitmap, Width = 30, Height = 30 };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        }
        else
        {
            icon = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(9),
                Child = new TextBlock
                {
                    Text = entry.Name.Length > 0 ? entry.Name[..1].ToUpperInvariant() : "?",
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            }.Themed(Border.BackgroundProperty, "IslandChip");
        }

        var content = new StackPanel();
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(icon);
        content.Children.Add(new TextBlock
        {
            Text = entry.Name,
            FontSize = 10.5,
            Margin = new Thickness(0, 5, 0, 0),
            MaxWidth = 68,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var button = new Button { Content = content, Style = Ui.Style("TileButton"), ToolTip = entry.Ziel };
        button.Click += (_, _) => IslandContext.Open(entry.Ziel, entry.Argumente);
        return button;
    }

    /// <summary>Sucht das passende Symbol (eigene .exe, Ordner, System-Programm oder installierte App).</summary>
    private static BitmapSource? TryGetIcon(SchnellstartEintrag entry)
    {
        try
        {
            var candidate = Environment.ExpandEnvironmentVariables(
                string.IsNullOrWhiteSpace(entry.Icon) ? entry.Ziel : entry.Icon!).Trim('"');

            if (File.Exists(candidate) || Directory.Exists(candidate))
                return Native.GetIcon(candidate);

            if (!candidate.Contains('\\') && candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var dir in new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) })
                {
                    var path = Path.Combine(dir, candidate);
                    if (File.Exists(path)) return Native.GetIcon(path);
                }

                // Installierte Programme wie chrome.exe stehen unter "App Paths"
                const string appPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\";
                using var key = Registry.CurrentUser.OpenSubKey(appPaths + candidate)
                             ?? Registry.LocalMachine.OpenSubKey(appPaths + candidate);
                if (key?.GetValue(null) is string registered)
                {
                    registered = Environment.ExpandEnvironmentVariables(registered.Trim('"'));
                    if (File.Exists(registered)) return Native.GetIcon(registered);
                }
            }
        }
        catch (Exception ex) { App.Log(ex); }

        return null;
    }
}

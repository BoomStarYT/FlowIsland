using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Island.Core;

namespace Island.Modules;

/// <summary>Spickzettel für Tastenkürzel. Bearbeiten: Einstellungen → Tastenkürzel.</summary>
public sealed class KeysModule : IslandModule
{
    public override string Name => "Tasten";
    public override string Icon => "";
    public override int Order => 20;
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

    public override void OnSettingsChanged()
    {
        if (Signature() != _signature) Rebuild();
    }

    private string Signature() => string.Join("\n", Context.Settings.Tastenkuerzel.Select(e => $"{e.Tasten}|{e.Beschreibung}"));

    private void Rebuild()
    {
        _signature = Signature();
        var list = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };

        foreach (var entry in Context.Settings.Tastenkuerzel)
        {
            if (string.IsNullOrWhiteSpace(entry.Tasten)) continue;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var part in entry.Tasten.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                keys.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(7, 2, 7, 3),
                    Margin = new Thickness(0, 0, 4, 0),
                    Child = new TextBlock { Text = part, FontSize = 11, FontWeight = FontWeights.SemiBold },
                }.Themed(Border.BackgroundProperty, "IslandChip"));
            }

            var description = new TextBlock
            {
                Text = entry.Beschreibung,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            }.Themed(TextBlock.ForegroundProperty, "IslandSubText");
            Grid.SetColumn(description, 1);

            row.Children.Add(keys);
            row.Children.Add(description);
            list.Children.Add(row);
        }

        _root.Content = list;
    }
}

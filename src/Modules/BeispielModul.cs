using System.Windows;
using System.Windows.Controls;
using Island.Core;

namespace Island.Modules;

/// <summary>
/// VORLAGE für eigene Module.
/// 1. Diese Datei kopieren und umbenennen (z. B. "MeinModul.cs"), Klassennamen ändern.
/// 2. "Enabled" auf true setzen.
/// 3. Neu bauen → neuer Tab erscheint automatisch (und kann in den Einstellungen ein-/ausgeschaltet werden).
/// Farben: .Themed(TextBlock.ForegroundProperty, "IslandSubText") usw. → passen sich dem Design an.
/// Symbole findest du, wenn du nach "Segoe Fluent Icons" suchst (z. B. E7E8 = Ein/Aus).
/// </summary>
public sealed class BeispielModul : IslandModule
{
    public override bool Enabled => false;          // ← auf true setzen, um es zu sehen
    public override string Name => "Beispiel";
    public override string Icon => "";        // Stern
    public override int Order => 30;                // Position der Tabs (Musik = 0, Apps = 10, Tasten = 20)
    public override double ExpandedHeight => 100;
    public override FrameworkElement? ExpandedView => _view;

    private StackPanel _view = null!;
    private int _clicks;

    protected override void OnInitialize()
    {
        var text = new TextBlock
        {
            Text = "Hallo! Das ist ein eigenes Modul.",
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var button = new Button
        {
            Content = "",                       // Plus
            Style = Ui.Style("IconButton"),
            Margin = new Thickness(0, 10, 0, 0),
        };
        button.Click += (_, _) => text.Text = $"Du hast {++_clicks}× geklickt";

        _view = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _view.Children.Add(text);
        _view.Children.Add(button);
    }

    // Optional:
    // public override void OnShown()  { /* Tab ist offen  → z. B. Timer starten */ }
    // public override void OnHidden() { /* Tab ist zu    → Timer stoppen (Akku sparen) */ }
    // public override void OnSettingsChanged() { /* Einstellungen wurden geändert */ }
}

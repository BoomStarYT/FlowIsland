using System;
using System.Windows;

namespace Island.Core;

/// <summary>
/// Ein Modul ist ein Baustein der Insel (z. B. Musik, Apps, Tasten, Uhr).
/// Neue Module: einfach eine neue Klasse anlegen, die von <see cref="IslandModule"/> erbt.
/// Die Insel findet sie beim Start automatisch – sonst muss nichts angepasst werden.
/// </summary>
public interface IIslandModule : IDisposable
{
    /// <summary>Name (Tooltip im Tab, Name in den Einstellungen unter "Funktionen").</summary>
    string Name { get; }

    /// <summary>Symbol für den Tab (Zeichen aus "Segoe Fluent Icons", z. B. "").</summary>
    string Icon { get; }

    /// <summary>Reihenfolge: kleinere Zahl = weiter links / wichtiger.</summary>
    int Order { get; }

    /// <summary>false = Modul wird nicht geladen.</summary>
    bool Enabled { get; }

    void Initialize(IslandContext context);

    /// <summary>Ansicht für die zugeklappte Insel (null = keine).</summary>
    FrameworkElement? CompactView { get; }

    /// <summary>Breite der zugeklappten Insel, wenn dieses Modul angezeigt wird.</summary>
    double CompactWidth { get; }

    /// <summary>true = dieses Modul möchte gerade in der zugeklappten Insel angezeigt werden.
    /// Das erste Modul (nach Order), das true sagt, gewinnt.</summary>
    bool WantsCompact { get; }

    /// <summary>Ansicht für "halb offen" (zwischen zu und ganz offen). null = keine.</summary>
    FrameworkElement? MediumView { get; }
    double MediumWidth { get; }
    double MediumHeight { get; }

    /// <summary>Ansicht für die aufgeklappte Insel (null = kein Tab).</summary>
    FrameworkElement? ExpandedView { get; }

    /// <summary>Höhe des Inhalts, wenn der Tab offen ist.</summary>
    double ExpandedHeight { get; }

    /// <summary>Tab wird sichtbar (gut zum Starten von Timern).</summary>
    void OnShown();

    /// <summary>Tab wird unsichtbar (Timer stoppen = Akku sparen).</summary>
    void OnHidden();

    /// <summary>Einstellungen wurden geändert (z. B. im Einstellungsfenster).</summary>
    void OnSettingsChanged();
}

/// <summary>Basisklasse mit sinnvollen Standardwerten. Nur überschreiben, was man braucht.
/// Farben: mit <c>element.Themed(Eigenschaft, "IslandText")</c> passen sie sich dem Design an.</summary>
public abstract class IslandModule : IIslandModule
{
    protected IslandContext Context { get; private set; } = null!;

    public abstract string Name { get; }
    public virtual string Icon => "";
    public virtual int Order => 50;
    public virtual bool Enabled => true;

    public virtual FrameworkElement? CompactView => null;
    public virtual double CompactWidth => 180;
    public virtual bool WantsCompact => false;

    public virtual FrameworkElement? MediumView => null;
    public virtual double MediumWidth => 360;
    public virtual double MediumHeight => 64;

    public virtual FrameworkElement? ExpandedView => null;
    public virtual double ExpandedHeight => 150;

    public void Initialize(IslandContext context)
    {
        Context = context;
        OnInitialize();
    }

    /// <summary>Hier Ansichten bauen und Daten laden.</summary>
    protected virtual void OnInitialize() { }

    public virtual void OnShown() { }
    public virtual void OnHidden() { }
    public virtual void OnSettingsChanged() { }
    public virtual void Dispose() { }
}

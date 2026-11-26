using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Island.Core;

/// <summary>Was die Insel für Module anbietet.</summary>
public interface IIslandHost
{
    void RefreshCompact();
    /// <summary>Höhe eines Tabs hat sich geändert (z. B. Lyrics an/aus).</summary>
    void RefreshLayout();
    /// <summary>Insel klappt auf und zeigt eine Meldung (z. B. Aufgaben-Erinnerung).</summary>
    void ShowAlert(FrameworkElement view, double height, int seconds);
    void HideAlert();
    void OpenSettings(string? page = null);
}

/// <summary>Was ein Modul von der Insel bekommt.</summary>
public sealed class IslandContext
{
    private readonly IIslandHost _host;

    public IslandContext(Settings settings, Dispatcher dispatcher, IIslandHost host)
    {
        Settings = settings;
        Dispatcher = dispatcher;
        _host = host;
    }

    public Settings Settings { get; }

    /// <summary>UI-Thread. Alles, was die Oberfläche ändert, muss hier laufen.</summary>
    public Dispatcher Dispatcher { get; }

    /// <summary>Sagt der Insel: "Prüf nochmal, wer zugeklappt angezeigt werden soll."</summary>
    public void RequestCompactRefresh() => Dispatcher.InvokeAsync(_host.RefreshCompact);

    /// <summary>Tab-Höhe hat sich geändert.</summary>
    public void RequestLayoutRefresh() => Dispatcher.InvokeAsync(_host.RefreshLayout);

    /// <summary>Meldung in der Insel zeigen (klappt auf). seconds = 0 → bleibt, bis HideAlert.</summary>
    public void ShowAlert(FrameworkElement view, double height, int seconds = 12) => Dispatcher.InvokeAsync(() => _host.ShowAlert(view, height, seconds));

    public void HideAlert() => Dispatcher.InvokeAsync(_host.HideAlert);

    /// <summary>Einstellungen öffnen, optional direkt auf einer Seite (z. B. "Apps").</summary>
    public void OpenSettings(string? page = null) => Dispatcher.InvokeAsync(() => _host.OpenSettings(page));

    /// <summary>Öffnet eine App, einen Ordner, eine Datei oder einen Link (z. B. "spotify:").</summary>
    public static void Open(string target, string? arguments = null)
    {
        try
        {
            var psi = new ProcessStartInfo(Environment.ExpandEnvironmentVariables(target)) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(arguments)) psi.Arguments = arguments;
            Process.Start(psi);
        }
        catch (Exception ex) { App.Log(ex); }
    }
}

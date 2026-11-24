using System;
using System.Diagnostics;
using System.Windows.Threading;

namespace Island.Core;

/// <summary>Was ein Modul von der Insel bekommt.</summary>
public sealed class IslandContext
{
    private readonly Action _refreshCompact;

    public IslandContext(Settings settings, Dispatcher dispatcher, Action refreshCompact)
    {
        Settings = settings;
        Dispatcher = dispatcher;
        _refreshCompact = refreshCompact;
    }

    public Settings Settings { get; }

    /// <summary>UI-Thread. Alles, was die Oberfläche ändert, muss hier laufen.</summary>
    public Dispatcher Dispatcher { get; }

    /// <summary>Sagt der Insel: "Prüf nochmal, wer zugeklappt angezeigt werden soll."</summary>
    public void RequestCompactRefresh() => Dispatcher.InvokeAsync(_refreshCompact);

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

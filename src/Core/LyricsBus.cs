using System;

namespace Island.Core;

/// <summary>Verteilt die aktuelle Lyrics-Zeile (z. B. an das Lyrics-Fenster neben der Insel).</summary>
public static class LyricsBus
{
    public static bool Active { get; private set; }
    public static bool Playing { get; private set; }
    public static string Now { get; private set; } = "";
    public static string Next { get; private set; } = "";

    public static event Action? Changed;

    public static void Set(bool active, bool playing, string now, string next)
    {
        if (active == Active && playing == Playing && now == Now && next == Next) return;
        Active = active;
        Playing = playing;
        Now = now;
        Next = next;
        try { Changed?.Invoke(); }
        catch (Exception ex) { App.Log(ex); }
    }
}

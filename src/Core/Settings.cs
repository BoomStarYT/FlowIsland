using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace Island.Core;

/// <summary>Alles aus "einstellungen.json" (liegt neben der .exe). Wird auch vom Einstellungsfenster geändert.</summary>
public sealed class Settings
{
    // ------------------------------------------------------------------ Aussehen

    /// <summary>"Einfarbig", "Verlauf", "Liquid Glass", "Klarglas" oder "Cover" (dunkel + Farben vom Song-Cover).</summary>
    public string Stil { get; set; } = "Einfarbig";
    public string Farbe1 { get; set; } = "#0A0A0C";
    public string Farbe2 { get; set; } = "#3A1C71";
    public double VerlaufWinkel { get; set; } = 135;
    /// <summary>0.3 – 1.0</summary>
    public double Deckkraft { get; set; } = 1.0;
    /// <summary>Farbe von Equalizer und Fortschrittsbalken.</summary>
    public string Akzent { get; set; } = "#30D158";
    public string Schriftart { get; set; } = "Segoe UI Variable Text";
    /// <summary>0.8 – 1.3</summary>
    public double Groesse { get; set; } = 1.0;
    /// <summary>Ecken-Rundung im aufgeklappten Zustand.</summary>
    public double Rundung { get; set; } = 26;
    public bool Schatten { get; set; } = true;

    /// <summary>Nur für "Klarglas": 0 = wolkig/milchig, 1 = ganz klar.</summary>
    public double GlasKlarheit { get; set; } = 0.6;

    /// <summary>Akzentfarbe automatisch aus dem Cover des laufenden Songs nehmen.</summary>
    public bool AkzentVomCover { get; set; }

    /// <summary>Aussehen des Fortschrittsbalkens, z. B. "Welle", "Linie", "Katze", "DNA" (siehe ProgressStyles).</summary>
    public string Fortschritt { get; set; } = "Welle";

    /// <summary>Eigene Farbe für den Fortschrittsbalken ("" = Akzentfarbe).</summary>
    public string FortschrittFarbe { get; set; } = "";

    /// <summary>Glas-Stile: echte Unschärfe hinter der Insel (Windows-Blur).</summary>
    public bool EchterBlur { get; set; } = true;

    /// <summary>Glas-Stile: Cover wird farblos und durchsichtig, damit alles einheitlich wirkt.</summary>
    public bool CoverGlas { get; set; } = true;

    /// <summary>Selbst gespeicherte Designs (lokal, nur in dieser Datei).</summary>
    public List<DesignEintrag> EigeneDesigns { get; set; } = new();

    // ------------------------------------------------------------------ Verhalten

    public bool OeffnenPerKlick { get; set; } = false;
    public int OeffnenVerzoegerungMs { get; set; } = 120;
    public int SchliessenVerzoegerungMs { get; set; } = 400;
    /// <summary>1.0 = normal, kleiner = schneller.</summary>
    public double AnimationsTempo { get; set; } = 1.0;
    public bool EqualizerAnimiert { get; set; } = true;
    public bool BeiVollbildAusblenden { get; set; } = true;
    /// <summary>Abstand vom oberen Rand, wenn die Insel oben in der Mitte ist.</summary>
    public double AbstandOben { get; set; } = 8;

    /// <summary>Wie die Insel aussieht, wenn man nichts macht: "Zu", "Halb" oder "Offen".</summary>
    public string Ruhezustand { get; set; } = "Zu";

    /// <summary>In die Nähe von oben-Mitte gezogen → sanft dort ankleben.</summary>
    public bool MagnetOben { get; set; } = true;

    /// <summary>Oben in der Mitte wie eine MacBook-Notch: bündig am Rand, gerade Ecken oben.</summary>
    public bool NotchLook { get; set; } = true;

    // ------------------------------------------------------------------ Position (wird beim Verschieben gespeichert)

    public double? PosX { get; set; }
    public double? PosY { get; set; }
    /// <summary>"Links", "Mitte", "Rechts" – in welche Richtung die Insel aufgeht.</summary>
    public string AusrichtungH { get; set; } = "Mitte";
    /// <summary>"Oben" oder "Unten".</summary>
    public string AusrichtungV { get; set; } = "Oben";

    /// <summary>An welchem Bildschirmrand die Insel bündig andockt: "", "Links", "Rechts", "Oben", "Unten".</summary>
    public string AmRand { get; set; } = "";

    /// <summary>true = Insel ist in den Rand "eingedrückt" (nur ein schmaler Streifen ist sichtbar).</summary>
    public bool Versteckt { get; set; }

    /// <summary>Darf man die Insel in den Rand eindrücken?</summary>
    public bool AmRandVerstecken { get; set; } = true;

    // ------------------------------------------------------------------ Arbeitsspeicher

    /// <summary>RAM-Begrenzer an/aus.</summary>
    public bool RamBegrenzen { get; set; }

    /// <summary>Gewählte Grenze in MB (0 = Maximum).</summary>
    public int RamLimitMb { get; set; }

    /// <summary>Gemessen: so viel braucht die Insel mindestens (0 = noch nicht gemessen).</summary>
    public int RamMinMb { get; set; }

    /// <summary>Gemessen: mehr als das bringt nichts.</summary>
    public int RamMaxMb { get; set; }

    // ------------------------------------------------------------------ Ruheanzeige

    /// <summary>Was zugeklappt angezeigt wird, wenn keine Musik läuft: "Datum", "Uhr", "Akku", "Leistung", "Minimal".</summary>
    public string RuheWidget { get; set; } = "Datum";

    /// <summary>Nach so vielen Sekunden Pause zur Ruheanzeige wechseln (0 = nie).</summary>
    public int PauseZuRuheSekunden { get; set; } = 60;

    // ------------------------------------------------------------------ Verbindungen / Funktionen

    /// <summary>true = nur Spotify anzeigen. false = auch YouTube, Browser usw.</summary>
    public bool NurSpotify { get; set; } = true;

    /// <summary>Songtexte anzeigen (holt sie von lrclib.net – nur Titel/Künstler werden gesendet).</summary>
    public bool Lyrics { get; set; }

    /// <summary>Aktuelle Lyrics-Zeile auch in der zugeklappten Insel.</summary>
    public bool LyricsKompakt { get; set; }

    /// <summary>Lyrics in einem eigenen kleinen Feld neben der Insel (auch wenn sie zu ist).</summary>
    public bool LyricsSeite { get; set; }

    /// <summary>Wetter: Ort (z. B. "Basel"). Leer = noch nicht eingerichtet.</summary>
    public string WetterOrt { get; set; } = "";
    public double? WetterLat { get; set; }
    public double? WetterLon { get; set; }
    /// <summary>Gefundener Name, z. B. "Basel, Schweiz".</summary>
    public string WetterOrtName { get; set; } = "";

    /// <summary>Eigene Aufgaben mit optionaler Erinnerung.</summary>
    public List<AufgabeEintrag> Aufgaben { get; set; } = new();
    public bool AufgabenTon { get; set; } = true;

    /// <summary>Knöpfe im Kontrollzentrum (Reihenfolge = Anzeige), siehe ControlModule.Actions.</summary>
    public List<string> KontrollKnoepfe { get; set; } = new() { "WLAN", "Bluetooth", "Flugmodus", "Stumm", "Kamera", "Screenshot", "Dunkelmodus", "Sperren" };

    /// <summary>Lautstärke- und Helligkeitsregler im Kontrollzentrum zeigen.</summary>
    public bool KontrollRegler { get; set; } = true;

    /// <summary>Ordner für Apps (Reihenfolge = Anzeige).</summary>
    public List<string> AppOrdner { get; set; } = new();

    /// <summary>Namen von Modulen, die nicht geladen werden sollen, z. B. ["Tasten"].</summary>
    public List<string> DeaktivierteModule { get; set; } = new();

    public List<SchnellstartEintrag> Schnellstart { get; set; } = new()
    {
        new() { Name = "Spotify", Ziel = "spotify:", Icon = @"%APPDATA%\Spotify\Spotify.exe" },
        new() { Name = "Explorer", Ziel = "explorer.exe" },
        new() { Name = "Downloads", Ziel = @"%USERPROFILE%\Downloads" },
        new() { Name = "Rechner", Ziel = "calc.exe" },
        new() { Name = "Task-Manager", Ziel = "taskmgr.exe" },
        new() { Name = "Einstellungen", Ziel = "ms-settings:" },
    };

    public List<TastenEintrag> Tastenkuerzel { get; set; } = new()
    {
        new() { Tasten = "Win + V", Beschreibung = "Zwischenablage-Verlauf" },
        new() { Tasten = "Win + Shift + S", Beschreibung = "Screenshot (Ausschnitt)" },
        new() { Tasten = "Win + .", Beschreibung = "Emojis" },
        new() { Tasten = "Win + D", Beschreibung = "Desktop zeigen" },
        new() { Tasten = "Win + L", Beschreibung = "PC sperren" },
        new() { Tasten = "Win + E", Beschreibung = "Explorer öffnen" },
        new() { Tasten = "Win + Pfeil", Beschreibung = "Fenster andocken" },
        new() { Tasten = "Alt + Tab", Beschreibung = "Zwischen Fenstern wechseln" },
        new() { Tasten = "Strg + Shift + Esc", Beschreibung = "Task-Manager" },
        new() { Tasten = "Strg + Shift + T", Beschreibung = "Geschlossenen Tab wieder öffnen" },
    };

    // ------------------------------------------------------------------ Änderungen melden + speichern

    /// <summary>Wird ausgelöst, wenn sich etwas geändert hat (Insel übernimmt es sofort).</summary>
    public event Action? Changed;

    private DispatcherTimer? _saveTimer;
    private DispatcherTimer? _changeTimer;

    /// <summary>Sofort übernehmen (z. B. Farbe, Schieberegler) und kurz danach speichern.</summary>
    public void NotifyChanged()
    {
        _changeTimer?.Stop();
        try { Changed?.Invoke(); }
        catch (Exception ex) { App.Log(ex); }
        SaveSoon();
    }

    /// <summary>Etwas später übernehmen (z. B. beim Tippen in Textfeldern).</summary>
    public void NotifyChangedSoon()
    {
        _changeTimer ??= CreateTimer(350, NotifyChanged);
        _changeTimer.Stop();
        _changeTimer.Start();
    }

    /// <summary>Speichert in 0.5 s (mehrere Änderungen werden zusammengefasst).</summary>
    public void SaveSoon()
    {
        _saveTimer ??= CreateTimer(500, Save);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Save()
    {
        _saveTimer?.Stop();
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions)); }
        catch (Exception ex) { App.Log(ex); }
    }

    private static DispatcherTimer CreateTimer(int ms, Action action)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); action(); };
        return timer;
    }

    // ------------------------------------------------------------------ Laden

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "einstellungen.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // Umlaute lesbar speichern
    };

    public static Settings Load()
    {
        if (File.Exists(FilePath))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions) ?? new Settings();
                loaded.Save(); // neue Einstellungen (nach Updates) gleich in die Datei schreiben
                return loaded;
            }
            catch (Exception ex)
            {
                App.Log(ex);
                MessageBox.Show(
                    "In \"einstellungen.json\" ist ein Fehler (z. B. ein Komma oder Anführungszeichen fehlt).\n\n" +
                    "Die Insel startet jetzt mit den Standard-Einstellungen.\n\nDetails: " + ex.Message,
                    "Dynamic Island", MessageBoxButton.OK, MessageBoxImage.Warning);
                return new Settings();
            }
        }

        var defaults = new Settings();
        defaults.Save();
        return defaults;
    }
}

public sealed class SchnellstartEintrag
{
    public string Name { get; set; } = "";

    /// <summary>Was geöffnet wird: Programm, Ordner, Datei, Webseite oder Link wie "spotify:".</summary>
    public string Ziel { get; set; } = "";

    /// <summary>Optional: Startparameter.</summary>
    public string? Argumente { get; set; }

    /// <summary>Optional: Pfad zu einer .exe/.ico, von der das Symbol genommen wird.</summary>
    public string? Icon { get; set; }

    /// <summary>Name des Ordners, in dem die App liegt (leer = oberste Ebene).</summary>
    public string? Ordner { get; set; }
}

public sealed class AufgabeEintrag
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    /// <summary>Bis wann (lokale Zeit). null = ohne Erinnerung.</summary>
    public DateTime? Faellig { get; set; }
    public bool Erledigt { get; set; }
    /// <summary>Erinnerung wurde schon gezeigt.</summary>
    public bool Erinnert { get; set; }
}

public sealed class TastenEintrag
{
    public string Tasten { get; set; } = "";
    public string Beschreibung { get; set; } = "";
}

/// <summary>Ein gespeichertes Design (Farben, Stil, Glas, Rundung, Schrift).</summary>
public sealed class DesignEintrag
{
    public string Name { get; set; } = "";
    public string Stil { get; set; } = "Einfarbig";
    public string Farbe1 { get; set; } = "#0A0A0C";
    public string Farbe2 { get; set; } = "#0A0A0C";
    public double VerlaufWinkel { get; set; } = 135;
    public double Deckkraft { get; set; } = 1.0;
    public string Akzent { get; set; } = "#30D158";
    public double GlasKlarheit { get; set; } = 0.6;

    /// <summary>null = beim Anwenden nicht ändern (Vorlagen lassen deine Rundung/Schrift in Ruhe).</summary>
    public double? Rundung { get; set; }
    public bool? Schatten { get; set; }
    public string? Schriftart { get; set; }

    public static DesignEintrag From(Settings s, string name) => new()
    {
        Name = name,
        Stil = s.Stil,
        Farbe1 = s.Farbe1,
        Farbe2 = s.Farbe2,
        VerlaufWinkel = s.VerlaufWinkel,
        Deckkraft = s.Deckkraft,
        Akzent = s.Akzent,
        GlasKlarheit = s.GlasKlarheit,
        Rundung = s.Rundung,
        Schatten = s.Schatten,
        Schriftart = s.Schriftart,
    };

    public void ApplyTo(Settings s)
    {
        s.Stil = Stil;
        s.Farbe1 = Farbe1;
        s.Farbe2 = Farbe2;
        s.VerlaufWinkel = VerlaufWinkel;
        s.Deckkraft = Deckkraft;
        s.Akzent = Akzent;
        s.GlasKlarheit = GlasKlarheit;
        if (Rundung is double r) s.Rundung = r;
        if (Schatten is bool sh) s.Schatten = sh;
        if (!string.IsNullOrWhiteSpace(Schriftart)) s.Schriftart = Schriftart!;
    }
}

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Island.Core;

/// <summary>
/// RAM-Begrenzer.
///
/// Misst selbst:
///  • Minimum = was die Insel in Ruhe wirklich braucht (nach dem "Aufräumen" des Arbeitsspeichers
///    wächst sie nur auf die Seiten zurück, die sie tatsächlich benutzt).
///  • Maximum = höchster Verbrauch im normalen Gebrauch (+ 15 % Reserve). Mehr bringt nichts.
///
/// Begrenzen: Windows bekommt eine harte Obergrenze für den Arbeitsspeicher der Insel.
/// Was darüber liegt, lagert Windows aus. Die App läuft weiter, kann aber bei sehr tiefer
/// Grenze etwas träger reagieren. Während das Einstellungsfenster offen ist, gilt keine Grenze.
/// </summary>
public sealed class RamLimiter
{
    private const uint QUOTA_LIMITS_HARDWS_MIN_DISABLE = 0x2;
    private const uint QUOTA_LIMITS_HARDWS_MAX_ENABLE = 0x4;
    private const uint QUOTA_LIMITS_HARDWS_MAX_DISABLE = 0x8;
    private const long MB = 1024 * 1024;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSizeEx(IntPtr process, IntPtr min, IntPtr max, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessWorkingSetSizeEx(IntPtr process, out IntPtr min, out IntPtr max, out uint flags);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private readonly Settings _settings;
    private readonly DispatcherTimer _sampleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _measureTimer = new();
    private readonly IntPtr _defaultMin, _defaultMax;
    private bool _suspended;
    private bool _measuring;
    private bool _limitActive;

    public RamLimiter(Settings settings)
    {
        _settings = settings;
        GetProcessWorkingSetSizeEx(GetCurrentProcess(), out _defaultMin, out _defaultMax, out _);

        // Alle 5 s den aktuellen Verbrauch ansehen → höchster Wert ergibt das Maximum
        _sampleTimer.Tick += (_, _) => Sample();
        _sampleTimer.Start();

        // 20 s nach dem Start einmal das Minimum messen (falls noch nie gemessen)
        _measureTimer.Interval = TimeSpan.FromSeconds(20);
        _measureTimer.Tick += (_, _) =>
        {
            _measureTimer.Stop();
            if (_settings.RamMinMb <= 0 && !_suspended) StartMeasure(); // sonst nach dem Schliessen der Einstellungen
        };
        _measureTimer.Start();

        Apply();
    }

    /// <summary>Wird ausgelöst, wenn sich die Messwerte geändert haben.</summary>
    public event Action? Measured;

    public bool IsMeasuring => _measuring;

    /// <summary>Aktueller Arbeitsspeicher der Insel in MB (wie im Task-Manager).</summary>
    public static int CurrentMb()
    {
        using var p = Process.GetCurrentProcess();
        return (int)(p.WorkingSet64 / MB);
    }

    public int MinMb => _settings.RamMinMb > 0 ? _settings.RamMinMb : 40;
    public int MaxMb => Math.Max(MinMb + 10, _settings.RamMaxMb > 0 ? _settings.RamMaxMb : 160);

    /// <summary>Minimum messen: Speicher aufräumen, 10 s warten, schauen wie viel zurückkommt.</summary>
    public void StartMeasure()
    {
        if (_measuring) return;
        _measuring = true;
        RemoveLimit();
        Trim();

        var wait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            var needed = CurrentMb();
            _settings.RamMinMb = (int)Math.Ceiling(needed * 1.1);          // + 10 % Luft
            if (_settings.RamMaxMb < _settings.RamMinMb + 10) _settings.RamMaxMb = _settings.RamMinMb + 40;
            _measuring = false;
            _settings.SaveSoon();
            Apply();
            Measured?.Invoke();
        };
        wait.Start();
    }

    /// <summary>Einstellungsfenster offen → Grenze kurz aufheben (das Fenster braucht mehr).</summary>
    public void Suspend(bool suspend)
    {
        _suspended = suspend;
        Apply();
        if (!suspend && _settings.RamMinMb <= 0)
        {
            // Messung (neu) anfordern: kurz warten, bis das Einstellungsfenster wirklich weg ist
            _measureTimer.Interval = TimeSpan.FromSeconds(3);
            _measureTimer.Start();
        }
    }

    /// <summary>Messwerte löschen → wird nach dem Schliessen der Einstellungen neu gemessen.</summary>
    public void ResetMeasurement()
    {
        _settings.RamMinMb = 0;
        _settings.RamMaxMb = 0;
        _settings.SaveSoon();
    }

    /// <summary>Einstellungen übernehmen (Schalter / Schieberegler).</summary>
    public void Apply()
    {
        if (_measuring) return;
        var limit = Math.Clamp(_settings.RamLimitMb > 0 ? _settings.RamLimitMb : MaxMb, MinMb, MaxMb);

        if (!_settings.RamBegrenzen || _suspended)
        {
            RemoveLimit();
            return;
        }

        try
        {
            var ok = SetProcessWorkingSetSizeEx(GetCurrentProcess(), (IntPtr)(4 * MB), (IntPtr)(limit * MB),
                QUOTA_LIMITS_HARDWS_MIN_DISABLE | QUOTA_LIMITS_HARDWS_MAX_ENABLE);
            _limitActive = ok;
            if (!ok) App.Log(new InvalidOperationException($"RAM-Grenze konnte nicht gesetzt werden (Fehler {Marshal.GetLastWin32Error()})."));
        }
        catch (Exception ex) { App.Log(ex); }
    }

    private void RemoveLimit()
    {
        if (!_limitActive) return;
        try
        {
            SetProcessWorkingSetSizeEx(GetCurrentProcess(), _defaultMin, _defaultMax,
                QUOTA_LIMITS_HARDWS_MIN_DISABLE | QUOTA_LIMITS_HARDWS_MAX_DISABLE);
        }
        catch (Exception ex) { App.Log(ex); }
        _limitActive = false;
    }

    /// <summary>Nicht benutzte Speicherseiten an Windows zurückgeben.</summary>
    private static void Trim()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSizeEx(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1), 0);
        }
        catch { /* egal */ }
    }

    private void Sample()
    {
        // Nur im normalen Betrieb messen (nicht mit offenem Einstellungsfenster / während der Messung / mit Grenze)
        if (_suspended || _measuring || _limitActive) return;
        var now = CurrentMb();
        var candidate = (int)Math.Ceiling(now * 1.15);                     // + 15 % Reserve
        if (candidate > _settings.RamMaxMb)
        {
            _settings.RamMaxMb = candidate;
            _settings.SaveSoon();
            Measured?.Invoke();
        }
    }

    public void Dispose()
    {
        _sampleTimer.Stop();
        _measureTimer.Stop();
        RemoveLimit();
    }
}

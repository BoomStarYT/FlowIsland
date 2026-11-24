using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Island.Core;

namespace Island;

public partial class App : Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Nur eine Insel gleichzeitig
        _mutex = new Mutex(true, @"Local\DynamicIsland_EinzigeInstanz", out bool isFirst);
        if (!isFirst)
        {
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, a) => { Log(a.Exception); a.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => { if (a.ExceptionObject is Exception ex) Log(ex); };
        TaskScheduler.UnobservedTaskException += (_, a) => { Log(a.Exception); a.SetObserved(); };

        base.OnStartup(e);

        var settings = Settings.Load();
        new MainWindow(settings).Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { /* egal */ }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Startet die App neu (z. B. nach Änderungen an den Einstellungen).</summary>
    public static void Restart()
    {
        try { _mutex?.ReleaseMutex(); } catch { /* egal */ }
        _mutex?.Dispose();
        _mutex = null;

        try
        {
            var exe = Environment.ProcessPath;
            if (exe != null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex) { Log(ex); }

        Current.Shutdown();
    }

    /// <summary>Schreibt Fehler in "fehler.log" neben der .exe.</summary>
    public static void Log(Exception ex)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "fehler.log");
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { /* Logging darf nie abstürzen */ }
    }
}

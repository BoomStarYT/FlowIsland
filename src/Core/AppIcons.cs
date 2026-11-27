using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WApps = global::Windows.ApplicationModel.Core;
using WDeploy = global::Windows.Management.Deployment;

namespace Island.Core;

/// <summary>
/// Symbole für Apps – für normale Programme (.exe, .lnk, Ordner) UND Microsoft-Store-Apps
/// (z. B. Spotify, WhatsApp aus dem Store). Wird zwischengespeichert, damit nichts doppelt geladen wird.
/// </summary>
public static class AppIcons
{
    public sealed record StoreApp(string Name, string Aumid, WApps.AppListEntry Entry)
    {
        public string Target => "shell:AppsFolder\\" + Aumid;
    }

    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<Action<ImageSource>>> Waiting = new(StringComparer.OrdinalIgnoreCase);
    private static Task<List<StoreApp>>? _store;

    /// <summary>Alle installierten Store-Apps (einmal im Hintergrund geladen).</summary>
    public static Task<List<StoreApp>> StoreAppsAsync() => _store ??= Task.Run(() =>
    {
        var list = new List<StoreApp>();
        try
        {
            var pm = new WDeploy.PackageManager();
            foreach (var pkg in pm.FindPackagesForUser(string.Empty))
            {
                try
                {
                    if (pkg.IsFramework || pkg.IsResourcePackage) continue;
                    foreach (var entry in pkg.GetAppListEntries())
                    {
                        var name = entry.DisplayInfo?.DisplayName;
                        if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) continue;
                        list.Add(new StoreApp(name, entry.AppUserModelId, entry));
                    }
                }
                catch { /* einzelnes Paket kaputt → überspringen */ }
            }
        }
        catch (Exception ex) { App.Log(ex); }
        return list.GroupBy(a => a.Aumid).Select(g => g.First()).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    });

    /// <summary>
    /// Symbol holen. Gibt es schon → sofort zurück. Sonst wird es im Hintergrund geladen und
    /// <paramref name="loaded"/> aufgerufen, sobald es da ist (auf dem UI-Thread).
    /// </summary>
    public static ImageSource? Get(string target, string? iconHint, string name, Action<ImageSource>? loaded = null)
    {
        var key = (iconHint ?? "") + "|" + target + "|" + name;
        if (Cache.TryGetValue(key, out var cached)) return cached;

        // 1) Normale Dateien, Ordner, Programme
        var file = FileIcon(string.IsNullOrWhiteSpace(iconHint) ? target : iconHint!) ?? (iconHint != null ? FileIcon(target) : null);
        if (file == null && target.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            file = Native.GetShellImage(target, 96);
        if (file != null)
        {
            var prepared = Prepare(file);
            Cache[key] = prepared;
            return prepared;
        }

        // 2) Store-App (shell:AppsFolder\...) oder per Name finden (z. B. "spotify:" → Spotify aus dem Store)
        if (loaded != null)
        {
            if (Waiting.TryGetValue(key, out var list)) { list.Add(loaded); return null; }
            Waiting[key] = new List<Action<ImageSource>> { loaded };
            _ = LoadStoreIconAsync(key, target, name);
        }
        return null;
    }

    private static async Task LoadStoreIconAsync(string key, string target, string name)
    {
        ImageSource? icon = null;
        try
        {
            var apps = await StoreAppsAsync();
            StoreApp? app = null;
            if (target.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
            {
                var aumid = target["shell:AppsFolder\\".Length..];
                app = apps.FirstOrDefault(a => a.Aumid.Equals(aumid, StringComparison.OrdinalIgnoreCase));
            }
            app ??= apps.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (app == null && target.EndsWith(":") && target.Length > 2)
            {
                var scheme = target.TrimEnd(':');
                app = apps.FirstOrDefault(a => a.Name.Contains(scheme, StringComparison.OrdinalIgnoreCase)
                                            || a.Aumid.Contains(scheme, StringComparison.OrdinalIgnoreCase));
            }
            if (app != null)
            {
                // Erst das Shell-Symbol (durchsichtig, wie im Startmenü), sonst das Logo der App
                var shell = Native.GetShellImage(app.Target, 96);
                icon = shell != null ? Prepare(shell) : await LogoAsync(app.Entry) is BitmapSource logo ? Prepare(logo) : null;
            }
        }
        catch (Exception ex) { App.Log(ex); }

        Cache[key] = icon;
        if (Waiting.Remove(key, out var callbacks) && icon != null)
            foreach (var cb in callbacks)
            {
                try { cb(icon); } catch (Exception ex) { App.Log(ex); }
            }
    }

    private static async Task<ImageSource?> LogoAsync(WApps.AppListEntry entry)
    {
        try
        {
            var reference = entry.DisplayInfo.GetLogo(new global::Windows.Foundation.Size(64, 64));
            using var winStream = await reference.OpenReadAsync();
            using var stream = winStream.AsStreamForRead();
            var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            memory.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 64;
            bmp.StreamSource = memory;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    // ================================================================== Aufbereiten

    /// <summary>Symbole, die schon selbst eine volle Fläche haben (z. B. Spotify-Grün) → füllen die ganze Kachel.</summary>
    private static readonly HashSet<ImageSource> FullBleed = new();

    public static bool IsFullBleed(ImageSource? src) => src != null && FullBleed.Contains(src);

    /// <summary>
    /// Durchsichtigen Rand wegschneiden, damit jedes Symbol gleich gross wirkt (manche haben viel leeren Rand).
    /// Merkt sich ausserdem, ob das Symbol eine volle, deckende Fläche ist.
    /// </summary>
    private static ImageSource Prepare(BitmapSource source)
    {
        try
        {
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            bgra.CopyPixels(px, stride, 0);

            int minX = w, minY = h, maxX = -1, maxY = -1, solid = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var a = px[y * stride + x * 4 + 3];
                    if (a <= 24) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    if (a > 200) solid++;
                }
            if (maxX < minX || maxY < minY) return source;

            // Quadratisch zuschneiden (Mitte bleibt Mitte)
            int bw = maxX - minX + 1, bh = maxY - minY + 1, side = Math.Max(bw, bh);
            int cx = minX + bw / 2, cy = minY + bh / 2;
            int left = Math.Clamp(cx - side / 2, 0, Math.Max(0, w - side)), top = Math.Clamp(cy - side / 2, 0, Math.Max(0, h - side));
            side = Math.Min(side, Math.Min(w - left, h - top));

            BitmapSource result = side < Math.Min(w, h) - 2
                ? new CroppedBitmap(source, new System.Windows.Int32Rect(left, top, side, side))
                : source;
            result.Freeze();
            if (solid > bw * bh * 0.82) FullBleed.Add(result);
            return result;
        }
        catch { return source; }
    }

    /// <summary>Symbol einer Datei / eines Ordners / einer .exe (auch System-Programme und "App Paths").</summary>
    public static BitmapSource? FileIcon(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var candidate = Environment.ExpandEnvironmentVariables(raw).Trim('"');
            if (candidate.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return null;
            if (File.Exists(candidate) || Directory.Exists(candidate)) return Native.GetShellImage(candidate, 96) ?? Native.GetIcon(candidate);

            if (!candidate.Contains('\\') && candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var dir in new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) })
                {
                    var path = Path.Combine(dir, candidate);
                    if (File.Exists(path)) return Native.GetShellImage(path, 96) ?? Native.GetIcon(path);
                }

                const string appPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\";
                using var key = Registry.CurrentUser.OpenSubKey(appPaths + candidate)
                             ?? Registry.LocalMachine.OpenSubKey(appPaths + candidate);
                if (key?.GetValue(null) is string registered)
                {
                    registered = Environment.ExpandEnvironmentVariables(registered.Trim('"'));
                    if (File.Exists(registered)) return Native.GetShellImage(registered, 96) ?? Native.GetIcon(registered);
                }
            }
        }
        catch (Exception ex) { App.Log(ex); }
        return null;
    }
}

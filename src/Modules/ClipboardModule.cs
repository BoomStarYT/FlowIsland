using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Island.Core;

namespace Island.Modules;

/// <summary>Ein gemerkter Eintrag der Zwischenablage.</summary>
public sealed class ClipEintrag
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Zeit { get; set; } = DateTime.Now;
    /// <summary>"Text", "Bild" oder "Dateien".</summary>
    public string Art { get; set; } = "Text";
    public string? Text { get; set; }
    /// <summary>Dateiname des Bildes im Ordner "zwischenablage".</summary>
    public string? Bild { get; set; }
    public List<string>? Dateien { get; set; }
    public long Bytes { get; set; }
}

/// <summary>
/// Verlauf der Zwischenablage – alles bleibt auf diesem PC (Ordner "zwischenablage" neben der .exe).
/// Speichergrenze, Aufbewahrungszeit und Löschen stellst du in den Einstellungen ein.
/// Inhalte aus Passwort-Managern (die das Windows-Signal "nicht merken" setzen) werden nie gespeichert.
/// </summary>
public sealed class ClipboardModule : IslandModule
{
    public override string Name => "Zwischenablage";
    public override string Icon => "";
    public override int Order => 18;
    public override double ExpandedHeight => 140;
    public override FrameworkElement? ExpandedView => _root;

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "zwischenablage");
    private static string IndexFile => Path.Combine(Folder, "verlauf.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>Für "Jetzt alles löschen" in den Einstellungen.</summary>
    public static event Action? ClearRequested;
    public static void RequestClear() => ClearRequested?.Invoke();

    private readonly List<ClipEintrag> _items = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer _prune = new() { Interval = TimeSpan.FromMinutes(10) };
    private HwndSource? _listener;
    private Grid _root = null!;
    private StackPanel _list = null!;
    private TextBlock _count = null!;
    private string? _ignoreText;
    private bool _shown;

    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    protected override void OnInitialize()
    {
        BuildView();
        Load();
        Prune();

        // Unsichtbares Nachrichtenfenster: Windows meldet jede Änderung der Zwischenablage
        try
        {
            _listener = new HwndSource(new HwndSourceParameters("FlowIslandZwischenablage") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
            _listener.AddHook(WndProc);
            AddClipboardFormatListener(_listener.Handle);
        }
        catch (Exception ex) { App.Log(ex); }

        _debounce.Tick += (_, _) => { _debounce.Stop(); Capture(); };
        _prune.Tick += (_, _) => Prune();
        _prune.Start();
        ClearRequested += ClearAll;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x031D) // WM_CLIPBOARDUPDATE
        {
            _debounce.Stop();
            _debounce.Start();
        }
        return IntPtr.Zero;
    }

    public override void OnSettingsChanged()
    {
        Prune();
        if (!Context.Settings.ZwSpeichern) DeleteFiles(keepMemory: true);
        else Save();
    }

    public override void OnShown()
    {
        _shown = true;
        Rebuild(); // "vor 3 Min" aktualisieren
    }

    public override void OnHidden() => _shown = false;

    // ================================================================== Aufnehmen

    private void Capture()
    {
        var s = Context.Settings;
        try
        {
            // Passwort-Manager & Co. sagen Windows: "bitte nicht merken" → respektieren
            if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")
                || Clipboard.ContainsData("Clipboard Viewer Ignore")) return;

            ClipEintrag? item = null;
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text)) return;
                if (text == _ignoreText) { _ignoreText = null; MoveToTop(i => i.Text == text); return; }
                if (text.Length > 100_000) text = text[..100_000]; // riesige Texte kürzen
                _items.RemoveAll(i => i.Art == "Text" && i.Text == text); // doppelt → nach oben
                item = new ClipEintrag { Art = "Text", Text = text, Bytes = Encoding.UTF8.GetByteCount(text) };
            }
            else if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                if (files.Count == 0) return;
                var key = string.Join("|", files);
                _items.RemoveAll(i => i.Art == "Dateien" && i.Dateien != null && string.Join("|", i.Dateien) == key);
                item = new ClipEintrag { Art = "Dateien", Dateien = files, Bytes = Encoding.UTF8.GetByteCount(key) };
            }
            else if (Clipboard.ContainsImage() && s.ZwBilder)
            {
                var image = Clipboard.GetImage();
                if (image == null) return;
                item = new ClipEintrag { Art = "Bild" };
                var png = EncodePng(image);
                item.Bytes = png.Length;
                if (item.Bytes > s.ZwMaxMb * 1024L * 1024L) return; // allein zu gross
                item.Bild = item.Id + ".png";
                _imageCache[item.Id] = png;
                if (s.ZwSpeichern) WriteImage(item, png);
            }
            if (item == null) return;

            _items.Insert(0, item);
            Prune();
            Save();
            Rebuild();
        }
        catch (Exception ex) when (ex is COMException or ExternalException or InvalidOperationException)
        {
            // Zwischenablage gerade von einer anderen App gesperrt → einfach überspringen
        }
        catch (Exception ex) { App.Log(ex); }
    }

    private void MoveToTop(Func<ClipEintrag, bool> match)
    {
        var existing = _items.FirstOrDefault(match);
        if (existing == null) return;
        _items.Remove(existing);
        existing.Zeit = DateTime.Now;
        _items.Insert(0, existing);
        Save();
        Rebuild();
    }

    private readonly Dictionary<string, byte[]> _imageCache = new();

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    // ================================================================== Grenzen: Zeit, Anzahl, Speicher

    private void Prune()
    {
        var s = Context.Settings;
        var removed = new List<ClipEintrag>();
        if (s.ZwAufbewahrenStunden > 0)
        {
            var limit = DateTime.Now.AddHours(-s.ZwAufbewahrenStunden);
            removed.AddRange(_items.Where(i => i.Zeit < limit));
        }
        foreach (var r in removed) _items.Remove(r);

        var maxItems = Math.Clamp(s.ZwMaxEintraege, 5, 500);
        while (_items.Count > maxItems) { removed.Add(_items[^1]); _items.RemoveAt(_items.Count - 1); }

        var maxBytes = Math.Clamp(s.ZwMaxMb, 1, 500) * 1024L * 1024L;
        while (_items.Count > 0 && _items.Sum(i => i.Bytes) > maxBytes) { removed.Add(_items[^1]); _items.RemoveAt(_items.Count - 1); }

        foreach (var r in removed) DeleteImage(r);
        if (removed.Count > 0) { Save(); Rebuild(); }
    }

    // ================================================================== Speichern (lokal)

    private void Load()
    {
        try
        {
            if (!Context.Settings.ZwSpeichern || !File.Exists(IndexFile)) return;
            var items = JsonSerializer.Deserialize<List<ClipEintrag>>(File.ReadAllText(IndexFile), Json);
            if (items == null) return;
            foreach (var i in items)
            {
                if (i.Art == "Bild" && (i.Bild == null || !File.Exists(Path.Combine(Folder, i.Bild)))) continue;
                _items.Add(i);
            }
        }
        catch (Exception ex) { App.Log(ex); }
    }

    private void Save()
    {
        if (!Context.Settings.ZwSpeichern) return;
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(IndexFile, JsonSerializer.Serialize(_items, Json));
        }
        catch (Exception ex) { App.Log(ex); }
    }

    private static void WriteImage(ClipEintrag item, byte[] png)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder, item.Bild!), png);
        }
        catch (Exception ex) { App.Log(ex); }
    }

    private void DeleteImage(ClipEintrag item)
    {
        _imageCache.Remove(item.Id);
        if (item.Bild == null) return;
        try
        {
            var path = Path.Combine(Folder, item.Bild);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* egal */ }
    }

    /// <summary>Dateien auf der Festplatte löschen (Liste im Arbeitsspeicher optional behalten).</summary>
    private void DeleteFiles(bool keepMemory)
    {
        try { if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true); }
        catch (Exception ex) { App.Log(ex); }
        if (!keepMemory) { _items.Clear(); _imageCache.Clear(); }
    }

    private void ClearAll()
    {
        DeleteFiles(keepMemory: false);
        Rebuild();
    }

    // ================================================================== Ansicht

    private void BuildView()
    {
        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var head = new Grid { Margin = new Thickness(2, -4, 0, 4) };
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "Zwischenablage", FontSize = 14, FontWeight = FontWeights.SemiBold });
        _count = new TextBlock { FontSize = 12, Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center }.Themed(TextBlock.ForegroundProperty, "IslandSubText");
        title.Children.Add(_count);
        var clear = new Button { Content = "", Style = Ui.Style("IconButton"), Width = 28, Height = 28, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Alles löschen" };
        clear.Click += (_, _) => ClearAll();
        head.Children.Add(title);
        head.Children.Add(clear);

        _list = new StackPanel();
        var scroll = new ScrollViewer
        {
            Content = _list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        Grid.SetRow(scroll, 1);
        _root.Children.Add(head);
        _root.Children.Add(scroll);
    }

    private void Rebuild()
    {
        if (_root == null) return;
        _list.Children.Clear();
        _count.Text = _items.Count == 0 ? "" : $"{_items.Count}";

        foreach (var item in _items.Take(60)) _list.Children.Add(Row(item));
        if (_items.Count == 0)
            _list.Children.Add(new TextBlock
            {
                Text = "Noch nichts kopiert.\nAlles, was du kopierst, erscheint hier – nur auf diesem PC.",
                TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0),
            }.Themed(TextBlock.ForegroundProperty, "IslandSubText"));
    }

    private FrameworkElement Row(ClipEintrag item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FrameworkElement lead;
        string text;
        switch (item.Art)
        {
            case "Bild":
                var img = new Image { Width = 44, Height = 30, Stretch = Stretch.UniformToFill, Source = Thumbnail(item) };
                lead = new Border { CornerRadius = new CornerRadius(6), Child = img, ClipToBounds = true, Margin = new Thickness(0, 0, 10, 0), Width = 44, Height = 30 };
                text = "Bild";
                break;
            case "Dateien":
                lead = Glyph("");
                text = item.Dateien is { Count: > 1 } f ? $"{Path.GetFileName(f[0])} + {f.Count - 1} weitere" : Path.GetFileName(item.Dateien?.FirstOrDefault() ?? "");
                break;
            default:
                lead = Glyph("");
                text = (item.Text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                break;
        }

        var label = new TextBlock { Text = text, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var time = new TextBlock { Text = Ago(item.Zeit), FontSize = 10.5, Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, "IslandSubText");
        var delete = new Button { Content = "", Style = Ui.Style("IconButton"), Width = 24, Height = 24, FontSize = 9, Opacity = 0, ToolTip = "Entfernen" };

        Grid.SetColumn(label, 1);
        Grid.SetColumn(time, 2);
        Grid.SetColumn(delete, 3);
        grid.Children.Add(lead);
        grid.Children.Add(label);
        grid.Children.Add(time);
        grid.Children.Add(delete);

        var row = new Border { Child = grid, CornerRadius = new CornerRadius(9), Padding = new Thickness(6, 4, 4, 4), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Klicken = wieder kopieren" };
        row.MouseEnter += (_, _) => { row.SetResourceReference(Border.BackgroundProperty, "IslandHover"); delete.Opacity = 1; };
        row.MouseLeave += (_, _) => { row.Background = Brushes.Transparent; delete.Opacity = 0; };
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (CopyBack(item))
            {
                time.Text = "✓ kopiert";
                time.SetResourceReference(TextBlock.ForegroundProperty, "IslandAccent");
            }
        };
        delete.Click += (_, _) =>
        {
            _items.Remove(item);
            DeleteImage(item);
            Save();
            Anim.To(row, UIElement.OpacityProperty, 0, 160, Anim.Smooth, Rebuild);
        };
        return row;
    }

    private static FrameworkElement Glyph(string glyph) => new Border
    {
        Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 10, 0),
        Child = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    }.Themed(Border.BackgroundProperty, "IslandChip");

    private ImageSource? Thumbnail(ClipEintrag item)
    {
        try
        {
            var bytes = _imageCache.TryGetValue(item.Id, out var b) ? b
                      : item.Bild != null && File.Exists(Path.Combine(Folder, item.Bild)) ? File.ReadAllBytes(Path.Combine(Folder, item.Bild)) : null;
            if (bytes == null) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 88; // klein = wenig RAM
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private bool CopyBack(ClipEintrag item)
    {
        try
        {
            switch (item.Art)
            {
                case "Text":
                    _ignoreText = item.Text;
                    Clipboard.SetText(item.Text ?? "");
                    break;
                case "Dateien":
                    var list = new StringCollection();
                    list.AddRange(item.Dateien?.ToArray() ?? Array.Empty<string>());
                    Clipboard.SetFileDropList(list);
                    break;
                case "Bild":
                    var bytes = _imageCache.TryGetValue(item.Id, out var b) ? b : File.ReadAllBytes(Path.Combine(Folder, item.Bild!));
                    var decoder = new PngBitmapDecoder(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    Clipboard.SetImage(decoder.Frames[0]);
                    break;
            }
            return true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return false;
        }
    }

    private static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return "jetzt";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} Min";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} Std";
        return t.ToString("d. MMM", new CultureInfo("de-CH"));
    }

    public override void Dispose()
    {
        _debounce.Stop();
        _prune.Stop();
        ClearRequested -= ClearAll;
        try
        {
            if (_listener != null)
            {
                RemoveClipboardFormatListener(_listener.Handle);
                _listener.Dispose();
            }
        }
        catch { /* egal */ }
        if (Context.Settings.ZwBeimBeendenLoeschen) DeleteFiles(keepMemory: false);
    }
}

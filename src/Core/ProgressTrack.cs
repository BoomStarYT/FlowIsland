using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Island.Core;

/// <summary>
/// Fortschrittsbalken mit vielen Designs: Google-Welle, Linie, kleine Pixel-Tiere, Programmierer-Sachen,
/// Natur, Wissenschaft … Alles wird selbst gezeichnet (kein Bild, kein Internet).
/// Animiert nur, solange Musik läuft und der Balken sichtbar ist – alle Balken teilen sich einen Takt (30 Bilder/s).
/// </summary>
public sealed class ProgressTrack : FrameworkElement
{
    // ================================================================== Katalog

    public static readonly (string Category, (string Id, string Name)[] Items)[] Catalog =
    {
        ("Klassisch", new[] { ("Welle", "Welle (Google)"), ("Linie", "Linie"), ("Dick", "Dick"), ("Punkte", "Punkte"), ("Verlauf", "Leuchten") }),
        ("Süsse Tiere", new[] { ("Katze", "Katze"), ("Hund", "Hund"), ("Hase", "Hase"), ("Ente", "Ente"), ("Schildkröte", "Schildkröte"), ("Schnecke", "Schnecke") }),
        ("Pixel Art", new[] { ("Pixel", "Pixel"), ("Herz", "Pixel-Herz"), ("Rakete", "Rakete") }),
        ("Programmierer", new[] { ("Terminal", "Terminal"), ("Code", "</> Code"), ("Binär", "Binär") }),
        ("Arbeit", new[] { ("Kaffee", "Kaffee"), ("Streifen", "In Arbeit") }),
        ("Freizeit", new[] { ("Fussball", "Fussball"), ("Gamepad", "Gamepad"), ("Noten", "Musiknoten") }),
        ("Natur", new[] { ("Ranke", "Ranke"), ("Blume", "Blume"), ("Biene", "Biene"), ("Meer", "Meer") }),
        ("Wissenschaft", new[] { ("DNA", "DNA"), ("Herzschlag", "Herzschlag"), ("Atom", "Atom"), ("Planet", "Planet") }),
    };

    public static bool IsKnown(string id) => Catalog.Any(c => c.Items.Any(i => i.Id == id));

    /// <summary>Diese Designs bewegen sich auch ohne Fortschritt (brauchen 30 Bilder/s).</summary>
    private static readonly HashSet<string> Moving = new()
    {
        "Welle", "Katze", "Hund", "Hase", "Ente", "Schildkröte", "Herz", "Rakete", "Terminal", "Code", "Binär", "Kaffee",
        "Streifen", "Gamepad", "Noten", "Blume", "Biene", "Meer", "DNA", "Herzschlag", "Atom", "Planet", "Verlauf",
    };

    // ================================================================== Gemeinsamer Takt

    private static readonly HashSet<ProgressTrack> Active = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static DispatcherTimer? _ticker;
    private static long _frame;

    private static void Register(ProgressTrack t)
    {
        if (!Active.Add(t)) return;
        if (_ticker == null)
        {
            _ticker = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            _ticker.Tick += (_, _) =>
            {
                _frame++;
                foreach (var p in Active.ToArray()) p.OnTick();
            };
        }
        if (!_ticker.IsEnabled) _ticker.Start();
    }

    private static void Unregister(ProgressTrack t)
    {
        Active.Remove(t);
        if (Active.Count == 0) _ticker?.Stop();
    }

    // ================================================================== Eigenschaften

    private string _style = "Welle";
    private bool _playing, _live;
    private double _fraction;
    private double _amp;          // 0 = pausiert (flach), 1 = läuft
    private double _lastTime;

    public ProgressTrack()
    {
        SnapsToDevicePixels = false;
        IsHitTestVisible = false;
        IsVisibleChanged += (_, _) => UpdateActive();
    }

    public string StyleId
    {
        get => _style;
        set { _style = IsKnown(value) ? value : "Welle"; UpdateActive(); InvalidateVisual(); }
    }

    /// <summary>Holt den aktuellen Fortschritt (0–1). Ohne Quelle gilt <see cref="Fraction"/>.</summary>
    public Func<double>? Source { get; set; }

    public double Fraction
    {
        get => _fraction;
        set { _fraction = Math.Clamp(value, 0, 1); InvalidateVisual(); }
    }

    public bool Playing
    {
        get => _playing;
        set { if (_playing == value) return; _playing = value; UpdateActive(); InvalidateVisual(); }
    }

    /// <summary>false = gar nicht animieren (z. B. Tab zu).</summary>
    public bool Live
    {
        get => _live;
        set { if (_live == value) return; _live = value; UpdateActive(); InvalidateVisual(); }
    }

    private bool Settling => Math.Abs((_playing ? 1 : 0) - _amp) > 0.01;

    private void UpdateActive()
    {
        if (_live && IsVisible && (_playing || Settling)) Register(this);
        else Unregister(this);
    }

    private void OnTick()
    {
        // Ruhige Designs brauchen nur ~8 Bilder/s, um dem Fortschritt zu folgen
        if (!Moving.Contains(_style) && !Settling && _frame % 4 != 0) return;
        InvalidateVisual();
        if (!_playing && !Settling) UpdateActive();
    }

    // ================================================================== Zeichnen

    private Brush _accent = Brushes.LimeGreen, _track = Brushes.Gray, _text = Brushes.White, _sub = Brushes.Gray;
    private double _t;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 10 || h < 4) return;

        _t = Clock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(_t - _lastTime, 0, 0.2);
        _lastTime = _t;
        var target = _playing ? 1.0 : 0.0;
        _amp += (target - _amp) * Math.Min(1, dt * 7);
        if (!_live) _amp = target;

        _accent = (TryFindResource("IslandProgress") as Brush) ?? (TryFindResource("IslandAccent") as Brush) ?? Brushes.LimeGreen;
        _track = (TryFindResource("IslandTrack") as Brush) ?? Brushes.Gray;
        _text = (TryFindResource("IslandText") as Brush) ?? Brushes.White;
        _sub = (TryFindResource("IslandSubText") as Brush) ?? Brushes.Gray;

        var f = Source != null ? Math.Clamp(Source(), 0, 1) : _fraction;

        // Unsichtbare Fläche, damit Klicks/Ziehen zum Spulen überall greifen
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        try { Draw(dc, w, h, f); }
        catch (Exception ex) { App.Log(ex); }
    }

    private void Draw(DrawingContext dc, double w, double h, double f)
    {
        var cy = h / 2;
        switch (_style)
        {
            case "Linie": Bar(dc, w, cy, f, 4); break;
            case "Dick": Bar(dc, w, cy, f, 8); break;
            case "Punkte": Dots(dc, w, cy, f); break;
            case "Verlauf": Glow(dc, w, cy, f); break;

            case "Katze": Walker(dc, w, h, f, Sprites.Cat, 1.5, hop: false, speed: 0.16); break;
            case "Hund": Walker(dc, w, h, f, Sprites.Dog, 1.5, hop: false, speed: 0.14); break;
            case "Hase": Walker(dc, w, h, f, Sprites.Bunny, 1.5, hop: true, speed: 0.22); break;
            case "Ente": Walker(dc, w, h, f, Sprites.Duck, 1.5, hop: false, speed: 0.2); break;
            case "Schildkröte": Walker(dc, w, h, f, Sprites.Turtle, 1.4, hop: false, speed: 0.4); break;
            case "Schnecke": Walker(dc, w, h, f, Sprites.Snail, 1.4, hop: false, speed: 0.5); break;

            case "Pixel": PixelBar(dc, w, cy, f); break;
            case "Herz": PixelBar(dc, w, cy, f); HeartThumb(dc, w, cy, f); break;
            case "Rakete": Rocket(dc, w, h, f); break;

            case "Terminal": Terminal(dc, w, cy, f); break;
            case "Code": Code(dc, w, cy, f); break;
            case "Binär": Binary(dc, w, cy, f); break;

            case "Kaffee": Coffee(dc, w, h, f); break;
            case "Streifen": Stripes(dc, w, cy, f); break;

            case "Fussball": Football(dc, w, h, f); break;
            case "Gamepad": Gamepad(dc, w, h, f); break;
            case "Noten": Notes(dc, w, h, f); break;

            case "Ranke": Vine(dc, w, cy, f); break;
            case "Blume": Bar(dc, w, cy, f, 3, pad: 9); Flower(dc, Px(w, f, 9), cy); break;
            case "Biene": Bee(dc, w, cy, f); break;
            case "Meer": Sea(dc, w, cy, f); break;

            case "DNA": Dna(dc, w, cy, f); break;
            case "Herzschlag": Ecg(dc, w, cy, f); break;
            case "Atom": Bar(dc, w, cy, f, 3, pad: 9); Atom(dc, Px(w, f, 9), cy); break;
            case "Planet": Planet(dc, w, cy, f); break;

            default: Wave(dc, w, cy, f); break;
        }
    }

    /// <summary>x-Position des Fortschritts (mit Rand links/rechts).</summary>
    private static double Px(double w, double f, double pad) => pad + (w - 2 * pad) * f;

    private Pen P(Brush b, double thickness, bool round = true) => new(b, thickness)
    {
        StartLineCap = round ? PenLineCap.Round : PenLineCap.Flat,
        EndLineCap = round ? PenLineCap.Round : PenLineCap.Flat,
        LineJoin = PenLineJoin.Round,
    };

    private void Line(DrawingContext dc, Brush b, double thickness, double x0, double x1, double y)
    {
        if (x1 - x0 < 0.5) return;
        dc.DrawLine(P(b, thickness), new Point(x0, y), new Point(x1, y));
    }

    private void Polyline(DrawingContext dc, Pen pen, IList<Point> points)
    {
        if (points.Count < 2) return;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(points[0], false, false);
            c.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }

    private FormattedText Text(string s, double size, Brush b, string font = "Consolas", bool bold = false)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
               new Typeface(new FontFamily(font), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
               size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    // ---------------------------------------------------------------- Klassisch

    private void Bar(DrawingContext dc, double w, double cy, double f, double thickness, double pad = -1)
    {
        if (pad < 0) pad = thickness / 2;
        var x = Px(w, f, pad);
        Line(dc, _track, thickness, pad, w - pad, cy);
        Line(dc, _accent, thickness, pad, x, cy);
    }

    /// <summary>Wie bei Android/Google: gespielter Teil ist eine wandernde Welle, Rest ist gerade.</summary>
    private void Wave(DrawingContext dc, double w, double cy, double f)
    {
        const double pad = 3;
        var x = Px(w, f, pad);
        Line(dc, _track, 3, x + 4, w - pad, cy);

        var amp = 2.6 * _amp;
        var phase = _t * 5.5;
        var pts = new List<Point>();
        for (var px = pad; px <= x; px += 1.5) pts.Add(new Point(px, cy + Math.Sin(px / 18 * 2 * Math.PI - phase) * amp));
        if (pts.Count == 1) pts.Add(new Point(x, cy));
        Polyline(dc, P(_accent, 3), pts);

        // Senkrechter Griff
        dc.DrawRoundedRectangle(_accent, null, new Rect(x - 2, cy - 7, 4, 14), 2, 2);
    }

    private void Dots(DrawingContext dc, double w, double cy, double f)
    {
        const double step = 7;
        var x = Px(w, f, 3);
        for (var px = 3.0; px <= w - 3; px += step)
        {
            var played = px <= x;
            var r = played ? 2.2 : 1.6;
            dc.DrawEllipse(played ? _accent : _track, null, new Point(px, cy), r, r);
        }
        dc.DrawEllipse(_accent, null, new Point(x, cy), 4, 4);
    }

    private void Glow(DrawingContext dc, double w, double cy, double f)
    {
        var x = Px(w, f, 3);
        Line(dc, _track, 4, 3, w - 3, cy);
        var pulse = 0.25 + 0.12 * Math.Sin(_t * 3) * _amp;
        dc.PushOpacity(pulse);
        Line(dc, _accent, 11, 3, x, cy);
        dc.Pop();
        dc.PushOpacity(0.5);
        Line(dc, _accent, 7, 3, x, cy);
        dc.Pop();
        Line(dc, _accent, 4, 3, x, cy);
        dc.DrawEllipse(_text, null, new Point(x, cy), 3.2, 3.2);
    }

    // ---------------------------------------------------------------- Tiere

    private void Walker(DrawingContext dc, double w, double h, double f, string[][] frames, double ps, bool hop, double speed)
    {
        var spriteW = frames[0][0].Length * ps;
        var lineY = h - 3;
        var pad = spriteW / 2;
        var x = Px(w, f, pad);
        Line(dc, _track, 3, pad, w - 3, lineY);
        Line(dc, _accent, 3, 3, x, lineY);

        var frame = _amp > 0.5 ? (int)(_t / speed) % frames.Length : 0;
        var lift = hop && _amp > 0.5 ? Math.Abs(Math.Sin(_t / speed * Math.PI / 2)) * 3 : 0;
        Sprites.Draw(dc, this, frames[frame], x, lineY - 1.5 - lift, ps);
    }

    // ---------------------------------------------------------------- Pixel Art

    private void PixelBar(DrawingContext dc, double w, double cy, double f)
    {
        const double size = 4, gap = 1.2;
        var count = (int)((w - 4) / (size + gap));
        var filled = (int)Math.Round(count * f);
        for (int i = 0; i < count; i++)
        {
            var b = i < filled ? _accent : _track;
            if (i == filled && _amp > 0.5 && (int)(_t * 3) % 2 == 0) b = _sub; // blinkender Kopf
            dc.DrawRectangle(b, null, new Rect(2 + i * (size + gap), cy - size / 2, size, size));
        }
    }

    private void HeartThumb(DrawingContext dc, double w, double cy, double f)
    {
        var x = Px(w, f, 8);
        var beat = 1 + 0.14 * Math.Max(0, Math.Sin(_t * 7)) * _amp;
        dc.PushTransform(new ScaleTransform(beat, beat, x, cy));
        Sprites.Draw(dc, this, Sprites.Heart, x, cy + 6, 1.5);
        dc.Pop();
    }

    private void Rocket(DrawingContext dc, double w, double h, double f)
    {
        var cy = h / 2;
        var x = Px(w, f, 9);
        // Sterne hinter der Rakete funkeln
        for (int i = 0; i < 24; i++)
        {
            var sx = 4 + (i * 37 % 100) / 100.0 * (w - 8);
            if (sx > x - 10) continue;
            var sy = cy + ((i * 53 % 11) - 5);
            var tw = 0.4 + 0.6 * Math.Abs(Math.Sin(_t * 2 + i));
            dc.PushOpacity(tw);
            dc.DrawRectangle(_accent, null, new Rect(sx, sy, 1.6, 1.6));
            dc.Pop();
        }
        Line(dc, _track, 2, x + 10, w - 3, cy);
        var frame = _amp > 0.5 ? (int)(_t / 0.08) % 2 : 0;
        Sprites.Draw(dc, this, Sprites.Rocket[frame], x, cy + 5, 1.5);
    }

    // ---------------------------------------------------------------- Programmierer

    private void Terminal(DrawingContext dc, double w, double cy, double f)
    {
        var prompt = Text(">", 12, _accent, bold: true);
        dc.DrawText(prompt, new Point(0, cy - prompt.Height / 2));
        var start = prompt.Width + 4;
        var x = start + (w - start - 8) * f;

        // "getippte" Zeichen
        for (var px = start; px < x - 4; px += 6)
        {
            var hgt = 4 + ((int)(px * 7) % 3);
            dc.DrawRectangle(_accent, null, new Rect(px, cy - hgt / 2 + 1, 4, hgt));
        }
        // Rest: gestrichelt
        for (var px = x + 10; px < w - 2; px += 6) dc.DrawRectangle(_track, null, new Rect(px, cy, 3, 1.5));
        // Blinkender Cursor
        if (_amp < 0.5 || (int)(_t * 2.2) % 2 == 0) dc.DrawRectangle(_text, null, new Rect(x, cy - 6, 6, 12));
    }

    private void Code(DrawingContext dc, double w, double cy, double f)
    {
        var tag = Text("</>", 11, _accent, bold: true);
        var pad = tag.Width / 2 + 1;
        var x = Px(w, f, pad);
        for (var px = x + pad + 2; px < w - 2; px += 5) dc.DrawRectangle(_track, null, new Rect(px, cy - 0.75, 2.5, 1.5));
        Line(dc, _accent, 3, 2, x - pad, cy);
        var bob = Math.Sin(_t * 4) * 1.5 * _amp;
        dc.DrawText(tag, new Point(x - tag.Width / 2, cy - tag.Height / 2 + bob));
    }

    private void Binary(DrawingContext dc, double w, double cy, double f)
    {
        var x = Px(w, f, 4);
        var seed = (int)(_t * 6 * _amp);
        var i = 0;
        for (var px = 2.0; px < x - 3; px += 6.5, i++)
        {
            var bit = ((i * 2654435761u) ^ (uint)(seed + i / 3)) % 2 == 0 ? "0" : "1";
            var ft = Text(bit, 9.5, _accent);
            dc.DrawText(ft, new Point(px, cy - ft.Height / 2));
        }
        for (var px = x + 6; px < w - 2; px += 6.5) dc.DrawEllipse(_track, null, new Point(px, cy), 1, 1);
        if (_amp < 0.5 || (int)(_t * 2.5) % 2 == 0) dc.DrawRectangle(_text, null, new Rect(x - 1, cy - 6, 2, 12));
    }

    // ---------------------------------------------------------------- Arbeit

    private void Coffee(DrawingContext dc, double w, double h, double f)
    {
        var lineY = h - 3;
        var x = Px(w, f, 8);
        Line(dc, _track, 3, x + 8, w - 3, lineY);
        Line(dc, _accent, 3, 3, x - 8, lineY);
        var top = Sprites.Draw(dc, this, Sprites.Coffee, x, lineY + 1, 1.4);

        // Dampf
        for (int k = 0; k < 2; k++)
        {
            var pts = new List<Point>();
            var sx = x - 3 + k * 4;
            for (double y = 0; y < 6; y += 1)
                pts.Add(new Point(sx + Math.Sin(y * 1.1 + _t * 5 + k * 2) * 1.2 * (0.4 + _amp * 0.6), top - 1 - y));
            dc.PushOpacity(0.35 + 0.35 * _amp);
            Polyline(dc, P(_sub, 1), pts);
            dc.Pop();
        }
    }

    private void Stripes(DrawingContext dc, double w, double cy, double f)
    {
        const double hgt = 8;
        var bar = new Rect(1, cy - hgt / 2, w - 2, hgt);
        dc.DrawRoundedRectangle(_track, null, bar, hgt / 2, hgt / 2);
        var fill = new Rect(1, cy - hgt / 2, Math.Max(hgt, (w - 2) * f), hgt);
        dc.PushClip(new RectangleGeometry(fill, hgt / 2, hgt / 2));
        dc.DrawRectangle(_accent, null, fill);
        var offset = (_t * 14) % 10;
        dc.PushOpacity(0.28);
        for (var sx = -10 + offset; sx < fill.Right + 10; sx += 10)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(sx, fill.Bottom), true, true);
                c.LineTo(new Point(sx + 5, fill.Bottom), false, false);
                c.LineTo(new Point(sx + 10, fill.Top), false, false);
                c.LineTo(new Point(sx + 5, fill.Top), false, false);
            }
            dc.DrawGeometry(Brushes.White, null, g);
        }
        dc.Pop();
        dc.Pop();
    }

    // ---------------------------------------------------------------- Freizeit

    private void Football(DrawingContext dc, double w, double h, double f)
    {
        const double r = 6;
        var lineY = h - 2;
        var x = Px(w, f, r);
        Line(dc, _track, 2, x + r, w - 2, lineY);
        Line(dc, Sprites.Green, 2, 2, x, lineY);
        var center = new Point(x, lineY - r - 0.5);
        dc.DrawEllipse(Brushes.White, P(Sprites.Dark, 0.8), center, r, r);
        // Muster dreht sich beim Rollen
        var angle = x / r * 180 / Math.PI;
        dc.PushTransform(new RotateTransform(angle, center.X, center.Y));
        dc.DrawEllipse(Sprites.Dark, null, center, 2, 2);
        for (int k = 0; k < 5; k++)
        {
            var a = k * 72 * Math.PI / 180;
            dc.DrawEllipse(Sprites.Dark, null, new Point(center.X + Math.Cos(a) * 4.6, center.Y + Math.Sin(a) * 4.6), 1.3, 1.3);
        }
        dc.Pop();
    }

    private void Gamepad(DrawingContext dc, double w, double h, double f)
    {
        var cy = h / 2;
        var x = Px(w, f, 10);
        Line(dc, _accent, 3, 3, x, cy + 2);
        // Münzen, die noch eingesammelt werden
        for (var px = 6.0; px < w - 4; px += 11)
        {
            if (px < x + 12) continue;
            var spin = Math.Abs(Math.Cos(_t * 3 + px));
            dc.DrawEllipse(Sprites.Yellow, null, new Point(px, cy + 2), 2.4 * (0.4 + 0.6 * spin), 2.4);
        }
        var bob = Math.Sin(_t * 6) * 1.2 * _amp;
        Sprites.Draw(dc, this, Sprites.Pad, x, cy + 7 + bob, 1.4);
    }

    private void Notes(DrawingContext dc, double w, double h, double f)
    {
        var lineY = h - 3;
        var x = Px(w, f, 4);
        Line(dc, _track, 3, x, w - 3, lineY);
        Line(dc, _accent, 3, 3, x, lineY);
        dc.DrawEllipse(_accent, null, new Point(x, lineY), 4, 4);
        if (_amp < 0.05) return;

        // Noten steigen auf und verblassen
        for (int k = 0; k < 4; k++)
        {
            var life = (_t * 0.9 + k * 0.25) % 1.0;
            var spawn = Math.Floor(_t * 0.9 + k * 0.25);
            var nx = x - 4 - life * 14 + ((int)(spawn * 13 + k * 7) % 9) - 4;
            var ny = lineY - 4 - life * (h - 4);
            var ft = Text(k % 2 == 0 ? "♪" : "♫", 10, _accent, "Segoe UI Symbol");
            dc.PushOpacity((1 - life) * _amp);
            dc.DrawText(ft, new Point(nx, ny - ft.Height / 2));
            dc.Pop();
        }
    }

    // ---------------------------------------------------------------- Natur

    private void Vine(DrawingContext dc, double w, double cy, double f)
    {
        var x = Px(w, f, 4);
        for (var px = x + 6; px < w - 2; px += 5) dc.DrawEllipse(_track, null, new Point(px, cy), 1, 1);

        var pts = new List<Point>();
        for (var px = 3.0; px <= x; px += 1.5) pts.Add(new Point(px, cy + Math.Sin(px / 9) * 2));
        Polyline(dc, P(Sprites.Green, 2), pts);

        var sway = Math.Sin(_t * 2) * 8 * _amp;
        var i = 0;
        for (var px = 10.0; px < x - 3; px += 12, i++)
        {
            var grow = Math.Clamp((x - px) / 20, 0.3, 1);
            var up = i % 2 == 0;
            var baseY = cy + Math.Sin(px / 9) * 2;
            dc.PushTransform(new RotateTransform((up ? -40 : 40) + sway, px, baseY));
            dc.DrawEllipse(Sprites.LightGreen, null, new Point(px + 3.5 * grow, baseY), 3.6 * grow, 1.8 * grow);
            dc.Pop();
        }
        // Knospe an der Spitze
        dc.DrawEllipse(Sprites.Pink, null, new Point(x, cy + Math.Sin(x / 9) * 2), 2.8, 2.8);
    }

    private void Flower(DrawingContext dc, double x, double cy)
    {
        var turn = _t * 40 * _amp;
        dc.PushTransform(new RotateTransform(turn, x, cy));
        for (int k = 0; k < 5; k++)
        {
            dc.PushTransform(new RotateTransform(k * 72, x, cy));
            dc.DrawEllipse(Sprites.Pink, null, new Point(x, cy - 4), 2.6, 3.6);
            dc.Pop();
        }
        dc.Pop();
        dc.DrawEllipse(Sprites.Yellow, null, new Point(x, cy), 2.4, 2.4);
    }

    private void Bee(DrawingContext dc, double w, double cy, double f)
    {
        var x = Px(w, f, 8);
        Line(dc, _track, 2, x + 8, w - 3, cy);
        // Flugbahn als Pünktchen
        for (var px = 3.0; px < x - 8; px += 4.5)
        {
            var y = cy + Math.Sin(px / 7 - _t * 3 * _amp) * 3;
            dc.DrawEllipse(_accent, null, new Point(px, y), 0.9, 0.9);
        }
        var bob = Math.Sin(_t * 8) * 2 * _amp;
        var frame = _amp > 0.5 ? (int)(_t / 0.05) % 2 : 0;
        Sprites.Draw(dc, this, Sprites.BeeFrames[frame], x, cy + 5 + bob, 1.4);
    }

    private void Sea(DrawingContext dc, double w, double cy, double f)
    {
        var x = Px(w, f, 3);
        Line(dc, _track, 3, x + 2, w - 3, cy);
        for (int layer = 0; layer < 2; layer++)
        {
            var pts = new List<Point>();
            var ph = _t * (layer == 0 ? 3 : -2.2);
            for (var px = 3.0; px <= x; px += 1.5)
                pts.Add(new Point(px, cy + Math.Sin(px / (layer == 0 ? 11 : 7) * 2 * Math.PI / 2 + ph) * (1 + 1.8 * _amp)));
            dc.PushOpacity(layer == 0 ? 1 : 0.45);
            Polyline(dc, P(layer == 0 ? _accent : Sprites.Cyan, 2.4), pts);
            dc.Pop();
        }
        dc.DrawEllipse(_text, null, new Point(x, cy), 3, 3);
    }

    // ---------------------------------------------------------------- Wissenschaft

    private void Dna(DrawingContext dc, double w, double cy, double f)
    {
        var x = Px(w, f, 3);
        Line(dc, _track, 2, x + 3, w - 3, cy);
        const double amp = 4.5, period = 18;
        var phase = _t * 3 * (0.3 + 0.7 * _amp);
        double Y(double px, double shift) => cy + Math.Sin(px / period * 2 * Math.PI - phase + shift) * amp;

        // Sprossen
        var i = 0;
        for (var px = 3.0; px <= x; px += 3, i++)
        {
            var y1 = Y(px, 0);
            var y2 = Y(px, Math.PI);
            dc.PushOpacity(0.55);
            dc.DrawLine(P(i % 2 == 0 ? _accent : _sub, 1, round: false), new Point(px, y1), new Point(px, y2));
            dc.Pop();
        }
        var a = new List<Point>();
        var b = new List<Point>();
        for (var px = 3.0; px <= x; px += 1.5) { a.Add(new Point(px, Y(px, 0))); b.Add(new Point(px, Y(px, Math.PI))); }
        Polyline(dc, P(_sub, 2), b);
        Polyline(dc, P(_accent, 2), a);
    }

    private void Ecg(DrawingContext dc, double w, double cy, double f)
    {
        var x = Px(w, f, 3);
        Line(dc, _track, 2, x, w - 3, cy);
        var shift = (_t * 28 * _amp) % 42;
        var pts = new List<Point> { new(3, cy) };
        for (var px = 3.0; px <= x; px += 1)
        {
            var local = (px + shift) % 42;
            double y = local switch
            {
                > 20 and < 23 => cy + (local - 20) * 1.2,       // Q
                >= 23 and < 26 => cy + 3.6 - (local - 23) * 4.4, // R (Spitze)
                >= 26 and < 29 => cy - 9.6 + (local - 26) * 4.2, // S
                >= 29 and < 31 => cy + 3 - (local - 29) * 1.5,
                >= 33 and < 38 => cy - Math.Sin((local - 33) / 5 * Math.PI) * 2, // T-Welle
                _ => cy,
            };
            pts.Add(new Point(px, Math.Clamp(y, 1, ActualHeight - 1)));
        }
        dc.PushOpacity(0.3);
        Polyline(dc, P(_accent, 4), pts);
        dc.Pop();
        Polyline(dc, P(_accent, 1.6), pts);
        dc.DrawEllipse(_text, null, pts[^1], 2.4, 2.4);
    }

    private void Atom(DrawingContext dc, double x, double cy)
    {
        var center = new Point(x, cy);
        for (int k = 0; k < 3; k++)
        {
            dc.PushTransform(new RotateTransform(k * 60 + 15, x, cy));
            dc.DrawEllipse(null, P(_sub, 0.8), center, 8, 3);
            var a = _t * (2.4 + k * 0.5) * (0.25 + 0.75 * _amp) + k * 2;
            dc.DrawEllipse(_accent, null, new Point(x + Math.Cos(a) * 8, cy + Math.Sin(a) * 3), 1.4, 1.4);
            dc.Pop();
        }
        dc.DrawEllipse(_text, null, center, 2.2, 2.2);
    }

    private void Planet(DrawingContext dc, double w, double cy, double f)
    {
        var x = Px(w, f, 10);
        // Umlaufbahn als Punkte
        for (var px = 3.0; px < w - 3; px += 5)
        {
            if (Math.Abs(px - x) < 10) continue;
            dc.DrawEllipse(px < x ? _accent : _track, null, new Point(px, cy), px < x ? 1.2 : 0.9, px < x ? 1.2 : 0.9);
        }
        var tilt = -18 + Math.Sin(_t * 1.5) * 6 * _amp;
        dc.PushTransform(new RotateTransform(tilt, x, cy));
        // hinterer Teil des Rings
        dc.PushClip(new RectangleGeometry(new Rect(x - 12, cy - 6, 24, 6)));
        dc.DrawEllipse(null, P(_sub, 1.3), new Point(x, cy), 10, 3);
        dc.Pop();
        dc.DrawEllipse(_accent, null, new Point(x, cy), 5.5, 5.5);
        dc.PushOpacity(0.35);
        dc.DrawEllipse(Brushes.White, null, new Point(x - 1.8, cy - 1.8), 1.8, 1.8);
        dc.Pop();
        // vorderer Teil des Rings
        dc.PushClip(new RectangleGeometry(new Rect(x - 12, cy, 24, 6)));
        dc.DrawEllipse(null, P(_text, 1.3), new Point(x, cy), 10, 3);
        dc.Pop();
        dc.Pop();
    }

    // ================================================================== Pixel-Figuren

    internal static class Sprites
    {
        public static readonly SolidColorBrush Dark = Make("#FF1C1C1E");
        public static readonly SolidColorBrush Pink = Make("#FFFF8FAB");
        public static readonly SolidColorBrush Orange = Make("#FFFF9F0A");
        public static readonly SolidColorBrush Yellow = Make("#FFFFD60A");
        public static readonly SolidColorBrush Green = Make("#FF30D158");
        public static readonly SolidColorBrush LightGreen = Make("#FFA8E6A1");
        public static readonly SolidColorBrush Brown = Make("#FFA2845E");
        public static readonly SolidColorBrush Beige = Make("#FFE8D3A9");
        public static readonly SolidColorBrush Red = Make("#FFFF453A");
        public static readonly SolidColorBrush Cyan = Make("#FF64D2FF");
        public static readonly SolidColorBrush White = Make("#FFFFFFFF");

        private static SolidColorBrush Make(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        // t = Textfarbe, i = Gegenfarbe (Augen), s = gedämpft, a = Akzent, sonst feste Farben

        public static readonly string[][] Cat =
        {
            new[]
            {
                "..........t.t",
                ".t........ttt",
                "t.........tit",
                "t..tttttttttp",
                ".tttttttttt..",
                "..tttttttt...",
                "..t.t...t.t..",
            },
            new[]
            {
                "..........t.t",
                ".t........ttt",
                "t.........tit",
                "t..tttttttttp",
                ".tttttttttt..",
                "..tttttttt...",
                "...t.t...t.t.",
            },
        };

        public static readonly string[][] Dog =
        {
            new[]
            {
                ".........ttt.",
                "t.......btitt",
                "t.......bttti",
                ".ttttttttttp.",
                ".tttttttttt..",
                ".t.t....t.t..",
            },
            new[]
            {
                ".........ttt.",
                "t.......btitt",
                "t.......bttti",
                ".ttttttttttp.",
                ".tttttttttt..",
                "..t.t....t.t.",
            },
        };

        public static readonly string[][] Bunny =
        {
            new[]
            {
                ".......t.t.",
                ".......t.t.",
                ".......ttt.",
                "......ttitp",
                "s.tttttttt.",
                ".ttttttttt.",
                "..tt...tt..",
            },
        };

        public static readonly string[][] Duck =
        {
            new[]
            {
                "......yy...",
                ".....yyky..",
                ".....yyyyoo",
                "yy..yyyy...",
                "yyyyyyyyy..",
                ".yyyyyyyy..",
                "...o..o....",
            },
            new[]
            {
                "......yy...",
                ".....yyky..",
                ".....yyyyoo",
                "yy..yyyy...",
                "yyyyyyyyy..",
                ".yyyyyyyy..",
                "....o..o...",
            },
        };

        public static readonly string[][] Turtle =
        {
            new[]
            {
                "....ggggg....",
                "...gagggagg..",
                "..gggggggggll",
                ".ggggggggggki",
                ".lllllllllll.",
                "..ll....ll...",
            },
            new[]
            {
                "....ggggg....",
                "...gagggagg..",
                "..gggggggggll",
                ".ggggggggggki",
                ".lllllllllll.",
                "...ll....ll..",
            },
        };

        public static readonly string[][] Snail =
        {
            new[]
            {
                "..bbbb...e.e.",
                ".bbeebb..e.e.",
                "bbeebeb..eee.",
                "bbebbbb.eeee.",
                ".bbbbbeeeeee.",
                "eeeeeeeeeeeee",
            },
            new[]
            {
                "..bbbb....e.e",
                ".bbeebb...e.e",
                "bbeebeb...eee",
                "bbebbbb..eeee",
                ".bbbbbeeeeeee",
                "eeeeeeeeeeeee",
            },
        };

        public static readonly string[] Heart =
        {
            ".rr...rr.",
            "rrrr.rrrr",
            "rwrrrrrrr",
            "rrrrrrrrr",
            ".rrrrrrr.",
            "..rrrrr..",
            "...rrr...",
            "....r....",
        };

        public static readonly string[][] Rocket =
        {
            new[]
            {
                "..rr.......",
                "..tttttt...",
                "yottccttttr",
                "oyttccttttr",
                "..tttttt...",
                "..rr.......",
            },
            new[]
            {
                "..rr.......",
                "..tttttt...",
                "oyttccttttr",
                "yottccttttr",
                "..tttttt...",
                "..rr.......",
            },
        };

        public static readonly string[] Coffee =
        {
            "ttttttt..",
            "tbbbbbttt",
            "ttttttt.t",
            "ttttttt.t",
            "tttttttt.",
            ".ttttt...",
            "sssssss..",
        };

        public static readonly string[] Pad =
        {
            "..ttttttttt..",
            ".tttitttttrt.",
            ".ttiiitttgtc.",
            ".tttitttttyt.",
            "ttttttttttttt",
            "tttt.....tttt",
            ".tt.......tt.",
        };

        public static readonly string[][] BeeFrames =
        {
            new[]
            {
                "...ww.ww.",
                "...wwwww.",
                ".yykyykyy",
                "kyykyykyk",
                ".yykyykyy",
            },
            new[]
            {
                "...ss.ss.",
                "....sss..",
                ".yykyykyy",
                "kyykyykyk",
                ".yykyykyy",
            },
        };

        /// <summary>Zeichnet eine Figur: unten-Mitte bei (cx, bottom). Gibt die Oberkante zurück.</summary>
        public static double Draw(DrawingContext dc, ProgressTrack owner, string[] rows, double cx, double bottom, double ps)
        {
            var h = rows.Length;
            var w = rows[0].Length;
            var left = Math.Round(cx - w * ps / 2, 1);
            var top = bottom - h * ps;
            var inverse = Theme.CurrentIsLight ? White : Dark;
            for (int y = 0; y < h; y++)
            {
                var row = rows[y];
                for (int x = 0; x < row.Length; x++)
                {
                    Brush? b = row[x] switch
                    {
                        't' => owner._text,
                        'i' => inverse,
                        's' => owner._sub,
                        'a' => owner._accent,
                        'k' => Dark,
                        'w' => White,
                        'p' => Pink,
                        'o' => Orange,
                        'y' => Yellow,
                        'g' => Green,
                        'l' => LightGreen,
                        'b' => Brown,
                        'e' => Beige,
                        'r' => Red,
                        'c' => Cyan,
                        _ => null,
                    };
                    if (b != null) dc.DrawRectangle(b, null, new Rect(left + x * ps, top + y * ps, ps + 0.35, ps + 0.35));
                }
            }
            return top;
        }
    }
}

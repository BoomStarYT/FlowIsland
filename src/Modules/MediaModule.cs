using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Island.Core;
using WMC = global::Windows.Media.Control;
using WStreams = global::Windows.Storage.Streams;

namespace Island.Modules;

/// <summary>
/// Zeigt, was gerade läuft – über die eingebaute Windows-Mediensteuerung (Spotify, Browser, YouTube …).
/// Kein Login, kein Premium. Lyrics (optional) kommen von lrclib.net.
/// Spart Akku: reagiert nur auf Ereignisse; animiert nur, wenn man es sieht.
/// </summary>
public sealed class MediaModule : IslandModule
{
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";
    private const string NoteGlyph = "";
    private const double LyricsHeight = 54;

    public override string Name => "Musik";
    public override string Icon => NoteGlyph;
    public override int Order => 0;
    public override double CompactWidth => 260;
    public override double ExpandedHeight => 112 + (LyricsVisible ? LyricsHeight : 0);
    public override bool WantsCompact => _hasMedia && !PausedLongEnough;

    public override FrameworkElement? CompactView => _compact;
    public override FrameworkElement? MediumView => _medium;
    public override double MediumWidth => 372;
    public override double MediumHeight => 66;
    public override FrameworkElement? ExpandedView => _view;

    /// <summary>Länger pausiert als eingestellt → zugeklappt lieber die Ruheanzeige zeigen.</summary>
    private bool PausedLongEnough => !_playing
        && Context.Settings.PauseZuRuheSekunden > 0
        && (DateTime.UtcNow - _pausedAtUtc).TotalSeconds >= Context.Settings.PauseZuRuheSekunden;

    private bool LyricsVisible => Context.Settings.Lyrics && _hasMedia;

    private MediaView _view = null!;
    private Grid _compact = null!;
    private Border _compactCover = null!;
    private TextBlock _compactTitle = null!;
    private TranslateTransform _compactTitleSlide = null!;
    private EqualizerBars _eqCompact = null!;
    private EqualizerBars _eqExpanded = null!;
    private ProgressTrack _progress = null!;

    // Halb offen
    private Grid _medium = null!;
    private Border _mediumCover = null!;
    private TextBlock _mediumTitle = null!, _mediumArtist = null!;
    private Button _mediumPlay = null!;
    private ProgressTrack _mediumProgress = null!;

    private WMC.GlobalSystemMediaTransportControlsSessionManager? _manager;
    private WMC.GlobalSystemMediaTransportControlsSession? _session;
    private string? _sessionId;
    private string? _manualId;          // über "•••" gewählte Quelle (null = automatisch)
    private bool _lastNurSpotify;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _idleTimer = new();
    private readonly DispatcherTimer _lyricsTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private DateTime _pausedAtUtc = DateTime.MinValue;

    private bool _hasMedia, _playing, _canSeek, _dragging, _shown, _sourceOpen;
    private double _dragFraction;
    private TimeSpan _start, _position, _duration;
    private DateTime _positionStampUtc = DateTime.UtcNow;
    private ImageBrush? _cover;
    private string? _coverKey;
    private string? _songKey;
    private string _title = "", _artist = "", _album = "";

    private List<LyricLine>? _lyrics;
    private string? _lyricsKey;
    private int _lyricIndex = -2;
    private bool _lyricsLoading;

    // ================================================================== Start

    protected override void OnInitialize()
    {
        _view = new MediaView();
        _eqExpanded = new EqualizerBars(14, 3);
        _view.EqHost.Content = _eqExpanded;

        _progress = new ProgressTrack { Source = () => _dragging ? _dragFraction : Fraction(CurrentPosition()) };
        _view.Track.Children.Add(_progress);

        BuildCompactView();
        BuildMediumView();
        WireUpView();
        _lastNurSpotify = Context.Settings.NurSpotify;
        ApplyLook();

        _tick.Tick += (_, _) => UpdateTimes();
        _idleTimer.Tick += (_, _) => { _idleTimer.Stop(); Context.RequestCompactRefresh(); };
        _lyricsTimer.Tick += (_, _) => UpdateLyricLine(animate: true);
        _compactTitle.IsVisibleChanged += (_, _) => UpdateLyricsTimer();
        ShowNothingPlaying();
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            _manager = await WMC.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += (_, _) => Post(() => { _ = SelectSessionAsync(); if (_sourceOpen) _ = FillSourcePanelAsync(); });
            _manager.CurrentSessionChanged += (_, _) => Post(() => _ = SelectSessionAsync());
            await SelectSessionAsync();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    /// <summary>Windows meldet Änderungen auf einem anderen Thread → zurück auf den UI-Thread.</summary>
    private void Post(Action action) => Context.Dispatcher.InvokeAsync(action);

    public override void OnSettingsChanged()
    {
        var lyricsBefore = _view.LyricsPanel.Visibility == Visibility.Visible;
        ApplyLook();
        if (_lastNurSpotify != Context.Settings.NurSpotify)
        {
            _lastNurSpotify = Context.Settings.NurSpotify;
            _ = SelectSessionAsync(force: true);
        }
        if (Context.Settings.Lyrics) _ = LoadLyricsAsync();
        ApplyLyricsVisibility();
        if (lyricsBefore != (_view.LyricsPanel.Visibility == Visibility.Visible)) Context.RequestLayoutRefresh();
        UpdateCompactTitle();
    }

    private void ApplyLook()
    {
        _eqCompact.Animated = Context.Settings.EqualizerAnimiert;
        _eqExpanded.Animated = Context.Settings.EqualizerAnimiert;
        _progress.StyleId = Context.Settings.Fortschritt;
        _mediumProgress.StyleId = Context.Settings.Fortschritt;
    }

    // ================================================================== Welche App?

    private WMC.GlobalSystemMediaTransportControlsSession? PickSession()
    {
        if (_manager == null) return null;
        var sessions = _manager.GetSessions();

        if (_manualId != null)
        {
            var manual = sessions.FirstOrDefault(s => s.SourceAppUserModelId == _manualId);
            if (manual != null) return manual;
            _manualId = null; // App wurde geschlossen → wieder automatisch
        }

        foreach (var s in sessions)
        {
            var id = s.SourceAppUserModelId ?? "";
            if (id.Contains("spotify", StringComparison.OrdinalIgnoreCase)) return s;
        }

        return Context.Settings.NurSpotify ? null : _manager.GetCurrentSession();
    }

    private async Task SelectSessionAsync(bool force = false)
    {
        WMC.GlobalSystemMediaTransportControlsSession? next = null;
        try { next = PickSession(); }
        catch (Exception ex) { App.Log(ex); }

        var nextId = next?.SourceAppUserModelId;
        if (!force && next != null && _session != null && nextId == _sessionId) return; // gleiche App wie vorher

        Unsubscribe();
        _session = next;
        _sessionId = nextId;
        _coverKey = null;
        _songKey = null;

        if (_session == null)
        {
            ShowNothingPlaying();
            return;
        }

        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged += OnTimelineChanged;

        RefreshPlayback();
        RefreshTimeline();
        await RefreshMediaAsync();
    }

    private void Unsubscribe()
    {
        if (_session == null) return;
        try
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        catch { /* App wurde schon beendet */ }
    }

    private void OnMediaPropertiesChanged(WMC.GlobalSystemMediaTransportControlsSession s, WMC.MediaPropertiesChangedEventArgs e)
        => Post(() => _ = RefreshMediaAsync());

    private void OnPlaybackInfoChanged(WMC.GlobalSystemMediaTransportControlsSession s, WMC.PlaybackInfoChangedEventArgs e)
        => Post(RefreshPlayback);

    private void OnTimelineChanged(WMC.GlobalSystemMediaTransportControlsSession s, WMC.TimelinePropertiesChangedEventArgs e)
        => Post(RefreshTimeline);

    /// <summary>Lesbarer Name aus der App-Kennung (z. B. "Chrome", "Spotify").</summary>
    private static string AppName(string? id)
    {
        var l = (id ?? "").ToLowerInvariant();
        (string Key, string Name)[] known =
        {
            ("spotify", "Spotify"), ("chrome", "Chrome"), ("msedge", "Edge"), ("firefox", "Firefox"), ("opera", "Opera"),
            ("brave", "Brave"), ("vivaldi", "Vivaldi"), ("zunemusic", "Medienwiedergabe"), ("zunevideo", "Filme & TV"),
            ("vlc", "VLC"), ("applemusic", "Apple Music"), ("discord", "Discord"), ("tidal", "Tidal"), ("deezer", "Deezer"),
            ("soundcloud", "SoundCloud"), ("amazon", "Amazon Music"), ("youtube", "YouTube Music"),
        };
        foreach (var (key, name) in known) if (l.Contains(key)) return name;
        var raw = id ?? "App";
        raw = raw.Split('!')[0].Split('\\').Last().Split('_')[0];
        if (raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) raw = raw[..^4];
        raw = raw.Split('.').Last();
        return raw.Length == 0 ? "App" : char.ToUpperInvariant(raw[0]) + raw[1..];
    }

    private static string AppGlyph(string? id)
    {
        var l = (id ?? "").ToLowerInvariant();
        if (l.Contains("spotify") || l.Contains("music")) return NoteGlyph;
        if (l.Contains("chrome") || l.Contains("edge") || l.Contains("firefox") || l.Contains("opera") || l.Contains("brave") || l.Contains("vivaldi")) return "";
        if (l.Contains("video") || l.Contains("vlc")) return "";
        return "";
    }

    // ================================================================== Daten holen

    private async Task RefreshMediaAsync()
    {
        var session = _session;
        if (session == null) return;

        WMC.GlobalSystemMediaTransportControlsSessionMediaProperties? props = null;
        try { props = await session.TryGetMediaPropertiesAsync(); }
        catch (Exception ex) { App.Log(ex); }
        if (!ReferenceEquals(session, _session)) return; // inzwischen andere App

        if (props == null || string.IsNullOrWhiteSpace(props.Title))
        {
            ShowNothingPlaying(keepTimeline: true);
            return;
        }

        var artist = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumTitle : props.Artist;
        var key = $"{props.Title}|{props.Artist}|{props.AlbumTitle}";
        var isNewSong = key != _songKey;
        var hadMedia = _hasMedia;
        _songKey = key;
        _hasMedia = true;
        _title = props.Title;
        _artist = props.Artist ?? "";
        _album = props.AlbumTitle ?? "";

        _view.TitleText.Text = props.Title;
        _view.ArtistText.Text = string.IsNullOrWhiteSpace(artist) ? AppName(_sessionId) : artist;
        _mediumTitle.Text = props.Title;
        _mediumArtist.Text = _view.ArtistText.Text;
        _view.CoverBorder.ToolTip = $"{AppName(_sessionId)} öffnen";
        if (isNewSong)
        {
            _lyrics = null;
            _lyricsKey = null;
            _lyricIndex = -2;
            AnimateSongChange();
        }
        UpdateCompactTitle();

        // Cover nur neu laden, wenn ein anderer Song läuft
        if (key != _coverKey)
        {
            var brush = await LoadCoverAsync(props.Thumbnail);
            if (!ReferenceEquals(session, _session)) return;
            _cover = brush;
            _coverKey = brush != null ? key : null; // ohne Cover: beim nächsten Ereignis nochmal versuchen
            ApplyCover(animate: true);
            Theme.SetCoverColor(CoverColors.Vibrant(brush?.ImageSource as BitmapSource));
        }

        UpdatePlayState();
        if (isNewSong) _ = LoadLyricsAsync();
        ApplyLyricsVisibility();
        if (!hadMedia && Context.Settings.Lyrics) Context.RequestLayoutRefresh();
        Context.RequestCompactRefresh();
    }

    private void RefreshPlayback()
    {
        var session = _session;
        if (session == null) return;

        WMC.GlobalSystemMediaTransportControlsSessionPlaybackInfo? info;
        try { info = session.GetPlaybackInfo(); }
        catch { return; }
        if (info == null) return;

        // Aktuelle Position "einfrieren", bevor sich Play/Pause ändert
        _position = CurrentPosition();
        _positionStampUtc = DateTime.UtcNow;

        SetPlaying(info.PlaybackStatus == WMC.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        _canSeek = info.Controls?.IsPlaybackPositionEnabled ?? false;
        _view.Track.Cursor = _canSeek ? Cursors.Hand : Cursors.Arrow;

        UpdatePlayState();
        if (!_dragging) UpdateTicking();
    }

    private void RefreshTimeline()
    {
        var session = _session;
        if (session == null) return;

        WMC.GlobalSystemMediaTransportControlsSessionTimelineProperties? t;
        try { t = session.GetTimelineProperties(); }
        catch { return; }
        if (t == null) return;

        _start = t.StartTime;
        _duration = t.EndTime - t.StartTime;
        if (_duration < TimeSpan.Zero) _duration = TimeSpan.Zero;
        _position = t.Position - t.StartTime;

        // Windows sagt, WANN die Position gemessen wurde → damit rechnen wir weiter
        var now = DateTime.UtcNow;
        var stamp = t.LastUpdatedTime.UtcDateTime;
        if (stamp > now || now - stamp > TimeSpan.FromMinutes(30)) stamp = now;
        _positionStampUtc = stamp;

        if (!_dragging) UpdateTicking();
        UpdateLyricLine(animate: false);
    }

    private static async Task<ImageBrush?> LoadCoverAsync(WStreams.IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail == null) return null;
        try
        {
            using var winStream = await thumbnail.OpenReadAsync();
            using var stream = winStream.AsStreamForRead();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            if (memory.Length == 0) return null;
            memory.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 208; // klein dekodieren = wenig RAM
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();

            var brush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            return brush;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return null;
        }
    }

    // ================================================================== Anzeige

    private void ShowNothingPlaying(bool keepTimeline = false)
    {
        var hadMedia = _hasMedia;
        _hasMedia = false;
        _cover = null;
        _coverKey = null;
        _songKey = null;
        _lyrics = null;
        _lyricsKey = null;
        _view.TitleText.Text = "Gerade läuft nichts";
        _view.ArtistText.Text = _session == null ? "Klick aufs Cover, um Spotify zu öffnen" : "Starte einen Song";
        _mediumTitle.Text = _view.TitleText.Text;
        _mediumArtist.Text = "";
        _compactTitle.Text = "";
        ApplyCover(animate: false);
        Theme.SetCoverColor(null);

        if (!keepTimeline)
        {
            _playing = false;
            _position = TimeSpan.Zero;
            _duration = TimeSpan.Zero;
        }

        UpdatePlayState();
        UpdateTicking();
        ApplyLyricsVisibility();
        if (hadMedia && Context.Settings.Lyrics) Context.RequestLayoutRefresh();
        Context.RequestCompactRefresh();
    }

    private void ApplyCover(bool animate)
    {
        foreach (var border in new[] { _view.CoverBorder, _compactCover, _mediumCover })
        {
            if (_cover == null) border.Themed(Border.BackgroundProperty, "IslandPlaceholder");
            else border.Background = _cover;
        }
        _view.CoverIcon.Visibility = _cover == null ? Visibility.Visible : Visibility.Collapsed;

        if (animate && _cover != null)
        {
            Anim.To(_view.CoverBorder, UIElement.OpacityProperty, 1, 320, Anim.Smooth, from: 0.25);
            Anim.To(_mediumCover, UIElement.OpacityProperty, 1, 320, Anim.Smooth, from: 0.25);
            Anim.To(_compactCover, UIElement.OpacityProperty, _playing ? 1 : 0.6, 320, Anim.Smooth, from: 0.2);
        }
    }

    /// <summary>Titel gleitet beim Songwechsel sanft rein.</summary>
    private void AnimateSongChange()
    {
        Anim.To(_view.TitleSlide, TranslateTransform.XProperty, 0, 520, Anim.Spring, from: 16);
        Anim.To(_view.ArtistSlide, TranslateTransform.XProperty, 0, 560, Anim.Spring, delayMs: 40, from: 16);
        Anim.To(_view.TitleText, UIElement.OpacityProperty, 1, 260, Anim.Smooth, from: 0);
        Anim.To(_view.ArtistText, UIElement.OpacityProperty, 1, 300, Anim.Smooth, delayMs: 40, from: 0);
        Anim.To(_compactTitleSlide, TranslateTransform.XProperty, 0, 520, Anim.Spring, from: 14);
        Anim.To(_compactTitle, UIElement.OpacityProperty, _playing ? 1 : 0.55, 280, Anim.Smooth, from: 0);
        Anim.To(_mediumTitle, UIElement.OpacityProperty, 1, 280, Anim.Smooth, from: 0);
    }

    /// <summary>Zeigt klar, ob Musik läuft: Equalizer hüpft vs. liegt flach, Cover wird kleiner und blasser bei Pause.</summary>
    private void UpdatePlayState()
    {
        var playing = _playing && _hasMedia;
        _view.PlayBtn.Content = _playing ? PauseGlyph : PlayGlyph;
        _mediumPlay.Content = _playing ? PauseGlyph : PlayGlyph;
        _eqCompact.IsPlaying = playing;
        _eqExpanded.IsPlaying = playing;
        _progress.Playing = playing;
        _mediumProgress.Playing = playing;

        Anim.To(_compactTitle, UIElement.OpacityProperty, playing || !_hasMedia ? 1 : 0.55, 250, Anim.Smooth);
        Anim.To(_compactCover, UIElement.OpacityProperty, playing || !_hasMedia ? 1 : 0.6, 250, Anim.Smooth);

        // Wie beim iPhone: Cover schrumpft leicht, wenn pausiert
        var coverScale = playing || !_hasMedia ? 1.0 : 0.9;
        Anim.To(_view.CoverScale, ScaleTransform.ScaleXProperty, coverScale, 520, Anim.Spring);
        Anim.To(_view.CoverScale, ScaleTransform.ScaleYProperty, coverScale, 520, Anim.Spring);
        UpdateLyricsTimer();
    }

    /// <summary>Merkt sich, seit wann pausiert ist (für den Wechsel zur Ruheanzeige).</summary>
    private void SetPlaying(bool playing)
    {
        if (_playing == playing) return;
        _playing = playing;
        _idleTimer.Stop();
        if (!playing)
        {
            _pausedAtUtc = DateTime.UtcNow;
            var seconds = Context.Settings.PauseZuRuheSekunden;
            if (seconds > 0)
            {
                _idleTimer.Interval = TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(200);
                _idleTimer.Start();
            }
        }
        Context.RequestCompactRefresh();
    }

    /// <summary>Zeiten nur aktualisieren, wenn man sie sieht und Musik läuft.</summary>
    private void UpdateTicking()
    {
        var run = _shown && _playing && _hasMedia && !_dragging;
        _tick.Stop();
        if (run) _tick.Start();
        UpdateTimes();
    }

    private void UpdateTimes()
    {
        if (_dragging) return;
        _view.PosText.Text = Format(CurrentPosition());
        _view.DurText.Text = _duration > TimeSpan.Zero ? Format(_duration) : "0:00";
        _progress.InvalidateVisual();
        _mediumProgress.InvalidateVisual();
    }

    private TimeSpan CurrentPosition()
    {
        var p = _position;
        if (_playing) p += DateTime.UtcNow - _positionStampUtc;
        if (p < TimeSpan.Zero) p = TimeSpan.Zero;
        if (_duration > TimeSpan.Zero && p > _duration) p = _duration;
        return p;
    }

    private double Fraction(TimeSpan p)
        => _duration.Ticks <= 0 ? 0 : Math.Clamp((double)p.Ticks / _duration.Ticks, 0, 1);

    private static string Format(TimeSpan t)
        => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    // ================================================================== Lyrics

    private async Task LoadLyricsAsync()
    {
        if (!Context.Settings.Lyrics || !_hasMedia) return;
        var key = _songKey;
        if (key == null || key == _lyricsKey) return;
        _lyricsKey = key;
        _lyricsLoading = true;
        ShowLyricText("Lyrics werden geladen …", "", animate: false);

        List<LyricLine>? lines = null;
        try { lines = await LyricsService.GetAsync(_title, _artist, _album, _duration); }
        catch (Exception ex) { App.Log(ex); }
        if (key != _songKey) return; // inzwischen anderer Song

        _lyricsLoading = false;
        _lyrics = lines;
        _lyricIndex = -2;
        if (lines == null) ShowLyricText("Keine Lyrics gefunden", "", animate: true);
        UpdateLyricLine(animate: true);
        UpdateCompactTitle();
        UpdateLyricsTimer();
    }

    private void ApplyLyricsVisibility()
    {
        _view.LyricsPanel.Visibility = LyricsVisible ? Visibility.Visible : Visibility.Collapsed;
        _view.Height = ExpandedHeight;
    }

    private void UpdateLyricsTimer()
    {
        var needed = _playing && _hasMedia && _lyrics != null && Context.Settings.Lyrics
                     && ((_shown && !_sourceOpen) || (Context.Settings.LyricsKompakt && _compactTitle.IsVisible));
        if (needed) { if (!_lyricsTimer.IsEnabled) _lyricsTimer.Start(); }
        else _lyricsTimer.Stop();
    }

    private void UpdateLyricLine(bool animate)
    {
        if (_lyrics == null || _lyricsLoading) return;
        var pos = CurrentPosition() + TimeSpan.FromMilliseconds(250); // etwas früher wirkt synchroner
        var index = LyricsService.IndexAt(_lyrics, pos);
        if (index == _lyricIndex) return;
        _lyricIndex = index;

        string Line(int i) => i >= 0 && i < _lyrics.Count ? (_lyrics[i].Text.Length > 0 ? _lyrics[i].Text : "♪") : "";
        var now = index < 0 ? "♪" : Line(index);
        ShowLyricText(now, Line(index + 1), animate);
        UpdateCompactTitle();
    }

    private void ShowLyricText(string now, string next, bool animate)
    {
        _view.LyricNow.Text = now;
        _view.LyricNext.Text = next;
        if (!animate) return;
        Anim.To(_view.LyricsSlide, TranslateTransform.YProperty, 0, 420, Anim.Spring, from: 12);
        Anim.To(_view.LyricsStack, UIElement.OpacityProperty, 1, 260, Anim.Smooth, from: 0.2);
    }

    /// <summary>Zugeklappt: Titel · Künstler – oder (wenn gewünscht) die aktuelle Lyrics-Zeile.</summary>
    private void UpdateCompactTitle()
    {
        if (!_hasMedia) { _compactTitle.Text = ""; return; }
        if (Context.Settings.Lyrics && Context.Settings.LyricsKompakt && _lyrics != null && _lyricIndex >= 0)
        {
            var line = _lyrics[Math.Min(_lyricIndex, _lyrics.Count - 1)].Text;
            if (line.Length > 0)
            {
                if (_compactTitle.Text != line)
                {
                    _compactTitle.Text = line;
                    Anim.To(_compactTitleSlide, TranslateTransform.YProperty, 0, 360, Anim.Spring, from: 8);
                }
                return;
            }
        }
        _compactTitle.Text = string.IsNullOrWhiteSpace(_artist) ? _title : $"{_title}  ·  {_artist}";
    }

    // ================================================================== Quelle wählen ("•••")

    private void ToggleSourcePanel()
    {
        if (_sourceOpen) CloseSourcePanel();
        else OpenSourcePanel();
    }

    private void OpenSourcePanel()
    {
        _sourceOpen = true;
        _ = FillSourcePanelAsync();
        _view.SourcePanel.Visibility = Visibility.Visible;
        Anim.To(_view.SourcePanel, UIElement.OpacityProperty, 1, 220, Anim.Smooth, delayMs: 60);
        Anim.To(_view.MainGrid, UIElement.OpacityProperty, 0, 140, Anim.Smooth, () => { if (_sourceOpen) _view.MainGrid.Visibility = Visibility.Hidden; });
        Anim.To(_view.LyricsPanel, UIElement.OpacityProperty, 0, 140, Anim.Smooth);
        UpdateLyricsTimer();
    }

    private void CloseSourcePanel()
    {
        if (!_sourceOpen) return;
        _sourceOpen = false;
        _view.MainGrid.Visibility = Visibility.Visible;
        Anim.To(_view.SourcePanel, UIElement.OpacityProperty, 0, 140, Anim.Smooth, () => { if (!_sourceOpen) _view.SourcePanel.Visibility = Visibility.Collapsed; });
        Anim.To(_view.MainGrid, UIElement.OpacityProperty, 1, 240, Anim.Smooth, delayMs: 60);
        Anim.To(_view.LyricsPanel, UIElement.OpacityProperty, 1, 240, Anim.Smooth, delayMs: 60);
        UpdateLyricsTimer();
    }

    private async Task FillSourcePanelAsync()
    {
        var panel = _view.SourcePanel;
        var root = new StackPanel { VerticalAlignment = VerticalAlignment.Top };

        var head = new Grid { Margin = new Thickness(2, 0, 0, 8) };
        head.Children.Add(new TextBlock { Text = "Was soll die Insel zeigen?", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var done = new Button { Content = "", Style = Ui.Style("IconButton"), Width = 30, Height = 30, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Fertig" };
        done.Click += (_, _) => CloseSourcePanel();
        head.Children.Add(done);
        root.Children.Add(head);

        var chips = new WrapPanel();
        chips.Children.Add(Chip("", "Automatisch", Context.Settings.NurSpotify ? "Spotify zuerst" : "Was gerade läuft", _manualId == null, () => PickSource(null)));

        var sessions = new List<WMC.GlobalSystemMediaTransportControlsSession>();
        try { if (_manager != null) sessions = _manager.GetSessions().ToList(); }
        catch (Exception ex) { App.Log(ex); }

        foreach (var s in sessions.Take(5))
        {
            string sub = "";
            try
            {
                var props = await s.TryGetMediaPropertiesAsync();
                sub = props?.Title ?? "";
            }
            catch { /* App reagiert nicht */ }
            var id = s.SourceAppUserModelId;
            chips.Children.Add(Chip(AppGlyph(id), AppName(id), sub, _manualId != null && _manualId == id, () => PickSource(id)));
        }
        if (sessions.Count == 0)
            chips.Children.Add(new TextBlock { Text = "Gerade spielt keine App etwas ab.", Margin = new Thickness(6, 10, 0, 0) }
                .Themed(TextBlock.ForegroundProperty, "IslandSubText"));
        root.Children.Add(chips);

        // Lyrics schnell an/aus
        var toggles = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        toggles.Children.Add(Chip("", "Lyrics", Context.Settings.Lyrics ? "an" : "aus", Context.Settings.Lyrics, () =>
        {
            Context.Settings.Lyrics = !Context.Settings.Lyrics;
            Context.Settings.NotifyChanged();
            _ = FillSourcePanelAsync();
        }, small: true));
        if (Context.Settings.Lyrics)
        {
            toggles.Children.Add(Chip("", "Lyrics zugeklappt", Context.Settings.LyricsKompakt ? "an" : "aus", Context.Settings.LyricsKompakt, () =>
            {
                Context.Settings.LyricsKompakt = !Context.Settings.LyricsKompakt;
                Context.Settings.NotifyChanged();
                _ = FillSourcePanelAsync();
            }, small: true));
        }
        root.Children.Add(toggles);

        panel.Children.Clear();
        panel.Children.Add(new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        });
    }

    private Button Chip(string glyph, string title, string sub, bool selected, Action click, bool small = false)
    {
        var icon = new TextBlock { Text = glyph, FontFamily = Ui.IconFont, FontSize = small ? 12 : 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        if (selected) icon.Themed(TextBlock.ForegroundProperty, "IslandAccent");
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = title, FontSize = small ? 11.5 : 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (sub.Length > 0)
            texts.Children.Add(new TextBlock { Text = sub, FontSize = 10.5, MaxWidth = small ? 60 : 118, TextTrimming = TextTrimming.CharacterEllipsis }
                .Themed(TextBlock.ForegroundProperty, "IslandSubText"));
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(icon);
        content.Children.Add(texts);

        var b = new Button { Content = content, Style = Ui.Style("ChipButton"), Margin = new Thickness(0, 0, 6, 6), Tag = selected ? "on" : null };
        b.Click += (_, _) => click();
        return b;
    }

    private void PickSource(string? id)
    {
        _manualId = id;
        _ = SelectSessionAsync(force: true);
        CloseSourcePanel();
    }

    // ================================================================== Bedienung

    private void WireUpView()
    {
        _view.PrevBtn.Click += async (_, _) => await Run(s => s.TrySkipPreviousAsync());
        _view.NextBtn.Click += async (_, _) => await Run(s => s.TrySkipNextAsync());
        _view.PlayBtn.Click += async (_, _) => await TogglePlay();
        _mediumPlay.Click += async (_, _) => await TogglePlay();

        // "•••" und Equalizer öffnen die Quellen-Auswahl
        _view.MoreBtn.Click += (_, _) => ToggleSourcePanel();
        _view.EqHost.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _view.EqHost.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleSourcePanel(); };

        // Cover: Klick öffnet die App (und zählt nicht als Ziehen/Doppelklick der Insel)
        _view.CoverBorder.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _view.CoverBorder.MouseLeftButtonUp += (_, _) => OpenSourceApp();

        // Spulen: klicken oder ziehen
        var track = _view.Track;
        track.MouseLeftButtonDown += (_, e) =>
        {
            if (!_canSeek || _duration <= TimeSpan.Zero) return;
            _dragging = true;
            _tick.Stop();
            track.CaptureMouse();
            PreviewSeek(e);
            e.Handled = true;
        };
        track.MouseMove += (_, e) => { if (_dragging) PreviewSeek(e); };
        track.MouseLeftButtonUp += async (_, e) =>
        {
            if (!_dragging) return;
            e.Handled = true;
            var fraction = FractionAt(e);
            _dragging = false;
            track.ReleaseMouseCapture();

            var target = TimeSpan.FromTicks((long)(_duration.Ticks * fraction));
            _position = target;
            _positionStampUtc = DateTime.UtcNow;
            _lyricIndex = -2;
            UpdateTicking();
            UpdateLyricLine(animate: true);

            var session = _session;
            if (session == null) return;
            try { await session.TryChangePlaybackPositionAsync((_start + target).Ticks); }
            catch (Exception ex) { App.Log(ex); }
        };
        track.LostMouseCapture += (_, _) =>
        {
            if (!_dragging) return;
            _dragging = false;
            UpdateTicking();
        };
    }

    private async Task TogglePlay()
    {
        if (_session == null) { OpenSpotify(); return; }
        // Sofort umschalten, damit es sich schnell anfühlt (Windows bestätigt kurz danach)
        _position = CurrentPosition();
        _positionStampUtc = DateTime.UtcNow;
        SetPlaying(!_playing);
        UpdatePlayState();
        UpdateTicking();
        await Run(s => s.TryTogglePlayPauseAsync());
    }

    private void PreviewSeek(MouseEventArgs e)
    {
        _dragFraction = FractionAt(e);
        _progress.InvalidateVisual();
        _view.PosText.Text = Format(TimeSpan.FromTicks((long)(_duration.Ticks * _dragFraction)));
    }

    private double FractionAt(MouseEventArgs e)
    {
        var w = _view.Track.ActualWidth;
        return w <= 0 ? 0 : Math.Clamp(e.GetPosition(_view.Track).X / w, 0, 1);
    }

    private async Task Run(Func<WMC.GlobalSystemMediaTransportControlsSession, global::Windows.Foundation.IAsyncOperation<bool>> action)
    {
        var session = _session;
        if (session == null) { OpenSpotify(); return; }
        try { await action(session); }
        catch (Exception ex) { App.Log(ex); }
    }

    private static void OpenSpotify() => IslandContext.Open("spotify:");

    private void OpenSourceApp()
    {
        var id = _sessionId ?? "";
        if (id.Length == 0 || id.Contains("spotify", StringComparison.OrdinalIgnoreCase)) { OpenSpotify(); return; }
        // Browser & Co.: über die App-Kennung starten/nach vorne holen
        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) IslandContext.Open(id);
        else IslandContext.Open("shell:AppsFolder\\" + id);
    }

    // ================================================================== Zugeklappte + halbe Ansicht

    private void BuildCompactView()
    {
        _compact = new Grid();
        _compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _compactCover = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
        }.Themed(Border.BackgroundProperty, "IslandPlaceholder");

        _compactTitleSlide = new TranslateTransform();
        _compactTitle = new TextBlock
        {
            FontSize = 12.5,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            RenderTransform = _compactTitleSlide,
        };

        _eqCompact = new EqualizerBars(13, 3) { Margin = new Thickness(0, 0, 2, 0) };

        Grid.SetColumn(_compactTitle, 1);
        Grid.SetColumn(_eqCompact, 2);
        _compact.Children.Add(_compactCover);
        _compact.Children.Add(_compactTitle);
        _compact.Children.Add(_eqCompact);
    }

    /// <summary>Halb offen: Cover, Titel, Künstler, Play und ein kleiner Balken.</summary>
    private void BuildMediumView()
    {
        _medium = new Grid { Margin = new Thickness(2, 0, 0, 0) };
        _medium.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _medium.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _medium.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _mediumCover = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Center }
            .Themed(Border.BackgroundProperty, "IslandPlaceholder");

        var texts = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        _mediumTitle = new TextBlock { FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        _mediumArtist = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis }.Themed(TextBlock.ForegroundProperty, "IslandSubText");
        _mediumProgress = new ProgressTrack { Height = 16, Margin = new Thickness(0, 2, 0, 0), Live = true, Source = () => Fraction(CurrentPosition()) };
        texts.Children.Add(_mediumTitle);
        texts.Children.Add(_mediumArtist);
        texts.Children.Add(_mediumProgress);

        _mediumPlay = new Button { Content = PlayGlyph, Style = Ui.Style("IconButton"), Width = 38, Height = 38, FontSize = 17, VerticalAlignment = VerticalAlignment.Center };

        Grid.SetColumn(texts, 1);
        Grid.SetColumn(_mediumPlay, 2);
        _medium.Children.Add(_mediumCover);
        _medium.Children.Add(texts);
        _medium.Children.Add(_mediumPlay);
    }

    // ================================================================== Sichtbar / unsichtbar

    public override void OnShown()
    {
        _shown = true;
        _progress.Live = true;
        RefreshTimeline(); // Position frisch holen
        UpdateTicking();
        _lyricIndex = -2;
        UpdateLyricLine(animate: false);
        UpdateLyricsTimer();
    }

    public override void OnHidden()
    {
        _shown = false;
        _dragging = false;
        _progress.Live = false;
        if (_sourceOpen)
        {
            _sourceOpen = false;
            _view.SourcePanel.Visibility = Visibility.Collapsed;
            _view.SourcePanel.BeginAnimation(UIElement.OpacityProperty, null);
            _view.SourcePanel.Opacity = 0;
            _view.MainGrid.Visibility = Visibility.Visible;
            Anim.Set(_view.MainGrid, UIElement.OpacityProperty, 1);
            Anim.Set(_view.LyricsPanel, UIElement.OpacityProperty, 1);
        }
        UpdateTicking(); // stoppt Timer
        UpdateLyricsTimer();
    }

    public override void Dispose()
    {
        _tick.Stop();
        _idleTimer.Stop();
        _lyricsTimer.Stop();
        _progress.Live = false;
        _mediumProgress.Live = false;
        Unsubscribe();
    }
}

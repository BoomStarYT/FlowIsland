using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Island.Core;
using WMC = global::Windows.Media.Control;
using WStreams = global::Windows.Storage.Streams;

namespace Island.Modules;

/// <summary>
/// Zeigt den Song, der gerade läuft – über die eingebaute Windows-Mediensteuerung.
/// Kein Spotify-Login, kein Premium, keine Internetverbindung nötig.
/// Spart Akku: reagiert nur auf Ereignisse; der Balken bewegt sich nur, wenn der Tab offen ist.
/// </summary>
public sealed class MediaModule : IslandModule
{
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";
    private const string NoteGlyph = "";

    public override string Name => "Musik";
    public override string Icon => NoteGlyph;
    public override int Order => 0;
    public override double CompactWidth => 260;
    public override double ExpandedHeight => 112;
    public override bool WantsCompact => _hasMedia && !PausedLongEnough;

    /// <summary>Länger pausiert als eingestellt → zugeklappt lieber die Ruheanzeige zeigen.</summary>
    private bool PausedLongEnough => !_playing
        && Context.Settings.PauseZuRuheSekunden > 0
        && (DateTime.UtcNow - _pausedAtUtc).TotalSeconds >= Context.Settings.PauseZuRuheSekunden;
    public override FrameworkElement? CompactView => _compact;
    public override FrameworkElement? ExpandedView => _view;

    private MediaView _view = null!;
    private Grid _compact = null!;
    private Border _compactCover = null!;
    private TextBlock _compactTitle = null!;
    private TranslateTransform _compactTitleSlide = null!;
    private EqualizerBars _eqCompact = null!;
    private EqualizerBars _eqExpanded = null!;

    private WMC.GlobalSystemMediaTransportControlsSessionManager? _manager;
    private WMC.GlobalSystemMediaTransportControlsSession? _session;
    private string? _sessionId;
    private bool _lastNurSpotify;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _idleTimer = new();
    private DateTime _pausedAtUtc = DateTime.MinValue;

    private bool _hasMedia, _playing, _canSeek, _dragging, _shown;
    private TimeSpan _start, _position, _duration;
    private DateTime _positionStampUtc = DateTime.UtcNow;
    private ImageBrush? _cover;
    private string? _coverKey;
    private string? _songKey;

    // ================================================================== Start

    protected override void OnInitialize()
    {
        _view = new MediaView();
        _eqExpanded = new EqualizerBars(14, 3);
        _view.EqHost.Content = _eqExpanded;
        BuildCompactView();
        WireUpView();
        _lastNurSpotify = Context.Settings.NurSpotify;
        ApplyEqualizerSetting();

        _tick.Tick += (_, _) => UpdateProgress(animate: true);
        _idleTimer.Tick += (_, _) => { _idleTimer.Stop(); Context.RequestCompactRefresh(); };
        ShowNothingPlaying();
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            _manager = await WMC.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += (_, _) => Post(() => _ = SelectSessionAsync());
            _manager.CurrentSessionChanged += (_, _) => Post(() => _ = SelectSessionAsync());
            await SelectSessionAsync();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    /// <summary>Windows meldet Änderungen auf einem anderen Thread → zurück auf den UI-Thread.</summary>
    private void Post(Action action) => Context.Dispatcher.InvokeAsync(action);

    public override void OnSettingsChanged()
    {
        ApplyEqualizerSetting();
        if (_lastNurSpotify != Context.Settings.NurSpotify)
        {
            _lastNurSpotify = Context.Settings.NurSpotify;
            _ = SelectSessionAsync(force: true);
        }
    }

    private void ApplyEqualizerSetting()
    {
        _eqCompact.Animated = Context.Settings.EqualizerAnimiert;
        _eqExpanded.Animated = Context.Settings.EqualizerAnimiert;
    }

    // ================================================================== Welche App?

    private WMC.GlobalSystemMediaTransportControlsSession? PickSession()
    {
        if (_manager == null) return null;

        foreach (var s in _manager.GetSessions())
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
        _songKey = key;
        _hasMedia = true;

        _view.TitleText.Text = props.Title;
        _view.ArtistText.Text = artist;
        _compactTitle.Text = string.IsNullOrWhiteSpace(artist) ? props.Title : $"{props.Title}  ·  {artist}";
        if (isNewSong) AnimateSongChange();

        // Cover nur neu laden, wenn ein anderer Song läuft
        if (key != _coverKey)
        {
            var brush = await LoadCoverAsync(props.Thumbnail);
            if (!ReferenceEquals(session, _session)) return;
            _cover = brush;
            _coverKey = brush != null ? key : null; // ohne Cover: beim nächsten Ereignis nochmal versuchen
            ApplyCover(animate: true);
        }

        UpdatePlayState();
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
        _hasMedia = false;
        _cover = null;
        _coverKey = null;
        _songKey = null;
        _view.TitleText.Text = "Gerade läuft nichts";
        _view.ArtistText.Text = _session == null ? "Klick aufs Cover, um Spotify zu öffnen" : "Starte einen Song in Spotify";
        _compactTitle.Text = "";
        ApplyCover(animate: false);

        if (!keepTimeline)
        {
            _playing = false;
            _position = TimeSpan.Zero;
            _duration = TimeSpan.Zero;
        }

        UpdatePlayState();
        UpdateTicking();
        Context.RequestCompactRefresh();
    }

    private void ApplyCover(bool animate)
    {
        if (_cover == null)
        {
            _view.CoverBorder.Themed(Border.BackgroundProperty, "IslandPlaceholder");
            _compactCover.Themed(Border.BackgroundProperty, "IslandPlaceholder");
        }
        else
        {
            _view.CoverBorder.Background = _cover;
            _compactCover.Background = _cover;
        }
        _view.CoverIcon.Visibility = _cover == null ? Visibility.Visible : Visibility.Collapsed;

        if (animate && _cover != null)
        {
            Anim.To(_view.CoverBorder, UIElement.OpacityProperty, 1, 320, Anim.Smooth, from: 0.25);
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
    }

    /// <summary>Zeigt klar, ob Musik läuft: Equalizer hüpft vs. liegt flach, Cover wird kleiner und blasser bei Pause.</summary>
    private void UpdatePlayState()
    {
        var playing = _playing && _hasMedia;
        _view.PlayBtn.Content = _playing ? PauseGlyph : PlayGlyph;
        _eqCompact.IsPlaying = playing;
        _eqExpanded.IsPlaying = playing;

        Anim.To(_compactTitle, UIElement.OpacityProperty, playing || !_hasMedia ? 1 : 0.55, 250, Anim.Smooth);
        Anim.To(_compactCover, UIElement.OpacityProperty, playing || !_hasMedia ? 1 : 0.6, 250, Anim.Smooth);

        // Wie beim iPhone: Cover schrumpft leicht, wenn pausiert
        var coverScale = playing || !_hasMedia ? 1.0 : 0.9;
        Anim.To(_view.CoverScale, ScaleTransform.ScaleXProperty, coverScale, 520, Anim.Spring);
        Anim.To(_view.CoverScale, ScaleTransform.ScaleYProperty, coverScale, 520, Anim.Spring);
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

    /// <summary>Balken nur bewegen, wenn man ihn sieht und Musik läuft.</summary>
    private void UpdateTicking()
    {
        var run = _shown && _playing && _hasMedia && !_dragging;
        _tick.Stop();
        if (run) _tick.Start();
        UpdateProgress(animate: run);
    }

    private void UpdateProgress(bool animate)
    {
        if (_dragging) return;

        var now = CurrentPosition();
        _view.PosText.Text = Format(now);
        _view.DurText.Text = _duration > TimeSpan.Zero ? Format(_duration) : "0:00";

        var from = Fraction(now);
        if (animate)
        {
            // Gleitet in 1 Sekunde zur nächsten Position → flüssig, aber nur 1 Update pro Sekunde
            var to = Fraction(now + TimeSpan.FromSeconds(1));
            _view.FillScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(from, to, TimeSpan.FromSeconds(1)));
        }
        else
        {
            Anim.Set(_view.FillScale, ScaleTransform.ScaleXProperty, from);
        }
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

    // ================================================================== Bedienung

    private void WireUpView()
    {
        _view.PrevBtn.Click += async (_, _) => await Run(s => s.TrySkipPreviousAsync());
        _view.NextBtn.Click += async (_, _) => await Run(s => s.TrySkipNextAsync());
        _view.PlayBtn.Click += async (_, _) =>
        {
            if (_session == null) { OpenSpotify(); return; }
            // Sofort umschalten, damit es sich schnell anfühlt (Windows bestätigt kurz danach)
            _position = CurrentPosition();
            _positionStampUtc = DateTime.UtcNow;
            SetPlaying(!_playing);
            UpdatePlayState();
            UpdateTicking();
            await Run(s => s.TryTogglePlayPauseAsync());
        };

        // Cover: Klick öffnet Spotify (und zählt nicht als Ziehen/Doppelklick der Insel)
        _view.CoverBorder.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _view.CoverBorder.MouseLeftButtonUp += (_, _) => OpenSpotify();

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
            UpdateTicking();

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

    private void PreviewSeek(MouseEventArgs e)
    {
        var f = FractionAt(e);
        Anim.Set(_view.FillScale, ScaleTransform.ScaleXProperty, f);
        _view.PosText.Text = Format(TimeSpan.FromTicks((long)(_duration.Ticks * f)));
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

    // ================================================================== Zugeklappte Ansicht

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

    // ================================================================== Sichtbar / unsichtbar

    public override void OnShown()
    {
        _shown = true;
        RefreshTimeline(); // Position frisch holen
        UpdateTicking();
    }

    public override void OnHidden()
    {
        _shown = false;
        _dragging = false;
        UpdateTicking(); // stoppt Timer und Animation
    }

    public override void Dispose()
    {
        _tick.Stop();
        _idleTimer.Stop();
        Unsubscribe();
    }
}

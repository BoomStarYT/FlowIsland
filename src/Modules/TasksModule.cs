using System;
using System.Globalization;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Island.Core;

namespace Island.Modules;

/// <summary>
/// Aufgaben mit Erinnerung. Ist eine Aufgabe fällig, klappt die Insel auf und erinnert dich
/// (mit "Erledigt" und "Später"). Alles bleibt lokal in einstellungen.json.
/// </summary>
public sealed class TasksModule : IslandModule
{
    public override string Name => "Aufgaben";
    public override string Icon => "";
    public override int Order => 15;
    public override double ExpandedHeight => 140;
    public override FrameworkElement? ExpandedView => _root;

    // Zugeklappt: kurz nach einer Erinnerung (wenn keine Musik läuft)
    public override bool WantsCompact => FreshReminder != null;
    public override FrameworkElement? CompactView => _compact;
    public override double CompactWidth => 250;

    private static readonly CultureInfo German = new("de-CH");
    private static readonly SolidColorBrush Overdue = Ui.Brush("#FF453A");

    private Grid _root = null!;
    private StackPanel _list = null!;
    private TextBlock _count = null!;
    private Grid _compact = null!;
    private TextBlock _compactText = null!;
    private readonly DispatcherTimer _check = new() { Interval = TimeSpan.FromSeconds(15) };
    private string _signature = "";

    private AufgabeEintrag? FreshReminder => Context.Settings.Aufgaben
        .Where(a => !a.Erledigt && a.Erinnert && a.Faellig is DateTime d && DateTime.Now - d < TimeSpan.FromMinutes(30))
        .OrderByDescending(a => a.Faellig)
        .FirstOrDefault();

    protected override void OnInitialize()
    {
        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var head = new Grid { Margin = new Thickness(2, -4, 0, 4) };
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "Aufgaben", FontSize = 14, FontWeight = FontWeights.SemiBold });
        _count = new TextBlock { FontSize = 12, Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center }
            .Themed(TextBlock.ForegroundProperty, "IslandSubText");
        title.Children.Add(_count);
        var add = new Button { Content = "", Style = Ui.Style("IconButton"), Width = 28, Height = 28, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Neue Aufgabe" };
        add.Click += (_, _) => TaskEditorWindow.Open(Context.Settings, null);
        head.Children.Add(title);
        head.Children.Add(add);

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

        // Zugeklappt
        _compact = new Grid();
        var bell = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) }
            .Themed(TextBlock.ForegroundProperty, "IslandAccent");
        _compactText = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.Medium, Margin = new Thickness(26, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _compact.Children.Add(bell);
        _compact.Children.Add(_compactText);

        _check.Tick += (_, _) => CheckDue();
        _check.Start();
        Rebuild();
        CheckDue();
    }

    public override void OnSettingsChanged()
    {
        var sig = Signature();
        if (sig != _signature) Rebuild();
        CheckDue();
    }

    private string Signature() => string.Join("\n", Context.Settings.Aufgaben.Select(a => $"{a.Id}|{a.Text}|{a.Faellig:o}|{a.Erledigt}|{a.Erinnert}"))
                                  + DateTime.Now.ToString("yyyyMMddHH");

    // ================================================================== Liste

    private void Rebuild()
    {
        _signature = Signature();
        _list.Children.Clear();
        var open = Context.Settings.Aufgaben.Where(a => !a.Erledigt)
            .OrderBy(a => a.Faellig ?? DateTime.MaxValue).ToList();
        _count.Text = open.Count == 0 ? "" : $"{open.Count} offen";

        foreach (var task in open) _list.Children.Add(Row(task));

        if (open.Count == 0)
        {
            var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            empty.Children.Add(new TextBlock { Text = "Alles erledigt ✨", FontSize = 13.5, HorizontalAlignment = HorizontalAlignment.Center });
            var create = new Button { Content = "Aufgabe hinzufügen", Style = Ui.Style("ChipButton"), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            create.Click += (_, _) => TaskEditorWindow.Open(Context.Settings, null);
            empty.Children.Add(create);
            _list.Children.Add(empty);
        }
        UpdateCompact();
    }

    private FrameworkElement Row(AufgabeEintrag task)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Runder Abhak-Kreis
        var check = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.6),
            Margin = new Thickness(2, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent,
        }.Themed(Border.BorderBrushProperty, "IslandSubText");
        var tick = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 9, Opacity = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        check.Child = tick;

        var text = new TextBlock { Text = task.Text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(text, 1);

        grid.Children.Add(check);
        grid.Children.Add(text);

        if (task.Faellig is DateTime due)
        {
            var late = due <= DateTime.Now;
            var chip = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(8, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock { Text = FormatDue(due), FontSize = 11 };
            if (late) { chip.Background = Overdue; label.Foreground = Brushes.White; }
            else chip.Themed(Border.BackgroundProperty, "IslandChip");
            chip.Child = label;
            Grid.SetColumn(chip, 2);
            grid.Children.Add(chip);
        }

        var row = new Border { Child = grid, CornerRadius = new CornerRadius(9), Padding = new Thickness(4, 5, 4, 5), Background = Brushes.Transparent };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "IslandHover");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

        // Kreis = erledigt, Rest = bearbeiten
        check.MouseLeftButtonDown += (_, e) => e.Handled = true;
        check.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            check.SetResourceReference(Border.BackgroundProperty, "IslandAccent");
            check.SetResourceReference(Border.BorderBrushProperty, "IslandAccent");
            tick.Foreground = Brushes.White;
            Anim.To(tick, UIElement.OpacityProperty, 1, 150, Anim.Smooth);
            text.TextDecorations = TextDecorations.Strikethrough;
            Anim.To(row, UIElement.OpacityProperty, 0, 380, Anim.Smooth, () => Complete(task), delayMs: 450);
        };
        text.MouseLeftButtonDown += (_, e) => e.Handled = true;
        text.MouseLeftButtonUp += (_, _) => TaskEditorWindow.Open(Context.Settings, task);
        return row;
    }

    private void Complete(AufgabeEintrag task)
    {
        task.Erledigt = true;
        // Erledigte nach einem Tag aufräumen
        Context.Settings.Aufgaben.RemoveAll(a => a.Erledigt && a.Faellig is DateTime d && DateTime.Now - d > TimeSpan.FromDays(1));
        Context.Settings.NotifyChanged();
    }

    public static string FormatDue(DateTime due)
    {
        var today = DateTime.Today;
        var time = due.ToString("HH:mm", German);
        if (due.Date == today) return time;
        if (due.Date == today.AddDays(1)) return "Morgen " + time;
        if (due.Date == today.AddDays(-1)) return "Gestern " + time;
        if (due.Date > today && due.Date < today.AddDays(7)) return due.ToString("ddd", German) + " " + time;
        return due.ToString("d. MMM", German) + " " + time;
    }

    // ================================================================== Erinnerung

    private void CheckDue()
    {
        var now = DateTime.Now;
        var due = Context.Settings.Aufgaben.FirstOrDefault(a => !a.Erledigt && !a.Erinnert && a.Faellig is DateTime d && d <= now);
        if (due != null)
        {
            due.Erinnert = true;
            Context.Settings.SaveSoon();
            Remind(due);
            Rebuild();
        }
        else if (Signature() != _signature) Rebuild(); // z. B. neue Stunde → "Morgen" wird "heute"
        UpdateCompact();
    }

    private void UpdateCompact()
    {
        var fresh = FreshReminder;
        _compactText.Text = fresh?.Text ?? "";
        Context.RequestCompactRefresh();
    }

    private void Remind(AufgabeEintrag task)
    {
        if (Context.Settings.AufgabenTon)
        {
            try { SystemSounds.Asterisk.Play(); }
            catch { /* kein Ton */ }
        }

        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bellCircle = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), VerticalAlignment = VerticalAlignment.Center }
            .Themed(Border.BackgroundProperty, "IslandAccent");
        bellCircle.Child = new TextBlock { Text = "", FontFamily = Ui.IconFont, FontSize = 19, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        // Glocke wackelt
        var swing = new RotateTransform(0, 22, 8);
        bellCircle.Child.RenderTransform = swing;
        bellCircle.Child.RenderTransformOrigin = new Point(0.5, 0.2);
        var wobble = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(3) };
        foreach (var (angle, ms) in new[] { (0.0, 0), (16.0, 90), (-14.0, 200), (10.0, 300), (-6.0, 400), (0.0, 520), (0.0, 900) })
            wobble.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(angle, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        swing.BeginAnimation(RotateTransform.AngleProperty, wobble);

        var right = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(new TextBlock { Text = "Erinnerung · " + FormatDue(task.Faellig ?? DateTime.Now), FontSize = 11.5 }.Themed(TextBlock.ForegroundProperty, "IslandSubText"));
        right.Children.Add(new TextBlock { Text = task.Text, FontSize = 15.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 8) });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        Button MakeButton(string label, bool primary, Action click)
        {
            var b = new Button { Content = label, Style = Ui.Style("ChipButton"), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(12, 5, 12, 5), Tag = primary ? "on" : null };
            b.Click += (_, _) => click();
            return b;
        }
        buttons.Children.Add(MakeButton("✓ Erledigt", true, () => { Complete(task); Context.HideAlert(); }));
        buttons.Children.Add(MakeButton("In 10 Min", false, () =>
        {
            task.Faellig = DateTime.Now.AddMinutes(10);
            task.Erinnert = false;
            Context.Settings.NotifyChanged();
            Context.HideAlert();
        }));
        buttons.Children.Add(MakeButton("OK", false, () => Context.HideAlert()));
        right.Children.Add(buttons);

        Grid.SetColumn(right, 1);
        panel.Children.Add(bellCircle);
        panel.Children.Add(right);
        Context.ShowAlert(panel, 92, 20);
    }

    public override void Dispose() => _check.Stop();
}

/// <summary>Kleines Fenster zum Erstellen/Bearbeiten – im gleichen Look wie die Insel.</summary>
public sealed class TaskEditorWindow : Window
{
    private static TaskEditorWindow? _open;
    private static readonly CultureInfo German = new("de-CH");

    private readonly Settings _s;
    private readonly AufgabeEintrag? _task;
    private readonly TextBox _text;
    private readonly TextBox _time;
    private readonly WrapPanel _quick = new();
    private readonly WrapPanel _days = new();
    private readonly StackPanel _custom = new() { Visibility = Visibility.Collapsed };
    private string _choice = "Ohne";
    private int _dayOffset;

    public static void Open(Settings settings, AufgabeEintrag? task)
    {
        _open?.Close();
        _open = new TaskEditorWindow(settings, task);
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    private TaskEditorWindow(Settings settings, AufgabeEintrag? task)
    {
        _s = settings;
        _task = task;
        Title = task == null ? "Neue Aufgabe" : "Aufgabe bearbeiten";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        Width = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;
        FontFamily = new FontFamily($"{settings.Schriftart}, Segoe UI");
        SetResourceReference(ForegroundProperty, "IslandText");

        var stack = new StackPanel { Margin = new Thickness(22, 18, 22, 20) };
        stack.Children.Add(new TextBlock { Text = Title, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });

        _text = Field(task?.Text ?? "", 15);
        stack.Children.Add(Wrap(_text));

        stack.Children.Add(Label("Erinnern"));
        stack.Children.Add(_quick);

        _custom.Children.Add(_days);
        _time = Field(DateTime.Now.AddHours(1).ToString("HH:00", German), 14);
        _time.Width = 90;
        _time.HorizontalContentAlignment = HorizontalAlignment.Center;
        var timeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        timeRow.Children.Add(new TextBlock { Text = "Uhrzeit", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 0) }.Themed(TextBlock.ForegroundProperty, "IslandSubText"));
        timeRow.Children.Add(Wrap(_time));
        _custom.Children.Add(timeRow);
        stack.Children.Add(_custom);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        if (task != null)
        {
            var delete = Btn("Löschen", false, () => { _s.Aufgaben.Remove(task); _s.NotifyChanged(); Close(); });
            delete.Foreground = Ui.Brush("#FF453A");
            buttons.Children.Add(delete);
        }
        buttons.Children.Add(Btn("Abbrechen", false, Close));
        buttons.Children.Add(Btn(task == null ? "Hinzufügen" : "Speichern", true, Save));
        stack.Children.Add(buttons);

        var card = new Border
        {
            CornerRadius = new CornerRadius(24),
            Background = Theme.CurrentFill,
            BorderBrush = Theme.CurrentRim,
            BorderThickness = new Thickness(1.2),
            Child = stack,
            Margin = new Thickness(16),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.94, 0.94),
            Opacity = 0,
        };
        // Weicher Schatten aus zwei Flächen (wie bei der Insel)
        var shadow = new Border { CornerRadius = new CornerRadius(28), Margin = new Thickness(10, 14, 10, 6), Background = Ui.Brush("#30000000") };
        var root = new Grid();
        root.Children.Add(shadow);
        root.Children.Add(card);
        Content = root;
        card.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is Border or StackPanel) DragMove(); };

        // Vorhandene Zeit übernehmen
        if (task?.Faellig is DateTime due)
        {
            _choice = "Eigene";
            _dayOffset = (int)(due.Date - DateTime.Today).TotalDays;
            _time.Text = due.ToString("HH:mm", German);
        }
        BuildQuick();

        Loaded += (_, _) =>
        {
            Top = Math.Max(SystemParameters.WorkArea.Top + 80, Top - 120);
            _text.Focus();
            _text.SelectAll();
            Anim.To(card, OpacityProperty, 1, 200, Anim.Smooth);
            Anim.To((ScaleTransform)card.RenderTransform, ScaleTransform.ScaleXProperty, 1, 420, Anim.Spring);
            Anim.To((ScaleTransform)card.RenderTransform, ScaleTransform.ScaleYProperty, 1, 420, Anim.Spring);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            if (e.Key == Key.Enter) { Save(); e.Handled = true; }
        };
    }

    private void BuildQuick()
    {
        _quick.Children.Clear();
        foreach (var option in new[] { "Ohne", "In 15 Min", "In 1 Std", "Heute 18:00", "Morgen 08:00", "Eigene" })
        {
            var o = option;
            var b = Btn(o == "Eigene" ? "Eigene Zeit …" : o, _choice == o, () => { _choice = o; BuildQuick(); });
            b.Margin = new Thickness(0, 0, 6, 6);
            _quick.Children.Add(b);
        }
        _custom.Visibility = _choice == "Eigene" ? Visibility.Visible : Visibility.Collapsed;

        _days.Children.Clear();
        for (int i = 0; i < 7; i++)
        {
            var offset = i;
            var day = DateTime.Today.AddDays(i);
            var label = i == 0 ? "Heute" : i == 1 ? "Morgen" : day.ToString("ddd d.", German);
            var b = Btn(label, _dayOffset == i, () => { _dayOffset = offset; BuildQuick(); });
            b.Margin = new Thickness(0, 0, 6, 6);
            b.Padding = new Thickness(9, 5, 9, 5);
            _days.Children.Add(b);
        }
    }

    private void Save()
    {
        var text = _text.Text.Trim();
        if (text.Length == 0) { _text.Focus(); return; }

        DateTime? due = _choice switch
        {
            "In 15 Min" => DateTime.Now.AddMinutes(15),
            "In 1 Std" => DateTime.Now.AddHours(1),
            "Heute 18:00" => DateTime.Today.AddHours(18),
            "Morgen 08:00" => DateTime.Today.AddDays(1).AddHours(8),
            "Eigene" => ParseTime(),
            _ => null,
        };
        if (_choice == "Eigene" && due == null) { _time.Focus(); _time.SelectAll(); return; }

        var task = _task ?? new AufgabeEintrag();
        task.Text = text;
        if (task.Faellig != due) task.Erinnert = false;
        task.Faellig = due;
        if (_task == null) _s.Aufgaben.Add(task);
        _s.NotifyChanged();
        Close();
    }

    private DateTime? ParseTime()
    {
        var raw = _time.Text.Trim().Replace('.', ':').Replace(' ', ':');
        if (!raw.Contains(':') && int.TryParse(raw, out var hourOnly)) raw = $"{hourOnly}:00";
        if (!TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var t) || t < TimeSpan.Zero || t >= TimeSpan.FromDays(1)) return null;
        return DateTime.Today.AddDays(_dayOffset) + t;
    }

    private static TextBox Field(string text, double size)
    {
        var box = new TextBox
        {
            Text = text, FontSize = size, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 0, 2, 0), VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.SetResourceReference(ForegroundProperty, "IslandText");
        box.SetResourceReference(TextBoxBase.CaretBrushProperty, "IslandText");
        box.SetResourceReference(TextBoxBase.SelectionBrushProperty, "IslandAccent");
        return box;
    }

    private static Border Wrap(FrameworkElement child) => new Border
    {
        CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8, 10, 8), Child = child, Margin = new Thickness(0, 0, 0, 4),
    }.Themed(Border.BackgroundProperty, "IslandChip");

    private static TextBlock Label(string text) => new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(2, 12, 0, 6) }
        .Themed(TextBlock.ForegroundProperty, "IslandSubText");

    private static Button Btn(string text, bool primary, Action click)
    {
        var b = new Button { Content = text, Style = Ui.Style("ChipButton"), Margin = new Thickness(6, 0, 0, 0), Tag = primary ? "on" : null };
        b.Click += (_, _) => click();
        return b;
    }
}

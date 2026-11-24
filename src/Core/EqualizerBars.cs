using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island.Core;

/// <summary>
/// Kleine Equalizer-Balken: hüpfen, wenn Musik läuft – liegen flach und grau, wenn pausiert.
/// Läuft nur, wenn sichtbar, und mit nur 30 Bildern/s (spart Akku).
/// </summary>
public sealed class EqualizerBars : StackPanel
{
    private static readonly double[] Durations = { 0.42, 0.56, 0.36, 0.49 };
    private static readonly double[] Lows = { 0.30, 0.22, 0.38, 0.26 };
    private static readonly double[] StaticHeights = { 0.55, 0.85, 0.45, 0.70 };
    private const double PausedScale = 0.2;

    private readonly Border[] _bars;
    private readonly ScaleTransform[] _scales;
    private bool _playing;
    private bool _animated = true;
    private bool _running;

    public EqualizerBars(double barHeight = 14, double barWidth = 3)
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Height = barHeight;

        _bars = new Border[Durations.Length];
        _scales = new ScaleTransform[Durations.Length];
        for (int i = 0; i < _bars.Length; i++)
        {
            _scales[i] = new ScaleTransform(1, PausedScale);
            _bars[i] = new Border
            {
                Width = barWidth,
                Height = barHeight,
                CornerRadius = new CornerRadius(barWidth / 2),
                Margin = new Thickness(i == 0 ? 0 : barWidth * 0.6, 0, 0, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _scales[i],
            };
            _bars[i].SetResourceReference(Border.BackgroundProperty, "IslandSubText");
            Children.Add(_bars[i]);
        }

        IsVisibleChanged += (_, _) => Update();
    }

    /// <summary>false = Balken stehen still (noch sparsamer).</summary>
    public bool Animated
    {
        get => _animated;
        set { if (_animated == value) return; _animated = value; _running = false; Update(); }
    }

    public bool IsPlaying
    {
        get => _playing;
        set { if (_playing == value) return; _playing = value; _running = false; Update(); }
    }

    private void Update()
    {
        var shouldRun = _playing && _animated && IsVisible;
        if (shouldRun == _running && shouldRun) return;
        _running = shouldRun;

        for (int i = 0; i < _bars.Length; i++)
        {
            _bars[i].SetResourceReference(Border.BackgroundProperty, _playing ? "IslandAccent" : "IslandSubText");

            if (shouldRun)
            {
                var a = new DoubleAnimation(Lows[i], 1.0, TimeSpan.FromSeconds(Durations[i]))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(a, 30);
                _scales[i].BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            else
            {
                // Sanft in die Ruheposition gleiten (oder sofort, wenn unsichtbar)
                var target = _playing ? StaticHeights[i] : PausedScale;
                if (IsVisible)
                {
                    var a = new DoubleAnimation(target, TimeSpan.FromMilliseconds(260))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    };
                    _scales[i].BeginAnimation(ScaleTransform.ScaleYProperty, a);
                }
                else
                {
                    _scales[i].BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    _scales[i].ScaleY = target;
                }
            }
        }
    }
}

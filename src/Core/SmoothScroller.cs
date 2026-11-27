using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Island.Core;

/// <summary>Weiches Scrollen für Listen: Mausrad setzt ein Ziel, die Ansicht gleitet hin (läuft nur während der Bewegung).</summary>
public sealed class SmoothScroller
{
    private readonly ScrollViewer _sv;
    private readonly double _factor;
    private double _target;
    private bool _running;

    private SmoothScroller(ScrollViewer sv, double factor)
    {
        _sv = sv;
        _factor = factor;
        sv.PreviewMouseWheel += OnWheel;
    }

    public static SmoothScroller Attach(ScrollViewer sv, double factor = 0.35) => new(sv, factor);

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_sv.ScrollableHeight <= 0) return;
        e.Handled = true;
        if (!_running) _target = _sv.VerticalOffset;
        _target = Math.Clamp(_target - e.Delta * _factor, 0, _sv.ScrollableHeight);
        if (_running) return;
        _running = true;
        CompositionTarget.Rendering += Step;
    }

    private void Step(object? sender, EventArgs e)
    {
        var current = _sv.VerticalOffset;
        var diff = _target - current;
        if (Math.Abs(diff) < 0.6 || !_sv.IsVisible)
        {
            _sv.ScrollToVerticalOffset(_target);
            CompositionTarget.Rendering -= Step;
            _running = false;
            return;
        }
        _sv.ScrollToVerticalOffset(current + diff * 0.2);
    }
}

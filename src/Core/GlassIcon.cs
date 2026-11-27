using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Island.Core;

/// <summary>
/// App-Symbol als Glas-Kachel (wie iOS/Liquid Glass): alle gleich gross, runde Ecken,
/// Glanz oben und feine Lichtkante. Symbole mit eigener Fläche füllen die ganze Kachel,
/// durchsichtige Symbole sitzen mittig auf mattem Glas.
/// </summary>
public sealed class GlassIcon : Grid
{
    private static readonly Brush GlassFill = Freeze(new LinearGradientBrush(Color.FromArgb(70, 255, 255, 255), Color.FromArgb(26, 255, 255, 255), 90));
    private static readonly Brush Sheen = Freeze(new LinearGradientBrush(new GradientStopCollection
    {
        new GradientStop(Color.FromArgb(90, 255, 255, 255), 0),
        new GradientStop(Color.FromArgb(18, 255, 255, 255), 0.45),
        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.55),
        new GradientStop(Color.FromArgb(20, 255, 255, 255), 1),
    }, 90));
    private static readonly Brush Rim = Freeze(new LinearGradientBrush(new GradientStopCollection
    {
        new GradientStop(Color.FromArgb(190, 255, 255, 255), 0),
        new GradientStop(Color.FromArgb(40, 255, 255, 255), 0.5),
        new GradientStop(Color.FromArgb(90, 255, 255, 255), 1),
    }, 90));

    private readonly Border _plate;
    private readonly Image _image;
    private readonly TextBlock _letter;
    private readonly double _size;

    public GlassIcon(double size, string letter)
    {
        _size = size;
        Width = size;
        Height = size;
        var r = size * 0.27;
        Clip = new RectangleGeometry(new Rect(0, 0, size, size), r, r);

        _plate = new Border { Background = GlassFill };
        _letter = new TextBlock
        {
            Text = letter, FontWeight = FontWeights.SemiBold, FontSize = size * 0.42,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        _image = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        Children.Add(_plate);
        Children.Add(_letter);
        Children.Add(_image);
        Children.Add(new Border { Background = Sheen, IsHitTestVisible = false });
        Children.Add(new Border { BorderBrush = Rim, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(r), IsHitTestVisible = false });
    }

    public ImageSource? Source
    {
        get => _image.Source;
        set
        {
            _image.Source = value;
            _letter.Visibility = value == null ? Visibility.Visible : Visibility.Collapsed;
            // Volle Fläche → ganze Kachel; sonst mit Rand auf Glas
            var fill = AppIcons.IsFullBleed(value);
            _image.Margin = new Thickness(fill ? 0 : _size * 0.17);
            _image.Stretch = fill ? Stretch.UniformToFill : Stretch.Uniform;
        }
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}

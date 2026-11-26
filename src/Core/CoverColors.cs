using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Island.Core;

/// <summary>Sucht die schönste kräftige Farbe in einem Cover (wie Apple Music / Spotify).</summary>
public static class CoverColors
{
    public static Color? Vibrant(BitmapSource? source)
    {
        if (source == null) return null;
        try
        {
            // Auf 24×24 verkleinern → schnell, und kleine Details zählen nicht
            var scale = 24.0 / Math.Max(source.PixelWidth, source.PixelHeight);
            BitmapSource small = new TransformedBitmap(source, new ScaleTransform(Math.Min(1, scale), Math.Min(1, scale)));
            small = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int w = small.PixelWidth, h = small.PixelHeight;
            var px = new byte[w * h * 4];
            small.CopyPixels(px, w * 4, 0);

            // 18 Farbton-Fächer, gewichtet nach Sättigung und mittlerer Helligkeit
            const int bins = 18;
            var weight = new double[bins];
            var sumR = new double[bins];
            var sumG = new double[bins];
            var sumB = new double[bins];
            double grayWeight = 0, gr = 0, gg = 0, gb = 0;

            for (int i = 0; i < px.Length; i += 4)
            {
                var c = Color.FromRgb(px[i + 2], px[i + 1], px[i]);
                var (hue, sat, light) = Theme.ToHsl(c);
                var lightScore = 1 - Math.Abs(light - 0.55) * 1.8;
                if (lightScore <= 0) continue;
                if (sat < 0.18)
                {
                    grayWeight += lightScore;
                    gr += c.R; gg += c.G; gb += c.B;
                    continue;
                }
                var wgt = sat * sat * lightScore;
                var bin = (int)(hue / 360 * bins) % bins;
                weight[bin] += wgt;
                sumR[bin] += c.R * wgt;
                sumG[bin] += c.G * wgt;
                sumB[bin] += c.B * wgt;
            }

            var best = -1;
            for (int b = 0; b < bins; b++)
                if (weight[b] > 0.35 && (best < 0 || weight[b] > weight[best])) best = b;

            Color pick;
            if (best >= 0)
                pick = Color.FromRgb((byte)(sumR[best] / weight[best]), (byte)(sumG[best] / weight[best]), (byte)(sumB[best] / weight[best]));
            else if (grayWeight > 0)
                return Color.FromRgb(235, 235, 240); // Schwarz-Weiss-Cover → fast weiss
            else
                return null;

            // Auf dunklem Hintergrund gut sichtbar machen
            var (hh, ss, ll) = Theme.ToHsl(pick);
            return Theme.FromHsl(hh, Math.Clamp(ss * 1.15, 0.45, 0.95), Math.Clamp(ll, 0.56, 0.70));
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return null;
        }
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EmuDOS.Controls;

/// <summary>
/// A Roland MT-32 LCD, rendered the way Boxer did it: a 20-character grid of 5x7 dots, lit dots
/// over a faint unlit grid, in the hardware's warm gold. We don't have Boxer's GPL glyph bitmap,
/// so each character is rasterized from a monospace font into the 5x7 dot grid at draw time.
/// </summary>
public sealed class Mt32LcdControl : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(Mt32LcdControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private const int CharCount = 20, Cols = 5, Rows = 7, Super = 4;

    // Boxer's warm gold lit pixels (HSB .144/.737/.923); unlit dots are a faint version so the
    // dot grid reads over the photo's (dark) LCD glass. No screen fill — the photo IS the glass.
    private static readonly Brush LitBrush = Frozen(Color.FromRgb(0xEB, 0xD4, 0x3E));
    private static readonly Brush UnlitBrush = Frozen(Color.FromArgb(0x1E, 0xEB, 0xD4, 0x3E));

    private static readonly Typeface Face =
        new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private static readonly Dictionary<char, bool[,]> GlyphCache = new();

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        // Size the dots to fit all 20 characters across the width (one-dot gutter between chars),
        // then centre the single line of text vertically over the photo's LCD glass.
        double dotPitch = w / (CharCount * Cols + (CharCount - 1));
        double charGap = dotPitch;
        double dotRadius = dotPitch * 0.45;
        double gridH = Rows * dotPitch;
        double y0 = (h - gridH) / 2;

        var raw = Text ?? string.Empty;
        var text = raw.Length > CharCount ? raw[..CharCount] : raw.PadRight(CharCount);

        for (int ci = 0; ci < CharCount; ci++)
        {
            var glyph = GlyphFor(text[ci]);
            double cx = ci * (Cols * dotPitch + charGap);
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Cols; col++)
                {
                    var center = new Point(cx + col * dotPitch + dotPitch / 2, y0 + row * dotPitch + dotPitch / 2);
                    dc.DrawEllipse(glyph[row, col] ? LitBrush : UnlitBrush, null, center, dotRadius, dotRadius);
                }
            }
        }
    }

    private static bool[,] GlyphFor(char c)
    {
        if (GlyphCache.TryGetValue(c, out var cached))
            return cached;
        var grid = Rasterize(c);
        GlyphCache[c] = grid;
        return grid;
    }

    // Rasterize one character into a 5x7 on/off dot grid by rendering it (supersampled) and
    // sampling each dot cell — gives recognizable glyphs for any character, no hand-coded font.
    private static bool[,] Rasterize(char c)
    {
        var grid = new bool[Rows, Cols];
        if (c <= ' ')
            return grid;

        int wpx = Cols * Super, hpx = Rows * Super;
        var visual = new DrawingVisual();
        using (var dcx = visual.RenderOpen())
        {
            var ft = new FormattedText(c.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Face, hpx * 0.95, Brushes.White, 1.0);
            dcx.DrawText(ft, new Point((wpx - ft.Width) / 2, (hpx - ft.Height) / 2));
        }

        var rtb = new RenderTargetBitmap(wpx, hpx, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var pixels = new byte[wpx * hpx * 4];
        rtb.CopyPixels(pixels, wpx * 4, 0);

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Cols; col++)
            {
                int on = 0;
                for (int sy = 0; sy < Super; sy++)
                {
                    for (int sx = 0; sx < Super; sx++)
                    {
                        int idx = ((row * Super + sy) * wpx + (col * Super + sx)) * 4;
                        if (pixels[idx + 3] > 64 && pixels[idx + 2] > 96) // alpha + brightness
                            on++;
                    }
                }
                grid[row, col] = on * 2 >= Super * Super; // majority of the cell lit
            }
        }

        return grid;
    }
}

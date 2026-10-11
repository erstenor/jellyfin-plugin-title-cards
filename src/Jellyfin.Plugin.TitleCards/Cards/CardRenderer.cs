using System.Collections.Concurrent;
using System.Text;
using SkiaSharp;

namespace Jellyfin.Plugin.TitleCards.Cards;

/// <summary>
/// Draws title cards the way Jellyfin draws its generated library images.
/// </summary>
/// <remarks>
/// Jellyfin's library images are one backdrop under a black layer at alpha 0x78, with the
/// library name in the bold "sans-serif" face at 112px, centred, its baseline at half height
/// plus half the x-height, and shrunk to 90% of the width when wider than 95%. "sans-serif" is
/// whatever the system's font configuration maps it to (DejaVu Sans in Jellyfin's Docker images).
/// Characters that font lacks, such as Japanese with only DejaVu, are drawn in another installed
/// font that has them, as a browser would.
/// </remarks>
public static class CardRenderer
{
    /// <summary>Opacity of the black layer over the artwork.</summary>
    public const byte DarkenAlpha = 0x78;

    /// <summary>Title text size before any shrinking.</summary>
    public const float TitleSize = 112;

    /// <summary>
    /// Images whose shape differs from the tile's by more than this ratio are shown whole on a
    /// blurred fill instead of being cropped.
    /// </summary>
    public const float MaxCropRatio = 1.25f;

    private static readonly SKFontStyle TitleStyle = new(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

    private static readonly Lazy<SKTypeface> TitleTypeface = new(
        () => SKTypeface.FromFamilyName("sans-serif", TitleStyle) ?? SKTypeface.Default);

    // Fallback typeface per character, or null when no installed font has it.
    private static readonly ConcurrentDictionary<int, SKTypeface?> Fallbacks = new();

    /// <summary>
    /// Fits an image into a tile: cropped to fill when the shapes are close (or always, with
    /// <paramref name="cropToFill"/>), otherwise shown whole over a blurred, darkened copy of
    /// itself (square album art, portrait covers).
    /// </summary>
    /// <param name="image">The source image.</param>
    /// <param name="size">The tile size.</param>
    /// <param name="cropToFill">Crop every image to fill the tile, whatever its shape.</param>
    /// <returns>A new bitmap of exactly the tile size.</returns>
    public static SKBitmap FitTile(SKBitmap image, (int Width, int Height) size, bool cropToFill = false)
    {
        var tile = new SKBitmap(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(tile);
        var shapeRatio = ((float)image.Width / image.Height) / ((float)size.Width / size.Height);
        var target = new SKRect(0, 0, size.Width, size.Height);
        using var sampling = new SKPaint { IsAntialias = true };
        var options = new SKSamplingOptions(SKCubicResampler.Mitchell);

        if (cropToFill || (shapeRatio >= 1 / MaxCropRatio && shapeRatio <= MaxCropRatio))
        {
            DrawCropped(canvas, image, target, options);
            return tile;
        }

        using (var blur = SKImageFilter.CreateBlur(size.Width / 24f, size.Width / 24f))
        using (var darken = SKColorFilter.CreateBlendMode(new SKColor(0, 0, 0, 115), SKBlendMode.SrcOver))
        using (var fill = new SKPaint { ImageFilter = blur, ColorFilter = darken })
        {
            canvas.SaveLayer(fill);
            DrawCropped(canvas, image, target, options);
            canvas.Restore();
        }

        var scale = Math.Min((float)size.Width / image.Width, (float)size.Height / image.Height);
        var width = image.Width * scale;
        var height = image.Height * scale;
        var whole = SKRect.Create((size.Width - width) / 2, (size.Height - height) / 2, width, height);
        using var source = SKImage.FromBitmap(image);
        canvas.DrawImage(source, whole, options);
        return tile;
    }

    /// <summary>
    /// Characters in the title that no installed font can draw; they come out as boxes.
    /// </summary>
    /// <param name="title">The title text.</param>
    /// <returns>The missing characters, once each.</returns>
    public static IReadOnlyList<string> MissingCharacters(string title)
        => [.. SplitRuns(title).Where(run => run.Missing).SelectMany(run => run.Text.EnumerateRunes()).Select(rune => rune.ToString()).Distinct()];

    /// <summary>
    /// Where the title's ink sits on the card, grown by the clear-space margin.
    /// </summary>
    /// <param name="title">The title text.</param>
    /// <returns>The band other text must stay out of.</returns>
    public static Box TitleBand(string title)
    {
        using var layout = new TitleLayout(title);
        var ink = SKRect.Empty;
        foreach (var (font, text, x) in layout.Runs)
        {
            font.MeasureText(text, out var bounds);
            if (bounds.IsEmpty)
            {
                continue;
            }

            bounds.Offset(x, layout.Baseline);
            ink = ink.IsEmpty ? bounds : SKRect.Union(ink, bounds);
        }

        return new Box(ink.Left, ink.Top, ink.Right, ink.Bottom).Inflate(CardLayout.TitleMargin);
    }

    /// <summary>
    /// Draws the card: tiles, the darkening layer, then the title.
    /// </summary>
    /// <param name="tiles">Tile images and where they go.</param>
    /// <param name="title">The view name.</param>
    /// <returns>The finished card.</returns>
    public static SKBitmap Compose(IEnumerable<(SKBitmap Image, Box Tile)> tiles, string title)
    {
        var card = new SKBitmap(CardLayout.Width, CardLayout.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(card);
        canvas.Clear(SKColors.Black);
        foreach (var (image, tile) in tiles)
        {
            canvas.DrawBitmap(image, tile.Left, tile.Top);
        }

        using (var darken = new SKPaint { Color = SKColors.Black.WithAlpha(DarkenAlpha), Style = SKPaintStyle.Fill })
        {
            canvas.DrawRect(0, 0, CardLayout.Width, CardLayout.Height, darken);
        }

        using var layout = new TitleLayout(title);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
        foreach (var (font, text, x) in layout.Runs)
        {
            canvas.DrawText(text, x, layout.Baseline, SKTextAlign.Left, font, paint);
        }

        return card;
    }

    /// <summary>
    /// Encodes a bitmap as PNG.
    /// </summary>
    /// <param name="bitmap">The bitmap.</param>
    /// <returns>PNG bytes.</returns>
    public static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Splits the title into runs drawn with one typeface each: the title font where it has the
    /// character, otherwise an installed font that does.
    /// </summary>
    private static List<(string Text, SKTypeface Typeface, bool Missing)> SplitRuns(string title)
    {
        var primary = TitleTypeface.Value;
        var runs = new List<(string Text, SKTypeface Typeface, bool Missing)>();
        var text = new StringBuilder();
        (SKTypeface Typeface, bool Missing)? current = null;
        foreach (var rune in title.EnumerateRunes())
        {
            (SKTypeface Typeface, bool Missing) face;
            if (primary.ContainsGlyph(rune.Value))
            {
                face = (primary, false);
            }
            else if (current is { Missing: false } previous && previous.Typeface.ContainsGlyph(rune.Value))
            {
                face = previous;
            }
            else
            {
                var fallback = Fallbacks.GetOrAdd(rune.Value, value => SKFontManager.Default.MatchCharacter("sans-serif", TitleStyle, null, value));
                face = fallback is not null && fallback.ContainsGlyph(rune.Value) ? (fallback, false) : (primary, true);
            }

            if (current is { } open && (open.Typeface != face.Typeface || open.Missing != face.Missing))
            {
                runs.Add((text.ToString(), open.Typeface, open.Missing));
                text.Clear();
            }

            current = face;
            text.Append(rune.ToString());
        }

        if (current is { } last)
        {
            runs.Add((text.ToString(), last.Typeface, last.Missing));
        }

        return runs;
    }

    private static void DrawCropped(SKCanvas canvas, SKBitmap image, SKRect target, SKSamplingOptions options)
    {
        // Centre crop to the target's shape, like ImageOps.fit / Jellyfin's thumbnail crop.
        var targetRatio = target.Width / target.Height;
        var sourceRatio = (float)image.Width / image.Height;
        SKRect source;
        if (sourceRatio > targetRatio)
        {
            var width = image.Height * targetRatio;
            source = SKRect.Create((image.Width - width) / 2, 0, width, image.Height);
        }
        else
        {
            var height = image.Width / targetRatio;
            source = SKRect.Create(0, (image.Height - height) / 2, image.Width, height);
        }

        using var skImage = SKImage.FromBitmap(image);
        canvas.DrawImage(skImage, source, target, options);
    }

    /// <summary>
    /// The title's runs, sized and placed the way Jellyfin places library names: long names are
    /// shrunk as a whole, and the baseline comes from the title font's x-height.
    /// </summary>
    private sealed class TitleLayout : IDisposable
    {
        public TitleLayout(string title)
        {
            var runs = SplitRuns(title);
            var size = TitleSize;
            var advance = Measure(runs, size);
            if (advance > CardLayout.Width * 0.95f)
            {
                size = 0.9f * CardLayout.Width * size / advance;
                advance = Measure(runs, size);
            }

            var x = (CardLayout.Width - advance) / 2;
            foreach (var (text, typeface, _) in runs)
            {
                var font = NewFont(typeface, size);
                Runs.Add((font, text, x));
                x += font.MeasureText(text);
            }

            using var primary = NewFont(TitleTypeface.Value, size);
            Baseline = (CardLayout.Height / 2f) + (primary.Metrics.XHeight / 2);
        }

        public List<(SKFont Font, string Text, float X)> Runs { get; } = [];

        public float Baseline { get; }

        public void Dispose()
        {
            foreach (var (font, _, _) in Runs)
            {
                font.Dispose();
            }
        }

        private static SKFont NewFont(SKTypeface typeface, float size)
            => new(typeface, size) { Edging = SKFontEdging.Antialias, Subpixel = true };

        private static float Measure(List<(string Text, SKTypeface Typeface, bool Missing)> runs, float size)
        {
            var total = 0f;
            foreach (var (text, typeface, _) in runs)
            {
                using var font = NewFont(typeface, size);
                total += font.MeasureText(text);
            }

            return total;
        }
    }
}

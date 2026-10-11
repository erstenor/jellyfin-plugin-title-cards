using SkiaSharp;

namespace Jellyfin.Plugin.TitleCards.Cards;

/// <summary>
/// Draws title cards the way Jellyfin draws its generated library images.
/// </summary>
/// <remarks>
/// Jellyfin's library images are one backdrop under a black layer at alpha 0x78, with the
/// library name in the bold "sans-serif" face at 112px, centred, its baseline at half height
/// plus half the x-height, and shrunk to 90% of the width when wider than 95%.
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

    private static SKTypeface TitleTypeface
        => SKTypeface.FromFamilyName("sans-serif", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
           ?? SKTypeface.Default;

    /// <summary>
    /// Fits an image into a tile: cropped to fill when the shapes are close, otherwise shown whole
    /// over a blurred, darkened copy of itself (square album art, portrait covers).
    /// </summary>
    /// <param name="image">The source image.</param>
    /// <param name="size">The tile size.</param>
    /// <returns>A new bitmap of exactly the tile size.</returns>
    public static SKBitmap FitTile(SKBitmap image, (int Width, int Height) size)
    {
        var tile = new SKBitmap(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(tile);
        var shapeRatio = ((float)image.Width / image.Height) / ((float)size.Width / size.Height);
        var target = new SKRect(0, 0, size.Width, size.Height);
        using var sampling = new SKPaint { IsAntialias = true };
        var options = new SKSamplingOptions(SKCubicResampler.Mitchell);

        if (shapeRatio >= 1 / MaxCropRatio && shapeRatio <= MaxCropRatio)
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
    /// The title's font, shrunk the way Jellyfin shrinks long library names.
    /// </summary>
    /// <param name="title">The title text.</param>
    /// <returns>The font to draw it with.</returns>
    public static SKFont TitleFont(string title)
    {
        var font = new SKFont(TitleTypeface, TitleSize) { Edging = SKFontEdging.Antialias, Subpixel = true };
        var textWidth = font.MeasureText(title);
        if (textWidth > CardLayout.Width * 0.95f)
        {
            font.Size = 0.9f * CardLayout.Width * font.Size / textWidth;
        }

        return font;
    }

    /// <summary>
    /// Where the title's ink sits on the card, grown by the clear-space margin.
    /// </summary>
    /// <param name="title">The title text.</param>
    /// <returns>The band other text must stay out of.</returns>
    public static Box TitleBand(string title)
    {
        using var font = TitleFont(title);
        var advance = font.MeasureText(title, out var bounds);
        var (x, y) = TitleOrigin(font, advance);
        return new Box(x + bounds.Left, y + bounds.Top, x + bounds.Right, y + bounds.Bottom).Inflate(CardLayout.TitleMargin);
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

        using var font = TitleFont(title);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
        var advance = font.MeasureText(title);
        var (x, y) = TitleOrigin(font, advance);
        canvas.DrawText(title, x, y, SKTextAlign.Left, font, paint);
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

    private static (float X, float Y) TitleOrigin(SKFont font, float advance)
        => ((CardLayout.Width - advance) / 2, (CardLayout.Height / 2f) + (font.Metrics.XHeight / 2));

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
}

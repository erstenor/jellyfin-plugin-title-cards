using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.TitleCards.Cards;

/// <summary>
/// Loads, fits and text-checks candidate images during one run, caching each step.
/// </summary>
public sealed class CardImages : IDisposable
{
    /// <summary>Tiles are scaled up to at least this width before text detection, so small lettering is found.</summary>
    public const int DetectionMinWidth = 960;

    private const int MaxSourceWidth = 1920;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TextDetector? _detector;
    private readonly ILogger _logger;
    private readonly Dictionary<CardCandidate, SKBitmap?> _sources = [];
    private readonly Dictionary<(CardCandidate, int, int, bool), SKBitmap> _tiles = [];
    private readonly Dictionary<(CardCandidate, int, int, bool), IReadOnlyList<Box>?> _text = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="CardImages"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory, for remote guide images.</param>
    /// <param name="detector">Text detector, or null to skip the text filter.</param>
    /// <param name="logger">Logger.</param>
    public CardImages(IHttpClientFactory httpClientFactory, TextDetector? detector, ILogger logger)
    {
        _httpClientFactory = httpClientFactory;
        _detector = detector is { IsAvailable: true } ? detector : null;
        _logger = logger;
    }

    /// <summary>
    /// Text in a candidate drawn into a tile of the given size, in tile coordinates.
    /// </summary>
    /// <param name="candidate">The candidate.</param>
    /// <param name="size">Tile size.</param>
    /// <param name="cropToFill">Crop square and portrait art to fill the tile.</param>
    /// <returns>Text boxes; null when the image cannot be used.</returns>
    public IReadOnlyList<Box>? TextBoxes(CardCandidate candidate, (int Width, int Height) size, bool cropToFill)
    {
        if (Source(candidate) is not { } source)
        {
            return null;
        }

        var crop = cropToFill && CardRenderer.CropChangesFit((source.Width, source.Height), size);
        var key = (candidate, size.Width, size.Height, crop);
        if (_text.TryGetValue(key, out var cached))
        {
            return cached;
        }

        IReadOnlyList<Box>? boxes = [];
        if (_detector is not null)
        {
            var scale = Math.Max(1f, (float)DetectionMinWidth / size.Width);
            var scaled = ((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale));
            var tile = Tile(candidate, scaled, crop);
            boxes = tile is null
                ? null
                : _detector.Detect(tile).Select(box => new Box(box.Left / scale, box.Top / scale, box.Right / scale, box.Bottom / scale)).ToList();
        }

        _text[key] = boxes;
        return boxes;
    }

    /// <summary>
    /// Text in a candidate drawn both ways (shown whole and cropped to fill), so a cover is only
    /// used where its lettering clears the title in both versions of the card.
    /// </summary>
    /// <param name="candidate">The candidate.</param>
    /// <param name="size">Tile size.</param>
    /// <returns>Text boxes from both fits; null when the image cannot be used.</returns>
    public IReadOnlyList<Box>? TextBoxesBothFits(CardCandidate candidate, (int Width, int Height) size)
    {
        var whole = TextBoxes(candidate, size, cropToFill: false);
        if (whole is null || !CropChangesFit(candidate, size))
        {
            return whole;
        }

        return TextBoxes(candidate, size, cropToFill: true) is { } cropped ? [.. whole, .. cropped] : null;
    }

    /// <summary>
    /// Whether cropping to fill draws this candidate differently in a tile of the given size.
    /// </summary>
    /// <param name="candidate">The candidate.</param>
    /// <param name="size">Tile size.</param>
    /// <returns>True for covers shown whole on a blurred copy unless cropped.</returns>
    public bool CropChangesFit(CardCandidate candidate, (int Width, int Height) size)
        => Source(candidate) is { } source && CardRenderer.CropChangesFit((source.Width, source.Height), size);

    /// <summary>
    /// A candidate fitted to a tile size.
    /// </summary>
    /// <param name="candidate">The candidate.</param>
    /// <param name="size">Tile size.</param>
    /// <param name="cropToFill">Crop square and portrait art to fill the tile.</param>
    /// <returns>The tile image; null when the image cannot be used.</returns>
    public SKBitmap? Tile(CardCandidate candidate, (int Width, int Height) size, bool cropToFill)
    {
        var source = Source(candidate);
        if (source is null)
        {
            return null;
        }

        // Shapes close to the tile's are cropped either way, so both fits share one tile.
        var crop = cropToFill && CardRenderer.CropChangesFit((source.Width, source.Height), size);
        var key = (candidate, size.Width, size.Height, crop);
        if (_tiles.TryGetValue(key, out var tile))
        {
            return tile;
        }

        tile = CardRenderer.FitTile(source, size, crop);
        _tiles[key] = tile;
        return tile;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var bitmap in _tiles.Values)
        {
            bitmap.Dispose();
        }

        foreach (var bitmap in _sources.Values)
        {
            bitmap?.Dispose();
        }
    }

    private SKBitmap? Source(CardCandidate candidate)
    {
        if (_sources.TryGetValue(candidate, out var source))
        {
            return source;
        }

        source = null;
        try
        {
            var bytes = candidate.ImagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? _httpClientFactory.CreateClient(NamedClient.Default).GetByteArrayAsync(new Uri(candidate.ImagePath)).GetAwaiter().GetResult()
                : File.ReadAllBytes(candidate.ImagePath);
            var decoded = SKBitmap.Decode(bytes);
            if (decoded is not null && (!candidate.RequireLandscape || (double)decoded.Width / decoded.Height > 1.3))
            {
                source = decoded.Width > MaxSourceWidth ? Shrink(decoded) : decoded;
            }
            else
            {
                decoded?.Dispose();
            }
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException or TaskCanceledException or UriFormatException)
        {
            _logger.LogDebug(ex, "Title Cards: could not load {Path}", candidate.ImagePath);
        }

        _sources[candidate] = source;
        return source;
    }

    private static SKBitmap Shrink(SKBitmap bitmap)
    {
        var height = (int)Math.Round((double)bitmap.Height * MaxSourceWidth / bitmap.Width);
        var smaller = bitmap.Resize(new SKImageInfo(MaxSourceWidth, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (smaller is null)
        {
            return bitmap;
        }

        bitmap.Dispose();
        return smaller;
    }
}

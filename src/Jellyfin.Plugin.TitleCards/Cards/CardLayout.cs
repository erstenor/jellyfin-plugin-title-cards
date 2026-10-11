namespace Jellyfin.Plugin.TitleCards.Cards;

/// <summary>
/// An axis-aligned rectangle in pixels.
/// </summary>
/// <param name="Left">Left edge.</param>
/// <param name="Top">Top edge.</param>
/// <param name="Right">Right edge.</param>
/// <param name="Bottom">Bottom edge.</param>
public readonly record struct Box(float Left, float Top, float Right, float Bottom)
{
    /// <summary>Gets the width.</summary>
    public float Width => Right - Left;

    /// <summary>Gets the height.</summary>
    public float Height => Bottom - Top;

    /// <summary>Gets the size in whole pixels.</summary>
    public (int Width, int Height) Size => ((int)Width, (int)Height);

    /// <summary>Whether two boxes share any area.</summary>
    /// <param name="other">The other box.</param>
    /// <returns>True when they overlap.</returns>
    public bool Overlaps(Box other)
        => Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

    /// <summary>Grows the box by the same amount on every side.</summary>
    /// <param name="amount">Pixels to add on each side.</param>
    /// <returns>The grown box.</returns>
    public Box Inflate(float amount) => new(Left - amount, Top - amount, Right + amount, Bottom + amount);
}

/// <summary>
/// Card size and tile arrangements, and where the title keeps other text out.
/// </summary>
public static class CardLayout
{
    /// <summary>Card width, matching Jellyfin's generated library images.</summary>
    public const int Width = 960;

    /// <summary>Card height.</summary>
    public const int Height = 540;

    /// <summary>Clear space kept around the title, in card pixels.</summary>
    public const float TitleMargin = 24;

    /// <summary>
    /// Tile boxes by tile count. The largest arrangement that can be filled is used.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, Box[]> Layouts = new Dictionary<int, Box[]>
    {
        [4] = [new(0, 0, 480, 270), new(480, 0, 960, 270), new(0, 270, 480, 540), new(480, 270, 960, 540)],
        [3] = [new(0, 0, 480, 270), new(480, 0, 960, 270), new(0, 270, 960, 540)],
        [2] = [new(0, 0, 480, 540), new(480, 0, 960, 540)],
        [1] = [new(0, 0, 960, 540)],
    };

    /// <summary>
    /// The part of the title band that covers a tile, in that tile's own coordinates.
    /// </summary>
    /// <param name="band">The title band in card coordinates.</param>
    /// <param name="tile">The tile box in card coordinates.</param>
    /// <returns>The covered area, or null when the title does not reach the tile.</returns>
    public static Box? Keepout(Box band, Box tile)
    {
        if (!band.Overlaps(tile))
        {
            return null;
        }

        return new Box(
            Math.Max(band.Left, tile.Left) - tile.Left,
            Math.Max(band.Top, tile.Top) - tile.Top,
            Math.Min(band.Right, tile.Right) - tile.Left,
            Math.Min(band.Bottom, tile.Bottom) - tile.Top);
    }
}

/// <summary>
/// Places candidate images into tiles so that no detected text sits behind the title.
/// </summary>
public static class TilePicker
{
    /// <summary>Candidates considered per layout before giving up on it.</summary>
    public const int MaxCandidates = 60;

    /// <summary>
    /// Fills the largest layout possible, in candidate order.
    /// </summary>
    /// <typeparam name="T">Candidate type.</typeparam>
    /// <param name="candidates">Candidates, most preferred first.</param>
    /// <param name="band">The title band in card coordinates.</param>
    /// <param name="textBoxes">
    /// Text found in a candidate when drawn into a tile of the given size, in tile coordinates;
    /// null when the candidate's image cannot be used.
    /// </param>
    /// <returns>The chosen (candidate, tile box) pairs; empty when no layout can be filled.</returns>
    public static IReadOnlyList<(T Candidate, Box Tile)> Pick<T>(
        IReadOnlyList<T> candidates,
        Box band,
        Func<T, (int Width, int Height), IReadOnlyList<Box>?> textBoxes)
        where T : class
    {
        foreach (var count in CardLayout.Layouts.Keys.OrderByDescending(count => count))
        {
            var layout = CardLayout.Layouts[count];
            var placed = Fill(candidates, layout, band, textBoxes);
            if (placed.All(candidate => candidate is not null))
            {
                return placed.Select((candidate, index) => (candidate!, layout[index])).ToList();
            }
        }

        return [];
    }

    private static T?[] Fill<T>(
        IReadOnlyList<T> candidates,
        Box[] layout,
        Box band,
        Func<T, (int Width, int Height), IReadOnlyList<Box>?> textBoxes)
        where T : class
    {
        var keepouts = layout.Select(tile => CardLayout.Keepout(band, tile)).ToArray();
        var placed = new T?[layout.Length];
        foreach (var candidate in candidates.Take(MaxCandidates))
        {
            for (var position = 0; position < layout.Length; position++)
            {
                if (placed[position] is not null)
                {
                    continue;
                }

                var boxes = textBoxes(candidate, layout[position].Size);
                if (boxes is null)
                {
                    // The image could not be loaded or is the wrong shape; try the next candidate.
                    break;
                }

                if (keepouts[position] is not { } keepout || !boxes.Any(text => text.Overlaps(keepout)))
                {
                    placed[position] = candidate;
                    break;
                }
            }

            if (placed.All(candidate => candidate is not null))
            {
                break;
            }
        }

        return placed;
    }
}

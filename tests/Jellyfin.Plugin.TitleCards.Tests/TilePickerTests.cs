using Jellyfin.Plugin.TitleCards.Cards;
using Xunit;

namespace Jellyfin.Plugin.TitleCards.Tests;

public class TilePickerTests
{
    // Roughly where "Live TV" sits on a card, with the clear-space margin.
    private static readonly Box Band = new(226, 192, 735, 326);

    private sealed record Item(string Id);

    private static IReadOnlyList<Box> NoText(Item item, (int Width, int Height) size) => [];

    [Fact]
    public void FillsFourTilesInCandidateOrder()
    {
        var items = Enumerable.Range(0, 6).Select(n => new Item($"i{n}")).ToList();
        var tiles = TilePicker.Pick(items, Band, NoText);
        Assert.Equal(["i0", "i1", "i2", "i3"], tiles.Select(tile => tile.Candidate.Id));
        Assert.Equal(CardLayout.Layouts[4], tiles.Select(tile => tile.Tile));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SmallLibrariesUseSmallerLayouts(int count)
    {
        var items = Enumerable.Range(0, count).Select(n => new Item($"i{n}")).ToList();
        var tiles = TilePicker.Pick(items, Band, NoText);
        Assert.Equal(CardLayout.Layouts[count], tiles.Select(tile => tile.Tile));
    }

    [Fact]
    public void NothingUsableGivesNoCard()
        => Assert.Empty(TilePicker.Pick(new List<Item>(), Band, NoText));

    [Fact]
    public void ImageWithTextBehindTheTitleIsSkipped()
    {
        var items = new List<Item> { new("busy"), new("a"), new("b"), new("c"), new("d") };
        IReadOnlyList<Box> Text(Item item, (int Width, int Height) size)
            => item.Id == "busy" ? [new Box(0, 0, size.Width, size.Height)] : [];

        var tiles = TilePicker.Pick(items, Band, Text);
        Assert.Equal(4, tiles.Count);
        Assert.DoesNotContain(tiles, tile => tile.Candidate.Id == "busy");
    }

    [Fact]
    public void TextAwayFromTheTitleIsAllowed()
    {
        // A logo in an image's top-left corner is clear of the title in the top-left tile
        // (it would sit under the title in the bottom-right tile).
        var items = Enumerable.Range(0, 4).Select(n => new Item($"i{n}")).ToList();
        IReadOnlyList<Box> Text(Item item, (int Width, int Height) size) => item.Id == "i0" ? [new Box(10, 10, 120, 60)] : [];
        var tiles = TilePicker.Pick(items, Band, Text);
        Assert.Equal(4, tiles.Count);
        Assert.Equal("i0", tiles[0].Candidate.Id);
    }

    [Fact]
    public void ImagesThatFailToLoadAreSkipped()
    {
        var items = new List<Item> { new("broken"), new("a"), new("b"), new("c"), new("d") };
        IReadOnlyList<Box>? Text(Item item, (int Width, int Height) size) => item.Id == "broken" ? null : [];
        Assert.DoesNotContain(TilePicker.Pick(items, Band, Text), tile => tile.Candidate.Id == "broken");
    }

    [Fact]
    public void KeepoutIsInTileCoordinates()
    {
        var keepout = CardLayout.Keepout(Band, new Box(480, 270, 960, 540));
        Assert.Equal(new Box(0, 0, 255, 56), keepout);
        Assert.Null(CardLayout.Keepout(new Box(0, 0, 10, 10), new Box(480, 270, 960, 540)));
    }
}

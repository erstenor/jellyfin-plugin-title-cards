using Jellyfin.Plugin.TitleCards.Cards;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.TitleCards.Tests;

public class RendererTests
{
    [Fact]
    public void SquareArtIsShownWholeOnABlurredFill()
    {
        using var square = new SKBitmap(1000, 1000);
        square.Erase(SKColors.White);
        using var tile = CardRenderer.FitTile(square, (480, 270));
        Assert.Equal((480, 270), (tile.Width, tile.Height));
        // The art fills the full height in the middle; the sides are the darkened fill.
        Assert.Equal(SKColors.White, tile.GetPixel(240, 135));
        Assert.True(tile.GetPixel(5, 135).Red < 200);
    }

    [Fact]
    public void SquareArtFillsTheTileWhenCropping()
    {
        using var square = new SKBitmap(1000, 1000);
        square.Erase(SKColors.White);
        using var tile = CardRenderer.FitTile(square, (480, 270), cropToFill: true);
        Assert.Equal(SKColors.White, tile.GetPixel(5, 135));
        Assert.Equal(SKColors.White, tile.GetPixel(474, 135));
    }

    [Fact]
    public void LandscapeArtIsCroppedToFill()
    {
        using var wide = new SKBitmap(1920, 1080);
        wide.Erase(SKColors.Red);
        using var tile = CardRenderer.FitTile(wide, (480, 270));
        Assert.Equal(SKColors.Red, tile.GetPixel(2, 2));
    }

    [Fact]
    public void CardIsDarkenedAndCarriesTheTitle()
    {
        using var white = new SKBitmap(480, 270);
        white.Erase(SKColors.White);
        using var card = CardRenderer.Compose(CardLayout.Layouts[4].Select(box => (white, box)), "Movies");
        Assert.Equal((CardLayout.Width, CardLayout.Height), (card.Width, card.Height));
        // A black layer at alpha 0x78 over white leaves about 53% brightness.
        Assert.InRange(card.GetPixel(5, 5).Red, 130, 140);

        var band = CardRenderer.TitleBand("Movies");
        Assert.InRange((band.Left + band.Right) / 2, 470, 490);
        Assert.True(band.Top < CardLayout.Height / 2 && band.Bottom > CardLayout.Height / 2);
    }

    [Fact]
    public void LongTitlesShrinkToFit()
    {
        var band = CardRenderer.TitleBand("A Very Long Library Name Indeed");
        Assert.True(band.Width - (2 * CardLayout.TitleMargin) <= CardLayout.Width * 0.95f);
    }

    [Fact]
    public void LatinTitlesNeedNoFallback()
        => Assert.Empty(CardRenderer.MissingCharacters("Movies & TV"));

    [Fact]
    public void JapaneseTitlesUseAnInstalledFontThatHasThem()
    {
        const string title = "映画 Movies";
        var missing = CardRenderer.MissingCharacters(title);
        if (SKFontManager.Default.MatchCharacter('映') is null)
        {
            // No Japanese font on this machine: the characters are reported, not silently boxed.
            Assert.Equal(["映", "画"], missing);
            return;
        }

        Assert.Empty(missing);
        using var black = new SKBitmap(480, 270);
        black.Erase(SKColors.Black);
        using var card = CardRenderer.Compose(CardLayout.Layouts[4].Select(box => (black, box)), title);
        var band = CardRenderer.TitleBand(title);
        Assert.InRange((band.Left + band.Right) / 2, 460, 500);

        // The Japanese part, left of "Movies", is drawn in white rather than left empty.
        var latin = CardRenderer.TitleBand("Movies");
        var lit = 0;
        for (var x = (int)band.Left; x < band.Left + (band.Width - latin.Width); x++)
        {
            for (var y = (int)band.Top; y < band.Bottom; y++)
            {
                lit += card.GetPixel(x, y).Red > 200 ? 1 : 0;
            }
        }

        Assert.True(lit > 500, $"only {lit} title pixels in the Japanese part");
    }
}

public class TextDetectorTests
{
    [Theory]
    [InlineData(960, 540, 1312, 736)]
    [InlineData(480, 270, 1312, 736)]
    [InlineData(1920, 1080, 1920, 1088)]
    public void InputIsScaledLikeRapidOcr(int width, int height, int expectedWidth, int expectedHeight)
        => Assert.Equal((expectedWidth, expectedHeight), TextDetector.ResizedSize(width, height));

    [Fact]
    public void FindsAConfidentRegionAndIgnoresWeakOnes()
    {
        const int width = 100, height = 50;
        var map = new float[width * height];
        for (var y = 10; y < 20; y++)
        {
            for (var x = 10; x < 60; x++)
            {
                map[(y * width) + x] = 0.9f; // confident text
            }
        }

        for (var y = 30; y < 40; y++)
        {
            for (var x = 70; x < 90; x++)
            {
                map[(y * width) + x] = 0.35f; // above the pixel threshold, below the box threshold
            }
        }

        var boxes = TextDetector.FindBoxes(map, width, height, 2f, 2f);
        var box = Assert.Single(boxes);
        // Grown by the unclip distance and scaled by 2 back to image coordinates.
        Assert.True(box.Left < 20 && box.Right > 120 && box.Top < 20 && box.Bottom > 40);
    }
}

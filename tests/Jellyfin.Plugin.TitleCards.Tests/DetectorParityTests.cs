using System.Text.Json;
using Jellyfin.Plugin.TitleCards.Cards;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.TitleCards.Tests;

/// <summary>
/// Compares the plugin's text detection with RapidOCR (full detection + recognition) on real artwork.
/// </summary>
/// <remarks>
/// Needs fixtures made by build/make_parity_fixtures.py (images plus RapidOCR's verdict for a title
/// band); set TITLECARDS_FIXTURES to their folder. Skipped otherwise, as the artwork is not committed.
/// </remarks>
public class DetectorParityTests
{
    private readonly ITestOutputHelper _output;

    public DetectorParityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void AgreesWithRapidOcrOnRealArtwork()
    {
        var folder = Environment.GetEnvironmentVariable("TITLECARDS_FIXTURES");
        if (string.IsNullOrEmpty(folder) || !File.Exists(Path.Combine(folder, "expected.json")))
        {
            _output.WriteLine("TITLECARDS_FIXTURES not set; skipping.");
            return;
        }

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "expected.json")));
        var b = json.RootElement.GetProperty("band").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        var band = new Box(b[0], b[1], b[2], b[3]);
        using var detector = TextDetector.Create(NullLogger.Instance);
        Assert.True(detector.IsAvailable, detector.UnavailableReason);

        int agree = 0, total = 0, stricter = 0, looser = 0;
        foreach (var entry in json.RootElement.GetProperty("images").EnumerateArray())
        {
            using var image = SKBitmap.Decode(Path.Combine(folder, entry.GetProperty("file").GetString()!));
            var expected = entry.GetProperty("reject").GetBoolean();
            var actual = detector.Detect(image).Any(box => box.Overlaps(band));
            total++;
            if (actual == expected)
            {
                agree++;
                continue;
            }

            if (actual)
            {
                stricter++;
            }
            else
            {
                looser++;
            }

            _output.WriteLine($"{(actual ? "plugin rejects, RapidOCR accepts" : "plugin accepts, RapidOCR rejects")}: {entry.GetProperty("name").GetString()}");
        }

        _output.WriteLine($"Agreement {agree}/{total}; plugin stricter on {stricter}, looser on {looser}.");
        // Being stricter only costs a candidate; being looser puts text behind the title.
        Assert.True(looser <= total / 20, $"plugin let text through on {looser} of {total} images");
        Assert.True(agree >= total * 0.85, $"agreement {agree}/{total}");
    }
}

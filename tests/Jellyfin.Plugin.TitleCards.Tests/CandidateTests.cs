using Jellyfin.Plugin.TitleCards.Cards;
using Xunit;

namespace Jellyfin.Plugin.TitleCards.Tests;

public class CandidateTests
{
    [Fact]
    public void RepeatedImagesAreDroppedEvenUnderDifferentNames()
    {
        // Chapters of one audiobook carry the same cover under different names.
        var candidates = new[]
        {
            new CardCandidate(Guid.NewGuid(), "03 - Ch 3", "/covers/artemus.jpg"),
            new CardCandidate(Guid.NewGuid(), "04 - Ch 4", "/covers/artemus.jpg"),
            new CardCandidate(Guid.NewGuid(), "Heart of Darkness", "/covers/conrad.jpg"),
        };

        var unique = CandidateSource.UniqueByName(candidates, shuffle: false);

        Assert.Equal(["03 - Ch 3", "Heart of Darkness"], unique.Select(candidate => candidate.Name));
    }

    [Fact]
    public void RepeatedNamesAreDropped()
    {
        var candidates = new[]
        {
            new CardCandidate(Guid.NewGuid(), "Family Guy", "/a.jpg"),
            new CardCandidate(Guid.NewGuid(), "family guy ", "/b.jpg"),
        };

        Assert.Single(CandidateSource.UniqueByName(candidates, shuffle: false));
    }
}

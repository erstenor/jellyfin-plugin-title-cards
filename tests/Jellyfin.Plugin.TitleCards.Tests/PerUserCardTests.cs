using Jellyfin.Plugin.TitleCards.Cards;
using Jellyfin.Plugin.TitleCards.Preferences;
using Jellyfin.Plugin.TitleCards.Variants;
using Jellyfin.Plugin.TitleCards.Web;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.TitleCards.Tests;

public sealed class PerUserCardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "titlecards-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private VariantImageStore Store() => new(() => Path.Combine(_root, "images"));

    [Fact]
    public async Task SavedCardIsFoundByItsTag()
    {
        var store = Store();
        var viewId = Guid.NewGuid();

        await store.SaveAsync(viewId, VariantImageStore.Card, [1, 2, 3], CancellationToken.None);
        var tag = store.TagFor(viewId, VariantImageStore.Card);

        Assert.NotNull(tag);
        Assert.True(VariantImageStore.TryParseTag(tag, out var variant));
        Assert.Equal(VariantImageStore.Card, variant);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(store.Find(viewId, variant)!));
    }

    [Fact]
    public async Task ReplacingACardChangesItsTag()
    {
        var store = Store();
        var viewId = Guid.NewGuid();
        await store.SaveAsync(viewId, VariantImageStore.Card, [1], CancellationToken.None);
        var first = store.TagFor(viewId, VariantImageStore.Card);
        await store.SaveAsync(viewId, VariantImageStore.Card, [2], CancellationToken.None);
        var second = store.TagFor(viewId, VariantImageStore.Card);
        await store.SaveAsync(viewId, VariantImageStore.Card, [2], CancellationToken.None);

        Assert.NotEqual(first, second);
        Assert.Equal(second, store.TagFor(viewId, VariantImageStore.Card)); // same image, same tag
    }

    [Fact]
    public async Task ACardInUseIsReplacedOnceItIsReleased()
    {
        var store = Store();
        var viewId = Guid.NewGuid();
        await store.SaveAsync(viewId, VariantImageStore.Card, [1], CancellationToken.None);

        // A reader without delete sharing (as on Windows while a client downloads the card)
        // blocks the replace until it lets go.
        var reader = new FileStream(store.Find(viewId, VariantImageStore.Card)!, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Delay(500).ContinueWith(_ => reader.Dispose(), TaskScheduler.Default);
        await store.SaveAsync(viewId, VariantImageStore.Card, [2], CancellationToken.None);
        await release;

        Assert.Equal([2], File.ReadAllBytes(store.Find(viewId, VariantImageStore.Card)!));
    }

    [Fact]
    public async Task DeletedCardsHaveNoTag()
    {
        var store = Store();
        var viewId = Guid.NewGuid();
        await store.SaveAsync(viewId, VariantImageStore.Card, [1], CancellationToken.None);

        store.DeleteAll(viewId);

        Assert.Null(store.TagFor(viewId, VariantImageStore.Card));
        Assert.Null(store.TagFor(Guid.NewGuid(), VariantImageStore.Card));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("8d2f0a6c1e4b9f7a3c5d")] // an ordinary Jellyfin tag
    [InlineData("tc-")]
    [InlineData("tc--1a")]
    [InlineData("tc-../../etc-1a")]
    [InlineData("tc-Card-1a")]
    public void OtherTagsAreNotThePlugins(string? tag)
        => Assert.False(VariantImageStore.TryParseTag(tag, out _));

    [Fact]
    public async Task ViewsWithCardsGetTheCardTagAndOtherItemsAreLeftAlone()
    {
        var store = Store();
        var withCard = Guid.NewGuid();
        var excluded = Guid.NewGuid();
        await store.SaveAsync(withCard, VariantImageStore.Card, [1], CancellationToken.None);
        await store.SaveAsync(excluded, VariantImageStore.Card, [1], CancellationToken.None);
        var filter = new VariantImageFilter(store, new UserPreferenceStore(() => Path.Combine(_root, "prefs.json")), NullLogger<VariantImageFilter>.Instance);
        var view = new BaseItemDto
        {
            Id = withCard,
            ImageTags = new Dictionary<ImageType, string> { [ImageType.Primary] = "jellyfin" },
            ImageBlurHashes = new Dictionary<ImageType, Dictionary<string, string>> { [ImageType.Primary] = new() { ["jellyfin"] = "hash" } },
        };
        var liveTv = new BaseItemDto { Id = withCard };
        var excludedView = new BaseItemDto { Id = excluded, ImageTags = new Dictionary<ImageType, string> { [ImageType.Primary] = "jellyfin" } };
        var movie = new BaseItemDto { Id = Guid.NewGuid(), ImageTags = new Dictionary<ImageType, string> { [ImageType.Primary] = "movie" } };

        filter.ApplyCards([view, liveTv, excludedView, movie], [excluded]);

        Assert.StartsWith("tc-card-", view.ImageTags[ImageType.Primary], StringComparison.Ordinal);
        Assert.False(view.ImageBlurHashes.ContainsKey(ImageType.Primary));
        Assert.StartsWith("tc-card-", liveTv.ImageTags![ImageType.Primary], StringComparison.Ordinal); // views without an image of their own get one
        Assert.Equal("jellyfin", excludedView.ImageTags[ImageType.Primary]);
        Assert.Equal("movie", movie.ImageTags[ImageType.Primary]);
    }

    [Fact]
    public void PreferencesPersistAndUnsetFollowsTheServerDefault()
    {
        var path = Path.Combine(_root, "prefs.json");
        var user = Guid.NewGuid();
        var store = new UserPreferenceStore(() => path);

        Assert.True(store.ShowsTitleCards(user, serverDefault: true));
        store.Set(user, new UserPreference { ShowTitleCards = false });

        var reloaded = new UserPreferenceStore(() => path);
        Assert.False(reloaded.ShowsTitleCards(user, serverDefault: true));
        reloaded.Set(user, new UserPreference { ShowTitleCards = null });
        Assert.False(new UserPreferenceStore(() => path).ShowsTitleCards(user, serverDefault: false));
        Assert.True(new UserPreferenceStore(() => path).ShowsTitleCards(user, serverDefault: true));
    }

    [Fact]
    public void ViewsNeedACardUntilOneIsStoredOrNoneCouldBeBuilt()
    {
        var state = new CardStateStore(_root);
        var unseen = Guid.NewGuid();

        Assert.True(state.NeedsCard(unseen, hasCard: false));
        Assert.True(state.NeedsCard(unseen, hasCard: true));
    }

    [Fact]
    public void EmptyViewsAreRetriedButRejectedOnesWaitForTheNightlyRefresh()
    {
        var state = new CardStateStore(_root);
        var empty = new MediaBrowser.Controller.Entities.Folder { Id = Guid.NewGuid(), Name = "New library" };
        var rejected = new MediaBrowser.Controller.Entities.Folder { Id = Guid.NewGuid(), Name = "Logos everywhere" };
        var carded = new MediaBrowser.Controller.Entities.Folder { Id = Guid.NewGuid(), Name = "Movies" };

        state.Record(empty, [], candidates: 0);
        state.Record(rejected, [], candidates: 40);
        state.Record(carded, ["A", "B"], candidates: 40);

        Assert.True(state.NeedsCard(empty.Id, hasCard: false));
        Assert.False(state.NeedsCard(rejected.Id, hasCard: false));
        Assert.False(state.NeedsCard(carded.Id, hasCard: true));
        Assert.True(state.NeedsCard(carded.Id, hasCard: false)); // its card was deleted
    }

    [Theory]
    // The shared "index.html" pipeline is also matched as a pattern against chunks like these.
    [InlineData("\"use strict\";(self.webpackChunk=self.webpackChunk||[]).push([[94889],{16343:function(e,a,d){a.default='<div id=\"homeScreenPreferencesPage\"></div>'}}]);")]
    [InlineData("var t='<html><body></body></html>';")]
    [InlineData("<div>template</div></body>")]
    public void OnlyHtmlPagesAreChanged(string contents)
        => Assert.Equal(contents, WebClientInjection.AddScript(new TransformationContents { Contents = contents }));

    [Fact]
    public void ScriptIsAddedToIndexHtmlOnce()
    {
        var once = WebClientInjection.AddScript(new TransformationContents { Contents = "<!doctype html><html><body><div></div></body></html>" });
        var twice = WebClientInjection.AddScript(new TransformationContents { Contents = once });

        Assert.Contains("TitleCards/ClientScript\"></script></body>", once, StringComparison.Ordinal);
        Assert.Equal(once, twice);
    }
}

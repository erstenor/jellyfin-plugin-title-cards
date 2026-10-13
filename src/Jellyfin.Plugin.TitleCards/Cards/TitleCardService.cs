using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.TitleCards.Variants;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TitleCards.Cards;

/// <summary>
/// Builds and saves title cards for home-screen views.
/// </summary>
public sealed class TitleCardService : IDisposable
{
    private readonly IUserManager _userManager;
    private readonly IUserViewManager _userViewManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CandidateSource _candidates;
    private readonly VariantImageStore _store;
    private readonly ILogger<TitleCardService> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly Lazy<TextDetector> _detector;
    private CardStateStore? _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="TitleCardService"/> class.
    /// </summary>
    /// <param name="userManager">User manager.</param>
    /// <param name="userViewManager">User view manager.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="providerManager">Provider manager, which refreshes Jellyfin's own images.</param>
    /// <param name="fileSystem">File system.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="candidates">Candidate source.</param>
    /// <param name="store">Where cards are kept.</param>
    /// <param name="logger">Logger.</param>
    public TitleCardService(
        IUserManager userManager,
        IUserViewManager userViewManager,
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IFileSystem fileSystem,
        IHttpClientFactory httpClientFactory,
        CandidateSource candidates,
        VariantImageStore store,
        ILogger<TitleCardService> logger)
    {
        _userManager = userManager;
        _userViewManager = userViewManager;
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _httpClientFactory = httpClientFactory;
        _candidates = candidates;
        _store = store;
        _logger = logger;
        // Loaded on first use, so runs with nothing to do never load the model.
        _detector = new Lazy<TextDetector>(() => TextDetector.Create(logger));
    }

    private CardStateStore State => _state ??= new CardStateStore(Plugin.Instance!.DataFolderPath);

    /// <summary>
    /// Builds cards for every view any enabled user sees.
    /// </summary>
    /// <param name="missingOnly">Only views without a current card (new views, or images the server replaced).</param>
    /// <param name="progress">Progress, 0 to 100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task RunAsync(bool missingOnly, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        await _runLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReturnImageSlotsAsync(cancellationToken).ConfigureAwait(false);
            var config = Plugin.Instance!.Configuration;
            var views = DiscoverViews()
                .Where(entry => !config.ExcludedViewIds.Contains(entry.View.Id))
                .Where(entry => !missingOnly || State.NeedsCard(entry.View.Id, _store.TagFor(entry.View.Id, VariantImageStore.Card) is not null))
                .ToList();
            if (views.Count == 0)
            {
                progress?.Report(100);
                return;
            }

            var detector = config.EnableTextFilter ? _detector.Value : null;
            for (var index = 0; index < views.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (view, user) = views[index];
                try
                {
                    await BuildCardAsync(view, user, detector, config.ExcludeSports, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Title Cards: failed to build the card for {View}", view.Name);
                }

                progress?.Report(100.0 * (index + 1) / views.Count);
            }
        }
        finally
        {
            _runLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _runLock.Dispose();
        if (_detector.IsValueCreated)
        {
            _detector.Value.Dispose();
        }
    }

    /// <summary>
    /// Every home-screen view any enabled user sees, once each, with a user who can see it.
    /// </summary>
    /// <remarks>
    /// Users with grouped folders get combined views (one per combination of grouped libraries),
    /// while other users see the libraries themselves, so all of them need cards.
    /// </remarks>
    /// <returns>Views and users.</returns>
    private List<(Folder View, User User)> DiscoverViews()
    {
        var views = new Dictionary<Guid, (Folder, User)>();
        foreach (var user in _userManager.GetUsers().Where(user => !user.HasPermission(PermissionKind.IsDisabled)))
        {
            foreach (var view in _userViewManager.GetUserViews(new UserViewQuery { User = user }))
            {
                views.TryAdd(view.Id, (view, user));
            }
        }

        return [.. views.Values];
    }

    private async Task BuildCardAsync(Folder view, User user, TextDetector? detector, bool excludeSports, CancellationToken cancellationToken)
    {
        var title = view.Name;
        var missing = CardRenderer.MissingCharacters(title);
        if (missing.Count > 0)
        {
            _logger.LogWarning("Title Cards: {View}: no installed font has {Characters}; they will show as boxes until a font with them is installed", title, string.Join(" ", missing));
        }

        var candidates = _candidates.ForView(view, user, excludeSports);
        using var images = new CardImages(_httpClientFactory, detector, _logger);
        // A cover is used only where its lettering clears the title both shown whole and cropped,
        // so the two versions of the card show the same artwork.
        var tiles = TilePicker.Pick(candidates, CardRenderer.TitleBand(title), images.TextBoxesBothFits);
        if (tiles.Count == 0)
        {
            _logger.LogInformation("Title Cards: {View}: no images clear the title ({Count} candidates); its current card (or Jellyfin's image) is kept", title, candidates.Count);
            State.Record(view, [], candidates.Count);
            return;
        }

        // The view's own image stays Jellyfin's; users who see cards are pointed at these.
        await _store.SaveAsync(view.Id, VariantImageStore.Card, Render(images, tiles, title, cropToFill: false), cancellationToken).ConfigureAwait(false);
        var cropped = tiles.Any(tile => images.CropChangesFit(tile.Candidate, tile.Tile.Size));
        if (cropped)
        {
            await _store.SaveAsync(view.Id, VariantImageStore.CroppedCard, Render(images, tiles, title, cropToFill: true), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Nothing to crop (backdrops fill their tiles already): everyone gets the one card.
            _store.Delete(view.Id, VariantImageStore.CroppedCard);
        }

        var names = tiles.Select(tile => tile.Candidate.Name).ToList();
        State.Record(view, names, candidates.Count);
        _logger.LogInformation("Title Cards: {View}: card saved{Cropped} [{Tiles}]", title, cropped ? " (and a cropped version)" : string.Empty, string.Join(", ", names));
    }

    private static byte[] Render(CardImages images, IReadOnlyList<(CardCandidate Candidate, Box Tile)> tiles, string title, bool cropToFill)
    {
        using var card = CardRenderer.Compose(tiles.Select(tile => (images.Tile(tile.Candidate, tile.Tile.Size, cropToFill)!, tile.Tile)), title);
        return CardRenderer.EncodePng(card);
    }

    /// <summary>
    /// Versions before 0.2 saved cards into views' own image slots, where Jellyfin then stopped
    /// refreshing its images. Removes those cards (only where the slot still holds the card the
    /// plugin saved) and lets Jellyfin rebuild its own images. Runs once.
    /// </summary>
    private async Task ReturnImageSlotsAsync(CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance!;
        if (plugin.Configuration.ImageSlotsReturned)
        {
            return;
        }

        foreach (var (viewId, imagePath, imageModified) in State.CardsInImageSlots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var view = _libraryManager.GetItemById(viewId);
                var image = view?.GetImageInfo(ImageType.Primary, 0);
                if (view is not null && image is not null
                    && string.Equals(image.Path, imagePath, StringComparison.Ordinal)
                    && image.DateModified == imageModified)
                {
                    await view.DeleteImageAsync(ImageType.Primary, 0).ConfigureAwait(false);
                    _providerManager.QueueRefresh(
                        view.Id,
                        new MetadataRefreshOptions(new DirectoryService(_fileSystem)) { ImageRefreshMode = MetadataRefreshMode.Default },
                        RefreshPriority.High);
                    _logger.LogInformation("Title Cards: {View}: returned its image to Jellyfin", view.Name);
                }

                State.ForgetImageSlot(viewId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Title Cards: could not return the image of view {ViewId} to Jellyfin", viewId);
            }
        }

        plugin.Configuration.ImageSlotsReturned = true;
        plugin.SaveConfiguration();
    }
}

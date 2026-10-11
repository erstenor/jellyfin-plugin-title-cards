using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
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
    private readonly IProviderManager _providerManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CandidateSource _candidates;
    private readonly ILogger<TitleCardService> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly Lazy<TextDetector> _detector;
    private CardStateStore? _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="TitleCardService"/> class.
    /// </summary>
    /// <param name="userManager">User manager.</param>
    /// <param name="userViewManager">User view manager.</param>
    /// <param name="providerManager">Provider manager, which saves item images.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="candidates">Candidate source.</param>
    /// <param name="logger">Logger.</param>
    public TitleCardService(
        IUserManager userManager,
        IUserViewManager userViewManager,
        IProviderManager providerManager,
        IHttpClientFactory httpClientFactory,
        CandidateSource candidates,
        ILogger<TitleCardService> logger)
    {
        _userManager = userManager;
        _userViewManager = userViewManager;
        _providerManager = providerManager;
        _httpClientFactory = httpClientFactory;
        _candidates = candidates;
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
            var config = Plugin.Instance!.Configuration;
            var views = DiscoverViews()
                .Where(entry => !config.ExcludedViewIds.Contains(entry.View.Id))
                .Where(entry => !missingOnly || State.NeedsCard(entry.View))
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
        var candidates = _candidates.ForView(view, user, excludeSports);
        using var images = new CardImages(_httpClientFactory, detector, _logger);
        var tiles = TilePicker.Pick(candidates, CardRenderer.TitleBand(title), images.TextBoxes);
        if (tiles.Count == 0)
        {
            _logger.LogInformation("Title Cards: {View}: no images clear the title ({Count} candidates); image kept", title, candidates.Count);
            State.Record(view, []);
            return;
        }

        byte[] png;
        using (var card = CardRenderer.Compose(tiles.Select(tile => (images.Tile(tile.Candidate, tile.Tile.Size)!, tile.Tile)), title))
        {
            png = CardRenderer.EncodePng(card);
        }

        using (var stream = new MemoryStream(png))
        {
            await _providerManager.SaveImage(view, stream, "image/png", ImageType.Primary, null, cancellationToken).ConfigureAwait(false);
        }

        await view.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
        var names = tiles.Select(tile => tile.Candidate.Name).ToList();
        State.Record(view, names);
        _logger.LogInformation("Title Cards: {View}: card saved [{Tiles}]", title, string.Join(", ", names));
    }
}

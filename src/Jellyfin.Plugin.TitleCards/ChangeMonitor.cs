using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.TitleCards.Cards;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Jellyfin.Data.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TitleCards;

/// <summary>
/// Builds missing cards shortly after a user's home settings change or a library is added.
/// </summary>
/// <remarks>
/// Changing Home &gt; Library Folders gives that user a new combined view for the new combination
/// of libraries, which starts with the server's plain image. Events are debounced so a burst of
/// changes leads to one run.
/// </remarks>
public sealed class ChangeMonitor : IHostedService, IDisposable
{
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(20);

    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly TitleCardService _service;
    private readonly ILogger<ChangeMonitor> _logger;
    private readonly object _lock = new();
    private CancellationTokenSource? _pending;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangeMonitor"/> class.
    /// </summary>
    /// <param name="userManager">User manager.</param>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="service">Title card service.</param>
    /// <param name="logger">Logger.</param>
    public ChangeMonitor(IUserManager userManager, ILibraryManager libraryManager, TitleCardService service, ILogger<ChangeMonitor> logger)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _service = service;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _userManager.OnUserUpdated += OnUserUpdated;
        _libraryManager.ItemAdded += OnItemAdded;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _userManager.OnUserUpdated -= OnUserUpdated;
        _libraryManager.ItemAdded -= OnItemAdded;
        lock (_lock)
        {
            _pending?.Cancel();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            _pending?.Dispose();
        }
    }

    private void OnUserUpdated(object? sender, GenericEventArgs<User> e) => Schedule("user settings changed");

    private void OnItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is CollectionFolder or UserView)
        {
            Schedule($"view added: {e.Item.Name}");
        }
    }

    private void Schedule(string reason)
    {
        if (Plugin.Instance?.Configuration.ReactToChanges != true)
        {
            return;
        }

        CancellationTokenSource source;
        lock (_lock)
        {
            _pending?.Cancel();
            _pending?.Dispose();
            _pending = source = new CancellationTokenSource();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Delay, source.Token).ConfigureAwait(false);
                _logger.LogDebug("Title Cards: checking for missing cards ({Reason})", reason);
                await _service.RunAsync(missingOnly: true, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a later change.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Title Cards: check after a change failed");
            }
        });
    }
}

using Jellyfin.Plugin.TitleCards.Cards;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.TitleCards.ScheduledTasks;

/// <summary>
/// Rebuilds every card with fresh artwork. Runs nightly by default.
/// </summary>
public class RefreshTitleCardsTask : IScheduledTask
{
    private readonly TitleCardService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshTitleCardsTask"/> class.
    /// </summary>
    /// <param name="service">The title card service.</param>
    public RefreshTitleCardsTask(TitleCardService service) => _service = service;

    /// <inheritdoc />
    public string Name => "Refresh title cards";

    /// <inheritdoc />
    public string Key => "TitleCardsRefresh";

    /// <inheritdoc />
    public string Description => "Rebuilds every home-screen library card with fresh artwork.";

    /// <inheritdoc />
    public string Category => "Title Cards";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _service.RunAsync(missingOnly: false, progress, cancellationToken);

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(2.5).Ticks };
    }
}

/// <summary>
/// Builds cards only for views without a current one. A safety net behind the change monitor.
/// </summary>
public class CreateMissingTitleCardsTask : IScheduledTask
{
    private readonly TitleCardService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateMissingTitleCardsTask"/> class.
    /// </summary>
    /// <param name="service">The title card service.</param>
    public CreateMissingTitleCardsTask(TitleCardService service) => _service = service;

    /// <inheritdoc />
    public string Name => "Create missing title cards";

    /// <inheritdoc />
    public string Key => "TitleCardsMissing";

    /// <inheritdoc />
    public string Description => "Builds cards for new views and libraries, and replaces cards the server has overwritten.";

    /// <inheritdoc />
    public string Category => "Title Cards";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _service.RunAsync(missingOnly: true, progress, cancellationToken);

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger };
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromMinutes(30).Ticks };
    }
}

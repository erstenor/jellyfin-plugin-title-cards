using Jellyfin.Plugin.TitleCards.Preferences;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TitleCards.Variants;

/// <summary>
/// Shows title cards to the users who want them, in every client. A view's own image stays
/// Jellyfin's; for users who see cards, replies that describe views carry the card's image tag
/// instead, and image requests with such a tag are answered with the stored card.
/// </summary>
/// <remarks>
/// Hooks Jellyfin's own controllers by name. If a Jellyfin version renames them, nothing matches
/// and everyone simply sees Jellyfin's images.
/// </remarks>
public sealed class VariantImageFilter : IAsyncResultFilter, IAsyncActionFilter
{
    private const string UserIdClaim = "Jellyfin-UserId";

    private static readonly string[] ImageActions = ["GetItemImage", "GetItemImageByIndex", "GetItemImage2"];

    private readonly VariantImageStore _store;
    private readonly UserPreferenceStore _preferences;
    private readonly ILogger<VariantImageFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VariantImageFilter"/> class.
    /// </summary>
    /// <param name="store">Stored cards.</param>
    /// <param name="preferences">Per-user choices.</param>
    /// <param name="logger">Logger.</param>
    public VariantImageFilter(VariantImageStore store, UserPreferenceStore preferences, ILogger<VariantImageFilter> logger)
    {
        _store = store;
        _preferences = preferences;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (IsAction(context.ActionDescriptor, "Image", ImageActions)
            && VariantImageStore.TryParseTag(TagOf(context), out var variant)
            && Guid.TryParse(context.RouteData.Values["itemId"]?.ToString(), out var itemId)
            && _store.Find(itemId, variant) is { } path)
        {
            var type = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            context.Result = new PhysicalFileResult(path, type) { LastModified = File.GetLastWriteTimeUtc(path) };
            // The tag changes whenever the card does, so the file under a tag never changes.
            context.HttpContext.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return;
        }

        await next().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        // Any reply carrying items, from Jellyfin or from other plugins (Home Screen Sections builds
        // its own "My Media" list); only views with a stored card are changed.
        if (context.Result is ObjectResult { Value: { } value } && ItemsIn(value) is { } items)
        {
            try
            {
                var config = Plugin.Instance?.Configuration;
                var userId = UserOf(context);
                if (config is not null && userId != Guid.Empty && _preferences.ShowsTitleCards(userId, config.ShowTitleCardsByDefault))
                {
                    ApplyCards(items, config.ExcludedViewIds);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Title Cards: could not add title cards to a reply");
            }
        }

        await next().ConfigureAwait(false);
    }

    /// <summary>
    /// Points each view that has a card at it.
    /// </summary>
    /// <param name="items">Items in a reply; only views have cards.</param>
    /// <param name="excluded">Views the admin left out.</param>
    public void ApplyCards(IEnumerable<BaseItemDto> items, IReadOnlyCollection<Guid> excluded)
    {
        foreach (var item in items)
        {
            if (item is null || excluded.Contains(item.Id) || _store.TagFor(item.Id, VariantImageStore.Card) is not { } tag)
            {
                continue;
            }

            item.ImageTags ??= [];
            item.ImageTags[ImageType.Primary] = tag;
            // Placeholders are keyed by tag; Jellyfin's no longer matches, so drop it.
            item.ImageBlurHashes?.Remove(ImageType.Primary);
        }
    }

    private static IEnumerable<BaseItemDto>? ItemsIn(object value) => value switch
    {
        QueryResult<BaseItemDto> result => result.Items,
        BaseItemDto item => [item],
        IEnumerable<BaseItemDto> list => list,
        _ => null,
    };

    private static bool IsAction(ActionDescriptor descriptor, string controller, string[] actions)
        => descriptor is ControllerActionDescriptor action
            && action.ControllerName == controller
            && actions.Contains(action.ActionName);

    private static string? TagOf(ActionExecutingContext context)
        => context.HttpContext.Request.Query["tag"].FirstOrDefault()
            ?? context.RouteData.Values["tag"]?.ToString();

    private static Guid UserOf(FilterContext context)
    {
        // An explicit user (admins may ask on behalf of others) wins over the signed-in one.
        var requested = context.HttpContext.Request.Query["userId"].FirstOrDefault()
            ?? context.RouteData.Values["userId"]?.ToString();
        if (Guid.TryParse(requested, out var userId) && userId != Guid.Empty)
        {
            return userId;
        }

        var claim = context.HttpContext.User.FindFirst(UserIdClaim)?.Value;
        return Guid.TryParse(claim, out userId) ? userId : Guid.Empty;
    }
}

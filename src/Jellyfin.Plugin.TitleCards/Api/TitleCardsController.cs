using System.Reflection;
using Jellyfin.Plugin.TitleCards.Preferences;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.TitleCards.Api;

/// <summary>
/// The signed-in user's Title Cards choice, and the web client script that shows it in
/// Settings &gt; Display.
/// </summary>
[ApiController]
[Route("TitleCards")]
public class TitleCardsController : ControllerBase
{
    private const string UserIdClaim = "Jellyfin-UserId";

    private readonly UserPreferenceStore _preferences;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="TitleCardsController"/> class.
    /// </summary>
    /// <param name="preferences">Per-user choices.</param>
    /// <param name="userManager">User manager.</param>
    public TitleCardsController(UserPreferenceStore preferences, IUserManager userManager)
    {
        _preferences = preferences;
        _userManager = userManager;
    }

    /// <summary>
    /// The signed-in user's choice.
    /// </summary>
    /// <returns>The choice, and what applies when it is unset.</returns>
    [HttpGet("Preference")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PreferenceDto> GetPreference()
    {
        if (CurrentUser() is not { } userId)
        {
            return Unauthorized();
        }

        var config = Plugin.Instance!.Configuration;
        var preference = _preferences.Get(userId);
        return new PreferenceDto
        {
            ShowTitleCards = preference.ShowTitleCards ?? config.ShowTitleCardsByDefault,
            IsDefault = preference.ShowTitleCards is null,
            ServerDefault = config.ShowTitleCardsByDefault,
            CropToFill = preference.CropToFill ?? config.CropToFill,
            CropIsDefault = preference.CropToFill is null,
            ServerCropDefault = config.CropToFill,
        };
    }

    /// <summary>
    /// Saves the signed-in user's choice. Applies in every client from its next home-screen load.
    /// </summary>
    /// <param name="request">The changes; fields left out stay as they are.</param>
    /// <returns>The saved choice.</returns>
    [HttpPost("Preference")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PreferenceDto> SetPreference([FromBody] PreferenceRequest request)
    {
        if (CurrentUser() is not { } userId)
        {
            return Unauthorized();
        }

        _preferences.Set(userId, request.ApplyTo(_preferences.Get(userId)));
        return GetPreference();
    }

    /// <summary>
    /// Every user's choice, for the admin settings page.
    /// </summary>
    /// <returns>Users and their choices (null follows the server default).</returns>
    [HttpGet("Preferences")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<UserPreferenceDto>> GetPreferences()
        => _userManager.GetUsers()
            .OrderBy(user => user.Username, StringComparer.OrdinalIgnoreCase)
            .Select(user => new UserPreferenceDto
            {
                UserId = user.Id,
                Name = user.Username,
                ShowTitleCards = _preferences.Get(user.Id).ShowTitleCards,
                CropToFill = _preferences.Get(user.Id).CropToFill,
            })
            .ToList();

    /// <summary>
    /// Sets a user's choice on their behalf (the same choice they can make in Settings &gt; Display).
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="request">The changes; fields left out stay as they are.</param>
    /// <returns>No content.</returns>
    [HttpPost("Preferences/{userId}")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult SetUserPreference([FromRoute] Guid userId, [FromBody] PreferenceRequest request)
    {
        if (_userManager.GetUserById(userId) is null)
        {
            return NotFound();
        }

        _preferences.Set(userId, request.ApplyTo(_preferences.Get(userId)));
        return NoContent();
    }

    /// <summary>
    /// The web client script that adds the setting to Settings &gt; Display (loaded through the
    /// File Transformation plugin).
    /// </summary>
    /// <returns>JavaScript.</returns>
    [HttpGet("ClientScript")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetClientScript()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("title-cards-client.js");
        return stream is null ? NotFound() : File(stream, "application/javascript; charset=utf-8");
    }

    private Guid? CurrentUser()
        => Guid.TryParse(User.FindFirst(UserIdClaim)?.Value, out var userId) && userId != Guid.Empty ? userId : null;
}

/// <summary>
/// A user's choice as the API reports it.
/// </summary>
public sealed class PreferenceDto
{
    /// <summary>Gets or sets a value indicating whether the user sees title cards.</summary>
    public bool ShowTitleCards { get; set; }

    /// <summary>Gets or sets a value indicating whether that comes from the server default.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Gets or sets a value indicating whether users who haven't chosen see cards.</summary>
    public bool ServerDefault { get; set; }

    /// <summary>Gets or sets a value indicating whether the user's cards crop covers to fill their tiles.</summary>
    public bool CropToFill { get; set; }

    /// <summary>Gets or sets a value indicating whether that comes from the server default.</summary>
    public bool CropIsDefault { get; set; }

    /// <summary>Gets or sets a value indicating whether users who haven't chosen get cropped covers.</summary>
    public bool ServerCropDefault { get; set; }
}

/// <summary>
/// A user's choice as the admin settings page shows it.
/// </summary>
public sealed class UserPreferenceDto
{
    /// <summary>Gets or sets the user's id.</summary>
    public Guid UserId { get; set; }

    /// <summary>Gets or sets the user's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the choice; null follows the server default.</summary>
    public bool? ShowTitleCards { get; set; }

    /// <summary>Gets or sets the crop choice; null follows the server default.</summary>
    public bool? CropToFill { get; set; }
}

/// <summary>
/// Changes to a user's choices. Fields left out (or null) stay as they are; fields named in
/// <see cref="Reset"/> return to the server default.
/// </summary>
public sealed class PreferenceRequest
{
    /// <summary>Gets or sets whether the user sees title cards.</summary>
    public bool? ShowTitleCards { get; set; }

    /// <summary>Gets or sets whether the user's cards crop covers to fill their tiles.</summary>
    public bool? CropToFill { get; set; }

    /// <summary>Gets or sets the fields to return to the server default ("ShowTitleCards", "CropToFill").</summary>
    public string[]? Reset { get; set; }

    /// <summary>
    /// Applies these changes to a user's current choices.
    /// </summary>
    /// <param name="current">The current choices.</param>
    /// <returns>The new choices.</returns>
    public UserPreference ApplyTo(UserPreference current)
    {
        bool Resets(string field) => Reset?.Contains(field, StringComparer.OrdinalIgnoreCase) == true;
        return new UserPreference
        {
            ShowTitleCards = Resets(nameof(ShowTitleCards)) ? null : ShowTitleCards ?? current.ShowTitleCards,
            CropToFill = Resets(nameof(CropToFill)) ? null : CropToFill ?? current.CropToFill,
        };
    }
}

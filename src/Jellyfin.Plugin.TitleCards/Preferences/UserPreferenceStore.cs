using System.Text.Json;

namespace Jellyfin.Plugin.TitleCards.Preferences;

/// <summary>
/// One user's Title Cards choices.
/// </summary>
public sealed class UserPreference
{
    /// <summary>
    /// Gets or sets whether the user sees title cards; null follows the server default.
    /// </summary>
    public bool? ShowTitleCards { get; set; }

    /// <summary>
    /// Gets or sets whether square and portrait covers are cropped to fill their tiles in the
    /// user's cards; null follows the server default.
    /// </summary>
    public bool? CropToFill { get; set; }
}

/// <summary>
/// Per-user choices, kept in the plugin's data folder (<c>user-preferences.json</c>) so they apply
/// in every client, whichever one the user set them in.
/// </summary>
public sealed class UserPreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Func<string> _path;
    private readonly object _lock = new();
    private Dictionary<Guid, UserPreference>? _preferences;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserPreferenceStore"/> class.
    /// </summary>
    public UserPreferenceStore()
        : this(() => Path.Combine(Plugin.Instance!.DataFolderPath, "user-preferences.json"))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UserPreferenceStore"/> class.
    /// </summary>
    /// <param name="path">Returns the file path.</param>
    public UserPreferenceStore(Func<string> path) => _path = path;

    /// <summary>
    /// A user's choices (a copy; unset fields follow the server defaults).
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <returns>The choices.</returns>
    public UserPreference Get(Guid userId)
    {
        lock (_lock)
        {
            return Preferences.TryGetValue(userId, out var preference)
                ? new UserPreference { ShowTitleCards = preference.ShowTitleCards, CropToFill = preference.CropToFill }
                : new UserPreference();
        }
    }

    /// <summary>
    /// Whether a user sees title cards.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="serverDefault">What users who haven't chosen see.</param>
    /// <returns>True for cards.</returns>
    public bool ShowsTitleCards(Guid userId, bool serverDefault) => Get(userId).ShowTitleCards ?? serverDefault;

    /// <summary>
    /// Whether a user's cards crop square and portrait covers to fill their tiles.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="serverDefault">What users who haven't chosen get.</param>
    /// <returns>True for cropped covers.</returns>
    public bool CropsToFill(Guid userId, bool serverDefault) => Get(userId).CropToFill ?? serverDefault;

    /// <summary>
    /// Saves a user's choices.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="preference">The choices.</param>
    public void Set(Guid userId, UserPreference preference)
    {
        lock (_lock)
        {
            if (preference.ShowTitleCards is null && preference.CropToFill is null)
            {
                Preferences.Remove(userId);
            }
            else
            {
                Preferences[userId] = new UserPreference { ShowTitleCards = preference.ShowTitleCards, CropToFill = preference.CropToFill };
            }

            var path = _path();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SafeFile.WriteAllText(path, JsonSerializer.Serialize(Preferences, JsonOptions));
        }
    }

    private Dictionary<Guid, UserPreference> Preferences
    {
        get
        {
            if (_preferences is null)
            {
                var path = _path();
                try
                {
                    _preferences = File.Exists(path)
                        ? JsonSerializer.Deserialize<Dictionary<Guid, UserPreference>>(File.ReadAllText(path), JsonOptions) ?? []
                        : [];
                }
                catch (JsonException)
                {
                    _preferences = [];
                }
            }

            return _preferences;
        }
    }
}

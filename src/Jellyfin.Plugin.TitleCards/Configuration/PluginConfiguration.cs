using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TitleCards.Configuration;

/// <summary>
/// Title Cards settings.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether images with text where the title goes are skipped.
    /// Needs the bundled text detection, which runs on Linux x64 and ARM64.
    /// </summary>
    public bool EnableTextFilter { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether sports listings are left out of Live TV cards
    /// (their team-logo graphics clash with the title and aren't caught by text detection).
    /// </summary>
    public bool ExcludeSports { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether cards are built as soon as a user's home settings
    /// change or a library is added, rather than waiting for the scheduled check.
    /// </summary>
    public bool ReactToChanges { get; set; } = true;

    /// <summary>
    /// Gets or sets views the plugin leaves alone.
    /// </summary>
    public Guid[] ExcludedViewIds { get; set; } = [];
}

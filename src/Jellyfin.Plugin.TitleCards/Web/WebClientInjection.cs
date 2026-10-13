using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TitleCards.Web;

/// <summary>
/// The page contents File Transformation passes to <see cref="WebClientInjection.AddScript"/>.
/// </summary>
public sealed class TransformationContents
{
    /// <summary>Gets or sets the file's contents.</summary>
    public string? Contents { get; set; }
}

/// <summary>
/// Adds the Title Cards setting script to the web client's index.html through the File
/// Transformation plugin, when that plugin is installed. Without it the plugin works the same,
/// with no setting in the web client (everyone gets the server default).
/// </summary>
/// <remarks>
/// Runs as a hidden startup task, like other plugins that use File Transformation, so that plugin
/// is fully loaded first.
/// </remarks>
public sealed class WebClientInjection : IScheduledTask, IConfigurableScheduledTask
{
    /// <summary>
    /// The file name to register. File Transformation runs one pipeline per file, preferring an
    /// exact name, so this joins the "index.html" pipeline other plugins use. That name is also
    /// tried as a pattern against other files (its "." matches "-" in chunks such as
    /// "user-home-index-html.&lt;hash&gt;.chunk.js"), so <see cref="AddScript"/> must leave anything
    /// that isn't an HTML page untouched.
    /// </summary>
    public const string FileNamePattern = "index.html";

    private const string ScriptTag = "<script defer src=\"../TitleCards/ClientScript\"></script>";
    private static readonly Guid TransformationId = Guid.Parse("4b0c7f0e-5d6a-4a35-9c4e-6f1d2b8e7a91");

    private readonly ILogger<WebClientInjection> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebClientInjection"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public WebClientInjection(ILogger<WebClientInjection> logger) => _logger = logger;

    /// <summary>
    /// File Transformation callback: adds the script tag to index.html.
    /// </summary>
    /// <param name="payload">The current page.</param>
    /// <returns>The page with the script tag.</returns>
    public static string AddScript(TransformationContents payload)
    {
        var html = payload.Contents ?? string.Empty;
        // Only ever touch an HTML page: anything else (a script that happens to match) is returned as is.
        var isPage = html.TrimStart().StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || html.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase);
        var end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return !isPage || end < 0 || html.Contains(ScriptTag, StringComparison.Ordinal) ? html : html.Insert(end, ScriptTag);
    }

    /// <inheritdoc />
    public string Name => "Add title cards setting to the web client";

    /// <inheritdoc />
    public string Key => "TitleCardsWebClient";

    /// <inheritdoc />
    public string Description => "Adds Settings > Display > Show title cards to the web client (needs File Transformation).";

    /// <inheritdoc />
    public string Category => "Title Cards";

    /// <inheritdoc />
    public bool IsHidden => true;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => false;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger };
    }

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            // File Transformation is loaded in its own context, so it is reached by reflection,
            // and its JObject parameter is built from its own Newtonsoft.Json.
            var assembly = AssemblyLoadContext.All
                .SelectMany(context => context.Assemblies)
                .FirstOrDefault(candidate => candidate.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) == true);
            var register = assembly?.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface")?.GetMethod("RegisterTransformation");
            var payloadType = register?.GetParameters().FirstOrDefault()?.ParameterType;
            var parse = payloadType?.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
            if (register is null || parse is null)
            {
                _logger.LogInformation("Title Cards: File Transformation is not installed, so the web client has no title cards setting; users see the server default");
                return Task.CompletedTask;
            }

            var payload = JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["id"] = TransformationId.ToString(),
                // A regular expression: anchored, so it can't match chunks like "user-home-index-html.<hash>.chunk.js".
                ["fileNamePattern"] = FileNamePattern,
                ["callbackAssembly"] = typeof(WebClientInjection).Assembly.FullName,
                ["callbackClass"] = typeof(WebClientInjection).FullName,
                ["callbackMethod"] = nameof(AddScript),
            });
            register.Invoke(null, [parse.Invoke(null, [payload])]);
            _logger.LogInformation("Title Cards: added the title cards setting to the web client (Settings > Display)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Title Cards: could not add the title cards setting to the web client");
        }

        return Task.CompletedTask;
    }
}

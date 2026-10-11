using System.Text.Json;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.TitleCards.Cards;

/// <summary>
/// What the plugin last did for a view.
/// </summary>
public sealed class CardRecord
{
    /// <summary>Gets or sets the view name at the time.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the names of the items in the tiles.</summary>
    public string[] Tiles { get; set; } = [];

    /// <summary>Gets or sets the Primary image path after the card was saved.</summary>
    public string? ImagePath { get; set; }

    /// <summary>Gets or sets the Primary image modification time after the card was saved.</summary>
    public DateTime? ImageModified { get; set; }

    /// <summary>Gets or sets a value indicating whether no card could be built (the image was left alone).</summary>
    public bool Skipped { get; set; }

    /// <summary>Gets or sets when the view was last handled.</summary>
    public DateTime CheckedAt { get; set; }
}

/// <summary>
/// Remembers which image each view had when the plugin last handled it, so new views and images
/// the server has since replaced can be told apart from the plugin's own cards.
/// </summary>
public sealed class CardStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _lock = new();
    private Dictionary<Guid, CardRecord>? _records;

    /// <summary>
    /// Initializes a new instance of the <see cref="CardStateStore"/> class.
    /// </summary>
    /// <param name="dataFolder">The plugin's data folder.</param>
    public CardStateStore(string dataFolder) => _path = Path.Combine(dataFolder, "title-cards-state.json");

    /// <summary>
    /// Whether a view has never been handled, or its image is no longer the one recorded.
    /// </summary>
    /// <param name="view">The view.</param>
    /// <returns>True when the view needs a card.</returns>
    public bool NeedsCard(BaseItem view)
    {
        lock (_lock)
        {
            if (!Records.TryGetValue(view.Id, out var record))
            {
                return true;
            }

            var image = view.GetImageInfo(ImageType.Primary, 0);
            return !string.Equals(record.ImagePath, image?.Path, StringComparison.Ordinal)
                || record.ImageModified != image?.DateModified;
        }
    }

    /// <summary>
    /// Records the view's current image after the plugin handled it.
    /// </summary>
    /// <param name="view">The view.</param>
    /// <param name="tiles">Names of the items in the tiles; empty when skipped.</param>
    public void Record(BaseItem view, IEnumerable<string> tiles)
    {
        var image = view.GetImageInfo(ImageType.Primary, 0);
        var names = tiles.ToArray();
        lock (_lock)
        {
            Records[view.Id] = new CardRecord
            {
                Title = view.Name,
                Tiles = names,
                ImagePath = image?.Path,
                ImageModified = image?.DateModified,
                Skipped = names.Length == 0,
                CheckedAt = DateTime.UtcNow,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Records, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
    }

    private Dictionary<Guid, CardRecord> Records
    {
        get
        {
            if (_records is null)
            {
                try
                {
                    _records = File.Exists(_path)
                        ? JsonSerializer.Deserialize<Dictionary<Guid, CardRecord>>(File.ReadAllText(_path), JsonOptions) ?? []
                        : [];
                }
                catch (JsonException)
                {
                    _records = [];
                }
            }

            return _records;
        }
    }
}

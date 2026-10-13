using System.Text.Json;
using MediaBrowser.Controller.Entities;

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

    /// <summary>
    /// Gets or sets the view's Primary image path after a version before 0.2 saved the card into
    /// it; null once that image has been handed back to Jellyfin (0.2 keeps cards to itself).
    /// </summary>
    public string? ImagePath { get; set; }

    /// <summary>Gets or sets that image's modification time.</summary>
    public DateTime? ImageModified { get; set; }

    /// <summary>Gets or sets a value indicating whether no card could be built (the image was left alone).</summary>
    public bool Skipped { get; set; }

    /// <summary>Gets or sets how many candidate images the view had.</summary>
    public int Candidates { get; set; }

    /// <summary>Gets or sets when the view was last handled.</summary>
    public DateTime CheckedAt { get; set; }
}

/// <summary>
/// Remembers what the plugin last did for each view, so the missing-cards check can tell new
/// views from ones already handled.
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
    /// Whether a view needs a card: never handled, given a card that is no longer stored, or
    /// empty when last checked (a new library is usually seen before its first scan fills it).
    /// Views whose images were all rejected wait for the nightly refresh, so the text detector
    /// isn't rerun on them every check.
    /// </summary>
    /// <param name="viewId">The view.</param>
    /// <param name="hasCard">Whether a card is stored for it.</param>
    /// <returns>True when the view needs a card.</returns>
    public bool NeedsCard(Guid viewId, bool hasCard)
    {
        lock (_lock)
        {
            if (!Records.TryGetValue(viewId, out var record))
            {
                return true;
            }

            return !hasCard && (!record.Skipped || record.Candidates == 0);
        }
    }

    /// <summary>
    /// Records what the plugin did for a view.
    /// </summary>
    /// <param name="view">The view.</param>
    /// <param name="tiles">Names of the items in the tiles; empty when skipped.</param>
    /// <param name="candidates">How many candidate images the view had.</param>
    public void Record(BaseItem view, IEnumerable<string> tiles, int candidates)
    {
        var names = tiles.ToArray();
        lock (_lock)
        {
            Records[view.Id] = new CardRecord
            {
                Title = view.Name,
                Tiles = names,
                Skipped = names.Length == 0,
                Candidates = candidates,
                CheckedAt = DateTime.UtcNow,
            };
            Save();
        }
    }

    /// <summary>
    /// Views whose own image slot still holds a card saved by a version before 0.2, with the
    /// image recorded at the time.
    /// </summary>
    /// <returns>View ids, image paths and modification times.</returns>
    public List<(Guid ViewId, string ImagePath, DateTime? ImageModified)> CardsInImageSlots()
    {
        lock (_lock)
        {
            return [.. Records
                .Where(entry => entry.Value.ImagePath is not null && !entry.Value.Skipped)
                .Select(entry => (entry.Key, entry.Value.ImagePath!, entry.Value.ImageModified))];
        }
    }

    /// <summary>
    /// Forgets that a view's image slot held a card (it has been handed back to Jellyfin).
    /// </summary>
    /// <param name="viewId">The view.</param>
    public void ForgetImageSlot(Guid viewId)
    {
        lock (_lock)
        {
            if (Records.TryGetValue(viewId, out var record))
            {
                record.ImagePath = null;
                record.ImageModified = null;
                Save();
            }
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        SafeFile.WriteAllText(_path, JsonSerializer.Serialize(Records, JsonOptions));
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

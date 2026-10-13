using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Jellyfin.Plugin.TitleCards.Variants;

/// <summary>
/// The plugin's own versions of view images (title cards), kept in the plugin's data folder as
/// <c>images/&lt;viewId&gt;/&lt;variant&gt;.png</c> rather than in the view's image slot, which stays
/// Jellyfin's.
/// </summary>
/// <remarks>
/// A variant reaches a client through its own image tag (<c>tc-&lt;variant&gt;-&lt;version&gt;</c>).
/// Image requests carry no user, but they do carry the tag, so the tag alone says which file to serve.
/// </remarks>
public sealed class VariantImageStore
{
    /// <summary>The title card.</summary>
    public const string Card = "card";

    private const string TagPrefix = "tc-";
    private static readonly string[] Extensions = [".png", ".jpg"];

    private readonly Func<string> _root;
    private readonly ConcurrentDictionary<(Guid ViewId, string Variant), Entry?> _index = new();
    private readonly object _viewsLock = new();
    private HashSet<Guid>? _views;

    /// <summary>
    /// Initializes a new instance of the <see cref="VariantImageStore"/> class.
    /// </summary>
    public VariantImageStore()
        : this(() => Path.Combine(Plugin.Instance!.DataFolderPath, "images"))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VariantImageStore"/> class.
    /// </summary>
    /// <param name="root">Returns the folder holding the per-view folders.</param>
    public VariantImageStore(Func<string> root) => _root = root;

    /// <summary>
    /// The stored file for a view's variant, if there is one.
    /// </summary>
    /// <param name="viewId">The view.</param>
    /// <param name="variant">The variant name.</param>
    /// <returns>The file path, or null.</returns>
    public string? Find(Guid viewId, string variant)
        => IsValidVariant(variant) && HasAny(viewId) ? Lookup(viewId, variant)?.Path : null;

    /// <summary>
    /// The image tag that asks for a view's variant, or null when the view has no such file.
    /// The version part is a hash of the file, so it changes exactly when the image does and
    /// clients never keep a stale copy.
    /// </summary>
    /// <param name="viewId">The view.</param>
    /// <param name="variant">The variant name.</param>
    /// <returns>The tag, or null.</returns>
    public string? TagFor(Guid viewId, string variant)
        => IsValidVariant(variant) && HasAny(viewId) ? Lookup(viewId, variant)?.Tag : null;

    /// <summary>
    /// Whether anything is stored for an item. Answered from memory, so replies listing many
    /// ordinary items cost no disk access.
    /// </summary>
    /// <param name="viewId">The item.</param>
    /// <returns>True when the item has a folder in the store.</returns>
    public bool HasAny(Guid viewId)
    {
        lock (_viewsLock)
        {
            _views ??= Directory.Exists(_root())
                ? [.. Directory.EnumerateDirectories(_root())
                    .Select(folder => Guid.TryParseExact(Path.GetFileName(folder), "N", out var id) ? id : Guid.Empty)
                    .Where(id => id != Guid.Empty)]
                : [];
            return _views.Contains(viewId);
        }
    }

    /// <summary>
    /// Stores (or replaces) a view's variant.
    /// </summary>
    /// <param name="viewId">The view.</param>
    /// <param name="variant">The variant name.</param>
    /// <param name="png">PNG bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task SaveAsync(Guid viewId, string variant, byte[] png, CancellationToken cancellationToken)
    {
        if (!IsValidVariant(variant))
        {
            throw new ArgumentException("Invalid variant name", nameof(variant));
        }

        var folder = Directory.CreateDirectory(FolderOf(viewId)).FullName;
        var path = Path.Combine(folder, variant + ".png");
        await SafeFile.WriteAllBytesAsync(path, png, cancellationToken).ConfigureAwait(false);
        _index[(viewId, variant)] = Load(viewId, variant);
        lock (_viewsLock)
        {
            _views?.Add(viewId);
        }
    }

    /// <summary>
    /// Deletes every stored variant of a view.
    /// </summary>
    /// <param name="viewId">The view.</param>
    public void DeleteAll(Guid viewId)
    {
        var folder = FolderOf(viewId);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        foreach (var key in _index.Keys.Where(key => key.ViewId == viewId))
        {
            _index.TryRemove(key, out _);
        }

        lock (_viewsLock)
        {
            _views?.Remove(viewId);
        }
    }

    /// <summary>
    /// The variant named by an image tag made by <see cref="TagFor"/>.
    /// </summary>
    /// <param name="tag">The tag from an image request.</param>
    /// <param name="variant">The variant name.</param>
    /// <returns>True when the tag is one of the plugin's.</returns>
    public static bool TryParseTag(string? tag, out string variant)
    {
        variant = string.Empty;
        if (tag is null || !tag.StartsWith(TagPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = tag[TagPrefix.Length..];
        var dash = rest.LastIndexOf('-');
        if (dash <= 0)
        {
            return false;
        }

        variant = rest[..dash];
        return IsValidVariant(variant);
    }

    private static bool IsValidVariant(string variant)
        => variant.Length is > 0 and <= 32 && variant.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c));

    private string FolderOf(Guid viewId) => Path.Combine(_root(), viewId.ToString("N"));

    private Entry? Lookup(Guid viewId, string variant) => _index.GetOrAdd((viewId, variant), key => Load(key.ViewId, key.Variant));

    private Entry? Load(Guid viewId, string variant)
    {
        var file = Extensions
            .Select(extension => new FileInfo(Path.Combine(FolderOf(viewId), variant + extension)))
            .FirstOrDefault(candidate => candidate.Exists);
        if (file is null)
        {
            return null;
        }

        using var stream = file.OpenRead();
        var version = Convert.ToHexStringLower(SHA256.HashData(stream).AsSpan(0, 8));
        return new Entry(file.FullName, $"{TagPrefix}{variant}-{version}");
    }

    private sealed record Entry(string Path, string Tag);
}

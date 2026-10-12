using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Common.Configuration;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;

namespace Jellyfin.Plugin.TitleCards.Cards;

/// <summary>
/// An image that could go into a tile.
/// </summary>
/// <param name="ItemId">The item the image belongs to.</param>
/// <param name="Name">Display name, used to avoid repeats (episodes use their series name).</param>
/// <param name="ImagePath">Local file path or URL of the image.</param>
/// <param name="RequireLandscape">Reject the image unless it is wider than about 4:3.</param>
public sealed record CardCandidate(Guid ItemId, string Name, string ImagePath, bool RequireLandscape = false);

/// <summary>
/// Chooses the artwork a view's card can draw from.
/// </summary>
public sealed class CandidateSource
{
    /// <summary>Items sampled from a library per run.</summary>
    public const int SampleSize = 300;

    // Which items supply images, by library type. Types not listed use DefaultItemTypes.
    private static readonly Dictionary<string, BaseItemKind[]> SourceItemTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tvshows"] = [BaseItemKind.Series],
        ["movies"] = [BaseItemKind.Movie],
        ["music"] = [BaseItemKind.MusicArtist, BaseItemKind.MusicAlbum],
        ["musicvideos"] = [BaseItemKind.MusicVideo],
        ["homevideos"] = [BaseItemKind.Video, BaseItemKind.Photo],
        ["photos"] = [BaseItemKind.Photo, BaseItemKind.Video],
        ["books"] = [BaseItemKind.Book, BaseItemKind.AudioBook],
        ["trailers"] = [BaseItemKind.Trailer],
    };

    private static readonly BaseItemKind[] DefaultItemTypes =
    [
        BaseItemKind.Series, BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.Video, BaseItemKind.MusicVideo,
        BaseItemKind.MusicArtist, BaseItemKind.MusicAlbum, BaseItemKind.Photo, BaseItemKind.Book,
        BaseItemKind.AudioBook, BaseItemKind.Playlist,
    ];

    // Recordings land in recording libraries as episodes, films or plain videos.
    private static readonly BaseItemKind[] RecordingItemTypes = [BaseItemKind.Series, BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.Video];

    private readonly ILibraryManager _libraryManager;
    private readonly IServerConfigurationManager _configurationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="CandidateSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="configurationManager">The server configuration manager.</param>
    public CandidateSource(ILibraryManager libraryManager, IServerConfigurationManager configurationManager)
    {
        _libraryManager = libraryManager;
        _configurationManager = configurationManager;
    }

    /// <summary>
    /// The view's type as Jellyfin reports it; libraries with no type are "mixed".
    /// </summary>
    /// <param name="view">The view.</param>
    /// <returns>The type, lower case.</returns>
    public static string ViewType(Folder view)
    {
        var type = view switch
        {
            UserView userView => userView.ViewType?.ToString(),
            CollectionFolder collection => collection.CollectionType?.ToString(),
            _ => null,
        };
        return string.IsNullOrEmpty(type) ? "mixed" : type.ToLowerInvariant();
    }

    /// <summary>
    /// Candidates for a view's card, most preferred first.
    /// </summary>
    /// <param name="view">The view.</param>
    /// <param name="user">A user who can see the view.</param>
    /// <param name="excludeSports">Skip sports listings for Live TV.</param>
    /// <returns>The candidates.</returns>
    public IReadOnlyList<CardCandidate> ForView(Folder view, User user, bool excludeSports)
    {
        var type = ViewType(view);
        if (type == "livetv")
        {
            return LiveTv(user, excludeSports);
        }

        if (type == "boxsets")
        {
            return Collections(view, user);
        }

        if (type == "playlists")
        {
            return Playlists(view, user);
        }

        if (IsRecordingLibrary(view))
        {
            // Use the recordings' own images first, then top up from what is airing so a
            // near-empty recording library still fills the card.
            var own = UniqueByName(ImageCandidates(Sample(view, user, RecordingItemTypes)), shuffle: false);
            return UniqueByName([.. own, .. LiveTv(user, excludeSports)], shuffle: false);
        }

        var itemTypes = SourceItemTypes.GetValueOrDefault(type, DefaultItemTypes);
        var items = Sample(view, user, itemTypes).Select(BookFor).DistinctBy(item => item.Id);
        return UniqueByName(ImageCandidates(items), shuffle: false);
    }

    /// <summary>
    /// Removes candidates whose name or image repeats an earlier one.
    /// </summary>
    /// <param name="candidates">Candidates.</param>
    /// <param name="shuffle">Shuffle first.</param>
    /// <returns>One candidate per name and image.</returns>
    public static List<CardCandidate> UniqueByName(IEnumerable<CardCandidate> candidates, bool shuffle = true)
    {
        var list = candidates.ToList();
        if (shuffle)
        {
            Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list));
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var images = new HashSet<string>(StringComparer.Ordinal);
        return list.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Name)
                && names.Add(candidate.Name.Trim())
                && images.Add(candidate.ImagePath))
            .ToList();
    }

    /// <summary>
    /// The item whose artwork stands for an audiobook: its book folder when the book is split
    /// into one item per chapter file (or the item has no image of its own), otherwise the item.
    /// </summary>
    /// <param name="item">A sampled item.</param>
    /// <returns>The item to take artwork from.</returns>
    private BaseItem BookFor(BaseItem item)
    {
        if (item is not AudioBook
            || item.GetParent() is not Folder book
            || book is CollectionFolder or UserRootFolder or AggregateFolder
            || book.GetImageInfo(ImageType.Primary, 0) is null)
        {
            return item;
        }

        if (item.GetImageInfo(ImageType.Primary, 0) is null)
        {
            return book;
        }

        // A folder holding several audiobook items is one book in chapters; a single file is a
        // book of its own, whose folder is more likely an author's.
        var siblings = _libraryManager.GetItemList(new InternalItemsQuery
        {
            ParentId = book.Id,
            IncludeItemTypes = [BaseItemKind.AudioBook],
            Limit = 2,
        });
        return siblings.Count > 1 ? book : item;
    }

    /// <summary>
    /// One image per item: backdrops and landscape images ahead of other shapes, each group shuffled.
    /// Episodes are named after their series so one show cannot fill the card.
    /// </summary>
    /// <param name="items">Items.</param>
    /// <returns>Candidates.</returns>
    public static List<CardCandidate> ImageCandidates(IEnumerable<BaseItem> items)
    {
        var landscape = new List<CardCandidate>();
        var other = new List<CardCandidate>();
        foreach (var item in items)
        {
            var name = item is Episode episode && !string.IsNullOrEmpty(episode.SeriesName) ? episode.SeriesName : item.Name;
            var backdrops = item.GetImages(ImageType.Backdrop).ToList();
            if (backdrops.Count > 0)
            {
                landscape.Add(new CardCandidate(item.Id, name, backdrops[Random.Shared.Next(backdrops.Count)].Path));
                continue;
            }

            var primary = item.GetImageInfo(ImageType.Primary, 0);
            if (primary is null)
            {
                continue;
            }

            var candidate = new CardCandidate(item.Id, name, primary.Path);
            (primary.Width > 0 && primary.Height > 0 && (double)primary.Width / primary.Height > 1.3 ? landscape : other).Add(candidate);
        }

        Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(landscape));
        Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(other));
        return [.. landscape, .. other];
    }

    /// <summary>
    /// Whether a library includes a Live TV recording folder (e.g. DVR).
    /// </summary>
    /// <param name="view">The view.</param>
    /// <returns>True for recording libraries.</returns>
    public bool IsRecordingLibrary(Folder view)
    {
        var options = _configurationManager.GetConfiguration<LiveTvOptions>("livetv");
        var roots = new[] { options.RecordingPath, options.MovieRecordingPath, options.SeriesRecordingPath }
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!.TrimEnd('/', '\\'))
            .ToList();
        if (roots.Count == 0)
        {
            return false;
        }

        var locations = _libraryManager.GetVirtualFolders()
            .FirstOrDefault(folder => Guid.TryParse(folder.ItemId, out var id) && id == view.Id)?.Locations ?? [];
        return locations.Any(location => roots.Any(root =>
            string.Equals(location.TrimEnd('/', '\\'), root, StringComparison.Ordinal)
            || location.StartsWith(root + "/", StringComparison.Ordinal)
            || location.StartsWith(root + "\\", StringComparison.Ordinal)));
    }

    private IReadOnlyList<BaseItem> Sample(Folder view, User user, BaseItemKind[] itemTypes)
        => view.GetItems(new InternalItemsQuery(user)
        {
            Recursive = true,
            IncludeItemTypes = itemTypes,
            OrderBy = [(ItemSortBy.Random, SortOrder.Ascending)],
            Limit = SampleSize,
        }).Items;

    private List<CardCandidate> LiveTv(User user, bool excludeSports)
    {
        var now = DateTime.UtcNow;
        var programs = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.LiveTvProgram],
                MaxStartDate = now,
                MinEndDate = now,
                Limit = 500,
            })
            .OfType<LiveTvProgram>()
            // Sports listings are team-logo matchup graphics, which text detection does not catch.
            .Where(program => !(excludeSports && program.IsSports == true))
            .ToList();
        var preferred = programs.Where(program => program.IsSeries || program.IsMovie).ToList();
        var pool = preferred.Count > 0 ? preferred : programs;
        return UniqueByName(pool
            .Select(program => (program, image: program.GetImageInfo(ImageType.Primary, 0)))
            .Where(entry => entry.image is not null)
            .Select(entry => new CardCandidate(entry.program.Id, entry.program.Name, entry.image!.Path, RequireLandscape: true)));
    }

    private List<CardCandidate> Collections(Folder view, User user)
    {
        // Few collections have their own backdrop, so add the films inside them. Take one image
        // per collection in turn so one large collection cannot fill the card.
        var groups = new List<List<CardCandidate>>();
        foreach (var boxSet in Sample(view, user, [BaseItemKind.BoxSet]).OfType<Folder>())
        {
            var members = boxSet.GetChildren(user, true).Take(200);
            var group = ImageCandidates([boxSet, .. members]);
            if (group.Count > 0)
            {
                groups.Add(group);
            }
        }

        return Interleave(groups);
    }

    private List<CardCandidate> Playlists(Folder view, User user)
    {
        // A playlist's own image is a mosaic of album covers that Jellyfin makes, which looks
        // busy in a collage tile. Use the art behind its contents instead: artists' backdrops,
        // album covers, series and films. A playlist with none of these uses its own image.
        var groups = new List<List<CardCandidate>>();
        foreach (var playlist in Sample(view, user, [BaseItemKind.Playlist]).OfType<Folder>())
        {
            var members = playlist.GetLinkedChildren().Where(member => member.IsVisible(user)).ToList();
            Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(members));
            var art = members.Take(200).SelectMany(ArtFor).DistinctBy(item => item.Id).ToList();
            var group = ImageCandidates(art);
            if (group.Count == 0)
            {
                group = ImageCandidates([playlist]);
            }

            if (group.Count > 0)
            {
                groups.Add(group);
            }
        }

        return Interleave(groups);
    }

    /// <summary>
    /// The items whose artwork stands for a playlist entry: a track's artist and album, an
    /// episode's series, or the item itself.
    /// </summary>
    private static IEnumerable<BaseItem> ArtFor(BaseItem member)
    {
        switch (member)
        {
            case Audio track:
                var album = track.AlbumEntity;
                if (album?.MusicArtist is { } artist)
                {
                    yield return artist;
                }

                if (album is not null)
                {
                    yield return album;
                }

                break;
            case Episode { Series: { } series }:
                yield return series;
                break;
            default:
                yield return member;
                break;
        }
    }

    /// <summary>
    /// Takes one candidate from each group in turn, so one large group cannot fill the card.
    /// </summary>
    private static List<CardCandidate> Interleave(List<List<CardCandidate>> groups)
    {
        Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(groups));
        var interleaved = new List<CardCandidate>();
        for (var i = 0; i < groups.Select(group => group.Count).DefaultIfEmpty(0).Max(); i++)
        {
            interleaved.AddRange(groups.Where(group => i < group.Count).Select(group => group[i]));
        }

        return UniqueByName(interleaved, shuffle: false);
    }
}

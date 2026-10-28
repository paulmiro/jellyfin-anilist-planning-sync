using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.AnimePlanningSync.Mapping;

/// <summary>
/// The ids of one library item relevant for matching. For seasons, TargetId is the parent series
/// and the TVDB/TMDB ids are the series' ids, since seasons are never favourited themselves.
/// </summary>
public record LibraryEntry(
    Guid TargetId,
    BaseItemKind Kind,
    string? AniList,
    string? AniDb,
    string? Tvdb,
    string? Tmdb,
    int? SeasonNumber);

public class LibraryMatcher
{
    private readonly ILibraryManager _libraryManager;

    public LibraryMatcher(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Returns the ids of the series and movies visible to <paramref name="user"/> that match any of the given AniList ids.
    /// </summary>
    public HashSet<Guid> FindTargets(User user, IReadOnlySet<int> aniListIds, MappingIndex? mapping)
    {
        var targets = new HashSet<Guid>();
        if (aniListIds.Count == 0)
        {
            return targets;
        }

        var items = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Series, BaseItemKind.Season, BaseItemKind.Movie],
            Recursive = true,
            IsVirtualItem = false
        });

        foreach (var item in items)
        {
            var entry = ToEntry(item);
            if (entry is not null && !targets.Contains(entry.TargetId) && GetAniListIds(entry, mapping).Any(aniListIds.Contains))
            {
                targets.Add(entry.TargetId);
            }
        }

        return targets;
    }

    public static IEnumerable<int> GetAniListIds(LibraryEntry entry, MappingIndex? mapping)
    {
        if (TryParse(entry.AniList, out var aniListId))
        {
            yield return aniListId;
        }

        if (mapping is null)
        {
            yield break;
        }

        if (TryParse(entry.AniDb, out var aniDbId))
        {
            foreach (var id in mapping.ByAniDb(aniDbId))
            {
                yield return id;
            }
        }

        if (entry.Kind == BaseItemKind.Movie)
        {
            if (TryParse(entry.Tmdb, out var tmdbMovie))
            {
                foreach (var id in mapping.ByTmdbMovie(tmdbMovie))
                {
                    yield return id;
                }
            }

            yield break;
        }

        var season = entry.Kind == BaseItemKind.Season ? entry.SeasonNumber : MappingIndex.WholeShow;
        // Season 0 holds specials, which would match too broadly; seasons without a number can't be mapped.
        if (season is null || season == 0)
        {
            yield break;
        }

        if (TryParse(entry.Tvdb, out var tvdb))
        {
            foreach (var id in mapping.ByTvdbSeason(tvdb, season.Value))
            {
                yield return id;
            }
        }

        if (TryParse(entry.Tmdb, out var tmdb))
        {
            foreach (var id in mapping.ByTmdbTvSeason(tmdb, season.Value))
            {
                yield return id;
            }
        }
    }

    internal static LibraryEntry? ToEntry(BaseItem item)
    {
        switch (item)
        {
            case Series series:
                return new LibraryEntry(
                    series.Id,
                    BaseItemKind.Series,
                    GetProviderId(series, "AniList"),
                    GetProviderId(series, "AniDB"),
                    GetProviderId(series, nameof(MetadataProvider.Tvdb)),
                    GetProviderId(series, nameof(MetadataProvider.Tmdb)),
                    null);
            case Season season:
                var parent = season.Series;
                if (parent is null)
                {
                    return null;
                }

                return new LibraryEntry(
                    parent.Id,
                    BaseItemKind.Season,
                    GetProviderId(season, "AniList"),
                    GetProviderId(season, "AniDB"),
                    GetProviderId(parent, nameof(MetadataProvider.Tvdb)),
                    GetProviderId(parent, nameof(MetadataProvider.Tmdb)),
                    season.IndexNumber);
            case Movie movie:
                return new LibraryEntry(
                    movie.Id,
                    BaseItemKind.Movie,
                    GetProviderId(movie, "AniList"),
                    GetProviderId(movie, "AniDB"),
                    null,
                    GetProviderId(movie, nameof(MetadataProvider.Tmdb)),
                    null);
            default:
                return null;
        }
    }

    // Provider id keys differ in casing between metadata plugins (e.g. "AniDB" vs "AniDb").
    private static string? GetProviderId(BaseItem item, string name)
        => item.ProviderIds?.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool TryParse(string? value, out int result)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
}

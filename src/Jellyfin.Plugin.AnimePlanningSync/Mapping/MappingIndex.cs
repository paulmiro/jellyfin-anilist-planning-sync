using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Jellyfin.Plugin.AnimePlanningSync.Mapping;

/// <summary>
/// Lookup tables built from Fribb/anime-lists' anime-list-full.json.
/// </summary>
public class MappingIndex
{
    // Season key used for entries that map a whole TVDB/TMDB show without season information.
    internal const int WholeShow = -1;

    private readonly Dictionary<int, List<int>> _byAniDb = [];
    private readonly Dictionary<(int Id, int Season), List<int>> _byTvdbSeason = [];
    private readonly Dictionary<(int Id, int Season), List<int>> _byTmdbTvSeason = [];
    private readonly Dictionary<int, List<int>> _byTmdbMovie = [];

    public int Count { get; private set; }

    public static MappingIndex Parse(Stream json)
    {
        var index = new MappingIndex();
        using var doc = JsonDocument.Parse(json);
        foreach (var entry in doc.RootElement.EnumerateArray())
        {
            if (!TryGetInt(entry, "anilist_id", out var aniListId))
            {
                continue;
            }

            index.Count++;

            if (TryGetInt(entry, "anidb_id", out var aniDbId))
            {
                Add(index._byAniDb, aniDbId, aniListId);
            }

            entry.TryGetProperty("season", out var season);

            if (TryGetInt(entry, "tvdb_id", out var tvdbId))
            {
                var seasonNumber = season.ValueKind == JsonValueKind.Object && TryGetInt(season, "tvdb", out var s) ? s : WholeShow;
                Add(index._byTvdbSeason, (tvdbId, seasonNumber), aniListId);
            }

            if (entry.TryGetProperty("themoviedb_id", out var tmdb) && tmdb.ValueKind == JsonValueKind.Object)
            {
                if (TryGetInt(tmdb, "tv", out var tmdbTv))
                {
                    var seasonNumber = season.ValueKind == JsonValueKind.Object && TryGetInt(season, "tmdb", out var s) ? s : WholeShow;
                    Add(index._byTmdbTvSeason, (tmdbTv, seasonNumber), aniListId);
                }

                if (tmdb.TryGetProperty("movie", out var movies))
                {
                    foreach (var movieId in IntValues(movies))
                    {
                        Add(index._byTmdbMovie, movieId, aniListId);
                    }
                }
            }
        }

        return index;
    }

    public IEnumerable<int> ByAniDb(int aniDbId) => Get(_byAniDb, aniDbId);

    public IEnumerable<int> ByTvdbSeason(int tvdbId, int season) => Get(_byTvdbSeason, (tvdbId, season));

    public IEnumerable<int> ByTmdbTvSeason(int tmdbId, int season) => Get(_byTmdbTvSeason, (tmdbId, season));

    public IEnumerable<int> ByTmdbMovie(int tmdbId) => Get(_byTmdbMovie, tmdbId);

    private static IEnumerable<int> Get<TKey>(Dictionary<TKey, List<int>> map, TKey key)
        where TKey : notnull
        => map.TryGetValue(key, out var values) ? values : Enumerable.Empty<int>();

    private static void Add<TKey>(Dictionary<TKey, List<int>> map, TKey key, int value)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }

        list.Add(value);
    }

    private static bool TryGetInt(JsonElement element, string property, out int value)
    {
        value = 0;
        return element.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out value);
    }

    private static IEnumerable<int> IntValues(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var single))
        {
            yield return single;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var value))
                {
                    yield return value;
                }
            }
        }
    }
}

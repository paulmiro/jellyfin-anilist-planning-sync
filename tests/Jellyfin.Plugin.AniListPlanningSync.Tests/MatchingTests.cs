using System.Text;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AniListPlanningSync.Mapping;
using Xunit;

namespace Jellyfin.Plugin.AniListPlanningSync.Tests;

public class MatchingTests
{
    // Excerpt in the format of Fribb/anime-lists' anime-list-full.json.
    private const string MappingJson = """
        [
          { "type": "TV", "anidb_id": 1, "anilist_id": 290, "themoviedb_id": { "tv": 26209 }, "tvdb_id": 72025, "season": { "tvdb": 1, "tmdb": 1 } },
          { "type": "OVA", "anidb_id": 3, "anilist_id": 1225, "themoviedb_id": { "tv": 62913 }, "tvdb_id": 70973, "season": { "tvdb": 2, "tmdb": 2 } },
          { "type": "SPECIAL", "anidb_id": 5, "anilist_id": 555, "tvdb_id": 70973, "season": { "tvdb": 0 } },
          { "type": "TV", "anidb_id": 19, "anilist_id": 197, "themoviedb_id": { "tv": 34684 }, "tvdb_id": 83692 },
          { "type": "MOVIE", "anidb_id": 7, "anilist_id": 164, "themoviedb_id": { "movie": [128] } },
          { "type": "TV", "anidb_id": 8, "mal_id": 8 }
        ]
        """;

    private static readonly MappingIndex Mapping = MappingIndex.Parse(new MemoryStream(Encoding.UTF8.GetBytes(MappingJson)));
    private static readonly Guid SeriesId = Guid.NewGuid();

    [Fact]
    public void ParsesEntriesWithAniListId()
    {
        Assert.Equal(5, Mapping.Count);
        Assert.Equal([290], Mapping.ByAniDb(1));
        Assert.Equal([1225], Mapping.ByTvdbSeason(70973, 2));
        Assert.Equal([164], Mapping.ByTmdbMovie(128));
        Assert.Equal([197], Mapping.ByTvdbSeason(83692, MappingIndex.WholeShow));
    }

    [Fact]
    public void DirectAniListIdIsUsedWithoutMapping()
    {
        var entry = new LibraryEntry(SeriesId, BaseItemKind.Series, "21", null, null, null, null);
        Assert.Equal([21], LibraryMatcher.GetAniListIds(entry, null));
    }

    [Fact]
    public void SeasonMatchesViaTvdbSeasonAndTargetsSeries()
    {
        var season = new LibraryEntry(SeriesId, BaseItemKind.Season, null, null, "70973", null, 2);

        Assert.Contains(1225, LibraryMatcher.GetAniListIds(season, Mapping));
        Assert.Equal(SeriesId, season.TargetId);
    }

    [Fact]
    public void SeasonWithOwnAniListIdMatches()
    {
        var season = new LibraryEntry(SeriesId, BaseItemKind.Season, "1225", null, null, null, 2);
        Assert.Equal([1225], LibraryMatcher.GetAniListIds(season, null));
    }

    [Fact]
    public void SpecialsSeasonIsNotMapped()
    {
        var specials = new LibraryEntry(SeriesId, BaseItemKind.Season, null, null, "70973", null, 0);
        Assert.Empty(LibraryMatcher.GetAniListIds(specials, Mapping));
    }

    [Fact]
    public void SeriesOnlyMatchesWholeShowTvdbEntries()
    {
        var withSeasons = new LibraryEntry(SeriesId, BaseItemKind.Series, null, null, "70973", null, null);
        var wholeShow = new LibraryEntry(SeriesId, BaseItemKind.Series, null, null, "83692", "34684", null);

        Assert.Empty(LibraryMatcher.GetAniListIds(withSeasons, Mapping));
        Assert.Contains(197, LibraryMatcher.GetAniListIds(wholeShow, Mapping));
    }

    [Fact]
    public void AniDbIdIsMapped()
    {
        var series = new LibraryEntry(SeriesId, BaseItemKind.Series, null, "1", null, null, null);
        Assert.Equal([290], LibraryMatcher.GetAniListIds(series, Mapping));
    }

    [Fact]
    public void MovieMatchesViaTmdb()
    {
        var movie = new LibraryEntry(Guid.NewGuid(), BaseItemKind.Movie, null, null, null, "128", null);
        Assert.Equal([164], LibraryMatcher.GetAniListIds(movie, Mapping));
    }

    [Fact]
    public void MappingIsIgnoredWhenDisabled()
    {
        var series = new LibraryEntry(SeriesId, BaseItemKind.Series, null, "1", "72025", null, null);
        Assert.Empty(LibraryMatcher.GetAniListIds(series, null));
    }
}

using Jellyfin.Plugin.AniListPlanningSync.AniSync;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.AniListPlanningSync.Tests;

public class AniSyncTokenReaderTests
{
    private static readonly Guid UserA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // Shape as written by Jellyfin's XmlSerializer for jellyfin-ani-sync's PluginConfiguration.
    private const string SampleXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
          <UserConfig>
            <UserConfig>
              <PlanToWatchOnly>true</PlanToWatchOnly>
              <RewatchCompleted>true</RewatchCompleted>
              <LibraryToCheck />
              <UserId>11111111-1111-1111-1111-111111111111</UserId>
              <UserApiAuth>
                <UserApiAuth>
                  <Name>Mal</Name>
                  <AccessToken>mal-token</AccessToken>
                  <RefreshToken>mal-refresh</RefreshToken>
                </UserApiAuth>
                <UserApiAuth>
                  <Name>AniList</Name>
                  <AccessToken>anilist-token-a</AccessToken>
                </UserApiAuth>
              </UserApiAuth>
              <KeyPairs />
            </UserConfig>
            <UserConfig>
              <UserId>22222222-2222-2222-2222-222222222222</UserId>
              <UserApiAuth>
                <UserApiAuth>
                  <Name>Kitsu</Name>
                  <AccessToken>kitsu-token</AccessToken>
                </UserApiAuth>
              </UserApiAuth>
            </UserConfig>
          </UserConfig>
          <ProviderApiAuth>
            <ProviderApiAuth>
              <Name>AniList</Name>
              <ClientId>1234</ClientId>
              <ClientSecret>secret</ClientSecret>
            </ProviderApiAuth>
          </ProviderApiAuth>
          <callbackUrl>http://localhost</callbackUrl>
        </PluginConfiguration>
        """;

    private static AniSyncTokenReader Reader(string? content)
    {
        var path = Path.Combine(TestHelpers.TempDir(), AniSyncTokenReader.ConfigFileName);
        if (content is not null)
        {
            File.WriteAllText(path, content);
        }

        return new AniSyncTokenReader(path, NullLogger<AniSyncTokenReader>.Instance);
    }

    [Fact]
    public void ReadsAniListTokenOnly()
    {
        var reader = Reader(SampleXml);

        Assert.Equal("anilist-token-a", reader.GetToken(UserA));
        Assert.Null(reader.GetToken(UserB));
        Assert.Single(reader.ReadTokens());
    }

    [Fact]
    public void MissingFileYieldsNoTokens()
    {
        Assert.Empty(Reader(null).ReadTokens());
    }

    [Fact]
    public void BrokenFileYieldsNoTokens()
    {
        Assert.Empty(Reader("<PluginConfiguration><UserConfig>").ReadTokens());
    }

    [Fact]
    public void DoesNotModifyFile()
    {
        var path = Path.Combine(TestHelpers.TempDir(), AniSyncTokenReader.ConfigFileName);
        File.WriteAllText(path, SampleXml);
        var before = File.GetLastWriteTimeUtc(path);

        new AniSyncTokenReader(path, NullLogger<AniSyncTokenReader>.Instance).ReadTokens();

        Assert.Equal(SampleXml, File.ReadAllText(path));
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }
}

using System.Net;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.AniListPlanningSync.AniList;
using Jellyfin.Plugin.AniListPlanningSync.AniSync;
using Jellyfin.Plugin.AniListPlanningSync.Configuration;
using Jellyfin.Plugin.AniListPlanningSync.Mapping;
using Jellyfin.Plugin.AniListPlanningSync.Sync;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;
using static Jellyfin.Plugin.AniListPlanningSync.Tests.TestHelpers;

namespace Jellyfin.Plugin.AniListPlanningSync.Tests;

public class TestConfiguration : IConfigurationAccessor
{
    public PluginConfiguration Configuration { get; } = new() { UseMappingFallback = false };

    public int Saves { get; private set; }

    public void Save() => Saves++;
}

public class SyncTests
{
    private readonly FakeHandler _handler = new();
    private readonly TestConfiguration _config = new();
    private readonly IUserManager _userManager = Substitute.For<IUserManager>();
    private readonly ILibraryManager _libraryManager = Substitute.For<ILibraryManager>();
    private readonly IUserDataManager _userDataManager = Substitute.For<IUserDataManager>();
    private readonly User _user = new("tester", "auth", "reset");
    private readonly string _dir = TempDir();
    private readonly AniListClient _client;
    private readonly FakeTimeProvider _time;
    private readonly FavouriteSyncService _service;
    private readonly UserSettingsService _settings;

    public SyncTests()
    {
        (_client, _time) = CreateClient(_handler);
        _userManager.GetUserById(_user.Id).Returns(_user);
        _libraryManager.GetItemList(Arg.Any<InternalItemsQuery>()).Returns([]);

        var paths = Substitute.For<IApplicationPaths>();
        paths.CachePath.Returns(_dir);
        var aniSync = new AniSyncTokenReader(Path.Combine(_dir, AniSyncTokenReader.ConfigFileName), NullLogger<AniSyncTokenReader>.Instance);

        _service = new FavouriteSyncService(
            _client,
            aniSync,
            new AnimeMappingStore(ClientFactory(_handler), paths, NullLogger<AnimeMappingStore>.Instance),
            new LibraryMatcher(_libraryManager),
            new SyncStateStore(Path.Combine(_dir, "state.json"), NullLogger<SyncStateStore>.Instance),
            _config,
            _userManager,
            _libraryManager,
            _userDataManager,
            NullLogger<FavouriteSyncService>.Instance);
        _settings = new UserSettingsService(_client, aniSync, _config, _userManager);
    }

    private void WriteAniSyncToken(Guid userId, string token)
    {
        File.WriteAllText(Path.Combine(_dir, AniSyncTokenReader.ConfigFileName), $"""
            <PluginConfiguration><UserConfig><UserConfig>
              <UserId>{userId}</UserId>
              <UserApiAuth><UserApiAuth><Name>AniList</Name><AccessToken>{token}</AccessToken></UserApiAuth></UserApiAuth>
            </UserConfig></UserConfig></PluginConfiguration>
            """);
    }

    private (BaseItem Item, UserItemData Data) AddItem(bool isFavorite = false)
    {
        var item = new Series { Id = Guid.NewGuid(), Name = "Show" };
        var data = new UserItemData { Key = item.Id.ToString(), IsFavorite = isFavorite };
        _libraryManager.GetItemById(item.Id).Returns(item);
        _userDataManager.GetUserData(_user, item).Returns(data);
        return (item, data);
    }

    private void AssertNeverUnfavourited()
    {
        foreach (var call in _userDataManager.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IUserDataManager.SaveUserData)))
        {
            var args = call.GetArguments();
            if (args[2] is UserItemData data)
            {
                Assert.True(data.IsFavorite);
            }
        }
    }

    [Fact]
    public void FavouritesMatchedItemOnce()
    {
        var (item, data) = AddItem();
        var state = new UserSyncState();

        Assert.Equal(1, _service.ApplyFavourites(_user, [item.Id], state, CancellationToken.None));
        Assert.True(data.IsFavorite);
        _userDataManager.Received(1).SaveUserData(_user, item, data, UserDataSaveReason.UpdateUserRating, Arg.Any<CancellationToken>());
        Assert.Contains(item.Id, state.AppliedItemIds);
    }

    [Fact]
    public void ManuallyRemovedFavouriteIsNotReapplied()
    {
        var (item, data) = AddItem();
        var state = new UserSyncState();
        _service.ApplyFavourites(_user, [item.Id], state, CancellationToken.None);

        data.IsFavorite = false; // user removes the favourite in Jellyfin
        _userDataManager.ClearReceivedCalls();

        Assert.Equal(0, _service.ApplyFavourites(_user, [item.Id], state, CancellationToken.None));
        Assert.False(data.IsFavorite);
        _userDataManager.DidNotReceiveWithAnyArgs().SaveUserData(default!, default!, default(UserItemData)!, default, default);
    }

    [Fact]
    public void ExistingFavouriteIsRecordedWithoutWriting()
    {
        var (item, _) = AddItem(isFavorite: true);
        var state = new UserSyncState();

        Assert.Equal(0, _service.ApplyFavourites(_user, [item.Id], state, CancellationToken.None));
        Assert.Contains(item.Id, state.AppliedItemIds);
        _userDataManager.DidNotReceiveWithAnyArgs().SaveUserData(default!, default!, default(UserItemData)!, default, default);
    }

    [Fact]
    public void NeverRemovesFavourites()
    {
        var items = Enumerable.Range(0, 5).Select(i => AddItem(i % 2 == 0)).ToList();
        var state = new UserSyncState();

        _service.ApplyFavourites(_user, items.Select(i => i.Item.Id), state, CancellationToken.None);

        Assert.All(items, i => Assert.True(i.Data.IsFavorite));
        AssertNeverUnfavourited();
    }

    [Fact]
    public async Task UserWithAniSyncLoginButNoOptInIsNotSynced()
    {
        WriteAniSyncToken(_user.Id, "token");

        await RunWithTime(_time, () => _service.SyncAllAsync(null, CancellationToken.None));

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task UserSetToOffIsNotSynced()
    {
        WriteAniSyncToken(_user.Id, "token");
        _config.Configuration.Users.Add(new UserSettings { UserId = _user.Id, Source = LoginSource.Off });

        await RunWithTime(_time, () => _service.SyncAllAsync(null, CancellationToken.None));

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task OptedInAniSyncUserIsSyncedWithToken()
    {
        WriteAniSyncToken(_user.Id, "token");
        _config.Configuration.Users.Add(new UserSettings { UserId = _user.Id, Source = LoginSource.AniSync });
        _handler.EnqueueJson(new { data = new { Viewer = new { id = 42, name = "me" } } });
        _handler.EnqueueJson(PlanningResponse(false, List(Entry(1, 10))));

        await RunWithTime(_time, () => _service.SyncAllAsync(null, CancellationToken.None));

        Assert.Equal(2, _handler.Requests.Count);
        Assert.All(_handler.Requests, r => Assert.Equal("token", r.Request.Headers.Authorization?.Parameter));
        Assert.Contains("\"userId\":42", _handler.Requests[1].Body);
    }

    [Fact]
    public async Task QuickCheckSkipsFullFetchWhenUnchanged()
    {
        _config.Configuration.Users.Add(new UserSettings { UserId = _user.Id, Source = LoginSource.Username, AniListUserName = "public" });
        _handler.EnqueueJson(PlanningResponse(false, List(Entry(1, 10))));
        await RunWithTime(_time, () => _service.SyncAllAsync(null, CancellationToken.None));

        _handler.EnqueueJson(PlanningResponse(true, List(Entry(1, 10))));
        await RunWithTime(_time, () => _service.QuickCheckAllAsync(null, CancellationToken.None));
        Assert.Equal(2, _handler.Requests.Count);

        _handler.EnqueueJson(PlanningResponse(true, List(Entry(2, 20))));
        _handler.EnqueueJson(PlanningResponse(false, List(Entry(2, 20), Entry(1, 10))));
        await RunWithTime(_time, () => _service.QuickCheckAllAsync(null, CancellationToken.None));
        Assert.Equal(4, _handler.Requests.Count);
    }

    [Fact]
    public async Task AniSyncWithoutTokenIsRejectedAndNotSaved()
    {
        var result = await _settings.ValidateAndSaveAsync(
            new UserSettings { UserId = _user.Id, Source = LoginSource.AniSync }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(_config.Configuration.Users);
        Assert.Equal(0, _config.Saves);
    }

    [Fact]
    public async Task InvalidAniSyncTokenIsRejectedAndNotSaved()
    {
        WriteAniSyncToken(_user.Id, "expired");
        _handler.Enqueue(HttpStatusCode.BadRequest, """{"errors":[{"message":"Invalid token","status":400}],"data":null}""");

        var result = await RunWithTime(_time, () => _settings.ValidateAndSaveAsync(
            new UserSettings { UserId = _user.Id, Source = LoginSource.AniSync }, CancellationToken.None));

        Assert.False(result.Success);
        Assert.Contains("Invalid token", result.Message);
        Assert.Empty(_config.Configuration.Users);
    }

    [Fact]
    public async Task ValidAniSyncTokenIsSaved()
    {
        WriteAniSyncToken(_user.Id, "good");
        _handler.EnqueueJson(new { data = new { Viewer = new { id = 42, name = "me" } } });

        var result = await RunWithTime(_time, () => _settings.ValidateAndSaveAsync(
            new UserSettings { UserId = _user.Id, Source = LoginSource.AniSync }, CancellationToken.None));

        Assert.True(result.Success);
        Assert.Equal(LoginSource.AniSync, Assert.Single(_config.Configuration.Users).Source);
        Assert.Equal(1, _config.Saves);
    }

    [Fact]
    public async Task PrivateUsernameIsRejectedAndNotSaved()
    {
        _handler.Enqueue(HttpStatusCode.NotFound, """{"errors":[{"message":"Private User","status":404}],"data":{"MediaListCollection":null}}""");

        var result = await RunWithTime(_time, () => _settings.ValidateAndSaveAsync(
            new UserSettings { UserId = _user.Id, Source = LoginSource.Username, AniListUserName = "hidden" }, CancellationToken.None));

        Assert.False(result.Success);
        Assert.Empty(_config.Configuration.Users);
        Assert.Null(_handler.Requests[0].Request.Headers.Authorization);
    }

    [Fact]
    public async Task EmptyUsernameIsRejectedWithoutRequest()
    {
        var result = await _settings.ValidateAndSaveAsync(
            new UserSettings { UserId = _user.Id, Source = LoginSource.Username, AniListUserName = "  " }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task PublicUsernameIsSavedAndReplacesPreviousEntry()
    {
        _config.Configuration.Users.Add(new UserSettings { UserId = _user.Id, Source = LoginSource.AniSync });
        _handler.EnqueueJson(PlanningResponse(false, List(Entry(1, 1))));

        var result = await RunWithTime(_time, () => _settings.ValidateAndSaveAsync(
            new UserSettings { UserId = _user.Id, Source = LoginSource.Username, AniListUserName = " public " }, CancellationToken.None));

        Assert.True(result.Success);
        var saved = Assert.Single(_config.Configuration.Users);
        Assert.Equal(LoginSource.Username, saved.Source);
        Assert.Equal("public", saved.AniListUserName);
    }
}

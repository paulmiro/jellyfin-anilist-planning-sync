using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AniListPlanningSync.Configuration;
using Jellyfin.Plugin.AniListPlanningSync.Sync;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AniListPlanningSync.Api;

public class UserSettingsDto
{
    public string Source { get; set; } = nameof(LoginSource.Off);

    public string? AniListUserName { get; set; }
}

[ApiController]
[Route("AniListPlanningSync")]
[Authorize(Policy = Policies.RequiresElevation)]
public class AniListPlanningSyncController : ControllerBase
{
    private readonly UserSettingsService _settings;
    private readonly FavouriteSyncService _sync;
    private readonly ILogger<AniListPlanningSyncController> _logger;

    public AniListPlanningSyncController(
        UserSettingsService settings,
        FavouriteSyncService sync,
        ILogger<AniListPlanningSyncController> logger)
    {
        _settings = settings;
        _sync = sync;
        _logger = logger;
    }

    [HttpGet("Users/{userId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<UserSettingsDto> GetUser([FromRoute] Guid userId)
    {
        var settings = _settings.Get(userId);
        return new UserSettingsDto
        {
            Source = settings.Source.ToString(),
            AniListUserName = settings.AniListUserName
        };
    }

    [HttpPost("Users/{userId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ValidationResult>> SaveUser(
        [FromRoute] Guid userId,
        [FromBody] UserSettingsDto dto,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<LoginSource>(dto.Source, true, out var source))
        {
            return BadRequest(new ValidationResult(false, "Unknown login source."));
        }

        var settings = new UserSettings { UserId = userId, Source = source, AniListUserName = dto.AniListUserName };
        var result = await _settings.ValidateAndSaveAsync(settings, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        if (settings.IsEnabled)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _sync.SyncUserAsync(userId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Initial sync for user {UserId} failed", userId);
                }
            });
        }

        return result;
    }
}

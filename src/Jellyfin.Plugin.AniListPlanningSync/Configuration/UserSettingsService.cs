using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.AniListPlanningSync.AniList;
using Jellyfin.Plugin.AniListPlanningSync.AniSync;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.AniListPlanningSync.Configuration;

public record ValidationResult(bool Success, string Message);

/// <summary>
/// Validates per-user settings against AniList and only persists them if they are usable.
/// </summary>
public class UserSettingsService
{
    private readonly AniListClient _aniList;
    private readonly AniSyncTokenReader _aniSync;
    private readonly IConfigurationAccessor _configuration;
    private readonly IUserManager _userManager;
    private readonly object _saveLock = new();

    public UserSettingsService(
        AniListClient aniList,
        AniSyncTokenReader aniSync,
        IConfigurationAccessor configuration,
        IUserManager userManager)
    {
        _aniList = aniList;
        _aniSync = aniSync;
        _configuration = configuration;
        _userManager = userManager;
    }

    public UserSettings Get(Guid userId)
        => _configuration.GetUser(userId) ?? new UserSettings { UserId = userId };

    public async Task<ValidationResult> ValidateAndSaveAsync(UserSettings settings, CancellationToken cancellationToken)
    {
        var result = await ValidateAsync(settings, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return result;
        }

        lock (_saveLock)
        {
            var users = _configuration.Configuration.Users;
            users.RemoveAll(u => u.UserId == settings.UserId);
            users.Add(new UserSettings
            {
                UserId = settings.UserId,
                Source = settings.Source,
                AniListUserName = settings.AniListUserName?.Trim()
            });
            _configuration.Save();
        }

        return result;
    }

    public async Task<ValidationResult> ValidateAsync(UserSettings settings, CancellationToken cancellationToken)
    {
        if (_userManager.GetUserById(settings.UserId) is null)
        {
            return new ValidationResult(false, "Unknown Jellyfin user.");
        }

        switch (settings.Source)
        {
            case LoginSource.Off:
                return new ValidationResult(true, "Sync disabled for this user.");

            case LoginSource.AniSync:
                var token = _aniSync.GetToken(settings.UserId);
                if (token is null)
                {
                    return new ValidationResult(false, "Ani-Sync has no AniList login for this user. Log in to AniList in the Ani-Sync plugin first.");
                }

                try
                {
                    var viewer = await _aniList.GetViewerAsync(token, cancellationToken).ConfigureAwait(false);
                    return new ValidationResult(true, $"Saved. Using the Ani-Sync login of AniList user \"{viewer.Name}\".");
                }
                catch (AniListException ex)
                {
                    return new ValidationResult(false, $"The AniList login stored by Ani-Sync is not valid: {ex.Message}");
                }

            case LoginSource.Username:
                var userName = settings.AniListUserName?.Trim();
                if (string.IsNullOrEmpty(userName))
                {
                    return new ValidationResult(false, "Please enter an AniList username.");
                }

                try
                {
                    // Deliberately unauthenticated: this succeeds only if the list is publicly visible.
                    await _aniList.GetPlanningMarkerAsync(new AniListUserRef(null, userName, null), cancellationToken).ConfigureAwait(false);
                    return new ValidationResult(true, $"Saved. Using the public list of AniList user \"{userName}\".");
                }
                catch (AniListException ex)
                {
                    return new ValidationResult(false, $"The Planning list of AniList user \"{userName}\" is not publicly visible: {ex.Message}");
                }

            default:
                return new ValidationResult(false, "Unknown login source.");
        }
    }
}

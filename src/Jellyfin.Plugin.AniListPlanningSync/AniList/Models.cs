using System;
using System.Collections.Generic;
using System.Net;

namespace Jellyfin.Plugin.AniListPlanningSync.AniList;

public record AniListViewer(int Id, string Name);

/// <summary>
/// Identifies whose list to read. Exactly one of UserId / UserName is set.
/// A token is only present for lists read through the Ani-Sync login.
/// </summary>
public record AniListUserRef(int? UserId, string? UserName, string? Token)
{
    public override string ToString() => UserName ?? $"#{UserId}";
}

public record PlanningList(IReadOnlySet<int> MediaIds, string? Marker);

public class AniListException : Exception
{
    public AniListException(string message, HttpStatusCode? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}

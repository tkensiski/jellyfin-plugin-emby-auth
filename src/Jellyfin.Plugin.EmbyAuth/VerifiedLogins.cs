using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// Holds the users whose password Emby accepted during a login, until <see cref="MoveToDefaultLoginMethod"/> handles the login.
/// </summary>
internal sealed class VerifiedLogins
{
    private readonly ConcurrentDictionary<Guid, byte> _userIds = new();

    /// <summary>
    /// Records that Emby accepted the password of the user and that the plugin saved it.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    public void Add(Guid userId) => _userIds[userId] = 0;

    /// <summary>
    /// Removes the record for the user.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <returns><c>true</c> if a record existed.</returns>
    public bool TryConsume(Guid userId) => _userIds.TryRemove(userId, out _);
}

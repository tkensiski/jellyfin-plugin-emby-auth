using System;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.EmbyAuth.Configuration;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// Applies the <see cref="AccountAccess"/> setting to Jellyfin accounts.
/// </summary>
internal static class AccountAccessPolicy
{
    /// <summary>
    /// Sets the access of an account that the plugin just created with Jellyfin's default permissions.
    /// </summary>
    /// <param name="user">The new account.</param>
    /// <param name="access">The account access setting.</param>
    /// <param name="embyAllowsRemoteAccess">Whether Emby allows the user to connect from outside the local network.</param>
    public static void ApplyToNewAccount(User user, AccountAccess access, bool embyAllowsRemoteAccess)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (access == AccountAccess.JellyfinDefaults)
        {
            return;
        }

        user.SetPermission(PermissionKind.EnableRemoteAccess, embyAllowsRemoteAccess);
        if (access == AccountAccess.NoLibraries)
        {
            user.SetPermission(PermissionKind.EnableAllFolders, false);
            user.SetPreference(PreferenceKind.EnabledFolders, Array.Empty<Guid>());
        }
    }

    /// <summary>
    /// Adjusts the access of an existing account after Emby accepts a login. It only removes remote access, and never adds it.
    /// </summary>
    /// <param name="user">The existing account.</param>
    /// <param name="access">The account access setting.</param>
    /// <param name="embyAllowsRemoteAccess">Whether Emby allows the user to connect from outside the local network.</param>
    public static void ApplyToExistingAccount(User user, AccountAccess access, bool embyAllowsRemoteAccess)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (access != AccountAccess.JellyfinDefaults && !embyAllowsRemoteAccess)
        {
            user.SetPermission(PermissionKind.EnableRemoteAccess, false);
        }
    }
}

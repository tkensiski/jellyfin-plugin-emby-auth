using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// The readiness of a user on the Emby login method to move off it.
/// </summary>
/// <remarks>
/// <see cref="NoPassword"/> takes precedence over <see cref="Unknown"/>. A missing saved password is a fact the
/// database answers on its own; readiness is the only thing the fingerprint file can make unknowable. AUTH-06
/// requires the settings page to name every account with no saved password, and an account that disappeared into
/// <see cref="Unknown"/> because the fingerprint file could not be read would not be named.
/// </remarks>
public enum MigrationUserState
{
    /// <summary>
    /// Emby verified the saved password. The user will move to the Default login method on the next migration run.
    /// </summary>
    Ready,

    /// <summary>
    /// The user has a saved password, but Emby has not verified it. The user must log in to Jellyfin once while
    /// Emby runs, or an administrator must set a new password.
    /// </summary>
    NeedsEmbyLogin,

    /// <summary>
    /// The user has no saved password.
    /// </summary>
    NoPassword,

    /// <summary>
    /// The fingerprint file could not be read on this call, so whether Emby verified the user's saved password is
    /// unknown. The user has a saved password.
    /// </summary>
    Unknown,
}

/// <summary>
/// A user on the Emby login method, as the migration sees the user.
/// </summary>
/// <param name="Id">The Jellyfin user ID.</param>
/// <param name="Username">The Jellyfin user name.</param>
/// <param name="PasswordHash">The saved password hash, if any.</param>
/// <param name="State">The user's readiness to move to the Default login method.</param>
internal sealed record EmbyLoginMethodUser(Guid Id, string Username, string? PasswordHash, MigrationUserState State);

/// <summary>
/// Lists the users on the Emby login method. The migration task and the migration API use the same list.
/// </summary>
internal static class EmbyLoginMethodUsers
{
    /// <summary>
    /// Lists the users on the Emby login method, sorted by name.
    /// </summary>
    /// <param name="dbContext">The Jellyfin database context.</param>
    /// <param name="verifiedPasswords">The record of password hashes that Emby verified.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The users.</returns>
    public static async Task<IReadOnlyList<EmbyLoginMethodUser>> ListAsync(
        JellyfinDbContext dbContext,
        EmbyVerifiedPasswords verifiedPasswords,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(verifiedPasswords);
        var users = await dbContext.Users
            .Where(user => user.AuthenticationProviderId == EmbyAuthenticationProvider.ProviderId)
            .OrderBy(user => user.Username)
            .Select(user => new { user.Id, user.Username, user.Password })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var recordsAvailable = verifiedPasswords.RecordsAvailable();

        return users
            .Select(user => new EmbyLoginMethodUser(
                user.Id,
                user.Username,
                user.Password,
                DetermineState(user.Password, user.Id, recordsAvailable, verifiedPasswords)))
            .ToList();
    }

    private static MigrationUserState DetermineState(string? passwordHash, Guid userId, bool recordsAvailable, EmbyVerifiedPasswords verifiedPasswords)
    {
        if (passwordHash is null)
        {
            return MigrationUserState.NoPassword;
        }

        if (!recordsAvailable)
        {
            return MigrationUserState.Unknown;
        }

        return verifiedPasswords.Matches(userId, passwordHash) ? MigrationUserState.Ready : MigrationUserState.NeedsEmbyLogin;
    }
}

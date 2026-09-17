using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// A user on the Emby login method, as the migration sees the user.
/// </summary>
/// <param name="Id">The Jellyfin user ID.</param>
/// <param name="Username">The Jellyfin user name.</param>
/// <param name="PasswordHash">The saved password hash, if any.</param>
/// <param name="ReadyToMove">Whether Emby verified the saved password, so that the migration can move the user to the Default login method.</param>
internal sealed record EmbyLoginMethodUser(Guid Id, string Username, string? PasswordHash, bool ReadyToMove);

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

        return users
            .Select(user => new EmbyLoginMethodUser(
                user.Id,
                user.Username,
                user.Password,
                user.Password is not null && verifiedPasswords.Matches(user.Id, user.Password)))
            .ToList();
    }
}

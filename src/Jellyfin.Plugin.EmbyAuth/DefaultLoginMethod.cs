using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// Moves users to Jellyfin's Default login method.
/// </summary>
internal static class DefaultLoginMethod
{
    /// <summary>
    /// The login method ID of Jellyfin's Default login method.
    /// </summary>
    public const string ProviderId = "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider";

    /// <summary>
    /// Moves the user to the Default login method, but only while the user is on the Emby login method and still has the given password hash.
    /// </summary>
    /// <remarks>
    /// The update changes one column in one statement, so it does not overwrite concurrent changes to the user.
    /// </remarks>
    /// <param name="dbContext">The Jellyfin database context.</param>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="passwordHash">The password hash that Emby verified.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if the user moved.</returns>
    public static async Task<bool> MoveAsync(JellyfinDbContext dbContext, Guid userId, string passwordHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        var moved = await dbContext.Users
            .Where(user => user.Id == userId
                && user.AuthenticationProviderId == EmbyAuthenticationProvider.ProviderId
                && user.Password == passwordHash)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.AuthenticationProviderId, ProviderId), cancellationToken)
            .ConfigureAwait(false);
        return moved == 1;
    }
}

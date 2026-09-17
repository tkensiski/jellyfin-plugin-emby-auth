using System;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// Moves a user from the Emby login method to the Default login method after Emby accepted the password of the user.
/// </summary>
/// <remarks>
/// This runs after the login completes, because Jellyfin sets the login method of the user to the method that accepted the login.
/// It acts only on logins in <see cref="VerifiedLogins"/>, so a Quick Connect login does not move a user.
/// </remarks>
/// <param name="verifiedLogins">The users whose password Emby accepted.</param>
/// <param name="dbContextFactory">The Jellyfin database context factory.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class MoveToDefaultLoginMethod(
    VerifiedLogins verifiedLogins,
    IDbContextFactory<JellyfinDbContext> dbContextFactory,
    ILogger<MoveToDefaultLoginMethod> logger)
    : IEventConsumer<AuthenticationResultEventArgs>
{
    /// <summary>
    /// The login method ID of Jellyfin's Default login method.
    /// </summary>
    public const string DefaultProviderId = "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider";

    /// <inheritdoc />
    public async Task OnEvent(AuthenticationResultEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        var userId = eventArgs.User.Id;
        if (!verifiedLogins.TryConsume(userId))
        {
            return;
        }

        // Update only this column, and only while the user is still on the Emby login method, so that concurrent changes to the user stay intact.
        var dbContext = await dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var moved = await dbContext.Users
                .Where(user => user.Id == userId && user.AuthenticationProviderId == EmbyAuthenticationProvider.ProviderId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.AuthenticationProviderId, DefaultProviderId))
                .ConfigureAwait(false);
            if (moved == 1)
            {
                LogMovedToDefault(logger, eventArgs.User.Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The plugin moved user {Username} to the Default login method. Jellyfin now checks the password of this user without Emby.")]
    private static partial void LogMovedToDefault(ILogger logger, string? username);
}

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// In <see cref="MigrationMode.MoveAfterFirstLogin"/>, moves a user to the configured migration target after a login, if Emby verified the saved password.
/// </summary>
/// <remarks>
/// This runs after the login completes, because Jellyfin sets the login method of the user to the method that accepted the login.
/// A Quick Connect login triggers the same event. The check against <see cref="EmbyVerifiedPasswords"/> makes sure that only a password that Emby verified moves.
/// </remarks>
/// <param name="verifiedPasswords">The record of password hashes that Emby verified.</param>
/// <param name="dbContextFactory">The Jellyfin database context factory.</param>
/// <param name="configurationSource">The plugin settings source. Reads the current configuration on each event, so tests can supply settings with no static plugin state.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class MoveAfterLogin(
    EmbyVerifiedPasswords verifiedPasswords,
    IDbContextFactory<JellyfinDbContext> dbContextFactory,
    Func<PluginConfiguration?> configurationSource,
    ILogger<MoveAfterLogin> logger)
    : IEventConsumer<AuthenticationResultEventArgs>
{
    /// <inheritdoc />
    public async Task OnEvent(AuthenticationResultEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        var configuration = configurationSource();
        if (configuration?.MigrationMode != MigrationMode.MoveAfterFirstLogin)
        {
            return;
        }

        var target = LoginMethodMove.ResolveMigrationTarget(configuration);
        if (target.Kind == MoveTargetKind.Remain)
        {
            return;
        }

        if (target.Kind == MoveTargetKind.Invalid)
        {
            LogTargetInvalid(logger);
            return;
        }

        var userId = eventArgs.User.Id;
        var dbContext = await dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var user = await dbContext.Users
                .Where(candidate => candidate.Id == userId && candidate.AuthenticationProviderId == EmbyAuthenticationProvider.ProviderId)
                .Select(candidate => new { candidate.Username, candidate.Password })
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
            if (user?.Password is null || !verifiedPasswords.Matches(userId, user.Password))
            {
                return;
            }

            if (await LoginMethodMove.MoveAsync(dbContext, userId, user.Password, target.ProviderId!, CancellationToken.None).ConfigureAwait(false))
            {
                LogMoved(logger, user.Username);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The plugin moved user {Username} to the configured login method. Jellyfin now checks the password of this user without Emby.")]
    private static partial void LogMoved(ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Error, Message = "The configured migration target is not a login method Jellyfin reports as enabled. The plugin moved nobody. Logins are unaffected, because Emby still checks every password.")]
    private static partial void LogTargetInvalid(ILogger logger);
}

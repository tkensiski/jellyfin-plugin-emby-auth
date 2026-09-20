using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using MediaBrowser.Model.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// A scheduled task that finishes the migration: it moves each user on the Emby login method whose saved password Emby verified to the Default login method.
/// </summary>
/// <remarks>
/// The task has no default trigger. An administrator runs it with "Run migration now" on the plugin settings page, from Dashboard > Advanced > Scheduled Tasks, or through the API.
/// </remarks>
public sealed partial class MoveEmbyUsersToDefaultTask : IScheduledTask
{
    private readonly IDbContextFactory<JellyfinDbContext> _dbContextFactory;
    private readonly EmbyVerifiedPasswords _verifiedPasswords;
    private readonly ILogger<MoveEmbyUsersToDefaultTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MoveEmbyUsersToDefaultTask"/> class.
    /// </summary>
    /// <param name="dbContextFactory">The Jellyfin database context factory.</param>
    /// <param name="verifiedPasswords">The record of password hashes that Emby verified.</param>
    /// <param name="logger">The logger.</param>
    public MoveEmbyUsersToDefaultTask(
        IDbContextFactory<JellyfinDbContext> dbContextFactory,
        EmbyVerifiedPasswords verifiedPasswords,
        ILogger<MoveEmbyUsersToDefaultTask> logger)
    {
        _dbContextFactory = dbContextFactory;
        _verifiedPasswords = verifiedPasswords;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Move Emby users to the Default login method";

    /// <inheritdoc />
    public string Key => "EmbyAuthMoveUsersToDefault";

    /// <inheritdoc />
    public string Description => "Moves each user on the Emby login method to the Default login method, if Emby verified the saved password of the user. Other users stay on the Emby login method.";

    /// <inheritdoc />
    public string Category => "Emby Auth";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var candidates = await EmbyLoginMethodUsers.ListAsync(dbContext, _verifiedPasswords, cancellationToken).ConfigureAwait(false);

            var moved = 0;
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                if (candidate.State == MigrationUserState.Ready
                    && await LoginMethodMove.MoveAsync(dbContext, candidate.Id, candidate.PasswordHash!, LoginMethodMove.DefaultProviderId, cancellationToken).ConfigureAwait(false))
                {
                    moved++;
                }
                else
                {
                    LogNotMoved(_logger, candidate.Username);
                }

                progress.Report(100.0 * (index + 1) / candidates.Count);
            }

            LogSummary(_logger, moved, candidates.Count - moved);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {Username} stays on the Emby login method, because Emby did not verify the saved password. The user must log in once while Emby runs, or an administrator must set a new password.")]
    private static partial void LogNotMoved(ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Information, Message = "The migration task moved {Moved} users to the Default login method. {Remaining} users stay on the Emby login method.")]
    private static partial void LogSummary(ILogger logger, int moved, int remaining);
}

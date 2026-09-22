using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// A scheduled task that finishes the migration: it moves each user on the Emby login method whose saved password Emby verified to the configured migration target.
/// </summary>
/// <remarks>
/// The task has no default trigger. An administrator runs it with "Run migration now" on the plugin settings page, from Dashboard > Advanced > Scheduled Tasks, or through the API.
/// </remarks>
public sealed partial class EmbyMigrationTask : IScheduledTask
{
    private readonly IDbContextFactory<JellyfinDbContext> _dbContextFactory;
    private readonly EmbyVerifiedPasswords _verifiedPasswords;
    private readonly Func<PluginConfiguration?> _configurationSource;
    private readonly ILogger<EmbyMigrationTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmbyMigrationTask"/> class.
    /// </summary>
    /// <param name="dbContextFactory">The Jellyfin database context factory.</param>
    /// <param name="verifiedPasswords">The record of password hashes that Emby verified.</param>
    /// <param name="configurationSource">The plugin settings source.</param>
    /// <param name="logger">The logger.</param>
    public EmbyMigrationTask(
        IDbContextFactory<JellyfinDbContext> dbContextFactory,
        EmbyVerifiedPasswords verifiedPasswords,
        Func<PluginConfiguration?> configurationSource,
        ILogger<EmbyMigrationTask> logger)
    {
        _dbContextFactory = dbContextFactory;
        _verifiedPasswords = verifiedPasswords;
        _configurationSource = configurationSource;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Finish the Emby migration";

    /// <inheritdoc />
    public string Key => "EmbyAuthMigration";

    /// <inheritdoc />
    public string Description => "Moves each user on the Emby login method to the configured migration target, if Emby verified the saved password of the user. Other users stay on the Emby login method.";

    /// <inheritdoc />
    public string Category => "Emby Auth";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var target = LoginMethodMove.ResolveMigrationTarget(_configurationSource());
        if (target.Kind == MoveTargetKind.Remain)
        {
            LogTargetRemain(_logger);
            progress.Report(100);
            return;
        }

        if (target.Kind == MoveTargetKind.Invalid)
        {
            LogTargetInvalid(_logger);
            progress.Report(100);
            return;
        }

        var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var candidates = await EmbyLoginMethodUsers.ListAsync(dbContext, _verifiedPasswords, cancellationToken).ConfigureAwait(false);

            if (candidates.Count == 0)
            {
                LogSummary(_logger, 0, 0);
                progress.Report(100);
                return;
            }

            var moved = 0;
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                if (candidate.State == MigrationUserState.Ready
                    && await LoginMethodMove.MoveAsync(dbContext, candidate.Id, candidate.PasswordHash!, target.ProviderId!, cancellationToken).ConfigureAwait(false))
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

    [LoggerMessage(Level = LogLevel.Information, Message = "The migration task moved {Moved} users to the configured migration target. {Remaining} users stay on the Emby login method.")]
    private static partial void LogSummary(ILogger logger, int moved, int remaining);

    [LoggerMessage(Level = LogLevel.Information, Message = "The migration target is set to remain on the Emby login method, so the migration task moved nobody.")]
    private static partial void LogTargetRemain(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "The configured migration target is not a login method Jellyfin reports as enabled. The task moved nobody. Logins are unaffected, because Emby still checks every password.")]
    private static partial void LogTargetInvalid(ILogger logger);
}

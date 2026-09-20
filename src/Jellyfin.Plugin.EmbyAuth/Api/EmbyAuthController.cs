using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.EmbyAuth.Api;

/// <summary>
/// The migration API of the Emby Auth plugin. Only administrators can use it.
/// </summary>
/// <param name="dbContextFactory">The Jellyfin database context factory.</param>
/// <param name="verifiedPasswords">The record of password hashes that Emby verified.</param>
/// <param name="taskManager">The Jellyfin scheduled task manager.</param>
/// <param name="userManager">The Jellyfin user manager. Used to list the login methods an administrator may pick as the migration target.</param>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("EmbyAuth")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class EmbyAuthController(
    IDbContextFactory<JellyfinDbContext> dbContextFactory,
    EmbyVerifiedPasswords verifiedPasswords,
    ITaskManager taskManager,
    IUserManager userManager)
    : ControllerBase
{
    /// <summary>
    /// Reports the migration status: the read failure, the per-user state, the migration task's state, and the
    /// login methods an administrator may pick as the migration target.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The migration status.</returns>
    [HttpGet("Migration")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<MigrationStatus>> GetMigrationStatus(CancellationToken cancellationToken)
    {
        var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (dbContext.ConfigureAwait(false))
        {
            var users = await EmbyLoginMethodUsers.ListAsync(dbContext, verifiedPasswords, cancellationToken).ConfigureAwait(false);
            var taskWorker = taskManager.ScheduledTasks.FirstOrDefault(worker => worker.ScheduledTask is EmbyMigrationTask);
            var task = taskWorker is null
                ? null
                : new MigrationTaskInfo(taskWorker.State, taskWorker.CurrentProgress, taskWorker.LastExecutionResult?.EndTimeUtc, taskWorker.LastExecutionResult?.Status);
            var availableTargets = userManager.GetAuthenticationProviders()
                .Where(provider => provider.Id != EmbyAuthenticationProvider.ProviderId)
                .ToList();

            return new MigrationStatus(
                !verifiedPasswords.RecordsAvailable(),
                task,
                users.Select(user => new MigrationUser(user.Username, user.State)).ToList(),
                availableTargets);
        }
    }

    /// <summary>
    /// Starts the migration task "Finish the Emby migration", unless it already runs.
    /// </summary>
    /// <returns>No content.</returns>
    [HttpPost("Migration/Run")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult RunMigration()
    {
        taskManager.QueueIfNotRunning<EmbyMigrationTask>();
        return NoContent();
    }
}

/// <summary>
/// The migration task's state, as Jellyfin's scheduled task manager reports it.
/// </summary>
/// <param name="State">The task's current state.</param>
/// <param name="Progress">The task's progress while it runs. Absent while the task does not run.</param>
/// <param name="LastEndTimeUtc">When the task's last run ended. Absent if the task has never run.</param>
/// <param name="LastResult">The result of the task's last run. Absent if the task has never run.</param>
public sealed record MigrationTaskInfo(TaskState State, double? Progress, DateTime? LastEndTimeUtc, TaskCompletionStatus? LastResult);

/// <summary>
/// The migration status.
/// </summary>
/// <remarks>
/// One response carries everything the Migration section of the settings page needs, rather than a second
/// endpoint for the pickable login methods: the page needs both at the same page load, and a second endpoint
/// would add a second round trip and a second failure-message path for data it needs at the same moment.
/// </remarks>
/// <param name="RecordsUnavailable">
/// Whether Jellyfin could not read the record of passwords Emby verified on this call. While <c>true</c>, every
/// user in <see cref="Users"/> with a saved password reports <see cref="MigrationUserState.Unknown"/>; the
/// condition is not sticky, and the next call reads the file again.
/// </param>
/// <param name="Task">The migration task's state, or absent if no scheduled-task worker is registered for it.</param>
/// <param name="Users">The users on the Emby login method, sorted by name.</param>
/// <param name="AvailableTargets">The login methods Jellyfin reports as enabled, with this plugin's own Emby method removed.</param>
public sealed record MigrationStatus(bool RecordsUnavailable, MigrationTaskInfo? Task, IReadOnlyList<MigrationUser> Users, IReadOnlyList<NameIdPair> AvailableTargets);

/// <summary>
/// A user on the Emby login method.
/// </summary>
/// <param name="Name">The Jellyfin user name.</param>
/// <param name="State">The user's readiness to move to the Default login method.</param>
public sealed record MigrationUser(string Name, MigrationUserState State);

using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using MediaBrowser.Common.Api;
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
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("EmbyAuth")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class EmbyAuthController(
    IDbContextFactory<JellyfinDbContext> dbContextFactory,
    EmbyVerifiedPasswords verifiedPasswords,
    ITaskManager taskManager)
    : ControllerBase
{
    /// <summary>
    /// Lists the users on the Emby login method and whether each user is ready to move to the Default login method.
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
            return new MigrationStatus(users.Select(user => new MigrationUser(user.Username, user.ReadyToMove)).ToList());
        }
    }

    /// <summary>
    /// Starts the migration task "Move Emby users to the Default login method", unless it already runs.
    /// </summary>
    /// <returns>No content.</returns>
    [HttpPost("Migration/Run")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public ActionResult RunMigration()
    {
        taskManager.QueueIfNotRunning<MoveEmbyUsersToDefaultTask>();
        return NoContent();
    }
}

/// <summary>
/// The migration status.
/// </summary>
/// <param name="Users">The users on the Emby login method, sorted by name.</param>
public sealed record MigrationStatus(IReadOnlyList<MigrationUser> Users);

/// <summary>
/// A user on the Emby login method.
/// </summary>
/// <param name="Name">The Jellyfin user name.</param>
/// <param name="ReadyToMove">
/// Whether Emby verified the saved password, so that the next migration run moves the user to the Default login method.
/// If not, the user must log in to Jellyfin once while Emby runs, or an administrator must set a new password.
/// </param>
public sealed record MigrationUser(string Name, bool ReadyToMove);

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// Moves users off the Emby login method, and resolves which login method a move should send them to.
/// </summary>
internal static class LoginMethodMove
{
    /// <summary>
    /// The login method ID of Jellyfin's Default login method. One possible destination of a move, not the only one.
    /// </summary>
    public const string DefaultProviderId = "Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider";

    /// <summary>
    /// Moves the user to the given login method, but only while the user is on the Emby login method and still has the given password hash.
    /// </summary>
    /// <remarks>
    /// The update changes one column in one statement, so it does not overwrite concurrent changes to the user.
    /// </remarks>
    /// <param name="dbContext">The Jellyfin database context.</param>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="passwordHash">The password hash that Emby verified.</param>
    /// <param name="targetProviderId">The login method to move the user to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if the user moved.</returns>
    public static async Task<bool> MoveAsync(JellyfinDbContext dbContext, Guid userId, string passwordHash, string targetProviderId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProviderId);
        var moved = await dbContext.Users
            .Where(user => user.Id == userId
                && user.AuthenticationProviderId == EmbyAuthenticationProvider.ProviderId
                && user.Password == passwordHash)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.AuthenticationProviderId, targetProviderId), cancellationToken)
            .ConfigureAwait(false);
        return moved == 1;
    }

    /// <summary>
    /// Resolves the migration target: the login method the move after a login and the migration task move a user to.
    /// </summary>
    /// <param name="configuration">The plugin configuration, or <c>null</c> if the plugin is not loaded.</param>
    /// <returns>The resolved target.</returns>
    public static MoveTarget ResolveMigrationTarget(PluginConfiguration? configuration) =>
        configuration is null ? new MoveTarget(MoveTargetKind.Invalid, null) : ResolveTarget(configuration.MigrationTarget);

    /// <summary>
    /// Resolves the password-set target: the login method the move happening when an administrator sets a password in Jellyfin moves a user to.
    /// </summary>
    /// <param name="configuration">The plugin configuration, or <c>null</c> if the plugin is not loaded.</param>
    /// <returns>The resolved target. Falls back to <see cref="ResolveMigrationTarget"/> while <see cref="PluginConfiguration.PasswordSetTarget"/> is empty.</returns>
    public static MoveTarget ResolvePasswordSetTarget(PluginConfiguration? configuration)
    {
        if (configuration is null || string.IsNullOrEmpty(configuration.PasswordSetTarget))
        {
            return ResolveMigrationTarget(configuration);
        }

        return ResolveTarget(configuration.PasswordSetTarget);
    }

    private static MoveTarget ResolveTarget(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new MoveTarget(MoveTargetKind.Invalid, null);
        }

        var trimmed = value.Trim();
        return trimmed == PluginConfiguration.RemainOnEmbyLoginMethod
            ? new MoveTarget(MoveTargetKind.Remain, null)
            : new MoveTarget(MoveTargetKind.Move, trimmed);
    }
}

/// <summary>
/// The three outcomes of resolving a move target.
/// </summary>
internal enum MoveTargetKind
{
    /// <summary>
    /// Move the user to <see cref="MoveTarget.ProviderId"/>.
    /// </summary>
    Move,

    /// <summary>
    /// The administrator asked for no move. This is silent: it is a deliberate choice, not a misconfiguration.
    /// </summary>
    Remain,

    /// <summary>
    /// The configured target is blank, or the configuration is not loaded. This is a misconfiguration: it logs one Error entry and moves nobody.
    /// </summary>
    Invalid,
}

/// <summary>
/// A resolved move target.
/// </summary>
/// <param name="Kind">The outcome.</param>
/// <param name="ProviderId">The login method to move the user to, when <paramref name="Kind"/> is <see cref="MoveTargetKind.Move"/>. Otherwise <c>null</c>.</param>
internal readonly record struct MoveTarget(MoveTargetKind Kind, string? ProviderId);

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// The state of an Emby user, as <see cref="EmbyUserDirectory"/> reports it.
/// </summary>
internal enum EmbyUserStatus
{
    /// <summary>An enabled Emby user has exactly this name, ignoring case.</summary>
    Active,

    /// <summary>The Emby user with this name is disabled.</summary>
    Disabled,

    /// <summary>No Emby user has exactly this name, ignoring case.</summary>
    NotFound,

    /// <summary>The plugin cannot read the list of Emby users.</summary>
    Unavailable,
}

/// <summary>
/// Keeps a short-lived copy of the Emby user list, so that the plugin sends a password to Emby only for a real Emby user name.
/// </summary>
/// <param name="embyClient">The Emby client.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class EmbyUserDirectory(EmbyClient embyClient, TimeProvider timeProvider, ILogger<EmbyUserDirectory> logger)
{
    /// <summary>
    /// How long the plugin uses a user list before it reads the list again.
    /// </summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long the plugin waits after a failed read before it contacts Emby again.
    /// </summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private volatile Snapshot? _snapshot;

    /// <summary>
    /// Gets the state of the Emby user that has the given name.
    /// </summary>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="username">The user name that the person typed. The match ignores case and nothing else.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The state of the Emby user.</returns>
    public async Task<EmbyUserStatus> GetStatusAsync(EmbyAuthSettings settings, string username, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var snapshot = _snapshot;
        var now = timeProvider.GetUtcNow();
        if (snapshot is null || snapshot.ServerUrl != settings.ServerUrl || snapshot.ApiKey != settings.ApiKey || now >= snapshot.ValidUntil)
        {
            var users = await embyClient.GetUsersAsync(settings.ServerUrl, settings.ApiKey, cancellationToken).ConfigureAwait(false);
            snapshot = new Snapshot(settings.ServerUrl, settings.ApiKey, users, now + (users is null ? RetryDelay : CacheDuration));
            _snapshot = snapshot;
            if (users is null)
            {
                LogUserListUnavailable(logger, RetryDelay.TotalSeconds);
            }
        }

        if (snapshot.Users is null)
        {
            return EmbyUserStatus.Unavailable;
        }

        var user = snapshot.Users.FirstOrDefault(candidate => string.Equals(candidate.Name, username, StringComparison.OrdinalIgnoreCase));
        if (user is null)
        {
            return EmbyUserStatus.NotFound;
        }

        return user.IsDisabled ? EmbyUserStatus.Disabled : EmbyUserStatus.Active;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jellyfin cannot read the list of Emby users. The plugin refuses logins on the Emby login method for {RetrySeconds} seconds. Then it tries again.")]
    private static partial void LogUserListUnavailable(ILogger logger, double retrySeconds);

    private sealed record Snapshot(Uri ServerUrl, string ApiKey, IReadOnlyList<EmbyUser>? Users, DateTimeOffset ValidUntil);
}

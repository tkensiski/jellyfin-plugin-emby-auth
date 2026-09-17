using System;
using System.Threading.Tasks;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// Moves a user from the Emby login method to the Default login method after a successful login.
/// </summary>
/// <remarks>
/// This runs after the login completes, because Jellyfin sets the login method of the user to the method that accepted the login.
/// </remarks>
/// <param name="userManager">The user manager.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class MoveToDefaultLoginMethod(IUserManager userManager, ILogger<MoveToDefaultLoginMethod> logger)
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
        var user = userManager.GetUserById(eventArgs.User.Id);
        if (user is null
            || !string.Equals(user.AuthenticationProviderId, EmbyAuthenticationProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (user.Password is null)
        {
            LogNoSavedPassword(logger, user.Username);
            return;
        }

        user.AuthenticationProviderId = DefaultProviderId;
        await userManager.UpdateUserAsync(user).ConfigureAwait(false);
        LogMovedToDefault(logger, user.Username);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The plugin moved user {Username} to the Default login method. Jellyfin now checks the password of this user without Emby.")]
    private static partial void LogMovedToDefault(ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {Username} has no saved password, so the user stays on the Emby login method. Before you shut down Emby, set the login method of this user to Default. Then set a password for the user.")]
    private static partial void LogNoSavedPassword(ILogger logger, string username);
}

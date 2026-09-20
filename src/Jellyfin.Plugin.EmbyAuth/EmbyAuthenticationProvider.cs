using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// A Jellyfin login method that checks passwords against Emby and saves a Jellyfin copy of each password that Emby accepts.
/// </summary>
/// <remarks>
/// This class stays internal. A public class would enter the <c>GetExports&lt;IAuthenticationProvider&gt;()</c> scan
/// another plugin can run, which would let that plugin send a password to Emby for a user this plugin does not
/// serve. <c>TypeVisibilityTests</c> fails if this class, or any other type in this assembly, becomes public
/// without a recorded reason.
/// </remarks>
/// <param name="serviceProvider">The service provider. The user manager is resolved on each login because it depends on all login methods.</param>
/// <param name="cryptoProvider">The Jellyfin password hasher.</param>
/// <param name="embyClient">The Emby client.</param>
/// <param name="userDirectory">The cached list of Emby users.</param>
/// <param name="verifiedPasswords">The record of password hashes that Emby verified.</param>
/// <param name="configurationSource">The plugin settings source. Reads the current configuration on each login, so tests can supply settings with no static plugin state.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class EmbyAuthenticationProvider(
    IServiceProvider serviceProvider,
    ICryptoProvider cryptoProvider,
    EmbyClient embyClient,
    EmbyUserDirectory userDirectory,
    EmbyVerifiedPasswords verifiedPasswords,
    Func<PluginConfiguration?> configurationSource,
    ILogger<EmbyAuthenticationProvider> logger)
    : IAuthenticationProvider, IRequiresResolvedUser
{
    private const string InvalidLogin = "Invalid username or password";

    /// <summary>
    /// Gets the login method ID that Jellyfin stores on each user.
    /// </summary>
    public static string ProviderId { get; } = typeof(EmbyAuthenticationProvider).FullName!;

    /// <inheritdoc />
    public string Name => "Emby";

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <summary>
    /// Not used. Jellyfin calls <see cref="Authenticate(string, string, User)"/>, because this login method implements <see cref="IRequiresResolvedUser"/>.
    /// </summary>
    /// <param name="username">The user name.</param>
    /// <param name="password">The password.</param>
    /// <returns>This method does not return.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public Task<ProviderAuthenticationResult> Authenticate(string username, string password) =>
        throw new NotSupportedException("Jellyfin calls the Authenticate overload that has a resolved user.");

    /// <inheritdoc />
    public async Task<ProviderAuthenticationResult> Authenticate(string username, string password, User? resolvedUser)
    {
        if (string.IsNullOrEmpty(password))
        {
            LogBlankPasswordRefused(logger, username);
            throw new AuthenticationException(InvalidLogin);
        }

        if (resolvedUser is not null && resolvedUser.HasPermission(PermissionKind.IsDisabled))
        {
            throw new AuthenticationException(InvalidLogin);
        }

        if (resolvedUser is not null && resolvedUser.HasPermission(PermissionKind.IsAdministrator))
        {
            LogAdministratorRefused(logger, resolvedUser.Username);
            throw new AuthenticationException(InvalidLogin);
        }

        var settings = GetSettings();
        var status = await userDirectory.GetStatusAsync(settings, username, CancellationToken.None).ConfigureAwait(false);
        if (status != EmbyUserStatus.Active)
        {
            LogPasswordNotSent(logger, username, status);
            throw new AuthenticationException(InvalidLogin);
        }

        var embyLogin = await embyClient.AuthenticateAsync(settings.ServerUrl, username, password, CancellationToken.None).ConfigureAwait(false)
            ?? throw new AuthenticationException(InvalidLogin);

        var action = LoginDecision.Decide(username, ToAccount(resolvedUser), embyLogin.Name, ProviderId);
        if (action == LoginAction.Deny)
        {
            LogAccountConflict(logger, username, embyLogin.Name);
            throw new AuthenticationException(InvalidLogin);
        }

        var passwordHash = cryptoProvider.CreatePasswordHash(password).ToString();
        var userManager = serviceProvider.GetRequiredService<IUserManager>();
        var user = action == LoginAction.CreateAccount
            ? await CreateAccountAsync(userManager, settings, embyLogin, passwordHash).ConfigureAwait(false)
            : await SavePasswordAsync(userManager, settings, resolvedUser!, embyLogin, passwordHash).ConfigureAwait(false);

        verifiedPasswords.Record(user.Id, passwordHash);
        return new ProviderAuthenticationResult { Username = user.Username };
    }

    /// <summary>
    /// Handles a password that an administrator or the user sets in Jellyfin.
    /// </summary>
    /// <remarks>
    /// A new password is saved, then the user moves to the resolved password-set target: the configured
    /// <see cref="PluginConfiguration.PasswordSetTarget"/>, or <see cref="PluginConfiguration.MigrationTarget"/>
    /// when that setting is empty. Remain on Emby Login saves the password but leaves the login method unchanged;
    /// the new password plays no part in a login while the user stays on the Emby method, since only Emby decides
    /// a login there. An unusable target also leaves the login method unchanged and logs one Error entry, and
    /// never refuses the password change: Jellyfin calls this method inside its own password-change flow, and a
    /// settings problem that has nothing to do with the password must not block it.
    /// A password reset removes the saved password, and the user stays on the Emby login method, because a Default account without a password opens with a blank password.
    /// </remarks>
    /// <param name="user">The user. Jellyfin saves the changes after this method returns.</param>
    /// <param name="newPassword">The new password, or an empty string for a password reset.</param>
    /// <returns>A completed task.</returns>
    public Task ChangePassword(User user, string newPassword)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrEmpty(newPassword))
        {
            user.Password = null;
            LogPasswordReset(logger, user.Username);
            return Task.CompletedTask;
        }

        user.Password = cryptoProvider.CreatePasswordHash(newPassword).ToString();
        var target = LoginMethodMove.ResolvePasswordSetTarget(configurationSource());
        switch (target.Kind)
        {
            case MoveTargetKind.Move:
                user.AuthenticationProviderId = target.ProviderId!;
                LogPasswordSetInJellyfin(logger, user.Username);
                break;
            case MoveTargetKind.Remain:
                LogPasswordSetTargetRemainsOnEmby(logger, user.Username);
                break;
            default:
                LogPasswordSetTargetInvalid(logger, user.Username);
                break;
        }

        return Task.CompletedTask;
    }

    private static JellyfinAccount? ToAccount(User? user) =>
        user is null ? null : new JellyfinAccount(user.Username, user.AuthenticationProviderId);

    private EmbyAuthSettings GetSettings()
    {
        if (EmbyAuthSettings.TryCreate(configurationSource(), out var settings, out var problem))
        {
            return settings;
        }

        LogSettingsInvalid(logger, problem);
        throw new AuthenticationException(problem);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The cleanup delete must never let a second exception replace the login refusal already in flight (D-03); every exception type is logged and swallowed.")]
    private async Task<User> CreateAccountAsync(IUserManager userManager, EmbyAuthSettings settings, EmbyLogin embyLogin, string passwordHash)
    {
        User user;
        try
        {
            user = await userManager.CreateUserAsync(embyLogin.Name).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogCreateAccountFailed(logger, ex, embyLogin.Name);
            throw new AuthenticationException(InvalidLogin, ex);
        }

        user.AuthenticationProviderId = ProviderId;
        user.Password = passwordHash;
        AccountAccessPolicy.ApplyToNewAccount(user, settings.AccountAccess, embyLogin.EnableRemoteAccess);

        // Jellyfin commits the new account without a password. If this save fails, delete the account so that no blank password opens it.
        Exception? saveFailure = null;
        try
        {
            await userManager.UpdateUserAsync(user).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            saveFailure = ex;
        }

        if (saveFailure is not null)
        {
            // The delete is attempted exactly once and never retried (D-03). Whichever of the two failures is
            // the last word gets the one Error log: the save failure if the cleanup delete succeeds, or the
            // delete failure if it does not, because that is the more actionable message for an administrator.
            try
            {
                await userManager.DeleteUserAsync(user.Id).ConfigureAwait(false);
                LogSaveFailed(logger, saveFailure, embyLogin.Name);
            }
            catch (Exception deleteEx)
            {
                LogDeleteFailed(logger, deleteEx, embyLogin.Name);
            }

            throw new AuthenticationException(InvalidLogin, saveFailure);
        }

        LogAccountCreated(logger, user.Username, settings.AccountAccess);
        return user;
    }

    private async Task<User> SavePasswordAsync(IUserManager userManager, EmbyAuthSettings settings, User user, EmbyLogin embyLogin, string passwordHash)
    {
        user.Password = passwordHash;
        AccountAccessPolicy.ApplyToExistingAccount(user, settings.AccountAccess, embyLogin.EnableRemoteAccess);

        try
        {
            await userManager.UpdateUserAsync(user).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogSaveFailed(logger, ex, user.Username);
            throw new AuthenticationException(InvalidLogin, ex);
        }

        return user;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The plugin refused a blank password for {Username}. The Emby login method needs a password.")]
    private static partial void LogBlankPasswordRefused(ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The plugin refused the login for administrator {Username}. Set the login method of an administrator to Default.")]
    private static partial void LogAdministratorRefused(ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Information, Message = "The plugin did not send the password for {Username} to Emby. Emby user status: {Status}.")]
    private static partial void LogPasswordNotSent(ILogger logger, string username, EmbyUserStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The plugin refused the login for {Username}. Emby accepted the password for Emby user {EmbyUserName}. But the names are different, or the Jellyfin account does not use the Emby login method.")]
    private static partial void LogAccountConflict(ILogger logger, string username, string embyUserName);

    [LoggerMessage(Level = LogLevel.Information, Message = "The plugin created a Jellyfin account for Emby user {EmbyUserName}. Account access: {AccountAccess}.")]
    private static partial void LogAccountCreated(ILogger logger, string embyUserName, AccountAccess accountAccess);

    [LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot create an account for Emby user {EmbyUserName}. If Jellyfin does not allow this user name, rename the user on Emby.")]
    private static partial void LogCreateAccountFailed(ILogger logger, Exception exception, string embyUserName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot save the password for user {Username}. The plugin refused the login.")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception, string username);

    [LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot delete the half-made account for Emby user {EmbyUserName} after a failed save. The account may still exist on the Default login method with no password. Remove it or give it a password.")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, string embyUserName);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Emby Auth plugin refuses all logins on the Emby login method. {Problem} Set it in Dashboard > Plugins > Emby Auth.")]
    private static partial void LogSettingsInvalid(ILogger logger, string problem);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {Username} got a new password in Jellyfin. The user now moves to the configured login method.")]
    private static partial void LogPasswordSetInJellyfin(ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {Username} got a new password in Jellyfin. The user keeps the Emby login method because the configured target is set to remain, so Emby still checks this user's logins.")]
    private static partial void LogPasswordSetTargetRemainsOnEmby(ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Error, Message = "User {Username} got a new password in Jellyfin, but the configured target is not a login method Jellyfin reports as enabled. The user keeps the Emby login method; logins are unaffected.")]
    private static partial void LogPasswordSetTargetInvalid(ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Information, Message = "The password of user {Username} was reset in Jellyfin. The user stays on the Emby login method, so Emby checks the next login.")]
    private static partial void LogPasswordReset(ILogger logger, string username);
}

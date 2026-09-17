using System;
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
/// <param name="serviceProvider">The service provider. The user manager is resolved on each login because it depends on all login methods.</param>
/// <param name="cryptoProvider">The Jellyfin password hasher.</param>
/// <param name="embyClient">The Emby client.</param>
/// <param name="userDirectory">The cached list of Emby users.</param>
/// <param name="verifiedPasswords">The record of password hashes that Emby verified.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class EmbyAuthenticationProvider(
    IServiceProvider serviceProvider,
    ICryptoProvider cryptoProvider,
    EmbyClient embyClient,
    EmbyUserDirectory userDirectory,
    EmbyVerifiedPasswords verifiedPasswords,
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

    /// <inheritdoc />
    public Task<ProviderAuthenticationResult> Authenticate(string username, string password)
    {
        var userManager = serviceProvider.GetRequiredService<IUserManager>();
        return Authenticate(username, password, userManager.GetUserByName(username));
    }

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
        if (settings.MigrationMode == MigrationMode.JellyfinPasswordFirst
            && resolvedUser?.Password is { } savedHash
            && verifiedPasswords.Matches(resolvedUser.Id, savedHash)
            && SavedPasswordMatches(resolvedUser.Username, savedHash, password))
        {
            return new ProviderAuthenticationResult { Username = resolvedUser.Username };
        }

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
    /// A new password is saved, and the user moves to the Default login method.
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
        user.AuthenticationProviderId = DefaultLoginMethod.ProviderId;
        LogPasswordSetInJellyfin(logger, user.Username);
        return Task.CompletedTask;
    }

    private static JellyfinAccount? ToAccount(User? user) =>
        user is null ? null : new JellyfinAccount(user.Username, user.AuthenticationProviderId, user.HasPermission(PermissionKind.IsAdministrator));

    private EmbyAuthSettings GetSettings()
    {
        if (EmbyAuthSettings.TryCreate(EmbyAuthPlugin.Instance?.Configuration, out var settings, out var problem))
        {
            return settings;
        }

        LogSettingsInvalid(logger, problem);
        throw new AuthenticationException(problem);
    }

    private bool SavedPasswordMatches(string username, string savedHash, string password)
    {
        try
        {
            return cryptoProvider.Verify(PasswordHash.Parse(savedHash), password);
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException)
        {
            LogSavedPasswordUnreadable(logger, ex, username);
            return false;
        }
    }

    private async Task<User> CreateAccountAsync(IUserManager userManager, EmbyAuthSettings settings, EmbyLogin embyLogin, string passwordHash)
    {
        User user;
        try
        {
            user = await userManager.CreateUserAsync(embyLogin.Name).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            LogCreateAccountFailed(logger, ex, embyLogin.Name);
            throw new AuthenticationException(InvalidLogin, ex);
        }

        user.AuthenticationProviderId = ProviderId;
        user.Password = passwordHash;
        AccountAccessPolicy.ApplyToNewAccount(user, settings.AccountAccess, embyLogin.EnableRemoteAccess);

        // Jellyfin commits the new account without a password. If this save fails, delete the account so that no blank password opens it.
        var saved = false;
        try
        {
            await userManager.UpdateUserAsync(user).ConfigureAwait(false);
            saved = true;
        }
        catch (Exception ex) when (ex is DbUpdateException or ResourceNotFoundException)
        {
            LogSaveFailed(logger, ex, embyLogin.Name);
            throw new AuthenticationException(InvalidLogin, ex);
        }
        finally
        {
            if (!saved)
            {
                await userManager.DeleteUserAsync(user.Id).ConfigureAwait(false);
            }
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
        catch (Exception ex) when (ex is DbUpdateException or ResourceNotFoundException)
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jellyfin cannot read the saved password of user {Username}. The plugin asks Emby instead.")]
    private static partial void LogSavedPasswordUnreadable(ILogger logger, Exception exception, string username);

    [LoggerMessage(Level = LogLevel.Information, Message = "The plugin created a Jellyfin account for Emby user {EmbyUserName}. Account access: {AccountAccess}.")]
    private static partial void LogAccountCreated(ILogger logger, string embyUserName, AccountAccess accountAccess);

    [LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot create an account for Emby user {EmbyUserName}. If Jellyfin does not allow this user name, rename the user on Emby.")]
    private static partial void LogCreateAccountFailed(ILogger logger, Exception exception, string embyUserName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot save the password for user {Username}. The plugin refused the login.")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception, string username);

    [LoggerMessage(Level = LogLevel.Error, Message = "The Emby Auth plugin refuses all logins on the Emby login method. {Problem} Set it in Dashboard > Plugins > Emby Auth.")]
    private static partial void LogSettingsInvalid(ILogger logger, string problem);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {Username} got a new password in Jellyfin. The user now uses the Default login method.")]
    private static partial void LogPasswordSetInJellyfin(ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Information, Message = "The password of user {Username} was reset in Jellyfin. The user stays on the Emby login method, so Emby checks the next login.")]
    private static partial void LogPasswordReset(ILogger logger, string username);
}

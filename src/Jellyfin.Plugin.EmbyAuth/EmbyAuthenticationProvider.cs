using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// A Jellyfin login method that checks passwords against Emby and keeps a Jellyfin copy of each accepted password.
/// </summary>
/// <param name="serviceProvider">The service provider. The user manager is resolved on each login because it depends on all login methods.</param>
/// <param name="cryptoProvider">The Jellyfin password hasher.</param>
/// <param name="embyClient">The Emby client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class EmbyAuthenticationProvider(
    IServiceProvider serviceProvider,
    ICryptoProvider cryptoProvider,
    EmbyClient embyClient,
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
        var embyServerUrl = GetEmbyServerUrl();
        var embyUserName = await embyClient.AuthenticateAsync(embyServerUrl, username, password, CancellationToken.None).ConfigureAwait(false)
            ?? throw new AuthenticationException(InvalidLogin);

        var userManager = serviceProvider.GetRequiredService<IUserManager>();
        var embyNameUser = userManager.GetUserByName(embyUserName);
        var action = LoginDecision.Decide(ToAccount(resolvedUser), embyUserName, ToAccount(embyNameUser), ProviderId);

        var user = action switch
        {
            LoginAction.UseAccount => resolvedUser ?? embyNameUser!,
            LoginAction.CreateAccount => await CreateAccountAsync(userManager, embyUserName).ConfigureAwait(false),
            _ => throw DenyAccountConflict(username, embyUserName),
        };

        user.Password = string.IsNullOrEmpty(password) ? null : cryptoProvider.CreatePasswordHash(password).ToString();
        await userManager.UpdateUserAsync(user).ConfigureAwait(false);

        return new ProviderAuthenticationResult { Username = user.Username };
    }

    /// <inheritdoc />
    public Task ChangePassword(User user, string newPassword)
    {
        ArgumentNullException.ThrowIfNull(user);
        throw new NotSupportedException(
            $"User {user.Username} uses the Emby login method. Change the password on the Emby server. To change the password in Jellyfin, set the login method of the user to Default.");
    }

    private static JellyfinAccount? ToAccount(User? user) =>
        user is null ? null : new JellyfinAccount(user.Username, user.AuthenticationProviderId);

    private static Uri GetEmbyServerUrl()
    {
        var configured = EmbyAuthPlugin.Instance?.Configuration.EmbyServerUrl;
        if (Uri.TryCreate(configured, UriKind.Absolute, out var url)
            && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps))
        {
            return url;
        }

        throw new AuthenticationException(
            $"The Emby server URL in the Emby Auth plugin settings is not a valid http or https URL: '{configured}'. Set it in Dashboard > Plugins > Emby Auth.");
    }

    private async Task<User> CreateAccountAsync(IUserManager userManager, string embyUserName)
    {
        User user;
        try
        {
            user = await userManager.CreateUserAsync(embyUserName).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            LogCreateAccountFailed(logger, ex, embyUserName);
            throw new AuthenticationException(InvalidLogin, ex);
        }

        user.AuthenticationProviderId = ProviderId;
        LogAccountCreated(logger, embyUserName);
        return user;
    }

    private AuthenticationException DenyAccountConflict(string username, string embyUserName)
    {
        LogAccountConflict(logger, username, embyUserName);
        return new AuthenticationException(InvalidLogin);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The plugin created a Jellyfin account for Emby user {EmbyUserName}. The account uses the Emby login method.")]
    private static partial void LogAccountCreated(ILogger logger, string embyUserName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot create an account for Emby user {EmbyUserName}. Create the account manually. Set its login method to Emby.")]
    private static partial void LogCreateAccountFailed(ILogger logger, Exception exception, string embyUserName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The plugin refused the login for {Username}. Emby accepted the password for Emby user {EmbyUserName}. But the Jellyfin account does not use the Emby login method, or its name is not {EmbyUserName}.")]
    private static partial void LogAccountConflict(ILogger logger, string username, string embyUserName);
}

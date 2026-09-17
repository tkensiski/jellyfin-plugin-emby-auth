using System;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// The Jellyfin account data that decides what happens after Emby accepts a login.
/// </summary>
/// <param name="Username">The Jellyfin user name.</param>
/// <param name="AuthenticationProviderId">The login method that the account uses.</param>
internal sealed record JellyfinAccount(string Username, string AuthenticationProviderId);

/// <summary>
/// The result of <see cref="LoginDecision.Decide"/>.
/// </summary>
internal enum LoginAction
{
    /// <summary>Refuse the login.</summary>
    Deny,

    /// <summary>Log in to the existing Jellyfin account.</summary>
    UseAccount,

    /// <summary>Create a Jellyfin account for the Emby user, then log in to it.</summary>
    CreateAccount,
}

/// <summary>
/// Decides which Jellyfin account an Emby login applies to.
/// </summary>
internal static class LoginDecision
{
    /// <summary>
    /// Decides what to do after Emby accepts a login.
    /// </summary>
    /// <param name="typedAccount">The Jellyfin account with the user name that the person typed, if one exists.</param>
    /// <param name="embyUserName">The user name that Emby returned for the login.</param>
    /// <param name="embyNameAccount">The Jellyfin account with the user name that Emby returned, if one exists.</param>
    /// <param name="bridgeProviderId">The login method ID of this plugin.</param>
    /// <returns>The action to take.</returns>
    public static LoginAction Decide(JellyfinAccount? typedAccount, string embyUserName, JellyfinAccount? embyNameAccount, string bridgeProviderId)
    {
        if (typedAccount is not null && !string.Equals(typedAccount.Username, embyUserName, StringComparison.OrdinalIgnoreCase))
        {
            return LoginAction.Deny;
        }

        var account = typedAccount ?? embyNameAccount;
        if (account is null)
        {
            return LoginAction.CreateAccount;
        }

        return string.Equals(account.AuthenticationProviderId, bridgeProviderId, StringComparison.OrdinalIgnoreCase)
            ? LoginAction.UseAccount
            : LoginAction.Deny;
    }
}

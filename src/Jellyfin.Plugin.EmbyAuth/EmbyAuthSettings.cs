using System;
using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.EmbyAuth.Configuration;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// Validated plugin settings.
/// </summary>
/// <param name="ServerUrl">The base URL of the Emby server.</param>
/// <param name="ApiKey">The Emby API key.</param>
/// <param name="MigrationMode">How the plugin moves users from Emby to Jellyfin.</param>
/// <param name="AccountAccess">The access that the plugin gives to Jellyfin accounts.</param>
/// <param name="MigrationTarget">The login method that the move after a login and the migration task move a user to.</param>
/// <param name="PasswordSetTarget">The login method that the move after an administrator sets a password in Jellyfin moves a user to. Empty means <paramref name="MigrationTarget"/> governs that move too.</param>
internal sealed record EmbyAuthSettings(Uri ServerUrl, string ApiKey, MigrationMode MigrationMode, AccountAccess AccountAccess, string MigrationTarget, string PasswordSetTarget)
{
    /// <summary>
    /// Validates the configured settings.
    /// </summary>
    /// <param name="configuration">The plugin configuration, or <c>null</c> if the plugin is not loaded.</param>
    /// <param name="settings">The settings, if they are valid.</param>
    /// <param name="problem">A message that describes the problem, if the settings are not valid. The message never repeats the configured values.</param>
    /// <returns><c>true</c> if the settings are valid.</returns>
    public static bool TryCreate(PluginConfiguration? configuration, [NotNullWhen(true)] out EmbyAuthSettings? settings, [NotNullWhen(false)] out string? problem)
    {
        settings = null;
        problem = FindProblem(configuration, out var url);
        if (problem is not null)
        {
            return false;
        }

        settings = new EmbyAuthSettings(
            url!,
            configuration!.EmbyApiKey.Trim(),
            configuration.MigrationMode,
            configuration.AccountAccess,
            configuration.MigrationTarget.Trim(),
            configuration.PasswordSetTarget.Trim());
        return true;
    }

    private static string? FindProblem(PluginConfiguration? configuration, out Uri? url)
    {
        url = null;
        if (configuration is null)
        {
            return "The plugin settings are not loaded.";
        }

        if (string.IsNullOrWhiteSpace(configuration.EmbyServerUrl))
        {
            return "The Emby server URL is not set.";
        }

        if (!Uri.TryCreate(configuration.EmbyServerUrl.Trim(), UriKind.Absolute, out url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            return "The Emby server URL is not a valid http or https URL.";
        }

        if (url.UserInfo.Length > 0)
        {
            return "The Emby server URL must not contain a user name or password.";
        }

        if (string.IsNullOrWhiteSpace(configuration.EmbyApiKey))
        {
            return "The Emby API key is not set.";
        }

        if (!Enum.IsDefined(configuration.MigrationMode))
        {
            return "The migration behavior setting is not valid.";
        }

        if (!Enum.IsDefined(configuration.AccountAccess))
        {
            return "The account access setting is not valid.";
        }

        return null;
    }
}

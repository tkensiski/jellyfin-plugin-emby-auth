using System;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// Validated plugin settings.
/// </summary>
/// <param name="ServerUrl">The base URL of the Emby server.</param>
/// <param name="ApiKey">The Emby API key.</param>
internal sealed record EmbyAuthSettings(Uri ServerUrl, string ApiKey)
{
    /// <summary>
    /// Validates the configured settings.
    /// </summary>
    /// <param name="serverUrl">The configured Emby server URL.</param>
    /// <param name="apiKey">The configured Emby API key.</param>
    /// <param name="settings">The settings, if they are valid.</param>
    /// <param name="problem">A message that describes the problem, if the settings are not valid. The message never repeats the configured values.</param>
    /// <returns><c>true</c> if the settings are valid.</returns>
    public static bool TryCreate(string? serverUrl, string? apiKey, [NotNullWhen(true)] out EmbyAuthSettings? settings, [NotNullWhen(false)] out string? problem)
    {
        settings = null;
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            problem = "The Emby server URL is not set.";
            return false;
        }

        if (!Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            problem = "The Emby server URL is not a valid http or https URL.";
            return false;
        }

        if (url.UserInfo.Length > 0)
        {
            problem = "The Emby server URL must not contain a user name or password.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            problem = "The Emby API key is not set.";
            return false;
        }

        settings = new EmbyAuthSettings(url, apiKey.Trim());
        problem = null;
        return true;
    }
}

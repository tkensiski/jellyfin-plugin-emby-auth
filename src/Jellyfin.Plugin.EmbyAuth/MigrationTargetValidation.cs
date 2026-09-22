using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// Checks a plugin configuration's move targets against Jellyfin's live enabled-provider list.
/// </summary>
/// <remarks>
/// This class takes the already-resolved list rather than <see cref="MediaBrowser.Controller.Library.IUserManager"/>
/// so it stays a pure function with no Jellyfin dependency to stand up. It lives apart from
/// <see cref="EmbyAuthSettings"/>, which is deliberately shape-only and free of any live Jellyfin state.
/// </remarks>
internal static class MigrationTargetValidation
{
    /// <summary>
    /// Finds a problem with the configured move targets, checked against Jellyfin's live enabled-provider list.
    /// </summary>
    /// <param name="configuration">The plugin configuration, or <c>null</c> if the plugin is not loaded.</param>
    /// <param name="enabledLoginMethods">The login methods Jellyfin currently reports as enabled.</param>
    /// <returns>A plain-language message that names the setting, or <c>null</c> if both targets are usable.</returns>
    public static string? FindProblem(PluginConfiguration? configuration, IReadOnlyList<NameIdPair> enabledLoginMethods)
    {
        ArgumentNullException.ThrowIfNull(enabledLoginMethods);
        if (configuration is null)
        {
            return "The plugin settings are not loaded.";
        }

        return FindTargetProblem("migration target", configuration.MigrationTarget, enabledLoginMethods)
            ?? FindPasswordSetTargetProblem(configuration.PasswordSetTarget, enabledLoginMethods);
    }

    private static string? FindPasswordSetTargetProblem(string? passwordSetTarget, IReadOnlyList<NameIdPair> enabledLoginMethods) =>
        string.IsNullOrEmpty(passwordSetTarget)
            ? null
            : FindTargetProblem("target used when a password is set in Jellyfin", passwordSetTarget, enabledLoginMethods);

    private static string? FindTargetProblem(string settingName, string? target, IReadOnlyList<NameIdPair> enabledLoginMethods)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return $"The {settingName} is not set.";
        }

        var trimmed = target.Trim();
        if (trimmed == PluginConfiguration.RemainOnEmbyLoginMethod)
        {
            return null;
        }

        if (trimmed == EmbyAuthenticationProvider.ProviderId)
        {
            return $"The {settingName} is set to this plugin's own Emby login method. Use Remain on Emby Login instead.";
        }

        return enabledLoginMethods.Any(method => method.Id == trimmed)
            ? null
            : $"The {settingName} is not a login method Jellyfin reports as enabled.";
    }
}

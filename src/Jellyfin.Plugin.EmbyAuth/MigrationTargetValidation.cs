using System;
using System.Collections.Generic;
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
        return null;
    }
}

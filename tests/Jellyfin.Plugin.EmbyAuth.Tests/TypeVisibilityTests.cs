using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

/// <summary>
/// Guards the plugin assembly's exported surface. <c>Assembly.GetExportedTypes()</c> is the surface another
/// plugin's <c>GetExports&lt;T&gt;()</c> scan can reach, and Jellyfin discovers scheduled tasks and login methods the
/// same way, so a type that becomes public without a recorded reason is a mistake this class catches.
/// </summary>
public sealed class TypeVisibilityTests
{
    /// <summary>
    /// Every type this plugin assembly is allowed to export, each with the reason a Jellyfin or ASP.NET Core scan
    /// needs it public. The assertion below is that every exported type is on this list — never that every listed
    /// type is exported yet — so a type a later plan in this phase adds can be listed here in advance with no
    /// churn to this file when that plan lands.
    /// </summary>
    private static readonly SortedSet<string> AllowedExportedTypeNames = new(StringComparer.Ordinal)
    {
        // Jellyfin discovers the plugin and its DI registrar by scanning exported types.
        "Jellyfin.Plugin.EmbyAuth.EmbyAuthPlugin",
        "Jellyfin.Plugin.EmbyAuth.PluginServiceRegistrator",

        // A constructor parameter of the migration task.
        "Jellyfin.Plugin.EmbyAuth.EmbyVerifiedPasswords",

        // Jellyfin discovers scheduled tasks by exported type. Renamed EmbyMigrationTask by plan 04.
        "Jellyfin.Plugin.EmbyAuth.MoveEmbyUsersToDefaultTask",
        "Jellyfin.Plugin.EmbyAuth.EmbyMigrationTask",

        // Jellyfin serializes the settings with XmlSerializer, and the settings page reads them.
        "Jellyfin.Plugin.EmbyAuth.Configuration.PluginConfiguration",
        "Jellyfin.Plugin.EmbyAuth.Configuration.MigrationMode",
        "Jellyfin.Plugin.EmbyAuth.Configuration.AccountAccess",

        // ASP.NET Core discovers the controller and serializes its response records.
        "Jellyfin.Plugin.EmbyAuth.Api.EmbyAuthController",
        "Jellyfin.Plugin.EmbyAuth.Api.MigrationStatus",
        "Jellyfin.Plugin.EmbyAuth.Api.MigrationUser",

        // Added by plans 03 and 04: the per-user state, and the task-info record for the API response.
        // MigrationUserState lives beside the class that computes it, so the dependency runs from the API to the
        // domain and not back.
        "Jellyfin.Plugin.EmbyAuth.MigrationUserState",
        "Jellyfin.Plugin.EmbyAuth.Api.MigrationTaskInfo",
    };

    [Fact]
    public void EmbyAuthenticationProvider_StaysInternal()
    {
        Assert.False(typeof(EmbyAuthenticationProvider).IsPublic);
    }

    [Fact]
    public void NoTypeJoinsTheExportedSurface_WithoutARecordedReason()
    {
        var exportedTypeNames = typeof(EmbyAuthPlugin).Assembly.GetExportedTypes()
            .Select(type => type.FullName!)
            .ToList();

        var unlisted = exportedTypeNames.Where(name => !AllowedExportedTypeNames.Contains(name)).ToList();

        Assert.True(
            unlisted.Count == 0,
            $"These exported types are not on the allowlist in {nameof(TypeVisibilityTests)}: {string.Join(", ", unlisted)}. "
                + "Make the type internal, or add it to AllowedExportedTypeNames with a one-line reason.");
    }
}

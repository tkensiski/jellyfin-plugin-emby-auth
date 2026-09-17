using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.EmbyAuth.Configuration;

/// <summary>
/// How the plugin moves users from Emby to Jellyfin.
/// </summary>
public enum MigrationMode
{
    /// <summary>
    /// Emby checks the first login. Then the user moves to the Default login method.
    /// </summary>
    MoveAfterFirstLogin,

    /// <summary>
    /// Emby checks every login. Users stay on the Emby login method until an administrator runs the migration task.
    /// </summary>
    KeepEmbyInCharge,

    /// <summary>
    /// Jellyfin checks the saved password first. Emby checks the login only if no saved password matches.
    /// Users stay on the Emby login method until an administrator runs the migration task.
    /// </summary>
    JellyfinPasswordFirst,
}

/// <summary>
/// The access that the plugin gives to Jellyfin accounts.
/// </summary>
public enum AccountAccess
{
    /// <summary>
    /// New accounts get Jellyfin's default permissions and copy Emby's remote access setting. Existing accounts lose remote access when Emby does not allow it.
    /// </summary>
    CopyEmbyRemoteAccess,

    /// <summary>
    /// New accounts get Jellyfin's default permissions. The plugin ignores Emby's remote access setting.
    /// </summary>
    JellyfinDefaults,

    /// <summary>
    /// Like <see cref="CopyEmbyRemoteAccess"/>, but new accounts get no library access until an administrator grants it.
    /// </summary>
    NoLibraries,
}

/// <summary>
/// Settings for the Emby Auth plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the base URL of the Emby server that checks passwords, for example <c>http://emby:8096</c>.
    /// </summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Jellyfin saves plugin settings with XmlSerializer, which cannot serialize System.Uri.")]
    public string EmbyServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Emby API key that the plugin uses to read the list of Emby users.
    /// </summary>
    public string EmbyApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets how the plugin moves users from Emby to Jellyfin.
    /// </summary>
    public MigrationMode MigrationMode { get; set; } = MigrationMode.MoveAfterFirstLogin;

    /// <summary>
    /// Gets or sets the access that the plugin gives to Jellyfin accounts.
    /// </summary>
    public AccountAccess AccountAccess { get; set; } = AccountAccess.CopyEmbyRemoteAccess;
}

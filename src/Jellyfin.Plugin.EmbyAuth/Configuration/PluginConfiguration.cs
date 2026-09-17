using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.EmbyAuth.Configuration;

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
}

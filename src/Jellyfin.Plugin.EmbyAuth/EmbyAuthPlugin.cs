using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// The Emby Auth plugin.
/// </summary>
public class EmbyAuthPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EmbyAuthPlugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    /// <param name="xmlSerializer">The XML serializer.</param>
    public EmbyAuthPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static EmbyAuthPlugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Emby Auth";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("e973e09a-e8b4-40c1-9be2-8e51342de1f9");

    /// <inheritdoc />
    public override string Description => "Checks the first Jellyfin login of each user against an Emby server. Then saves the password in Jellyfin and moves the user to the Default login method.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace),
            },
        ];
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// The Emby Auth plugin.
/// </summary>
public partial class EmbyAuthPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmbyAuthPlugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    /// <param name="xmlSerializer">The XML serializer.</param>
    /// <param name="serviceProvider">
    /// The service provider. Used to resolve <see cref="IUserManager"/> and a logger inside
    /// <see cref="UpdateConfiguration"/>, never in this constructor: <see cref="IUserManager"/> depends on every
    /// login method including this plugin's own, so holding it from construction time would be a cycle.
    /// </param>
    public EmbyAuthPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, IServiceProvider serviceProvider)
        : base(applicationPaths, xmlSerializer)
    {
        _serviceProvider = serviceProvider;
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

    /// <summary>
    /// Saves the settings, after refusing a migration target or password-set target that Jellyfin does not
    /// currently report as an enabled login method.
    /// </summary>
    /// <remarks>
    /// This is the one point every settings save passes through, including a caller that posts straight to
    /// Jellyfin's plugin configuration API without ever loading the settings page. A caller that fails this check
    /// gets nothing written: the previous settings stand.
    /// </remarks>
    /// <param name="configuration">The new configuration.</param>
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        var userManager = _serviceProvider.GetRequiredService<IUserManager>();
        var problem = MigrationTargetValidation.FindProblem(configuration as PluginConfiguration, userManager.GetAuthenticationProviders());
        if (problem is not null)
        {
            var logger = _serviceProvider.GetRequiredService<ILogger<EmbyAuthPlugin>>();
            LogConfigurationRefused(logger, problem);
            throw new ArgumentException(problem, nameof(configuration));
        }

        base.UpdateConfiguration(configuration);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The Emby Auth plugin refused to save its settings. {Problem}")]
    private static partial void LogConfigurationRefused(ILogger logger, string problem);

    /// <summary>
    /// Clears pooled SQLite connections before the plugin's assembly is unloaded. <see cref="SqliteConnection"/>
    /// pooling can otherwise hold the collectible <see cref="System.Runtime.Loader.AssemblyLoadContext"/> a
    /// plugin is loaded into (dotnet/efcore#27498), the same treatment Jellyfin's own SqliteDatabaseProvider
    /// gives its connections at shutdown.
    /// </summary>
    public override void OnUninstalling()
    {
        SqliteConnection.ClearAllPools();
        base.OnUninstalling();
    }
}

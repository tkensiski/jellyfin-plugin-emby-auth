using System;
using System.IO;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// Registers the Emby login method with Jellyfin.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <summary>
    /// The name of the SQLite database file, in the plugin's data folder, that records the password hashes that Emby verified.
    /// </summary>
    public const string VerifiedPasswordsDatabaseFileName = "Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.db";

    /// <summary>
    /// The name of the legacy JSON file, in the plugin's configuration folder, that recorded verified-password
    /// fingerprints before the plugin moved to the SQLite store. The plugin reads this file at most once, to
    /// import its records into the database, and never writes to it.
    /// </summary>
    public const string LegacyVerifiedPasswordsFileName = "Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.json";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<EmbyClient>();
        serviceCollection.AddSingleton<EmbyUserDirectory>();
        serviceCollection.AddSingleton(services => new EmbyVerifiedPasswords(
            // Same PluginsPath + assembly-name derivation Jellyfin uses for BasePlugin.DataFolderPath
            // (MediaBrowser.Common/Plugins/BasePluginOfT.cs:50, tag v12.1), so the database lands in the
            // plugin data folder without depending on EmbyAuthPlugin.Instance being constructed yet.
            Path.Combine(
                services.GetRequiredService<IApplicationPaths>().PluginsPath,
                Path.GetFileNameWithoutExtension(typeof(EmbyVerifiedPasswords).Assembly.Location),
                VerifiedPasswordsDatabaseFileName),
            // The same configuration-folder path the old JSON-backed store wrote to, so a real upgrade finds
            // the file a prior version left behind.
            Path.Combine(
                services.GetRequiredService<IApplicationPaths>().PluginConfigurationsPath,
                LegacyVerifiedPasswordsFileName),
            services.GetRequiredService<ILogger<EmbyVerifiedPasswords>>()));
        serviceCollection.AddSingleton<Func<PluginConfiguration?>>(_ => () => EmbyAuthPlugin.Instance?.Configuration);
        serviceCollection.AddSingleton<IAuthenticationProvider>(services => new EmbyAuthenticationProvider(
            services,
            services.GetRequiredService<ICryptoProvider>(),
            services.GetRequiredService<EmbyClient>(),
            services.GetRequiredService<EmbyUserDirectory>(),
            services.GetRequiredService<EmbyVerifiedPasswords>(),
            services.GetRequiredService<Func<PluginConfiguration?>>(),
            services.GetRequiredService<ILogger<EmbyAuthenticationProvider>>()));
        serviceCollection.AddScoped<IEventConsumer<AuthenticationResultEventArgs>, MoveAfterLogin>();
    }
}

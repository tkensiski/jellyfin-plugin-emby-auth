using System;
using System.IO;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Plugins;
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
    /// The name of the file, in Jellyfin's plugin configuration folder, that records the password hashes that Emby verified.
    /// </summary>
    public const string VerifiedPasswordsFileName = "Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.json";

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<EmbyClient>();
        serviceCollection.AddSingleton<EmbyUserDirectory>();
        serviceCollection.AddSingleton(services => new EmbyVerifiedPasswords(
            Path.Combine(services.GetRequiredService<IApplicationPaths>().PluginConfigurationsPath, VerifiedPasswordsFileName),
            services.GetRequiredService<ILogger<EmbyVerifiedPasswords>>()));
        serviceCollection.AddSingleton<IAuthenticationProvider, EmbyAuthenticationProvider>();
        serviceCollection.AddScoped<IEventConsumer<AuthenticationResultEventArgs>, MoveToDefaultLoginMethod>();
    }
}

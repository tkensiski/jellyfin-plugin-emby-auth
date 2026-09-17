using System;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// Registers the Emby login method with Jellyfin.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<EmbyClient>();
        serviceCollection.AddSingleton<EmbyUserDirectory>();
        serviceCollection.AddSingleton<VerifiedLogins>();
        serviceCollection.AddSingleton<IAuthenticationProvider, EmbyAuthenticationProvider>();
        serviceCollection.AddScoped<IEventConsumer<AuthenticationResultEventArgs>, MoveToDefaultLoginMethod>();
    }
}

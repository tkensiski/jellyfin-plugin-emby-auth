using System.Reflection;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public sealed class EmbyAuthPluginTests
{
    /// <summary>
    /// Constructing <see cref="EmbyAuthPlugin"/> needs <c>IApplicationPaths</c> and <c>IXmlSerializer</c> fakes and
    /// would write an XML file. The end-to-end proof that <see cref="EmbyAuthPlugin.UpdateConfiguration"/> actually
    /// refuses a save belongs to plan 07's real server, not to a unit test standing up half of Jellyfin. This test
    /// only guards that the override exists, so a save always passes through it.
    /// </summary>
    [Fact]
    public void DeclaresItsOwnUpdateConfigurationOverride()
    {
        var method = typeof(EmbyAuthPlugin).GetMethod(
            nameof(EmbyAuthPlugin.UpdateConfiguration),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.NotNull(method);
    }
}

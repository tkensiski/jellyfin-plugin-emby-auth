using System;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public class EmbyAuthSettingsTests
{
    [Theory]
    [InlineData("http://emby:8096")]
    [InlineData("https://emby.example.com/emby/")]
    public void Accepts_HttpAndHttpsUrlsWithAnApiKey(string url)
    {
        var ok = EmbyAuthSettings.TryCreate(url, "0123456789abcdef", out var settings, out var problem);

        Assert.True(ok);
        Assert.Null(problem);
        Assert.Equal(new Uri(url), settings!.ServerUrl);
        Assert.Equal("0123456789abcdef", settings.ApiKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_MissingUrl(string? url)
    {
        var ok = EmbyAuthSettings.TryCreate(url, "key", out var settings, out var problem);

        Assert.False(ok);
        Assert.Null(settings);
        Assert.Equal("The Emby server URL is not set.", problem);
    }

    [Theory]
    [InlineData("emby:8096")]
    [InlineData("ftp://emby:8096")]
    [InlineData("/emby")]
    public void Rejects_UrlThatIsNotHttpOrHttps(string url)
    {
        var ok = EmbyAuthSettings.TryCreate(url, "key", out _, out var problem);

        Assert.False(ok);
        Assert.Equal("The Emby server URL is not a valid http or https URL.", problem);
    }

    [Fact]
    public void Rejects_UrlWithCredentials_WithoutRepeatingThem()
    {
        var ok = EmbyAuthSettings.TryCreate("https://svc:s3cret@emby.example.com", "key", out _, out var problem);

        Assert.False(ok);
        Assert.Equal("The Emby server URL must not contain a user name or password.", problem);
        Assert.DoesNotContain("s3cret", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Rejects_MissingApiKey(string? apiKey)
    {
        var ok = EmbyAuthSettings.TryCreate("http://emby:8096", apiKey, out _, out var problem);

        Assert.False(ok);
        Assert.Equal("The Emby API key is not set.", problem);
    }
}

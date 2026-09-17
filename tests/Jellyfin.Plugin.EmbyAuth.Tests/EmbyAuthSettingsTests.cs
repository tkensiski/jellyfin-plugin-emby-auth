using System;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public class EmbyAuthSettingsTests
{
    private static PluginConfiguration Config(
        string? url = "http://emby:8096",
        string? apiKey = "0123456789abcdef",
        MigrationMode mode = MigrationMode.MoveAfterFirstLogin,
        AccountAccess access = AccountAccess.CopyEmbyRemoteAccess) => new()
        {
            EmbyServerUrl = url!,
            EmbyApiKey = apiKey!,
            MigrationMode = mode,
            AccountAccess = access,
        };

    [Theory]
    [InlineData("http://emby:8096")]
    [InlineData("https://emby.example.com/emby/")]
    public void Accepts_HttpAndHttpsUrlsWithAnApiKey(string url)
    {
        var ok = EmbyAuthSettings.TryCreate(Config(url: url), out var settings, out var problem);

        Assert.True(ok);
        Assert.Null(problem);
        Assert.Equal(new Uri(url), settings!.ServerUrl);
        Assert.Equal("0123456789abcdef", settings.ApiKey);
    }

    [Fact]
    public void CarriesTheChosenMigrationModeAndAccountAccess()
    {
        var ok = EmbyAuthSettings.TryCreate(Config(mode: MigrationMode.KeepEmbyInCharge, access: AccountAccess.NoLibraries), out var settings, out _);

        Assert.True(ok);
        Assert.Equal(MigrationMode.KeepEmbyInCharge, settings!.MigrationMode);
        Assert.Equal(AccountAccess.NoLibraries, settings.AccountAccess);
    }

    [Fact]
    public void DefaultsToMoveAfterFirstLoginAndCopyEmbyRemoteAccess()
    {
        var configuration = new PluginConfiguration();

        Assert.Equal(MigrationMode.MoveAfterFirstLogin, configuration.MigrationMode);
        Assert.Equal(AccountAccess.CopyEmbyRemoteAccess, configuration.AccountAccess);
    }

    [Fact]
    public void Rejects_MissingConfiguration()
    {
        var ok = EmbyAuthSettings.TryCreate(null, out var settings, out var problem);

        Assert.False(ok);
        Assert.Null(settings);
        Assert.Equal("The plugin settings are not loaded.", problem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_MissingUrl(string? url)
    {
        var ok = EmbyAuthSettings.TryCreate(Config(url: url), out var settings, out var problem);

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
        var ok = EmbyAuthSettings.TryCreate(Config(url: url), out _, out var problem);

        Assert.False(ok);
        Assert.Equal("The Emby server URL is not a valid http or https URL.", problem);
    }

    [Fact]
    public void Rejects_UrlWithCredentials_WithoutRepeatingThem()
    {
        var ok = EmbyAuthSettings.TryCreate(Config(url: "https://svc:s3cret@emby.example.com"), out _, out var problem);

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
        var ok = EmbyAuthSettings.TryCreate(Config(apiKey: apiKey), out _, out var problem);

        Assert.False(ok);
        Assert.Equal("The Emby API key is not set.", problem);
    }

    [Fact]
    public void Rejects_UnknownMigrationMode()
    {
        var ok = EmbyAuthSettings.TryCreate(Config(mode: (MigrationMode)99), out _, out var problem);

        Assert.False(ok);
        Assert.Equal("The migration behavior setting is not valid.", problem);
    }

    [Fact]
    public void Rejects_UnknownAccountAccess()
    {
        var ok = EmbyAuthSettings.TryCreate(Config(access: (AccountAccess)99), out _, out var problem);

        Assert.False(ok);
        Assert.Equal("The account access setting is not valid.", problem);
    }
}

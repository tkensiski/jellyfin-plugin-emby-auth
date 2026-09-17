using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public class EmbyUserDirectoryTests
{
    private static readonly EmbyAuthSettings Settings =
        new(new Uri("http://emby:8096"), "key-1", MigrationMode.MoveAfterFirstLogin, AccountAccess.CopyEmbyRemoteAccess);

    private readonly ManualTimeProvider _clock = new();

    private static HttpResponseMessage UserList() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            """[{"Name":"Alice","Policy":{"IsDisabled":false}},{"Name":"ivy","Policy":{"IsDisabled":true}}]""",
            Encoding.UTF8,
            "application/json"),
    };

    private EmbyUserDirectory CreateDirectory(StubHttpMessageHandler handler) =>
        new(new EmbyClient(new StubHttpClientFactory(handler), NullLogger<EmbyClient>.Instance), _clock, NullLogger<EmbyUserDirectory>.Instance);

    [Theory]
    [InlineData("Alice")]
    [InlineData("alice")]
    public async Task ReturnsActive_WhenEmbyHasTheUser(string username)
    {
        var directory = CreateDirectory(new StubHttpMessageHandler().Then(UserList));

        Assert.Equal(EmbyUserStatus.Active, await directory.GetStatusAsync(Settings, username, CancellationToken.None));
    }

    [Theory]
    [InlineData("alice ")]
    [InlineData(" alice")]
    [InlineData("bob")]
    [InlineData("")]
    public async Task ReturnsNotFound_WhenNoEmbyUserHasExactlyThatName(string username)
    {
        var directory = CreateDirectory(new StubHttpMessageHandler().Then(UserList));

        Assert.Equal(EmbyUserStatus.NotFound, await directory.GetStatusAsync(Settings, username, CancellationToken.None));
    }

    [Fact]
    public async Task ReturnsDisabled_WhenEmbyUserIsDisabled()
    {
        var directory = CreateDirectory(new StubHttpMessageHandler().Then(UserList));

        Assert.Equal(EmbyUserStatus.Disabled, await directory.GetStatusAsync(Settings, "ivy", CancellationToken.None));
    }

    [Fact]
    public async Task UsesCachedList_WithinCacheDuration()
    {
        var handler = new StubHttpMessageHandler().Then(UserList);
        var directory = CreateDirectory(handler);

        await directory.GetStatusAsync(Settings, "alice", CancellationToken.None);
        _clock.Advance(EmbyUserDirectory.CacheDuration - TimeSpan.FromSeconds(1));
        await directory.GetStatusAsync(Settings, "ivy", CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RefreshesList_AfterCacheDuration()
    {
        var handler = new StubHttpMessageHandler().Then(UserList).Then(UserList);
        var directory = CreateDirectory(handler);

        await directory.GetStatusAsync(Settings, "alice", CancellationToken.None);
        _clock.Advance(EmbyUserDirectory.CacheDuration);
        await directory.GetStatusAsync(Settings, "alice", CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task RefreshesList_WhenSettingsChange()
    {
        var handler = new StubHttpMessageHandler().Then(UserList).Then(UserList);
        var directory = CreateDirectory(handler);

        await directory.GetStatusAsync(Settings, "alice", CancellationToken.None);
        await directory.GetStatusAsync(Settings with { ApiKey = "key-2" }, "alice", CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("key-2", handler.Requests[1].EmbyToken);
    }

    [Fact]
    public async Task KeepsCachedList_WhenOnlyTheMigrationSettingsChange()
    {
        var handler = new StubHttpMessageHandler().Then(UserList);
        var directory = CreateDirectory(handler);

        await directory.GetStatusAsync(Settings, "alice", CancellationToken.None);
        await directory.GetStatusAsync(Settings with { MigrationMode = MigrationMode.KeepEmbyInCharge, AccountAccess = AccountAccess.NoLibraries }, "alice", CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ReturnsUnavailable_WhenListRequestFails()
    {
        var directory = CreateDirectory(new StubHttpMessageHandler().Then(() => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        Assert.Equal(EmbyUserStatus.Unavailable, await directory.GetStatusAsync(Settings, "alice", CancellationToken.None));
    }

    [Fact]
    public async Task DoesNotContactEmby_WithinRetryDelayAfterFailure()
    {
        var handler = new StubHttpMessageHandler().Then(() => throw new HttpRequestException("Connection refused"));
        var directory = CreateDirectory(handler);

        await directory.GetStatusAsync(Settings, "alice", CancellationToken.None);
        _clock.Advance(EmbyUserDirectory.RetryDelay - TimeSpan.FromSeconds(1));
        var status = await directory.GetStatusAsync(Settings, "alice", CancellationToken.None);

        Assert.Equal(EmbyUserStatus.Unavailable, status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RetriesList_AfterRetryDelay()
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => throw new HttpRequestException("Connection refused"))
            .Then(UserList);
        var directory = CreateDirectory(handler);

        await directory.GetStatusAsync(Settings, "alice", CancellationToken.None);
        _clock.Advance(EmbyUserDirectory.RetryDelay);
        var status = await directory.GetStatusAsync(Settings, "alice", CancellationToken.None);

        Assert.Equal(EmbyUserStatus.Active, status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task KeepsNoStaleList_AfterRefreshFails()
    {
        var handler = new StubHttpMessageHandler()
            .Then(UserList)
            .Then(() => throw new HttpRequestException("Connection refused"));
        var directory = CreateDirectory(handler);

        await directory.GetStatusAsync(Settings, "alice", CancellationToken.None);
        _clock.Advance(EmbyUserDirectory.CacheDuration);
        var status = await directory.GetStatusAsync(Settings, "alice", CancellationToken.None);

        Assert.Equal(EmbyUserStatus.Unavailable, status);
    }
}

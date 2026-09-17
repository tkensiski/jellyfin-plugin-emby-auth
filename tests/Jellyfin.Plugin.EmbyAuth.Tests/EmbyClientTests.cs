using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public class EmbyClientTests
{
    private static readonly Uri EmbyUrl = new("http://emby:8096");

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage LoginAccepted(string name = "alice", string token = "emby-token") =>
        Json(HttpStatusCode.OK, $$"""{"User":{"Name":"{{name}}","Id":"1"},"AccessToken":"{{token}}"}""");

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    private static EmbyClient CreateClient(StubHttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), NullLogger<EmbyClient>.Instance);

    [Fact]
    public async Task ReturnsEmbyUserName_WhenEmbyAcceptsLogin()
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => LoginAccepted(name: "Alice"))
            .Then(() => Status(HttpStatusCode.NoContent));

        var name = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", "pw", CancellationToken.None);

        Assert.Equal("Alice", name);
    }

    [Fact]
    public async Task SendsCredentialsToAuthenticateByName()
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => LoginAccepted())
            .Then(() => Status(HttpStatusCode.NoContent));

        await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", "s3cret \"pw\"", CancellationToken.None);

        var login = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, login.Method);
        Assert.Equal(new Uri("http://emby:8096/Users/AuthenticateByName"), login.Uri);
        Assert.StartsWith("MediaBrowser ", login.Authorization, StringComparison.Ordinal);
        Assert.Contains("DeviceId=", login.Authorization, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(login.Body!);
        Assert.Equal("alice", body.RootElement.GetProperty("Username").GetString());
        Assert.Equal("s3cret \"pw\"", body.RootElement.GetProperty("Pw").GetString());
    }

    [Fact]
    public async Task SendsLoginBodyWithContentLength_BecauseEmbyRejectsChunkedBodies()
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => LoginAccepted())
            .Then(() => Status(HttpStatusCode.NoContent));

        await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", "pw", CancellationToken.None);

        var login = handler.Requests[0];
        Assert.Equal(Encoding.UTF8.GetByteCount(login.Body!), login.ContentLength);
    }

    [Theory]
    [InlineData("http://emby:8096/", "http://emby:8096/Users/AuthenticateByName")]
    [InlineData("http://emby:8096/emby", "http://emby:8096/emby/Users/AuthenticateByName")]
    [InlineData("http://emby:8096/emby/", "http://emby:8096/emby/Users/AuthenticateByName")]
    public async Task KeepsBasePathOfEmbyUrl(string embyUrl, string expectedLoginUrl)
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => LoginAccepted())
            .Then(() => Status(HttpStatusCode.NoContent));

        await CreateClient(handler).AuthenticateAsync(new Uri(embyUrl), "alice", "pw", CancellationToken.None);

        Assert.Equal(new Uri(expectedLoginUrl), handler.Requests[0].Uri);
    }

    [Fact]
    public async Task SignsOutOfEmbyAfterLogin()
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => LoginAccepted(token: "abc123"))
            .Then(() => Status(HttpStatusCode.NoContent));

        await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", "pw", CancellationToken.None);

        var logout = Assert.Single(handler.Requests.Skip(1));
        Assert.Equal(HttpMethod.Post, logout.Method);
        Assert.Equal(new Uri("http://emby:8096/Sessions/Logout"), logout.Uri);
        Assert.Contains("Token=\"abc123\"", logout.Authorization, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReturnsEmbyUserName_WhenSignOutFails()
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => LoginAccepted())
            .Then(() => Status(HttpStatusCode.InternalServerError));

        var name = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", "pw", CancellationToken.None);

        Assert.Equal("alice", name);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReturnsNull_WhenEmbyRejectsLogin(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler().Then(() => Status(status));

        var name = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", "wrong", CancellationToken.None);

        Assert.Null(name);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ReturnsNull_WhenEmbyIsUnreachable()
    {
        var handler = new StubHttpMessageHandler().Then(() => throw new HttpRequestException("Connection refused"));

        var name = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", "pw", CancellationToken.None);

        Assert.Null(name);
    }

    [Fact]
    public async Task ReturnsNull_WhenEmbyTimesOut()
    {
        var handler = new StubHttpMessageHandler().Then(() => throw new TaskCanceledException("Timed out"));

        var name = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", "pw", CancellationToken.None);

        Assert.Null(name);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"User":{"Name":""},"AccessToken":"t"}""")]
    [InlineData("not json")]
    public async Task ReturnsNull_WhenEmbyResponseHasNoUserName(string json)
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => Json(HttpStatusCode.OK, json))
            .Then(() => Status(HttpStatusCode.NoContent));

        var name = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", "pw", CancellationToken.None);

        Assert.Null(name);
    }
}

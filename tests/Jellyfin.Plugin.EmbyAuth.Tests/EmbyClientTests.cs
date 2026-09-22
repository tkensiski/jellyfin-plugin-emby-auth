using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public class EmbyClientTests
{
    private const string Password = "s3cret-pw";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private static readonly Uri EmbyUrl = new("http://emby:8096");

    private readonly CapturingLogger<EmbyClient> _logger = new();

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage LoginAccepted(string name = "alice", string token = "emby-token", bool remoteAccess = true)
    {
        var remote = remoteAccess ? "true" : "false";
        return Json(HttpStatusCode.OK, $$$"""{"User":{"Name":"{{{name}}}","Id":"1","Policy":{"EnableRemoteAccess":{{{remote}}}}},"AccessToken":"{{{token}}}"}""");
    }

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    private EmbyClient CreateClient(StubHttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler), _logger);

    private void AssertNoSecretsLogged()
    {
        Assert.All(_logger.Entries, entry =>
        {
            Assert.DoesNotContain(Password, entry, StringComparison.Ordinal);
            Assert.DoesNotContain(ApiKey, entry, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Login_ReturnsEmbyUserNameAndRemoteAccess_WhenEmbyAcceptsLogin()
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => LoginAccepted(name: "Alice", remoteAccess: false))
            .Then(() => Status(HttpStatusCode.NoContent));

        var login = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

        Assert.Equal(new EmbyLogin("Alice", EnableRemoteAccess: false), login);
    }

    [Fact]
    public async Task Login_TreatsMissingPolicyAsNoRemoteAccess()
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => Json(HttpStatusCode.OK, """{"User":{"Name":"alice"},"AccessToken":"t"}"""))
            .Then(() => Status(HttpStatusCode.NoContent));

        var login = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

        Assert.Equal(new EmbyLogin("alice", EnableRemoteAccess: false), login);
    }

    [Fact]
    public async Task Login_SendsCredentialsToAuthenticateByName()
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
    public async Task Login_SendsBodyWithContentLength_BecauseEmbyRejectsChunkedBodies()
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => LoginAccepted())
            .Then(() => Status(HttpStatusCode.NoContent));

        await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

        var login = handler.Requests[0];
        Assert.Equal(Encoding.UTF8.GetByteCount(login.Body!), login.ContentLength);
    }

    [Theory]
    [InlineData("http://emby:8096/", "http://emby:8096/Users/AuthenticateByName")]
    [InlineData("http://emby:8096/emby", "http://emby:8096/emby/Users/AuthenticateByName")]
    [InlineData("http://emby:8096/emby/", "http://emby:8096/emby/Users/AuthenticateByName")]
    public async Task Login_KeepsBasePathOfEmbyUrl(string embyUrl, string expectedLoginUrl)
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => LoginAccepted())
            .Then(() => Status(HttpStatusCode.NoContent));

        await CreateClient(handler).AuthenticateAsync(new Uri(embyUrl), "alice", Password, CancellationToken.None);

        Assert.Equal(new Uri(expectedLoginUrl), handler.Requests[0].Uri);
    }

    [Fact]
    public async Task Login_SignsOutOfEmbyAfterLogin()
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => LoginAccepted(token: "abc123"))
            .Then(() => Status(HttpStatusCode.NoContent));

        await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

        var logout = Assert.Single(handler.Requests.Skip(1));
        Assert.Equal(HttpMethod.Post, logout.Method);
        Assert.Equal(new Uri("http://emby:8096/Sessions/Logout"), logout.Uri);
        Assert.Contains("Token=\"abc123\"", logout.Authorization, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_ReturnsLogin_WhenSignOutFails()
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => LoginAccepted())
            .Then(() => Status(HttpStatusCode.InternalServerError));

        var login = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

        Assert.Equal("alice", login?.Name);
    }

    [Fact]
    public async Task Login_NamesTheTypedUserName_WhenTheSignOutFailsForAResponseWithoutAUserName()
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => Json(HttpStatusCode.OK, """{"User":{"Name":""},"AccessToken":"logout-token-789"}"""))
            .Then(() => throw new HttpRequestException("Connection refused"));

        var login = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

        Assert.Null(login);
        Assert.Contains(_logger.Entries, entry => entry.Contains("alice", StringComparison.Ordinal));
        Assert.All(_logger.Entries, entry => Assert.DoesNotContain("logout-token-789", entry, StringComparison.Ordinal));
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task Login_DoesNotEndASession_WhenEmbyReturnsNoToken()
    {
        var handler = new StubHttpMessageHandler().Then(() => Json(HttpStatusCode.OK, """{"User":{"Name":"alice"}}"""));

        var login = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

        Assert.Equal(new EmbyLogin("alice", EnableRemoteAccess: false), login);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Login_ReturnsNull_WhenEmbyRejectsLogin(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler().Then(() => Status(status));

        var login = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

        Assert.Null(login);
        Assert.Single(handler.Requests);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task Login_ReturnsNull_WhenEmbyIsUnreachable()
    {
        var handler = new StubHttpMessageHandler().Then(() => throw new HttpRequestException("Connection refused"));

        var login = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

        Assert.Null(login);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task Login_ReturnsNull_WhenEmbyTimesOut()
    {
        var handler = new StubHttpMessageHandler().Then(() => throw new TaskCanceledException("Timed out"));

        var login = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

        Assert.Null(login);
    }

    /// <summary>
    /// Both remaining rows carry no <c>AccessToken</c>, so neither can send a sign-out.
    /// </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    public async Task Login_ReturnsNull_WhenEmbyResponseHasNoUserName(string json)
    {
        var handler = new StubHttpMessageHandler().Then(() => Json(HttpStatusCode.OK, json));

        var login = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

        Assert.Null(login);
        Assert.Single(handler.Requests);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task Login_EndsTheEmbySession_WhenTheResponseHasATokenAndNoUserName()
    {
        var handler = new StubHttpMessageHandler()
            .Then(() => Json(HttpStatusCode.OK, """{"User":{"Name":""},"AccessToken":"t"}"""))
            .Then(() => Status(HttpStatusCode.NoContent));

        var login = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

        Assert.Null(login);
        Assert.Equal(2, handler.Requests.Count);
        var logout = handler.Requests[1];
        Assert.EndsWith("Sessions/Logout", logout.Uri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("Token=\"t\"", logout.Authorization, StringComparison.Ordinal);
        AssertNoSecretsLogged();
    }

    /// <summary>
    /// A body Jellyfin cannot parse hides the token inside it, so the plugin cannot end that Emby session. This is
    /// the documented limit (D-02), not a defect: no code buffers or hand-parses a body that deserialization rejected.
    /// </summary>
    [Fact]
    public async Task Login_ReturnsNull_WhenEmbyResponseHasAnInvalidCharset()
    {
        var handler = new StubHttpMessageHandler().Then(() =>
        {
            var response = Json(HttpStatusCode.OK, """{"User":{"Name":"alice"},"AccessToken":"t"}""");
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=bogus");
            return response;
        });

        var login = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

        Assert.Null(login);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Users_SendsApiKeyToUsersEndpoint()
    {
        var handler = new StubHttpMessageHandler().Then(() => Json(HttpStatusCode.OK, "[]"));

        await CreateClient(handler).GetUsersAsync(EmbyUrl, ApiKey, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("http://emby:8096/Users"), request.Uri);
        Assert.Equal(ApiKey, request.EmbyToken);
    }

    [Fact]
    public async Task Users_ReturnsNamesAndDisabledState()
    {
        var handler = new StubHttpMessageHandler().Then(() => Json(
            HttpStatusCode.OK,
            """[{"Name":"alice","Policy":{"IsDisabled":false}},{"Name":"ivy","Policy":{"IsDisabled":true}},{"Name":""},{"Policy":{}}]"""));

        var users = await CreateClient(handler).GetUsersAsync(EmbyUrl, ApiKey, CancellationToken.None);

        Assert.Equal([new EmbyUser("alice", IsDisabled: false), new EmbyUser("ivy", IsDisabled: true)], users);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Users_ReturnsNull_WhenEmbyRejectsRequest(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler().Then(() => Status(status));

        var users = await CreateClient(handler).GetUsersAsync(EmbyUrl, ApiKey, CancellationToken.None);

        Assert.Null(users);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task Users_ReturnsNull_WhenEmbyIsUnreachable()
    {
        var handler = new StubHttpMessageHandler().Then(() => throw new HttpRequestException("Connection refused"));

        var users = await CreateClient(handler).GetUsersAsync(EmbyUrl, ApiKey, CancellationToken.None);

        Assert.Null(users);
        AssertNoSecretsLogged();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    public async Task Users_ReturnsNull_WhenResponseIsNotAUserList(string json)
    {
        var handler = new StubHttpMessageHandler().Then(() => Json(HttpStatusCode.OK, json));

        var users = await CreateClient(handler).GetUsersAsync(EmbyUrl, ApiKey, CancellationToken.None);

        Assert.Null(users);
    }
}

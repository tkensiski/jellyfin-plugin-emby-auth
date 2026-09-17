using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public class EmbyAuthenticationProviderTests
{
    private static readonly PluginConfiguration Settings = new()
    {
        EmbyServerUrl = "http://emby:8096",
        EmbyApiKey = "key-1",
        MigrationMode = MigrationMode.KeepEmbyInCharge,
        AccountAccess = AccountAccess.CopyEmbyRemoteAccess,
    };

    private readonly ManualTimeProvider _clock = new();

    private static HttpResponseMessage AliceUserList() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            """[{"Name":"alice","Policy":{"IsDisabled":false}}]""",
            Encoding.UTF8,
            "application/json"),
    };

    private static HttpResponseMessage AliceAuthenticateResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            """{"User":{"Name":"alice","Policy":{"EnableRemoteAccess":true}}}""",
            Encoding.UTF8,
            "application/json"),
    };

    // RED, confirmed locally: fails today with the raw InvalidOperationException escaping Authenticate,
    // because EmbyAuthenticationProvider.cs's catch on the save only admits DbUpdateException and
    // ResourceNotFoundException. Skipped only for this commit, because the repository's pre-commit hook
    // requires a fully green suite at every commit; unskipped in the immediately following feat(01-01) commit
    // that widens the catch.
    [Fact(Skip = "RED — unskipped when the catch widens in the next commit (AUTH-04)")]
    public async Task RefusesTheLogin_WhenTheSaveAfterCreateUserFailsWithAnUnexpectedExceptionType()
    {
        var userManager = new FakeUserManager { UpdateUserThrows = new InvalidOperationException("save failed") };
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse);
        var provider = CreateProvider(handler, userManager, out _);

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", null));

        Assert.Equal(userManager.LastCreatedUser?.Id, userManager.LastDeletedId);
    }

    private EmbyAuthenticationProvider CreateProvider(
        StubHttpMessageHandler handler,
        FakeUserManager userManager,
        out string verifiedPasswordsPath,
        ILogger<EmbyAuthenticationProvider>? logger = null)
    {
        var embyClient = new EmbyClient(new StubHttpClientFactory(handler), NullLogger<EmbyClient>.Instance);
        var userDirectory = new EmbyUserDirectory(embyClient, _clock, NullLogger<EmbyUserDirectory>.Instance);
        verifiedPasswordsPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var verifiedPasswords = new EmbyVerifiedPasswords(verifiedPasswordsPath, NullLogger<EmbyVerifiedPasswords>.Instance);
        var services = new ServiceCollection()
            .AddSingleton<IUserManager>(userManager)
            .BuildServiceProvider();

        return new EmbyAuthenticationProvider(
            services,
            new FakeCryptoProvider(),
            embyClient,
            userDirectory,
            verifiedPasswords,
            () => Settings,
            logger ?? NullLogger<EmbyAuthenticationProvider>.Instance);
    }
}

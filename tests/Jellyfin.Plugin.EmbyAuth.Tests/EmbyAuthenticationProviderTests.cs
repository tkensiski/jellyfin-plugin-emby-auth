using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using Microsoft.EntityFrameworkCore;
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

    [Fact]
    public async Task RefusesTheLogin_WhenTheSaveAfterCreateUserFailsWithAnUnexpectedExceptionType()
    {
        var userManager = new FakeUserManager { UpdateUserThrows = new InvalidOperationException("save failed") };
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse);
        var provider = CreateProvider(handler, userManager, out _);

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", null));

        Assert.Equal(userManager.LastCreatedUser?.Id, userManager.LastDeletedId);
    }

    [Fact]
    public async Task RefusesTheLogin_WhenTheSaveAfterCreateUserFailsWithACaughtExceptionType()
    {
        var userManager = new FakeUserManager { UpdateUserThrows = new DbUpdateException("save failed") };
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse);
        var provider = CreateProvider(handler, userManager, out _);

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", null));

        Assert.Equal(userManager.LastCreatedUser?.Id, userManager.LastDeletedId);
    }

    // RED, confirmed locally: the cleanup DeleteUserAsync sits in a finally with no surrounding
    // try/catch, so its ResourceNotFoundException replaces the AuthenticationException on the way
    // out. Skipped only for this commit (see the previous test's comment); unskipped in the next
    // commit that guards the cleanup delete.
    [Fact(Skip = "RED — unskipped when the cleanup delete is guarded in the next commit (AUTH-04)")]
    public async Task RefusesTheLogin_WhenTheSaveAndTheCleanupDeleteBothFail()
    {
        var userManager = new FakeUserManager
        {
            UpdateUserThrows = new DbUpdateException("save failed"),
            DeleteUserThrows = new ResourceNotFoundException("userId"),
        };
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse);
        var logger = new CapturingLogger<EmbyAuthenticationProvider>();
        var provider = CreateProvider(handler, userManager, out _, logger);

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", null));

        var errorEntries = logger.Entries.Where(entry => entry.StartsWith("Error:", StringComparison.Ordinal)).ToList();
        var errorEntry = Assert.Single(errorEntries);
        Assert.Contains("alice", errorEntry, StringComparison.Ordinal);

        var typedPassword = "alice-pass";
        var savedHash = new FakeCryptoProvider().CreatePasswordHash(typedPassword).ToString();
        Assert.DoesNotContain(logger.Entries, entry => entry.Contains(typedPassword, StringComparison.Ordinal) || entry.Contains(savedHash, StringComparison.Ordinal));
    }

    // RED, confirmed locally: SavePasswordAsync's catch has the same narrow filter as
    // CreateAccountAsync had before the previous plan's fix. Skipped only for this commit;
    // unskipped in the next commit that widens this catch too.
    [Fact(Skip = "RED — unskipped when SavePasswordAsync's catch widens in the next commit (AUTH-04)")]
    public async Task RefusesTheLogin_WhenSavingThePasswordOfAnExistingAccountFailsWithAnUnexpectedExceptionType()
    {
        var userManager = new FakeUserManager { UpdateUserThrows = new InvalidOperationException("save failed") };
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse);
        var provider = CreateProvider(handler, userManager, out _);
        var existingUser = new User("alice", EmbyAuthenticationProvider.ProviderId, "reset-provider");
        existingUser.AddDefaultPermissions();
        existingUser.AddDefaultPreferences();

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", existingUser));
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

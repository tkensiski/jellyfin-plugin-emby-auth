using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
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

    private static HttpResponseMessage BobAuthenticateResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            """{"User":{"Name":"bob","Policy":{"EnableRemoteAccess":true}}}""",
            Encoding.UTF8,
            "application/json"),
    };

    private static HttpResponseMessage DisabledAliceUserList() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            """[{"Name":"alice","Policy":{"IsDisabled":true}}]""",
            Encoding.UTF8,
            "application/json"),
    };

    private static HttpResponseMessage EmptyUserList() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("[]", Encoding.UTF8, "application/json"),
    };

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task RefusesTheLogin_AndDoesNotContactEmby_WhenThePasswordIsBlank(string? password)
    {
        var userManager = new FakeUserManager();
        var handler = new StubHttpMessageHandler();
        var provider = CreateProvider(handler, userManager, out _);

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", password!, null));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RefusesTheLogin_AndDoesNotContactEmby_WhenTheAccountIsDisabled()
    {
        var userManager = new FakeUserManager();
        var handler = new StubHttpMessageHandler();
        var provider = CreateProvider(handler, userManager, out _);
        var resolvedUser = new User("alice", EmbyAuthenticationProvider.ProviderId, "reset-provider");
        resolvedUser.AddDefaultPermissions();
        resolvedUser.AddDefaultPreferences();
        resolvedUser.SetPermission(PermissionKind.IsDisabled, true);

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", resolvedUser));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RefusesTheLogin_AndDoesNotContactEmby_WhenTheAccountIsAnAdministrator()
    {
        var userManager = new FakeUserManager();
        var handler = new StubHttpMessageHandler();
        var provider = CreateProvider(handler, userManager, out _);
        var resolvedUser = new User("alice", EmbyAuthenticationProvider.ProviderId, "reset-provider");
        resolvedUser.AddDefaultPermissions();
        resolvedUser.AddDefaultPreferences();
        resolvedUser.SetPermission(PermissionKind.IsAdministrator, true);

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", resolvedUser));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RefusesTheLogin_WithTheSettingsProblem_WhenTheSettingsAreInvalid()
    {
        var userManager = new FakeUserManager();
        var handler = new StubHttpMessageHandler();
        var invalidSettings = new PluginConfiguration
        {
            EmbyServerUrl = string.Empty,
            EmbyApiKey = "key-1",
            MigrationMode = MigrationMode.KeepEmbyInCharge,
            AccountAccess = AccountAccess.CopyEmbyRemoteAccess,
        };
        var provider = CreateProvider(handler, userManager, out _, settingsSource: () => invalidSettings);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", null));

        Assert.Equal("The Emby server URL is not set.", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("absent")]
    [InlineData("unavailable")]
    public async Task DoesNotSendThePasswordToEmby_WhenNoEnabledEmbyUserHasTheTypedName(string userListCase)
    {
        var userManager = new FakeUserManager();
        var handler = new StubHttpMessageHandler().Then(() => userListCase switch
        {
            "disabled" => DisabledAliceUserList(),
            "absent" => EmptyUserList(),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        });
        var provider = CreateProvider(handler, userManager, out _);

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", null));

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("Users", request.Uri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesTheLogin_WhenEmbyRefusesThePassword()
    {
        var userManager = new FakeUserManager();
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var provider = CreateProvider(handler, userManager, out _);

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", null));
    }

    [Fact]
    public async Task RefusesTheLogin_WhenEmbyReturnsADifferentUserName()
    {
        var userManager = new FakeUserManager();
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(BobAuthenticateResponse);
        var provider = CreateProvider(handler, userManager, out _);

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", null));

        Assert.Empty(userManager.Calls);
    }

    [Fact]
    public async Task RefusesTheLogin_WhenTheJellyfinAccountUsesAnotherLoginMethod()
    {
        var userManager = new FakeUserManager();
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse);
        var provider = CreateProvider(handler, userManager, out _);
        var resolvedUser = new User("alice", DefaultLoginMethod.ProviderId, "reset-provider");
        resolvedUser.AddDefaultPermissions();
        resolvedUser.AddDefaultPreferences();

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", resolvedUser));

        Assert.Empty(userManager.Calls);
    }

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

    [Fact]
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

    [Fact]
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
        ILogger<EmbyAuthenticationProvider>? logger = null,
        Func<PluginConfiguration?>? settingsSource = null)
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
            settingsSource ?? (() => Settings),
            logger ?? NullLogger<EmbyAuthenticationProvider>.Instance);
    }
}

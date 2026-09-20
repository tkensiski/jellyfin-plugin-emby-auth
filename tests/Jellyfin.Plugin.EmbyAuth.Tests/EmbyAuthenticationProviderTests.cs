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
        var resolvedUser = new User("alice", LoginMethodMove.DefaultProviderId, "reset-provider");
        resolvedUser.AddDefaultPermissions();
        resolvedUser.AddDefaultPreferences();

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", resolvedUser));

        Assert.Empty(userManager.Calls);
    }

    [Fact]
    public async Task RefusesTheLogin_WhenCreateUserFailsBecauseJellyfinRejectsTheName()
    {
        var userManager = new FakeUserManager { CreateUserThrows = new ArgumentException("bad name") };
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse);
        var provider = CreateProvider(handler, userManager, out _);

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", null));

        Assert.Equal(["CreateUserAsync"], userManager.Calls);
        Assert.DoesNotContain("DeleteUserAsync", userManager.Calls);
    }

    [Fact]
    public async Task RefusesTheLogin_WhenCreateUserFailsWithAnUnexpectedExceptionType()
    {
        var userManager = new FakeUserManager { CreateUserThrows = new DbUpdateException("infrastructure failure") };
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse);
        var provider = CreateProvider(handler, userManager, out _);

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", null));

        Assert.Equal(["CreateUserAsync"], userManager.Calls);
        Assert.DoesNotContain("DeleteUserAsync", userManager.Calls);
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

    [Fact]
    public async Task CreatesTheAccount_WithTheEmbyVerifiedHashAndTheEmbyLoginMethod()
    {
        var userManager = new FakeUserManager();
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse);
        var provider = CreateProvider(handler, userManager, out _);
        var expectedHash = new FakeCryptoProvider().CreatePasswordHash("alice-pass").ToString();

        var result = await provider.Authenticate("alice", "alice-pass", null);

        Assert.Equal("alice", result.Username);
        Assert.Equal("alice", userManager.LastCreatedUser?.Username);
        Assert.Same(userManager.LastCreatedUser, userManager.LastUpdatedUser);
        Assert.Equal(expectedHash, userManager.LastUpdatedUser!.Password);
        Assert.Equal(EmbyAuthenticationProvider.ProviderId, userManager.LastUpdatedUser.AuthenticationProviderId);
        Assert.True(userManager.LastUpdatedUser.HasPermission(PermissionKind.EnableRemoteAccess));
    }

    [Fact]
    public async Task SavesTheHashInTheCallDirectlyAfterCreateUser()
    {
        var userManager = new FakeUserManager();
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse);
        var provider = CreateProvider(handler, userManager, out _);

        await provider.Authenticate("alice", "alice-pass", null);

        Assert.Equal(["CreateUserAsync", "UpdateUserAsync"], userManager.Calls);
    }

    [Fact]
    public async Task CreatesTheAccountOnlyOnce_WhenTheSameEmbyUserLogsInTwice()
    {
        var userManager = new FakeUserManager();
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse).Then(AliceAuthenticateResponse);
        var provider = CreateProvider(handler, userManager, out _);

        await provider.Authenticate("alice", "alice-pass", null);
        var createdUser = userManager.LastCreatedUser!;
        await provider.Authenticate("alice", "alice-pass", createdUser);

        Assert.Equal(1, userManager.Calls.Count(call => call == "CreateUserAsync"));
        Assert.Equal(2, userManager.Calls.Count(call => call == "UpdateUserAsync"));
    }

    [Fact]
    public async Task AsksEmbyEveryTime_AndRewritesTheHash_EvenWhenTheSavedHashAlreadyMatches()
    {
        var userManager = new FakeUserManager();
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse).Then(AliceAuthenticateResponse);
        var provider = CreateProvider(handler, userManager, out var verifiedPasswordsPath);
        var savedHash = new FakeCryptoProvider().CreatePasswordHash("alice-pass").ToString();
        var existingUser = new User("alice", EmbyAuthenticationProvider.ProviderId, "reset-provider");
        existingUser.AddDefaultPermissions();
        existingUser.AddDefaultPreferences();
        existingUser.Password = savedHash;
        new EmbyVerifiedPasswords(verifiedPasswordsPath, NullLogger<EmbyVerifiedPasswords>.Instance).Record(existingUser.Id, savedHash);

        await provider.Authenticate("alice", "alice-pass", existingUser);
        await provider.Authenticate("alice", "alice-pass", existingUser);

        var authenticateRequests = handler.Requests.Count(request => request.Uri!.AbsolutePath.EndsWith("AuthenticateByName", StringComparison.Ordinal));
        Assert.Equal(2, authenticateRequests);
        Assert.Equal(2, userManager.Calls.Count(call => call == "UpdateUserAsync"));
    }

    [Fact]
    public async Task RecordsTheVerifiedFingerprint_OnlyAfterTheHashIsSaved_OnSuccess()
    {
        var userManager = new FakeUserManager();
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse);
        var provider = CreateProvider(handler, userManager, out var verifiedPasswordsPath);

        await provider.Authenticate("alice", "alice-pass", null);

        var savedHash = userManager.LastUpdatedUser!.Password;
        var freshVerifiedPasswords = new EmbyVerifiedPasswords(verifiedPasswordsPath, NullLogger<EmbyVerifiedPasswords>.Instance);
        Assert.True(freshVerifiedPasswords.Matches(userManager.LastUpdatedUser.Id, savedHash));
    }

    [Fact]
    public async Task RecordsTheVerifiedFingerprint_OnlyAfterTheHashIsSaved_WhenTheSaveFails()
    {
        var userManager = new FakeUserManager { UpdateUserThrows = new InvalidOperationException("save failed") };
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse);
        var provider = CreateProvider(handler, userManager, out var verifiedPasswordsPath);

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", null));

        var expectedHash = new FakeCryptoProvider().CreatePasswordHash("alice-pass").ToString();
        var freshVerifiedPasswords = new EmbyVerifiedPasswords(verifiedPasswordsPath, NullLogger<EmbyVerifiedPasswords>.Instance);
        Assert.False(freshVerifiedPasswords.Matches(userManager.LastCreatedUser!.Id, expectedHash));
    }

    [Fact]
    public async Task AcceptsTheTypedName_WhenItDiffersFromTheEmbyNameOnlyInCase()
    {
        var userManager = new FakeUserManager();
        var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse);
        var provider = CreateProvider(handler, userManager, out _);

        var result = await provider.Authenticate("Alice", "alice-pass", null);

        Assert.Equal("alice", result.Username);
        Assert.Equal("alice", userManager.LastCreatedUser?.Username);
    }

    [Fact]
    public async Task RefusesTheTypedName_WhenItHasATrailingSpace()
    {
        var userManager = new FakeUserManager();
        var handler = new StubHttpMessageHandler().Then(AliceUserList);
        var provider = CreateProvider(handler, userManager, out _);

        await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice ", "alice-pass", null));

        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("Users", request.Uri!.AbsolutePath, StringComparison.Ordinal);
    }

    private static PluginConfiguration SettingsWithTargets(string migrationTarget = LoginMethodMove.DefaultProviderId, string passwordSetTarget = "") => new()
    {
        EmbyServerUrl = "http://emby:8096",
        EmbyApiKey = "key-1",
        MigrationMode = MigrationMode.KeepEmbyInCharge,
        AccountAccess = AccountAccess.CopyEmbyRemoteAccess,
        MigrationTarget = migrationTarget,
        PasswordSetTarget = passwordSetTarget,
    };

    private static User NewEmbyMethodUser(string username = "alice")
    {
        var user = new User(username, EmbyAuthenticationProvider.ProviderId, "reset-provider");
        user.AddDefaultPermissions();
        user.AddDefaultPreferences();
        return user;
    }

    [Fact(Skip = "Task 2 GREEN: ChangePassword still hardcodes Default, not the password-set target.")]
    public async Task ChangePassword_SavesTheHash_AndMovesTheUserToThePasswordSetTarget()
    {
        var provider = CreateProvider(new StubHttpMessageHandler(), new FakeUserManager(), out _, settingsSource: () => SettingsWithTargets(passwordSetTarget: "jf-security-id"));
        var user = NewEmbyMethodUser();

        await provider.ChangePassword(user, "new-pass");

        Assert.Equal("jf-security-id", user.AuthenticationProviderId);
        Assert.Equal(new FakeCryptoProvider().CreatePasswordHash("new-pass").ToString(), user.Password);
    }

    [Fact(Skip = "Task 2 GREEN: ChangePassword still hardcodes Default, not the migration target.")]
    public async Task ChangePassword_WithAnEmptyPasswordSetTarget_MovesTheUserToTheMigrationTarget()
    {
        var provider = CreateProvider(new StubHttpMessageHandler(), new FakeUserManager(), out _, settingsSource: () => SettingsWithTargets(migrationTarget: "jf-security-id"));
        var user = NewEmbyMethodUser();

        await provider.ChangePassword(user, "new-pass");

        Assert.Equal("jf-security-id", user.AuthenticationProviderId);
    }

    [Fact(Skip = "Task 2 GREEN: ChangePassword still hardcodes Default, ignoring Remain on Emby Login.")]
    public async Task ChangePassword_WithAPasswordSetTargetOfTheSentinel_SavesTheHash_AndLeavesTheLoginMethodUnchanged()
    {
        var logger = new CapturingLogger<EmbyAuthenticationProvider>();
        var provider = CreateProvider(
            new StubHttpMessageHandler(),
            new FakeUserManager(),
            out _,
            logger,
            settingsSource: () => SettingsWithTargets(passwordSetTarget: PluginConfiguration.RemainOnEmbyLoginMethod));
        var user = NewEmbyMethodUser();

        await provider.ChangePassword(user, "new-pass");

        Assert.Equal(EmbyAuthenticationProvider.ProviderId, user.AuthenticationProviderId);
        Assert.Equal(new FakeCryptoProvider().CreatePasswordHash("new-pass").ToString(), user.Password);
        Assert.DoesNotContain(logger.Entries, entry => entry.StartsWith("Error:", StringComparison.Ordinal));
    }

    [Fact(Skip = "Task 2 GREEN: ChangePassword still hardcodes Default, ignoring Remain on Emby Login.")]
    public async Task ChangePassword_WithAMigrationTargetOfTheSentinel_AndEmptyPasswordSetTarget_LeavesTheLoginMethodUnchanged()
    {
        var provider = CreateProvider(
            new StubHttpMessageHandler(),
            new FakeUserManager(),
            out _,
            settingsSource: () => SettingsWithTargets(migrationTarget: PluginConfiguration.RemainOnEmbyLoginMethod));
        var user = NewEmbyMethodUser();

        await provider.ChangePassword(user, "new-pass");

        Assert.Equal(EmbyAuthenticationProvider.ProviderId, user.AuthenticationProviderId);
    }

    [Fact(Skip = "Task 2 GREEN: ChangePassword still hardcodes Default and never logs an Error for an unusable target.")]
    public async Task ChangePassword_WithAnUnusableTarget_SavesTheHash_LeavesTheLoginMethodUnchanged_AndLogsOneError()
    {
        var logger = new CapturingLogger<EmbyAuthenticationProvider>();
        var provider = CreateProvider(
            new StubHttpMessageHandler(),
            new FakeUserManager(),
            out _,
            logger,
            settingsSource: () => SettingsWithTargets(migrationTarget: string.Empty));
        var user = NewEmbyMethodUser();

        await provider.ChangePassword(user, "new-pass");

        Assert.Equal(EmbyAuthenticationProvider.ProviderId, user.AuthenticationProviderId);
        Assert.Equal(new FakeCryptoProvider().CreatePasswordHash("new-pass").ToString(), user.Password);
        var errorEntries = logger.Entries.Where(entry => entry.StartsWith("Error:", StringComparison.Ordinal)).ToList();
        Assert.Single(errorEntries);
    }

    [Fact(Skip = "Task 2 GREEN: ChangePassword still hardcodes Default and never logs an Error for a null configuration.")]
    public async Task ChangePassword_WithANullConfiguration_DoesTheSameAsAnUnusableTarget()
    {
        var logger = new CapturingLogger<EmbyAuthenticationProvider>();
        var provider = CreateProvider(new StubHttpMessageHandler(), new FakeUserManager(), out _, logger, settingsSource: () => null);
        var user = NewEmbyMethodUser();

        await provider.ChangePassword(user, "new-pass");

        Assert.Equal(EmbyAuthenticationProvider.ProviderId, user.AuthenticationProviderId);
        Assert.Equal(new FakeCryptoProvider().CreatePasswordHash("new-pass").ToString(), user.Password);
        var errorEntries = logger.Entries.Where(entry => entry.StartsWith("Error:", StringComparison.Ordinal)).ToList();
        Assert.Single(errorEntries);
    }

    [Fact]
    public async Task ChangePassword_WithAnEmptyNewPassword_ClearsTheSavedPassword_AndLeavesTheLoginMethodUnchanged()
    {
        var provider = CreateProvider(new StubHttpMessageHandler(), new FakeUserManager(), out _, settingsSource: () => SettingsWithTargets(passwordSetTarget: "jf-security-id"));
        var user = NewEmbyMethodUser();
        user.Password = new FakeCryptoProvider().CreatePasswordHash("old-pass").ToString();

        await provider.ChangePassword(user, string.Empty);

        Assert.Null(user.Password);
        Assert.Equal(EmbyAuthenticationProvider.ProviderId, user.AuthenticationProviderId);
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

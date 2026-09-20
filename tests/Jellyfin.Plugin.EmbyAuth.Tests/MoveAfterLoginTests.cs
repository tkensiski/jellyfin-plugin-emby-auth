using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Events.Authentication;
using MediaBrowser.Model.Dto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public sealed class MoveAfterLoginTests
{
    private const string PasswordHash = "hash-a";

    private static PluginConfiguration Configuration(string migrationTarget) => new()
    {
        EmbyServerUrl = "http://emby:8096",
        EmbyApiKey = "key-1",
        MigrationMode = MigrationMode.MoveAfterFirstLogin,
        MigrationTarget = migrationTarget,
    };

    private static async Task<Guid> SeedUserAsync(SqliteJellyfinDbContextFactory factory, string authenticationProviderId, string? passwordHash)
    {
        var userId = Guid.NewGuid();
        var context = factory.CreateDbContext();
        await using (context)
        {
            context.Users.Add(new User("alice", authenticationProviderId, "reset-provider")
            {
                Id = userId,
                Password = passwordHash,
            });
            await context.SaveChangesAsync(CancellationToken.None);
        }

        return userId;
    }

    private static async Task<User> ReadUserAsync(SqliteJellyfinDbContextFactory factory, Guid userId)
    {
        var context = factory.CreateDbContext();
        await using (context)
        {
            return await context.Users.SingleAsync(user => user.Id == userId, CancellationToken.None);
        }
    }

    private static AuthenticationResultEventArgs EventFor(Guid userId) =>
        new(new AuthenticationResult { User = new UserDto { Id = userId } });

    private static EmbyVerifiedPasswords NewVerifiedPasswords()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        return new(databasePath, databasePath + ".legacy.json", NullLogger<EmbyVerifiedPasswords>.Instance);
    }

    private static MoveAfterLogin CreateConsumer(SqliteJellyfinDbContextFactory factory, EmbyVerifiedPasswords verifiedPasswords, PluginConfiguration? configuration, CapturingLogger<MoveAfterLogin> logger) =>
        new(verifiedPasswords, factory, () => configuration, logger);

    [Fact]
    public async Task OnEvent_MovesTheUserToTheConfiguredTarget_WhenTheSavedHashIsVerified()
    {
        using var factory = new SqliteJellyfinDbContextFactory();
        var userId = await SeedUserAsync(factory, EmbyAuthenticationProvider.ProviderId, PasswordHash);
        var verifiedPasswords = NewVerifiedPasswords();
        verifiedPasswords.Record(userId, PasswordHash);
        const string target = "Jellyfin.Plugin.EmbyAuth.Tests.AnotherLoginMethod";
        var consumer = CreateConsumer(factory, verifiedPasswords, Configuration(target), new CapturingLogger<MoveAfterLogin>());

        await consumer.OnEvent(EventFor(userId));

        var read = await ReadUserAsync(factory, userId);
        Assert.Equal(target, read.AuthenticationProviderId);
    }

    [Fact]
    public async Task OnEvent_MovesNobody_AndLogsNoErrorEntry_WhenTheTargetIsTheSentinel()
    {
        using var factory = new SqliteJellyfinDbContextFactory();
        var userId = await SeedUserAsync(factory, EmbyAuthenticationProvider.ProviderId, PasswordHash);
        var verifiedPasswords = NewVerifiedPasswords();
        verifiedPasswords.Record(userId, PasswordHash);
        var logger = new CapturingLogger<MoveAfterLogin>();
        var consumer = CreateConsumer(factory, verifiedPasswords, Configuration(PluginConfiguration.RemainOnEmbyLoginMethod), logger);

        await consumer.OnEvent(EventFor(userId));

        var read = await ReadUserAsync(factory, userId);
        Assert.Equal(EmbyAuthenticationProvider.ProviderId, read.AuthenticationProviderId);
        Assert.DoesNotContain(logger.Entries, entry => entry.StartsWith("Error:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnEvent_MovesNobody_AndLogsExactlyOneErrorEntry_WhenTheTargetIsBlank()
    {
        using var factory = new SqliteJellyfinDbContextFactory();
        var userId = await SeedUserAsync(factory, EmbyAuthenticationProvider.ProviderId, PasswordHash);
        var verifiedPasswords = NewVerifiedPasswords();
        verifiedPasswords.Record(userId, PasswordHash);
        var logger = new CapturingLogger<MoveAfterLogin>();
        var consumer = CreateConsumer(factory, verifiedPasswords, Configuration("   "), logger);

        await consumer.OnEvent(EventFor(userId));

        var read = await ReadUserAsync(factory, userId);
        Assert.Equal(EmbyAuthenticationProvider.ProviderId, read.AuthenticationProviderId);
        var errorEntries = logger.Entries.Where(entry => entry.StartsWith("Error:", StringComparison.Ordinal)).ToList();
        Assert.Single(errorEntries);
    }

    [Fact]
    public async Task OnEvent_MovesNobody_WhenTheSavedHashIsNotOneEmbyVerified()
    {
        using var factory = new SqliteJellyfinDbContextFactory();
        var userId = await SeedUserAsync(factory, EmbyAuthenticationProvider.ProviderId, PasswordHash);
        var verifiedPasswords = NewVerifiedPasswords();
        var consumer = CreateConsumer(factory, verifiedPasswords, Configuration(LoginMethodMove.DefaultProviderId), new CapturingLogger<MoveAfterLogin>());

        await consumer.OnEvent(EventFor(userId));

        var read = await ReadUserAsync(factory, userId);
        Assert.Equal(EmbyAuthenticationProvider.ProviderId, read.AuthenticationProviderId);
    }

    [Fact]
    public async Task OnEvent_DoesNothing_WhenTheMigrationModeIsNotMoveAfterFirstLogin()
    {
        using var factory = new SqliteJellyfinDbContextFactory();
        var userId = await SeedUserAsync(factory, EmbyAuthenticationProvider.ProviderId, PasswordHash);
        var verifiedPasswords = NewVerifiedPasswords();
        verifiedPasswords.Record(userId, PasswordHash);
        var configuration = Configuration(LoginMethodMove.DefaultProviderId);
        configuration.MigrationMode = MigrationMode.KeepEmbyInCharge;
        var consumer = CreateConsumer(factory, verifiedPasswords, configuration, new CapturingLogger<MoveAfterLogin>());

        await consumer.OnEvent(EventFor(userId));

        var read = await ReadUserAsync(factory, userId);
        Assert.Equal(EmbyAuthenticationProvider.ProviderId, read.AuthenticationProviderId);
    }
}

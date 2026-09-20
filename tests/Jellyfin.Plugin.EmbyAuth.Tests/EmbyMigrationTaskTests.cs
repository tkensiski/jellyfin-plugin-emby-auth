using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public sealed class EmbyMigrationTaskTests : IDisposable
{
    private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"emby-migration-task-tests-{Guid.NewGuid():N}.json");
    private readonly SqliteJellyfinDbContextFactory _dbContextFactory = new();

    public void Dispose()
    {
        File.Delete(_filePath);
        File.Delete(_filePath + ".tmp");
        _dbContextFactory.Dispose();
    }

    private static PluginConfiguration Configuration(string migrationTarget) => new() { MigrationTarget = migrationTarget };

    private EmbyMigrationTask CreateTask(string migrationTarget, CapturingLogger<EmbyMigrationTask> logger) =>
        new(
            _dbContextFactory,
            new EmbyVerifiedPasswords(_filePath, NullLogger<EmbyVerifiedPasswords>.Instance),
            () => Configuration(migrationTarget),
            logger);

    private async Task<Guid> SeedUserAsync(string? passwordHash)
    {
        var userId = Guid.NewGuid();
        var context = _dbContextFactory.CreateDbContext();
        await using (context)
        {
            context.Users.Add(new User("alice", EmbyAuthenticationProvider.ProviderId, "reset-provider")
            {
                Id = userId,
                Password = passwordHash,
            });
            await context.SaveChangesAsync(CancellationToken.None);
        }

        return userId;
    }

    private async Task<User> ReadUserAsync(Guid userId)
    {
        var context = _dbContextFactory.CreateDbContext();
        await using (context)
        {
            return await context.Users.SingleAsync(user => user.Id == userId, CancellationToken.None);
        }
    }

    private sealed class RecordingProgress : IProgress<double>
    {
        public double Last { get; private set; } = -1;

        public void Report(double value) => Last = value;
    }

    [Fact]
    public async Task ExecuteAsync_MovesEveryReadyAccount_ToTheConfiguredTarget()
    {
        const string passwordHash = "hash-a";
        const string target = "Jellyfin.Plugin.EmbyAuth.Tests.AnotherLoginMethod";
        var userId = await SeedUserAsync(passwordHash);
        new EmbyVerifiedPasswords(_filePath, NullLogger<EmbyVerifiedPasswords>.Instance).Record(userId, passwordHash);
        var task = CreateTask(target, new CapturingLogger<EmbyMigrationTask>());
        var progress = new RecordingProgress();

        await task.ExecuteAsync(progress, CancellationToken.None);

        var read = await ReadUserAsync(userId);
        Assert.Equal(target, read.AuthenticationProviderId);
        Assert.Equal(100, progress.Last);
    }

    [Fact]
    public async Task ExecuteAsync_LeavesAnAccountWithNoSavedPassword_OnTheEmbyLoginMethod()
    {
        var userId = await SeedUserAsync(null);
        var task = CreateTask(LoginMethodMove.DefaultProviderId, new CapturingLogger<EmbyMigrationTask>());
        var progress = new RecordingProgress();

        await task.ExecuteAsync(progress, CancellationToken.None);

        var read = await ReadUserAsync(userId);
        Assert.Equal(EmbyAuthenticationProvider.ProviderId, read.AuthenticationProviderId);
        Assert.Equal(100, progress.Last);
    }

    [Fact]
    public async Task ExecuteAsync_MovesNobody_AndLogsNoErrorEntry_WhenTheTargetIsTheSentinel()
    {
        const string passwordHash = "hash-a";
        var userId = await SeedUserAsync(passwordHash);
        new EmbyVerifiedPasswords(_filePath, NullLogger<EmbyVerifiedPasswords>.Instance).Record(userId, passwordHash);
        var logger = new CapturingLogger<EmbyMigrationTask>();
        var task = CreateTask(PluginConfiguration.RemainOnEmbyLoginMethod, logger);
        var progress = new RecordingProgress();

        await task.ExecuteAsync(progress, CancellationToken.None);

        var read = await ReadUserAsync(userId);
        Assert.Equal(EmbyAuthenticationProvider.ProviderId, read.AuthenticationProviderId);
        Assert.Equal(100, progress.Last);
        Assert.DoesNotContain(logger.Entries, entry => entry.StartsWith("Error:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_MovesNobody_AndLogsExactlyOneErrorEntry_WhenTheTargetIsBlank()
    {
        const string passwordHash = "hash-a";
        var userId = await SeedUserAsync(passwordHash);
        new EmbyVerifiedPasswords(_filePath, NullLogger<EmbyVerifiedPasswords>.Instance).Record(userId, passwordHash);
        var logger = new CapturingLogger<EmbyMigrationTask>();
        var task = CreateTask("   ", logger);
        var progress = new RecordingProgress();

        await task.ExecuteAsync(progress, CancellationToken.None);

        var read = await ReadUserAsync(userId);
        Assert.Equal(EmbyAuthenticationProvider.ProviderId, read.AuthenticationProviderId);
        Assert.Equal(100, progress.Last);
        var errorEntries = logger.Entries.Where(entry => entry.StartsWith("Error:", StringComparison.Ordinal)).ToList();
        Assert.Single(errorEntries);
    }

    [Fact]
    public async Task ExecuteAsync_CompletesWithoutThrowing_WhenNoAccountIsOnTheEmbyLoginMethod()
    {
        var task = CreateTask(LoginMethodMove.DefaultProviderId, new CapturingLogger<EmbyMigrationTask>());
        var progress = new RecordingProgress();

        await task.ExecuteAsync(progress, CancellationToken.None);

        Assert.Equal(100, progress.Last);
    }

    [Fact]
    public void Key_IsEmbyAuthMigration_AndNameDoesNotSayDefault()
    {
        var task = CreateTask(LoginMethodMove.DefaultProviderId, new CapturingLogger<EmbyMigrationTask>());

        Assert.Equal("EmbyAuthMigration", task.Key);
        Assert.DoesNotContain("Default", task.Name, StringComparison.Ordinal);
    }
}

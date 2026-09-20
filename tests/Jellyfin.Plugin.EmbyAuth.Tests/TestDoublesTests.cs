using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public sealed class TestDoublesTests
{
    [Fact]
    public async Task SqliteJellyfinDbContextFactory_SharesRows_AcrossContexts()
    {
        using var factory = new SqliteJellyfinDbContextFactory();
        var userId = Guid.NewGuid();

        var writeContext = factory.CreateDbContext();
        await using (writeContext)
        {
            var user = new User("alice", EmbyAuthenticationProvider.ProviderId, "reset-provider")
            {
                Id = userId,
            };
            writeContext.Users.Add(user);
            await writeContext.SaveChangesAsync(CancellationToken.None);
        }

        var readContext = factory.CreateDbContext();
        await using (readContext)
        {
            var read = await readContext.Users.SingleAsync(user => user.Id == userId, CancellationToken.None);
            Assert.Equal("alice", read.Username);
        }
    }

    [Fact]
    public async Task SqliteJellyfinDbContextFactory_ExecutesExecuteUpdateAsync()
    {
        using var factory = new SqliteJellyfinDbContextFactory();
        var userId = Guid.NewGuid();
        const string passwordHash = "hash-a";

        var seedContext = factory.CreateDbContext();
        await using (seedContext)
        {
            var user = new User("alice", EmbyAuthenticationProvider.ProviderId, "reset-provider")
            {
                Id = userId,
                Password = passwordHash,
            };
            seedContext.Users.Add(user);
            await seedContext.SaveChangesAsync(CancellationToken.None);
        }

        var moveContext = factory.CreateDbContext();
        await using (moveContext)
        {
            var moved = await LoginMethodMove.MoveAsync(moveContext, userId, passwordHash, LoginMethodMove.DefaultProviderId, CancellationToken.None);
            Assert.True(moved);
        }

        var readContext = factory.CreateDbContext();
        await using (readContext)
        {
            var read = await readContext.Users.SingleAsync(user => user.Id == userId, CancellationToken.None);
            Assert.Equal(LoginMethodMove.DefaultProviderId, read.AuthenticationProviderId);
        }
    }

    [Fact]
    public void FakeTaskManager_RecordsQueuedTypeAndReturnsWorkers()
    {
        var taskManager = new FakeTaskManager();
        var task = new FakeScheduledTask();
        var worker = new FakeScheduledTaskWorker(task);
        taskManager.Tasks.Add(worker);

        taskManager.QueueIfNotRunning<FakeScheduledTask>();

        Assert.Equal([typeof(FakeScheduledTask)], taskManager.QueuedTypes);
        Assert.Same(worker, Assert.Single(taskManager.ScheduledTasks));
    }

    [Fact]
    public void FakeUserManager_GetAuthenticationProviders_ReturnsWhatWasSet()
    {
        var userManager = new FakeUserManager
        {
            AuthenticationProviders = [new NameIdPair { Name = "Default", Id = "default-id" }, new NameIdPair { Name = "Emby", Id = "emby-id" }],
        };

        Assert.Equal(userManager.AuthenticationProviders, userManager.GetAuthenticationProviders());
    }

    private sealed class FakeScheduledTask : IScheduledTask
    {
        public string Name => "Fake task";

        public string Key => "FakeTask";

        public string Description => "A fake scheduled task for FakeTaskManager tests.";

        public string Category => "Tests";

        public System.Collections.Generic.IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

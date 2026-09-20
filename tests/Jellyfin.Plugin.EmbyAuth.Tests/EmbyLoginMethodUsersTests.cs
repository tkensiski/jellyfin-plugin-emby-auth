using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public sealed class EmbyLoginMethodUsersTests : IDisposable
{
    private const string AnotherProviderId = "Jellyfin.Plugin.EmbyAuth.Tests.AnotherLoginMethod";

    private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"emby-auth-login-method-users-tests-{Guid.NewGuid():N}.json");
    private readonly SqliteJellyfinDbContextFactory _dbContextFactory = new();

    public void Dispose()
    {
        File.Delete(_filePath);
        File.Delete(_filePath + ".tmp");
        _dbContextFactory.Dispose();
    }

    private EmbyVerifiedPasswords CreateVerifiedPasswords() =>
        new(_filePath, NullLogger<EmbyVerifiedPasswords>.Instance);

    private async Task SeedUserAsync(string username, string authenticationProviderId, string? passwordHash)
    {
        var context = _dbContextFactory.CreateDbContext();
        await using (context)
        {
            context.Users.Add(new User(username, authenticationProviderId, "reset-provider")
            {
                Id = Guid.NewGuid(),
                Password = passwordHash,
            });
            await context.SaveChangesAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyUsersOnTheEmbyLoginMethod()
    {
        await SeedUserAsync("alice", EmbyAuthenticationProvider.ProviderId, "hash-alice");
        await SeedUserAsync("bob", EmbyAuthenticationProvider.ProviderId, "hash-bob");
        await SeedUserAsync("carol", AnotherProviderId, "hash-carol");
        var context = _dbContextFactory.CreateDbContext();
        var verifiedPasswords = CreateVerifiedPasswords();

        IReadOnlyList<EmbyLoginMethodUser> users;
        await using (context)
        {
            users = await EmbyLoginMethodUsers.ListAsync(context, verifiedPasswords, CancellationToken.None);
        }

        Assert.Equal(["alice", "bob"], users.Select(user => user.Username).ToList());
    }

    [Fact]
    public async Task ListAsync_ReturnsEmptyList_WhenNoUserIsOnTheEmbyLoginMethod()
    {
        await SeedUserAsync("carol", AnotherProviderId, "hash-carol");
        var context = _dbContextFactory.CreateDbContext();
        var verifiedPasswords = CreateVerifiedPasswords();

        IReadOnlyList<EmbyLoginMethodUser> users;
        await using (context)
        {
            users = await EmbyLoginMethodUsers.ListAsync(context, verifiedPasswords, CancellationToken.None);
        }

        Assert.Empty(users);
    }

    [Fact]
    public async Task ListAsync_OrdersUsersByName()
    {
        await SeedUserAsync("zeta", EmbyAuthenticationProvider.ProviderId, "hash-zeta");
        await SeedUserAsync("alpha", EmbyAuthenticationProvider.ProviderId, "hash-alpha");
        await SeedUserAsync("mike", EmbyAuthenticationProvider.ProviderId, "hash-mike");
        var context = _dbContextFactory.CreateDbContext();
        var verifiedPasswords = CreateVerifiedPasswords();

        IReadOnlyList<EmbyLoginMethodUser> users;
        await using (context)
        {
            users = await EmbyLoginMethodUsers.ListAsync(context, verifiedPasswords, CancellationToken.None);
        }

        Assert.Equal(["alpha", "mike", "zeta"], users.Select(user => user.Username).ToList());
    }

    [Fact]
    public async Task ListAsync_MarksUserReady_WhenTheSavedHashIsVerified()
    {
        const string hash = "hash-alice";
        await SeedUserAsync("alice", EmbyAuthenticationProvider.ProviderId, hash);
        var context = _dbContextFactory.CreateDbContext();
        var verifiedPasswords = CreateVerifiedPasswords();

        User seeded;
        await using (var seedRead = _dbContextFactory.CreateDbContext())
        {
            seeded = await seedRead.Users.SingleAsync(user => user.Username == "alice", CancellationToken.None);
        }

        verifiedPasswords.Record(seeded.Id, hash);

        IReadOnlyList<EmbyLoginMethodUser> users;
        await using (context)
        {
            users = await EmbyLoginMethodUsers.ListAsync(context, verifiedPasswords, CancellationToken.None);
        }

        Assert.True(Assert.Single(users).ReadyToMove);
    }

    [Fact]
    public async Task ListAsync_MarksUserNotReady_WhenTheSavedHashIsUnverified()
    {
        await SeedUserAsync("alice", EmbyAuthenticationProvider.ProviderId, "hash-alice");
        var context = _dbContextFactory.CreateDbContext();
        var verifiedPasswords = CreateVerifiedPasswords();

        IReadOnlyList<EmbyLoginMethodUser> users;
        await using (context)
        {
            users = await EmbyLoginMethodUsers.ListAsync(context, verifiedPasswords, CancellationToken.None);
        }

        Assert.False(Assert.Single(users).ReadyToMove);
    }

    [Fact]
    public async Task ListAsync_MarksUserNotReady_WhenThePasswordIsNull()
    {
        await SeedUserAsync("alice", EmbyAuthenticationProvider.ProviderId, passwordHash: null);
        var context = _dbContextFactory.CreateDbContext();
        var verifiedPasswords = CreateVerifiedPasswords();

        IReadOnlyList<EmbyLoginMethodUser> users;
        await using (context)
        {
            users = await EmbyLoginMethodUsers.ListAsync(context, verifiedPasswords, CancellationToken.None);
        }

        var user = Assert.Single(users);
        Assert.Null(user.PasswordHash);
        Assert.False(user.ReadyToMove);
    }
}

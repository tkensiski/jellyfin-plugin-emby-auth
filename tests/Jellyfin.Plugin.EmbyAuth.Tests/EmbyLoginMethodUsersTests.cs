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

    /// <summary>
    /// A record cut short: an opening brace, a quoted user identifier, a colon, and a quoted value with no closing
    /// quote or brace. Deserializing this throws <see cref="System.Text.Json.JsonException"/>, matching the fixture
    /// <c>EmbyVerifiedPasswordsTests</c> already uses to provoke a read failure.
    /// </summary>
    private const string UnreadableContents = "{\"3fa85f64-5717-4562-b3fc-2c963f66afa6\":\"AB";

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

        Assert.Equal(MigrationUserState.Ready, Assert.Single(users).State);
    }

    [Fact]
    public async Task ListAsync_MarksUserNeedsEmbyLogin_WhenTheSavedHashIsUnverified()
    {
        await SeedUserAsync("alice", EmbyAuthenticationProvider.ProviderId, "hash-alice");
        var context = _dbContextFactory.CreateDbContext();
        var verifiedPasswords = CreateVerifiedPasswords();

        IReadOnlyList<EmbyLoginMethodUser> users;
        await using (context)
        {
            users = await EmbyLoginMethodUsers.ListAsync(context, verifiedPasswords, CancellationToken.None);
        }

        Assert.Equal(MigrationUserState.NeedsEmbyLogin, Assert.Single(users).State);
    }

    [Fact]
    public async Task ListAsync_MarksUserNoPassword_WhenThePasswordIsNull()
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
        Assert.Equal(MigrationUserState.NoPassword, user.State);
    }

    [Fact]
    public async Task ListAsync_MarksUserNoPassword_EvenWhenTheFingerprintFileCannotBeRead()
    {
        File.WriteAllText(_filePath, UnreadableContents);
        await SeedUserAsync("alice", EmbyAuthenticationProvider.ProviderId, passwordHash: null);
        var context = _dbContextFactory.CreateDbContext();
        var verifiedPasswords = CreateVerifiedPasswords();

        IReadOnlyList<EmbyLoginMethodUser> users;
        await using (context)
        {
            users = await EmbyLoginMethodUsers.ListAsync(context, verifiedPasswords, CancellationToken.None);
        }

        Assert.Equal(MigrationUserState.NoPassword, Assert.Single(users).State);
    }

    [Fact]
    public async Task ListAsync_MarksUserUnknown_WhenTheFingerprintFileCannotBeRead()
    {
        File.WriteAllText(_filePath, UnreadableContents);
        await SeedUserAsync("alice", EmbyAuthenticationProvider.ProviderId, "hash-alice");
        var context = _dbContextFactory.CreateDbContext();
        var verifiedPasswords = CreateVerifiedPasswords();

        IReadOnlyList<EmbyLoginMethodUser> users;
        await using (context)
        {
            users = await EmbyLoginMethodUsers.ListAsync(context, verifiedPasswords, CancellationToken.None);
        }

        Assert.Equal(MigrationUserState.Unknown, Assert.Single(users).State);
    }

    [Fact]
    public async Task ListAsync_NeverMarksNeedsEmbyLogin_AndChecksAvailabilityOnlyOnce_WhenTheFingerprintFileCannotBeRead()
    {
        File.WriteAllText(_filePath, UnreadableContents);
        await SeedUserAsync("alice", EmbyAuthenticationProvider.ProviderId, "hash-alice");
        await SeedUserAsync("bob", EmbyAuthenticationProvider.ProviderId, "hash-bob");
        await SeedUserAsync("carol", EmbyAuthenticationProvider.ProviderId, "hash-carol");
        var logger = new CapturingLogger<EmbyVerifiedPasswords>();
        var verifiedPasswords = new EmbyVerifiedPasswords(_filePath, logger);
        var context = _dbContextFactory.CreateDbContext();

        IReadOnlyList<EmbyLoginMethodUser> users;
        await using (context)
        {
            users = await EmbyLoginMethodUsers.ListAsync(context, verifiedPasswords, CancellationToken.None);
        }

        Assert.Equal(3, users.Count);
        Assert.All(users, user => Assert.Equal(MigrationUserState.Unknown, user.State));

        // EmbyVerifiedPasswords.Load() logs an error every time it retries a failed read, and never caches a
        // failure. So exactly one log entry proves RecordsAvailable() was called once for this whole ListAsync
        // call, not once per one of the three users above. A counting subclass is not available here because
        // EmbyVerifiedPasswords is sealed.
        Assert.Single(logger.Entries);
    }
}

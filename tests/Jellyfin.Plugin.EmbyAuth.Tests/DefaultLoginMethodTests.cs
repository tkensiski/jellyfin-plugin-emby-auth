using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public sealed class DefaultLoginMethodTests
{
    private const string PasswordHash = "hash-a";
    private const string AnotherProviderId = "Jellyfin.Plugin.EmbyAuth.Tests.AnotherLoginMethod";

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

    [Fact]
    public async Task MoveAsync_MovesUserAndReturnsTrue_WhenOnEmbyMethodWithMatchingHash()
    {
        using var factory = new SqliteJellyfinDbContextFactory();
        var userId = await SeedUserAsync(factory, EmbyAuthenticationProvider.ProviderId, PasswordHash);

        var moveContext = factory.CreateDbContext();
        bool moved;
        await using (moveContext)
        {
            moved = await DefaultLoginMethod.MoveAsync(moveContext, userId, PasswordHash, CancellationToken.None);
        }

        Assert.True(moved);
        var read = await ReadUserAsync(factory, userId);
        Assert.Equal(DefaultLoginMethod.ProviderId, read.AuthenticationProviderId);
    }

    [Fact]
    public async Task MoveAsync_ReturnsFalseAndDoesNotChangeTheRow_WhenUserIsOnAnotherLoginMethod()
    {
        using var factory = new SqliteJellyfinDbContextFactory();
        var userId = await SeedUserAsync(factory, AnotherProviderId, PasswordHash);

        var moveContext = factory.CreateDbContext();
        bool moved;
        await using (moveContext)
        {
            moved = await DefaultLoginMethod.MoveAsync(moveContext, userId, PasswordHash, CancellationToken.None);
        }

        Assert.False(moved);
        var read = await ReadUserAsync(factory, userId);
        Assert.Equal(AnotherProviderId, read.AuthenticationProviderId);
    }

    [Fact]
    public async Task MoveAsync_ReturnsFalseAndDoesNotChangeTheRow_WhenTheSavedHashDiffers()
    {
        using var factory = new SqliteJellyfinDbContextFactory();
        var userId = await SeedUserAsync(factory, EmbyAuthenticationProvider.ProviderId, PasswordHash);

        var moveContext = factory.CreateDbContext();
        bool moved;
        await using (moveContext)
        {
            moved = await DefaultLoginMethod.MoveAsync(moveContext, userId, "a-different-hash", CancellationToken.None);
        }

        Assert.False(moved);
        var read = await ReadUserAsync(factory, userId);
        Assert.Equal(EmbyAuthenticationProvider.ProviderId, read.AuthenticationProviderId);
    }

    [Fact]
    public async Task MoveAsync_ReturnsFalse_WhenNoUserHasThatId()
    {
        using var factory = new SqliteJellyfinDbContextFactory();
        var moveContext = factory.CreateDbContext();
        bool moved;
        await using (moveContext)
        {
            moved = await DefaultLoginMethod.MoveAsync(moveContext, Guid.NewGuid(), PasswordHash, CancellationToken.None);
        }

        Assert.False(moved);
    }

    /// <summary>
    /// Invariant test for the assumption-delta decision recorded in 03-01-PLAN.md's
    /// <c>assumption_delta_decision</c>: today <see cref="DefaultLoginMethod.MoveAsync"/> always writes
    /// <see cref="DefaultLoginMethod.ProviderId"/>, so the non-Default row is expected to be red until plan 04
    /// task 2 generalizes the move target into a parameter.
    /// </summary>
    /// <param name="targetProviderId">The login method ID the moved user is expected to end up on.</param>
    [Theory]
    [InlineData(DefaultLoginMethod.ProviderId)]
    [InlineData(AnotherProviderId, Skip = "RED until plan 04 task 2 generalizes DefaultLoginMethod.MoveAsync's target; today it always writes DefaultLoginMethod.ProviderId")]
    public async Task MoveAsync_WritesTheGivenTarget(string targetProviderId)
    {
        using var factory = new SqliteJellyfinDbContextFactory();
        var userId = await SeedUserAsync(factory, EmbyAuthenticationProvider.ProviderId, PasswordHash);

        var moveContext = factory.CreateDbContext();
        await using (moveContext)
        {
            await DefaultLoginMethod.MoveAsync(moveContext, userId, PasswordHash, CancellationToken.None);
        }

        var read = await ReadUserAsync(factory, userId);
        Assert.Equal(targetProviderId, read.AuthenticationProviderId);
    }
}

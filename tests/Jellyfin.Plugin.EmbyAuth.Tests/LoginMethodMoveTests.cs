using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.EmbyAuth.Configuration;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public sealed class LoginMethodMoveTests
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
            moved = await LoginMethodMove.MoveAsync(moveContext, userId, PasswordHash, LoginMethodMove.DefaultProviderId, CancellationToken.None);
        }

        Assert.True(moved);
        var read = await ReadUserAsync(factory, userId);
        Assert.Equal(LoginMethodMove.DefaultProviderId, read.AuthenticationProviderId);
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
            moved = await LoginMethodMove.MoveAsync(moveContext, userId, PasswordHash, LoginMethodMove.DefaultProviderId, CancellationToken.None);
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
            moved = await LoginMethodMove.MoveAsync(moveContext, userId, "a-different-hash", LoginMethodMove.DefaultProviderId, CancellationToken.None);
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
            moved = await LoginMethodMove.MoveAsync(moveContext, Guid.NewGuid(), PasswordHash, LoginMethodMove.DefaultProviderId, CancellationToken.None);
        }

        Assert.False(moved);
    }

    /// <summary>
    /// Invariant test for the assumption-delta decision recorded in 03-01-PLAN.md's
    /// <c>assumption_delta_decision</c>: this proves <see cref="LoginMethodMove.MoveAsync"/> writes whichever
    /// target it is given, not only Jellyfin's Default. The 03-01 skip on the non-Default row is lifted here.
    /// </summary>
    /// <param name="targetProviderId">The login method ID the moved user is expected to end up on.</param>
    [Theory]
    [InlineData(LoginMethodMove.DefaultProviderId)]
    [InlineData(AnotherProviderId)]
    public async Task MoveAsync_WritesTheGivenTarget(string targetProviderId)
    {
        using var factory = new SqliteJellyfinDbContextFactory();
        var userId = await SeedUserAsync(factory, EmbyAuthenticationProvider.ProviderId, PasswordHash);

        var moveContext = factory.CreateDbContext();
        await using (moveContext)
        {
            await LoginMethodMove.MoveAsync(moveContext, userId, PasswordHash, targetProviderId, CancellationToken.None);
        }

        var read = await ReadUserAsync(factory, userId);
        Assert.Equal(targetProviderId, read.AuthenticationProviderId);
    }

    [Fact]
    public void ResolveMigrationTarget_ReturnsMove_ForAnOrdinaryConfiguredTarget()
    {
        var configuration = new PluginConfiguration { MigrationTarget = AnotherProviderId };

        var target = LoginMethodMove.ResolveMigrationTarget(configuration);

        Assert.Equal(MoveTargetKind.Move, target.Kind);
        Assert.Equal(AnotherProviderId, target.ProviderId);
    }

    [Fact]
    public void ResolveMigrationTarget_ReturnsRemain_ForTheSentinel()
    {
        var configuration = new PluginConfiguration { MigrationTarget = PluginConfiguration.RemainOnEmbyLoginMethod };

        var target = LoginMethodMove.ResolveMigrationTarget(configuration);

        Assert.Equal(MoveTargetKind.Remain, target.Kind);
        Assert.Null(target.ProviderId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveMigrationTarget_ReturnsInvalid_ForABlankTarget(string? blankTarget)
    {
        var configuration = new PluginConfiguration { MigrationTarget = blankTarget! };

        var target = LoginMethodMove.ResolveMigrationTarget(configuration);

        Assert.Equal(MoveTargetKind.Invalid, target.Kind);
        Assert.Null(target.ProviderId);
    }

    [Fact]
    public void ResolveMigrationTarget_ReturnsInvalid_ForANullConfiguration()
    {
        var target = LoginMethodMove.ResolveMigrationTarget(null);

        Assert.Equal(MoveTargetKind.Invalid, target.Kind);
        Assert.Null(target.ProviderId);
    }

    [Fact]
    public void ResolvePasswordSetTarget_ReturnsWhateverResolveMigrationTargetReturns_WhenItsOwnValueIsEmpty()
    {
        var configuration = new PluginConfiguration { MigrationTarget = AnotherProviderId, PasswordSetTarget = string.Empty };

        var target = LoginMethodMove.ResolvePasswordSetTarget(configuration);

        Assert.Equal(LoginMethodMove.ResolveMigrationTarget(configuration), target);
    }

    [Fact]
    public void ResolvePasswordSetTarget_ReturnsMove_FromItsOwnNonEmptyValue()
    {
        var configuration = new PluginConfiguration { MigrationTarget = LoginMethodMove.DefaultProviderId, PasswordSetTarget = AnotherProviderId };

        var target = LoginMethodMove.ResolvePasswordSetTarget(configuration);

        Assert.Equal(MoveTargetKind.Move, target.Kind);
        Assert.Equal(AnotherProviderId, target.ProviderId);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public sealed class EmbyVerifiedPasswordsTests : IDisposable
{
    private const string HashA = "$PBKDF2-SHA512$iterations=210000$AAAA$BBBB";
    private const string HashB = "$PBKDF2-SHA512$iterations=210000$CCCC$DDDD";

    /// <summary>
    /// Bytes that are not a SQLite database. Opening a database at this path fails with a
    /// <see cref="Microsoft.Data.Sqlite.SqliteException"/> reporting "file is not a database".
    /// </summary>
    private const string NotADatabase = "not a database";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"emby-auth-tests-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-wal");
        File.Delete(_databasePath + "-shm");
    }

    private EmbyVerifiedPasswords CreateStore(ILogger<EmbyVerifiedPasswords>? logger = null) =>
        new(_databasePath, logger ?? NullLogger<EmbyVerifiedPasswords>.Instance);

    [Fact]
    public void Matches_TheRecordedHash()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();

        store.Record(userId, HashA);

        Assert.True(store.Matches(userId, HashA));
    }

    [Theory]
    [InlineData(HashB)]
    [InlineData("")]
    [InlineData(null)]
    public void DoesNotMatch_AnotherHash(string? hash)
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();

        store.Record(userId, HashA);

        Assert.False(store.Matches(userId, hash));
    }

    [Fact]
    public void DoesNotMatch_AnUnknownUser()
    {
        var store = CreateStore();
        store.Record(Guid.NewGuid(), HashA);

        Assert.False(store.Matches(Guid.NewGuid(), HashA));
    }

    [Fact]
    public void Record_ReplacesTheEarlierHash()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();

        store.Record(userId, HashA);
        store.Record(userId, HashB);

        Assert.False(store.Matches(userId, HashA));
        Assert.True(store.Matches(userId, HashB));
    }

    [Fact]
    public void Records_SurviveARestart()
    {
        var userId = Guid.NewGuid();
        CreateStore().Record(userId, HashA);

        Assert.True(CreateStore().Matches(userId, HashA));
    }

    [Fact]
    public void Database_DoesNotContainThePasswordHash()
    {
        CreateStore().Record(Guid.NewGuid(), HashA);

        Assert.DoesNotContain("BBBB", Encoding.Latin1.GetString(File.ReadAllBytes(_databasePath)), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingFile_MatchesNothing_WithoutAnError()
    {
        var logger = new CapturingLogger<EmbyVerifiedPasswords>();

        Assert.False(CreateStore(logger).Matches(Guid.NewGuid(), HashA));
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void UnreadableFile_MatchesNothing_AndLogsAnError()
    {
        File.WriteAllText(_databasePath, NotADatabase);
        var logger = new CapturingLogger<EmbyVerifiedPasswords>();

        Assert.False(CreateStore(logger).Matches(Guid.NewGuid(), HashA));
        Assert.Contains(logger.Entries, entry => entry.StartsWith("Error:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConcurrentRecords_AreAllKept()
    {
        var store = CreateStore();
        var userIds = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();

        await Task.WhenAll(userIds.Select(id => Task.Run(() => store.Record(id, HashA))));

        var reloaded = CreateStore();
        Assert.All(userIds, id => Assert.True(reloaded.Matches(id, HashA)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Record_Throws_ForAnEmptyOrNullHash(string? hash)
    {
        var store = CreateStore();

        Assert.ThrowsAny<ArgumentException>(() => store.Record(Guid.NewGuid(), hash!));
    }

    [Fact]
    public void Fingerprint_IsCaseSensitive()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var differentCaseHash = HashA.ToLowerInvariant();

        store.Record(userId, HashA);

        Assert.False(store.Matches(userId, differentCaseHash));
    }
}

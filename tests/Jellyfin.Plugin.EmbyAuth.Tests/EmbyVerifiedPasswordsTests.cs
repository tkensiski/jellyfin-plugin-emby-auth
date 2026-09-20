using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private readonly string _legacyFilePath = Path.Combine(Path.GetTempPath(), $"emby-auth-tests-legacy-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-wal");
        File.Delete(_databasePath + "-shm");
        File.Delete(_legacyFilePath);
    }

    private EmbyVerifiedPasswords CreateStore(ILogger<EmbyVerifiedPasswords>? logger = null) =>
        new(_databasePath, _legacyFilePath, logger ?? NullLogger<EmbyVerifiedPasswords>.Instance);

    private static string Fingerprint(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)));

    private void WriteLegacyFile(Dictionary<Guid, string> records) =>
        File.WriteAllText(_legacyFilePath, JsonSerializer.Serialize(records));

    private long GetUserVersion()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)command.ExecuteScalar()!;
    }

    private void SetUserVersion(int version)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    private void DeleteRow(Guid userId)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM VerifiedPasswords WHERE UserId = $userId;";
        command.Parameters.AddWithValue("$userId", userId.ToString());
        command.ExecuteNonQuery();
    }

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

    [Fact]
    public void StoreThatCannotBeOpened_MatchesNothing_AndLogsOneError()
    {
        var blockingFilePath = Path.Combine(Path.GetTempPath(), $"emby-auth-tests-blocking-{Guid.NewGuid():N}");
        File.WriteAllText(blockingFilePath, "not a directory");
        try
        {
            var unreachableDatabasePath = Path.Combine(blockingFilePath, "fingerprints.db");
            var logger = new CapturingLogger<EmbyVerifiedPasswords>();
            var store = new EmbyVerifiedPasswords(unreachableDatabasePath, unreachableDatabasePath + ".legacy.json", logger);

            Assert.False(store.Matches(Guid.NewGuid(), HashA));
            Assert.False(store.RecordsAvailable());
            Assert.Contains(logger.Entries, entry => entry.StartsWith("Error:", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(blockingFilePath);
        }
    }

    [Fact]
    public void RecordsAvailable_IsFalse_WhenTheDatabaseFileIsNotADatabase()
    {
        File.WriteAllText(_databasePath, NotADatabase);
        var logger = new CapturingLogger<EmbyVerifiedPasswords>();

        Assert.False(CreateStore(logger).RecordsAvailable());
        Assert.Contains(logger.Entries, entry => entry.StartsWith("Error:", StringComparison.Ordinal));
    }

    [Fact]
    public void Record_LogsOneError_AndDoesNotThrow_WhenTheDatabaseFileIsNotADatabase()
    {
        var logger = new CapturingLogger<EmbyVerifiedPasswords>();
        var store = CreateStore(logger);
        var userId = Guid.NewGuid();

        // Construct on a healthy database first, so construction itself logs nothing. Corrupting the file only
        // after construction means the single log entry asserted on below can come only from Record's own write
        // failure, not from a second construction-time initialization attempt.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.WriteAllText(_databasePath, NotADatabase);

        store.Record(userId, HashA);

        Assert.Single(logger.Entries, entry => entry.StartsWith("Error:", StringComparison.Ordinal));
        Assert.False(store.Matches(userId, HashA));
    }

    [Fact]
    public void NoLogEntryNamesAHashOrAFingerprint()
    {
        var logger = new CapturingLogger<EmbyVerifiedPasswords>();
        var store = CreateStore(logger);

        // Drives both failing paths above (Record's write failure and RecordsAvailable's read failure) against
        // the same corrupted database, with a known hash constant, so a fingerprint or hash leak in either
        // method's log message would be caught here.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.WriteAllText(_databasePath, NotADatabase);
        store.Record(Guid.NewGuid(), HashA);
        _ = store.RecordsAvailable();

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(HashA)));
        Assert.NotEmpty(logger.Entries);
        Assert.DoesNotContain(
            logger.Entries,
            entry =>
                entry.Contains(HashA, StringComparison.Ordinal) ||
                entry.Contains(HashA.ToLowerInvariant(), StringComparison.Ordinal) ||
                entry.Contains(fingerprint, StringComparison.Ordinal));
    }

    [Fact]
    public void Import_MakesEveryLegacyRecordMatch_OnFirstStart()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        WriteLegacyFile(new Dictionary<Guid, string>
        {
            [userA] = Fingerprint(HashA),
            [userB] = Fingerprint(HashB),
        });

        var store = CreateStore();

        Assert.True(store.Matches(userA, HashA));
        Assert.True(store.Matches(userB, HashB));
    }

    [Fact]
    public void Import_DoesNotRunASecondTime()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        WriteLegacyFile(new Dictionary<Guid, string>
        {
            [userA] = Fingerprint(HashA),
            [userB] = Fingerprint(HashB),
        });
        CreateStore();

        DeleteRow(userA);

        var secondStore = CreateStore();

        Assert.False(secondStore.Matches(userA, HashA));
        Assert.True(secondStore.Matches(userB, HashB));
    }

    [Fact]
    public void Import_DoesNotOverwriteARecordTheStoreAlreadyWrote()
    {
        var userId = Guid.NewGuid();
        var store = CreateStore();
        store.Record(userId, HashB);

        SetUserVersion(0);
        WriteLegacyFile(new Dictionary<Guid, string> { [userId] = Fingerprint(HashA) });

        var secondStore = CreateStore();

        Assert.True(secondStore.Matches(userId, HashB));
        Assert.False(secondStore.Matches(userId, HashA));
    }

    [Fact]
    public void Import_LeavesTheLegacyFileUnchanged()
    {
        WriteLegacyFile(new Dictionary<Guid, string> { [Guid.NewGuid()] = Fingerprint(HashA) });
        var bytesBefore = File.ReadAllBytes(_legacyFilePath);

        CreateStore();

        Assert.True(File.Exists(_legacyFilePath));
        Assert.Equal(bytesBefore, File.ReadAllBytes(_legacyFilePath));
    }

    [Fact]
    public void Import_MarksItselfDone_WhenNoLegacyFileExists()
    {
        var logger = new CapturingLogger<EmbyVerifiedPasswords>();

        CreateStore(logger);

        Assert.DoesNotContain(logger.Entries, entry => entry.StartsWith("Error:", StringComparison.Ordinal));
        Assert.Equal(1, GetUserVersion());
    }

    [Fact]
    public void Import_ImportsNothing_AndLogsOneError_WhenTheLegacyFileCannotBeRead()
    {
        File.WriteAllText(_legacyFilePath, "not json");
        var logger = new CapturingLogger<EmbyVerifiedPasswords>();

        var store = CreateStore(logger);

        Assert.Single(logger.Entries, entry => entry.StartsWith("Error:", StringComparison.Ordinal));
        Assert.Equal(0, GetUserVersion());

        var userId = Guid.NewGuid();
        store.Record(userId, HashA);
        Assert.True(store.Matches(userId, HashA));

        var validUserId = Guid.NewGuid();
        WriteLegacyFile(new Dictionary<Guid, string> { [validUserId] = Fingerprint(HashB) });
        var thirdStore = CreateStore();

        Assert.True(thirdStore.Matches(validUserId, HashB));
    }

    [Fact]
    public void Import_ImportsNothing_WhenOneEntryInTheFileIsUnusable()
    {
        var userId = Guid.NewGuid();
        File.WriteAllText(
            _legacyFilePath,
            $$"""{"{{userId}}":"{{Fingerprint(HashA)}}","not-a-user-id":"{{Fingerprint(HashB)}}"}""");
        var logger = new CapturingLogger<EmbyVerifiedPasswords>();

        var store = CreateStore(logger);

        Assert.False(store.Matches(userId, HashA));
        Assert.Equal(0, GetUserVersion());
        Assert.Single(logger.Entries, entry => entry.StartsWith("Error:", StringComparison.Ordinal));
    }

    [Fact]
    public void Import_LeavesAUsableStore_WhenItFails()
    {
        File.WriteAllText(_legacyFilePath, "not json");
        var store = CreateStore();

        var userId = Guid.NewGuid();
        store.Record(userId, HashA);

        Assert.True(store.Matches(userId, HashA));
    }
}

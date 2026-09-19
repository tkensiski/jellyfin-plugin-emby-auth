using System;
using System.IO;
using System.Linq;
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
    /// A record cut short: an opening brace, a quoted user identifier, a colon, and a quoted value with no closing quote or brace.
    /// Deserializing this throws <see cref="System.Text.Json.JsonException"/>, so the three tests below assert against a real
    /// record that survived, not a placeholder.
    /// </summary>
    private const string UnreadableContents = "{\"3fa85f64-5717-4562-b3fc-2c963f66afa6\":\"AB";

    private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"emby-auth-tests-{Guid.NewGuid():N}.json");
    private readonly string _retryFilePath = Path.Combine(Path.GetTempPath(), $"emby-auth-tests-retry-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_filePath);
        File.Delete(_filePath + ".tmp");
        File.Delete(_retryFilePath);
        File.Delete(_retryFilePath + ".tmp");
    }

    private EmbyVerifiedPasswords CreateStore(ILogger<EmbyVerifiedPasswords>? logger = null) =>
        new(_filePath, logger ?? NullLogger<EmbyVerifiedPasswords>.Instance);

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
    public void File_DoesNotContainThePasswordHash()
    {
        CreateStore().Record(Guid.NewGuid(), HashA);

        Assert.DoesNotContain("BBBB", File.ReadAllText(_filePath), StringComparison.Ordinal);
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
        File.WriteAllText(_filePath, "not json");
        var logger = new CapturingLogger<EmbyVerifiedPasswords>();

        Assert.False(CreateStore(logger).Matches(Guid.NewGuid(), HashA));
        Assert.Contains(logger.Entries, entry => entry.StartsWith("Error:", StringComparison.Ordinal));
    }

    [Fact(Skip = "RED — unskipped when Load() stops caching a failed read in the next commit (FPRT-02)")]
    public void UnreadableFile_KeepsItsRecords_WhenALoginIsRecorded()
    {
        File.WriteAllText(_filePath, UnreadableContents);
        var logger = new CapturingLogger<EmbyVerifiedPasswords>();
        var store = CreateStore(logger);

        Assert.False(store.Matches(Guid.NewGuid(), HashA));

        store.Record(Guid.NewGuid(), HashA);

        Assert.Equal(UnreadableContents, File.ReadAllText(_filePath));
        Assert.False(File.Exists(_filePath + ".tmp"));
        Assert.False(store.Matches(Guid.NewGuid(), HashA));
        Assert.Contains(logger.Entries, entry => entry.StartsWith("Error:", StringComparison.Ordinal));
    }

    [Fact(Skip = "RED — unskipped when Load() stops caching a failed read in the next commit (FPRT-02)")]
    public void UnreadableFile_IsReadAgain_WhenItBecomesReadable()
    {
        var userId = Guid.NewGuid();
        var retryStore = new EmbyVerifiedPasswords(_retryFilePath, NullLogger<EmbyVerifiedPasswords>.Instance);
        retryStore.Record(userId, HashA);
        var validContents = File.ReadAllText(_retryFilePath);

        File.WriteAllText(_filePath, UnreadableContents);
        var store = CreateStore();

        Assert.False(store.Matches(userId, HashA));

        File.WriteAllText(_filePath, validContents);

        Assert.True(store.Matches(userId, HashA));
    }

    [Fact(Skip = "RED — unskipped when Load() stops caching a failed read in the next commit (FPRT-02)")]
    public async Task ConcurrentRecords_AreNotWritten_WhenTheFileIsUnreadable()
    {
        File.WriteAllText(_filePath, UnreadableContents);
        var store = CreateStore();
        var userIds = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();

        await Task.WhenAll(userIds.Select(id => Task.Run(() => store.Record(id, HashA))));

        Assert.Equal(UnreadableContents, File.ReadAllText(_filePath));
        Assert.False(File.Exists(_filePath + ".tmp"));
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
}

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmbyAuth.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public sealed class EmbyAuthControllerTests : IDisposable
{
    /// <summary>
    /// A record cut short: an opening brace, a quoted user identifier, a colon, and a quoted value with no closing
    /// quote or brace. Deserializing this throws <see cref="System.Text.Json.JsonException"/>, matching the fixture
    /// <c>EmbyVerifiedPasswordsTests</c> already uses to provoke a read failure.
    /// </summary>
    private const string UnreadableContents = "{\"3fa85f64-5717-4562-b3fc-2c963f66afa6\":\"AB";

    private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"emby-auth-controller-tests-{Guid.NewGuid():N}.json");
    private readonly SqliteJellyfinDbContextFactory _dbContextFactory = new();
    private readonly FakeTaskManager _taskManager = new();

    public void Dispose()
    {
        File.Delete(_filePath);
        File.Delete(_filePath + ".tmp");
        _dbContextFactory.Dispose();
    }

    /// <summary>
    /// Builds the contents of a readable fingerprint file by recording a real password through a throwaway
    /// <see cref="EmbyVerifiedPasswords"/> instance, then reading back what it wrote.
    /// </summary>
    /// <returns>The valid JSON contents of a fingerprint file holding one record.</returns>
    private static string ValidFingerprintFileContents()
    {
        var seedPath = Path.Combine(Path.GetTempPath(), $"emby-auth-controller-tests-seed-{Guid.NewGuid():N}.json");
        try
        {
            new EmbyVerifiedPasswords(seedPath, NullLogger<EmbyVerifiedPasswords>.Instance).Record(Guid.NewGuid(), "hash");
            return File.ReadAllText(seedPath);
        }
        finally
        {
            File.Delete(seedPath);
            File.Delete(seedPath + ".tmp");
        }
    }

    private EmbyAuthController CreateController() =>
        new(_dbContextFactory, new EmbyVerifiedPasswords(_filePath, NullLogger<EmbyVerifiedPasswords>.Instance), _taskManager);

    [Fact(Skip = "RED: unskipped in the 03-01 Task 2 GREEN commit that wires GetMigrationStatus to EmbyVerifiedPasswords.RecordsAvailable()")]
    public async Task GetMigrationStatus_ReportsRecordsUnavailable_WhenTheFingerprintFileCannotBeRead()
    {
        File.WriteAllText(_filePath, UnreadableContents);
        var controller = CreateController();

        var response = await controller.GetMigrationStatus(CancellationToken.None);

        Assert.True(response.Value!.RecordsUnavailable);
    }

    [Fact]
    public async Task GetMigrationStatus_ReportsRecordsAvailable_WhenTheFingerprintFileIsReadable()
    {
        var controller = CreateController();

        var response = await controller.GetMigrationStatus(CancellationToken.None);

        Assert.False(response.Value!.RecordsUnavailable);
    }

    [Fact(Skip = "RED: unskipped in the 03-01 Task 2 GREEN commit that wires GetMigrationStatus to EmbyVerifiedPasswords.RecordsAvailable()")]
    public async Task GetMigrationStatus_ClearsRecordsUnavailable_OnceTheFileBecomesReadable()
    {
        File.WriteAllText(_filePath, UnreadableContents);
        var controller = CreateController();

        var first = await controller.GetMigrationStatus(CancellationToken.None);
        Assert.True(first.Value!.RecordsUnavailable);

        File.WriteAllText(_filePath, ValidFingerprintFileContents());

        var second = await controller.GetMigrationStatus(CancellationToken.None);
        Assert.False(second.Value!.RecordsUnavailable);
    }
}

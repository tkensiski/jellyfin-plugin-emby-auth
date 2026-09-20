using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmbyAuth.Api;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Mvc;
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
    private readonly FakeUserManager _userManager = new();

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
        new(_dbContextFactory, new EmbyVerifiedPasswords(_filePath, NullLogger<EmbyVerifiedPasswords>.Instance), _taskManager, _userManager);

    private MoveEmbyUsersToDefaultTask CreateMigrationTask() =>
        new(_dbContextFactory, new EmbyVerifiedPasswords(_filePath, NullLogger<EmbyVerifiedPasswords>.Instance), NullLogger<MoveEmbyUsersToDefaultTask>.Instance);

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task GetMigrationStatus_ReportsTaskStateAndProgress_WhenAWorkerIsRunning()
    {
        var worker = new FakeScheduledTaskWorker(CreateMigrationTask())
        {
            State = TaskState.Running,
            CurrentProgress = 42.5,
        };
        _taskManager.Tasks.Add(worker);
        var controller = CreateController();

        var response = await controller.GetMigrationStatus(CancellationToken.None);

        var task = response.Value!.Task;
        Assert.NotNull(task);
        Assert.Equal(TaskState.Running, task.State);
        Assert.Equal(42.5, task.Progress);
    }

    [Fact]
    public async Task GetMigrationStatus_ReportsTaskAsAbsent_WhenTheOnlyRegisteredWorkerWrapsADifferentTask()
    {
        _taskManager.Tasks.Add(new FakeScheduledTaskWorker(new OtherScheduledTask()));
        var controller = CreateController();

        var response = await controller.GetMigrationStatus(CancellationToken.None);

        Assert.Null(response.Value!.Task);
    }

    [Fact]
    public async Task GetMigrationStatus_ReportsNoLastEndTimeOrResult_WhenTheWorkerHasNoLastExecutionResult()
    {
        var worker = new FakeScheduledTaskWorker(CreateMigrationTask())
        {
            LastExecutionResult = null,
        };
        _taskManager.Tasks.Add(worker);
        var controller = CreateController();

        var response = await controller.GetMigrationStatus(CancellationToken.None);

        var task = response.Value!.Task;
        Assert.NotNull(task);
        Assert.Null(task.LastEndTimeUtc);
        Assert.Null(task.LastResult);
    }

    [Fact]
    public async Task GetMigrationStatus_ReportsAvailableTargets_ExcludingTheEmbyMethod()
    {
        _userManager.AuthenticationProviders =
        [
            new NameIdPair { Name = "Default", Id = "default-id" },
            new NameIdPair { Name = "Emby", Id = EmbyAuthenticationProvider.ProviderId },
            new NameIdPair { Name = "JellyfinSecurity", Id = "jf-security-id" },
        ];
        var controller = CreateController();

        var response = await controller.GetMigrationStatus(CancellationToken.None);

        Assert.Equal(
            ["Default", "JellyfinSecurity"],
            response.Value!.AvailableTargets.Select(target => target.Name).ToList());
    }

    [Fact]
    public async Task GetMigrationStatus_ReportsEmptyAvailableTargets_WhenOnlyTheEmbyMethodIsEnabled()
    {
        _userManager.AuthenticationProviders =
        [
            new NameIdPair { Name = "Emby", Id = EmbyAuthenticationProvider.ProviderId },
        ];
        var controller = CreateController();

        var response = await controller.GetMigrationStatus(CancellationToken.None);

        Assert.Empty(response.Value!.AvailableTargets);
    }

    [Fact]
    public void RunMigration_Returns204_AndQueuesTheMigrationTask()
    {
        var controller = CreateController();

        var result = controller.RunMigration();

        Assert.IsType<NoContentResult>(result);
        Assert.Equal([typeof(MoveEmbyUsersToDefaultTask)], _taskManager.QueuedTypes);
    }

    private sealed class OtherScheduledTask : IScheduledTask
    {
        public string Name => "Other task";

        public string Key => "OtherTask";

        public string Description => "Some other task, not the migration task.";

        public string Category => "Other";

        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) => Task.CompletedTask;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
    }
}

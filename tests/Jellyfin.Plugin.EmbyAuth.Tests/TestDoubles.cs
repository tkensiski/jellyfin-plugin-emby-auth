using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Events;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.DbConfiguration;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Locking;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Cryptography;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Users;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public sealed record RecordedRequest(HttpMethod Method, Uri? Uri, string? Authorization, string? EmbyToken, string? Body, long? ContentLength);

public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Lock _lock = new();
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();
    private readonly TaskCompletionSource _firstRequestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile TaskCompletionSource? _hold;

    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>
    /// Gets a task that completes as soon as <see cref="SendAsync"/> has recorded its first request.
    /// </summary>
    public Task FirstRequestStarted => _firstRequestStarted.Task;

    public StubHttpMessageHandler Then(Func<HttpResponseMessage> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    /// <summary>
    /// Makes every response returned after this call wait until <see cref="ReleaseResponses"/> runs. The request is
    /// still recorded into <see cref="Requests"/> before the wait begins.
    /// </summary>
    public void HoldResponses() => _hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Releases any response that <see cref="HoldResponses"/> is holding open. Safe to call when nothing is held.
    /// </summary>
    public void ReleaseResponses() => _hold?.TrySetResult();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var contentLength = request.Content?.Headers.ContentLength;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        Func<HttpResponseMessage> factory;
        lock (_lock)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri,
                HeaderValue(request, "Authorization"),
                HeaderValue(request, "X-Emby-Token"),
                body,
                contentLength));

            if (Requests.Count == 1)
            {
                _firstRequestStarted.TrySetResult();
            }

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException($"No stub response for {request.Method} {request.RequestUri}");
            }

            factory = _responses.Dequeue();
        }

        if (_hold is { } hold)
        {
            await hold.Task.ConfigureAwait(false);
        }

        return factory();
    }

    private static string? HeaderValue(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}

public sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IEnumerable<string> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _entries.Enqueue($"{logLevel}: {formatter(state, exception)} {exception}");
    }
}

/// <summary>
/// A hand-written <see cref="IUserManager"/> double. It implements <see cref="CreateUserAsync"/>, <see cref="UpdateUserAsync"/>,
/// and <see cref="DeleteUserAsync"/>, each of which can be made to throw a chosen exception. Every other member throws
/// <see cref="NotImplementedException"/>, because the plugin never calls them.
/// </summary>
public sealed class FakeUserManager : IUserManager
{
    /// <summary>
    /// Gets the ordered list of method names this fake was called with, recorded before any configured exception throws.
    /// </summary>
    public List<string> Calls { get; } = [];

    /// <summary>
    /// Gets or sets the exception that <see cref="CreateUserAsync"/> throws, or <c>null</c> to succeed.
    /// </summary>
    public Exception? CreateUserThrows { get; set; }

    /// <summary>
    /// Gets or sets the exception that <see cref="UpdateUserAsync"/> throws, or <c>null</c> to succeed.
    /// </summary>
    public Exception? UpdateUserThrows { get; set; }

    /// <summary>
    /// Gets or sets the exception that <see cref="DeleteUserAsync"/> throws, or <c>null</c> to succeed.
    /// </summary>
    public Exception? DeleteUserThrows { get; set; }

    /// <summary>
    /// Gets the user that <see cref="CreateUserAsync"/> last returned.
    /// </summary>
    public User? LastCreatedUser { get; private set; }

    /// <summary>
    /// Gets the user that <see cref="UpdateUserAsync"/> last recorded.
    /// </summary>
    public User? LastUpdatedUser { get; private set; }

    /// <summary>
    /// Gets the user ID that <see cref="DeleteUserAsync"/> last recorded.
    /// </summary>
    public Guid? LastDeletedId { get; private set; }

    /// <inheritdoc />
    public event EventHandler<GenericEventArgs<User>>? OnUserUpdated
    {
        add => throw new NotImplementedException();
        remove => throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<User> CreateUserAsync(string name)
    {
        Calls.Add(nameof(CreateUserAsync));
        if (CreateUserThrows is { } exception)
        {
            throw exception;
        }

        var user = new User(name, "provider", "reset-provider");
        user.AddDefaultPermissions();
        user.AddDefaultPreferences();
        LastCreatedUser = user;
        return Task.FromResult(user);
    }

    /// <inheritdoc />
    public Task UpdateUserAsync(User user)
    {
        Calls.Add(nameof(UpdateUserAsync));
        if (UpdateUserThrows is { } exception)
        {
            throw exception;
        }

        LastUpdatedUser = user;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteUserAsync(Guid userId)
    {
        Calls.Add(nameof(DeleteUserAsync));
        if (DeleteUserThrows is { } exception)
        {
            throw exception;
        }

        LastDeletedId = userId;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IEnumerable<User> GetUsers() => throw new NotImplementedException();

    /// <inheritdoc />
    public IEnumerable<Guid> GetUsersIds() => throw new NotImplementedException();

    /// <inheritdoc />
    public Task InitializeAsync() => throw new NotImplementedException();

    /// <inheritdoc />
    public User? GetUserById(Guid id) => throw new NotImplementedException();

    /// <inheritdoc />
    public User? GetFirstUser() => throw new NotImplementedException();

    /// <inheritdoc />
    public User? GetUserByName(string name) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task RenameUser(Guid userId, string oldName, string newName) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task ResetPassword(Guid userId) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task ChangePassword(Guid userId, string newPassword) => throw new NotImplementedException();

    /// <inheritdoc />
    public UserDto GetUserDto(User user, string? remoteEndPoint = null) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task<User?> AuthenticateUser(string username, string password, string remoteEndPoint, bool isUserSession) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task<ForgotPasswordResult> StartForgotPasswordProcess(string enteredUsername, bool isInNetwork) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task<PinRedeemResult> RedeemPasswordResetPin(string pin) => throw new NotImplementedException();

    /// <summary>
    /// Gets or sets the value <see cref="GetAuthenticationProviders"/> returns.
    /// </summary>
    public NameIdPair[] AuthenticationProviders { get; set; } = [];

    /// <inheritdoc />
    public NameIdPair[] GetAuthenticationProviders() => AuthenticationProviders;

    /// <inheritdoc />
    public NameIdPair[] GetPasswordResetProviders() => throw new NotImplementedException();

    /// <inheritdoc />
    public Task UpdateConfigurationAsync(Guid userId, UserConfiguration config) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task UpdatePolicyAsync(Guid userId, UserPolicy policy) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task ClearProfileImageAsync(User user) => throw new NotImplementedException();
}

/// <summary>
/// A hand-written <see cref="ICryptoProvider"/> double. Only <see cref="CreatePasswordHash"/> has a real body: it returns a
/// deterministic hash that is distinguishable per input password. Every other member throws <see cref="NotImplementedException"/>,
/// because the plugin never calls them.
/// </summary>
public sealed class FakeCryptoProvider : ICryptoProvider
{
    /// <inheritdoc />
    public string DefaultHashMethod => throw new NotImplementedException();

    /// <inheritdoc />
    public PasswordHash CreatePasswordHash(ReadOnlySpan<char> password) =>
        new("fake", Encoding.UTF8.GetBytes(password.ToString()));

    /// <inheritdoc />
    public bool Verify(PasswordHash hash, ReadOnlySpan<char> password) => throw new NotImplementedException();

    /// <inheritdoc />
    public byte[] GenerateSalt() => throw new NotImplementedException();

    /// <inheritdoc />
    public byte[] GenerateSalt(int length) => throw new NotImplementedException();
}

/// <summary>
/// A hand-written <see cref="IJellyfinDatabaseProvider"/> double for the SQLite-backed test seam.
/// </summary>
/// <remarks>
/// <see cref="OnModelCreating"/> and <see cref="ConfigureConventions"/> are deliberately no-ops: Jellyfin's real
/// SQLite provider sets a default <see cref="DateTimeKind"/> and disables the <c>RETURNING</c> clause, and no test
/// in this phase reads or writes a <see cref="DateTime"/> column on <see cref="User"/> or asserts on a value
/// returned from <c>ExecuteUpdateAsync</c>.
/// </remarks>
public sealed class FakeJellyfinDatabaseProvider : IJellyfinDatabaseProvider
{
    /// <inheritdoc />
    public IDbContextFactory<JellyfinDbContext>? DbContextFactory { get; set; }

    /// <inheritdoc />
    public void Initialise(DbContextOptionsBuilder options, DatabaseConfigurationOptions databaseConfiguration)
    {
    }

    /// <inheritdoc />
    public void OnModelCreating(ModelBuilder modelBuilder)
    {
    }

    /// <inheritdoc />
    public void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
    }

    /// <inheritdoc />
    public Task RunScheduledOptimisation(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task RunShutdownTask(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<string> MigrationBackupFast(CancellationToken cancellationToken) => Task.FromResult(string.Empty);

    /// <inheritdoc />
    public Task RestoreBackupFast(string key, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task DeleteBackup(string key) => Task.CompletedTask;

    /// <inheritdoc />
    public Task PurgeDatabase(JellyfinDbContext dbContext, IEnumerable<string>? tableNames) => Task.CompletedTask;
}

/// <summary>
/// Hands out a real <see cref="JellyfinDbContext"/> backed by one held-open SQLite in-memory connection, so tests can
/// exercise <c>ExecuteUpdateAsync</c> and other EF Core translations that the InMemory provider cannot run.
/// </summary>
/// <remarks>
/// The connection stays open for the lifetime of this factory: a SQLite in-memory database is destroyed the instant
/// its last connection closes, and production hands out a fresh <see cref="JellyfinDbContext"/> per unit of work
/// through the same factory contract. Construct one instance per test class and dispose it — never share an
/// instance across test classes, so two classes running in parallel cannot see each other's rows.
/// </remarks>
public sealed class SqliteJellyfinDbContextFactory : IDbContextFactory<JellyfinDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<JellyfinDbContext> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteJellyfinDbContextFactory"/> class, opening the backing
    /// connection and creating the schema.
    /// </summary>
    public SqliteJellyfinDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<JellyfinDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateDbContext();
        context.Database.EnsureCreated();
    }

    /// <inheritdoc />
    public JellyfinDbContext CreateDbContext() => new(
        _options,
        NullLogger<JellyfinDbContext>.Instance,
        new FakeJellyfinDatabaseProvider(),
        new NoLockBehavior(NullLogger<NoLockBehavior>.Instance));

    /// <inheritdoc />
    public void Dispose() => _connection.Dispose();
}

/// <summary>
/// A hand-written <see cref="ITaskManager"/> double. Only <see cref="ScheduledTasks"/> and
/// <see cref="QueueIfNotRunning{T}"/> have real bodies. Every other member throws
/// <see cref="NotImplementedException"/>, because the plugin never calls them.
/// </summary>
public sealed class FakeTaskManager : ITaskManager
{
    /// <summary>
    /// Gets the workers that <see cref="ScheduledTasks"/> returns.
    /// </summary>
    public List<IScheduledTaskWorker> Tasks { get; } = [];

    /// <summary>
    /// Gets the ordered list of task types that <see cref="QueueIfNotRunning{T}"/> was called with.
    /// </summary>
    public List<Type> QueuedTypes { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<IScheduledTaskWorker> ScheduledTasks => Tasks;

    /// <inheritdoc />
    public event EventHandler<GenericEventArgs<IScheduledTaskWorker>>? TaskExecuting
    {
        add => throw new NotImplementedException();
        remove => throw new NotImplementedException();
    }

    /// <inheritdoc />
    public event EventHandler<TaskCompletionEventArgs>? TaskCompleted
    {
        add => throw new NotImplementedException();
        remove => throw new NotImplementedException();
    }

    /// <inheritdoc />
    public void QueueIfNotRunning<T>()
        where T : IScheduledTask => QueuedTypes.Add(typeof(T));

    /// <inheritdoc />
    public void QueueScheduledTask<T>()
        where T : IScheduledTask => throw new NotImplementedException();

    /// <inheritdoc />
    public void QueueScheduledTask<T>(TaskOptions options)
        where T : IScheduledTask => throw new NotImplementedException();

    /// <inheritdoc />
    public void QueueScheduledTask(IScheduledTask task, TaskOptions options) => throw new NotImplementedException();

    /// <inheritdoc />
    public void CancelIfRunning<T>()
        where T : IScheduledTask => throw new NotImplementedException();

    /// <inheritdoc />
    public void CancelIfRunningAndQueue<T>()
        where T : IScheduledTask => throw new NotImplementedException();

    /// <inheritdoc />
    public void CancelIfRunningAndQueue<T>(TaskOptions options)
        where T : IScheduledTask => throw new NotImplementedException();

    /// <inheritdoc />
    public void AddTasks(IEnumerable<IScheduledTask> tasks) => throw new NotImplementedException();

    /// <inheritdoc />
    public void Cancel(IScheduledTaskWorker task) => throw new NotImplementedException();

    /// <inheritdoc />
    public Task Execute(IScheduledTaskWorker task, TaskOptions options) => throw new NotImplementedException();

    /// <inheritdoc />
    public void Execute<T>()
        where T : IScheduledTask => throw new NotImplementedException();

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>
/// A hand-written <see cref="IScheduledTaskWorker"/> double wrapping a constructor-supplied <see cref="IScheduledTask"/>.
/// </summary>
/// <param name="task">The wrapped scheduled task.</param>
public sealed class FakeScheduledTaskWorker(IScheduledTask task) : IScheduledTaskWorker
{
    /// <inheritdoc />
    public IScheduledTask ScheduledTask => task;

    /// <inheritdoc />
    public TaskState State { get; set; } = TaskState.Idle;

    /// <inheritdoc />
    public double? CurrentProgress { get; set; }

    /// <inheritdoc />
    public TaskResult? LastExecutionResult { get; set; } = new();

    /// <inheritdoc />
    public string Name => task.Name;

    /// <inheritdoc />
    public string Description => task.Description;

    /// <inheritdoc />
    public string Category => task.Category;

    /// <inheritdoc />
    public string Id => "fake-worker-id";

    /// <inheritdoc />
    public IReadOnlyList<TaskTriggerInfo> Triggers { get; set; } = [];

    /// <inheritdoc />
    public void ReloadTriggerEvents()
    {
    }

    /// <inheritdoc />
    public event EventHandler<GenericEventArgs<double>>? TaskProgress
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

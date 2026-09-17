using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Events;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Cryptography;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Users;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public sealed record RecordedRequest(HttpMethod Method, Uri? Uri, string? Authorization, string? EmbyToken, string? Body, long? ContentLength);

public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public StubHttpMessageHandler Then(Func<HttpResponseMessage> response)
    {
        _responses.Enqueue(response);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var contentLength = request.Content?.Headers.ContentLength;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri,
            HeaderValue(request, "Authorization"),
            HeaderValue(request, "X-Emby-Token"),
            body,
            contentLength));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No stub response for {request.Method} {request.RequestUri}");
        }

        return _responses.Dequeue()();
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

    /// <inheritdoc />
    public NameIdPair[] GetAuthenticationProviders() => throw new NotImplementedException();

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

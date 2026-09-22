using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// The result of a login that Emby accepted.
/// </summary>
/// <param name="Name">The Emby user name.</param>
/// <param name="EnableRemoteAccess">Whether Emby allows the user to connect from outside the local network.</param>
internal sealed record EmbyLogin(string Name, bool EnableRemoteAccess);

/// <summary>
/// An entry in the Emby user list.
/// </summary>
/// <param name="Name">The Emby user name.</param>
/// <param name="IsDisabled">Whether the Emby user is disabled.</param>
internal sealed record EmbyUser(string Name, bool IsDisabled);

/// <summary>
/// Sends requests to an Emby server. This is the only code that contacts Emby.
/// </summary>
/// <param name="httpClientFactory">The HTTP client factory.</param>
/// <param name="logger">The logger. Messages never contain a password or the API key.</param>
internal sealed partial class EmbyClient(IHttpClientFactory httpClientFactory, ILogger<EmbyClient> logger)
{
    private const string ClientAuthorization =
        "MediaBrowser Client=\"Jellyfin Emby Auth\", Device=\"Jellyfin\", DeviceId=\"jellyfin-plugin-emby-auth\", Version=\"1.0.0\"";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Logs in to Emby with the given credentials, then ends the Emby session.
    /// </summary>
    /// <param name="embyServerUrl">The base URL of the Emby server.</param>
    /// <param name="username">The user name that the person typed.</param>
    /// <param name="password">The password that the person typed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The login if Emby accepts it; otherwise <c>null</c>.</returns>
    public async Task<EmbyLogin?> AuthenticateAsync(Uri embyServerUrl, string username, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embyServerUrl);
        var baseUrl = WithTrailingSlash(embyServerUrl);
        using var client = CreateClient();

        AuthenticateByNameResponse? login;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, "Users/AuthenticateByName"));
            request.Headers.TryAddWithoutValidation("Authorization", ClientAuthorization);

            // Emby answers HTTP 400 to a chunked body, so send a buffered body that has a Content-Length.
            request.Content = new StringContent(
                JsonSerializer.Serialize(new AuthenticateByNameRequest(username, password)),
                Encoding.UTF8,
                "application/json");
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogLoginRejected(logger, username, (int)response.StatusCode);
                return null;
            }

            login = await response.Content.ReadFromJsonAsync<AuthenticateByNameResponse>(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsUnreachable(ex, cancellationToken))
        {
            LogEmbyUnreachable(logger, ex, baseUrl);
            return null;
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            LogUnreadableResponse(logger, ex, baseUrl);
            return null;
        }

        var embyUserName = login?.User?.Name;
        var accessToken = login?.AccessToken;

        await SignOutAsync(client, baseUrl, embyUserName, accessToken, username, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(embyUserName))
        {
            LogResponseWithoutUserName(logger, baseUrl);
            return null;
        }

        return new EmbyLogin(embyUserName, login?.User?.Policy?.EnableRemoteAccess ?? false);
    }

    /// <summary>
    /// Reads the list of Emby users.
    /// </summary>
    /// <param name="embyServerUrl">The base URL of the Emby server.</param>
    /// <param name="apiKey">The Emby API key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The users that have a name, or <c>null</c> if Emby does not return a user list.</returns>
    public async Task<IReadOnlyList<EmbyUser>?> GetUsersAsync(Uri embyServerUrl, string apiKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embyServerUrl);
        var baseUrl = WithTrailingSlash(embyServerUrl);
        using var client = CreateClient();

        List<UserResponse>? users;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, "Users"));
            request.Headers.TryAddWithoutValidation("X-Emby-Token", apiKey);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogUserListRejected(logger, baseUrl, (int)response.StatusCode);
                return null;
            }

            users = await response.Content.ReadFromJsonAsync<List<UserResponse>>(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsUnreachable(ex, cancellationToken))
        {
            LogEmbyUnreachable(logger, ex, baseUrl);
            return null;
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            LogUnreadableResponse(logger, ex, baseUrl);
            return null;
        }

        if (users is null)
        {
            LogUnreadableResponse(logger, null, baseUrl);
            return null;
        }

        return users
            .Where(user => !string.IsNullOrEmpty(user.Name))
            .Select(user => new EmbyUser(user.Name!, user.Policy?.IsDisabled ?? false))
            .ToList();
    }

    private static bool IsUnreachable(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    // ReadFromJsonAsync throws InvalidOperationException for a response with an unsupported charset.
    private static bool IsUnreadable(Exception exception) =>
        exception is JsonException or InvalidOperationException;

    private static Uri WithTrailingSlash(Uri url) =>
        url.AbsoluteUri.EndsWith('/') ? url : new Uri(url.AbsoluteUri + "/");

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient(NamedClient.Default);
        client.Timeout = RequestTimeout;
        return client;
    }

    private async Task SignOutAsync(HttpClient client, Uri baseUrl, string? embyUserName, string? accessToken, string username, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            return;
        }

        // Emby's response can carry a token with no user name (AUTH-05). The typed user name is not a secret and is
        // already logged by LogLoginRejected, so it names the sign-out when Emby's own response does not.
        var signOutUserName = string.IsNullOrEmpty(embyUserName) ? username : embyUserName;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, "Sessions/Logout"));
            request.Headers.TryAddWithoutValidation("Authorization", $"{ClientAuthorization}, Token=\"{accessToken}\"");
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogSignOutRejected(logger, signOutUserName, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (IsUnreachable(ex, cancellationToken))
        {
            LogSignOutFailed(logger, ex, signOutUserName);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Emby did not accept the login for user {Username}. Emby returned HTTP {StatusCode}.")]
    private static partial void LogLoginRejected(ILogger logger, string username, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Emby server at {EmbyServerUrl} refused the request for the user list. Emby returned HTTP {StatusCode}. Make sure that the Emby API key in the plugin settings is correct.")]
    private static partial void LogUserListRejected(ILogger logger, Uri embyServerUrl, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jellyfin did not get a response from the Emby server at {EmbyServerUrl}. Make sure that Emby runs. Make sure that the Emby server URL in the plugin settings is correct.")]
    private static partial void LogEmbyUnreachable(ILogger logger, Exception exception, Uri embyServerUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Emby server at {EmbyServerUrl} sent a response that Jellyfin cannot read.")]
    private static partial void LogUnreadableResponse(ILogger logger, Exception? exception, Uri embyServerUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Emby server at {EmbyServerUrl} sent a login response that has no user name.")]
    private static partial void LogResponseWithoutUserName(ILogger logger, Uri embyServerUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jellyfin did not end the Emby session for user {Username}. Emby returned HTTP {StatusCode}. The session can stay on the Emby server.")]
    private static partial void LogSignOutRejected(ILogger logger, string username, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jellyfin did not end the Emby session for user {Username}. The session can stay on the Emby server.")]
    private static partial void LogSignOutFailed(ILogger logger, Exception exception, string username);

    private sealed record AuthenticateByNameRequest(
        [property: JsonPropertyName("Username")] string Username,
        [property: JsonPropertyName("Pw")] string Password);

    private sealed record AuthenticateByNameResponse(
        [property: JsonPropertyName("User")] UserResponse? User,
        [property: JsonPropertyName("AccessToken")] string? AccessToken);

    private sealed record UserResponse(
        [property: JsonPropertyName("Name")] string? Name,
        [property: JsonPropertyName("Policy")] PolicyResponse? Policy);

    private sealed record PolicyResponse(
        [property: JsonPropertyName("EnableRemoteAccess")] bool? EnableRemoteAccess,
        [property: JsonPropertyName("IsDisabled")] bool? IsDisabled);
}

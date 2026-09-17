using System;
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
/// Checks a user name and password against an Emby server.
/// </summary>
/// <param name="httpClientFactory">The HTTP client factory.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class EmbyClient(IHttpClientFactory httpClientFactory, ILogger<EmbyClient> logger)
{
    private const string ClientAuthorization =
        "MediaBrowser Client=\"Jellyfin Emby Auth\", Device=\"Jellyfin\", DeviceId=\"jellyfin-plugin-emby-auth\", Version=\"1.0.0\"";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Logs in to Emby with the given credentials, then ends the Emby session.
    /// </summary>
    /// <param name="embyServerUrl">The base URL of the Emby server.</param>
    /// <param name="username">The user name that the person typed.</param>
    /// <param name="password">The password that the person typed.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The Emby user name if Emby accepts the login; otherwise <c>null</c>.</returns>
    public async Task<string?> AuthenticateAsync(Uri embyServerUrl, string username, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embyServerUrl);
        var baseUrl = WithTrailingSlash(embyServerUrl);
        using var client = httpClientFactory.CreateClient(NamedClient.Default);
        client.Timeout = RequestTimeout;

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
        catch (HttpRequestException ex)
        {
            LogEmbyUnreachable(logger, ex, baseUrl);
            return null;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogEmbyUnreachable(logger, ex, baseUrl);
            return null;
        }
        catch (JsonException ex)
        {
            LogUnreadableResponse(logger, ex, baseUrl);
            return null;
        }

        var embyUserName = login?.User?.Name;
        if (string.IsNullOrEmpty(embyUserName))
        {
            LogResponseWithoutUserName(logger, baseUrl);
            return null;
        }

        await SignOutAsync(client, baseUrl, embyUserName, login?.AccessToken, cancellationToken).ConfigureAwait(false);
        return embyUserName;
    }

    private static Uri WithTrailingSlash(Uri url) =>
        url.AbsoluteUri.EndsWith('/') ? url : new Uri(url.AbsoluteUri + "/");

    private async Task SignOutAsync(HttpClient client, Uri baseUrl, string embyUserName, string? accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, "Sessions/Logout"));
            request.Headers.TryAddWithoutValidation("Authorization", $"{ClientAuthorization}, Token=\"{accessToken}\"");
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogSignOutRejected(logger, embyUserName, (int)response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            LogSignOutFailed(logger, ex, embyUserName);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogSignOutFailed(logger, ex, embyUserName);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Emby did not accept the login for user {Username}. Emby returned HTTP {StatusCode}.")]
    private static partial void LogLoginRejected(ILogger logger, string username, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jellyfin did not get a login response from the Emby server at {EmbyServerUrl}. Make sure that Emby runs. Make sure that the Emby server URL in the plugin settings is correct.")]
    private static partial void LogEmbyUnreachable(ILogger logger, Exception exception, Uri embyServerUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Emby server at {EmbyServerUrl} sent a login response that Jellyfin cannot read.")]
    private static partial void LogUnreadableResponse(ILogger logger, Exception exception, Uri embyServerUrl);

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
        [property: JsonPropertyName("User")] EmbyUser? User,
        [property: JsonPropertyName("AccessToken")] string? AccessToken);

    private sealed record EmbyUser([property: JsonPropertyName("Name")] string? Name);
}

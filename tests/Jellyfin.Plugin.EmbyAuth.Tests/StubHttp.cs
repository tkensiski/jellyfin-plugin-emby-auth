using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.EmbyAuth.Tests;

public sealed record RecordedRequest(HttpMethod Method, Uri? Uri, string? Authorization, string? Body, long? ContentLength);

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
        var authorization = request.Headers.TryGetValues("Authorization", out var values) ? string.Join(",", values) : null;
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri, authorization, body, contentLength));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No stub response for {request.Method} {request.RequestUri}");
        }

        return _responses.Dequeue()();
    }
}

public sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

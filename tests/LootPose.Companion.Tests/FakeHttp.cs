using System.Net;
using System.Text;

namespace LootPose.Companion.Tests;

/// <summary>What the fake server saw. Copied when the request is sent, because requests are disposed afterwards.</summary>
internal sealed record SentRequest(HttpMethod Method, Uri? Uri, Dictionary<string, string> Headers, string? Body);

/// <summary>Answers HTTP requests from a queue, in order, and keeps a copy of every request.</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _answers = new();

    public List<SentRequest> Requests { get; } = new();

    public FakeHttp Reply(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _answers.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        return this;
    }

    /// <summary>The next request fails as if the PC were offline.</summary>
    public FakeHttp Fail()
    {
        _answers.Enqueue(() => throw new HttpRequestException("offline"));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(" ", header.Value);
        }
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key] = string.Join(" ", header.Value);
            }
        }
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new SentRequest(request.Method, request.RequestUri, headers, body));
        if (_answers.Count == 0) throw new InvalidOperationException($"No answer queued for request {Requests.Count}.");
        return _answers.Dequeue()();
    }
}

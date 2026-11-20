using System.Net;
using System.Text;

namespace SchoolLibrary.Tests.Services;

/// <summary>A source that answers every request with the same body and remembers the last address.</summary>
internal sealed class FakeSourceHandler : HttpMessageHandler
{
    private readonly string _body;
    private readonly HttpStatusCode _status;

    public FakeSourceHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _body = body;
        _status = status;
    }

    public Uri? LastUri { get; private set; }

    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastUri = request.RequestUri;
        Requests++;
        return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") });
    }
}

internal sealed class FakeSourceFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public FakeSourceFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

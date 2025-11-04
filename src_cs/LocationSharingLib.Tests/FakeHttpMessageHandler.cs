using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LocationSharingLib.Tests;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly string _response;
    private readonly HttpStatusCode _statusCode;
    public FakeHttpMessageHandler(string response, HttpStatusCode statusCode = HttpStatusCode.OK)
    { _response = response; _statusCode = statusCode; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(_statusCode){ Content = new StringContent(_response) });
}

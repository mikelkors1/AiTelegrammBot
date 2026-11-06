namespace ItmoBot.Tests.TestDoubles;

internal sealed class WeatherHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public int DisposeCalls
    {
        get; private set;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return send(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCalls++;
        }

        base.Dispose(disposing);
    }
}

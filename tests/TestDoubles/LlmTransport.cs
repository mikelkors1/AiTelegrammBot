using System.Net;
using System.Text;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class LlmTransport : HttpMessageHandler
{
    public string ResponseBody { get; init; } = "{}";
    public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
    public Exception? Error
    {
        get; init;
    }
    public bool WaitForCancellation
    {
        get; init;
    }
    public bool WasDisposed
    {
        get; private set;
    }
    public TimeSpan? RetryAfter
    {
        get; init;
    }
    public int Calls
    {
        get; private set;
    }
    public string? Body
    {
        get; private set;
    }
    public Uri? Url
    {
        get; private set;
    }
    public string? Authorization
    {
        get; private set;
    }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        Body = await request.Content!.ReadAsStringAsync(cancellationToken);
        Url = request.RequestUri;
        Authorization = request.Headers.Authorization?.ToString();
        Started.TrySetResult();
        if (WaitForCancellation)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        if (Error is not null)
        {
            throw Error;
        }

        var response = new HttpResponseMessage(Status)
        {
            Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
        };
        if (RetryAfter is { } delay)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
        }

        return response;
    }

    protected override void Dispose(bool disposing)
    {
        WasDisposed = true;
        base.Dispose(disposing);
    }
}

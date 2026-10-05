using System.Net;
using System.Text;

namespace ItmoBot.Tests.TestDoubles;

internal sealed class TelegramTransport : HttpMessageHandler
{
    public System.Net.HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
    public Exception? Error
    {
        get; init;
    }
    public bool MalformedJson
    {
        get; init;
    }
    public string WebhookUrl { get; init; } = "https://example.org/hook";
    public string UpdatesJson { get; init; } = "[]";
    public List<string> Methods { get; } = [];
    public string? ContentType
    {
        get; private set;
    }
    public string? Body
    {
        get;
        private set;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Error is not null)
        {
            throw Error;
        }

        var method = request.RequestUri!.Segments[^1];
        ContentType = request.Content?.Headers.ContentType?.MediaType;
        Methods.Add(method);
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var result = method switch
        {
            "getUpdates" => UpdatesJson,
            "deleteMessage" => "true",
            "getMe" => "{\"id\":123456789,\"is_bot\":true,\"first_name\":\"Test\",\"username\":\"test_bot\"}",
            "getWebhookInfo" => "{\"url\":" + System.Text.Json.JsonSerializer.Serialize(WebhookUrl) + ",\"has_custom_certificate\":false,\"pending_update_count\":0}",
            _ => "{\"message_id\":1,\"date\":0,\"chat\":{\"id\":42,\"type\":\"private\"},\"text\":\"ok\"}"
        };
        return new HttpResponseMessage(Status)
        {
            Content = new StringContent(MalformedJson ? "invalid-json" : Status == HttpStatusCode.OK ? "{\"ok\":true,\"result\":" + result + "}" : "{\"ok\":false,\"error_code\":" + (int)Status + ",\"description\":\"secret-error\"}", Encoding.UTF8, "application/json")
        };
    }
}

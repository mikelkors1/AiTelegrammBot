using System.Text.Json;
using ItmoBot.Application.Mcp;
using ItmoBot.Shared.Errors;
using ItmoBot.Shared.Results;
using ItmoBot.Shared.Validation;
using ItmoBot.Validation;

namespace ItmoBot.Application.AgentLoop;

public sealed class AgentReplyParser
{
    private readonly ToolSchemaValidator validator;

    public AgentReplyParser(ToolSchemaValidator validator)
    {
        this.validator = validator;
        using var document = JsonDocument.Parse("""
            {"type":"object","properties":{
                "action":{"type":"string","enum":["respond","clarify"]},
                "text":{"type":"string","minLength":1,"description":"Полный ответ пользователю, включая запрошенный программный код. Код должен находиться внутри этой строки в блоке с тремя обратными кавычками и языком, например csharp. Не заменяй реализацию кратким описанием."},
                "reason":{"type":"string","minLength":1,"maxLength":200}},
                "required":["action","text","reason"],"additionalProperties":false}
            """);
        Schema = RequestChecks.McpObjectSchema(document.RootElement);
    }

    public JsonElement Schema
    {
        get;
    }

    public Result<AgentFinalReply> Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
            var value = document.RootElement;
            if (validator.IsValid(Schema, value))
            {
                return new Result<AgentFinalReply>.Success(new AgentFinalReply(RequestChecks.LlmText(value.GetProperty("text").GetString()!),
                    AgentChecks.AuditReason(value.GetProperty("reason").GetString()!), value.GetProperty("action").GetString() == "clarify"));
            }
        }
        catch (Exception error) when (error is ArgumentException or JsonException)
        {
            // Повреждённый ответ не преобразуется в успешный диалог.
        }

        return new Result<AgentFinalReply>.Failure(new AppError(ErrorCode.InvalidLlmResponse, "Модель вернула ответ вне ожидаемой схемы агента."));
    }
}

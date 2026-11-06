using System.Text.Json;

namespace ItmoBot.Application.Llm;

public sealed record class LlmToolCall
{
    public LlmToolCall(string id, string name, JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object || arguments.GetRawText().Length > 4096)
        {
            throw new ArgumentException("Аргументы инструмента должны быть JSON-объектом ограниченного размера.");
        }

        Id = id;
        Name = name;
        Arguments = arguments.Clone();
    }

    public string Id
    {
        get;
    }
    public string Name
    {
        get;
    }
    public JsonElement Arguments
    {
        get;
    }
}

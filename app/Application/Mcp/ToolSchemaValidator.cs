using System.Text.Json;
using Json.Schema;

namespace ItmoBot.Application.Mcp;

public sealed class ToolSchemaValidator
{
    public bool IsValid(JsonElement schema, JsonElement instance)
    {
        try
        {
            if (!IsLocal(schema, 0) || !HasUniqueKeys(instance, 0))
            {
                return false;
            }

            var built = JsonSchema.Build(schema, new BuildOptions
            {
                Dialect = Dialect.Draft202012,
                SchemaRegistry = new SchemaRegistry { Fetch = (_, _) => null },
            });
            return built.Evaluate(instance, new EvaluationOptions { RequireFormatValidation = true }).IsValid;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or JsonSchemaException)
        {
            return false;
        }
    }

    private bool IsLocal(JsonElement value, int depth)
    {
        if (depth > 32)
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name is "$ref" or "$dynamicRef" && (property.Value.ValueKind != JsonValueKind.String
                    || !property.Value.GetString()!.StartsWith('#')))
                {
                    return false;
                }

                if (!IsLocal(property.Value, depth + 1))
                {
                    return false;
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray().All(item => IsLocal(item, depth + 1));
        }

        return true;
    }

    private bool HasUniqueKeys(JsonElement value, int depth)
    {
        if (depth > 32)
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            return value.EnumerateObject().All(property => names.Add(property.Name) && HasUniqueKeys(property.Value, depth + 1));
        }

        return value.ValueKind != JsonValueKind.Array || value.EnumerateArray().All(item => HasUniqueKeys(item, depth + 1));
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ItmoBot.Application.Agent.Models;
using ItmoBot.Application.AgentLoop;
using ItmoBot.Application.Conversations;
using ItmoBot.Application.Conversations.Context;
using ItmoBot.Application.Conversations.Prompts;
using ItmoBot.Application.Llm;
using ItmoBot.Application.Mcp;
using ItmoBot.Configuration.Loading;
using ItmoBot.Configuration.Models;
using ItmoBot.Infrastructure.Llm;
using ItmoBot.Infrastructure.Mcp;
using ItmoBot.Shared.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ItmoBot.Tools;

public sealed class RoutingExperimentRunner
{
    public async Task<int> RunAsync(string[] args)
    {
        try
        {
            return await RunCoreAsync(args);
        }
        catch (Exception error)
        {
            var location = new StackTrace(error, true).GetFrames()
                .FirstOrDefault(frame => frame.GetMethod()?.DeclaringType?.FullName?.Contains(nameof(RoutingExperimentRunner), StringComparison.Ordinal) == true);
            Console.Error.WriteLine($"ExperimentFailure: {error.GetType().Name}; строка {location?.GetFileLineNumber()}. Сообщение исключения скрыто.");
            return 1;
        }
    }

    private async Task<int> RunCoreAsync(string[] args)
    {
        var inputPath = "docs/evaluation/lab2-routing.json";
        var outputPath = args.Length > 1 ? args[1] : "docs/experiments/lab2/routing-baseline.json";
        if (File.Exists(outputPath))
        {
            Console.Error.WriteLine("Файл результата уже существует. Укажите новый путь, чтобы сохранить предыдущий прогон.");
            return 1;
        }

        var loaded = new SettingsLoader(new EnvironmentFileReader()).Load(".env");
        if (loaded is Result<Settings>.Failure failure)
        {
            Console.Error.WriteLine(failure.Error.Code);
            return 1;
        }

        var settings = ((Result<Settings>.Success)loaded).Value;
        var bytes = await File.ReadAllBytesAsync(inputPath);
        using var dataset = JsonDocument.Parse(bytes);
        var cases = dataset.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        string? previousPath = args.Length > 2 ? args[2] : null;
        if (previousPath is not null)
        {
            using var previous = JsonDocument.Parse(await File.ReadAllTextAsync(previousPath));
            if (previous.RootElement.GetProperty("status").GetString() != "completed"
                || previous.RootElement.GetProperty("model").GetString() != settings.Llm.Model)
            {
                throw new ArgumentException("Для повтора требуется завершённый прогон той же модели.");
            }

            var failed = previous.RootElement.GetProperty("cases").EnumerateArray()
                .Where(row => !row.GetProperty("action_correct").GetBoolean()
                    || row.GetProperty("arguments_correct").ValueKind == JsonValueKind.False)
                .Select(row => row.GetProperty("id").GetString()).ToHashSet();
            cases = cases.Where(item => failed.Contains(item.GetProperty("id").GetString())).ToArray();
        }

        var context = dataset.RootElement.GetProperty("evaluation_context");
        var now = DateTimeOffset.Parse(context.GetProperty("now").GetString()!, CultureInfo.InvariantCulture);
        var zone = new UserTimeZone(context.GetProperty("timezone").GetString()!);
        var parser = new AgentReplyParser(new ToolSchemaValidator());
        var requests = new AgentRequestBuilder(new EvaluationTimeProvider(now), new Utf8ContextTokenEstimator(),
            new ContextBudget(settings.Llm.ContextTokenLimit, settings.Llm.MaxOutputTokens, settings.Llm.HistoryMessageLimit), parser);
        var client = new StdioMcpClient(settings.Mcp, new StdioMcpTransportFactory(), new McpErrorMapper(),
            TimeProvider.System, NullLogger.Instance, new McpCatalogReader(new McpCatalogMapper()));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var connected = await client.ConnectAsync(deadline.Token);
        if (connected is not Result<IMcpSession>.Success connection)
        {
            Console.Error.WriteLine("Не удалось подключиться к MCP для обнаружения схем.");
            return 1;
        }

        await using var session = connection.Value;
        var discovered = await session.DiscoverAsync(deadline.Token);
        if (discovered is not Result<McpCatalog>.Success catalog)
        {
            Console.Error.WriteLine("Не удалось обнаружить каталог MCP.");
            return 1;
        }

        var tools = catalog.Value.Tools;
        var rows = new JsonArray();
        var report = new JsonObject
        {
            ["status"] = "running",
            ["started_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["dataset"] = inputPath,
            ["retry_of"] = previousPath,
            ["cases_total"] = cases.Length,
            ["ipv6_disabled"] = Environment.GetEnvironmentVariable("DOTNET_SYSTEM_NET_DISABLEIPV6"),
            ["dataset_sha256"] = Convert.ToHexString(SHA256.HashData(bytes)),
            ["evaluation_context"] = JsonNode.Parse(context.GetRawText()),
            ["model"] = settings.Llm.Model,
            ["decision_strategy"] = settings.Llm.StructuredOutputMode == LlmStructuredOutputMode.ToolCompatibleJson ? "required_native_tools_with_local_final_reply" : "native_auto",
            ["structured_output_mode"] = settings.Llm.StructuredOutputMode.ToString(),
            ["temperature"] = 0.0m,
            ["max_output_tokens"] = settings.Llm.MaxOutputTokens,
            ["context_token_limit"] = settings.Llm.ContextTokenLimit,
            ["timeout_seconds"] = settings.Llm.Timeout.TotalSeconds,
            ["mode"] = "study",
            ["prompt_version"] = new StudyPrompt().Definition.Version,
            ["scope"] = "First routing decision using production prompts, client and discovered MCP schemas. No tool execution or reminder writes.",
            ["tool_catalog"] = JsonSerializer.SerializeToNode(tools),
            ["cases"] = rows,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using var llm = new OpenRouterLlmClient(settings.Llm, OpenRouterLlmClient.CreateHttpClient(settings.Llm), NullLogger.Instance, requests);
        var schema = new ToolSchemaValidator();
        var routed = 0;
        var argumentsCorrect = 0;
        foreach (var item in cases)
        {
            var row = JsonNode.Parse(item.GetRawText())!.AsObject();
            rows.Add(row);
            var original = new LlmRequest(new StudyPrompt().Definition.Messages.Append(
                new LlmMessage(LlmMessageRole.User, item.GetProperty("query").GetString()!)), 0.0m);
            var request = requests.Build(original, tools, zone, []);
            var timer = Stopwatch.StartNew();
            LlmCompletionResult result;
            if (!requests.Fits(request))
            {
                throw new InvalidOperationException("Экспериментальный запрос не помещается в контекст.");
            }

            report["instructions_sha256"] = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                string.Join("\n", request.Messages.Where(message => message.Role == LlmMessageRole.System).Select(message => message.Content)))));
            await SaveAsync(outputPath, report);
            try
            {
                result = await llm.CompleteAsync(request, CancellationToken.None);
            }
            catch (System.Net.Sockets.SocketException error)
            {
                row["transport_error"] = error.SocketErrorCode.ToString();
                report["status"] = "incomplete_transport_failure";
                await SaveAsync(outputPath, report);
                Console.Error.WriteLine($"Сеть недоступна: {error.SocketErrorCode}. Прогон сохранён как незавершённый.");
                return 1;
            }
            row["duration_ms"] = timer.ElapsedMilliseconds;
            if (result is LlmCompletionResult.Failure llmFailure)
            {
                row["transport_error"] = llmFailure.Error.Code.ToString();
                report["status"] = "incomplete_provider_failure";
                await SaveAsync(outputPath, report);
                Console.Error.WriteLine($"{item.GetProperty("id").GetString()}: {llmFailure.Error.Code}. Прогон остановлен; метрики полного набора не рассчитаны.");
                return 1;
            }

            var actual = "invalid_response";
            var actualArguments = JsonSerializer.SerializeToElement(new
            {
            });
            var valid = false;
            if (result is LlmCompletionResult.ToolCalls calls)
            {
                row["tool_calls"] = JsonSerializer.SerializeToNode(calls.Calls);
                if (calls.Calls.Count == 1)
                {
                    var call = calls.Calls[0];
                    actual = call.Name;
                    actualArguments = call.Arguments;
                    var tool = tools.SingleOrDefault(tool => tool.Name == actual);
                    valid = tool is not null && schema.IsValid(tool.InputSchema, actualArguments);
                }
            }
            else if (result is LlmCompletionResult.Success success)
            {
                var reply = parser.Parse(success.Response.Text);
                if (reply is Result<AgentFinalReply>.Success parsed)
                {
                    actual = parsed.Value.RequiresClarification ? "clarify" : "respond";
                    row["text"] = parsed.Value.Text;
                    row["reason"] = parsed.Value.Reason;
                    valid = true;
                }
            }

            var expected = item.GetProperty("expected_action").GetString();
            var actionCorrect = valid && actual == expected;
            var toolExpected = expected is "get_weather" or "get_schedule" or "add_reminder";
            var correctArguments = actionCorrect && ArgumentsMatch(item.GetProperty("expected_arguments"), actualArguments);
            row["actual_action"] = actual;
            row["actual_arguments"] = JsonNode.Parse(actualArguments.GetRawText());
            row["schema_valid"] = valid;
            row["action_correct"] = actionCorrect;
            row["arguments_correct"] = toolExpected ? JsonValue.Create(correctArguments) : null;
            if (actionCorrect)
            {
                routed++;
            }

            if (toolExpected && correctArguments)
            {
                argumentsCorrect++;
            }

            await SaveAsync(outputPath, report);
            Console.WriteLine($"{item.GetProperty("id").GetString()}: {actual}; действие={actionCorrect}, аргументы={correctArguments}");
        }

        report["status"] = "completed";
        report["finished_at"] = DateTimeOffset.UtcNow.ToString("O");
        report["routing_correct"] = routed;
        report["routing_accuracy"] = cases.Length == 0 ? null : JsonValue.Create(routed / (double)cases.Length);
        report["arguments_correct"] = argumentsCorrect;
        var toolCount = cases.Count(item => item.GetProperty("expected_action").GetString() is "get_weather" or "get_schedule" or "add_reminder");
        report["argument_accuracy"] = toolCount == 0 ? null : JsonValue.Create(argumentsCorrect / (double)toolCount);
        report["acceptance_passed"] = previousPath is null ? JsonValue.Create(routed >= 10 && argumentsCorrect >= 8) : null;
        await SaveAsync(outputPath, report);
        Console.WriteLine($"Действия: {routed}/{cases.Length}; аргументы: {argumentsCorrect}/{toolCount}.");
        return 0;
    }

    private bool ArgumentsMatch(JsonElement expected, JsonElement actual)
    {
        if (actual.ValueKind != JsonValueKind.Object || expected.EnumerateObject().Count() != actual.EnumerateObject().Count())
        {
            return false;
        }

        foreach (var property in expected.EnumerateObject())
        {
            if (!actual.TryGetProperty(property.Name, out var value) || value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var text = value.GetString()!;
            if (property.Name == "remind_at")
            {
                if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                    || !time.EqualsExact(DateTimeOffset.Parse(property.Value.GetString()!, CultureInfo.InvariantCulture)))
                {
                    return false;
                }
            }
            else if (!string.Equals(property.Value.GetString()!.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private Task SaveAsync(string path, JsonObject report)
    {
        return File.WriteAllTextAsync(path, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}

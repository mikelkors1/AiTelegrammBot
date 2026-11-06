using System.Diagnostics;
using System.Text.Json;

namespace ItmoBot.Tests.Support;

internal sealed class McpServerProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task<string> standardError;

    public McpServerProcess()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = directory.Parent!.Name;
        while (!File.Exists(Path.Combine(directory.FullName, "ItmoBot.sln")))
        {
            directory = directory.Parent ?? throw new DirectoryNotFoundException("Не найден корень решения.");
        }

        var serverPath = Path.Combine(directory.FullName, "mcp", "bin", configuration, "net10.0", "ItmoBot.Mcp.dll");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = directory.FullName,
        };
        startInfo.ArgumentList.Add(serverPath);
        process = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить MCP-сервер.");
        standardError = process.StandardError.ReadToEndAsync();
    }

    public async Task<JsonDocument> InitializeAsync(CancellationToken cancellationToken)
    {
        await SendAsync(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-11-25",
                capabilities = new
                {
                },
                clientInfo = new
                {
                    name = "ItmoBot.ProtocolTests",
                    version = "1.0.0"
                },
            },
        }, cancellationToken);
        var response = await ReadAsync(cancellationToken);
        await SendAsync(new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        }, cancellationToken);
        return response;
    }

    public async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message);
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    public async Task<JsonDocument> ReadAsync(CancellationToken cancellationToken)
    {
        var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
        if (line is null)
        {
            throw new EndOfStreamException("MCP-сервер закрыл stdout до ответа.");
        }

        return JsonDocument.Parse(line);
    }

    public async Task<(int ExitCode, string RemainingOutput, string Diagnostics)> CloseInputAsync(CancellationToken cancellationToken)
    {
        process.StandardInput.Close();
        var remainingOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await remainingOutput, await standardError.WaitAsync(cancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }

            await standardError;
        }
        finally
        {
            process.Dispose();
        }
    }
}

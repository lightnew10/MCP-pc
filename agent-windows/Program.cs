using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace McpPc.Agent;

internal static class Program
{
    private const string PipeName = "mcp-pc-agent";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task Main()
    {
        WindowsDesktopService.EnablePerMonitorDpiAwareness();
        Console.Error.WriteLine($"MCP-PC Windows agent listening on \\.\\pipe\\{PipeName}");

        while (true)
        {
            await using var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await pipe.WaitForConnectionAsync();

            using var reader = new StreamReader(
                pipe,
                new UTF8Encoding(false),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);

            await using var writer = new StreamWriter(
                pipe,
                new UTF8Encoding(false),
                bufferSize: 4096,
                leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };

            AgentRequest? request = null;

            try
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                request = JsonSerializer.Deserialize<AgentRequest>(line, JsonOptions)
                    ?? throw new InvalidOperationException("Invalid request payload.");

                var result = Dispatch(request);
                var response = new AgentResponse(request.Id, true, result, null);
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
            }
            catch (Exception exception)
            {
                var response = new AgentResponse(
                    request?.Id ?? string.Empty,
                    false,
                    null,
                    exception.Message);

                await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
            }
        }
    }

    private static object? Dispatch(AgentRequest request)
    {
        return request.Method switch
        {
            "windows.list" => WindowsDesktopService.ListWindows(),
            "windows.active" => WindowsDesktopService.GetActiveWindow(),
            "screen.capture" => WindowsDesktopService.CaptureScreen(),
            "screen.captureWindow" => WindowsDesktopService.CaptureWindow(GetRequiredString(request.Params, "windowId")),
            _ => throw new InvalidOperationException($"Unknown method: {request.Method}")
        };
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidOperationException($"Missing or invalid parameter: {propertyName}");
        }

        return value.GetString()!;
    }
}

internal sealed record AgentRequest(string Id, string Method, JsonElement Params);
internal sealed record AgentResponse(string Id, bool Ok, object? Result, string? Error);

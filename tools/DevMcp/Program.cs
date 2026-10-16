// Dev MCP server for testing the Item Pack: speaks MCP (JSON-RPC over stdio) to Claude Code and forwards game
// tools to the plugin's DevTools-only server (src/DevMcp/DevServer.cs) on 127.0.0.1. A few tools (status, log,
// launch) work without the game running.
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

CleanOldCopies();
var server = new McpServer(
    port: int.TryParse(Environment.GetEnvironmentVariable("SBG_DEV_PORT"), out int p) ? p : 47150,
    gameDir: Environment.GetEnvironmentVariable("SBG_GAME_DIR") ?? @"G:\Games\SteamLibrary\steamapps\common\Super Battle Golf");
await server.RunAsync();

// run.cmd starts each session from its own copy under %TEMP%\sbg-dev-mcp; remove the ones no running copy uses.
static void CleanOldCopies()
{
    string root = Path.Combine(Path.GetTempPath(), "sbg-dev-mcp");
    string own = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
    if (!Directory.Exists(root))
    {
        return;
    }
    foreach (string dir in Directory.GetDirectories(root))
    {
        if (string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), own, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }
        try
        {
            Directory.Delete(dir, recursive: true); // fails while another session's copy is running, which is fine
        }
        catch (Exception)
        {
        }
    }
}

internal sealed class McpServer(int port, string gameDir)
{
    private const string SteamAppId = "4069520";
    private long logOffset = -1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public async Task RunAsync()
    {
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        string? line;
        while ((line = await stdin.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            JsonObject? response;
            try
            {
                response = await HandleAsync(JsonNode.Parse(line)!.AsObject());
            }
            catch (Exception e)
            {
                response = Error(null, -32700, "Parse error: " + e.Message);
            }
            if (response != null)
            {
                await stdout.WriteLineAsync(response.ToJsonString(JsonOptions));
            }
        }
    }

    private async Task<JsonObject?> HandleAsync(JsonObject message)
    {
        JsonNode? id = message["id"]?.DeepClone();
        string method = (string?)message["method"] ?? "";
        if (id == null)
        {
            return null; // a notification (initialized, cancelled...)
        }
        switch (method)
        {
            case "initialize":
                return Result(id, new JsonObject
                {
                    ["protocolVersion"] = (string?)message["params"]?["protocolVersion"] ?? "2025-06-18",
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "sbg-dev", ["version"] = "1.0.0" },
                    ["instructions"] =
                        "Drives a running Super Battle Golf with the Item Pack's DevTools build (dev testing only). " +
                        "Start with game_status. Typical loop: start_game -> state -> give_item/spawn_item -> teleport/camera -> " +
                        "use_item/interact/input -> screenshot -> read_log. Use sequence for anything timing-sensitive. " +
                        "Never launch the game unless the user said to.",
                });
            case "ping":
                return Result(id, new JsonObject());
            case "tools/list":
                return Result(id, new JsonObject { ["tools"] = Tools.List() });
            case "tools/call":
                string name = (string?)message["params"]?["name"] ?? "";
                JsonObject args = message["params"]?["arguments"] as JsonObject ?? new JsonObject();
                return Result(id, await CallAsync(name, args));
            default:
                return Error(id, -32601, "Method not found: " + method);
        }
    }

    private async Task<JsonObject> CallAsync(string name, JsonObject args)
    {
        try
        {
            return name switch
            {
                "game_status" => Text(await StatusAsync()),
                "read_log" => Text(ReadLog(args)),
                "launch_game" => Text(await LaunchAsync(args)),
                _ => await ForwardAsync(name, args),
            };
        }
        catch (Exception e)
        {
            return Text(e.Message, isError: true);
        }
    }

    // ---- forwarding to the game ----

    private async Task<JsonObject> ForwardAsync(string tool, JsonObject args)
    {
        if (!Tools.IsGameTool(tool))
        {
            return Text($"Unknown tool '{tool}'.", isError: true);
        }
        double timeout = tool == "sequence" || tool == "start_game" ? 300 : 120;
        var request = new JsonObject { ["tool"] = tool, ["args"] = args.DeepClone(), ["timeout_s"] = timeout };
        JsonObject reply;
        try
        {
            reply = await SendAsync(request, TimeSpan.FromSeconds(timeout + 10));
        }
        catch (Exception e) when (e is SocketException || e is TimeoutException || e is IOException)
        {
            return Text($"Can't reach the game on 127.0.0.1:{port} ({e.Message}). Is it running with a DevTools build of the Item Pack? " +
                        "Check game_status.", isError: true);
        }
        var content = reply["content"] as JsonArray ?? new JsonArray();
        bool ok = (bool?)reply["ok"] ?? false;
        if (!ok)
        {
            content.Add(new JsonObject { ["type"] = "text", ["text"] = "ERROR: " + ((string?)reply["error"] ?? "unknown") });
        }
        if (content.Count == 0)
        {
            content.Add(new JsonObject { ["type"] = "text", ["text"] = "Done." });
        }
        return new JsonObject { ["content"] = content.DeepClone(), ["isError"] = !ok };
    }

    private async Task<JsonObject> SendAsync(JsonObject request, TimeSpan timeout)
    {
        using var client = new TcpClient();
        using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await client.ConnectAsync("127.0.0.1", port, connectTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("connect timed out");
        }
        NetworkStream stream = client.GetStream();
        var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        var reader = new StreamReader(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync(request.ToJsonString(JsonOptions));
        using var readTimeout = new CancellationTokenSource(timeout);
        string? line = await reader.ReadLineAsync(readTimeout.Token) ?? throw new IOException("the game closed the connection");
        return JsonNode.Parse(line)!.AsObject();
    }

    // ---- tools that work without the game ----

    private async Task<string> StatusAsync()
    {
        var lines = new List<string>();
        Process[] processes = Process.GetProcessesByName("Super Battle Golf");
        lines.Add(processes.Length > 0 ? $"Game process running (pid {processes[0].Id})." : "Game process not running.");
        string doorstop = Path.Combine(gameDir, "doorstop_config.ini");
        if (File.Exists(doorstop))
        {
            string? enabled = File.ReadLines(doorstop).FirstOrDefault(l => l.TrimStart().StartsWith("enabled", StringComparison.OrdinalIgnoreCase));
            lines.Add($"Mods (doorstop_config.ini): {enabled?.Trim() ?? "unknown"}.");
        }
        try
        {
            JsonObject reply = await SendAsync(new JsonObject { ["tool"] = "state", ["args"] = new JsonObject(), ["timeout_s"] = 10 }, TimeSpan.FromSeconds(15));
            lines.Add($"Dev server on 127.0.0.1:{port} is answering. State:");
            foreach (JsonNode? block in reply["content"] as JsonArray ?? new JsonArray())
            {
                lines.Add((string?)block?["text"] ?? "");
            }
        }
        catch (Exception e)
        {
            lines.Add($"Dev server on 127.0.0.1:{port} not answering ({e.Message}). It needs a DevTools build (-p:DevTools=true) " +
                      "with [Debug] DevMcpEnabled = true, and the game past its splash screens.");
        }
        return string.Join("\n", lines);
    }

    private string ReadLog(JsonObject args)
    {
        string path = Path.Combine(gameDir, "BepInEx", "LogOutput.log");
        if (!File.Exists(path))
        {
            return "No log at " + path;
        }
        int maxLines = (int?)args["lines"] ?? 60;
        string? contains = (string?)args["contains"];
        bool newOnly = (bool?)args["new_only"] ?? false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long start = 0;
        if (newOnly && logOffset >= 0 && logOffset <= stream.Length)
        {
            start = logOffset;
        }
        stream.Seek(start, SeekOrigin.Begin);
        string text = new StreamReader(stream, Encoding.UTF8).ReadToEnd();
        logOffset = stream.Length;
        IEnumerable<string> lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0);
        if (!string.IsNullOrEmpty(contains))
        {
            lines = lines.Where(l => l.Contains(contains, StringComparison.OrdinalIgnoreCase));
        }
        List<string> list = lines.ToList();
        List<string> tail = list.Skip(Math.Max(0, list.Count - maxLines)).ToList();
        string header = $"{path}: {tail.Count} of {list.Count} matching line(s){(newOnly && start > 0 ? " since the last read" : "")}.";
        return header + "\n" + string.Join("\n", tail);
    }

    private async Task<string> LaunchAsync(JsonObject args)
    {
        if (Process.GetProcessesByName("Super Battle Golf").Length > 0)
        {
            return "The game is already running.";
        }
        Process.Start(new ProcessStartInfo("steam://rungameid/" + SteamAppId) { UseShellExecute = true });
        int waitSeconds = (int?)args["wait_seconds"] ?? 90;
        var until = DateTime.UtcNow.AddSeconds(waitSeconds);
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(2000);
            try
            {
                await SendAsync(new JsonObject { ["tool"] = "wait", ["args"] = new JsonObject { ["frames"] = 1 }, ["timeout_s"] = 5 }, TimeSpan.FromSeconds(8));
                return "Launched; the dev server is answering. Use start_game to enter the Driving Range.";
            }
            catch (Exception)
            {
                // not up yet
            }
        }
        return $"Launched via Steam, but the dev server didn't answer within {waitSeconds} s. Check game_status / read_log.";
    }

    // ---- JSON-RPC helpers ----

    private static JsonObject Text(string text, bool isError = false) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError,
    };

    private static JsonObject Result(JsonNode id, JsonObject result) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };
}

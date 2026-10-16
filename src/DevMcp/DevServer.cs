#if DEVTOOLS
// Dev-only remote control for testing: only compiled into builds made with -p:DevTools=true (never into releases).
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ItemPack.DevMcp;

/// <summary>
/// A localhost TCP server that lets tools/DevMcp (an MCP server for Claude Code) drive the game: one JSON request
/// per line, {"tool": "...", "args": {...}}, answered with one JSON line {"ok": bool, "content": [...], "error": "..."}.
/// Commands run on Unity's main thread as coroutines (see <see cref="DevCommands"/>).
/// </summary>
internal static class DevServer
{
    private sealed class Pending
    {
        public string Tool;
        public JObject Args;
        public readonly DevReply Reply = new();
        public readonly ManualResetEventSlim Done = new(false);
    }

    private static readonly ConcurrentQueue<Pending> queue = new();
    private static TcpListener listener;
    private static Thread thread;

    public static void Start(int port)
    {
        // Keep running (and reading simulated input) while another window has focus.
        Application.runInBackground = true;
        UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Dev MCP server couldn't listen on 127.0.0.1:{port}: {e.Message}");
            return;
        }
        thread = new Thread(AcceptLoop) { IsBackground = true, Name = "ItemPack DevMcp" };
        thread.Start();
        Plugin.Log.LogInfo($"Dev MCP server listening on 127.0.0.1:{port}.");
    }

    /// <summary>Starts queued requests. Called every frame from the runner.</summary>
    public static void Update()
    {
        while (queue.TryDequeue(out Pending pending))
        {
            Plugin.Runner.StartCoroutine(Run(pending));
        }
    }

    private static IEnumerator Run(Pending pending)
    {
        IEnumerator routine = Step(pending.Tool, pending.Args, pending.Reply);
        while (routine.MoveNext())
        {
            yield return routine.Current;
        }
        pending.Done.Set();
    }

    /// <summary>
    /// Runs a command, stepping it by hand so an exception in any step still answers the request.
    /// Also used by sequences to run their steps.
    /// </summary>
    public static IEnumerator Step(string tool, JObject args, DevReply reply)
    {
        IEnumerator routine;
        try
        {
            routine = DevCommands.Begin(tool, args, reply);
        }
        catch (Exception e)
        {
            reply.Fail($"{e.GetType().Name}: {e.Message}");
            yield break;
        }
        while (true)
        {
            object current;
            try
            {
                if (!routine.MoveNext())
                {
                    yield break;
                }
                current = routine.Current;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Dev MCP {tool} failed: {e}");
                reply.Fail($"{e.GetType().Name}: {e.Message}");
                yield break;
            }
            yield return current;
        }
    }

    private static void AcceptLoop()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                return;
            }
            new Thread(() => Serve(client)) { IsBackground = true }.Start();
        }
    }

    private static void Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                var reader = new StreamReader(stream, new UTF8Encoding(false));
                var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Trim().Length == 0)
                    {
                        continue;
                    }
                    writer.WriteLine(Handle(line).ToString(Formatting.None));
                }
            }
            catch (Exception)
            {
                // client went away
            }
        }
    }

    private static JObject Handle(string line)
    {
        JObject request;
        try
        {
            request = JObject.Parse(line);
        }
        catch (Exception e)
        {
            return new JObject { ["ok"] = false, ["error"] = "Bad JSON: " + e.Message };
        }
        var pending = new Pending
        {
            Tool = (string)request["tool"] ?? "",
            Args = request["args"] as JObject ?? new JObject(),
        };
        double timeout = (double?)request["timeout_s"] ?? 120.0;
        queue.Enqueue(pending);
        if (!pending.Done.Wait(TimeSpan.FromSeconds(timeout)))
        {
            return new JObject { ["ok"] = false, ["error"] = $"Timed out after {timeout} s (is the game frozen or loading?)." };
        }
        return pending.Reply.ToJson();
    }
}

/// <summary>What a command answers: text and image blocks (MCP content), or an error.</summary>
internal sealed class DevReply
{
    private readonly List<JObject> content = new();
    public string Error { get; private set; }

    public void Text(string text) => content.Add(new JObject { ["type"] = "text", ["text"] = text });

    public void Json(object value) => Text(JsonConvert.SerializeObject(value, Formatting.Indented, DevJson.Settings));

    public void Image(byte[] bytes, string mimeType) =>
        content.Add(new JObject { ["type"] = "image", ["data"] = Convert.ToBase64String(bytes), ["mimeType"] = mimeType });

    public void Fail(string error) => Error = Error == null ? error : Error + "\n" + error;

    /// <summary>Copies this reply's blocks into another (a sequence's), text prefixed with the step label.</summary>
    public void AppendTo(DevReply other, string prefix)
    {
        foreach (JObject block in content)
        {
            if ((string)block["type"] == "text")
            {
                other.Text(prefix + (string)block["text"]);
            }
            else
            {
                other.content.Add(block);
            }
        }
        if (Error != null)
        {
            other.Text(prefix + "ERROR: " + Error);
        }
    }

    public JObject ToJson()
    {
        var json = new JObject { ["ok"] = Error == null, ["content"] = new JArray(content) };
        if (Error != null)
        {
            json["error"] = Error;
        }
        return json;
    }
}

internal static class DevJson
{
    public static readonly JsonSerializerSettings Settings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
    };

    public static float[] V(Vector3 v) => new[] { R(v.x), R(v.y), R(v.z) };

    public static float R(float f) => Mathf.Round(f * 100f) / 100f;

    /// <summary>Reads [x, y, z] or {"x":..,"y":..,"z":..}; null if the token is missing or malformed.</summary>
    public static Vector3? ReadVector(JToken token)
    {
        if (token is JArray array && array.Count == 3)
        {
            return new Vector3((float)array[0], (float)array[1], (float)array[2]);
        }
        if (token is JObject obj && obj["x"] != null)
        {
            return new Vector3((float)obj["x"], (float?)obj["y"] ?? 0f, (float?)obj["z"] ?? 0f);
        }
        return null;
    }
}
#endif

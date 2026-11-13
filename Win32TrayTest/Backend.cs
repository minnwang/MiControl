using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Win32TrayTest;

internal sealed record Status(bool Ac, bool Charging, int Battery, int Perf, int ChargePct, int Health, int Watts)
{
    public string PerfName => Perf switch
    {
        0x02 => "静音",
        0x09 => "智能",
        0x03 => "性能",
        0x04 => "全速",
        _ => "0x" + Perf.ToString("X2")
    };
}

/// <summary>Talks to the running MiControl service exactly like the WinForms tray does.</summary>
internal static class Backend
{
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MiControl", "state.json");

    private static (string Token, int Port) Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(StatePath));
        var token = doc.RootElement.GetProperty("Token").GetString() ?? "";
        int port = doc.RootElement.TryGetProperty("Port", out var p) ? p.GetInt32() : 50123;
        return (token, port);
    }

    private static TcpClient? _client;
    private static StreamWriter? _writer;
    private static StreamReader? _reader;
    private static readonly object _gate = new();

    private static void Open(int port)
    {
        _client = new TcpClient { ReceiveTimeout = 4000 };
        _client.Connect("127.0.0.1", port);
        var stream = _client.GetStream();
        _writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        _reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
    }

    private static void Close()
    {
        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        _writer = null; _reader = null; _client = null;
    }

    /// <summary>Reuses one connection for all request/response traffic.</summary>
    private static JsonObject Send(JsonObject request)
    {
        var (token, port) = Load();
        request["token"] = token;

        lock (_gate)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if (_client is null) Open(port);
                    _writer!.WriteLine(request.ToJsonString());
                    var line = _reader!.ReadLine();
                    if (line is null) throw new IOException("connection closed");
                    return JsonNode.Parse(line)!.AsObject();
                }
                catch (Exception) when (attempt == 0)
                {
                    Close();
                }
            }
        }

        throw new IOException("ipc failed");
    }

    /// <summary>Keeps a connection open and invokes the callback for every pushed action.</summary>
    public static void StartPushListener(Action<string> onAction)
    {
        Task.Run(() =>
        {
            while (true)
            {
                try
                {
                    var (token, port) = Load();
                    using var client = new TcpClient { ReceiveTimeout = 0 };
                    client.Connect("127.0.0.1", port);
                    client.NoDelay = true;
                    using var stream = client.GetStream();
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
                    using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);

                    writer.WriteLine(new JsonObject { ["cmd"] = "status", ["token"] = token }.ToJsonString());

                    string? line;
                    while ((line = reader.ReadLine()) is not null)
                    {
                        var obj = JsonNode.Parse(line)!.AsObject();
                        if (obj["push"]?.GetValue<string>() != "actions") continue;
                        if (obj["actions"] is not JsonArray arr) continue;
                        foreach (var n in arr)
                        {
                            var a = n?.GetValue<string>();
                            if (!string.IsNullOrEmpty(a)) onAction(a);
                        }
                    }
                }
                catch { }
                Thread.Sleep(1000);
            }
        });
    }

    public static bool SetHotkey(string code, string action)
    {
        try
        {
            var r = Send(new JsonObject { ["cmd"] = "setHotkey", ["key"] = code, ["action"] = action });
            return r["ok"]?.GetValue<bool>() ?? false;
        }
        catch { return false; }
    }

    public static Dictionary<string, string> GetHotkeys()
    {
        var result = new Dictionary<string, string>();
        try
        {
            var r = Send(new JsonObject { ["cmd"] = "getConfig" });
            if (r["hotkeys"] is JsonObject o)
                foreach (var kv in o)
                    result[kv.Key] = kv.Value?.GetValue<string>() ?? "none";
        }
        catch { }
        return result;
    }

    public static Status GetStatus()
    {
        try
        {
            var r = Send(new JsonObject { ["cmd"] = "status" });
            return new Status(
                r["ac"]?.GetValue<bool>() ?? false,
                r["charging"]?.GetValue<bool>() ?? false,
                r["battery"]?.GetValue<int>() ?? -1,
                r["perf"]?.GetValue<int>() ?? -1,
                r["chargePct"]?.GetValue<int>() ?? -1,
                r["health"]?.GetValue<int>() ?? -1,
                r["watts"]?.GetValue<int>() ?? -1);
        }
        catch
        {
            return new Status(false, false, -1, -1, -1, -1, -1);
        }
    }

    public static bool GetAutoFull()
    {
        try
        {
            var r = Send(new JsonObject { ["cmd"] = "getConfig" });
            return r["autoFull"]?.GetValue<bool>() ?? FetchAutoFullFromStatus();
        }
        catch { return false; }
    }

    private static bool FetchAutoFullFromStatus()
    {
        try
        {
            var r = Send(new JsonObject { ["cmd"] = "status" });
            return r["autoFull"]?.GetValue<bool>() ?? false;
        }
        catch { return false; }
    }

    public static bool SetAutoFull(bool enabled)
    {
        try
        {
            var r = Send(new JsonObject { ["cmd"] = "setAutoFull", ["enabled"] = enabled });
            return r["ok"]?.GetValue<bool>() ?? false;
        }
        catch { return false; }
    }

    public static bool SetCharge(int pct)
    {
        try
        {
            var r = Send(new JsonObject { ["cmd"] = "setCharge", ["pct"] = pct });
            return r["ok"]?.GetValue<bool>() ?? false;
        }
        catch { return false; }
    }

    public static bool SetPerf(int mode)
    {
        try
        {
            var r = Send(new JsonObject { ["cmd"] = "setPerf", ["mode"] = mode });
            return r["ok"]?.GetValue<bool>() ?? false;
        }
        catch { return false; }
    }
}




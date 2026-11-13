using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MiControl.Tray;

public sealed class Status
{
    public bool Ok { get; init; }
    public bool Ac { get; init; }
    public bool Charging { get; init; }
    public int Battery { get; init; }
    public int Perf { get; init; }
    public int ChargeCode { get; init; }
    public int ChargePct { get; init; }
    public int Health { get; init; }
    public int Watts { get; init; }
    public bool AutoFull { get; init; }
    public bool ChargeEnabled { get; init; }
    public string? Message { get; init; }
}

/// <summary>Talks to MiControlService over loopback with a shared token.</summary>
public sealed class IpcClient
{
    private readonly string _token;
    private readonly int _port;

    public IpcClient()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "MiControl", "state.json");

        var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        _token = node["Token"]?.GetValue<string>() ?? "";
        _port = node["Port"]?.GetValue<int>() ?? 50123;
    }

    private TcpClient? _client;
    private StreamWriter? _writer;
    private StreamReader? _reader;
    private readonly object _gate = new();

    private readonly System.Collections.Concurrent.BlockingCollection<JsonObject> _responses = new();

    /// <summary>Raised on the reader thread when the service pushes hotkey actions.</summary>
    public event Action<string[]>? ActionsPushed;
    public event Action<JsonObject>? StatusPushed;

    private void Open()
    {
        _client = new TcpClient { ReceiveTimeout = 4000 };
        _client.Connect("127.0.0.1", _port);
        var stream = _client.GetStream();
        _writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        _reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
        var reader = _reader;
        var self = this;
        Task.Run(() =>
        {
            try
            {
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    var obj = JsonNode.Parse(line)!.AsObject();
                    if (obj["push"]?.GetValue<string>() == "status")
                    {
                        self.StatusPushed?.Invoke(obj);
                    }
                    else if (obj["push"] is not null)
                    {
                        var arr = obj["actions"] as JsonArray;
                        var list = arr?.Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToArray()
                                   ?? Array.Empty<string>();
                        self.ActionsPushed?.Invoke(list);
                    }
                    else
                    {
                        self._responses.Add(obj);
                    }
                }
            }
            catch { }
            finally
            {
                self.Close();
                self.ReconnectLoop();
            }
        });
    }

    // Keep a live connection at all times: otherwise the service has nobody to push to.
    private void ReconnectLoop()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(1000);
                try
                {
                    lock (_gate)
                    {
                        if (_client is not null) return;
                        Open();
                    }
                    return;
                }
                catch { }
            }
        });
    }

    private void Close()
    {
        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        _writer = null; _reader = null; _client = null;
    }

    // One long-lived connection instead of a new TCP connection per request.
    private JsonObject Send(JsonObject request, int timeoutMs = 4000)
    {
        request["token"] = _token;

        lock (_gate)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if (_client is null) Open();
                    _writer!.WriteLine(request.ToJsonString());
                    if (_responses.TryTake(out var resp, timeoutMs)) return resp;
                    throw new IOException("ipc timeout");
                }
                catch (Exception) when (attempt == 0)
                {
                    Close();
                }
            }
        }

        throw new IOException("ipc failed");
    }

    public Status GetStatus() => Parse(Send(new JsonObject { ["cmd"] = "status" }));

    public static Status Parse(JsonObject r)
    {
        return new Status
        {
            Ok = r["ok"]?.GetValue<bool>() ?? false,
            Ac = r["ac"]?.GetValue<bool>() ?? false,
            Charging = r["charging"]?.GetValue<bool>() ?? false,
            Battery = r["battery"]?.GetValue<int>() ?? -1,
            Perf = r["perf"]?.GetValue<int>() ?? -1,
            ChargeCode = r["chargeCode"]?.GetValue<int>() ?? -1,
            ChargePct = r["chargePct"]?.GetValue<int>() ?? -1,
            Health = r["health"]?.GetValue<int>() ?? -1,
            Watts = r["watts"]?.GetValue<int>() ?? -1,
            AutoFull = r["autoFull"]?.GetValue<bool>() ?? false,
            ChargeEnabled = r["chargeEnabled"]?.GetValue<bool>() ?? false
        };
    }

    public (bool ok, string message) SetPerf(int mode)
    {
        var r = Send(new JsonObject { ["cmd"] = "setPerf", ["mode"] = mode });
        return (r["ok"]?.GetValue<bool>() ?? false, r["message"]?.GetValue<string>() ?? "");
    }

    public (bool ok, string message) SetCharge(int pct)
    {
        var r = Send(new JsonObject { ["cmd"] = "setCharge", ["pct"] = pct });
        return (r["ok"]?.GetValue<bool>() ?? false, r["message"]?.GetValue<string>() ?? "");
    }

    public (bool ok, string message) SetAutoFull(bool enabled)
    {
        var r = Send(new JsonObject { ["cmd"] = "setAutoFull", ["enabled"] = enabled });
        return (r["ok"]?.GetValue<bool>() ?? false, r["message"]?.GetValue<string>() ?? "");
    }

    /// <summary>Actions raised by firmware hotkeys inside the service (session 0).</summary>
    public string[] PollActions()
    {
        var r = Send(new JsonObject { ["cmd"] = "pollActions" }, 2000);
        if (r["actions"] is not JsonArray arr) return Array.Empty<string>();
        return arr.Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToArray();
    }

    public Dictionary<string, string> GetHotkeys()
    {
        var r = Send(new JsonObject { ["cmd"] = "getConfig" });
        var result = new Dictionary<string, string>();
        if (r["hotkeys"] is not JsonObject obj) return result;
        foreach (var kv in obj) result[kv.Key] = kv.Value?.GetValue<string>() ?? "none";
        return result;
    }

    public bool GetAutoStart()
    {
        var r = Send(new JsonObject { ["cmd"] = "getConfig" });
        return r["autoStart"]?.GetValue<bool>() ?? true;
    }

    public bool SetAutoStart(bool enabled)
    {
        var r = Send(new JsonObject { ["cmd"] = "setAutoStart", ["enabled"] = enabled });
        return r["ok"]?.GetValue<bool>() ?? false;
    }

    public bool SetHotkey(string key, string action)
    {
        var r = Send(new JsonObject { ["cmd"] = "setHotkey", ["key"] = key, ["action"] = action });
        return r["ok"]?.GetValue<bool>() ?? false;
    }
}








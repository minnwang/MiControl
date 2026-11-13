using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiControl.Service;

public sealed class AppState
{
    public ChargeLevel ChargeLevel { get; set; } = ChargeLevel.Percent80;
    public bool ChargeProtectEnabled { get; set; } = true;
    public PerfMode PerfMode { get; set; } = PerfMode.Smart;
    public bool AutoFullSpeedOnAc { get; set; } = false;
    public bool AutoStart { get; set; } = true;
    public int PreviousPerfMode { get; set; } = 0x09;
    public string Token { get; set; } = "";
    public int Port { get; set; } = 50123;

    /// <summary>
    /// Vendor hotkey mapping: firmware key code (decimal, as hex string) -> action.
    /// Actions: none | settings | winp | mic | app:&lt;full path&gt;
    /// </summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new()
    {
        ["33"] = "mic",   // Fn+F4  microphone mute
        ["27"] = "ms:",   // Fn+F9  Windows Settings
        ["1"] = "winp",   // Fn+F8  projection
        ["35"] = "none",  // Fn+F7  xiaoai
        ["37"] = "none"   // AI key
    };
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppState))]
internal partial class StateJsonContext : JsonSerializerContext { }

public sealed class StateStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MiControl");

    private static readonly string FilePath = Path.Combine(Dir, "state.json");

    private readonly object _gate = new();

    public AppState State { get; private set; }

    public StateStore()
    {
        Directory.CreateDirectory(Dir);
        State = Load();
        if (string.IsNullOrEmpty(State.Token))
        {
            State.Token = Guid.NewGuid().ToString("N");
            Save();
        }
    }

    private static AppState Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize(json, StateJsonContext.Default.AppState);
                if (s is not null) return s;
            }
        }
        catch { /* corrupted state -> defaults */ }
        return new AppState();
    }

    public void Save()
    {
        lock (_gate)
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(State, StateJsonContext.Default.AppState));
            }
            catch { }
        }
    }

    public void Update(Action<AppState> mutate)
    {
        lock (_gate)
        {
            mutate(State);
            Save();
        }
    }
}



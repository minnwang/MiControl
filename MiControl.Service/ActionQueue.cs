using System.Collections.Concurrent;

namespace MiControl.Service;

/// <summary>
/// Pending UI actions raised by firmware hotkeys. The tray app cannot poll for these
/// (400 ms polling cost ~0.4% CPU), so the service pushes them over the existing
/// connection instead.
/// </summary>
public static class ActionQueue
{
    private static readonly ConcurrentQueue<string> Queue = new();

    /// <summary>Raised after an action is queued, so connected tray clients can be notified.</summary>
    public static event Action? Changed;

    public static void Push(string action)
    {
        Queue.Enqueue(action);
        Changed?.Invoke();
    }

    public static string[] Drain()
    {
        var list = new List<string>();
        while (Queue.TryDequeue(out var a)) list.Add(a);
        return list.ToArray();
    }
}

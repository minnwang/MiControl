using System.Globalization;

namespace MiControl.Tray;

/// <summary>
/// Turns a key spec such as "0x41+0x11+0x10" into a readable label like "Ctrl+Shift+A".
/// The spec format is: main key first, then modifiers (matches ActionRunner.SendKeySpec).
/// </summary>
internal static class KeyNames
{
    private static readonly string[] ModifierOrder = { "Ctrl", "Shift", "Alt", "Win" };

    public static string Describe(string spec) => Describe(ParseOrdered(spec));

    private static string Describe(List<byte> keys)
    {
        if (keys.Count == 0) return "";

        var main = Name(keys[0]);

        var mods = new List<string>();
        foreach (var vk in keys.Skip(1))
        {
            var m = ModifierName(vk);
            if (m is not null && !mods.Contains(m)) mods.Add(m);
        }

        if (mods.Count == 0) return main;

        var ordered = ModifierOrder.Where(mods.Contains).ToList();
        return string.Join("+", ordered) + "+" + main;
    }

    /// <summary>
    /// Parses a spec into VK codes and normalises the order to "main key first".
    /// Legacy specs recorded as "modifiers first" (0x11+0x10+0x41) are reordered,
    /// so both storage formats behave the same.
    /// </summary>
    public static List<byte> ParseOrdered(string spec)
    {
        var keys = new List<byte>();
        foreach (var t in spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                byte.TryParse(t.AsSpan(2), NumberStyles.HexNumber, null, out var vk))
            {
                keys.Add(vk);
            }
        }

        if (keys.Count > 1 && IsModifier(keys[0]))
        {
            int idx = keys.FindIndex(k => !IsModifier(k));
            if (idx > 0)
            {
                var main = keys[idx];
                keys.RemoveAt(idx);
                keys.Insert(0, main);
            }
        }

        return keys;
    }

    public static bool IsModifier(int vk) => ModifierName(vk) is not null;

    private static string? ModifierName(int vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => "Shift",
        0x11 or 0xA2 or 0xA3 => "Ctrl",
        0x12 or 0xA4 or 0xA5 => "Alt",
        0x5B or 0x5C => "Win",
        _ => null
    };

    private static string Name(int vk)
    {
        // letters and digits
        if (vk is >= 0x30 and <= 0x39) return ((char)vk).ToString();
        if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();

        // function keys
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);

        // numpad digits
        if (vk is >= 0x60 and <= 0x69) return "Num" + (vk - 0x60);

        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x13 => "Pause",
            0x14 => "CapsLock",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrtSc",
            0x2D => "Insert",
            0x2E => "Delete",
            0x5D => "Menu",
            0x6A => "Num*",
            0x6B => "Num+",
            0x6D => "Num-",
            0x6E => "Num.",
            0x6F => "Num/",
            0x90 => "NumLock",
            0x91 => "ScrollLock",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => "VK" + vk.ToString("X2")
        };
    }
}

using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed record WorldMapDialogueWindow(int WindowId, ushort State, uint TextPointer, string Text)
{
    public bool IsWaitingForInput => State is 4 or 6 or 11 or 13 or 14;
}

public sealed record WorldMapDialogueSnapshot(bool PlayerHasControl, IReadOnlyList<WorldMapDialogueWindow> Windows)
{
    public bool IsBlockingMovement => !PlayerHasControl || Windows.Count != 0;
}

/// <summary>
/// FUN_00769836 gives each world window a rendered buffer at E3B220, and
/// FUN_00769C02 appends the visible characters. CFF5E4 is the window state;
/// DE6B5C is the control flag maintained by FUN_0074D438. These guest addresses
/// are shared by both runtimes. No text is taken from an unshown script.
/// </summary>
public sealed class WorldMapDialogueReader(ILegacyAddressSpace memory)
{
    public const uint ControlAddress = 0x00DE6B5C;
    public const uint WindowStateAddress = 0x00CFF5E4;
    public const uint TextPointerAddress = 0x00E3B210;
    public const uint TextBufferAddress = 0x00E3B220;

    public bool TryRead(out WorldMapDialogueSnapshot snapshot)
    {
        snapshot = new(false, []);
        if (!TryCapture(out var control, out var before)) return false;
        var windows = new List<WorldMapDialogueWindow>();
        var encoded = new Dictionary<int, byte[]>();
        for (var i = 0; i < before.Length; i++)
        {
            if (before[i].State == 0) continue;
            if (before[i].Owner == FieldMessageReader.FreeWindowState ||
                !LegacyFf7TextReader.TryReadTerminated(memory, TextBufferAddress + (uint)(i * 0x100),
                    0x100, out var bytes, out var text)) return false;
            encoded[i] = bytes;
            windows.Add(new(i, before[i].State, before[i].Pointer,
                Ff7EncodedTextDecoder.NormalizeWhitespace(text)));
        }
        if (!TryCapture(out var middleControl, out var middle) || middleControl != control ||
            !before.SequenceEqual(middle)) return false;
        foreach (var (i, bytes) in encoded)
        {
            if (!LegacyFf7TextReader.TryReadTerminated(memory, TextBufferAddress + (uint)(i * 0x100),
                    0x100, out var second, out _) || !bytes.AsSpan().SequenceEqual(second)) return false;
        }
        if (!TryCapture(out var afterControl, out var after) || afterControl != control ||
            !before.SequenceEqual(after)) return false;
        snapshot = new(control, windows);
        return true;
    }

    private bool TryCapture(out bool control, out WindowHeader[] windows)
    {
        control = false;
        windows = new WindowHeader[4];
        if (!memory.TryReadByte((uint)WorldMapStateReader.AddressCurrentModule, out var module) ||
            module != WorldMapStateReader.WorldModule ||
            !memory.TryReadInt32(ControlAddress, out var nativeControl) || nativeControl is not (0 or 1))
            return false;
        control = nativeControl != 0;
        for (var i = 0; i < windows.Length; i++)
        {
            if (!memory.TryReadUInt16(WindowStateAddress + (uint)(i * 0x30), out var state) || state > 14 ||
                !memory.TryReadByte((uint)FieldMessageReader.AddressFieldWindowStates + (uint)i, out var owner) ||
                !memory.TryReadUInt32(TextPointerAddress + (uint)(i * 4), out var pointer)) return false;
            windows[i] = new(state, owner, pointer);
        }
        return true;
    }

    private readonly record struct WindowHeader(ushort State, byte Owner, uint Pointer);
}

/// <summary>Announces completed visible pages once while leaving Confirm to the player.</summary>
public sealed class WorldMapDialogueTracker
{
    private readonly Dictionary<int, (uint Pointer, string Text)> spoken = new();

    public string? Observe(WorldMapDialogueSnapshot snapshot)
    {
        foreach (var id in spoken.Keys.Except(snapshot.Windows.Select(w => w.WindowId)).ToArray())
            spoken.Remove(id);
        var lines = new List<string>();
        foreach (var window in snapshot.Windows)
        {
            if (!window.IsWaitingForInput || window.Text.Length == 0) continue;
            var key = (window.TextPointer, window.Text);
            if (spoken.TryGetValue(window.WindowId, out var previous) && previous == key) continue;
            spoken[window.WindowId] = key;
            lines.Add(window.Text);
        }
        return lines.Count == 0 ? null : string.Join(" ", lines);
    }

    public void Reset() => spoken.Clear();
}

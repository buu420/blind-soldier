using Ff7.Accessibility.LegacyLayout;
namespace Ff7.Accessibility.Reloaded;

// Read-only compatibility with Postscriptthree Gameplay Tweaks 1.78.
// Evidence: BattlePHS/LimitOverhaul native hext, independently disassembled in Ghidra.
// These menus reuse renderers; recognizing their command without their code is unsafe.
internal sealed class PostscriptBattleCompatibility
{
    private const int Command = 0x00DC3C70;
    private readonly Func<int, byte> readByte;
    private readonly Func<int, ushort> readUInt16;
    private readonly Func<int, int> readInt32;
    private readonly Func<int, int, bool> readable;
    private readonly Func<int, int, string> readText;
    private readonly Func<int, string?>? limitName;
    private readonly Func<int, string?>? limitDescription;
    private static readonly byte[] Route = Convert.FromHexString("8A0C10880D703CDC008A44100180F91D7C1031C088C883E81D6BC00340");
    private static readonly byte[] Store = Convert.FromHexString("A2743CDC00");
    private static readonly byte[] PhsRenderer = Convert.FromHexString("833D703CDC001D740F0F1F400081F9FF000000");
    private static readonly byte[] LimitRenderer = Convert.FromHexString("833D703CDC001E740E0F1F40008B45D431C9");
    internal PostscriptBattleCompatibility(Func<int, byte> readByte, Func<int, ushort> readUInt16,
        Func<int, int> readInt32, Func<int, int, bool> readable, Func<int, int, string> readText,
        Func<int, string?>? limitName, Func<int, string?>? limitDescription)
    {
        this.readByte = readByte;
        this.readUInt16 = readUInt16;
        this.readInt32 = readInt32;
        this.readable = readable;
        this.readText = readText;
        this.limitName = limitName;
        this.limitDescription = limitDescription;
    }

    internal bool TryReadSelection(short renderer, int partySlot, out BattleMenuSelectionSnapshot selection, out bool handled)
    {
        selection = default;
        handled = false;
        // Missing optional state in a stock address-space adapter is not a failed stock read.
        if (renderer is not (4 or 6)) return false;
        byte command;
        try { command = readByte(Command); } catch { return false; }
        if (command is < 29 or >= 128) return false;
        handled = true;
        try
        {
            var first = Read(renderer, partySlot);
            var second = Read(renderer, partySlot);
            if (first is null || second != first) return false;
            selection = first.Value.Selection;
            return true;
        }
        catch { return false; }
    }

    internal string? ReadRootCommandName(int commandId) => ReadRootCommandName(commandId, out _);

    internal string? ReadRootCommandName(int commandId, out bool handled)
    {
        handled = false;
        if (commandId is not (29 or 30)) return null;
        try
        {
            if (!Recognized(commandId)) return null;
            handled = true;
            var address = commandId == 29 ? 0x0091A960 : 0x0091A938;
            var first = readText(address, 12);
            return !string.IsNullOrWhiteSpace(first) && Recognized(commandId) && readText(address, 12) == first ? first : null;
        }
        catch { return null; }
    }

    private bool Recognized(int command) => Matches(0x006D9528, Route) && Matches(0x006D954A, Store) &&
        (command == 29 ? Matches(0x009A7A21, PhsRenderer) : command == 30 && Matches(0x00916A51, LimitRenderer));

    private bool Matches(int address, byte[] expected)
    {
        if (!readable(address, expected.Length)) return false;
        for (int i = 0; i < expected.Length; i++) if (readByte(address + i) != expected[i]) return false;
        return true;
    }

    private Observation? Read(short renderer, int partySlot)
    {
        var command = readByte(Command);
        if (partySlot is < 0 or > 2 || command is not (29 or 30) || readInt32(Command) != command || !Recognized(command) ||
            readByte(0xDC3C7C) != partySlot || readByte(0xDC3C74) != (command == 29 ? 1 : 4) ||
            readUInt16(0x91EF9C) != renderer || renderer != (command == 29 ? 6 : 4)) return null;
        var a = readByte(0xDC0230); var b = readByte(0xDC0231); var c = readByte(0xDC0232);
        var actor = partySlot switch { 0 => a, 1 => b, _ => c };
        if (actor >= 9 || actor != readByte(0x9AB0DC + partySlot * 0x68 + 8)) return null;
        var column = readInt32((command == 29 ? 0xDC2110 : 0xDC2180) + partySlot * 0x700);
        var row = readInt32((command == 29 ? 0xDC2114 : 0xDC2184) + partySlot * 0x700);
        var scroll = readInt32((command == 29 ? 0xDC2124 : 0xDC2194) + partySlot * 0x700);
        int columns = command == 29 ? 3 : 2;
        long index = (long)column + ((long)row + scroll) * columns;
        if (column < 0 || column >= columns || row < 0 || scroll < 0 || index < 0 || index > (command == 29 ? 8 : 6)) return null;
        var selected = (int)index;
        if (command == 29)
        {
            var visible = readUInt16(0xDC0DDE);
            if ((visible & (1 << selected)) == 0) return null; // The native renderer omits this row.
            var hp = readUInt16(0xDBFDB8 + selected * 0x84);
            var name = readText(0xDBFD9C + selected * 0x84, 12);
            if (string.IsNullOrWhiteSpace(name)) return null;
            return new(command, actor, a, b, c, column, row, scroll, visible, hp, 0, 0, 0, 0, 0,
                new(selected, name, null, null, null, hp > 0 && selected != a && selected != b && selected != c, selected, command));
        }
        // 00916A74..00916A99: seven visible limits, with the unused third bit of each level skipped.
        var learned = readUInt16(0xDBFDAE + actor * 0x84); int learnedBit = selected + selected / 2;
        if ((learned & (1 << learnedBit)) == 0) return null;
        var level = readByte(0xDBFD9A + actor * 0x84);
        if (level is < 1 or > 4) return null;
        var selectedThreshold = readUInt16(0x99CE38 + actor * 0x38 + (selected / 2) * 4);
        var currentThreshold = readUInt16(0x99CE38 + actor * 0x38 + (level - 1) * 4);
        if (selectedThreshold == 0 || currentThreshold == 0) return null;
        var gauge = readByte(0x9A8DC0 + partySlot * 0x34);
        // FUN_005C8684 resolves exactly the byte from the loaded per-character table, then subtracts 0x80.
        var encodedId = readByte(0x99CE1C + actor * 0x38 + learnedBit);
        if (encodedId is < 128 or 255) return null;
        var id = encodedId - 128; var limit = limitName?.Invoke(id);
        if (string.IsNullOrWhiteSpace(limit)) return null;
        return new(command, actor, a, b, c, column, row, scroll, 0, 0, learned, level, selectedThreshold, currentThreshold, gauge,
            new(id, limit, limitDescription?.Invoke(id), null, null,
                gauge * currentThreshold >= 255 * selectedThreshold, selected, command));
    }
    private readonly record struct Observation(int Command, int Actor, int Party0, int Party1, int Party2,
        int Column, int Row, int Scroll, int Visible, int Hp, int Learned, int Level, int SelectedThreshold, int CurrentThreshold, int Gauge,
        BattleMenuSelectionSnapshot Selection);
}

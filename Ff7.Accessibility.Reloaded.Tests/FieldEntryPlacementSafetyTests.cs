using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class FieldEntryPlacementSafetyTests
{
    internal static void Run()
    {
        var writes = new Dictionary<(int Bank, int Address), int> { [(15, 143)] = 1 };
        // The native md8 entry mechanism is the motivating supported branch. If a
        // different field also tests that byte, unsupported control flow must not
        // be silently treated as straight-line execution.
        foreach (var operation in new[] { new byte[] { 0x07, 0 }, new byte[] { 0x04, 0, 3 }, new byte[] { 0x60, 0, 0, 0, 0, 0, 0, 0, 0, 0 } })
        {
            var actor = Entry(operation);
            Check(FieldEntryPlacement.Place([actor], 0, 0, 0, writes) is null,
                $"unsupported entry control opcode {operation[0]:X2} cannot certify a landing");
        }

        var caller = Script(1, 0, [0xA0, 0], [0x00], [0x14, 0xF0, 143, 1, 0, 1], [0x03, 2, 3], [0x00]);
        var intermediate = Script(2, 3, [0x03, 1, 3], [0x00]);
        var mover = Script(1, 3, [0xA5, 0, 0, 10, 0, 20, 0, 0, 0, 5, 0], [0x00]);
        var indirectlyPlaced = FieldEntryPlacement.Place([caller, intermediate, mover], 0, 0, 0, writes);
        Check(indirectlyPlaced is null || indirectlyPlaced == new FieldEntryPlacementResult(10, 20, 0, 5),
            "an indirect request that moves the party cannot be skipped");

        var unknownWriter = Script(2, 3, [0x80, 0xF0, 143, 2], [0x00]);
        var selectorCaller = Script(1, 0, [0xA0, 0], [0x00], [0x14, 0xF0, 143, 1, 0, 1],
            [0x03, 2, 3], [0x14, 0xF0, 143, 1, 0, 12],
            [0xA5, 0, 0, 10, 0, 20, 0, 0, 0, 5, 0], [0x00]);
        var selectorPlaced = FieldEntryPlacement.Place([selectorCaller, unknownWriter], 0, 0, 0, writes);
        Check(selectorPlaced is null || selectorPlaced == new FieldEntryPlacementResult(0, 0, null, 0),
            "a requested script changing the entry selector cannot be skipped");
        Console.WriteLine("Entry placement rejects unsupported control flow and indirect state changes.");
    }

    private static FieldScriptDefinition Entry(byte[] operation) => Script(1, 0,
        [0xA0, 0], [0x00], [0x14, 0xF0, 143, 1, 0, 1], operation,
        [0xA5, 0, 0, 10, 0, 20, 0, 0, 0, 5, 0], [0x00]);

    private static FieldScriptDefinition Script(int entity, int slot, params byte[][] operations)
    {
        var offset = 0;
        var decoded = new List<FieldScriptOpcodeDefinition>();
        foreach (var bytes in operations)
        {
            decoded.Add(new(733, entity, $"actor{entity}", slot, offset, bytes[0], bytes));
            offset += bytes.Length;
        }
        return new(733, entity, $"actor{entity}", slot, decoded);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Entry placement safety: " + message);
    }
}

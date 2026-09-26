using Ff7.Accessibility.Reloaded;

namespace GuideRouteAudit;

internal static class NativeLineWitnesses
{
    internal sealed record Witness(FieldNavigationTriggerLine? Line, string Evidence);

    // These are possible native segments, not a claim that Init and later SLINE states
    // coexist. Retain variable SLINE as an unknown witness instead of substituting the
    // catalog midpoint for the live segment used by the production object reader.
    internal static IReadOnlyList<Witness> Read(IReadOnlyList<FieldScriptDefinition> scripts, int entity)
    {
        var result = new List<Witness>();
        foreach (var op in scripts.Where(s => s.EntityId == entity).SelectMany(s => s.Opcodes)
                     .Where(o => o.Opcode is 0xD0 or 0xD3))
        {
            var bytes = op.Bytes.ToArray();
            var offset = op.Opcode == 0xD0 ? 1 : 4;
            var literal = op.Opcode == 0xD0 ? bytes.Length == 13 :
                bytes.Length == 16 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0;
            FieldNavigationTriggerLine? line = literal ? new(
                BitConverter.ToInt16(bytes, offset), BitConverter.ToInt16(bytes, offset + 2),
                BitConverter.ToInt16(bytes, offset + 4), BitConverter.ToInt16(bytes, offset + 6),
                BitConverter.ToInt16(bytes, offset + 8), BitConverter.ToInt16(bytes, offset + 10)) : null;
            if (result.Any(r => r.Line == line)) continue;
            result.Add(new(line, $"{(op.Opcode == 0xD0 ? "LINE" : "SLINE")}:{op.ScriptId}:{op.ByteIndex}" +
                (literal ? "" : ":variable-segment")));
        }
        if (result.Count == 0) result.Add(new(null, "no-native-LINE-segment"));
        return result;
    }
}

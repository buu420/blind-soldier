using Ff7.Accessibility.Reloaded;

namespace GuideRouteAudit;

internal sealed class NativeRadii
{
    private readonly IReadOnlyList<FieldScriptDefinition> scripts;
    private readonly int scale;
    internal NativeRadii(byte[] field, IReadOnlyList<FieldScriptDefinition> scripts)
    {
        this.scripts = scripts;
        // Ghidra 0060BCFA copies script header +8 into context +0x10, initializes
        // collision to 30*scale/512 and Talk to 80*scale/512. 0061813D/00618253
        // and their two-byte counterparts apply the same scaling to script ranges.
        scale = BitConverter.ToInt16(field, BitConverter.ToInt32(field, 6) + 4 + 8);
    }
    internal (int Value, bool Exact) Player()
    {
        var entities = scripts.Where(s => s.Opcodes.Any(o => o.Opcode is 0xA0 or 0xBF)).Select(s => s.EntityId).Distinct().ToArray();
        var options = entities.Select(e => Read(e, false)).ToArray();
        return options.Length == 0 ? (30 * scale / 512, false) :
            (options.Max(x => x.Value), options.All(x => x.Exact) && options.Select(x => x.Value).Distinct().Count() == 1);
    }
    internal (int Value, bool Exact) Read(int entity, bool talk)
    {
        var values = new HashSet<int> { (talk ? 80 : 30) * scale / 512 };
        var exact = true;
        foreach (var o in scripts.Where(s => s.EntityId == entity).SelectMany(s => s.Opcodes)
                     .Where(o => talk ? o.Opcode is 0xC5 or 0xD6 : o.Opcode is 0xC6 or 0xD7))
        {
            // Parameter 2 is the low nibble of byte 1. Bank-backed values stay unknown.
            if ((o.Bytes[1] & 15) != 0) { exact = false; continue; }
            var value = o.Bytes.Count == 3 ? o.Bytes[2] : BitConverter.ToInt16(o.Bytes.ToArray(), 2);
            values.Add(value * scale / 512);
        }
        // An envelope is useful for finding impossible geometry, but different
        // ranges are not evidence of one playable state and never earn a pass.
        return (values.Max(), exact && values.Count == 1);
    }
}

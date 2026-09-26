using System.Text.Json;
using Ff7.Accessibility.Reloaded;

namespace GuideRouteAudit;

internal static class InspectField
{
    internal static int Run(string gameRoot, int field, string output)
    {
        var source = new FlevelDataSource(gameRoot);
        if (!source.TryReadField(field, out var encoded)) throw new InvalidDataException(source.Diagnostic);
        var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
        const int ptr = 0x02000000;
        int Int(int a) => a == FieldWalkmeshReader.AddressFieldDataPtr ? ptr :
            a >= ptr && a + 4 <= ptr + bytes.Length ? BitConverter.ToInt32(bytes, a - ptr) : 0;
        short Short(int a) => a >= ptr && a + 2 <= ptr + bytes.Length ? BitConverter.ToInt16(bytes, a - ptr) : (short)0;
        var mesh = new FieldWalkmeshReader(Int, Short).Read(new(1, field, 0, 0, 0, 0, 0, 0)).Walkmesh;
        if (mesh is null) throw new InvalidDataException("Native mesh unavailable.");
        // Private research output from the user's licensed installation. Do not commit.
        File.WriteAllText(output, JsonSerializer.Serialize(mesh, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Wrote {mesh.Triangles.Count} native triangles for field {field}.");
        return 0;
    }
}

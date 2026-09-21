using System.Buffers.Binary;
using System.Text.Json;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Scratch export of the installed script identity for a list of fields, computed by the
/// production <see cref="LoadedFieldScriptIdentityReader"/> over the installed flevel exactly
/// as the running game would see it. Research only; the values go into
/// <c>EchoSCompatibilityManifest.Fingerprints</c> by hand after both archives agree.
/// </summary>
internal static class FieldScriptIdentityProbe
{
    public static void Run(string fieldList, string outputPath)
    {
        const uint scriptPointer = 0x02000000;
        var gameRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? @"C:\Games\Final Fantasy VII\workingdir";
        var source = new FlevelDataSource(gameRoot);
        var rows = new List<object>();
        foreach (var fieldId in fieldList.Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(value => int.Parse(value.Trim()))
                     .Distinct()
                     .OrderBy(value => value))
        {
            if (!source.TryReadField(fieldId, out var encoded))
            {
                rows.Add(new { fieldId, identity = (string?)null, diagnostic = "field unavailable" });
                continue;
            }

            var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
            var sectionOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(6));
            var sectionLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(sectionOffset));
            var section = bytes.AsSpan(sectionOffset + sizeof(int), sectionLength).ToArray();
            var memory = new ProbeMemory();
            memory.Write((uint)FieldScriptContextReader.AddressCurrentModule, FieldPositionReader.FieldModule);
            memory.WriteUInt16((uint)FieldScriptContextReader.AddressCurrentFieldId, (ushort)fieldId);
            memory.WriteUInt32((uint)FieldScriptContextReader.AddressFieldScriptPtr, scriptPointer);
            memory.Write(scriptPointer, section);
            var reader = new LoadedFieldScriptIdentityReader(memory);
            rows.Add(reader.TryRead(out var identity)
                ? new { fieldId, identity = (string?)identity.ScriptPrefixSha256, diagnostic = "ok" }
                : new { fieldId, identity = (string?)null, diagnostic = "identity read failed" });
        }

        File.WriteAllText(
            outputPath,
            JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"identities for {rows.Count} fields into {outputPath}");
    }

    private sealed class ProbeMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];

        public void Write(uint address, byte value) => bytes[address] = value;

        public void Write(uint address, ReadOnlySpan<byte> value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                bytes[address + (uint)index] = value[index];
            }
        }

        public void WriteUInt16(uint address, ushort value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            Write(address, buffer);
        }

        public void WriteUInt32(uint address, uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            Write(address, buffer);
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(virtualAddress + (uint)index, out var value))
                {
                    return false;
                }

                destination[index] = value;
            }

            return true;
        }
    }
}

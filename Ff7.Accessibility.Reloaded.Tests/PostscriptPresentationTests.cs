using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class PostscriptPresentationTests
{
    internal static void Run()
    {
        var failures = new List<string>();
        void Check(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS Postscript presentation " + name); }
            catch (Exception e) { failures.Add(name + ": " + e.Message); }
        }
        Check("both native accessory rows", () =>
        {
            var status = default(StatusMenuSnapshot) with { Name = "Cloud", AccessoryName = "Ribbon",
                SecondAccessoryName = "Protect Ring", HasSecondAccessorySlot = true };
            var text = StatusMenuSpeechTracker.Format(status);
            Require(text.Contains("Accessory 1 Ribbon") && text.Contains("Accessory 2 Protect Ring"), text);
            Require(!StatusMenuSpeechTracker.Format(status with { HasSecondAccessorySlot = false })
                .Contains("Protect Ring"), "Stock status must not expose the unused second accessory byte.");
        });
        Check("remapped dialogue roles preserve party tokens", () =>
        {
            byte[] bytes = [0xEA, 0x00, 0xF3, 0xFF];
            var text = string.Join(" ", Ff7EncodedTextDecoder.DecodePages(bytes,
                slot => slot == 0 ? "Vincent" : null, id => id == 0 ? "Tifa" : null)
                .SelectMany(p => p.Lines).Select(l => l.Text));
            Require(text == "Tifa Vincent", "Expected displayed role and actual party slot, got " + text);
            var stock = Ff7EncodedTextDecoder.DecodePages(bytes, _ => "Vincent");
            Require(stock[0].Lines[0].Text == "Cloud Vincent", "Stock name expansion changed.");
        });
        Check("stock message roles retain the existing decoder", () =>
        {
            var memory = new PresentationMemory();
            memory.Write((uint)FieldPositionReader.AddressCurrentModule, [1]);
            memory.Write((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)116));
            memory.Write(FieldMessageReader.AddressFieldMessageDataPointer, BitConverter.GetBytes(0x100000u));
            memory.Write(0x100000, [1,0,4,0,0xEA,0xFF]);
            var reader = new FieldMessageReader(memory);
            Require(reader.TryReadMessageById(0, out var text) && text.Text == "Cloud",
                "Unpatched messages must not require the randomizer's role/name state.");
        });
        Check("unavailable requested role does not become a canonical name", () =>
        {
            var memory = new PresentationMemory();
            memory.Write(0x00631FCA, Convert.FromHexString("E8712238008945FC90908B4DFC"));
            memory.Write((uint)FieldPositionReader.AddressCurrentModule, [1]);
            memory.Write((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)116));
            memory.Write(FieldMessageReader.AddressFieldMessageDataPointer, BitConverter.GetBytes(0x100000u));
            memory.Write(0x100000, [1,0,4,0,0xEA,0xFF]);
            var reader = new FieldMessageReader(memory);
            Require(!reader.TryReadMessageById(0, out _),
                "A recognized remapping with unavailable native identity must not produce preview speech.");
        });
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class PresentationMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = new();
        public void Write(uint address, byte[] values)
        { for (var i=0;i<values.Length;i++) bytes[address+(uint)i]=values[i]; }
        public bool TryRead(uint address, Span<byte> destination)
        { for(var i=0;i<destination.Length;i++) destination[i]=bytes.GetValueOrDefault(address+(uint)i); return true; }
    }
}

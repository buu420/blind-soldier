using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class FieldCreatureWarningReaderTests
{
    public static void Run()
    {
        foreach (var variant in new[] { "stock", "base", "Aavock", "ESUI", "FT", "Retouch" })
        {
            var m = new Memory(variant);
            var identity = new LoadedFieldScriptIdentityReader(m);
            var reader = new FieldCreatureWarningReader(m, () => identity.TryRead(out var value) ? value : null);
            Check(reader.TryRead(out var full), $"{variant}: real native MESSAGE44 full page");
            Check(full.DialogId == m.Dialog && full.Text.Contains("coming!!", StringComparison.Ordinal),
                $"{variant}: native dialog/text must match the checked table");
            Check(full.WindowPointer == m.MessageStart, $"{variant}: normalized page identity uses start, not typing cursor");

            m.ShowPrefix(0);
            Check(!reader.TryRead(out _), $"{variant}: an empty opening window is not fully visible");
            Check(Owner(reader, out var early) && early.Text == full.Text,
                $"{variant}: native ownership is available before any text is typed");
            var gate = new FieldCreatureWarningSpeechGate(reader);
            gate.NotePrismAccepted(1, full.Text);
            Check(!gate.IsCompleted(true), $"{variant}: backend progress while typing cannot permit narration");
            m.ShowPrefix(3);
            Check(Owner(reader, out var partial) && partial == early,
                $"{variant}: an advancing cursor preserves the same exact page identity");
            Check(!gate.IsCompleted(false), $"{variant}: idle is insufficient before full current page");
            m.ShowPrefix(m.RawMessage.Length - 1);
            Check(reader.TryRead(out var typed) && typed == full,
                $"{variant}: the terminator cursor remains owned after typing completes");
            Check(gate.IsCompleted(false), $"{variant}: accepted warning and observed busy-to-idle may finish after typing");

            gate.Reset();
            gate.NotePrismAccepted(1, "Cloud");
            gate.IsCompleted(true);
            Check(!gate.IsCompleted(false), $"{variant}: a typed fragment cannot stand in for accepted full speech");
            gate.NotePrismAccepted(0, full.Text);
            Check(!gate.IsCompleted(false), $"{variant}: unrelated accepted window cannot finish warning");
            gate.NotePrismAccepted(1, full.Text);
            Check(!gate.IsCompleted(false), $"{variant}: acceptance alone cannot finish warning");
            gate.IsCompleted(true);
            Check(!gate.IsCompleted(null), $"{variant}: unknown backend cannot finish warning");

            m.U16(FieldActivityStateReader.AddressEntityProgramCounters + 6, (ushort)(m.ScriptEntry + 43));
            Reject(reader, variant, "wrong entity cursor");
            m.U16(FieldActivityStateReader.AddressEntityProgramCounters + 6, (ushort)(m.ScriptEntry + 44));
            m.Byte(FieldScriptControllerReader.AddressEntityScriptIds + 3 * 8 + 3, 5);
            Reject(reader, variant, "wrong running script");
            m.Byte(FieldScriptControllerReader.AddressEntityScriptIds + 3 * 8 + 3, 6);
            m.Byte(FieldMessageReader.AddressFieldWindowStates + 1, 2);
            Reject(reader, variant, "different native window owner");
            m.Byte(FieldMessageReader.AddressFieldWindowStates + 1, 3);
            m.U32(FieldMessageReader.AddressFieldWindowMessagePointers + 4, m.PreviousMessageStart);
            Reject(reader, variant, "stale Jenova page cursor even with current warning buffer");
            m.U32(FieldMessageReader.AddressFieldWindowMessagePointers + 4, m.MessageStart + (uint)m.RawMessage.Length);
            Reject(reader, variant, "cursor beyond exact warning terminator");
            m.ShowPrefix(m.RawMessage.Length - 1);
            m.OtherWindow(true);
            Reject(reader, variant, "another readable window must protect its dialogue");
            m.OtherWindow(false);
            m.TearPointer = true;
            Reject(reader, variant, "torn cursor ownership");
            m.TearPointer = false;
            m.ShowPrefix(m.RawMessage.Length - 1);
            m.U32(FieldMessageReader.AddressFieldMessageDataPointer, m.TextData + 2);
            Reject(reader, variant, "message data detached from loaded section");
            m.U32(FieldMessageReader.AddressFieldMessageDataPointer, m.TextData);
            m.Byte(FieldPositionReader.AddressCurrentModule, 2);
            Reject(reader, variant, "non-field module");
            m.Byte(FieldPositionReader.AddressCurrentModule, 1);
            m.U16(FieldPositionReader.AddressFieldId, 766);
            Reject(reader, variant, "different current field");
            m.U16(FieldPositionReader.AddressFieldId, 767);
            m.Byte(Memory.Script + (uint)m.ScriptEntry + 46, (byte)(m.Dialog + 1));
            Reject(reader, variant, "unreviewed script hash/dialog operand");
        }
        Console.WriteLine("Native field767 warning ownership/full-page/early acceptance fixtures passed for stock and all five Echo variants.");
    }

    private static bool Owner(FieldCreatureWarningReader reader, out FieldCreatureWarning warning)
    {
        var method = typeof(FieldCreatureWarningReader).GetMethod("TryReadSpeechOwner", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(method is not null, "missing early native MESSAGE44 speech-owner reader");
        object?[] args = [null];
        var ok = (bool)method!.Invoke(reader, args)!;
        warning = ok ? (FieldCreatureWarning)args[0]! : default;
        return ok;
    }
    private static void Reject(FieldCreatureWarningReader reader, string variant, string reason)
    {
        Check(!Owner(reader, out _) && !reader.TryRead(out _), $"{variant}: {reason} must fail closed");
    }
    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        internal const uint Script = 0x02000000;
        private const uint Model = 0x02020000;
        private readonly Dictionary<uint, byte> bytes = [];
        internal readonly int Dialog, ScriptEntry;
        internal readonly uint TextData, MessageStart, PreviousMessageStart;
        internal readonly byte[] RawMessage;
        internal bool TearPointer;

        internal Memory(string variant)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
                "Fixtures", "Field767Warning", variant + ".json")));
            var section = Convert.FromBase64String(json.RootElement.GetProperty("sectionBase64").GetString()!);
            Check(Convert.ToHexString(SHA256.HashData(section)).Equals(json.RootElement.GetProperty("sha256").GetString(),
                StringComparison.OrdinalIgnoreCase), $"{variant}: fixture hash");
            // Exact section bytes, with mapped readable native text-range padding for the chunked reader.
            Put(Script, Enumerable.Repeat((byte)0xff, 0x10000 + section.Length).ToArray());
            Put(Script, section);
            Dialog = variant == "stock" ? 10 : 9;
            var textOffset = BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(4));
            Check(Convert.ToHexString(SHA256.HashData(section.AsSpan(0, textOffset)))
                .Equals(json.RootElement.GetProperty("scriptPrefixSha256").GetString(), StringComparison.OrdinalIgnoreCase),
                $"{variant}: actual loaded-prefix identity");
            var entryTable = 32 + section[2] * 8 + BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(6)) * 4;
            ScriptEntry = BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(entryTable + (3 * 32 + 6) * 2));
            Check(section.AsSpan(ScriptEntry + 44, 3).SequenceEqual(new byte[] { 0x40, 1, (byte)Dialog }),
                $"{variant}: actual Cloud e3/s6 MESSAGE44 bytes");
            TextData = Script + textOffset;
            MessageStart = TextData + BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(textOffset + 2 + Dialog * 2));
            PreviousMessageStart = TextData + BinaryPrimitives.ReadUInt16LittleEndian(section.AsSpan(textOffset + 2 + (Dialog - 1) * 2));
            var start = (int)(MessageStart - Script);
            var count = section.AsSpan(start).IndexOf((byte)0xff) + 1;
            Check(count > 1, $"{variant}: native terminated warning");
            RawMessage = section.AsSpan(start, count).ToArray();
            Byte(FieldPositionReader.AddressCurrentModule, 1);
            U16(FieldPositionReader.AddressFieldId, 767);
            U32(FieldScriptControllerReader.AddressFieldScriptPointer, Script);
            Byte(FieldScriptControllerReader.AddressEntityScriptPriorities + 3, 3);
            Byte(FieldScriptControllerReader.AddressEntityScriptIds + 3 * 8 + 3, 6);
            Byte(FieldScriptControllerReader.AddressEntityModelIds + 3, 0);
            U32(FieldScriptControllerReader.AddressModelTablePointer, Model);
            Byte(FieldScriptControllerReader.AddressModelAnimationRunState, 0);
            Byte(Model + 0x62, 1); Byte(Model + 0x64, 7);
            U16(Model + 0x68, 0); U16(Model + 0x6a, 10);
            U16(FieldActivityStateReader.AddressEntityProgramCounters + 6, (ushort)(ScriptEntry + 44));
            U32(FieldMessageReader.AddressFieldMessageDataPointer, TextData);
            for (var w = 0; w < 4; w++)
            {
                Byte((uint)(FieldMessageReader.AddressFieldWindowStates + w), 0xff);
                U32((uint)(FieldMessageReader.AddressFieldWindowMessagePointers + w * 4), 0);
                Put((uint)(FieldMessageReader.AddressFieldWindowTextBuffers + w * 256), Enumerable.Repeat((byte)0xff, 256).ToArray());
            }
            Byte(FieldMessageReader.AddressFieldWindowStates + 1, 3);
            ShowPrefix(RawMessage.Length - 1);
        }
        internal void ShowPrefix(int encodedCount)
        {
            var buffer = Enumerable.Repeat((byte)0xff, 256).ToArray();
            RawMessage.AsSpan(0, encodedCount).CopyTo(buffer);
            Put(FieldMessageReader.AddressFieldWindowTextBuffers + 256, buffer);
            U32(FieldMessageReader.AddressFieldWindowMessagePointers + 4, MessageStart + (uint)encodedCount);
        }
        internal void OtherWindow(bool active)
        {
            Byte(FieldMessageReader.AddressFieldWindowStates, active ? (byte)4 : (byte)0xff);
            U32(FieldMessageReader.AddressFieldWindowMessagePointers, active ? PreviousMessageStart : 0);
            Put(FieldMessageReader.AddressFieldWindowTextBuffers, active ? RawMessage : new byte[] { 0xff });
        }
        public bool TryRead(uint address, Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++)
                if (!bytes.TryGetValue(address + (uint)i, out destination[i])) return false;
            if (TearPointer && address == FieldMessageReader.AddressFieldWindowMessagePointers + 4)
            {
                var value = BinaryPrimitives.ReadUInt32LittleEndian(destination);
                U32(address, value == MessageStart ? PreviousMessageStart : MessageStart);
            }
            return true;
        }
        internal void Byte(uint address, byte value) => bytes[address] = value;
        internal void U16(uint address, ushort value)
        {
            Span<byte> valueBytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(valueBytes, value); Put(address, valueBytes);
        }
        internal void U32(uint address, uint value)
        {
            Span<byte> valueBytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(valueBytes, value); Put(address, valueBytes);
        }
        private void Put(uint address, ReadOnlySpan<byte> values)
        {
            for (var i = 0; i < values.Length; i++) bytes[address + (uint)i] = values[i];
        }
    }
}

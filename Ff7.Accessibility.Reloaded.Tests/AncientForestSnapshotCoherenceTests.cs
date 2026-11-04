using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class AncientForestSnapshotCoherenceTests
{
    internal static void Run()
    {
        var memory = new MovingMemory();
        var identity = new LoadedFieldScriptIdentity(622, MovingMemory.Script,
            EchoSCompatibilityManifest.GetVanillaFingerprint(622)!);
        var reader = new AncientForestPuzzleStateReader(memory, () => identity);
        if (reader.ReadState(622) is null) throw new InvalidOperationException("The coherent bank must survive unreadable optional model/cursor data.");
        memory.DuringOptionalCapture = () => memory.Bank[9] = 4;
        if (reader.ReadState(622) is not null)
            throw new InvalidOperationException("A pickup during model/cursor capture must not combine old carrying state with later scene data.");
        Console.WriteLine("Ancient Forest assembled snapshot coherence test passed.");
    }

    private sealed class MovingMemory : ILegacyAddressSpace
    {
        internal const uint Script = 0x02000000;
        internal byte[] Bank { get; } = new byte[128];
        internal Action? DuringOptionalCapture;
        public bool TryRead(uint address, Span<byte> destination)
        {
            if (address == FieldNavigationObjectReader.AddressTemporaryFieldBankBase)
                Bank.CopyTo(destination);
            else if (address == FieldScriptContextReader.AddressCurrentModule && destination.Length == 1)
                destination[0] = FieldPositionReader.FieldModule;
            else if (address == FieldPositionReader.AddressFieldId && destination.Length == 2)
                BinaryPrimitives.WriteUInt16LittleEndian(destination, 622);
            else if (address == FieldScriptContextReader.AddressFieldScriptPtr && destination.Length == 4)
                BinaryPrimitives.WriteUInt32LittleEndian(destination, Script);
            else
            {
                var change = DuringOptionalCapture; DuringOptionalCapture = null; change?.Invoke();
                return false;
            }
            return true;
        }
    }
}

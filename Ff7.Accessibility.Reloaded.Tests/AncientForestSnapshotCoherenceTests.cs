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
        memory.Bank[19] = 1;
        memory.Pickups = AncientForestStoryGuide.Supershot;
        if (reader.ReadStoryState(622) is not { Collected: AncientForestStoryGuide.Supershot })
            throw new InvalidOperationException("Story must sample the persistent collection bits and the current room together.");
        memory.OnPickupsRead = () => memory.Bank[19] = 2;
        if (reader.ReadStoryState(622) is not null)
            throw new InvalidOperationException("A throw zone changed across the capture.");
        memory.OnPickupsRead = () => memory.Pickups |= AncientForestStoryGuide.SpringGun;
        if (reader.ReadStoryState(622) is not null)
            throw new InvalidOperationException("A reward was collected across the capture.");
        memory.PlayerControl = 1;
        if (reader.ReadStoryState(622) is not null)
            throw new InvalidOperationException("Story must wait for the game to hand the controls back.");
        Console.WriteLine("Ancient Forest assembled snapshot coherence test passed.");
    }

    private sealed class MovingMemory : ILegacyAddressSpace
    {
        internal const uint Script = 0x02000000;
        internal byte[] Bank { get; } = new byte[128];
        internal Action? DuringOptionalCapture;
        internal Action? OnPickupsRead;
        internal ushort Pickups;
        internal byte PlayerControl;
        public bool TryRead(uint address, Span<byte> destination)
        {
            if (address == FieldNavigationObjectReader.AddressTemporaryFieldBankBase)
                Bank.AsSpan(0, destination.Length).CopyTo(destination);
            else if (address == FieldScriptContextReader.AddressCurrentModule && destination.Length == 1)
                destination[0] = FieldPositionReader.FieldModule;
            else if (address == FieldPositionReader.AddressFieldId && destination.Length == 2)
                BinaryPrimitives.WriteUInt16LittleEndian(destination, 622);
            else if (address == FieldScriptContextReader.AddressFieldScriptPtr && destination.Length == 4)
                BinaryPrimitives.WriteUInt32LittleEndian(destination, Script);
            else if (address == FieldAudibleCueStateReader.AddressUserControl && destination.Length == 1)
                destination[0] = PlayerControl;
            else if (address == FieldNavigationObjectReader.AddressFieldBankBase + 0x400 + 37 && destination.Length == 2)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(destination, Pickups);
                var change = OnPickupsRead; OnPickupsRead = null; change?.Invoke();
            }
            else
            {
                var change = DuringOptionalCapture; DuringOptionalCapture = null; change?.Invoke();
                return false;
            }
            return true;
        }
    }
}

using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Pagoda's way into the Hidden Room, uutai2 gateway 0, is not a door the player can
/// use until the bell has opened it.
///
/// <para>uutai2/AD holds triangle 128 on entry unless temporary 5[3] is 1, which it sets
/// itself when the party arrives back from uttmpin4. KANE, the bell, tests 5[3] for 0,
/// releases 128 and sets 5[3] to 1. So 5[3] is the door. While it is shut the route
/// planner can still reach the gateway's line from the bell platform 200 units above it,
/// and Exits offered a way into a room nobody has been shown. The exit is now published
/// only while a coherent read of field 587 says 5[3] is 1.</para>
/// </summary>
internal static class WutaiHiddenRoomExitTests
{
    private const string HiddenRoomGateway = "gateway:587:0:591";
    private const int Pagoda = 587;

    private static readonly uint ModuleAddress = FieldPositionReader.AddressCurrentModule;
    private static readonly uint FieldAddress = FieldPositionReader.AddressFieldId;
    private static readonly uint DoorAddress = FieldNavigationObjectReader.AddressTemporaryFieldBankBase + 3;

    public static void Run()
    {
        TheDoorIsReadFromOneCoherentPagodaFrame();
        TheHiddenRoomEntranceWaitsForTheBell();
        TheDoorIsReadAgainOnEveryPublication();
        NothingButTheHiddenRoomEntranceIsGated();
        BothRuntimesWireTheSharedDoorReader();
    }

    /// <summary>
    /// Open is 1 and only 1: AD tests <c>5[3] == 1</c> before it holds 128, and KANE opens
    /// the door only from 0. Anything the reader cannot confirm is not an open door.
    /// </summary>
    private static void TheDoorIsReadFromOneCoherentPagodaFrame()
    {
        Equal(false, new WutaiBellDoorStateReader(PagodaMemory(door: 0)).ReadDoorOpen(),
            "a bell that has not been rung leaves the door shut");
        Equal(true, new WutaiBellDoorStateReader(PagodaMemory(door: 1)).ReadDoorOpen(),
            "5[3] = 1 is the open door, whether the bell or the return from uttmpin4 set it");
        Equal(false, new WutaiBellDoorStateReader(PagodaMemory(door: 2)).ReadDoorOpen(),
            "no other value opens it");

        var unreadable = PagodaMemory(door: 1);
        unreadable.Unreadable.Add(DoorAddress);
        Equal<bool?>(null, new WutaiBellDoorStateReader(unreadable).ReadDoorOpen(),
            "an unreadable flag is unknown, not shut and not open");

        var noField = PagodaMemory(door: 1);
        noField.Unreadable.Add(FieldAddress);
        Equal<bool?>(null, new WutaiBellDoorStateReader(noField).ReadDoorOpen(),
            "an unreadable field id owns nothing");

        var town = PagodaMemory(door: 1);
        town.Set(FieldAddress, 579 & 0xFF, 579 >> 8);
        Equal<bool?>(null, new WutaiBellDoorStateReader(town).ReadDoorOpen(),
            "5[3] belongs to whichever field is loaded; outside 587 it says nothing about this door");

        var world = PagodaMemory(door: 1);
        world.Set(ModuleAddress, 3);
        Equal<bool?>(null, new WutaiBellDoorStateReader(world).ReadDoorOpen(),
            "outside the field module there is no field to own the flag");

        var ringing = PagodaMemory(door: 0);
        ringing.Sequence(DoorAddress, 0, 1);
        Equal<bool?>(null, new WutaiBellDoorStateReader(ringing).ReadDoorOpen(),
            "a flag that changes between its two reads is not yet known");

        var leaving = PagodaMemory(door: 1);
        leaving.Sequence(FieldAddress, Pagoda & 0xFF, 579 & 0xFF);
        Equal<bool?>(null, new WutaiBellDoorStateReader(leaving).ReadDoorOpen(),
            "a field change around the read disowns it");

        var switching = PagodaMemory(door: 1);
        switching.Sequence(ModuleAddress, FieldPositionReader.FieldModule, 3);
        Equal<bool?>(null, new WutaiBellDoorStateReader(switching).ReadDoorOpen(),
            "a module change around the read disowns it");
    }

    private static void TheHiddenRoomEntranceWaitsForTheBell()
    {
        bool? door = false;
        var policy = new FieldExitPresentationPolicy(() => null, () => door);
        Equal(false, policy.Apply(PagodaExits()).Any(t => t.StableId == HiddenRoomGateway),
            "a shut door is not an exit");

        door = null;
        Equal(false, policy.Apply(PagodaExits()).Any(t => t.StableId == HiddenRoomGateway),
            "an unknown door is not offered");

        door = true;
        var open = policy.Apply(PagodaExits()).Single(t => t.StableId == HiddenRoomGateway);
        Equal("Exit to Hidden Room", open.Label, "an open door is the exit it always was");

        var throwing = new FieldExitPresentationPolicy(() => null, () => throw new InvalidOperationException("torn read"));
        Equal(false, throwing.Apply(PagodaExits()).Any(t => t.StableId == HiddenRoomGateway),
            "a failing reader fails closed");

        var unwired = new FieldExitPresentationPolicy(() => null);
        Equal(false, unwired.Apply(PagodaExits()).Any(t => t.StableId == HiddenRoomGateway),
            "a policy with no door reader cannot know the door is open");

        var memory = PagodaMemory(door: 0);
        var wired = new FieldExitPresentationPolicy(() => null, new WutaiBellDoorStateReader(memory).ReadDoorOpen);
        Equal(false, wired.Apply(PagodaExits()).Any(t => t.StableId == HiddenRoomGateway),
            "through the real reader, before the bell");
        memory.Set(DoorAddress, 1);
        Equal(true, wired.Apply(PagodaExits()).Any(t => t.StableId == HiddenRoomGateway),
            "through the real reader, once KANE has set 5[3]");
    }

    /// <summary>
    /// The door opens while the party stands in the field, so nothing about the native
    /// exits changes when it does. Each publication must read the door again, both ways.
    /// </summary>
    private static void TheDoorIsReadAgainOnEveryPublication()
    {
        bool? door = false;
        var policy = new FieldExitPresentationPolicy(() => null, () => door);
        var exits = PagodaExits();
        Equal(false, policy.Apply(exits).Any(t => t.StableId == HiddenRoomGateway), "before the bell");
        door = true;
        Equal(true, policy.Apply(exits).Any(t => t.StableId == HiddenRoomGateway), "after the bell");
        door = false;
        Equal(false, policy.Apply(exits).Any(t => t.StableId == HiddenRoomGateway),
            "back on a later visit, when AD has shut it again");
    }

    private static void NothingButTheHiddenRoomEntranceIsGated()
    {
        var policy = new FieldExitPresentationPolicy(() => null, () => false);
        var published = policy.Apply(PagodaExits()).Select(t => t.StableId).ToArray();
        foreach (var kept in new[] { "gateway:587:1:588", "gateway:587:2:586", "script-exit:587:17:579" })
        {
            Equal(true, published.Contains(kept), $"{kept} is not the bell's door");
        }

        var returnToPagoda = new FieldNavigationTarget(591, FieldNavigationCategory.Exits, "Exit to Wutai, Pagoda",
            8, -566, 8, "gateway:591:1:587", CompletesOnArrival: true, DestinationFieldIds: [587],
            TriggerLine: new FieldNavigationTriggerLine(-52, -566, 8, 68, -566, 8));
        Equal(1, policy.Apply([returnToPagoda]).Count, "the way back out of the Hidden Room is never gated");
    }

    /// <summary>
    /// Both runtimes build the policy with the one shared reader over their own address
    /// space. Checked as source text, as <c>DualRuntimeFeatureWiringTests</c> does, because
    /// the alternative is standing up a whole field session.
    /// </summary>
    private static void BothRuntimesWireTheSharedDoorReader()
    {
        var root = FindSourceRoot();
        var x64Project = File.ReadAllText(Path.Combine(root, "Ff7.Accessibility.Steam2026X64", "Ff7.Accessibility.Steam2026X64.csproj"));
        Equal(true, x64Project.Contains(@"..\Ff7.Accessibility.Reloaded\WutaiBellDoorStateReader.cs", StringComparison.Ordinal),
            "the shared reader is compiled into x64");
        foreach (var (runtime, relative, addressSpace) in new[]
                 {
                     ("x86", Path.Combine("Ff7.Accessibility.Reloaded", "Mod.cs"), "legacyAddressSpace"),
                     ("x64", Path.Combine("Ff7.Accessibility.Steam2026X64", "Runtime", "Field", "Steam2026FieldNavigationCoordinator.cs"), "addressSpace")
                 })
        {
            var text = File.ReadAllText(Path.Combine(root, relative));
            var construction = ReadArguments(text, "new FieldExitPresentationPolicy(");
            Equal(true, construction is not null, $"{runtime} builds the exit policy");
            Equal(true, construction!.Contains("ReadDoorOpen", StringComparison.Ordinal),
                $"{runtime} hands the policy the shared door reader");
            Equal(true, text.Contains($"new WutaiBellDoorStateReader({addressSpace})", StringComparison.Ordinal),
                $"{runtime} reads the door through its own checked address space");
        }
    }

    /// <summary>
    /// 5[3] is the door in the installed scripts, and gateway 0 is the door it opens.
    /// </summary>
    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var catalog = new FieldScriptNavigationCatalog(root);
        var scripts = catalog.ReadAllScriptOpcodes(Pagoda);
        (int Entity, string Hex, string Meaning)[] anchors =
        [
            (14, "1450030000", "KANE opens the door only while 5[3] is 0"),
            (14, "6D800000", "KANE releases triangle 128"),
            (14, "80500301", "KANE sets 5[3] to 1"),
            (11, "166001004F02", "AD recognises the return from uttmpin4"),
            (11, "80500301", "and sets 5[3] to 1 for it"),
            (11, "1450030100", "AD tests 5[3] for 1"),
            (11, "6D800001", "and holds triangle 128 when it is not"),
        ];
        foreach (var anchor in anchors)
        {
            Equal(true, scripts.Any(s => s.EntityId == anchor.Entity && s.ScriptId == 0 &&
                    s.Opcodes.Any(o => Convert.ToHexString(o.Bytes.ToArray())
                        .StartsWith(anchor.Hex, StringComparison.OrdinalIgnoreCase))),
                $"native uutai2 entity {anchor.Entity}: {anchor.Meaning}");
        }

        var setDoor = scripts.Single(s => s.EntityId == 14 && s.ScriptId == 0).Opcodes
            .First(o => o.Opcode == 0x80).Bytes;
        Equal(0x50, setDoor[1] & 0xF0, "KANE writes the temporary byte bank");
        Equal(WutaiBellDoorStateReader.DoorOpenFlagAddress,
            FieldNavigationObjectReader.AddressTemporaryFieldBankBase + setDoor[2],
            "the reader reads the byte KANE writes");
        Equal(WutaiBellDoorStateReader.DoorOpenValue, setDoor[3], "and treats KANE's value as open");

        var source = new FlevelDataSource(root);
        Equal(true, source.TryReadField(Pagoda, out var pagoda), "uutai2 readable");
        Equal(591, GatewayDestination(Ff7LzsDecoder.DecodeFieldFile(pagoda), 0), "uutai2 gateway 0 leads into the Hidden Room");
        Equal(true, source.TryReadField(591, out var hiddenRoom), "uttmpin4 readable");
        Equal(Pagoda, GatewayDestination(Ff7LzsDecoder.DecodeFieldFile(hiddenRoom), 1),
            "uttmpin4 gateway 1 is the return AD opens the door for");
        Console.WriteLine("Wutai Hidden Room entrance: the bell's native door flag and gateway match the installed archive.");
    }

    private static int GatewayDestination(byte[] field, int index)
    {
        var section = BitConverter.ToInt32(field, 6 + 7 * 4) + 4;
        return BitConverter.ToInt16(field, section + 0x38 + index * 24 + 18);
    }

    private static FieldNavigationTarget[] PagodaExits() =>
    [
        Gateway(0, 591, "Exit to Hidden Room", -865, -4998, -72, -778, -4998, -76),
        Gateway(1, 588, "Exit to Wutai, Main Mtn.", 677, -4378, 100, 655, -4560, 100),
        Gateway(2, 586, "Exit to Wutai, Godo's Pagoda", -119, -1843, 128, -7, -1843, 128),
        new(Pagoda, FieldNavigationCategory.Exits, "Exit to Wutai", 596, -5436, -56, "script-exit:587:17:579",
            TriggerEntityId: 17, CompletesOnArrival: true, DestinationFieldIds: [579],
            TriggerLine: new FieldNavigationTriggerLine(656, -5369, -35, 536, -5503, -77)),
    ];

    private static FieldNavigationTarget Gateway(int index, int destination, string label,
        int x1, int y1, int z1, int x2, int y2, int z2) =>
        new(Pagoda, FieldNavigationCategory.Exits, label, (x1 + x2) / 2, (y1 + y2) / 2, (z1 + z2) / 2,
            $"gateway:{Pagoda}:{index}:{destination}", CompletesOnArrival: true, DestinationFieldIds: [destination],
            TriggerLine: new FieldNavigationTriggerLine(x1, y1, z1, x2, y2, z2));

    private static ScriptedAddressSpace PagodaMemory(byte door)
    {
        var memory = new ScriptedAddressSpace();
        memory.Set(ModuleAddress, FieldPositionReader.FieldModule);
        memory.Set(FieldAddress, Pagoda & 0xFF, Pagoda >> 8);
        memory.Set(DoorAddress, door);
        return memory;
    }

    /// <summary>The argument list of the first call that starts with <paramref name="call"/>.</summary>
    private static string? ReadArguments(string text, string call)
    {
        var start = text.IndexOf(call, StringComparison.Ordinal);
        if (start < 0) return null;
        var depth = 0;
        for (var index = start + call.Length - 1; index < text.Length; index++)
        {
            if (text[index] == '(') depth++;
            else if (text[index] == ')' && --depth == 0) return text[(start + call.Length)..index];
        }

        return null;
    }

    private static string FindSourceRoot()
    {
        var configured = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT");
        if (!string.IsNullOrEmpty(configured) &&
            File.Exists(Path.Combine(configured, "Ff7.Accessibility.Steam2026X64", "Ff7.Accessibility.Steam2026X64.csproj")))
        {
            return configured;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Ff7.Accessibility.Steam2026X64", "Ff7.Accessibility.Steam2026X64.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repository root from " + AppContext.BaseDirectory + ".");
    }

    /// <summary>
    /// Guest memory whose bytes can be missing, or can change from one read to the next.
    /// A sequence yields its values in order and then keeps its last one.
    /// </summary>
    private sealed class ScriptedAddressSpace : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        private readonly Dictionary<uint, Queue<byte>> sequences = [];

        public HashSet<uint> Unreadable { get; } = [];

        public void Set(uint address, params byte[] values)
        {
            for (var index = 0; index < values.Length; index++)
            {
                bytes[address + (uint)index] = values[index];
                sequences.Remove(address + (uint)index);
            }
        }

        public void Sequence(uint address, params byte[] values) => sequences[address] = new Queue<byte>(values);

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                var address = virtualAddress + (uint)index;
                if (Unreadable.Contains(address))
                {
                    destination.Clear();
                    return false;
                }

                if (sequences.TryGetValue(address, out var sequence) && sequence.Count > 0)
                {
                    destination[index] = sequence.Count > 1 ? sequence.Dequeue() : sequence.Peek();
                    continue;
                }

                if (!bytes.TryGetValue(address, out var value))
                {
                    destination.Clear();
                    return false;
                }

                destination[index] = value;
            }

            return true;
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"wutai hidden room exit - {label}: expected {expected}, got {actual}");
        }
    }
}

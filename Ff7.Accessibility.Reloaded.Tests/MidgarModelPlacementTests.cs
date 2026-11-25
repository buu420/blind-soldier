using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Floor 65's Midgar model against the native blin65_1 and blin65_2 scripts, which
/// <see cref="CheckNativeChests"/> and <see cref="CheckNativeSlots"/> re-read from stock and every
/// reviewed Echo-S flevel whenever the data roots are set:
/// <list type="bullet">
/// <item>1[68] bits 3-7 is the one part being carried; 3[180] bits 3-7 are the filled slots.</item>
/// <item>PARTD has no prerequisite. PARTB, PARTC, PARTA and PARTE open only when their own slot or
/// the bottom slot is filled, and only while no part is carried; otherwise they say "Locked…".</item>
/// <item>CITYA-E accept any carried part into their empty slot, clear every carry bit and set their
/// own slot bit. TAKARA needs all five slots and writes 1[57] bit 3 and 1[224] = 66.</item>
/// </list>
/// Every legal order of chests and slots is walked. At every stage both rooms' Story must offer
/// exactly what the game accepts next, and never nothing while the puzzle can still progress.
/// </summary>
internal static class MidgarModelPlacementTests
{
    private const byte AllSlots = 0xF8;
    private const byte BottomSlot = 0x80;
    private const byte TopSlot = 0x10;
    private const int EventTable = 0x02500000;
    private const int KeycardAddress = 224;
    private const byte Keycard66 = 66;
    private const int NativeOrderCount = 816;
    private const string CarryLabel = "Carry the Midgar part into the model room";
    private const string NextChestLabel = "Return to the outer floor for the next unlocked chest";
    private const string KeycardReturnLabel = "Return to the outer floor and collect Keycard 66";
    private const string KeycardChestLabel = "Collect Keycard 66 from the completed Midgar model";
    private const string AfterKeycardReturnLabel = "Return to the outer floor and continue to floor 66";
    private const string ContinueLabel = "Continue to floor 66 with Keycard 66";
    private static readonly int Bank = FieldNavigationObjectReader.AddressFieldBankBase;
    private static readonly FieldPositionSnapshot OuterFloor = new(1, 248, 0, 120, -921, 0, 84, 0);
    private static readonly FieldPositionSnapshot ModelRoom = new(1, 249, 0, 0, -448, 0, 4, 0);

    private sealed record Chest(string Entity, int EntityId, int ModelId, int X, int Y,
        byte UnlockSlot, byte CarryBit, int CollectedAddress, byte CollectedMask, string Label);

    // Live model ids and positions are the ones the floor 65 Story sequence test uses.
    private static readonly Chest[] Chests =
    [
        new("PARTD", 18, 6, -996, -92, 0, 0x40, 57, 0x02,
            "Open the lower chest in the upper-left room for the first Midgar part"),
        new("PARTB", 16, 4, -872, -524, 0x40, 0x10, 56, 0x80,
            "Open the lower chest in the lower-left room for the second Midgar part"),
        new("PARTC", 17, 5, -915, 141, 0x20, 0x20, 57, 0x01,
            "Open the remaining chest in the upper-left room for the third Midgar part"),
        new("PARTA", 15, 3, -982, -681, 0x08, 0x08, 56, 0x40,
            "Open the remaining chest in the lower-left room for the fourth Midgar part"),
        new("PARTE", 19, 7, 1008, 858, TopSlot, 0x80, 57, 0x04,
            "Open the chest in the upper-right room for the final Midgar part")
    ];

    private static readonly Chest KeycardChest =
        new("TAKARA", 20, 8, 772, -4, AllSlots, 0, 57, 0x08, KeycardChestLabel);

    private sealed record Slot(byte Bit, string Name, string Entity, int X, int Y, int[] Triangles);

    private static readonly Slot[] Slots =
    [
        new(0x08, "upper-left", "CITYA", -342, 89, [75, 31]),
        new(TopSlot, "top", "CITYB", -149, 283, [68, 16]),
        new(0x20, "upper-right", "CITYC", 294, 170, [33, 6]),
        new(0x40, "bottom-right", "CITYD", 342, -194, [70, 34]),
        new(BottomSlot, "bottom", "CITYE", 128, -316, [26])
    ];

    private readonly record struct Stage(byte Carry, byte Filled, byte Collected56, byte Collected57, bool Keycard);

    internal static void Run()
    {
        EveryCarriedPartHasOnlyEmptySlots();
        var (orders, stages) = WalkEveryNativeOrder();
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            var echoes = (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_ECHO_S_DATA_ROOTS") ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var directory in new[] { root }.Concat(echoes))
            {
                CheckNativeChests(directory);
                CheckNativeSlots(directory);
            }
        }

        Console.WriteLine($"Midgar model: {orders} native chest and slot orders, {stages} distinct stages, " +
            "each with exactly the next native step in Story.");
    }

    // CITYA-E accept any carried part, not one assigned to a particular socket, including
    // arrangements no legal order reaches. Collected-chest flags are not slot prerequisites.
    private static void EveryCarriedPartHasOnlyEmptySlots()
    {
        var floor = new Floor65();
        foreach (var carry in new byte[] { 0x08, 0x10, 0x20, 0x40, 0x80 })
        foreach (var filled in Enumerable.Range(0, 31).Select(value => (byte)(value << 3)))
        {
            floor.Set(new Stage(carry, filled, 0, 0, false));
            RequirePlacements(floor.ModelTargets(), filled, $"carried 0x{carry:X2}, filled 0x{filled:X2}");
        }

        floor.Set(new Stage(0, 0x78, 0xC0, 0x07, false));
        if (floor.ModelTargets().Any(target => target.Label.StartsWith("Place", StringComparison.Ordinal)))
            throw new InvalidOperationException("A collected chest is not evidence of a currently carried piece.");
    }

    private static (int Orders, int Stages) WalkEveryNativeOrder()
    {
        var floor = new Floor65();
        var checkedStages = new HashSet<Stage>();
        var orders = 0;
        var earlyTopOrBottom = 0;
        var finalChestBeforeOthers = 0;

        void Check(Stage stage, string path)
        {
            if (checkedStages.Add(stage)) CheckStage(floor, stage, path);
        }

        void Visit(Stage stage, string path, byte firstSlot)
        {
            Check(stage, path);
            if (stage.Carry != 0)
            {
                foreach (var slot in Slots.Where(candidate => (stage.Filled & candidate.Bit) == 0))
                    Visit(stage with { Carry = 0, Filled = (byte)(stage.Filled | slot.Bit) },
                        $"{path} > {slot.Name} slot", firstSlot == 0 ? slot.Bit : firstSlot);
                return;
            }

            if (stage.Filled == AllSlots)
            {
                orders++;
                if (firstSlot is TopSlot or BottomSlot) earlyTopOrBottom++;
                Check(stage with { Keycard = true }, path + " > Keycard 66");
                return;
            }

            var openable = Openable(stage).ToArray();
            if (openable.Length == 0)
                throw new InvalidOperationException($"The native puzzle cannot progress after {path}.");
            foreach (var chest in openable)
            {
                if (chest.CarryBit == 0x80 && Chests.Any(other => other != chest && !IsCollected(stage, other)))
                    finalChestBeforeOthers++;
                Visit(Open(stage, chest), $"{path} > {chest.Entity}", firstSlot);
            }
        }

        Visit(default, "start", 0);
        if (orders != NativeOrderCount)
            throw new InvalidOperationException($"Expected {NativeOrderCount} native Midgar model orders, walked {orders}.");
        if (earlyTopOrBottom == 0 || finalChestBeforeOthers == 0)
            throw new InvalidOperationException("The walk must include a first part in the top or bottom slot and PARTE before the last chest.");
        return (orders, checkedStages.Count);
    }

    private static void CheckStage(Floor65 floor, Stage stage, string path)
    {
        floor.Set(stage);
        string[] expectedOuter;
        string? expectedModel;
        if (stage.Keycard)
        {
            expectedOuter = [ContinueLabel];
            expectedModel = AfterKeycardReturnLabel;
        }
        else if (stage.Carry != 0)
        {
            // Every chest says "Locked…" while a part is carried.
            expectedOuter = [CarryLabel];
            expectedModel = null;
        }
        else if (stage.Filled == AllSlots)
        {
            expectedOuter = [KeycardChestLabel];
            expectedModel = KeycardReturnLabel;
        }
        else
        {
            expectedOuter = Openable(stage).Select(chest => chest.Label).ToArray();
            expectedModel = NextChestLabel;
        }

        var outer = floor.OuterLabels();
        if (!outer.Order(StringComparer.Ordinal).SequenceEqual(expectedOuter.Order(StringComparer.Ordinal)))
            throw new InvalidOperationException(
                $"Outer floor Story after {path}: expected {Describe(expectedOuter)}, read {Describe(outer)}.");

        var model = floor.ModelTargets();
        if (expectedModel is null)
        {
            RequirePlacements(model, stage.Filled, path);
        }
        else if (model.Count != 1 || model[0].Label != expectedModel)
        {
            throw new InvalidOperationException(
                $"Model room Story after {path}: expected {expectedModel}, read {Describe(model.Select(target => target.Label))}.");
        }
    }

    // A carried part may go into any empty slot, and only an empty slot.
    private static void RequirePlacements(IReadOnlyList<FieldNavigationTarget> targets, byte filled, string context)
    {
        if (targets.Count == 0)
            throw new InvalidOperationException($"No model placement for {context}.");
        foreach (var target in targets)
        {
            var slot = Slots.SingleOrDefault(candidate => candidate.X == target.X && candidate.Y == target.Y);
            if (!target.Label.StartsWith("Place ", StringComparison.Ordinal) || slot is null || (filled & slot.Bit) != 0)
                throw new InvalidOperationException($"Story offered '{target.Label}' for {context}, which is not an empty native slot.");
        }
    }

    private static IEnumerable<Chest> Openable(Stage stage) =>
        stage.Carry != 0
            ? []
            : Chests.Where(chest => !IsCollected(stage, chest) &&
                (chest.UnlockSlot == 0 || (stage.Filled & (chest.UnlockSlot | BottomSlot)) != 0));

    private static bool IsCollected(Stage stage, Chest chest) =>
        ((chest.CollectedAddress == 56 ? stage.Collected56 : stage.Collected57) & chest.CollectedMask) != 0;

    private static Stage Open(Stage stage, Chest chest) => stage with
    {
        Carry = chest.CarryBit,
        Collected56 = chest.CollectedAddress == 56 ? (byte)(stage.Collected56 | chest.CollectedMask) : stage.Collected56,
        Collected57 = chest.CollectedAddress == 57 ? (byte)(stage.Collected57 | chest.CollectedMask) : stage.Collected57
    };

    private static string Describe(IEnumerable<string> labels)
    {
        var text = string.Join(" | ", labels);
        return text.Length == 0 ? "none" : text;
    }

    private sealed class Floor65
    {
        private readonly Dictionary<int, byte> memory = new();
        private readonly FieldStoryTargetReader reader;

        internal Floor65()
        {
            WriteUInt16(Bank, 263);
            WriteUInt32(FieldNavigationObjectReader.AddressFieldEventDataPtr, EventTable);
            memory[FieldPositionReader.AddressFieldNumModels] = 9;
            foreach (var chest in Chests.Append(KeycardChest))
            {
                memory[FieldNavigationObjectReader.AddressFieldModelIdArray + chest.EntityId] = (byte)chest.ModelId;
                var model = EventTable + chest.ModelId * FieldNavigationObjectReader.FieldEventDataStride;
                memory[model + FieldNavigationObjectReader.VisibilityOffset] = 1;
                WriteUInt32(model + FieldNavigationObjectReader.PositionXOffset,
                    unchecked((uint)(chest.X * FieldNavigationObjectReader.ModelPositionFixedPointScale)));
                WriteUInt32(model + FieldNavigationObjectReader.PositionYOffset,
                    unchecked((uint)(chest.Y * FieldNavigationObjectReader.ModelPositionFixedPointScale)));
                WriteUInt32(model + FieldNavigationObjectReader.PositionZOffset, 0);
            }

            reader = new FieldStoryTargetReader(ReadInt32, ReadInt16, ReadByte,
                FieldStoryEventCatalog.CreateAllFields(), _ => true);
        }

        // TAKARA also sets 1[68] bit 1, outside the carried-part bits, and an earlier keycard
        // level stands in 1[224] until then.
        internal void Set(Stage stage)
        {
            memory[Bank + 68] = (byte)(stage.Carry | (stage.Keycard ? 0x02 : 0));
            memory[Bank + 56] = stage.Collected56;
            memory[Bank + 57] = (byte)(stage.Collected57 | (stage.Keycard ? KeycardChest.CollectedMask : 0));
            memory[Bank + KeycardAddress] = stage.Keycard ? Keycard66 : (byte)65;
            memory[Bank + 0x100 + 180] = stage.Filled;
        }

        internal string[] OuterLabels() => reader.ReadTargets(OuterFloor).Select(target => target.Label).ToArray();

        internal IReadOnlyList<FieldNavigationTarget> ModelTargets() => reader.ReadTargets(ModelRoom);

        private byte ReadByte(int address) => memory.GetValueOrDefault(address);

        private short ReadInt16(int address) => unchecked((short)(ReadByte(address) | (ReadByte(address + 1) << 8)));

        private int ReadInt32(int address) =>
            ReadByte(address) | (ReadByte(address + 1) << 8) | (ReadByte(address + 2) << 16) | (ReadByte(address + 3) << 24);

        private void WriteUInt16(int address, ushort value)
        {
            memory[address] = (byte)value;
            memory[address + 1] = (byte)(value >> 8);
        }

        private void WriteUInt32(int address, uint value)
        {
            for (var index = 0; index < 4; index++) memory[address + index] = (byte)(value >> (index * 8));
        }
    }

    private static void CheckNativeChests(string root)
    {
        var program = ReadProgram(root, 248, out _);
        foreach (var chest in Chests)
        {
            var code = EntityCode(program, chest.Entity, root);
            Require(code, Biton(1, 68, BitIndex(chest.CarryBit)), $"{chest.Entity} gives its carried part", root);
            Require(code, Biton(1, chest.CollectedAddress, BitIndex(chest.CollectedMask)), $"{chest.Entity} records its chest", root);
            if (chest.UnlockSlot == 0)
            {
                if (code.Any(op => op.Id == 0x14 && op.Bytes.Length >= 3 && op.Bytes[1] >> 4 == 3 && op.Bytes[2] == 180))
                    throw new InvalidOperationException($"{chest.Entity} gained a model-slot prerequisite: {root}");
                continue;
            }

            Require(code, Ifub(3, 180, chest.UnlockSlot, 6), $"{chest.Entity} opens after its own model slot", root);
            Require(code, Ifub(3, 180, BottomSlot, 6), $"{chest.Entity} opens after the bottom model slot", root);
            for (var bit = 3; bit <= 7; bit++)
                Require(code, Ifub(1, 68, bit, 9), $"{chest.Entity} is locked while carried-part bit {bit} is set", root);
        }

        var keycard = EntityCode(program, KeycardChest.Entity, root);
        foreach (var slot in Slots)
            Require(keycard, Ifub(3, 180, slot.Bit, 6), $"Keycard 66 needs the {slot.Name} slot", root);
        Require(keycard, Biton(1, 57, BitIndex(KeycardChest.CollectedMask)), "Keycard 66 records its chest", root);
        Require(keycard, [0x80, 0x10, KeycardAddress, Keycard66], "Keycard 66 writes the keycard level", root);
        Console.WriteLine($"PASS native Midgar chests and Keycard 66: {root}");
    }

    private static void CheckNativeSlots(string root)
    {
        var program = ReadProgram(root, 249, out var data);
        foreach (var slot in Slots)
        {
            var code = EntityCode(program, slot.Entity, root);
            Require(code, Biton(3, 180, BitIndex(slot.Bit)), $"{slot.Entity} fills the {slot.Name} slot", root);
            for (var bit = 3; bit <= 7; bit++)
            {
                Require(code, Ifub(1, 68, bit, 9), $"{slot.Entity} accepts carried-part bit {bit}", root);
                Require(code, [0x83, 0x10, 68, (byte)bit], $"{slot.Entity} clears carried-part bit {bit}", root);
            }

            foreach (var triangle in slot.Triangles)
                Require(code, [0x16, 0x60, 5, 0, (byte)triangle, (byte)(triangle >> 8), 0],
                    $"{slot.Entity} activates on native triangle {triangle}", root);
        }

        const int address = 0x02000000;
        var mesh = new FieldWalkmeshReader(
            at => at == FieldWalkmeshReader.AddressFieldDataPtr ? address :
                at >= address && at + 4 <= address + data.Length ? BitConverter.ToInt32(data, at - address) : 0,
            at => at >= address && at + 2 <= address + data.Length ? BitConverter.ToInt16(data, at - address) : (short)0);
        var planner = new FieldWalkmeshRoutePlanner(mesh);
        var from = new FieldPositionSnapshot(1, 249, 0, 3, -415, -10, 4, 128);
        var placements = FieldStoryEventCatalog.CreateAllFields()
            .Where(definition => definition.FieldId == 249 && definition.Label.StartsWith("Place ", StringComparison.Ordinal))
            .ToArray();
        if (placements.Length != 10)
            throw new InvalidOperationException($"Expected five preferred and five empty-slot placements, found {placements.Length}.");
        foreach (var placement in placements)
        {
            var slot = Slots.SingleOrDefault(candidate => candidate.X == placement.X && candidate.Y == placement.Y)
                ?? throw new InvalidOperationException($"{placement.Label} is not at a native model slot.");
            if (placement.CompletionPlayerTriangles is not { } triangles ||
                !triangles.Order().SequenceEqual(slot.Triangles.Order()))
                throw new InvalidOperationException($"{placement.Label} must arrive on {slot.Entity}'s native triangles.");
            var target = new FieldNavigationTarget(249, FieldNavigationCategory.Story, placement.Label,
                placement.X, placement.Y, placement.Z, placement.Label, CompletesOnArrival: false,
                CompletionTriangles: triangles);
            if (!planner.TryBuildRoute(from, target, out var plan) || !triangles.Contains(plan.TargetTriangle))
                throw new InvalidOperationException($"Native model-slot route {root}: {placement.Label}: {planner.LastDiagnostic}");
        }

        Console.WriteLine($"PASS native model-slot routes: {root}");
    }

    private static FieldScriptProgram ReadProgram(string root, int fieldId, out byte[] data)
    {
        if (!new FlevelDataSource(root).TryReadField(fieldId, out var encoded))
            throw new InvalidOperationException($"Native field {fieldId} unavailable: {root}");
        data = Ff7LzsDecoder.DecodeFieldFile(encoded);
        var sectionOffset = BitConverter.ToInt32(data, 6);
        var section = data.AsSpan(sectionOffset + 4, BitConverter.ToInt32(data, sectionOffset)).ToArray();
        return FieldScriptProgram.TryParse(section, out var diagnostic)
            ?? throw new InvalidOperationException($"Native field {fieldId} script: {diagnostic}");
    }

    private static IReadOnlyList<FieldScriptInstruction> EntityCode(FieldScriptProgram program, string name, string root)
    {
        var entity = program.EntityNames.ToList().FindIndex(entityName => string.Equals(entityName, name, StringComparison.Ordinal));
        if (entity < 0) throw new InvalidOperationException($"Native entity {name} is missing: {root}");
        return Enumerable.Range(0, FieldScriptProgram.SlotCount)
            .SelectMany(slot => program.Script(entity, slot))
            .DistinctBy(op => op.Offset)
            .ToArray();
    }

    private static void Require(IReadOnlyList<FieldScriptInstruction> code, byte[] prefix, string fact, string root)
    {
        if (!code.Any(op => op.Bytes.Length >= prefix.Length && op.Bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix)))
            throw new InvalidOperationException($"Native fact changed ({fact}): {root}");
    }

    private static byte[] Ifub(int bank, int address, int value, int comparison) =>
        [0x14, (byte)(bank << 4), (byte)address, (byte)value, (byte)comparison];

    private static byte[] Biton(int bank, int address, int bit) => [0x82, (byte)(bank << 4), (byte)address, (byte)bit];

    private static int BitIndex(byte mask) => System.Numerics.BitOperations.TrailingZeroCount(mask);
}

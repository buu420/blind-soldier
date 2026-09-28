using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Great Glacier's exits and its map screen.
///
/// <para>Every glacier screen has the map name "Great Glacier", so the exits all read "Exit
/// to Great Glacier". Most of them lead into one of the six passage screens (move_s, move_i,
/// move_f, move_r, move_u, move_d, fields 670..675), which send the party on according to
/// bank 1 byte 184. A path from an ordinary screen always writes the same value, so where it
/// finally comes out is fixed and is named here. Inside a passage the way on depends on that
/// byte, so those exits are named only when the host reads it.</para>
///
/// <para>hyoumap (669) is the Glacier Map the [SWITCH] key opens. It shows one static
/// picture, speaks nothing and waits for OK, Cancel or Switch.</para>
/// </summary>
internal static class GreatGlacierExitLabelTests
{
    private static readonly uint ModuleAddress = FieldPositionReader.AddressCurrentModule;
    private static readonly uint FieldAddress = FieldPositionReader.AddressFieldId;
    private static readonly uint CorridorStateAddress = FieldNavigationObjectReader.AddressFieldBankBase + 184;

    private static readonly string[] TreasureWords =
        ["Mind Source", "Potion", "Safety Bit", "Elixir", "Added Cut", "All", "Alexander", "Materia", "materia", "treasure"];

    public static void Run()
    {
        OrdinaryScreenExitsNameWhereThePathComesOut();
        PassageExitsAreNamedOnlyFromTheLiveState();
        NoLabelGivesAwayATreasure();
        TheCorridorStateIsReadCoherently();
        TheMapScreenIsDescribedOnEntryAndOnRequest();
        Console.WriteLine("Great Glacier exit labels and map screen checked.");
    }

    private static void OrdinaryScreenExitsNameWhereThePathComesOut()
    {
        var labels = Resolver(() => null);
        Equal("Exit to Great Glacier, passage toward the snow pillars", Label(labels, 663, 10, 670),
            "hyou4's line02 goes through move_s and move_i to hyou8_1");
        Equal("Exit to Great Glacier, frozen lake", Label(labels, 664, 13, 665),
            "hyou5_1's line03 goes straight onto the lake");
        Equal("Exit to Great Glacier, passage toward the hot spring", Label(labels, 679, 5, 674),
            "hyou9's line01 goes through move_u to hyou10");
        Equal("Exit to Great Glacier, passage toward the snowfield", Label(labels, 681, 4, 670),
            "hyou11's line00 goes through move_s onto the snowfield");
        Equal("Exit to Great Glacier, passage toward the Ice Gate", Label(labels, 660, 4, 673),
            "hyou3's line00 goes back through move_r to hyou1");
        Equal("Leave the glacier for the world map", Label(labels, 658, 7, 48),
            "hyou1's line03 is MAPJUMP 48, a world-map entry with no map name");
        Equal("Exit to Frostbite Cave", Label(labels, 660, 7, 661),
            "a screen with its own map name keeps it");
        Equal("Exit to Cave", Label(labels, 677, 10, 678), "a cave keeps its own map name");
    }

    private static void PassageExitsAreNamedOnlyFromTheLiveState()
    {
        int? state = 74;
        var labels = Resolver(() => state);
        Equal("Exit to Great Glacier, passage toward the hot spring", Label(labels, 675, 8, 675, 680),
            "move_d at 74: line01 leads through the next passage to hyou10");
        Equal("Exit to Great Glacier, rocky crossroads", Label(labels, 675, 4, 679, 680),
            "move_d at 74: line00 opens straight back onto hyou9");
        state = 76;
        Equal("Exit to Great Glacier, hot spring", Label(labels, 675, 4, 679, 680),
            "move_d at 76: line00 opens straight onto hyou10");
        Equal("Exit to Great Glacier, passage toward the snowfield", Label(labels, 675, 8, 670),
            "move_d at 76: line01 goes through move_s to the snowfield");
        state = null;
        Equal("Exit to Great Glacier", Label(labels, 675, 8, 670),
            "without the live state a passage exit is not guessed");
        state = 200;
        Equal("Exit to Great Glacier", Label(labels, 675, 8, 670), "a state no script writes is not guessed");
        Equal("Exit to Great Glacier", Label(Resolver(() => throw new InvalidOperationException("torn read")), 675, 8, 670),
            "a failing state reader falls back to the plain name");
        Equal("Exit to Great Glacier", Label(new FieldExitLabelResolver(Names, () => "Great Glacier"), 675, 8, 670),
            "a host that does not read the state keeps the plain name");
    }

    private static void NoLabelGivesAwayATreasure()
    {
        foreach (var label in GreatGlacierExitLabels.AllLabels())
        {
            foreach (var word in TreasureWords)
            {
                Equal(false, label.Contains(word, StringComparison.Ordinal), $"'{label}' names no treasure");
            }
        }
    }

    private static void TheCorridorStateIsReadCoherently()
    {
        var memory = Passage(675, 75);
        Equal<int?>(75, new GreatGlacierCorridorStateReader(memory).Read(), "move_d's bank 1 byte 184");
        Equal<int?>(null, new GreatGlacierCorridorStateReader(Passage(664, 75)).Read(), "outside a passage it is not read");
        var world = Passage(675, 75);
        world.Set(ModuleAddress, 3);
        Equal<int?>(null, new GreatGlacierCorridorStateReader(world).Read(), "outside the field module");
        var changing = Passage(675, 74);
        changing.Sequence(CorridorStateAddress, 74, 75);
        Equal<int?>(null, new GreatGlacierCorridorStateReader(changing).Read(), "a value that changes between reads");
        var unreadable = Passage(675, 75);
        unreadable.Unreadable.Add(CorridorStateAddress);
        Equal<int?>(null, new GreatGlacierCorridorStateReader(unreadable).Read(), "an unreadable byte");
    }

    private static void TheMapScreenIsDescribedOnEntryAndOnRequest()
    {
        var readout = new GreatGlacierMapScreenReadout();
        Equal<string?>(null, readout.Observe(658), "no map outside hyoumap");
        Equal(GreatGlacierMapScreen.Description, readout.Observe(GreatGlacierMapScreen.FieldId), "opening the map describes it");
        Equal<string?>(null, readout.Observe(GreatGlacierMapScreen.FieldId), "once per opening");
        Equal<string?>(null, readout.Observe(658), "closing it says nothing");
        Equal(GreatGlacierMapScreen.Description, readout.Observe(GreatGlacierMapScreen.FieldId), "each opening is described");
        Contains(GreatGlacierMapScreen.Description, "Glacier Map.", "the screen is named first");
        Contains(GreatGlacierMapScreen.Description, "Press OK, Cancel or Switch to close the map.", "the native close keys");
        Contains(GreatGlacierMapScreen.Description, "no mark showing where you are", "the picture has no position marker");
        Equal(true, GreatGlacierMapScreen.Description.EndsWith("Press OK, Cancel or Switch to close the map.", StringComparison.Ordinal),
            "the controls come last, where a repeat ends");
    }

    /// <summary>
    /// Every table entry rebuilt from the installed scripts: each LINE's MAPJUMP and the bank 1
    /// byte 184 it leaves, followed through the passage screens by the one line that does not
    /// lead back, to the screen it comes out in. And hyoumap's controls.
    /// </summary>
    public static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return;
        var catalog = new FieldScriptNavigationCatalog(root);
        var fields = Enumerable.Range(658, 27).Where(f => f != 669).ToArray();
        var scripts = fields.ToDictionary(f => f, f => catalog.ReadAllScriptOpcodes(f));
        var lines = fields.ToDictionary(f => f, f => scripts[f]
            .Where(s => s.ScriptId == 0 && s.Opcodes.Any(o => o.Opcode == 0xD0)).Select(s => s.EntityId).ToArray());

        (int Field, int State)? Exit(int field, int entity, int state)
        {
            for (var slot = 1; slot <= 6; slot++)
            {
                if (!scripts[field].Any(s => s.EntityId == entity && s.ScriptId == slot)) continue;
                var bank = new Dictionary<int, int> { [184] = state };
                if (Run(scripts[field], entity, slot, bank, 0) is { } jump) return (jump, bank[184]);
            }

            return null;
        }

        int Final(int field, int state, (int Field, int State) came)
        {
            var seen = new HashSet<(int, int)>();
            while (GreatGlacierExitLabels.IsPassage(field))
            {
                Equal(true, seen.Add((field, state)), $"the passage chain loops at {field}/{state}");
                var outs = lines[field].Select(e => Exit(field, e, state)).OfType<(int Field, int State)>().Distinct().ToList();
                var on = outs.Where(o => o != came).ToList();
                if (on.Count > 1) on = on.Where(o => o.Field != came.Field).ToList();
                Equal(1, on.Count, $"passage {field}/{state} has one way on");
                came = (field, state);
                (field, state) = on[0];
            }

            return field;
        }

        var fixedCount = 0;
        var producedFixed = new HashSet<(int, int)>();
        var producedCorridor = new HashSet<(int, int, int)>();
        var states = new HashSet<(int, int)> { (670, 52), (670, 53), (670, 55) }; // the snowfield's edges (wm3.ev)
        foreach (var field in fields.Where(f => !GreatGlacierExitLabels.IsPassage(f)))
        {
            foreach (var entity in lines[field])
            {
                if (Exit(field, entity, 0) is not { } next) continue;
                var final = GreatGlacierExitLabels.IsPassage(next.Field) ? Final(next.Field, next.State, (field, 0)) : next.Field;
                Equal<int?>(final, GreatGlacierExitLabels.FixedFinal(field, entity), $"{field} LINE {entity} comes out in {final}");
                if (GreatGlacierExitLabels.IsPassage(next.Field)) states.Add(next);
                producedFixed.Add((field, entity));
                fixedCount++;
            }
        }

        var pending = new Queue<(int, int)>(states);
        while (pending.Count > 0)
        {
            var (field, state) = pending.Dequeue();
            foreach (var entity in lines[field])
            {
                if (Exit(field, entity, state) is { } next && GreatGlacierExitLabels.IsPassage(next.Field) && states.Add(next))
                    pending.Enqueue(next);
            }
        }

        var corridorCount = 0;
        foreach (var (field, state) in states)
        {
            foreach (var entity in lines[field])
            {
                if (Exit(field, entity, state) is not { } next) continue;
                var final = GreatGlacierExitLabels.IsPassage(next.Field) ? Final(next.Field, next.State, (field, state)) : next.Field;
                Equal<(int, int)?>((next.Field, final), GreatGlacierExitLabels.CorridorFinal(field, state, entity),
                    $"passage {field}/{state} LINE {entity}");
                producedCorridor.Add((field, state, entity));
                corridorCount++;
            }
        }

        Equal("", string.Join(", ", GreatGlacierExitLabels.FixedKeys.Where(k => !producedFixed.Contains(k))),
            "no ordinary-screen entry the scripts do not produce");
        Equal("", string.Join(", ", GreatGlacierExitLabels.CorridorKeys.Where(k => !producedCorridor.Contains(k))),
            "no passage entry the scripts do not produce");

        // Every scripted exit the catalog offers on a glacier screen has a name to take.
        var unlabeled = fields
            .SelectMany(f => catalog.ReadField(f).Exits)
            .Where(e => e.StableId.StartsWith("script-exit:", StringComparison.Ordinal))
            .Where(e => !GreatGlacierExitLabels.IsPassage(e.FieldId))
            .Where(e => GreatGlacierExitLabels.FixedFinal(e.FieldId, e.TriggerEntityId) is null)
            .Select(e => e.StableId)
            .ToArray();
        Equal(0, unlabeled.Length, $"ordinary-screen exits without a known destination: {string.Join(", ", unlabeled)}");

        // hyoumap: nothing spoken, nothing drawn on demand, control off, and OK, Cancel or
        // Switch (IFKEYON 0x02E0) sends the party back.
        var map = catalog.ReadAllScriptOpcodes(GreatGlacierMapScreen.FieldId);
        var ops = map.SelectMany(s => s.Opcodes).ToArray();
        Equal(false, ops.Any(o => o.Opcode is 0x40 or 0x48), "the map screen shows no message");
        Equal(false, ops.Any(o => o.Opcode is 0xE0 or 0xE1), "the map picture never changes");
        Equal(true, ops.Any(o => o.Opcode == 0x33 && o.Bytes[1] == 1), "the party cannot move on the map");
        Equal(true, ops.Any(o => o.Opcode == 0x31 && BitConverter.ToUInt16(o.Bytes.ToArray(), 1) == 0x02E0),
            "OK (0x0220), Cancel (0x0040) or Switch (0x0080) closes it");
        Console.WriteLine($"Great Glacier exit labels: {fixedCount} ordinary-screen and {corridorCount} passage LINE exits rebuilt from the installed scripts over {states.Count} passage states.");
    }

    private static int? Run(IReadOnlyList<FieldScriptDefinition> scripts, int entity, int slot, Dictionary<int, int> bank, int depth)
    {
        if (depth > 8) return null;
        var script = scripts.SingleOrDefault(s => s.EntityId == entity && s.ScriptId == slot);
        if (script.Opcodes is null) return null;
        var byOffset = script.Opcodes.ToDictionary(o => o.ByteIndex);
        var offset = script.Opcodes[0].ByteIndex;
        for (var steps = 0; steps < 3000 && byOffset.TryGetValue(offset, out var op); steps++)
        {
            var b = op.Bytes.ToArray();
            var next = offset + b.Length;
            switch (op.Opcode)
            {
                case 0x00:
                    return null;
                case 0x01 or 0x02 or 0x03:
                    if (Run(scripts, b[1], b[2] & 31, bank, depth + 1) is { } called) return called;
                    break;
                case 0x10: next = offset + 1 + b[1]; break;
                case 0x11: next = offset + 1 + BitConverter.ToUInt16(b, 1); break;
                case 0x14 or 0x15:
                    // Bank 1 is the savemap; 5 is the field's temporary bank, which starts at 0 and
                    // holds what the script itself wrote (an ASK keeps the default it was given).
                    int Value(int nibble, int raw) => nibble switch
                    {
                        0 => raw,
                        1 => bank.GetValueOrDefault(raw),
                        5 => bank.GetValueOrDefault(1000 + raw),
                        _ => 0
                    };
                    var left = Value(b[1] >> 4, b[2]);
                    var right = Value(b[1] & 15, b[3]);
                    var passes = b[4] switch
                    {
                        0 => left == right, 1 => left != right, 2 => left > right, 3 => left < right,
                        4 => left >= right, 5 => left <= right, 6 => (left & right) != 0,
                        9 => ((left >> right) & 1) == 1, 10 => ((left >> right) & 1) == 0, _ => false
                    };
                    if (!passes) next = offset + 5 + (op.Opcode == 0x15 ? BitConverter.ToUInt16(b, 5) : b[5]);
                    break;
                case 0x80:
                    var value = (b[1] & 15) switch { 0 => b[3], 1 => bank.GetValueOrDefault(b[3]), 5 => bank.GetValueOrDefault(1000 + b[3]), _ => 0 };
                    if (b[1] >> 4 == 1) bank[b[2]] = value;
                    if (b[1] >> 4 == 5) bank[1000 + b[2]] = value;
                    break;
                case 0x60:
                    return BitConverter.ToUInt16(b, 1);
            }

            offset = next;
        }

        return null;
    }

    private static FieldExitLabelResolver Resolver(Func<int?> state) =>
        new(Names, () => "Great Glacier", readGlacierCorridorState: state);

    private static FieldMapNameResolution Names(int field) => field switch
    {
        >= 658 and <= 684 and not (661 or 662 or 666 or 678 or 682 or 684) => FieldMapNameResolution.Known(["Great Glacier"]),
        661 or 662 => FieldMapNameResolution.Known(["Frostbite Cave"]),
        666 or 678 or 682 or 684 => FieldMapNameResolution.Known(["Cave"]),
        _ => FieldMapNameResolution.Unknown
    };

    private static string Label(FieldExitLabelResolver resolver, int field, int entity, params int[] destinations) =>
        resolver.Resolve([new FieldNavigationTarget(field, FieldNavigationCategory.Exits, "Scripted exit", 0, 0, 0,
            $"script-exit:{field}:{entity}:{string.Join(',', destinations.Order())}", TriggerEntityId: entity,
            DestinationFieldIds: destinations.Order().ToArray())]).Single().Label;

    private static PassageMemory Passage(int field, int state)
    {
        var memory = new PassageMemory();
        memory.Set(ModuleAddress, FieldPositionReader.FieldModule);
        memory.Set(FieldAddress, (byte)(field & 0xFF), (byte)(field >> 8));
        memory.Set(CorridorStateAddress, (byte)state);
        return memory;
    }

    private sealed class PassageMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        private readonly Dictionary<uint, Queue<byte>> sequences = [];
        public HashSet<uint> Unreadable { get; } = [];

        public void Set(uint address, params byte[] values)
        {
            for (var index = 0; index < values.Length; index++) bytes[address + (uint)index] = values[index];
        }

        public void Set(uint address, int value) => Set(address, (byte)value);

        public void Sequence(uint address, params byte[] values) => sequences[address] = new Queue<byte>(values);

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                var address = virtualAddress + (uint)index;
                if (Unreadable.Contains(address)) return false;
                if (sequences.TryGetValue(address, out var queue) && queue.Count > 0)
                {
                    destination[index] = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
                    continue;
                }

                if (!bytes.TryGetValue(address, out var value)) return false;
                destination[index] = value;
            }

            return true;
        }
    }

    private static void Contains(string text, string expected, string message)
    {
        if (!text.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Great Glacier exit labels: {message}. Expected '{expected}' in '{text}'.");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Great Glacier exit labels: {message}. Expected {expected}, got {actual}.");
    }
}

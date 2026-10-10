using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class MidgarTunnelStoryTests
{
    internal static void Run(Func<int, FieldWalkmeshReader>? createWalkmeshReader = null)
    {
        foreach (var (_, check) in Cases(createWalkmeshReader)) check();
    }

    internal static IEnumerable<(string Name, Action Check)> Cases(Func<int, FieldWalkmeshReader>? createWalkmeshReader)
    {
        yield return ("entry ladder gives native manual guidance", EntryLadderGivesManualGuidance);
        yield return ("pre-encounter tunnel visits have onward guidance", BeforeEncounterHasOnwardGuidance);
        yield return ("post-encounter return visits lead to the bridge", AfterEncounterLeadsToTheBridge);
        yield return ("disabled lines and invalid section reads have no guessed route", DisabledAndInvalidSectionsHaveNoRoute);
        if (Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") is not { Length: > 0 } root) yield break;
        yield return ("tunnel lines bind to installed native scripts", () => LinesBindToNativeScripts(root));
        if (createWalkmeshReader is not null)
            yield return ("tunnel guidance routes from native arrivals", () => RoutesFromNativeArrivals(createWalkmeshReader));
    }

    private static void EntryLadderGivesManualGuidance()
    {
        foreach (var moment in new[] { 1601, 1602 })
        {
            var state = new State(moment, 4, moment == 1602);
            var target = Only(state, 735, 9);
            Contains(target.ManualNavigationGuidance, "Hold Up", "native ladder input is spoken");
            var controller = new FieldNavigationController(new FieldNavigationTargetSource([target]));
            var position = Position(735, 9);
            _ = controller.HandleAction(FieldNavigationAction.NextCategory, position, new(0));
            while (controller.CurrentCategory != FieldNavigationCategory.Story)
                _ = controller.HandleAction(FieldNavigationAction.NextCategory, position, new(0));
            var result = controller.HandleAction(FieldNavigationAction.ToggleBeacon, position, new(0));
            Contains(result?.Speech, "Hold Up", "pursuit explains the ordinary manual control");
            Equal(false, controller.BeaconEnabled, "manual ladder instruction cannot start a walking route");
            Equal(false, controller.TryResolveAutomaticInput(position, new(0), 80, out var input),
                "the entry ladder cannot own automatic movement");
            Equal(FieldNavigationInput.None, input, "manual ladder produces no game input");
        }
    }

    private static void BeforeEncounterHasOnwardGuidance()
    {
        foreach (var section in Enumerable.Range(0, 10).Select(n => n * 2))
        {
            var state = new State(1601, section, false);
            Equal(section <= 2 ? 16 : 15, Entity(Only(state, 736)),
                $"tunnel_4 section {section} leads back to the main route before the encounter");
        }
        foreach (var section in Enumerable.Range(0, 9).Select(n => n * 2 + 1))
        {
            var state = new State(1601, section, false);
            Equal(section <= 3 ? 12 : 11, Entity(Only(state, 737)),
                $"tunnel_5 section {section} approaches the actual encounter floor");
        }
        Equal(18, Entity(Only(new State(1601, 4, false), 778)),
            "the native crossing is still the only step when the encounter is ahead");
    }

    private static void AfterEncounterLeadsToTheBridge()
    {
        foreach (var section in Enumerable.Range(0, 10).Select(n => n * 2))
            Equal(section <= 2 ? 16 : 15, Entity(Only(new State(1602, section, true), 736)),
                $"tunnel_4 section {section} returns to the bridge's own section");
        foreach (var section in Enumerable.Range(0, 9).Select(n => n * 2 + 1))
            Equal(section == 1 ? 12 : section == 3 ? 10 : 11,
                Entity(Only(new State(1602, section, true), 737)),
                $"tunnel_5 section {section} uses the onward route after the encounter");
        Equal(19, Entity(Only(new State(1602, 4, true), 778)),
            "both fighting and declining settle the native encounter before climbing on");
        Equal(0, new State(1602, 4, false).Reader().ReadTargets(Position(778)).Count,
            "a chapter number alone cannot skip an unsettled encounter");
    }

    private static void DisabledAndInvalidSectionsHaveNoRoute()
    {
        var state = new State(1601, 5, false) { DisabledLine = 11 };
        Equal(0, state.Reader().ReadTargets(Position(737)).Count,
            "the native line must actually be enabled");
        foreach (var moment in new[] { 1600, 1603 })
            Equal(0, new State(moment, 4, true).Reader().ReadTargets(Position(736)).Count,
                "tunnel objectives stay in the active chapter");
        foreach (var section in new[] { 19, 255 })
            foreach (var field in new[] { 736, 737, 778 })
                Equal(0, new State(1601, section, false).Reader().ReadTargets(Position(field)).Count,
                    $"field {field} rejects unavailable section {section}");
        foreach (var field in new[] { 737, 778 })
            Equal(0, new State(1601, 0, false).Reader().ReadTargets(Position(field)).Count,
                $"section zero belongs to tunnel_4, not field {field}");
        Equal(0, new State(1601, 3, false).Reader().ReadTargets(Position(736)).Count,
            "a section belonging to the other reused screen is not a guessed tunnel_4 route");
        Equal(0, new State(1601, 2, false).Reader().ReadTargets(Position(737)).Count,
            "nor is it a guessed tunnel_5 route");
    }

    private static void LinesBindToNativeScripts(string root)
    {
        var scripts = new FieldScriptNavigationCatalog(root);
        foreach (var row in FieldStoryEventCatalog.CreateAllFields().Where(r => r.FieldId is 736 or 737 or 778))
        {
            var native = scripts.ReadAllScriptOpcodes(row.FieldId)
                .Single(s => s.EntityId == row.EntityId && s.ScriptId == 0).Opcodes
                .Single(op => op.Opcode == 0xD0).Bytes.ToArray();
            var line = new FieldNavigationTriggerLine(
                BitConverter.ToInt16(native, 1), BitConverter.ToInt16(native, 3), BitConverter.ToInt16(native, 5),
                BitConverter.ToInt16(native, 7), BitConverter.ToInt16(native, 9), BitConverter.ToInt16(native, 11));
            Equal(line, row.TriggerLine!.Value, "Story uses the installed LINE rather than a guessed waypoint");
            Equal(row.EntityId, row.RequiredEnabledLineEntityId!.Value,
                "the same native entity owns the line-enable prerequisite");
        }
        var entry = scripts.ReadAllScriptOpcodes(733).Single(s => s.EntityId == 10 && s.ScriptId == 2).Opcodes
            .Single(op => op.Opcode == 0x60).Bytes.ToArray();
        Equal((ushort)735, BitConverter.ToUInt16(entry, 1), "the catwalk enters sbwy4_22");
        Equal((ushort)9, BitConverter.ToUInt16(entry, 7), "entry arrives on the native upper ladder triangle");
        var ladder = scripts.ReadAllScriptOpcodes(735).Single(s => s.EntityId == 1 && s.ScriptId == 0).Opcodes
            .First(op => op.Opcode == 0xC2).Bytes.ToArray();
        Equal((byte)1, ladder[11], "the entry's native ladder operand selects Up");
        Equal((ushort)2, BitConverter.ToUInt16(ladder, 9), "the entry ladder lands on its native onward triangle");
        var upperLeft = scripts.ReadAllScriptOpcodes(737).Single(s => s.EntityId == 10 && s.ScriptId == 2).Opcodes;
        Equal("14F081010012", Convert.ToHexString(upperLeft[0].Bytes.ToArray()),
            "the upper-left passage accepts section one");
        Equal("7C0F81", Convert.ToHexString(upperLeft.Single(op => op.Opcode == 0x7C).Bytes.ToArray()),
            "then decrements the section to zero");
        Equal((ushort)736, BitConverter.ToUInt16(upperLeft.First(op => op.Opcode == 0x60).Bytes.ToArray(), 1),
            "section zero is a reachable tunnel_4 screen");
    }

    private static void RoutesFromNativeArrivals(Func<int, FieldWalkmeshReader> create)
    {
        var arrivals = new (int Field, int X, int Y, ushort Triangle)[]
        {
            (736, 289, -427, 54), (736, 652, 2138, 88),
            (737, 3, 464, 47), (737, -520, 1741, 19), (737, 532, 1723, 50)
        };
        foreach (var (field, x, y, triangle) in arrivals)
        {
            foreach (var moment in new[] { 1601, 1602 })
            {
                foreach (var section in field == 736 ? new[] { 0, 2, 4, 6, 18 } : new[] { 1, 3, 5, 17 })
                {
                    var position = new FieldPositionSnapshot(1, (ushort)field, 0, x, y, 0, triangle, 0);
                    var state = new State(moment, section, moment == 1602);
                    var target = state.Reader().ReadTargets(position).Single();
                    var planner = new FieldWalkmeshRoutePlanner(create(field));
                    Equal(true, planner.TryBuildRoute(position, target, out _),
                        $"field {field}, native arrival {triangle}, section {section}, moment {moment}: {planner.LastDiagnostic}");
                }
            }
        }
    }

    private static FieldNavigationTarget Only(State state, int field, ushort triangle = 0)
    {
        var targets = state.Reader().ReadTargets(Position(field, triangle));
        Equal(1, targets.Count, $"field {field} has one current Story step");
        return targets[0];
    }

    private static int Entity(FieldNavigationTarget target) => int.Parse(target.StableId!.Split(':')[2]);

    private static FieldPositionSnapshot Position(int field, ushort triangle = 0) =>
        new(1, (ushort)field, 0, 0, 0, 0, triangle, 0);

    private sealed class State
    {
        private readonly Dictionary<int, byte> bytes = [];
        public int? DisabledLine { get; init; }

        public State(int moment, int section, bool settled)
        {
            var bank = FieldNavigationObjectReader.AddressFieldBankBase;
            bytes[bank] = (byte)moment;
            bytes[bank + 1] = (byte)(moment >> 8);
            bytes[bank + 0x400 + 129] = (byte)section;
            bytes[bank + 0x400 + 128] = settled ? (byte)4 : (byte)0;
        }

        public FieldStoryTargetReader Reader() => new(_ => 0,
            address => unchecked((short)(bytes.GetValueOrDefault(address) | bytes.GetValueOrDefault(address + 1) << 8)),
            address => bytes.GetValueOrDefault(address), FieldStoryEventCatalog.CreateAllFields(),
            entity => entity != DisabledLine);
    }

    private static void Contains(string? actual, string expected, string why)
    {
        if (actual?.Contains(expected, StringComparison.Ordinal) != true)
            throw new InvalidOperationException($"{why}: expected {expected}, got {actual ?? "none"}");
    }

    private static void Equal<T>(T expected, T actual, string why)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{why}: expected {expected}, got {actual}");
    }
}

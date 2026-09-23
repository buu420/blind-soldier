using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The fourteen rows the static route screen could not witness, started from the place
/// the game actually puts the party rather than from a door.
///
/// <para>The screen begins at gateway and MAPJUMP arrivals. That is the right start for
/// a room you walk into and the wrong one for a room you are put down in. All six fields
/// here are the second kind: what carries the party is the field's own ladders and jumps,
/// or a <c>JUMP</c> a script performs on the player entity, and a landing of either sort
/// touches no door.</para>
///
/// <para>Two are worth naming. zcoal_2's <c>cid</c> Main is
/// <c>UC Frozen; JUMP (790,200) triangle 0; UC Movable</c> - the field jumps the party
/// onto the first carriage before it gives control back, which is why no arrival lands
/// there and why the first carriage's two rows had no witness. ujunon3's pole sits on a
/// four-triangle island, component 1 of 3, reached only by <c>cloud</c> Scripts 6, 7 and
/// 8, the jumps the dolphin sequence performs; the field's single MAPJUMP arrival is in
/// the 323-triangle component that has no path to it.</para>
///
/// <para>hyou12 has no incoming gateway or MAPJUMP anywhere in the archive. Its own
/// <c>line00</c> Go leaves to <c>wm63</c>, so the Great Glacier is entered from the world
/// module and a field-to-field scan can have no start for it at all. What can be said
/// statically is that its walkmesh is one piece, which is asserted instead.</para>
/// </summary>
internal static class NativeLandingArrivalTests
{
    /// <summary>The fourteen screened rows, by field.</summary>
    private static readonly (int Field, string[] Labels)[] ScreenedRows =
    [
        (223, ["Climb the final ladder after landing from the swinging bar"]),
        (430, ["Climb the pole to Upper Junon"]),
        (733, ["Go through the duct", "Take the second ladder down"]),
        (734, ["Climb the ladder on the left back up"]),
        (729,
        [
            "Take on the first carriage", "Jump on past the first carriage",
            "Take on the second carriage", "Jump on past the second carriage",
            "Take on the third carriage", "Jump on past the third carriage",
            "Take on the fourth carriage", "Jump on past the fourth carriage",
        ]),
    ];

    /// <summary>
    /// Landings a script performs with <c>JUMP</c> on the player entity. The traversal
    /// catalog reads entity-owned LADER and JUMP handlers; these are neither, so they
    /// have to be named. Every one is an opcode in the installed field.
    /// </summary>
    private static readonly (int Field, int X, int Y, int Triangle, string Source)[] ScriptedLandings =
    [
        (430, -105, -380, 1, "ujunon3 cloud Script 6 JUMP"),
        (430, 141, -379, 328, "ujunon3 cloud Script 8 JUMP"),
        (430, -476, -540, 0, "ujunon3 cloud Script 9 JUMP"),
        (729, 790, 200, 0, "zcoal_2 cid Main JUMP"),
        (729, 831, 445, 14, "zcoal_2 cid Script 7 JUMP"),
        (729, 805, 697, 24, "zcoal_2 cid Script 8 JUMP"),
        (729, 790, 936, 38, "zcoal_2 cid Script 9 JUMP"),
        (729, 830, 1309, 54, "zcoal_2 cid Script 10 JUMP"),
    ];

    /// <summary>The glacier screen no field in the archive enters.</summary>
    private const int GreatGlacierScreen = 682;

    private const string GreatGlacierRow =
        "Continue through the Great Glacier toward the northern snowfield";

    public static void RunWithInstalledGameData(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        FieldScriptNavigationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(createWalkmeshReader);
        ArgumentNullException.ThrowIfNull(catalog);
        EveryScreenedRowIsWalkableFromANativeLanding(createWalkmeshReader, catalog);
        TheFirstCarriageIsReachedByTheFieldsOwnArrivalJump(createWalkmeshReader, catalog);
        ThePoleIsReachedByTheJumpsTheDolphinPerforms(createWalkmeshReader, catalog);
        NoWallClimbStageIsOfferedAStepItCannotReach(createWalkmeshReader, catalog);
        TheGlacierScreenIsOnePieceOfWalkmesh(createWalkmeshReader);
        Console.WriteLine("native landing arrival tests passed.");
    }

    /// <summary>
    /// A row has to be walkable from somewhere the game actually puts the party. Not from
    /// every landing: these fields are several stages each, and a landing belonging to a
    /// later stage is no evidence about an earlier row. A row no landing at all can reach
    /// would be the defect.
    /// </summary>
    private static void EveryScreenedRowIsWalkableFromANativeLanding(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        FieldScriptNavigationCatalog catalog)
    {
        var catalogRows = FieldStoryEventCatalog.CreateAllFields();
        var checkedRows = 0;
        foreach (var (field, labels) in ScreenedRows)
        {
            var reader = createWalkmeshReader(field);
            var mesh = Mesh(reader, field);
            var transitions = catalog.ReadField(field).Transitions;
            var landings = ResolveLandings(field, mesh, transitions);
            Equal(true, landings.Count > 0, $"field {field} must have at least one native landing");

            // The same transition-aware planner the hosts build, so a row on the far side
            // of a native ladder or jump is reachable the way the game makes it reachable.
            var planner = new FieldWalkmeshRoutePlanner(reader, transitionProvider: _ => transitions);
            foreach (var label in labels)
            {
                var target = TargetFor(catalogRows, field, label);
                var reached = 0;
                foreach (var (x, y, triangle, _) in landings)
                {
                    var position = new FieldPositionSnapshot(
                        FieldPositionReader.FieldModule, field, 0,
                        x, y, (int)Math.Round(mesh.Triangles[triangle].GetCentroid().Z), (ushort)triangle, 0);
                    if (planner.TryBuildRoute(position, target, out _))
                    {
                        reached++;
                    }
                }

                Equal(true, reached > 0,
                    $"field {field}: \"{label}\" must be walkable from at least one of the " +
                    $"{landings.Count} native landings; none reaches it");
                checkedRows++;
            }
        }

        Console.WriteLine($"  native landings: {checkedRows} screened rows each reach a native landing.");
    }

    /// <summary>
    /// The one that had no witness of any kind. zcoal_2's own Main jumps the party onto
    /// the first carriage, and the two rows there are offered from that landing and
    /// nowhere else - the field's twelve walkmesh components are four carriages of
    /// fourteen triangles each and the small pieces between them, and no door touches the
    /// first carriage.
    /// </summary>
    private static void TheFirstCarriageIsReachedByTheFieldsOwnArrivalJump(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        FieldScriptNavigationCatalog catalog)
    {
        const int coalTrain = 729;
        var reader = createWalkmeshReader(coalTrain);
        var mesh = Mesh(reader, coalTrain);
        var transitions = catalog.ReadField(coalTrain).Transitions;
        var planner = new FieldWalkmeshRoutePlanner(reader, transitionProvider: _ => transitions);
        var catalogRows = FieldStoryEventCatalog.CreateAllFields();
        var arrivalJump = Position(mesh, coalTrain, 790, 200, 0);

        foreach (var label in new[] { "Take on the first carriage", "Jump on past the first carriage" })
        {
            Equal(true, planner.TryBuildRoute(arrivalJump, TargetFor(catalogRows, coalTrain, label), out _),
                $"\"{label}\" must be walkable from the arrival jump at (790,200) triangle 0: " +
                planner.LastDiagnostic);
        }
    }

    /// <summary>
    /// And the other one. The pole row is on a four-triangle island; the dolphin's jumps
    /// are what puts the party on it, and the field's own arrival cannot reach it.
    /// </summary>
    private static void ThePoleIsReachedByTheJumpsTheDolphinPerforms(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        FieldScriptNavigationCatalog catalog)
    {
        const int lowerJunonBeach = 430;
        const string pole = "Climb the pole to Upper Junon";
        var reader = createWalkmeshReader(lowerJunonBeach);
        var mesh = Mesh(reader, lowerJunonBeach);
        var transitions = catalog.ReadField(lowerJunonBeach).Transitions;
        var planner = new FieldWalkmeshRoutePlanner(reader, transitionProvider: _ => transitions);
        var target = TargetFor(FieldStoryEventCatalog.CreateAllFields(), lowerJunonBeach, pole);

        foreach (var (scriptedField, x, y, triangle, scriptedSource) in ScriptedLandings)
        {
            if (scriptedField != lowerJunonBeach || triangle is not (1 or 328))
            {
                continue;
            }

            Equal(true, planner.TryBuildRoute(Position(mesh, lowerJunonBeach, x, y, triangle), target, out _),
                $"the pole must be walkable from the {scriptedSource} landing at ({x},{y}) " +
                $"triangle {triangle}: {planner.LastDiagnostic}");
        }

        // The single MAPJUMP arrival is in the large component, which is a different
        // piece of beach. Recording it keeps the reason the screen had no witness.
        Equal(false, planner.TryBuildRoute(Position(mesh, lowerJunonBeach, 0, 0, 147), target, out _),
            "and the field's own arrival cannot reach it, which is why no door witnesses this row");
    }

    /// <summary>
    /// The wall climb is one half of a wall the party crosses by alternating with
    /// wcrimb_2, and its walkmesh is sixteen components with a separate MAPJUMP arrival
    /// per stage. Two things have to hold at every one of them: whatever a stage is told
    /// to do it has to be able to walk there, and every stage has to be told something,
    /// because Story is the category that carries this chapter.
    ///
    /// <para>Neither held. The swinging-bar row was withheld only from the landing the
    /// bar serves, so the other arrivals from 224 were offered a bar they had no path
    /// to - <c>(331,2218)</c> on the 0,1 ledge, and <c>(-111,1554)</c> on 68,69. The
    /// ledge takes the final ladder instead. 68,69 is component 9 and 28,29,162..165 is
    /// component 4, joined by <c>line02</c>'s own ladder, and withholding the bar left
    /// the pair with no Story row at all: the way on was published as an Exit and
    /// nothing else, so the player had to change category to find it. <c>line02</c>'s
    /// row is what covers them, and the native chain behind it is asserted below.</para>
    /// </summary>
    private static void NoWallClimbStageIsOfferedAStepItCannotReach(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        FieldScriptNavigationCatalog catalog)
    {
        const int wallClimb = 223;
        const int otherHalf = 224;
        const int moment = 257;
        const string bar = "Reach the swinging bar and press OK at the prompt";
        const string finalLadder = "Climb the final ladder after landing from the swinging bar";
        const string line02 =
            "Take the ladder across to the other wall and back toward the swinging bar";
        const string leftLadder = "Descend the left ladder to return to the swinging bar";

        var reader = createWalkmeshReader(wallClimb);
        var mesh = Mesh(reader, wallClimb);
        var field = catalog.ReadField(wallClimb);
        var planner = new FieldWalkmeshRoutePlanner(reader, transitionProvider: _ => field.Transitions);

        // The battery byte as the native sockets leave it once both are in: root's own
        // reader replay used 0xE6, and the bar, ladder and line02 rows test bits 1 and 2
        // of it. Both socket rows read as complete at that value, so what is left is the
        // three Location rows that move the party on.
        var storyReader = new WallClimbMemory(moment, wallClimbFlags: 0xE6).Reader();
        var stages = new (int X, int Y, int Triangle, string From, string Step)[]
        {
            (-450, 1689, 10, "the fade arrival", bar),
            (-493, 1958, 2, "wcrimb_2 cloud Script 3", bar),
            (-111, 1554, 68, "wcrimb_2 cloud Script 5, component 9", line02),
            (-141, 1463, 164, "line02's own crossing, component 4", line02),
            (331, 2218, 0, "wcrimb_2 cloud Script 7", finalLadder),
            (8, 819, 111, "the mrkt4 arrival", bar),
        };

        foreach (var (x, y, triangle, from, step) in stages)
        {
            var at = new FieldPositionSnapshot(
                FieldPositionReader.FieldModule, wallClimb, 0,
                x, y, (int)Math.Round(mesh.Triangles[triangle].GetCentroid().Z), (ushort)triangle, 0);
            var offered = storyReader.ReadTargets(at);

            // Named, not counted. A stage with no Story row is the chapter going quiet,
            // and a stage with the wrong one is worse than quiet.
            Equal(true, offered.Any(candidate => candidate.Label == step),
                $"field {wallClimb}: the {from} landing ({x},{y}) triangle {triangle} must be " +
                $"offered \"{step}\" under Story; it was offered " +
                (offered.Count == 0
                    ? "nothing at all"
                    : string.Join(", ", offered.Select(candidate => $"\"{candidate.Label}\""))));

            foreach (var candidate in offered)
            {
                Equal(true, planner.TryBuildRoute(at, candidate, out _),
                    $"field {wallClimb}: \"{candidate.Label}\" is offered at the {from} landing " +
                    $"({x},{y}) triangle {triangle} and must be walkable from it: {planner.LastDiagnostic}");
            }
        }

        // The native chain the component 4 and 9 row stands on. line02 is entity 9; its
        // Move handler runs cloud's script 8, which is a LADER up to triangle 68 and then
        // MAPJUMP 60 e0 00 dc ff 3e 03 5b 00 80 - field 224 at (-36,830) triangle 91. The
        // row is only honest if that landing is somewhere the guidance continues.
        var line02Exits = field.Exits.Where(exit => exit.TriggerEntityId == 9).ToList();
        Equal(1, line02Exits.Count, $"field {wallClimb}: line02 must be the field's entity 9 exit");
        Equal(true,
            line02Exits[0].DestinationFieldIds is { Count: 1 } destinations &&
            destinations[0] == otherHalf,
            $"field {wallClimb}: line02's only destination must be field {otherHalf}; it is " +
            $"[{string.Join(",", line02Exits[0].DestinationFieldIds ?? [])}]");

        var otherReader = createWalkmeshReader(otherHalf);
        var otherMesh = Mesh(otherReader, otherHalf);
        var otherField = catalog.ReadField(otherHalf);
        var otherPlanner = new FieldWalkmeshRoutePlanner(
            otherReader, transitionProvider: _ => otherField.Transitions);
        const int landingTriangle = 91;
        Equal(true, landingTriangle < otherMesh.Triangles.Count,
            $"field {otherHalf} must have a triangle {landingTriangle} to be put down on");
        var landingZ = (int)Math.Round(otherMesh.Triangles[landingTriangle].GetCentroid().Z);
        Equal(landingTriangle,
            FieldWalkmeshPathfinder.ResolveTriangle(otherMesh, -36, 830, landingZ, -1),
            $"field {otherHalf}: the MAPJUMP destination (-36,830) must stand on triangle " +
            $"{landingTriangle}, the triangle the opcode names");

        var landing = new FieldPositionSnapshot(
            FieldPositionReader.FieldModule, otherHalf, 0,
            -36, 830, landingZ, landingTriangle, 0);
        var atLanding = storyReader.ReadTargets(landing);
        Equal(true, atLanding.Any(candidate => candidate.Label == leftLadder),
            $"field {otherHalf}: the ({-36},830) triangle {landingTriangle} landing must be " +
            $"offered \"{leftLadder}\", which is what closes the round trip; it was offered " +
            (atLanding.Count == 0
                ? "nothing at all"
                : string.Join(", ", atLanding.Select(candidate => $"\"{candidate.Label}\""))));

        foreach (var candidate in atLanding)
        {
            Equal(true, otherPlanner.TryBuildRoute(landing, candidate, out _),
                $"field {otherHalf}: \"{candidate.Label}\" is offered at the line02 landing " +
                $"(-36,830) triangle {landingTriangle} and must be walkable from it: " +
                otherPlanner.LastDiagnostic);
        }

        Console.WriteLine(
            $"  wall climb: {stages.Length} stages each carry a Story step they can walk, " +
            $"and line02 lands on {otherHalf} triangle {landingTriangle} where the return continues.");
    }

    /// <summary>
    /// hyou12 is entered from the world module, so there is no field arrival to start
    /// from. One piece of walkmesh is the whole static statement that can be made: from
    /// anywhere the party can stand on this screen, the way on is walkable.
    /// </summary>
    private static void TheGlacierScreenIsOnePieceOfWalkmesh(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var reader = createWalkmeshReader(GreatGlacierScreen);
        var mesh = Mesh(reader, GreatGlacierScreen);
        var planner = new FieldWalkmeshRoutePlanner(reader);
        var target = TargetFor(
            FieldStoryEventCatalog.CreateAllFields(), GreatGlacierScreen, GreatGlacierRow);

        var unreachable = new List<int>();
        foreach (var triangle in mesh.Triangles)
        {
            var centre = triangle.GetCentroid();
            var position = new FieldPositionSnapshot(
                FieldPositionReader.FieldModule, GreatGlacierScreen, 0,
                (int)Math.Round(centre.X), (int)Math.Round(centre.Y), (int)Math.Round(centre.Z),
                (ushort)triangle.Index, 0);
            if (!planner.TryBuildRoute(position, target, out _))
            {
                unreachable.Add(triangle.Index);
            }
        }

        Equal(0, unreachable.Count,
            "every standing position on the glacier screen reaches the way on; " +
            $"unreachable: [{string.Join(",", unreachable.Take(12))}]");
        Console.WriteLine(
            $"  glacier screen: {mesh.Triangles.Count} standing positions reach the way on.");
    }

    /// <summary>
    /// Everywhere the game puts the party down inside one field: the far end of every
    /// native traversal it owns, and the scripted jumps above.
    /// </summary>
    private static List<(int X, int Y, int Triangle, string Source)> ResolveLandings(
        int field,
        FieldWalkmesh mesh,
        IReadOnlyList<FieldScriptNavigationTransition> transitions)
    {
        var landings = new List<(int X, int Y, int Triangle, string Source)>();
        foreach (var transition in transitions)
        {
            if (transition.TargetTriangle >= 0 && transition.TargetTriangle < mesh.Triangles.Count)
            {
                landings.Add((transition.TargetX, transition.TargetY, transition.TargetTriangle,
                    $"{transition.Kind} landing from entity {transition.SourceEntityId}"));
            }
        }

        foreach (var (scriptedField, x, y, triangle, scriptedSource) in ScriptedLandings)
        {
            if (scriptedField == field && triangle >= 0 && triangle < mesh.Triangles.Count)
            {
                landings.Add((x, y, triangle, scriptedSource));
            }
        }

        return landings;
    }

    private static FieldNavigationTarget TargetFor(
        IReadOnlyList<FieldStoryEventDefinition> catalogRows,
        int field,
        string label)
    {
        var definition = catalogRows.SingleOrDefault(row => row.FieldId == field && row.Label == label);
        if (definition.Label is not { Length: > 0 })
        {
            throw new InvalidOperationException(
                $"Native landing arrival - the catalog must still carry \"{label}\" in field {field}.");
        }

        return new FieldNavigationTarget(
            field,
            FieldNavigationCategory.Story,
            definition.Label,
            definition.X,
            definition.Y,
            definition.Z,
            TriggerLine: definition.TriggerLine,
            InteractionRadius: definition.UsesPlayerCollisionRadius ? 33 : 0);
    }

    private static FieldPositionSnapshot Position(FieldWalkmesh mesh, int field, int x, int y, int triangle) =>
        new(
            FieldPositionReader.FieldModule, field, 0,
            x, y, (int)Math.Round(mesh.Triangles[triangle].GetCentroid().Z), (ushort)triangle, 0);

    private static FieldWalkmesh Mesh(FieldWalkmeshReader reader, int field) =>
        reader
            .Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, field, 0, 0, 0, 0, 0, 0))
            .Walkmesh
        ?? throw new InvalidOperationException($"the installed field {field} walkmesh must be readable.");

    /// <summary>
    /// The native state the wall climb's rows read: the moment, and the byte at
    /// Bank[1][165] the battery sockets record their progress in. No model resolves, so
    /// only Location rows can be offered - which is all four of them.
    /// </summary>
    private sealed class WallClimbMemory(int gameMoment, byte wallClimbFlags)
    {
        private const int EventTable = 0x100000;

        internal FieldStoryTargetReader Reader() =>
            new(ReadInt32, ReadInt16, ReadByte, FieldStoryEventCatalog.CreateAllFields(), _ => true);

        private byte ReadByte(int address)
        {
            if (address == FieldPositionReader.AddressCurrentModule)
            {
                return FieldPositionReader.FieldModule;
            }

            if (address == FieldPositionReader.AddressFieldNumModels)
            {
                return 1;
            }

            if (address == FieldNavigationObjectReader.AddressFieldBankBase)
            {
                return (byte)gameMoment;
            }

            if (address == FieldNavigationObjectReader.AddressFieldBankBase + 1)
            {
                return (byte)(gameMoment >> 8);
            }

            if (address == FieldNavigationObjectReader.AddressFieldBankBase + 165)
            {
                return wallClimbFlags;
            }

            return address >= FieldNavigationObjectReader.AddressFieldModelIdArray &&
                   address < FieldNavigationObjectReader.AddressFieldModelIdArray + 255
                ? (byte)0xFF
                : (byte)0;
        }

        private static int ReadInt32(int address) =>
            address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? EventTable : 0;

        private static short ReadInt16(int address) =>
            address == EventTable + 0x72 ? (short)34 : (short)0;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Native landing arrival - {label}: expected {expected}, got {actual}.");
        }
    }
}

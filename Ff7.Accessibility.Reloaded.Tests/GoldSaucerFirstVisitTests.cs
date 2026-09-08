using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// First-visit Gold Saucer coverage: paid entrance (GameMoment 439) through the
/// Battle Square accusation that drops the party into Corel Prison (445).
/// Every coordinate below is an installed walkmesh centroid or native gateway
/// line, not a guessed scene position.
/// </summary>
internal static class GoldSaucerFirstVisitTests
{
    private const int TerminalFloor = 497;
    private const int BattleSquarePlaza = 499;
    private const int ArenaLobby = 500;

    // gldgate walkmesh triangles 24..37 are the seven Square ramp pads in pairs.
    // cloud Script 4 picks the destination purely from the pad triangle id, so
    // the z=18 member of each pair is the step the player reaches first.
    private static readonly (string Square, int X, int Y, int Z, int PadA, int PadB)[] Platforms =
    [
        ("Ghost Square", -133, 627, 18, 24, 25),
        ("Battle Square", 427, 479, 18, 26, 27),
        ("Wonder Square", 587, 259, 18, 28, 29),
        ("Chocobo Square", 639, -22, 18, 30, 31),
        ("Event Square", -637, 19, 18, 32, 33),
        ("Speed Square", -559, 304, 18, 34, 35),
        ("Round Square", -390, 507, 18, 36, 37)
    ];

    private static FieldPositionSnapshot Position(int field, int x, int y, int z, ushort triangle) =>
        new(1, field, 0, x, y, z, triangle, 0);

    private static string Join(IEnumerable<int>? values) =>
        values is null ? "(none)" : string.Join(",", values);

    public static void Run(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        IReadOnlyList<FieldStoryEventDefinition>? definitions = null)
    {
        definitions ??= FieldStoryEventCatalog.CreateAllFields();
        EverySquareIsReachableFromTheTerminalFloor(definitions);
        TheObjectiveSquareTracksTheNativeStage(definitions);
        PlatformArrivalRequiresTheNativeActivationTriangle(createWalkmeshReader, definitions);
        TheInformationCounterWaitsUntilItsScriptDoesSomething(definitions);
        BattleSquareOpensOnlyAfterCaitSithJoins(definitions);
        LaterGoldSaucerChaptersStayOutOfTheFirstVisit(definitions);
        InstalledScriptsStillMatchTheStageAndGeometryEvidence();
    }

    private static void PlatformArrivalRequiresTheNativeActivationTriangle(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        var definition = definitions.Single(row => row.FieldId == TerminalFloor &&
                                                   row.Label == "Take the Wonder Square platform");
        Equal("28,29", Join(definition.CompletionPlayerTriangles),
            "the Wonder Square platform must complete on its native ramp pad triangles");

        var target = new FieldNavigationTarget(TerminalFloor, FieldNavigationCategory.Story,
            definition.Label, definition.X, definition.Y, definition.Z,
            CompletesOnArrival: true, CompletionTriangles: definition.CompletionPlayerTriangles);
        var transform = new FieldNavigationControlTransform(-128);
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([target]),
            new FieldWalkmeshRoutePlanner(createWalkmeshReader(TerminalFloor)));
        var start = Position(TerminalFloor, -80, 109, 0, 13);
        while (controller.CurrentCategory != FieldNavigationCategory.Story)
            controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);

        // 58 units from the ramp centroid - inside the configured 80-unit arrival
        // radius - but still on plaza triangle 12, where chekun's poll of
        // Bank[6][7] does nothing because the triangle is below 24.
        var onPlaza = Position(TerminalFloor, 540, 230, 0, 12);
        var offset = Math.Sqrt(Math.Pow(definition.X - onPlaza.X, 2) +
                               Math.Pow(definition.Y - onPlaza.Y, 2) +
                               Math.Pow(definition.Z - onPlaza.Z, 2));
        Equal(true, offset < 80,
            $"the regression only bites if the plaza point is inside the configured radius; it is {offset:0}");
        var nearby = controller.UpdateLiveTracking(onPlaza, new(0, FieldNavigationInput.None),
            transform, false, 80, observedAt: DateTime.UnixEpoch);
        Equal(null, nearby?.Speech,
            "a plaza position inside the arrival radius must not report the platform reached");
        Equal(true, controller.BeaconEnabled,
            "auto walk must not be released before the native activation triangle");

        var onPad = Position(TerminalFloor, 587, 259, 18, 29);
        var arrived = controller.UpdateLiveTracking(onPad, new(0, FieldNavigationInput.None),
            transform, false, 80, observedAt: DateTime.UnixEpoch.AddSeconds(1));
        Equal("Take the Wonder Square platform reached. Navigation off.", arrived?.Speech,
            "entering the native ramp pad must complete the platform objective");
        Equal(false, controller.BeaconEnabled, "a completed platform stops navigation");
    }

    private static void TheInformationCounterWaitsUntilItsScriptDoesSomething(
        IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        // gldgate/al [OK] bytes 0..8 return immediately while the GameMoment is
        // still 439, and again at 598. Only past 439 does it reach the MAPJUMP to
        // 498 gldinfo, so offering it at 439 would advertise a dead button press.
        Equal(0, Active(definitions, TerminalFloor, 439)
                .Count(row => row.Label.Contains("information counter", StringComparison.Ordinal)),
            "the information counter must not be offered while its [OK] script returns immediately");
        foreach (var moment in new[] { 440, 441, 442, 443, 444 })
        {
            Equal(1, Active(definitions, TerminalFloor, moment)
                    .Count(row => row.Label.Contains("information counter", StringComparison.Ordinal)),
                $"the information counter must be selectable at GameMoment {moment}");
        }
    }

    private static void EverySquareIsReachableFromTheTerminalFloor(
        IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        // The Terminal Floor has exactly one native gateway, back to the Ropeway
        // Station. Without these rows a blind player cannot reach any Square:
        // the tubes are chekun/cloud script triangles, not gateways.
        foreach (var moment in new[] { 439, 440, 441, 442, 443, 444 })
        {
            var rows = Active(definitions, TerminalFloor, moment);
            foreach (var platform in Platforms)
            {
                var matching = rows
                    .Where(row => row.Label.StartsWith($"Take the {platform.Square} platform", StringComparison.Ordinal))
                    .ToArray();
                Equal(1, matching.Length,
                    $"GameMoment {moment} must offer exactly one {platform.Square} platform on the Terminal Floor");
                Equal((platform.X, platform.Y, platform.Z),
                    (matching[0].X, matching[0].Y, matching[0].Z),
                    $"the {platform.Square} platform must sit on its native ramp pad");
                Equal("chekun", matching[0].SourceEntityName,
                    $"the {platform.Square} platform must record the native polling entity");
                Equal($"{platform.PadA},{platform.PadB}", Join(matching[0].CompletionPlayerTriangles),
                    $"the {platform.Square} platform must complete on its native ramp pad triangles");
            }
        }
    }

    private static void TheObjectiveSquareTracksTheNativeStage(
        IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        // games/dic Main runs the Cait Sith scene only at GameMoment 440, and
        // clsin2_1/dic Main runs the accusation only at 442. The objective must
        // follow those gates; every other Square stays optional so that visiting
        // one never becomes a prerequisite.
        var stages = new (int Moment, string Objective)[]
        {
            (439, "Wonder Square"), (440, "Wonder Square"), (441, "Wonder Square"),
            (442, "Battle Square"), (443, "Battle Square"), (444, "Battle Square")
        };
        foreach (var (moment, objective) in stages)
        {
            var rows = Active(definitions, TerminalFloor, moment)
                .Where(row => row.Label.StartsWith("Take the ", StringComparison.Ordinal))
                .ToArray();
            var required = rows.Where(row => !row.Label.EndsWith("(optional)", StringComparison.Ordinal)).ToArray();
            Equal(1, required.Length, $"GameMoment {moment} must have exactly one required Square platform");
            Equal($"Take the {objective} platform", required[0].Label,
                $"GameMoment {moment} must point at the Square holding the native scene");
            Equal(6, rows.Length - required.Length,
                $"GameMoment {moment} must keep the other six Squares optional");
            foreach (var optional in rows.Except([required[0]]))
            {
                Equal(true, optional.Priority > required[0].Priority,
                    $"{optional.Label} must never outrank the {objective} objective");
            }
        }
    }

    private static void BattleSquareOpensOnlyAfterCaitSithJoins(
        IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        // The Battle Square interior is shut for the whole first visit. coloss
        // entity 9 man2 stands solid at the foot of the stairs below 442 and says
        // the arena is being renovated, so none of the interior may be promised at
        // any first-visit moment, and the 442 discovery is a cutscene chain with no
        // walk left in it either.
        foreach (var moment in new[] { 439, 440, 441, 442, 443, 444, 445 })
        {
            Equal(0, Active(definitions, BattleSquarePlaza, moment).Count,
                $"the Battle Square plaza offers no navigable objective at GameMoment {moment}");
            Equal(0, Active(definitions, ArenaLobby, moment).Count,
                $"the Arena Lobby offers no navigable objective at GameMoment {moment}");
            Equal(0, Active(definitions, 502, moment).Count,
                $"the arena offers no navigable objective at GameMoment {moment}");
        }

        foreach (var forbidden in new[] { "Arena Lobby", "Battle Arena", "Dio's Museum" })
        {
            Equal(0, definitions.Count(row =>
                    row.Label.Contains(forbidden, StringComparison.Ordinal) &&
                    row.MinimumGameMoment >= 439 && row.MinimumGameMoment <= 445),
                $"no first-visit row may promise the {forbidden}; the renovation guard blocks it");
        }

        // The last thing the player actually steers is the Terminal Floor platform.
        Equal("Take the Battle Square platform",
            Active(definitions, TerminalFloor, 442)
                .Where(row => row.Label.StartsWith("Take the ", StringComparison.Ordinal))
                .Single(row => !row.Label.EndsWith("(optional)", StringComparison.Ordinal)).Label,
            "the Battle Square platform is the final steered step before the cutscene chain");
    }

    private static void LaterGoldSaucerChaptersStayOutOfTheFirstVisit(
        IReadOnlyList<FieldStoryEventDefinition> definitions)
    {
        // ghotin_4/dic Main writes 589 and clsin2_2/dio Talk writes 580. Both
        // rows were extracted without any stage gate and therefore advertised
        // the Keystone chapter during the first visit.
        foreach (var moment in new[] { 439, 440, 441, 442, 443, 444, 445 })
        {
            Equal(0, Active(definitions, 493, moment).Count,
                $"the Ghost Hotel room chapter must stay closed at GameMoment {moment}");
            Equal(0, Active(definitions, 503, moment).Count,
                $"Dio's Keystone conversation must stay closed at GameMoment {moment}");
        }

        // The replaced placeholder named four areas at once and carried no
        // reachable geometry, so it could never be navigated to.
        Equal(0, definitions.Count(row => row.FieldId == TerminalFloor &&
                                          row.Label.StartsWith("Continue Gold Saucer", StringComparison.Ordinal)),
            "the four-area Terminal Floor placeholder must be replaced by real platforms");
    }

    private static void InstalledScriptsStillMatchTheStageAndGeometryEvidence()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot))
            throw new InvalidOperationException("Gold Saucer native tests require FF7_ACCESSIBILITY_DATA_ROOT.");
        var scripts = new FieldScriptNavigationCatalog(dataRoot);
        // chekun Main reads the leader's triangle into Bank[6][7], arms on >=24,
        // and forces the companion choice while the GameMoment is still below 440.
        AssertBytes(497, 12, 0, 9, "7566660011131507");
        AssertBytes(497, 12, 0, 17, "1660070018000413");
        AssertBytes(497, 12, 0, 25, "16200000B8010308");
        // cloud Script 4 maps pad triangle >=26 to the Battle Square tube.
        AssertBytes(497, 1, 4, 210, "166007001A000421");
        AssertBytes(497, 1, 4, 218, "C00000D90155022B001700");
        // Cait Sith joins in Wonder Square, gated on GameMoment 440.
        AssertBytes(505, 0, 0, 68, "17200000B801003603");
        AssertBytes(505, 0, 0, 873, "C806");
        // coloss entity 9 man2 is the renovation guard. Below 442 it keeps a solid
        // 60-unit collision radius at the foot of the Battle Square stairs and its
        // talk shows the renovation notice; only at 442 and above does it turn
        // itself off. This is why no first-visit row may promise the interior.
        AssertBytes(499, 9, 0, 16, "16200000BA010409");
        AssertBytes(499, 9, 0, 32, "C6003C");
        AssertBytes(499, 9, 1, 0, "16200000BA010328");
        AssertBytes(499, 9, 1, 31, "400000");

        // The Arena accusation is gated on 442; clsin2_3 writes 445 and jumps to
        // field 471 jail1, which is the transition into the Corel Prison chapter.
        AssertBytes(502, 0, 0, 21, "17200000BA0100A300");
        AssertBytes(504, 0, 0, 54, "812000BD01");
        AssertBytes(504, 0, 0, 88, "60D70100000000000000");

        void AssertBytes(int field, int entity, int script, int offset, string expected)
        {
            var opcode = scripts.ReadScriptOpcodes(field, entity, script).Single(item => item.ByteIndex == offset);
            Equal(expected, Convert.ToHexString(opcode.Bytes.ToArray()),
                $"installed native script anchor {field}:{entity}:{script}:{offset}");
        }
    }

    private static IReadOnlyList<FieldStoryEventDefinition> Active(
        IReadOnlyList<FieldStoryEventDefinition> definitions, int field, int moment) =>
        definitions
            .Where(row => row.FieldId == field)
            .Where(row => row.MinimumGameMoment < 0 || moment >= row.MinimumGameMoment)
            .Where(row => row.MaximumGameMoment < 0 || moment <= row.MaximumGameMoment)
            .ToArray();

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Gold Saucer: {message}; expected {expected}, actual {actual}.");
    }
}

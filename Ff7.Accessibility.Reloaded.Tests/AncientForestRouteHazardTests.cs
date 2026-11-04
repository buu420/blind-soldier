using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Mutant Flytraps of the Ancient Forest (620 anfrst_1, 622 anfrst_3, 623 anfrst_4) as a
/// route hazard.
///
/// <para>Each jaw is a LINE whose slot 4 runs the leader's bite: HPD 1000 then HPU 1 on the whole
/// party, a placement into the mouth and a JUMP back out on the jaw's own side. 00637879 projects
/// the leader onto the line and 00637ABB counts a touch while the foot lies inside the segment's X
/// and Y extent and the squared 3D distance is below the leader's +0x72 radius (30 here). 622's
/// only walkable link between its halves is the flytrap's mouth: on 2026-11-04 the 0.10.8 auto
/// walk to the Spring Gun Clip went from the bite landing (179,-962) back into bigm0 37 times,
/// until the party sat at 1 HP and the next battle ended the game.</para>
///
/// <para><see cref="Run"/> needs no game data. <see cref="RunWithInstalledGameData"/> reads the
/// installed fields from FF7_ACCESSIBILITY_DATA_ROOT and fails when that is not set.</para>
/// </summary>
internal static class AncientForestRouteHazardTests
{
    private const int Radius = AncientForestRouteHazards.NativeLeaderRadius;

    // The bite landings, from the leader's bite scripts (622 cl s11 and s12).
    private static readonly (int X, int Y, int Triangle) WestBiteLanding = (179, -962, 185);
    private static readonly (int X, int Y, int Triangle) EastBiteLanding = (726, -846, 241);

    // field.tbl's world-map arrival and the gateway arrivals, from the installed gateways.
    private static readonly (int X, int Y, int Triangle) Entrance622 = (-2016, -748, 85);
    private static readonly (int X, int Y, int Triangle) From620Into622 = (2041, -655, 303);
    private static readonly (int X, int Y, int Triangle) From622Into620 = (-2043, -586, 44);

    internal static void Run()
    {
        var failures = new List<string>();
        foreach (var (name, check) in SyntheticCases())
        {
            Execute(name, check, failures);
        }

        Finish("synthetic", failures);
    }

    internal static void RunWithInstalledGameData()
    {
        var root = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("FF7_ACCESSIBILITY_DATA_ROOT is required for the installed flytrap checks.");
        }

        var forest = new Forest(root);
        var failures = new List<string>();
        foreach (var (name, check) in InstalledCases(forest))
        {
            Execute(name, check, failures);
        }

        Finish("installed", failures);
    }

    private static IEnumerable<(string Name, Action Check)> SyntheticCases()
    {
        yield return ("the jaws are the native LINEs and gates", TheJawsAreTheNativeLinesAndGates);
        yield return ("the hazard holds every engine touch, in three dimensions", TheHazardHoldsEveryEngineTouch);
        yield return ("an unreadable or short bank keeps every gated jaw biting", UnreadableGatesKeepTheJawsBiting);
        yield return ("a floor high above a jaw is walked, the floor beside it is not", AFloorAboveAJawIsWalked);
        yield return ("from inside the margin only a way out is clear", InsideTheMarginOnlyAWayOutIsClear);
        yield return ("the margin is checked to its full width", TheMarginIsCheckedToItsFullWidth);
        yield return ("a direction is held no longer than its touch clearance allows", SafeFramesAreTheTouchClearance);
        yield return ("a field without flytraps plans as it always did", OtherFieldsAreUntouched);
        yield return ("an unreadable flytrap check is an incoherent read, never a flytrap", AnUnreadableCheckIsNotAFlytrap);
    }

    private static IEnumerable<(string Name, Action Check)> InstalledCases(Forest forest)
    {
        yield return ("the jaw table is the installed scripts' own", () => TheJawTableIsTheInstalledScripts(forest));
        yield return ("622's west half never routes into the jaws (the 2026-11-04 loop)", () => WestHalfNeverRoutesIntoTheJaws(forest));
        yield return ("a bite published as a jump is still never walked into", () => ABiteJumpIsNeverWalkedInto(forest));
        yield return ("622's east half still routes through its vine", () => EastHalfStillRoutesThroughItsVine(forest));
        yield return ("622's west half still routes on its own ground", () => WestHalfStillRoutesItsOwnGround(forest));
        yield return ("a live leg across a jaw is refused by every movement check", () => LiveLegsAcrossAJawAreRefused(forest));
        yield return ("the corridor never shortcuts through a jaw", () => CorridorNeverShortcutsThroughAJaw(forest));
        yield return ("620's flytraps open the way only when shut", () => Field620OpensOnlyWhenShut(forest));
        yield return ("623's flytrap opens the way only when shut", () => Field623OpensOnlyWhenShut(forest));
        yield return ("a throwing spot past its line is never walked round", () => ThrowingSpotsAreNeverWalkedRound(forest));
        yield return ("Supershot ST is walked to within its native Talk reach, clear of both jaws", () => SupershotIsWalkedToWithinItsTalkReach(forest));
        yield return ("auto walk reaches Supershot ST at the scan cadence, clear of both jaws", () => AutoWalkReachesSupershotAtTheScanCadence(forest));
        yield return ("held within its safe frames, no logged decision cadence bites", () => HeldWithinItsSafeFramesNoLoggedCadenceBites(forest));
        yield return ("at Steam 2026's three steps a poll, nothing bites and nothing stalls silently", () => SteamThreeStepsAPollNeverBites(forest));
        yield return ("every route keeps clear of the live jaws", () => EveryRouteKeepsClearOfLiveJaws(forest));
    }

    // --- synthetic --------------------------------------------------------------------------

    /// <summary>
    /// The LINE coordinates and gates, as the installed scripts have them (the private native
    /// dumps of 2026-11-04, re-read from the installed data by <see cref="TheJawTableIsTheInstalledScripts"/>).
    /// </summary>
    private static readonly (int Field, int Entity, string Name, FieldNavigationTriggerLine Line, int Gate)[] NativeJaws =
    [
        (620, 25, "big0lt", new(-270, -207, 0, -270, -546, 0), 51),
        (620, 26, "big0rt", new(21, -263, 5, -1, -611, 0), 51),
        (620, 27, "big1lt", new(1441, -26, -12, 1315, -384, -11), 52),
        (620, 28, "big1rt", new(1700, -65, -11, 1433, -560, -12), 52),
        (622, 22, "bigm0", new(406, -868, 22, 406, -968, 25), -1),
        (622, 23, "bigm1", new(493, -863, 22, 485, -963, 18), -1),
        (623, 31, "big0up", new(443, -178, 37, 663, -178, 37), 61),
        (623, 32, "big0dn", new(390, -399, 37, 646, -458, 37), 61),
        (623, 33, "big0dn1", new(646, -458, 37, 767, -335, 37), 61),
    ];

    private static void TheJawsAreTheNativeLinesAndGates()
    {
        foreach (var field in new[] { 620, 622, 623 })
        {
            var expected = NativeJaws.Where(jaw => jaw.Field == field)
                .Select(jaw => $"e{jaw.Entity} {jaw.Name} {jaw.Line} gate {jaw.Gate}");
            var actual = AncientForestRouteHazards.Jaws(field)
                .Select(jaw => $"e{jaw.EntityId} {jaw.Name} {jaw.Line} gate {jaw.GateAddress}");
            Equal(string.Join("; ", expected), string.Join("; ", actual), $"field {field} jaws");
        }

        foreach (var field in new[] { 0, 1, 116, 621, 624, 777 })
        {
            Equal(0, AncientForestRouteHazards.Jaws(field).Count, $"field {field} has no flytrap");
        }
    }

    /// <summary>
    /// Every point the engine counts as touching a jaw - the foot inside the segment, the 3D
    /// distance under the radius, integer arithmetic as 00637879 does it - is inside the hazard,
    /// and the hazard is the touch region itself grown by the margin, not a planar band: a floor
    /// three hundred units over the line is clear, as it is to the engine.
    /// </summary>
    private static void TheHazardHoldsEveryEngineTouch()
    {
        var margin = AncientForestRouteHazards.RouteMargin;
        Equal(true, margin >= 8d, "the margin keeps the walk off the engine's own boundary");
        foreach (var jaw in NativeJaws)
        {
            var line = jaw.Line;
            var lineZ = (line.StartZ + line.EndZ) / 2;
            var minX = Math.Min(line.StartX, line.EndX) - Radius - 24;
            var maxX = Math.Max(line.StartX, line.EndX) + Radius + 24;
            var minY = Math.Min(line.StartY, line.EndY) - Radius - 24;
            var maxY = Math.Max(line.StartY, line.EndY) + Radius + 24;
            var touches = 0;
            for (var x = minX; x <= maxX; x += 2)
            for (var y = minY; y <= maxY; y += 2)
            foreach (var dz in new[] { -40, -20, -6, 0, 6, 20, 40, 300 })
            {
                var z = lineZ + dz;
                var clearance = AncientForestRouteHazards.Clearance(line, x, y, z, Radius);
                if (FieldNativeLineContact.Touches(line, x, y, z, Radius))
                {
                    touches++;
                    Equal(0d, clearance, $"{jaw.Name} engine touch at ({x},{y},{z}) is inside the hazard");
                }

                if (dz == 300)
                {
                    Equal(true, clearance > 250d, $"{jaw.Name} ({x},{y},{z}): a floor 300 over the line is clear");
                }
            }

            Equal(true, touches > 100, $"{jaw.Name}: the grid reaches the engine's touch region");
            var midX = (line.StartX + line.EndX) / 2;
            var midY = (line.StartY + line.EndY) / 2;
            Equal(0d, AncientForestRouteHazards.Clearance(line, midX, midY, lineZ, Radius), $"{jaw.Name}: on the line");
            // Never more than the true distance, and only the engine's rounding (a two-unit
            // allowance for its floored foot) less.
            var above = AncientForestRouteHazards.Clearance(line, midX, midY, lineZ + Radius + 5, Radius);
            Equal(true, above is >= 2d and <= 5d, $"{jaw.Name}: five over the radius straight above the line ({above:0.0})");
            var pastEnd = AncientForestRouteHazards.Clearance(line, line.EndX + (line.EndX - line.StartX) / 4d,
                line.EndY + (line.EndY - line.StartY) / 4d, line.EndZ, Radius);
            Equal(true, pastEnd > 0d, $"{jaw.Name}: a quarter of its length past its end is outside ({pastEnd:0.0})");
        }
    }

    private static void UnreadableGatesKeepTheJawsBiting()
    {
        string Active(int field, byte[]? bank) =>
            string.Join(",", AncientForestRouteHazards.ActiveJaws(field, bank).Select(jaw => jaw.EntityId));

        Equal("22,23", Active(622, null), "622 always bites");
        Equal("22,23", Active(622, Bank((51, 1), (52, 1), (61, 1))), "622 has no gate");
        Equal("25,26,27,28", Active(620, null), "620 unreadable: both flytraps bite");
        Equal("25,26,27,28", Active(620, new byte[40]), "620 short bank: both flytraps bite");
        Equal("25,26,27,28", Active(620, Bank()), "620 open");
        Equal("27,28", Active(620, Bank((51, 1))), "620 flytrap 1 shut");
        Equal("25,26", Active(620, Bank((52, 1))), "620 flytrap 2 shut");
        Equal("", Active(620, Bank((51, 1), (52, 1))), "620 both shut");
        Equal("31,32,33", Active(623, null), "623 unreadable");
        Equal("31,32,33", Active(623, new byte[61]), "623 short bank");
        Equal("31,32,33", Active(623, Bank((51, 1), (52, 1))), "623 is gated on its own byte");
        Equal("", Active(623, Bank((61, 1))), "623 shut");
        Equal("", Active(623, Bank((61, 2))), "IFUB [5][61] == 0 is the bite's own test: any other value is shut");
        Equal("", Active(624, null), "624 has none");
    }

    /// <summary>
    /// Two floors over bigm0's own plan position in a synthetic 622: one at the jaw's height and
    /// one 300 above it, not joined. The engine bites on the lower and never on the upper.
    /// </summary>
    private static void AFloorAboveAJawIsWalked()
    {
        var mesh = TwoFloorsOverBigm0();
        var planner = new FieldWalkmeshRoutePlanner(ReaderFor(mesh), playerCollisionRadiusProvider: _ => Radius);
        var upperStart = Snapshot(622, 320, -918, 320, 2);
        var upperGoal = Point(622, "upper floor east", 500, -918, 320);
        Equal(true, planner.TryBuildRoute(upperStart, upperGoal, out _), $"the upper floor crosses over the jaw: {planner.LastDiagnostic}");
        Equal(true, planner.IsAutomaticMovementClear(upperStart, upperGoal, new(500, -918, 320)), "and is walked straight across");

        var lowerStart = Snapshot(622, 320, -918, 20, 0);
        var lowerGoal = Point(622, "lower floor east", 500, -918, 20);
        Equal(false, planner.TryBuildRoute(lowerStart, lowerGoal, out _), "the lower floor passes through the jaw");
        Equal(true, planner.LastDiagnostic.Contains("flytrap", StringComparison.OrdinalIgnoreCase), $"and says why: {planner.LastDiagnostic}");
        Equal(true, planner.LastFailureWasFlytrap, "as a flytrap, not a door");
        Equal(false, planner.LastFailureWasNativeBoundary, "never a native lock to wait for");
        Equal(false, planner.IsAutomaticMovementClear(lowerStart, lowerGoal, new(500, -918, 20)), "nor walked straight across");
    }

    /// <summary>
    /// The leader standing inside the margin but outside the engine's touch (x 370: 36 from bigm0,
    /// six past its radius) may walk away and may not walk on toward or along the jaw.
    /// </summary>
    private static void InsideTheMarginOnlyAWayOutIsClear()
    {
        var mesh = TwoFloorsOverBigm0();
        var planner = new FieldWalkmeshRoutePlanner(ReaderFor(mesh), playerCollisionRadiusProvider: _ => Radius);
        var start = Snapshot(622, 370, -918, 20, 0);
        var away = Point(622, "west", 310, -918, 20);
        Equal(false, FieldNativeLineContact.Touches(NativeJaws[4].Line, start.X, start.Y, start.Z, Radius), "the start is not a touch");
        Equal(true, AncientForestRouteHazards.Clearance(NativeJaws[4].Line, start.X, start.Y, start.Z, Radius) < AncientForestRouteHazards.RouteMargin,
            "but it is inside the margin");
        Equal(true, planner.IsAutomaticMovementClear(start, away, new(310, -918, 20)), "walking straight away is clear");
        Equal(true, planner.TryBuildRoute(start, away, out _), $"and a route away is offered: {planner.LastDiagnostic}");
        Equal(false, planner.IsAutomaticMovementClear(start, away, new(390, -918, 20)), "walking on toward the jaw is not");
        Equal(false, planner.IsAutomaticMovementClear(start, away, new(370, -905, 20)), "nor along it at the same distance");
    }

    /// <summary>
    /// A walk along bigm0 at 47 units in plan, its foot inside the segment and 3.5 below the line:
    /// 47.1 in 3D, 15.1 past the radius and its two-unit rounding allowance, so inside the margin -
    /// though it is further from the segment than the radius and margin alone.
    /// </summary>
    private static void TheMarginIsCheckedToItsFullWidth()
    {
        var mesh = TwoFloorsOverBigm0();
        var jaws = AncientForestRouteHazards.Jaws(622);
        var clearance = AncientForestRouteHazards.Clearance(NativeJaws[4].Line, 359, -918, 20, Radius);
        Equal(true, clearance is > 15d and < AncientForestRouteHazards.RouteMargin, $"47 units out is inside the margin ({clearance:0.00})");
        Equal(true, AncientForestRouteHazards.TryFindContact(mesh, new(359, -935, 20), new(359, -900, 20), jaws, Radius,
            fromLeader: false, out var contact), "the walk along it comes within the margin");
        Equal(true, contact.Clearance < AncientForestRouteHazards.RouteMargin, $"at {contact}");
        Equal(false, AncientForestRouteHazards.TryFindContact(mesh, new(355, -935, 20), new(355, -900, 20), jaws, Radius,
            fromLeader: false, out _), "four units further out it does not");
    }

    /// <summary>
    /// On the flat floor at z 20, x 320, bigm0's touch is 54 units away in 3D (86 in plan and 3.5
    /// below the line, less its radius and the rounding allowance): six running frames of 8 or
    /// thirteen walking frames of 4, and running straight at the jaw for those six never touches.
    /// The floor 300 above has more to spare, and a field or gate with nothing biting no bound.
    /// </summary>
    private static void SafeFramesAreTheTouchClearance()
    {
        var mesh = TwoFloorsOverBigm0();
        var planner = new FieldWalkmeshRoutePlanner(ReaderFor(mesh), playerCollisionRadiusProvider: _ => Radius);
        var lower = Snapshot(622, 320, -918, 20, 0);
        var clearance = AncientForestRouteHazards.Clearance(NativeJaws[4].Line, 320, -918, 20, Radius);
        Equal(true, clearance is > 54d and < 55d, $"bigm0's touch is 54 away ({clearance:0.00})");
        Equal<int?>(6, planner.FlytrapSafeFrames(lower, 8), "six running frames");
        Equal<int?>(13, planner.FlytrapSafeFrames(lower, 4), "thirteen walking frames");
        for (var frame = 1; frame <= 6; frame++)
        {
            Equal(false, NativeJaws.Any(jaw => FieldNativeLineContact.Touches(jaw.Line, 320 + frame * 8, -918, 20, Radius)),
                $"running straight at bigm0, frame {frame}");
        }

        Equal(true, planner.FlytrapSafeFrames(Snapshot(622, 320, -918, 320, 2), 8) > 30, "the floor 300 above is far from the touch");
        var gated = new FieldWalkmeshRoutePlanner(ReaderFor(mesh), playerCollisionRadiusProvider: _ => Radius,
            ancientForestBankReader: _ => Bank((51, 1), (52, 1)));
        Equal<int?>(null, gated.FlytrapSafeFrames(Snapshot(620, 320, -918, 20, 0), 8), "620 with both flytraps shut");
        Equal<int?>(null, gated.FlytrapSafeFrames(Snapshot(621, 320, -918, 20, 0), 8), "621, where none lives");
    }

    /// <summary>
    /// The route is built on one walkmesh read and checked against the jaws on the next. When
    /// that second read fails, it is the game's memory that failed: the build reports an
    /// incoherent read, never a coherent refusal for the flytrap, which would have the
    /// destination taken for one the jaws shut off. The same walk over a steady read is planned.
    /// </summary>
    private static void AnUnreadableCheckIsNotAFlytrap()
    {
        var mesh = TwoFloorsOverBigm0();
        var unreadable = false;
        var planner = new FieldWalkmeshRoutePlanner(ReaderFor(mesh, () => unreadable),
            transitionProvider: _ =>
            {
                // Read while the route is built, after its walkmesh and before the jaw check's.
                unreadable = true;
                return Array.Empty<FieldScriptNavigationTransition>();
            },
            playerCollisionRadiusProvider: _ => Radius);
        var start = Snapshot(622, 320, -918, 320, 2);
        var goal = Point(622, "upper floor east", 500, -918, 320);
        Equal(false, planner.TryBuildRoute(start, goal, out _), "no route comes of an unreadable check");
        Equal(false, planner.LastReadWasCoherent, $"it is an incoherent read ({planner.LastDiagnostic})");
        Equal(false, planner.LastFailureWasFlytrap, "not the flytrap");
        Equal(false, planner.LastFailureWasNativeBoundary, "nor a lock");
        Equal(true, planner.LastDiagnostic.Contains("walkmesh read for the Mutant Flytrap check failed", StringComparison.Ordinal),
            $"and says so ({planner.LastDiagnostic})");

        var steady = new FieldWalkmeshRoutePlanner(ReaderFor(mesh), playerCollisionRadiusProvider: _ => Radius);
        Equal(true, steady.TryBuildRoute(start, goal, out _), $"over a steady read it is planned ({steady.LastDiagnostic})");
    }

    private static void OtherFieldsAreUntouched()
    {
        // The same two floors as field 621, where no flytrap lives, with a bank reader that
        // would report every gate open: the lower floor is ordinary ground there.
        var mesh = TwoFloorsOverBigm0();
        var planner = new FieldWalkmeshRoutePlanner(ReaderFor(mesh), playerCollisionRadiusProvider: _ => Radius,
            ancientForestBankReader: _ => new byte[128]);
        Equal(true, planner.TryBuildRoute(Snapshot(621, 320, -918, 20, 0), Point(621, "east", 500, -918, 20), out _),
            $"621 plans through: {planner.LastDiagnostic}");
        Equal(false, planner.LastFailureWasFlytrap, "and never reports a flytrap");
    }

    // --- installed ----------------------------------------------------------------------------

    private static void TheJawTableIsTheInstalledScripts(Forest forest)
    {
        foreach (var jaw in NativeJaws)
        {
            Equal(jaw.Line, forest.LineOf(jaw.Field, jaw.Entity), $"{jaw.Field} e{jaw.Entity} {jaw.Name} LINE");
            var touch = forest.Scripts.ReadScriptOpcodes(jaw.Field, jaw.Entity, 4).Select(Hex).ToArray();
            if (jaw.Gate < 0)
            {
                Equal(true, touch.Length > 0 && touch[0].StartsWith("0600C", StringComparison.Ordinal),
                    $"{jaw.Field} e{jaw.Entity}: the touch runs the leader's bite unconditionally ({string.Join(" ", touch)})");
            }
            else
            {
                Equal($"1450{jaw.Gate:X2}000004", touch.FirstOrDefault(), $"{jaw.Field} e{jaw.Entity}: bites only while 5[{jaw.Gate}] is 0");
            }

            var request = touch.First(op => op.StartsWith("0600C", StringComparison.Ordinal) || op.StartsWith("0600D", StringComparison.Ordinal));
            var script = Convert.ToInt32(request[4..6], 16) & 0x1F;
            var leader = forest.Leader(jaw.Field);
            Equal(true, forest.Scripts.ReadScriptOpcodes(jaw.Field, leader, script).Select(Hex).Contains("4F0000E803"),
                $"{jaw.Field} e{jaw.Entity}: leader script {script} takes 1000 HP");
        }

        foreach (var field in new[] { 620, 622, 623 })
        {
            Equal(Radius, forest.CollisionRadius(field), $"{field}: the leader's native radius");
        }
    }

    private static void WestHalfNeverRoutesIntoTheJaws(Forest forest)
    {
        var springGunClip = forest.Chest(622, 27);
        var exitTo620 = forest.Gateway(622, 620);
        foreach (var (label, at) in new[] { ("the forest entrance", Entrance622), ("the bite landing (179,-962)", WestBiteLanding) })
        {
            var start = forest.At(622, at);
            foreach (var target in new[] { springGunClip, exitTo620 })
            {
                foreach (var (kind, planner) in forest.ProductionPlanners(622, bank: null))
                {
                    var routed = planner.TryBuildRoute(start, target, out var plan);
                    Equal(false, routed && plan is not null,
                        $"{kind} from {label} to {target.Label}: no walk through the jaws ({planner.LastDiagnostic})");
                    Equal(true, planner.LastFailureWasFlytrap, $"{kind} from {label} to {target.Label}: refused for the flytrap ({planner.LastDiagnostic})");
                    Equal(true, planner.LastDiagnostic.Contains("flytrap", StringComparison.OrdinalIgnoreCase),
                        $"{kind} from {label} to {target.Label}: the diagnostic names the flytrap ({planner.LastDiagnostic})");
                    Equal(false, planner.LastFailureWasNativeBoundary, $"{kind}: not a native lock to wait for");
                    Equal(false, planner.TryGetNextWaypoint(start, target, out _), $"{kind}: no first step either");
                }
            }
        }

        // The tracker replans from where a bite drops the party: the same refusal, every time.
        var tracker = new FieldNavigationRouteTracker(forest.Planner(622));
        Equal(false, tracker.TryStart(forest.At(622, WestBiteLanding), springGunClip, out _), "the loop's own restart is refused");
    }

    /// <summary>
    /// The Echo-S layouts move 622's HPD out of the leader's bite scripts, which is how the mod
    /// tells a bite from a way across; a catalog that missed that would publish bigm0's JUMP as a
    /// traversal from the jaw to the east landing. The walk to its entry is a walk into the jaw,
    /// so nothing is routed through it, and nothing round it either.
    /// </summary>
    private static void ABiteJumpIsNeverWalkedInto(Forest forest)
    {
        var bite = new FieldScriptNavigationTransition(622, FieldNavigationTransitionKind.Jump, 22, 406, -918, 23,
            EastBiteLanding.X, EastBiteLanding.Y, null, EastBiteLanding.Triangle, "jump:622:22:bite");
        var transitions = forest.Scripts.ReadField(622).Transitions.Append(bite).ToArray();
        var planner = new FieldWalkmeshRoutePlanner(forest.Reader(622), null, _ => transitions,
            playerCollisionRadiusProvider: _ => Radius);
        foreach (var target in new[] { forest.Chest(622, 27), forest.Gateway(622, 620), forest.Model(622, 10, "fro1") })
        {
            Equal(false, planner.TryBuildRoute(forest.At(622, WestBiteLanding), target, out _),
                $"no route to {target.Label} through the bite: {planner.LastDiagnostic}");
            Equal(true, planner.LastFailureWasFlytrap, $"refused for the flytrap: {planner.LastDiagnostic}");
        }
    }

    private static void EastHalfStillRoutesThroughItsVine(Forest forest)
    {
        var start = forest.At(622, EastBiteLanding);
        foreach (var target in new[] { forest.Chest(622, 27), forest.Gateway(622, 620), forest.Model(622, 10, "fro1"), forest.Model(622, 11, "fro0 e11") })
        {
            foreach (var (kind, planner) in forest.ProductionPlanners(622, bank: null))
            {
                Equal(true, planner.TryBuildRoute(start, target, out var plan), $"{kind}: the east half reaches {target.Label}: {planner.LastDiagnostic}");
                forest.AssertClear(622, start, plan, null, $"{kind} east to {target.Label}");
            }
        }

        var fromFarEast = forest.At(622, From620Into622);
        foreach (var target in new[] { forest.Model(622, 10, "fro1"), forest.Chest(622, 27) })
        {
            var planner = forest.Planner(622);
            Equal(true, planner.TryBuildRoute(fromFarEast, target, out var plan), $"from 620's arrival to {target.Label}: {planner.LastDiagnostic}");
            forest.AssertClear(622, fromFarEast, plan, null, $"far east to {target.Label}");
        }
    }

    private static void WestHalfStillRoutesItsOwnGround(Forest forest)
    {
        foreach (var (from, target) in new[]
                 {
                     (Entrance622, forest.Model(622, 8, "bat2")),
                     (Entrance622, forest.Model(622, 9, "fro0")),
                     (WestBiteLanding, forest.Model(622, 9, "fro0")),
                     (WestBiteLanding, forest.Model(622, 7, "bat1")),
                     (WestBiteLanding, forest.Gateway(622, 55)),
                     (WestBiteLanding, Point(622, "beside jp0", 30, -124, 0) with { Z = forest.FloorZ(622, 30, -124, 240) }),
                 })
        {
            var start = forest.At(622, from);
            foreach (var (kind, planner) in forest.ProductionPlanners(622, bank: null))
            {
                Equal(true, planner.TryBuildRoute(start, target, out var plan), $"{kind} {from} to {target.Label}: {planner.LastDiagnostic}");
                forest.AssertClear(622, start, plan, null, $"{kind} {from} to {target.Label}");
            }
        }
    }

    /// <summary>
    /// (300,-918) on t24 to (440,-918) on t23 is open floor straight through bigm0 at x 406: the
    /// throat the 0.10.8 walk took. No check that lets auto walk take a step may call it clear.
    /// </summary>
    private static void LiveLegsAcrossAJawAreRefused(Forest forest)
    {
        var planner = forest.Planner(622);
        var start = forest.At(622, (300, -918, 24));
        var across = forest.Waypoint(622, 440, -918, 23);
        var target = forest.Chest(622, 27);
        Equal(true, FieldWalkmeshPathfinder.TraceWalkableSegment(forest.Mesh(622), 24, new(start.X, start.Y, start.Z), across,
            applyPortalInset: false).IsClear, "precondition: the leg is open floor");
        Equal(true, forest.NativelyTouches(622, [(new(start.X, start.Y, start.Z), across)], null), "precondition: the engine bites on it");

        Equal(false, planner.IsAutomaticMovementClear(start, target, across), "automatic movement");
        Equal(false, planner.IsModelAndBoundaryClear(start, target, across), "model and boundary clearance");
        Equal(false, planner.IsModelClearLegClear(start, target, across), "model-clear leg");
        Equal(false, planner.IsNativeProbeMovementClear(start, target, across), "native straight movement");
        Equal(false, planner.IsNativeProbeAutomaticMovementClear(start, target, across), "native immediate movement");
        Equal(true, planner.IsBodyMovementClear(start, target, across) != true, "body movement");

        // An eight-unit auto walk probe from just outside the margin into it.
        var edge = forest.At(622, (406 - Radius - (int)AncientForestRouteHazards.RouteMargin - 2, -918, 24));
        Equal(false, planner.IsAutomaticMovementClear(edge, target, forest.Waypoint(622, edge.X + 8, -918, 24)),
            "an eight-unit step into the margin");

        // The same checks still clear ordinary floor west of the jaw.
        var west = forest.Waypoint(622, 230, -930, 184);
        Equal(true, planner.IsAutomaticMovementClear(start, target, west), $"a step away from the jaw: {planner.LastDiagnostic}");
        Equal(true, planner.IsModelAndBoundaryClear(start, target, west), "model and boundary clearance away from the jaw");
    }

    /// <summary>
    /// A plan through the throat - as 0.10.8 built it, or as any planner without the hazard would -
    /// handed to the corridor lookahead: neither its visible step nor a held heading nor a
    /// recovery bend may lead through a jaw.
    /// </summary>
    private static void CorridorNeverShortcutsThroughAJaw(Forest forest)
    {
        var mesh = forest.Mesh(622);
        var start = forest.At(622, (300, -918, 24));
        var goal = forest.At(622, EastBiteLanding);
        Equal(true, FieldWalkmeshPathfinder.TryBuildRoute(mesh, 24, start.X, start.Y, start.Z, goal.X, goal.Y, goal.Z,
            out var path, out var portals, out var goalTriangle), "the bare walkmesh joins the halves through the mouth");
        var target = Point(622, "east", goal.X, goal.Y, goal.Z);
        var throughJaws = new FieldNavigationRoutePlan(622, "point:622:east", path, portals,
            new(goal.X, goal.Y, goal.Z), goalTriangle);
        var steps = FieldWalkmeshPathfinder.BuildStableWaypoints(start.X, start.Y, start.Z, portals, throughJaws.FinalApproach);
        Equal(true, forest.NativelyTouches(622, Legs(new(start.X, start.Y, start.Z), steps), null), "precondition: that plan bites");

        foreach (var heading in new[]
                 {
                     new FieldNavigationRouteHeading(false, 0, 0, "no heading"),
                     new FieldNavigationRouteHeading(true, 1, 0, "held east"),
                     new FieldNavigationRouteHeading(true, 1, 0, "blocked east", IsBlocked: true, BlockedSamples: 4, RecoveryAttempt: 1),
                 })
        {
            var planner = forest.Planner(622);
            _ = planner.TryBuildRoute(start, target, out _);
            if (!planner.TryObserveCorridor(start, throughJaws, steps, 0, null, heading, out var observation))
            {
                continue;
            }

            if (observation.Mode == FieldNavigationLookaheadMode.ReplanRequired)
            {
                continue;
            }

            var legs = new List<(FieldNavigationRouteWaypoint, FieldNavigationRouteWaypoint)>
            {
                (new(start.X, start.Y, start.Z), observation.Waypoint)
            };
            if (observation.RecoveryContinuation is { } continuation)
            {
                legs.Add((observation.Waypoint, continuation));
            }

            Equal(false, forest.NativelyTouches(622, legs, null),
                $"{heading.Diagnostic}: {observation.Mode} to {observation.Waypoint} must not lead through a jaw ({observation.Diagnostic})");
        }

        // And a safe plan keeps its ordinary lookahead.
        var east = forest.At(622, EastBiteLanding);
        var safePlanner = forest.Planner(622);
        var chest = forest.Chest(622, 27);
        Equal(true, safePlanner.TryBuildRoute(east, chest, out var safePlan), "precondition: the east half routes");
        var safeSteps = safePlan.StableWaypointsOverride ??
            FieldWalkmeshPathfinder.BuildStableWaypoints(east.X, east.Y, east.Z, safePlan.Portals, safePlan.FinalApproach);
        Equal(true, safePlanner.TryObserveCorridor(east, safePlan, safeSteps, 0, null,
            new FieldNavigationRouteHeading(false, 0, 0, "no heading"), out var safeObservation), "the safe plan is observed");
        Equal(true, safeObservation.Mode != FieldNavigationLookaheadMode.ReplanRequired,
            $"and not replanned for a flytrap it does not go near ({safeObservation.Diagnostic})");
    }

    private static void Field620OpensOnlyWhenShut(Forest forest)
    {
        var west = forest.At(620, From622Into620);
        var frog = forest.Model(620, 10, "fro1, middle");
        var eastPoint = forest.At(620, (1960, -866, 257));
        var east = Point(620, "east region", eastPoint.X, eastPoint.Y, eastPoint.Z);
        var exitTo623 = forest.Gateway(620, 623);
        var middle = forest.At(620, (501, -728, 157));
        byte[]? unreadable = null;
        var open = Bank();
        var oneShut = Bank((51, 1));
        var twoShut = Bank((52, 1));
        int[] oneLocks = [123, 124];
        int[] twoLocks = [207, 208, 209, 217, 218, 219];

        void Expect(bool routes, FieldPositionSnapshot from, FieldNavigationTarget to, byte[]? bank, int[] locks, string label)
        {
            foreach (var (kind, planner) in forest.ProductionPlanners(620, bank, locks))
            {
                var built = planner.TryBuildRoute(from, to, out var plan);
                Equal(routes, built, $"{kind} {label}: {planner.LastDiagnostic}");
                if (built)
                {
                    forest.AssertClear(620, from, plan, bank, $"{kind} {label}");
                    Equal(true, plan.TrianglePath.All(triangle => !locks.Contains(triangle)), $"{kind} {label}: the shut mouth's locks hold");
                }
                else
                {
                    Equal(true, planner.LastFailureWasFlytrap, $"{kind} {label}: refused for the flytrap ({planner.LastDiagnostic})");
                }
            }
        }

        Expect(false, west, frog, unreadable, [], "west to the middle, flytraps unreadable");
        Expect(false, west, frog, open, [], "west to the middle, both open");
        Expect(false, west, frog, twoShut, twoLocks, "west to the middle, only flytrap 2 shut");
        Expect(true, west, frog, oneShut, oneLocks, "west to the middle, flytrap 1 shut");
        Expect(false, middle, east, open, [], "middle to east, both open");
        Expect(false, middle, exitTo623, oneShut, oneLocks, "middle to the 623 exit, only flytrap 1 shut");
        Expect(true, middle, east, twoShut, twoLocks, "middle to east, flytrap 2 shut");
        Expect(true, middle, exitTo623, twoShut, twoLocks, "middle to the 623 exit, flytrap 2 shut");
        Expect(true, middle, forest.Model(620, 10, "fro1 from the middle"), open, [], "inside the middle region");
        // Slash-All (box1, e36) lies between flytrap 1's jaws: walked to only once 5[51] says it has shut.
        var slashAll = forest.Chest(620, 36);
        Expect(false, west, slashAll, unreadable, [], "Slash-All, flytrap 1 unreadable");
        Expect(false, west, slashAll, open, [], "Slash-All, flytrap 1 open");
        Expect(false, west, slashAll, twoShut, twoLocks, "Slash-All, only flytrap 2 shut");
        Expect(true, west, slashAll, oneShut, oneLocks, "Slash-All, flytrap 1 shut");
        Expect(true, west, Point(620, "west region", -455, -440, forest.FloorZ(620, -455, -440, 0)), open, [], "inside the west region");
    }

    private static void Field623OpensOnlyWhenShut(Forest forest)
    {
        var north = forest.At(623, (463, 11, 133));
        var south = forest.Model(623, 8, "bat2, south-middle");
        int[] locks = [114, 115];

        void Expect(bool routes, FieldPositionSnapshot from, FieldNavigationTarget to, byte[]? bank, int[] shut, string label)
        {
            foreach (var (kind, planner) in forest.ProductionPlanners(623, bank, shut))
            {
                var built = planner.TryBuildRoute(from, to, out var plan);
                Equal(routes, built, $"{kind} {label}: {planner.LastDiagnostic}");
                if (built)
                {
                    forest.AssertClear(623, from, plan, bank, $"{kind} {label}");
                    Equal(true, plan.TrianglePath.All(triangle => !shut.Contains(triangle)), $"{kind} {label}: the shut mouth's locks hold");
                }
                else
                {
                    Equal(true, planner.LastFailureWasFlytrap, $"{kind} {label}: refused for the flytrap ({planner.LastDiagnostic})");
                }
            }
        }

        Expect(false, north, south, null, [], "north-middle to south-middle, unreadable");
        Expect(false, north, south, Bank(), [], "north-middle to south-middle, open");
        Expect(true, north, south, Bank((61, 1)), locks, "north-middle to south-middle, shut");
        var southStart = forest.At(623, (467, -667, 90));
        foreach (var (label, x, y, triangle) in new[] { ("bat3", 998, -753, 72), ("the hakjp landing", 872, -324, 118), ("plant 4's drop", 1096, -284, 101) })
        {
            var to = forest.At(623, (x, y, triangle));
            Expect(true, southStart, Point(623, label, to.X, to.Y, to.Z), Bank(), [], $"south-middle to {label}, open");
        }
    }

    /// <summary>
    /// A throwing spot past a line works only if that line is the last the walk crosses. From
    /// 623's t108, just under big0dn, the walk to bigt1's spot passes about seven units from
    /// big0dn1's bite, and the way round the jaws crosses bigt3, which puts the zone back to 1
    /// (GuideRouteRegressionTests.ForestThrowingSpotsAreTheZoneLines). The spot is refused rather
    /// than walked round; the same point as plain ground is walked round, clear of the jaws.
    /// </summary>
    private static void ThrowingSpotsAreNeverWalkedRound(Forest forest)
    {
        var start = forest.Starts(623).ElementAt(108);
        var spot = Point(623, "Mutant Flytrap 1, throwing spot", 781, -449, 31) with { ApproachCrossingLine = forest.LineOf(623, 36) };
        var planner = forest.Planner(623);
        Equal(false, planner.TryBuildRoute(start, spot, out _), $"the spot is not walked round: {planner.LastDiagnostic}");
        Equal(true, planner.LastFailureWasFlytrap, $"refused for the flytrap: {planner.LastDiagnostic}");
        var ground = spot with { ApproachCrossingLine = null, StableId = "point:623:ground" };
        Equal(true, planner.TryBuildRoute(start, ground, out var round), $"the same ground is walked round: {planner.LastDiagnostic}");
        forest.AssertClear(623, start, round, null, "the way round");
    }

    /// <summary>
    /// Supershot ST (622 box0, e26) stands in the flytrap's throat at (441,-914), but Talk reaches
    /// it from outside either jaw. 00636284 takes the model the leader faces whose centre is nearer
    /// than the leader's +0x72 (30) plus the model's +0x74 - 80, field set-up's default, since
    /// box0's Init has no TALKR - so 110, while bigm0 and bigm1 stand 35 and 48 units from the
    /// chest. The tester received it at 107 (2026-11-04 09:58:53). From either half the route has
    /// to end on that half, inside the reach and outside both jaws' margin.
    /// </summary>
    private static void SupershotIsWalkedToWithinItsTalkReach(Forest forest)
    {
        var init = forest.Scripts.ReadScriptOpcodes(622, 26, 0);
        Equal(false, init.Any(op => op.Opcode is 0xC5 or 0xD6), "box0 sets no talk radius of its own (TALKR, TLKR2)");
        var chest = forest.Talk(622, 26, "Supershot ST");
        Equal(110, chest.InteractionRadius, "the native Talk reach: 30 and 80");
        foreach (var (side, from) in new[] { ("west", Entrance622), ("west", WestBiteLanding), ("east", EastBiteLanding), ("east", From620Into622) })
        {
            var start = forest.At(622, from);
            foreach (var (kind, planner) in forest.ProductionPlanners(622, bank: null))
            {
                Equal(true, planner.TryBuildRoute(start, chest, out var plan), $"{kind} from {from}: {planner.LastDiagnostic}");
                var end = plan.FinalApproach;
                var reach = Math.Sqrt(Math.Pow(end.X - chest.X, 2) + Math.Pow(end.Y - chest.Y, 2));
                Equal(true, reach < chest.InteractionRadius, $"{kind} from {from}: ends within the Talk reach ({reach:0.0} at {end})");
                Equal(true, side == "west" ? end.X < 406 : end.X > 489, $"{kind} from {from}: ends on the {side} half ({end})");
                forest.AssertClear(622, start, plan, null, $"{kind} from {from} to Supershot ST");
            }
        }
    }

    /// <summary>
    /// The walk itself, as the hosts drive it and the native step moves it (the verified flat
    /// subset of 00636C41/006367B7, on 622's own triangles with their heights set aside for the
    /// step and put back for the touch): each decision's direction through the shared auto walk
    /// controller, acted on only in the polls its planner budget allows. At the 50 ms scan the
    /// runtimes are set for - a decision every two frames - walking, as the log's cadence=Walk
    /// was, auto walk arrives within the Talk reach from both halves and from the entrance, and on
    /// no frame does the engine's own touch (radius 30, the real floor's height) fire on either jaw.
    /// </summary>
    private static void AutoWalkReachesSupershotAtTheScanCadence(Forest forest)
    {
        var chest = forest.Talk(622, 26, "Supershot ST");
        foreach (var from in new[] { WestBiteLanding, EastBiteLanding, Entrance622 })
        {
            var (reached, bite, detail, _) = forest.Replay(chest, from, 1024, 2, scanFrames: 1);
            Equal(null, bite, $"from {from}: {detail}");
            Equal(true, reached, $"from {from}: {detail}");
        }
    }

    /// <summary>
    /// The 0.10.8 worker's decisions in 622 came 351 to 530 ms apart (10 to 16 frames), 250 to 424
    /// ms of each spent on the scan's own work, and the x64 sink kept pressing a direction for up
    /// to 500 ms unrenewed. Pressed until the next decision, a direction cleared for its first
    /// eight units walks the leader into bigm1 from the east landing (16 frames, walking) - the
    /// first control. A budget counted from before the scan's work is spent before it is pressed,
    /// and the walk goes nowhere - the second (root's probe, 2026-11-04). Scheduled as production
    /// is - three quarters of each decision on the scan's work, then the budget measured and the
    /// direction pressed - none of the logged cadences, walking or running, from either half or
    /// the entrance, lets the touch fire, and every one arrives within the Talk reach.
    /// </summary>
    private static void HeldWithinItsSafeFramesNoLoggedCadenceBites(Forest forest)
    {
        var chest = forest.Talk(622, 26, "Supershot ST");
        var (_, unleasedBite, unleasedDetail, _) = forest.Replay(chest, EastBiteLanding, 1024, 16, leased: false);
        Equal(true, unleasedBite is not null, $"held to the next decision, the east walk bites: {unleasedDetail}");
        var (staleReached, staleBite, staleDetail, _) = forest.Replay(chest, EastBiteLanding, 1024, 16, scanFrames: 12, staleMark: true);
        Equal(true, staleBite is null && !staleReached, $"counted from before the scan's work, it goes nowhere: {staleDetail}");
        foreach (var from in new[] { WestBiteLanding, EastBiteLanding, Entrance622 })
        {
            foreach (var speed in new ushort[] { 1024, 2048 })
            {
                foreach (var frames in new[] { 4, 10, 13, 16 })
                {
                    var (reached, bite, detail, _) = forest.Replay(chest, from, speed, frames, scanFrames: frames * 3 / 4);
                    var label = $"from {from}, {(speed == 1024 ? "walking" : "running")}, a decision every {frames} frames";
                    Equal(null, bite, $"{label}: {detail}");
                    Equal(true, reached, $"{label}: {detail}");
                }
            }
        }
    }

    /// <summary>
    /// Steam 2026 budgets each of its keyboard polls at three runs' steps: whether its 3x speed
    /// runs three field iterations, each polling, or moves three steps on one poll is not proven.
    /// Either way - one step a poll, or three - at the logged cadences, walking or running, from
    /// either half or the entrance, the touch never fires; and a walk that does not arrive was
    /// handed back to the player in words, never left standing silently.
    /// </summary>
    private static void SteamThreeStepsAPollNeverBites(Forest forest)
    {
        var chest = forest.Talk(622, 26, "Supershot ST");
        foreach (var steps in new[] { 1, 3 })
        {
            foreach (var from in new[] { WestBiteLanding, EastBiteLanding, Entrance622 })
            {
                foreach (var speed in new ushort[] { 1024, 2048 })
                {
                    foreach (var frames in new[] { 13, 16 })
                    {
                        var (reached, bite, detail, _) = forest.Replay(chest, from, speed, frames, scanFrames: frames * 3 / 4,
                            unitsPerPoll: 3 * AutoWalkPollLease.MaximumUnitsPerPoll, stepsPerPoll: steps);
                        var label = $"{steps} step(s) a poll, from {from}, {(speed == 1024 ? "walking" : "running")}, every {frames} frames";
                        Equal(null, bite, $"{label}: {detail}");
                        Equal(true, reached || detail.StartsWith("handed back", StringComparison.Ordinal), $"{label}: {detail}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// From the centre of every third triangle of each field to its creatures, chests and exits:
    /// whatever is routed never lets the engine's touch fire on an active jaw and keeps the margin.
    /// </summary>
    private static void EveryRouteKeepsClearOfLiveJaws(Forest forest)
    {
        foreach (var (field, bank) in new (int, byte[]?)[] { (622, null), (620, null), (620, Bank((51, 1))), (623, null) })
        {
            var targets = forest.Targets(field);
            var locks = field == 620 && bank is not null ? new[] { 123, 124 } : Array.Empty<int>();
            var planner = forest.Planner(field, bank, locks);
            var routed = 0;
            foreach (var start in forest.Starts(field).Where((_, index) => index % 3 == 0))
            {
                foreach (var target in targets)
                {
                    if (planner.TryBuildRoute(start, target, out var plan))
                    {
                        routed++;
                        forest.AssertClear(field, start, plan, bank, $"{field} from ({start.X},{start.Y}) t{start.TriangleId} to {target.Label}");
                    }
                }
            }

            Equal(true, routed > 0, $"{field}: the sweep routed something");
        }
    }

    // --- plumbing -----------------------------------------------------------------------------

    private static void Execute(string name, Action check, List<string> failures)
    {
        try
        {
            check();
            Console.WriteLine($"PASS flytrap route hazard: {name}");
        }
        catch (Exception exception)
        {
            failures.Add($"{name}: {exception.Message}");
            Console.WriteLine($"FAIL flytrap route hazard: {name}: {exception.Message}");
        }
    }

    private static void Finish(string suite, List<string> failures)
    {
        if (failures.Count > 0)
        {
            throw new InvalidOperationException($"{failures.Count} Ancient Forest flytrap route hazard checks ({suite}) failed.");
        }

        Console.WriteLine($"Ancient Forest flytrap route hazard checks ({suite}) passed.");
    }

    private static byte[] Bank(params (int Address, byte Value)[] values)
    {
        var bank = new byte[128];
        foreach (var (address, value) in values)
        {
            bank[address] = value;
        }

        return bank;
    }

    private static FieldPositionSnapshot Snapshot(int field, int x, int y, int z, int triangle) =>
        new(FieldPositionReader.FieldModule, field, 0, x, y, z, (ushort)triangle, 0);

    private static FieldNavigationTarget Point(int field, string label, int x, int y, int z) =>
        new(field, FieldNavigationCategory.Objects, label, x, y, z, $"point:{field}:{label}");

    private static IReadOnlyList<(FieldNavigationRouteWaypoint From, FieldNavigationRouteWaypoint To)> Legs(
        FieldNavigationRouteWaypoint start, IReadOnlyList<FieldNavigationRouteStep> steps)
    {
        var legs = new List<(FieldNavigationRouteWaypoint, FieldNavigationRouteWaypoint)>();
        var from = start;
        foreach (var step in steps)
        {
            legs.Add((from, step.Waypoint));
            from = step.Waypoint;
        }

        return legs;
    }

    /// <summary>
    /// A strip of two triangles at z 20 and the same strip 300 higher, x 300-520 by y -940 to
    /// -896: both pass over bigm0 (x 406, y -868 to -968) and bigm1 (x 485-493) in plan.
    /// </summary>
    private static FieldWalkmesh TwoFloorsOverBigm0()
    {
        FieldWalkmeshVertex V(int x, int y, int z) => new((short)x, (short)y, (short)z);
        return new FieldWalkmesh(
        [
            new(0, V(300, -940, 20), V(520, -940, 20), V(520, -896, 20), -1, -1, 1),
            new(1, V(300, -940, 20), V(520, -896, 20), V(300, -896, 20), 0, -1, -1),
            new(2, V(300, -940, 320), V(520, -940, 320), V(520, -896, 320), -1, -1, 3),
            new(3, V(300, -940, 320), V(520, -896, 320), V(300, -896, 320), 2, -1, -1),
        ]);
    }

    private static FieldWalkmeshReader ReaderFor(FieldWalkmesh mesh, Func<bool>? unreadable = null)
    {
        const int fieldDataBase = 0x02000000;
        const int sectionOffset = 0x100;
        var memory = new Dictionary<int, int> { [FieldWalkmeshReader.AddressFieldDataPtr] = fieldDataBase };
        var table = fieldDataBase + FieldWalkmeshReader.SectionOffsetsHeaderOffset + FieldWalkmeshReader.WalkmeshSectionIndex * sizeof(int);
        var triangles = mesh.Triangles;
        memory[table] = sectionOffset;
        memory[table + sizeof(int)] = sectionOffset + sizeof(int) + sizeof(int) +
            triangles.Count * (FieldWalkmeshReader.TriangleSize + FieldWalkmeshReader.AccessSize);
        var payload = fieldDataBase + sectionOffset + sizeof(int);
        memory[payload] = triangles.Count;
        var trianglesBase = payload + sizeof(int);
        void Vertex(int address, FieldWalkmeshVertex vertex)
        {
            memory[address] = vertex.X;
            memory[address + sizeof(short)] = vertex.Y;
            memory[address + sizeof(short) * 2] = vertex.Z;
            memory[address + sizeof(short) * 3] = 0;
        }

        for (var index = 0; index < triangles.Count; index++)
        {
            var at = trianglesBase + index * FieldWalkmeshReader.TriangleSize;
            Vertex(at, triangles[index].Vertex0);
            Vertex(at + FieldWalkmeshReader.VertexSize, triangles[index].Vertex1);
            Vertex(at + FieldWalkmeshReader.VertexSize * 2, triangles[index].Vertex2);
        }

        var access = trianglesBase + triangles.Count * FieldWalkmeshReader.TriangleSize;
        for (var index = 0; index < triangles.Count; index++)
        {
            var at = access + index * FieldWalkmeshReader.AccessSize;
            memory[at] = triangles[index].Adjacent0;
            memory[at + sizeof(short)] = triangles[index].Adjacent1;
            memory[at + sizeof(short) * 2] = triangles[index].Adjacent2;
        }

        // Unreadable, the field data pointer reads as nothing, as an unmapped page would.
        return new FieldWalkmeshReader(
            address => unreadable?.Invoke() == true && address == FieldWalkmeshReader.AddressFieldDataPtr
                ? 0
                : memory.TryGetValue(address, out var value) ? value : 0,
            address => (short)(memory.TryGetValue(address, out var value) ? value : 0));
    }

    private static string Hex(FieldScriptOpcodeDefinition op) => Convert.ToHexString(op.Bytes.ToArray());

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }

    /// <summary>The installed forest fields, as the live runtimes plan them.</summary>
    private sealed class Forest
    {
        private const int FieldDataBase = 0x02000000;
        private const int FieldState = 0x03200000;
        private readonly FlevelDataSource source;
        private readonly Dictionary<int, byte[]> decoded = [];
        private readonly Dictionary<int, FieldWalkmesh> meshes = [];

        public Forest(string root)
        {
            source = new FlevelDataSource(root);
            Scripts = new FieldScriptNavigationCatalog(root);
        }

        public FieldScriptNavigationCatalog Scripts { get; }

        public byte[] Decoded(int field)
        {
            if (!decoded.TryGetValue(field, out var bytes))
            {
                Equal(true, source.TryReadField(field, out var encoded), $"field {field} is in the archive");
                decoded[field] = bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
            }

            return bytes;
        }

        public FieldWalkmeshReader Reader(int field)
        {
            var bytes = Decoded(field);
            return new FieldWalkmeshReader(
                address => address == FieldWalkmeshReader.AddressFieldDataPtr
                    ? FieldDataBase
                    : address - FieldDataBase >= 0 && address - FieldDataBase + 4 <= bytes.Length
                        ? BitConverter.ToInt32(bytes, address - FieldDataBase)
                        : 0,
                address => address - FieldDataBase >= 0 && address - FieldDataBase + 2 <= bytes.Length
                    ? BitConverter.ToInt16(bytes, address - FieldDataBase)
                    : (short)0);
        }

        public FieldWalkmesh Mesh(int field)
        {
            if (!meshes.TryGetValue(field, out var mesh))
            {
                meshes[field] = mesh = Reader(field).Read(Snapshot(field, 0, 0, 0, 0)).Walkmesh
                    ?? throw new InvalidOperationException($"field {field} walkmesh must be readable");
            }

            return mesh;
        }

        /// <summary>
        /// The main navigation planner as Mod.cs and the x64 coordinator build it: live locks,
        /// transitions, the leader's radius and the forest bank.
        /// </summary>
        public FieldWalkmeshRoutePlanner Planner(int field, byte[]? bank = null, IReadOnlyCollection<int>? locks = null) =>
            new(Reader(field), Boundaries(field, locks ?? []), _ => Scripts.ReadField(field).Transitions,
                playerCollisionRadiusProvider: _ => Radius,
                ancientForestBankReader: id => id == field ? bank : null);

        /// <summary>
        /// Every planner shape production builds for a forest field: the main planner, the exit
        /// reachability planner (no radius) and one with no bank reader at all.
        /// </summary>
        public IEnumerable<(string Kind, FieldWalkmeshRoutePlanner Planner)> ProductionPlanners(
            int field, byte[]? bank, IReadOnlyCollection<int>? locks = null)
        {
            yield return ("main planner", Planner(field, bank, locks));
            yield return ("exit reachability planner", new FieldWalkmeshRoutePlanner(Reader(field), Boundaries(field, locks ?? []),
                _ => Scripts.ReadField(field).Transitions, ancientForestBankReader: id => id == field ? bank : null));
            if (bank is null)
            {
                yield return ("planner without a bank reader", new FieldWalkmeshRoutePlanner(Reader(field), Boundaries(field, locks ?? []),
                    _ => Scripts.ReadField(field).Transitions, playerCollisionRadiusProvider: _ => Radius));
            }
        }

        private static FieldBoundaryStateReader Boundaries(int field, IReadOnlyCollection<int> locks) =>
            new(address => address == FieldBoundaryStateReader.AddressFieldGlobalObjectPtr ? FieldState : 0,
                address =>
                {
                    if (address == FieldPositionReader.AddressCurrentModule)
                    {
                        return (byte)FieldPositionReader.FieldModule;
                    }

                    if (address == FieldPositionReader.AddressFieldId || address == FieldPositionReader.AddressFieldId + 1)
                    {
                        return (byte)(field >> (address == FieldPositionReader.AddressFieldId ? 0 : 8));
                    }

                    var offset = address - FieldState - FieldBoundaryStateReader.BoundaryBitsOffset;
                    var bits = 0;
                    foreach (var triangle in locks.Where(triangle => triangle / 8 == offset))
                    {
                        bits |= 1 << (triangle % 8);
                    }

                    return (byte)bits;
                },
                (_, _) => true);

        public double FloorZ(int field, double x, double y, double zHint)
        {
            var best = double.NaN;
            foreach (var triangle in Mesh(field).Triangles)
            {
                if (PlaneZ(triangle, x, y) is { } z && (double.IsNaN(best) || Math.Abs(z - zHint) < Math.Abs(best - zHint)))
                {
                    best = z;
                }
            }

            return double.IsNaN(best) ? zHint : best;
        }

        public int FloorZ(int field, int x, int y, int zHint) => (int)Math.Round(FloorZ(field, (double)x, y, zHint));

        public FieldPositionSnapshot At(int field, (int X, int Y, int Triangle) at)
        {
            var triangle = Mesh(field).Triangles[at.Triangle];
            var z = PlaneZ(triangle, at.X, at.Y) ?? triangle.GetCentroid().Z;
            return Snapshot(field, at.X, at.Y, (int)Math.Round(z), at.Triangle);
        }

        public FieldNavigationRouteWaypoint Waypoint(int field, int x, int y, int triangle)
        {
            var at = At(field, (x, y, triangle));
            return new(at.X, at.Y, at.Z);
        }

        public IEnumerable<FieldPositionSnapshot> Starts(int field) =>
            Mesh(field).Triangles.Select(triangle =>
            {
                var centre = triangle.GetCentroid();
                return Snapshot(field, (int)Math.Round(centre.X), (int)Math.Round(centre.Y), (int)Math.Round(centre.Z), triangle.Index);
            });

        public FieldNavigationTriggerLine LineOf(int field, int entity)
        {
            var line = Scripts.ReadScriptOpcodes(field, entity, 0).Single(op => op.Opcode == 0xD0).Bytes.ToArray();
            return new(BitConverter.ToInt16(line, 1), BitConverter.ToInt16(line, 3), BitConverter.ToInt16(line, 5),
                BitConverter.ToInt16(line, 7), BitConverter.ToInt16(line, 9), BitConverter.ToInt16(line, 11));
        }

        /// <summary>The entity bound to Cloud (PC 0): the leader whose scripts the jaws request.</summary>
        public int Leader(int field) =>
            Scripts.ReadAllScriptOpcodes(field).First(s => s.ScriptId == 0 && s.Opcodes.Any(op => Hex(op) == "A000")).EntityId;

        /// <summary>The leader's +0x72 after field set-up: 30 (0060BCFA), or SLIDR's, times the header scale / 512.</summary>
        public int CollisionRadius(int field)
        {
            var bytes = Decoded(field);
            var scale = BitConverter.ToInt16(bytes, BitConverter.ToInt32(bytes, 6) + 4 + 8);
            var leader = Scripts.ReadAllScriptOpcodes(field).First(s => s.ScriptId == 0 && s.Opcodes.Any(op => Hex(op) == "A000"));
            var slidr = leader.Opcodes.LastOrDefault(op => op.Opcode == 0xC6 && op.Bytes.Count == 3 && op.Bytes[1] == 0);
            var value = slidr.Bytes is null ? 30 : slidr.Bytes[2];
            return value * scale / 512;
        }

        /// <summary>A model by its own XYZI, as the object reader places it.</summary>
        public FieldNavigationTarget Model(int field, int entity, string label)
        {
            var xyzi = Scripts.ReadScriptOpcodes(field, entity, 0).First(op => op.Opcode == 0xA5).Bytes.ToArray();
            return new FieldNavigationTarget(field, FieldNavigationCategory.Objects, label, BitConverter.ToInt16(xyzi, 3),
                BitConverter.ToInt16(xyzi, 5), BitConverter.ToInt16(xyzi, 7), $"object:{field}:{entity}",
                TriggerEntityId: entity, InteractionRadius: 48);
        }

        public FieldNavigationTarget Chest(int field, int entity) => Model(field, entity, $"chest e{entity}") with { InteractionRadius = 100 };

        /// <summary>A Talk model as the object reader offers it: reached within the leader's 30 and field set-up's talk radius 80.</summary>
        public FieldNavigationTarget Talk(int field, int entity, string label) =>
            Model(field, entity, label) with { Activation = FieldNavigationActivation.Talk, InteractionRadius = Radius + 80 };

        /// <summary>The trigger header's signed control byte (+0x09): the angle field input is turned by.</summary>
        public int ControlDirection(int field)
        {
            var bytes = Decoded(field);
            return (sbyte)bytes[BitConverter.ToInt32(bytes, 6 + 7 * 4) + 4 + 9];
        }

        /// <summary>The field's own triangles, adjacency and indices with every height 0: the native step's verified subset.</summary>
        public FieldWalkmesh Flat(int field)
        {
            var flat = new FieldWalkmesh(Mesh(field).Triangles.Select(triangle => triangle with
            {
                Vertex0 = triangle.Vertex0 with { Z = 0 },
                Vertex1 = triangle.Vertex1 with { Z = 0 },
                Vertex2 = triangle.Vertex2 with { Z = 0 }
            }).ToArray());
            Equal(true, FieldNavigationNativeProbeMovement.IsSupportedWalkmesh(flat), $"the native step supports field {field}'s triangles in plan");
            return flat;
        }

        /// <summary>
        /// Auto walk to a target as the hosts schedule it: one scan every so many native frames,
        /// which reads the position and decides a direction, then spends
        /// <paramref name="scanFrames"/> of them on its route work while the game goes on acting on
        /// the last press, and only then presses - through the shared NavigationAutoWalkController,
        /// its poll lease armed, when <paramref name="leased"/> as in production, from a fresh mark
        /// and position (ResolveFreshFieldBudget), or with <paramref name="staleMark"/> from the
        /// scan's own. A direction is acted on in a frame only when the lease lets it, for
        /// <paramref name="stepsPerPoll"/> native steps - three for a host that might batch its 3x
        /// speed under one poll - by FieldNavigationNativeProbeMovement on the flattened triangles,
        /// the leader given the real floor's height. Whether it was reached, handed back, or bitten.
        /// </summary>
        public (bool Reached, string? Bite, string Detail, FieldPositionSnapshot Last) Replay(
            FieldNavigationTarget target, (int X, int Y, int Triangle) from, ushort speed, int framesPerDecision,
            bool leased = true, int scanFrames = 0, bool staleMark = false,
            double unitsPerPoll = AutoWalkPollLease.MaximumUnitsPerPoll, int stepsPerPoll = 1)
        {
            var field = target.FieldId;
            var planner = Planner(field);
            var controller = new FieldNavigationController(new FieldNavigationTargetSource([], objectTargetProvider: _ => [target]), planner);
            var transform = new FieldNavigationControlTransform(ControlDirection(field));
            var flat = Flat(field);
            var jaws = NativeJaws.Where(jaw => jaw.Field == field).ToArray();
            var state = new FieldNavigationNativeMovementState(from.X * 4096, from.Y * 4096, 0, from.Triangle, 0);
            var z = At(field, from).Z;
            FieldPositionSnapshot Here()
            {
                var x = state.FixedX >> 12;
                var y = state.FixedY >> 12;
                z = FloorZ(field, x, y, z);
                return new FieldPositionSnapshot(FieldPositionReader.FieldModule, field, 0, x, y, z, (ushort)state.TriangleId, state.Heading)
                    { NativeFixedPosition = new(state.FixedX, state.FixedY, z * 4096) };
            }

            for (var guard = 0; guard < 8 && controller.CurrentCategory != FieldNavigationCategory.Objects; guard++)
            {
                controller.HandleAction(FieldNavigationAction.NextCategory, Here(), transform);
            }

            var selected = controller.HandleAction(FieldNavigationAction.ToggleBeacon, Here(), transform)?.Speech ?? string.Empty;
            if (!controller.BeaconEnabled)
            {
                return (false, null, $"navigation did not start: \"{selected}\"", Here());
            }

            using var walk = new NavigationAutoWalkController(new AcceptingSink(), new AutoWalkPollLease(unitsPerPoll: unitsPerPoll));
            // The replay's frames are the game's polls.
            walk.PollLease.AttachPoller(() => true);
            _ = walk.TryStart(NavigationAutoWalkDomain.Field, routeActive: true);
            var input = FieldNavigationInput.None;
            byte? pressed = null;
            var trail = new List<string>();
            string? bite = null;
            var biteDetail = string.Empty;

            // One of the game's polls, on whatever is pressed: false once the engine's touch fires.
            bool Frame(int tick, int frame)
            {
                if (!walk.PollLease.Poll() || pressed is not { } heading)
                {
                    return true;
                }

                for (var step = 0; step < stepsPerPoll; step++)
                {
                    state = FieldNavigationNativeProbeMovement.Step(flat, state, heading, speed, Radius, []).State;
                    var x = state.FixedX >> 12;
                    var y = state.FixedY >> 12;
                    var floor = FloorZ(field, x, y, z);
                    foreach (var jaw in jaws)
                    {
                        if (FieldNativeLineContact.Touches(jaw.Line, x, y, floor, Radius))
                        {
                            bite = $"{jaw.Name} bites at {x},{y},{floor}";
                            biteDetail = $"tick {tick} frame {frame}, trail {string.Join(" ", trail.TakeLast(12))}";
                            return false;
                        }
                    }
                }

                return true;
            }

            for (var tick = 0; tick < 1500; tick++)
            {
                var scanMark = walk.PollLease.Mark();
                var position = Here();
                var spoken = controller.UpdateLiveTracking(position, new(0, input), transform, false, 80,
                    observedAt: DateTime.UnixEpoch.AddMilliseconds(tick * framesPerDecision * 1000d / 30d))?.Speech;
                if (spoken is not null && spoken.Contains("reached", StringComparison.Ordinal))
                {
                    var distance = Math.Sqrt(Math.Pow(position.X - target.X, 2) + Math.Pow(position.Y - target.Y, 2));
                    return (distance < target.InteractionRadius, null, $"\"{spoken}\" at {position.X},{position.Y}, {distance:0.0} away", position);
                }

                var moving = controller.TryResolveAutomaticInput(position, transform, 80, out input);

                // The scan's route work: the game goes on polling the last press meanwhile.
                for (var frame = 0; frame < Math.Min(scanFrames, framesPerDecision); frame++)
                {
                    if (!Frame(tick, frame))
                    {
                        return (false, bite, biteDetail, Here());
                    }
                }

                var fieldBudget = !leased || !moving
                    ? default
                    : staleMark
                        ? planner.FlytrapSafeFrames(position, unitsPerPoll) is { } stale
                            ? new AutoWalkFieldBudget(new AutoWalkPollBudget(scanMark, stale), null, null)
                            : default
                        : walk.PollLease.ResolveFreshFieldBudget(planner, position, () => Here());
                if (fieldBudget.Handoff is { } handoff)
                {
                    var now = Here();
                    return (false, null, $"handed back at {now.X},{now.Y}: {handoff}", now);
                }

                var pressing = moving && fieldBudget.Blocked is null;
                _ = walk.Drive(pressing ? input : FieldNavigationInput.None, canMove: pressing, routeActive: true,
                    pollBudget: fieldBudget.Budget);
                var (dx, dy) = FieldNavigationMovementObserver.PredictWorldDirection(input, transform);
                pressed = pressing ? unchecked((byte)(int)Math.Round(Math.Atan2(dx, -dy) * 128 / Math.PI)) : null;
                trail.Add($"{position.X},{position.Y}:{(pressing ? input : FieldNavigationInput.None)}/{fieldBudget.Budget?.SafePolls.ToString() ?? "-"}");
                for (var frame = Math.Min(scanFrames, framesPerDecision); frame < framesPerDecision; frame++)
                {
                    if (!Frame(tick, frame))
                    {
                        return (false, bite, biteDetail, Here());
                    }
                }
            }

            var last = Here();
            return (false, null, $"not reached; at {last.X},{last.Y} ({controller.LastNavigationDiagnostic}); trail {string.Join(" ", trail.TakeLast(12))}", last);
        }

        /// <summary>A delivery that takes every key: the replay moves the leader itself.</summary>
        private sealed class AcceptingSink : IHighwayKeyboardInputSink
        {
            public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions) =>
                new(transitions.Count, 0);
        }

        public FieldNavigationTarget Gateway(int field, int destination)
        {
            var bytes = Decoded(field);
            var section = BitConverter.ToInt32(bytes, 6 + 7 * 4) + 4;
            for (var index = 0; index < 12; index++)
            {
                var at = section + 0x38 + index * 24;
                if (BitConverter.ToInt16(bytes, at + 18) != destination)
                {
                    continue;
                }

                var line = new FieldNavigationTriggerLine(
                    BitConverter.ToInt16(bytes, at), BitConverter.ToInt16(bytes, at + 2), BitConverter.ToInt16(bytes, at + 4),
                    BitConverter.ToInt16(bytes, at + 6), BitConverter.ToInt16(bytes, at + 8), BitConverter.ToInt16(bytes, at + 10));
                return new FieldNavigationTarget(field, FieldNavigationCategory.Exits, $"exit to {destination}",
                    (line.StartX + line.EndX) / 2, (line.StartY + line.EndY) / 2, (line.StartZ + line.EndZ) / 2,
                    $"gateway:{field}:{index}:{destination}", DestinationFieldIds: [destination], TriggerLine: line);
            }

            throw new InvalidOperationException($"field {field} has no gateway to {destination}");
        }

        public IReadOnlyList<FieldNavigationTarget> Targets(int field) => field switch
        {
            622 => [Chest(622, 27), Chest(622, 26), Gateway(622, 620), Gateway(622, 55), Model(622, 8, "bat2"), Model(622, 10, "fro1")],
            620 => [Gateway(620, 622), Gateway(620, 623), Model(620, 8, "bat1"), Model(620, 10, "fro1"), Chest(620, 36)],
            _ => [Gateway(623, 620), Gateway(623, 624), Model(623, 7, "bat1"), Model(623, 8, "bat2"), Model(623, 9, "bat3")],
        };

        /// <summary>
        /// The legs auto walk walks for a plan: the tracker's own steps from the planning position,
        /// with each scripted traversal's entry walked to and the walk resumed at its exit.
        /// </summary>
        public IReadOnlyList<(FieldNavigationRouteWaypoint From, FieldNavigationRouteWaypoint To)> WalkedLegs(
            FieldPositionSnapshot start, FieldNavigationRoutePlan plan)
        {
            var steps = plan.StableWaypointsOverride ??
                FieldWalkmeshPathfinder.BuildStableWaypoints(start.X, start.Y, start.Z, plan.Portals, plan.FinalApproach);
            var traversals = plan.Portals.Where(portal => portal.TransitionKind is not null).ToArray();
            var legs = new List<(FieldNavigationRouteWaypoint, FieldNavigationRouteWaypoint)>();
            var from = new FieldNavigationRouteWaypoint(start.X, start.Y, start.Z);
            var next = 0;
            foreach (var step in steps)
            {
                var arrivedAtEntry = false;
                while (next < traversals.Length && OnSegment(traversals[next].Left, from, step.Waypoint))
                {
                    legs.Add((from, traversals[next].Left));
                    arrivedAtEntry = traversals[next].Left == step.Waypoint;
                    from = traversals[next].TransitionExit ?? traversals[next].Left;
                    next++;
                    if (arrivedAtEntry)
                    {
                        break;
                    }
                }

                if (!arrivedAtEntry)
                {
                    legs.Add((from, step.Waypoint));
                    from = step.Waypoint;
                }
            }

            return legs;
        }

        public bool NativelyTouches(int field, IReadOnlyList<(FieldNavigationRouteWaypoint From, FieldNavigationRouteWaypoint To)> legs, byte[]? bank)
        {
            _ = MinimumClearance(field, legs, bank, out var touched, out _);
            return touched;
        }

        /// <summary>
        /// Every leg at one-unit steps on the floor under it: whether the engine's own touch fires
        /// on a live jaw anywhere (FieldNativeLineContact, radius 30), and the least 3D distance to
        /// that touch region (flat-ended, as 00637879 has it).
        /// </summary>
        public double MinimumClearance(int field, IReadOnlyList<(FieldNavigationRouteWaypoint From, FieldNavigationRouteWaypoint To)> legs,
            byte[]? bank, out bool touched, out string where)
        {
            touched = false;
            where = string.Empty;
            var least = double.PositiveInfinity;
            var jaws = NativeJaws.Where(jaw => jaw.Field == field &&
                (jaw.Gate < 0 || bank is null || bank.Length <= jaw.Gate || bank[jaw.Gate] == 0)).ToArray();
            foreach (var (from, to) in legs)
            {
                var length = Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));
                var samples = Math.Max(1, (int)Math.Ceiling(length));
                for (var sample = 0; sample <= samples; sample++)
                {
                    var amount = sample / (double)samples;
                    var x = from.X + (to.X - from.X) * amount;
                    var y = from.Y + (to.Y - from.Y) * amount;
                    var z = FloorZ(field, x, y, from.Z + (to.Z - from.Z) * amount);
                    foreach (var jaw in jaws)
                    {
                        var distance = TouchRegionDistance(jaw.Line, x, y, z);
                        if (distance < least)
                        {
                            least = distance;
                            where = $"{jaw.Name} at ({x:0},{y:0},{z:0})";
                        }

                        if (FieldNativeLineContact.Touches(jaw.Line, (int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z), Radius))
                        {
                            touched = true;
                        }
                    }
                }
            }

            return least;
        }

        public void AssertClear(int field, FieldPositionSnapshot start, FieldNavigationRoutePlan plan, byte[]? bank, string label)
        {
            var legs = WalkedLegs(start, plan);
            var clearance = MinimumClearance(field, legs, bank, out var touched, out var where);
            Equal(false, touched, $"{label}: the engine bites on this route near {where}");
            // The start may already be inside the margin; every other point keeps it, less the
            // one-unit sampling and rounding of the corners.
            var startClearance = MinimumClearance(field, [(new(start.X, start.Y, start.Z), new(start.X, start.Y, start.Z))], bank, out _, out _);
            Equal(true, clearance >= Math.Min(AncientForestRouteHazards.RouteMargin, startClearance) - 2d,
                $"{label}: comes within {clearance:0.0} of a live jaw's touch near {where}");
        }

        private static bool OnSegment(FieldNavigationRouteWaypoint point, FieldNavigationRouteWaypoint a, FieldNavigationRouteWaypoint b)
        {
            var dx = b.X - (double)a.X;
            var dy = b.Y - (double)a.Y;
            var lengthSquared = dx * dx + dy * dy;
            var amount = lengthSquared <= 0d ? 0d : Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0d, 1d);
            return Math.Sqrt(Math.Pow(a.X + dx * amount - point.X, 2) + Math.Pow(a.Y + dy * amount - point.Y, 2)) < 1d;
        }

        private static double? PlaneZ(FieldWalkmeshTriangle triangle, double x, double y)
        {
            var (a, b, c) = (triangle.Vertex0, triangle.Vertex1, triangle.Vertex2);
            var denominator = (b.Y - c.Y) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Y - c.Y);
            if (Math.Abs(denominator) < 1e-9)
            {
                return null;
            }

            var wa = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / denominator;
            var wb = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / denominator;
            var wc = 1d - wa - wb;
            return wa < -1e-9 || wb < -1e-9 || wc < -1e-9 ? null : wa * a.Z + wb * b.Z + wc * c.Z;
        }

        /// <summary>3D distance to the engine's touch region: the foot inside the segment, under the radius.</summary>
        private static double TouchRegionDistance(FieldNavigationTriggerLine line, double x, double y, double z)
        {
            double ax = line.StartX, ay = line.StartY, az = line.StartZ;
            double bx = line.EndX - ax, by = line.EndY - ay, bz = line.EndZ - az;
            var lengthSquared = bx * bx + by * by + bz * bz;
            var length = Math.Sqrt(lengthSquared);
            var t = ((x - ax) * bx + (y - ay) * by + (z - az) * bz) / lengthSquared;
            var normal = Math.Sqrt(Math.Pow(x - (ax + t * bx), 2) + Math.Pow(y - (ay + t * by), 2) + Math.Pow(z - (az + t * bz), 2));
            var beyond = t < 0d ? -t * length : t > 1d ? (t - 1d) * length : 0d;
            return Math.Sqrt(beyond * beyond + Math.Pow(Math.Max(0d, normal - Radius), 2));
        }
    }
}

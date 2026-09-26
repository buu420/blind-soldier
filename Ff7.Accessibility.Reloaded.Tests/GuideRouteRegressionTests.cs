using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Two routes the 2026-09-25 Absolute Steve re-audit found broken in 0.7.0, checked against the
/// installed field data on whichever archive the host runs with.
///
/// <para><b>Added Effect Materia, gidun_1 (546).</b> The materia stands on an upper ledge that
/// the cave floor's walkmesh does not join; the only native way up is out through gidun_2 (547)
/// and back in by its other passage. The player's log has "Route unavailable to Added Effect
/// Materia" from the floor.</para>
///
/// <para><b>Don Corneo's bedroom, colne_6 (211).</b> When Corneo chooses Cloud, the scene is
/// started by pressing OK on the line beside the bed; the only Story row pointed at the rug,
/// whose line is off until the scene has played.</para>
/// </summary>
internal static class GuideRouteRegressionTests
{
    internal static void Run()
    {
        foreach (var (_, check) in Cases())
        {
            check();
        }
    }

    internal static IEnumerable<(string Name, Action Check)> Cases()
    {
        yield return ("bedroom Story follows Corneo's own branches and lines", BedroomStoryFollowsTheBranchesAndLines);
        yield return ("an arrival is placed on its triangle's own plane", ArrivalHeightIsTheTrianglesPlane);
        yield return ("entry placement follows only what it can decide", EntryPlacementFailsClosed);
        yield return ("a transfer carries only the writes proven on its own path", TransferWritesAreProvenOnTheirPath);
        if (DataRoot is null)
        {
            yield break;
        }

        yield return ("Added Effect ledge native evidence", AddedEffectLedgeNativeEvidence);
        yield return ("Added Effect is walked to out through gidun_2 and back", AddedEffectIsWalkedToThroughTheNextCave);
        yield return ("a part of a field no way leads back to stays unavailable", UnreachablePartWithNoWayBackStaysUnavailable);
        yield return ("bedroom native evidence", BedroomNativeEvidence);
        yield return ("bedroom steps route from each branch's own arrival", BedroomStepsRouteFromEachArrival);
        yield return ("Train Graveyard and Sector 5 Talk pickups use the native talk range", TalkPickupsUseTheNativeTalkRange);
        yield return ("md8 scaffold entrances are scripted ladders, not detour starts", ScaffoldEntrancesAreScriptedLadders);
        yield return ("md8 scaffold items are walked to through their native chains", ScaffoldItemsAreWalkedToThroughNativeChains);
        yield return ("Northern Crater jump lines each take their own jump", CraterJumpLinesTakeTheirOwnJump);
        yield return ("Mega All is offered at its take-off lines, not its floating model", MegaAllIsOfferedAtItsTakeOffLines);
        yield return ("Ancient Forest throwing spots are the lines that write each creature's zone", ForestThrowingSpotsAreTheZoneLines);
        yield return ("Ancient Forest rewards: each routed natively or honestly not", ForestRewardsAreRoutedHonestly);
        yield return ("Seventh Heaven and fitting-room steps are offered only while their own line is on", LineStepsFollowTheirOwnLine);
        yield return ("Gongaga jungle's Turks are offered at the line that starts their scene", TurksAreOfferedAtTheirLine);
        yield return ("a throwing spot past its line survives its own crossing, and nothing else is kept", CrossingSpotsSurviveTheirOwnCrossing);
        yield return ("a LINE is reached on the engine's own touch test, not at the arrival distance", LinesAreReachedOnTheEngineTouchTest);
    }

    /// <summary>
    /// A counter or background LINE works while the leader touches it: 00637879 projects the
    /// leader onto the line, -1 off the segment, else the squared 3D distance; 00637ABB counts a
    /// touch only while that is below the square of the leader's collision radius. The controller
    /// used to stop at the player's arrival distance (80 by default) from such a target - 42.9 units
    /// off Sector 5's BLINE with a radius of 30, where OK does nothing. Now it stops only on the
    /// touch, at 80 and at a larger arrival distance alike, with auto walk walking until then;
    /// an Object reached this way is given its live segment and the leader's radius by the reader
    /// and is not offered when either cannot be read, when its line is off or when it is collected.
    /// </summary>
    private static void LinesAreReachedOnTheEngineTouchTest()
    {
        // The port against hand-worked cases of 00637879.
        var horizontal = new FieldNavigationTriggerLine(0, 0, 0, 100, 0, 0);
        Equal(900L, FieldNativeLineContact.DistanceSquared(horizontal, 50, 30, 0), "foot inside the segment: 30 squared");
        Equal(1300L, FieldNativeLineContact.DistanceSquared(horizontal, 50, 30, 20), "the height counts: 30, 20");
        Equal(-1L, FieldNativeLineContact.DistanceSquared(horizontal, 130, 0, 0), "past the end: off the segment, not the end's distance");
        Equal((true, false), (FieldNativeLineContact.Touches(horizontal, 50, 29, 0, 30), FieldNativeLineContact.Touches(horizontal, 50, 30, 0, 30)),
            "strictly inside the radius");

        var world = new Caves();

        // Sector 5's bed (175): the child's counter, BLINE7, through the NPC reader.
        const int events = 0x02404000;
        var radius = world.CollisionRadius(175);
        var model = events + FieldNavigationObjectReader.FieldEventDataStride;
        var npcBytes = new Dictionary<int, byte>
        {
            [FieldPositionReader.AddressFieldNumModels] = 2,
            [FieldNavigationObjectReader.AddressFieldModelIdArray + 5] = 1,
            [model + FieldNavigationObjectReader.VisibilityOffset] = 1,
            [events + FieldNavigationNpcReader.CollisionRadiusOffset] = (byte)radius
        };
        var npcs = new FieldNavigationNpcReader(
            address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events :
                address == model + FieldNavigationObjectReader.PositionXOffset ? 174 << 12 :
                address == model + FieldNavigationObjectReader.PositionYOffset ? -139 << 12 :
                address == model + FieldNavigationObjectReader.PositionZOffset ? -65 << 12 : 0,
            address => (short)(npcBytes.GetValueOrDefault(address) | (npcBytes.GetValueOrDefault(address + 1) << 8)),
            address => npcBytes.GetValueOrDefault(address),
            (_, _) => [],
            field => world.Scripts.ReadField(field).Npcs,
            isLineEnabled: entity => entity == 7);
        var bedside = world.LineOf(175, 7);
        foreach (var arrival in new[] { 80, 400 })
        {
            // The repro's start, (-150,-86) on t23, the room's west side.
            var walk = LineWalk(world, 175, FieldNavigationCategory.Npcs, npcs.ReadTargets, (-150, -86), arrival, bedside, radius, startTriangle: 23, leaveAfterArrival: true);
            Equal(true, walk.Stopped && walk.TouchingAtStop, $"175 bedside at arrival {arrival}: stops only on the touch ({walk.Log})");
            Equal(true, walk.InputUntilStop, $"175 bedside at arrival {arrival}: auto walk walked until then");
            Equal(true, walk.ResumedAfterLeaving, $"175 bedside at arrival {arrival}: walking off the line resumes navigation");
        }

        foreach (var unusable in new byte[] { 0, 1 })
        {
            npcBytes[events + FieldNavigationNpcReader.CollisionRadiusOffset] = unusable;
            Equal(0, npcs.ReadTargets(world.At(175, -150, -86, 23)).Count(t => t.StableId == "npc:175:5"),
                $"175: a leader radius of {unusable} cannot touch the counter, so it is not offered");
        }

        npcBytes[events + FieldNavigationNpcReader.CollisionRadiusOffset] = (byte)radius;
        Equal(0, npcs.ReadTargets(world.At(175, -150, -86, 23) with { ModelIndex = 7 }).Count(t => t.StableId == "npc:175:5"),
            "175: a leader model outside the model table is no geometry");

        // Native coordinates: 00637ABB floors the fixed point (>> 12). A leader at fixed x -4097
        // (-1.0002 units) is at -2, not the -1 a truncating division gives, and on BLINE's west
        // side that is one unit further off.
        var edge = new FieldNavigationTriggerLine(0, -100, 0, 0, 100, 0);
        var fractional = Position(175, -1, 0, 0, 0) with { NativeFixedPosition = new FieldNavigationFixedPosition(-4097, 0, 0) };
        Equal((-2, 0, 0), FieldNativeLineContact.NativeCoordinates(fractional), "the fixed point floors");
        Equal((true, false), (FieldNativeLineContact.Touches(edge, Position(175, -1, 0, 0, 0), 2), FieldNativeLineContact.Touches(edge, fractional, 2)),
            "a radius of 2: touching at the integer -1, not at the native -2");
        // Extreme positions wrap as the engine's 32-bit arithmetic does and never throw. The one
        // division that could (int.MinValue / -1) would need the squared length to wrap to -1, which
        // is 7 mod 8 and so no sum of three squares; the guard is kept all the same.
        foreach (var extreme in new[] { int.MinValue, int.MaxValue, -1 })
        {
            _ = FieldNativeLineContact.DistanceSquared(new FieldNavigationTriggerLine(-32768, -32768, -32768, 32767, 32767, 32767), extreme, extreme, extreme);
            _ = FieldNativeLineContact.Touches(new FieldNavigationTriggerLine(0, 0, 0, 1, 0, 0), extreme, 0, 0, 30);
        }

        // The Story step at Corneo's bedside (211, BLINE18, x 128, y 207 to 368): the actual Story
        // reader, Cloud chosen (3[162] bit 4) at moment 197, from Cloud's own arrival (103,38) t32
        // and from the north door (-159,-89).
        var storyMemory = new SaveMemory();
        storyMemory.SetGameMoment(197);
        storyMemory.SetBankByte(3, 162, 0x10);
        storyMemory.SetLeaderRadius(world.CollisionRadius(Bedroom));
        var story = new FieldStoryTargetReader(storyMemory.ReadInt32, storyMemory.ReadInt16, storyMemory.ReadByte,
            FieldStoryEventCatalog.CreateAllFields(), entity => entity == 18);
        var bedsideLine = world.LineOf(Bedroom, 18);
        var arrivals = world.ArrivalsInto(210, Bedroom);
        foreach (var arrivalPoint in arrivals)
        {
            foreach (var arrival in new[] { 80, 400 })
            {
                var walk = LineWalk(world, Bedroom, FieldNavigationCategory.Story, story.ReadTargets, (arrivalPoint.X, arrivalPoint.Y), arrival, bedsideLine,
                    world.CollisionRadius(Bedroom), startTriangle: arrivalPoint.Triangle, leaveAfterArrival: true);
                Equal(true, walk.Stopped && walk.TouchingAtStop && walk.InputUntilStop,
                    $"211 bedside from ({arrivalPoint.X},{arrivalPoint.Y}) at arrival {arrival}: walked to the touch of BLINE ({walk.Log})");
                Equal(true, walk.ResumedAfterLeaving, $"211 bedside from ({arrivalPoint.X},{arrivalPoint.Y}): leaving the line resumes");
            }
        }

        // Another party member chosen: no bedside step at all; the rug stays a crossing.
        storyMemory.SetBankByte(3, 162, 0x20);
        var otherBranch = new FieldStoryTargetReader(storyMemory.ReadInt32, storyMemory.ReadInt16, storyMemory.ReadByte,
            FieldStoryEventCatalog.CreateAllFields(), entity => entity == 16);
        var rugStep = otherBranch.ReadTargets(world.At(Bedroom, 103, 38, 32)).Single();
        Equal((CrossTheRug, true, 0), (rugStep.Label, rugStep.CompletesOnArrival, rugStep.LineActivationRadius),
            "Tifa or Aeris chosen: the rug, crossed, not a touch pause");

        // A background LINE Object: the Materia crystal of 82 (zz5), l1 along the crystal's south
        // side, 168 units long. Given its live segment, it is reached at whichever part of the line
        // the walk comes to, near its end from the west.
        var crystal = MateriaCaveObjectCatalog.Create().Single(o => o.FieldId == 82);
        var crystalLine = world.LineOf(82, 4);
        var crystalRadius = world.CollisionRadius(82);
        FieldNavigationTriggerLine? lineOn = crystalLine;
        var collected = false;
        var objectBytes = new Dictionary<int, byte>
        {
            [FieldPositionReader.AddressFieldNumModels] = 1,
            [events + FieldNavigationNpcReader.CollisionRadiusOffset] = (byte)crystalRadius
        };
        FieldNavigationObjectReader Objects() => new(
            address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events : 0,
            address => address == FieldNavigationObjectReader.AddressFieldBankBase + 49 ? (byte)(collected ? crystal.CollectedMask : 0) : objectBytes.GetValueOrDefault(address),
            _ => null, _ => null, [crystal], _ => lineOn is not null, readLiveLine: _ => lineOn);
        var start = world.Starts(82).Where(s => world.WalkingPlanner(82).TryBuildRoute(s,
                new FieldNavigationTarget(82, FieldNavigationCategory.Objects, "c", crystal.StaticX, crystal.StaticY, crystal.StaticZ, "c", TriggerLine: crystalLine), out _))
            .OrderBy(s => s.X).First();
        var emitted = Objects().ReadTargets(start).Single();
        Equal((crystalLine, crystalRadius, 0), (emitted.TriggerLine!.Value, emitted.LineActivationRadius, emitted.InteractionRadius),
            "82 crystal: the reader gives the live segment and the leader's radius");
        foreach (var arrival in new[] { 80, 400 })
        {
            var walk = LineWalk(world, 82, FieldNavigationCategory.Objects, position => Objects().ReadTargets(position), (start.X, start.Y), arrival, crystalLine, crystalRadius,
                startTriangle: start.TriangleId);
            Equal(true, walk.Stopped && walk.TouchingAtStop && walk.InputUntilStop, $"82 crystal at arrival {arrival}: walked to the touch ({walk.Log})");
            var fromStart = Math.Sqrt(Math.Pow(walk.StoppedAt.X - crystalLine.StartX, 2) + Math.Pow(walk.StoppedAt.Y - crystalLine.StartY, 2));
            Equal(true, fromStart < 60, $"82 crystal at arrival {arrival}: reached near the line's west end ({walk.StoppedAt}), not walked round to its middle");
        }

        lineOn = null;
        Equal(0, Objects().ReadTargets(start).Count, "82 crystal: a line that is off, or whose segment cannot be read, is not offered");
        lineOn = crystalLine;
        collected = true;
        Equal(0, Objects().ReadTargets(start).Count, "82 crystal: once collected it is not offered");
        collected = false;
        objectBytes[events + FieldNavigationNpcReader.CollisionRadiusOffset] = 0;
        Equal(0, Objects().ReadTargets(start).Count, "82 crystal: without the leader's radius it is not offered");
    }

    private sealed record LineWalkResult(bool Stopped, bool TouchingAtStop, (int X, int Y) StoppedAt, bool InputUntilStop, bool ResumedAfterLeaving, string Log);

    /// <summary>
    /// Selects the one target the provider offers, then walks the native route to it a unit at a
    /// time through UpdateLiveTracking and TryResolveAutomaticInput with the given arrival distance,
    /// until the controller pauses; optionally then walks back the way it came until it resumes.
    /// </summary>
    private static LineWalkResult LineWalk(
        Caves world, int field, FieldNavigationCategory category, Func<FieldPositionSnapshot, IReadOnlyList<FieldNavigationTarget>> provider,
        (int X, int Y) from, int arrival, FieldNavigationTriggerLine line, int radius, int startTriangle = -1, bool leaveAfterArrival = false)
    {
        var mesh = world.Mesh(field);
        var planner = world.WalkingPlanner(field);
        var source = category switch
        {
            FieldNavigationCategory.Npcs => new FieldNavigationTargetSource([], npcTargetProvider: provider),
            FieldNavigationCategory.Story => new FieldNavigationTargetSource([], storyTargetProvider: provider),
            _ => new FieldNavigationTargetSource([], objectTargetProvider: provider)
        };
        var controller = new FieldNavigationController(source, planner);
        var transform = new FieldNavigationControlTransform(-128);
        var lastZ = 0;
        FieldPositionSnapshot At(double x, double y, int hint)
        {
            // Resolved from the previous sample's triangle and height, so a stacked floor is not
            // swapped for another.
            var triangle = FieldWalkmeshPathfinder.ResolveTriangle(mesh, (int)Math.Round(x), (int)Math.Round(y), lastZ, hint);
            var z = triangle >= 0 ? FieldCrossFieldApproachResolver.SurfaceZ(mesh.Triangles[triangle], (int)Math.Round(x), (int)Math.Round(y)) : lastZ;
            lastZ = z;
            return Position(field, (int)Math.Round(x), (int)Math.Round(y), (ushort)Math.Max(0, triangle), z);
        }

        var start = world.At(field, from.X, from.Y, startTriangle);
        lastZ = start.Z;
        SelectCategory(controller, start, category);
        var target = provider(start).Single();
        Equal(true, planner.TryBuildRoute(start, target, out var plan), $"{field}: {planner.LastDiagnostic}");
        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
        Equal(true, controller.BeaconEnabled, $"{field}: navigation started");
        controller.NoteAutoWalkStarted();

        var corners = new List<(double X, double Y)> { (start.X, start.Y) };
        corners.AddRange(plan.Portals.Select(portal => ((portal.Left.X + portal.Right.X) / 2.0, (portal.Left.Y + portal.Right.Y) / 2.0)));
        corners.Add((plan.FinalApproach.X, plan.FinalApproach.Y));
        var samples = new List<(double X, double Y)>();
        for (var at = 1; at < corners.Count; at++)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Pow(corners[at].X - corners[at - 1].X, 2) + Math.Pow(corners[at].Y - corners[at - 1].Y, 2))));
            for (var step = 0; step < steps; step++)
            {
                samples.Add((corners[at - 1].X + (corners[at].X - corners[at - 1].X) * step / steps,
                    corners[at - 1].Y + (corners[at].Y - corners[at - 1].Y) * step / steps));
            }
        }

        samples.Add(corners[^1]);
        var log = new System.Text.StringBuilder();
        var clock = new DateTime(2026, 9, 25, 18, 0, 0, DateTimeKind.Utc);
        var index = 0;
        var hint = start.TriangleId;
        bool stopped = false, touching = false, input = true, resumed = false;
        (int X, int Y) stoppedAt = default;
        var visited = new List<FieldPositionSnapshot>();
        foreach (var (x, y) in samples)
        {
            var position = At(x, y, hint);
            hint = position.TriangleId;
            visited.Add(position);
            var result = controller.UpdateLiveTracking(position, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None), transform, false, arrival,
                observedAt: clock.AddMilliseconds(50 * index++));
            var hasInput = controller.TryResolveAutomaticInput(position, transform, arrival, out _);
            var onLine = FieldNativeLineContact.Touches(line, position.X, position.Y, position.Z, radius);
            if (result is { } spoken && spoken.Speech.Contains("reached", StringComparison.Ordinal))
            {
                stopped = true;
                touching = onLine;
                stoppedAt = (position.X, position.Y);
                log.Append($" [{position.X},{position.Y},{position.Z}] {spoken.Speech} touch={onLine}");
                break;
            }

            if (!hasInput && !onLine && controller.LastAutomaticInputHold is FieldAutoWalkHoldReason.Arrived or FieldAutoWalkHoldReason.PlayerAction)
            {
                input = false;
                log.Append($" [{position.X},{position.Y}] held {controller.LastAutomaticInputHold} off the line");
            }
        }

        if (stopped && leaveAfterArrival)
        {
            // Back the way it came: off the line, the pause ends.
            for (var back = visited.Count - 1; back >= 0 && !resumed; back--)
            {
                var result = controller.UpdateLiveTracking(visited[back], new FieldNavigationInputSnapshot(0, FieldNavigationInput.None), transform, false, arrival,
                    observedAt: clock.AddMilliseconds(50 * index++));
                resumed = result?.Speech.Contains("resumed", StringComparison.Ordinal) == true &&
                          !FieldNativeLineContact.Touches(line, visited[back].X, visited[back].Y, visited[back].Z, radius);
            }
        }

        return new LineWalkResult(stopped, touching, stoppedAt, input, resumed, log.ToString());
    }

    /// <summary>
    /// A crossed throwing spot is offered from one side of its zone line (or from the triangles
    /// a walk was proven from), so the object that offers it goes away as the walk crosses the
    /// line. The controller keeps the locked point through the crossing, the line itself (side
    /// 0) and the triangles' edge, and only while the walk is on its way: auto walk keeps its
    /// input until arrival, the destination never becomes the other side's point, and it ends
    /// as any other object does when the walk retreats past it, when an object without this
    /// metadata is withdrawn, or after arrival. The walks are the native routes on each field's
    /// walkmesh through the actual object reader and the shared controller.
    /// </summary>
    private static void CrossingSpotsSurviveTheirOwnCrossing()
    {
        var world = new Caves();
        var catalog = TownInteractionObjectCatalog.Create();
        var big0 = catalog.Where(o => o.FieldId == 620 && o.EntityId == 29).ToArray();
        var big0Line = world.LineOf(620, 29);

        // Both directions over big0thw, with a sample exactly on the line.
        foreach (var (startX, expectedX) in new[] { (150, 95), (60, 118) })
        {
            var walk = CrossingWalk(world, 620, big0, (startX, -506), onLine: big0Line);
            Equal(expectedX, walk.Target.X, $"from x {startX}: the point past the line on the other side");
            Equal(true, walk.Reached && walk.NoLongerAvailable is null, $"from x {startX}: reached, never withdrawn ({walk.Log})");
            Equal(true, walk.SameDestination, $"from x {startX}: the destination never becomes the other side's point");
            Equal(true, walk.InputUntilArrival, $"from x {startX}: auto walk kept its input until arrival");
            Equal(FieldNavigationObjectReader.SideOf(big0Line, walk.Target.X, walk.Target.Y),
                FieldNavigationObjectReader.SideOf(big0Line, walk.ReachedAt.X, walk.ReachedAt.Y), $"from x {startX}: reached past the line");
            // Native geometry: the partner line that resets zone 7 (bg0tho) is not crossed on the way.
            Equal(false, walk.Crossed(world.LineOf(620, 30)) && startX < 158, $"from x {startX}: bg0tho is not crossed after big0thw");

            // Arrival, then the object going: completed, not "no longer available".
            var removal = CrossingWalk(world, 620, big0, (startX, -506), onLine: big0Line, withdrawAfterArrival: true);
            Equal(true, removal.Completed, $"from x {startX}: removal after arrival completes ({removal.Log})");
        }

        // Side 0: big0thw has no integer point between its ends, so the same two points behind
        // a vertical line at x 106 on the same walkmesh give a sample on the line itself, where
        // the reader offers neither side.
        var vertical = new FieldNavigationTriggerLine(106, -321, -12, 106, -692, -10);
        var squared = big0.Select(o => o with { PlayerSideLine = vertical, CrossingLine = vertical, PlayerSide = -Math.Sign(o.StaticX - 106) }).ToArray();
        foreach (var startX in new[] { 150, 60 })
        {
            var onIt = CrossingWalk(world, 620, squared, (startX, -506), onLine: vertical);
            Equal(true, onIt.SawSideZero && onIt.Reached && onIt.NoLongerAvailable is null, $"side 0 from x {startX}: kept on the line ({onIt.Log})");
        }

        // Retreat: crossing to one side of the point and walking on past it is not an approach.
        var retreat = CrossingWalk(world, 620, big0, (150, -540), to: (40, -540));
        Equal(true, retreat.NoLongerAvailable is { } gone && gone < 104 - 20, $"walking on past it ends it ({retreat.Log})");

        // Triangle-offered: 623's strip points past rk0rt, frhol0, rk2lt and rk3rt are offered only
        // on the triangles a walk was proven from. Where the walk leaves them before it crosses,
        // the point is still kept; every one is reached.
        var leftSomewhere = false;
        foreach (var variant in catalog.Where(o => o.FieldId == 623 && o.CrossingLine is not null && o.RequiredPlayerTriangles is { Length: > 0 }))
        {
            var entity = variant.EntityId;
            foreach (var triangle in variant.RequiredPlayerTriangles!)
            {
                var centre = world.Mesh(623).Triangles[triangle].GetCentroid();
                var walk = CrossingWalk(world, 623, [variant], ((int)centre.X, (int)centre.Y), startTriangle: triangle);
                Equal(true, walk.Reached && walk.NoLongerAvailable is null && walk.InputUntilArrival,
                    $"623 e{entity} from t{triangle}: reached with input, never withdrawn ({walk.Log})");
                leftSomewhere |= walk.LeftTriangles;
            }
        }

        Equal(true, leftSomewhere, "a walk that leaves its offering triangles before arriving is among them");

        // An object without crossing metadata that is withdrawn mid-walk still goes.
        var unrelated = new FieldNavigationObjectDefinition(620, 99, FieldNavigationObjectKind.Named, Label: "Unrelated",
            TargetKind: FieldNavigationObjectTargetKind.Location, StaticX: 95, StaticY: -506, StaticZ: -11, InteractionRadiusOverride: 8,
            RequiredBank: 5, RequiredAddress: 200, RequiredMask: 0xFF, RequiredValue: 1);
        var withdrawn = CrossingWalk(world, 620, [unrelated], (150, -506), withdrawAtX: 130);
        Equal(true, withdrawn.NoLongerAvailable is not null, $"an ordinary object withdrawn mid-walk goes ({withdrawn.Log})");
    }

    private sealed record CrossingWalkResult(
        FieldNavigationTarget Target, bool Reached, (int X, int Y) ReachedAt, int? NoLongerAvailable, bool Completed,
        bool SameDestination, bool InputUntilArrival, bool SawSideZero, bool LeftTriangles, List<(double X, double Y)> Path, string Log)
    {
        public bool Crossed(FieldNavigationTriggerLine line)
        {
            for (var at = 1; at < Path.Count; at++)
            {
                if (GuideRouteRegressionTests.Crosses(Path[at - 1], Path[at], line))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Selects the one object offered at the start through the shared controller, then walks the
    /// native route to it (or to <paramref name="to"/>) a unit at a time, feeding each sample to
    /// UpdateLiveTracking and asking TryResolveAutomaticInput for the auto-walk direction.
    /// </summary>
    private static CrossingWalkResult CrossingWalk(
        Caves world, int field, FieldNavigationObjectDefinition[] definitions, (int X, int Y) from,
        (int X, int Y)? to = null, FieldNavigationTriggerLine? onLine = null, int startTriangle = -1,
        bool withdrawAfterArrival = false, int withdrawAtX = int.MinValue)
    {
        var mesh = world.Mesh(field);
        var live = new LiveObjects(world.CollisionRadius(field));
        foreach (var definition in definitions.Where(d => d.RequiredBank == 5))
        {
            live.SetTemporary(definition.RequiredAddress, 1);
        }

        var offered = true;
        IReadOnlyList<FieldNavigationTarget> Objects(FieldPositionSnapshot position) => offered ? live.Read(definitions, position) : [];
        var planner = world.WalkingPlanner(field);
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([], objectTargetProvider: Objects), planner);
        var transform = new FieldNavigationControlTransform(-128);
        FieldPositionSnapshot At(double x, double y, int hint)
        {
            var triangle = FieldWalkmeshPathfinder.ResolveTriangle(mesh, (int)Math.Round(x), (int)Math.Round(y), 0, hint);
            var z = triangle >= 0 ? FieldCrossFieldApproachResolver.SurfaceZ(mesh.Triangles[triangle], (int)Math.Round(x), (int)Math.Round(y)) : 0;
            return Position(field, (int)Math.Round(x), (int)Math.Round(y), (ushort)Math.Max(0, triangle), z);
        }

        var start = startTriangle >= 0 ? world.At(field, from.X, from.Y, startTriangle) : At(from.X, from.Y, -1);
        SelectCategory(controller, start, FieldNavigationCategory.Objects);
        var target = Objects(start).Single();
        Equal(true, planner.TryBuildRoute(start, target, out var plan), $"{field}: {planner.LastDiagnostic}");
        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
        Equal(true, controller.BeaconEnabled, $"{field}: navigation started");
        controller.NoteAutoWalkStarted();
        var identity = controller.CurrentRouteIdentity;

        var corners = new List<(double X, double Y)> { (start.X, start.Y) };
        if (to is { } end)
        {
            corners.Add((end.X, end.Y));
        }
        else
        {
            corners.AddRange(plan.Portals.Select(portal => ((portal.Left.X + portal.Right.X) / 2.0, (portal.Left.Y + portal.Right.Y) / 2.0)));
            corners.Add((plan.FinalApproach.X, plan.FinalApproach.Y));
        }

        var samples = new List<(double X, double Y)>();
        for (var at = 1; at < corners.Count; at++)
        {
            var (ax, ay) = corners[at - 1];
            var (bx, by) = corners[at];
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay))));
            for (var step = 0; step < steps; step++)
            {
                samples.Add((ax + (bx - ax) * step / steps, ay + (by - ay) * step / steps));
            }
        }

        samples.Add(corners[^1]);
        if (onLine is { } line)
        {
            // One sample exactly on the zone line, where the reader offers neither side.
            for (var at = 1; at < samples.Count; at++)
            {
                if (Crosses(samples[at - 1], samples[at], line))
                {
                    var (ax, ay) = samples[at - 1];
                    var (bx, by) = samples[at];
                    for (var t = 0.0; t <= 1.0; t += 0.01)
                    {
                        var (px, py) = (Math.Round(ax + (bx - ax) * t), Math.Round(ay + (by - ay) * t));
                        if (FieldNavigationObjectReader.SideOf(line, (int)px, (int)py) == 0)
                        {
                            samples.Insert(at, (px, py));
                            break;
                        }
                    }

                    break;
                }
            }
        }

        var clock = new DateTime(2026, 9, 25, 18, 0, 0, DateTimeKind.Utc);
        var log = new System.Text.StringBuilder();
        bool reached = false, completed = false, same = true, input = true, sideZero = false, left = false;
        (int X, int Y) reachedAt = default;
        int? gone = null;
        var hint = start.TriangleId;
        var index = 0;
        foreach (var (x, y) in samples)
        {
            var position = At(x, y, hint);
            hint = position.TriangleId;
            if (onLine is { } zoneLine && FieldNavigationObjectReader.SideOf(zoneLine, position.X, position.Y) == 0)
            {
                sideZero = sideZero || Objects(position).Count == 0;
            }

            if (definitions.Select(d => d.RequiredPlayerTriangles).FirstOrDefault(list => list is { Length: > 0 }) is { } triangles && !triangles.Contains(position.TriangleId))
            {
                left = true;
            }

            if (position.X <= withdrawAtX)
            {
                offered = false;
            }

            var result = controller.UpdateLiveTracking(position, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None), transform, false,
                observedAt: clock.AddMilliseconds(50 * index++));
            if (result is { } spoken)
            {
                log.Append($" [{position.X},{position.Y}] {spoken.Speech}");
                if (spoken.Speech.Contains("no longer available", StringComparison.Ordinal))
                {
                    gone = position.X;
                    break;
                }

                if (spoken.Speech.Contains("reached", StringComparison.Ordinal) && !reached)
                {
                    reached = true;
                    reachedAt = (position.X, position.Y);
                    if (withdrawAfterArrival)
                    {
                        offered = false;
                        var after = controller.UpdateLiveTracking(position, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None), transform, false,
                            observedAt: clock.AddMilliseconds(50 * index++));
                        log.Append($" [after] {after?.Speech}");
                        completed = after?.Speech.Contains("completed", StringComparison.Ordinal) == true;
                    }

                    break;
                }

                if (spoken.Speech.Contains("Navigation off", StringComparison.Ordinal))
                {
                    break;
                }
            }

            if (controller.BeaconEnabled)
            {
                same &= controller.CurrentRouteIdentity == identity;
                var distance = Math.Sqrt(Math.Pow(position.X - target.X, 2) + Math.Pow(position.Y - target.Y, 2));
                // Steering can hold a sample at a corner (NoClearDirection); what the crossing must
                // never do is leave auto walk without its target (NoRoute).
                if (distance > target.InteractionRadius + 4 &&
                    !controller.TryResolveAutomaticInput(position, transform, 0, out _) &&
                    controller.LastAutomaticInputHold == FieldAutoWalkHoldReason.NoRoute)
                {
                    input = false;
                    log.Append($" [{position.X},{position.Y}] no input ({controller.LastAutomaticInputHold})");
                }
            }
        }

        log.Append($" end=({samples[^1].X:F0},{samples[^1].Y:F0}) final={plan.FinalApproach} target=({target.X},{target.Y}) r={target.InteractionRadius} beacon={controller.BeaconEnabled} diag={controller.LastNavigationDiagnostic}");
        return new CrossingWalkResult(target, reached, reachedAt, gone, completed, same, input, sideZero, left, samples, log.ToString());
    }

    /// <summary>
    /// gonjun2 (515): Reno, Rude and Elena stand in the jungle with empty Talk scripts. line1's
    /// slot 2 (Move) shares its [OK] script, so walking onto the line runs it: before moment 598
    /// (IFSW 2[0] &lt;= 598) and while 3[129] bit 3 is clear it sets the bit and starts the scene
    /// that leads to the battle. Reno's Init hides him once the bit is set or the moment is 598 or
    /// later, so the NPC reader's own visibility test is the gate. The target the reader gives is
    /// the line, and the walk to it from each way in ends touching it.
    /// </summary>
    private static void TurksAreOfferedAtTheirLine()
    {
        var world = new Caves();
        // The script table (section 1: header 0x20, eight bytes per entity name, four per AKAO
        // offset, then 32 slot pointers per entity): line1's slots 1 and 2 are one pointer, and
        // 0060C94D starts slot 2 from the Move flag whatever it points at.
        var data = world.Decoded(515);
        var section = BitConverter.ToInt32(data, 6) + 4;
        var table = section + 0x20 + 8 * data[section + 2] + 4 * BitConverter.ToUInt16(data, section + 6);
        int Pointer(int entity, int slot) => BitConverter.ToUInt16(data, table + (entity * 32 + slot) * 2);
        Equal(Pointer(1, 1), Pointer(1, 2), "line1's Move is its [OK]");
        var okay = world.Scripts.ReadScriptOpcodes(515, 1, 1).Select(Hex).ToArray();
        Equal(true, okay.Contains("1620000056020512") && okay.Contains("143081030A0C") && okay.Contains("82308103") && okay.Contains("0102C3"),
            "moment <= 598 and 3[129] bit 3 clear: set it and start the scene");
        var reno = world.Scripts.ReadScriptOpcodes(515, 11, 0).Select(Hex).ToArray();
        Equal(true, reno.Contains("143081030909") && reno.Contains("1620000056020407") && reno.Count(hex => hex == "A400") == 2,
            "Reno is hidden once 3[129] bit 3 is set or from moment 598");
        Equal(false, world.Scripts.ReadAllScriptOpcodes(515).SelectMany(s => s.Opcodes).Any(op => op.Opcode == 0xD1), "line1 is never switched off");
        Equal(1, world.Scripts.ReadScriptOpcodes(515, 11, 1).Count, "and his own Talk is empty");

        const int events = 0x02404000;
        var radius = world.CollisionRadius(515);
        var bytes = new Dictionary<int, byte>
        {
            [FieldPositionReader.AddressFieldNumModels] = 12,
            [FieldNavigationObjectReader.AddressFieldModelIdArray + 11] = 9,
            [events + FieldNavigationNpcReader.CollisionRadiusOffset] = (byte)radius,
            [events + 9 * FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset] = 1
        };
        var lineOn = true;
        var reader = new FieldNavigationNpcReader(
            address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events : 0,
            address => (short)(bytes.GetValueOrDefault(address) | (bytes.GetValueOrDefault(address + 1) << 8)),
            address => bytes.GetValueOrDefault(address),
            (_, _) => [],
            field => world.Scripts.ReadField(field).Npcs,
            isLineEnabled: entity => entity == 1 && lineOn);
        var arrivals = world.ArrivalsInto(514, 515).Concat(world.ArrivalsInto(516, 515)).ToArray();
        Equal(true, arrivals.Length > 0, "515 is entered");
        var line = world.LineOf(515, 1);
        foreach (var arrival in arrivals)
        {
            var start = world.At(515, arrival.X, arrival.Y, arrival.Triangle);
            var target = reader.ReadTargets(start).Single(t => t.Label == "Reno and Rude");
            Equal((1, (FieldNavigationTriggerLine?)line), (target.TriggerEntityId, target.TriggerLine), "the reader's target is line1");
            Equal(true, world.Planner(515).TryBuildRoute(start, target, out var route), $"from ({arrival.X},{arrival.Y}): {world.Planner(515).LastDiagnostic}");
            Equal(true, DistanceToSegment(line, route.FinalApproach.X, route.FinalApproach.Y) < radius, "the walk ends touching line1, where its Move runs");
        }

        var somewhere = world.At(515, arrivals[0].X, arrivals[0].Y, arrivals[0].Triangle);
        lineOn = false;
        Equal(0, reader.ReadTargets(somewhere).Count(t => t.Label == "Reno and Rude"), "not while line1 is off");
        lineOn = true;
        bytes[events + 9 * FieldNavigationObjectReader.FieldEventDataStride + FieldNavigationObjectReader.VisibilityOffset] = 0;
        Equal(0, reader.ReadTargets(somewhere).Count(t => t.Label == "Reno and Rude"), "nor once Reno is hidden: the scene is had, or it is too late");
    }

    /// <summary>
    /// mds7pb_1 (154) switches border1 and border4 off in their Main every visit and back on only
    /// from Cloud's scripts 5 and 3; mkt_s1 (201) has line01 on only while 1[160] bit 7 and
    /// 1[161] &amp; 0xE0 are set before moment 192. Each of these Story steps is run by nothing but
    /// its own line's Move script, so each is offered only while that line is on.
    /// </summary>
    private static void LineStepsFollowTheirOwnLine()
    {
        var scripts = new FieldScriptNavigationCatalog(DataRoot!);
        AssertOpcode(scripts, 154, 14, 0, 14, "D100", "border1's Main switches it off");
        AssertOpcode(scripts, 154, 17, 0, 14, "D100", "border4's Main switches it off");
        AssertOpcode(scripts, 154, 14, 8, 2, "D101", "border1's script 8 switches it on");
        Equal(true, scripts.ReadScriptOpcodes(154, 18, 5).Any(op => Hex(op) == "020EC8"), "Cloud's script 5 asks border1 for its script 8");
        Equal(true, scripts.ReadScriptOpcodes(154, 18, 3).Any(op => Hex(op) == "0211C8"), "Cloud's script 3 asks border4 for its script 8");
        var line01 = scripts.ReadScriptOpcodes(201, 3, 0).Select(Hex).ToArray();
        Equal(true, line01.Contains("1410A0800619") && line01.Contains("1410A1E0060F") && line01.Contains("D101") &&
            line01.Contains("16200000C0000403"), "line01 is on only with 1[160] bit 7, 1[161] & 0xE0 and moment below 192");
        Equal(1, scripts.ReadScriptOpcodes(201, 3, 1).Count, "line01's [OK] is empty: walking in runs it");

        var rows = FieldStoryEventCatalog.CreateAllFields();
        foreach (var (field, label, line) in new[]
                 {
                     (154, "Approach the front door; Barret and Avalanche are entering", 14),
                     (154, "Walk toward the front door; Tifa will stop Cloud about the promise", 17),
                     (201, "Enter the fitting room and choose to change clothes", 3)
                 })
        {
            Equal(line, rows.Single(row => row.FieldId == field && row.Label == label).RequiredEnabledLineEntityId, $"{label}: gated on its own line");
        }
    }

    private static string? DataRoot => Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") is { Length: > 0 } root
        ? root
        : null;

    // --- 546 gidun_1 / 547 gidun_2 --------------------------------------------------------------

    private const int CaveFloor = 546;
    private const int NextCave = 547;
    private const string AddedEffect = "Added Effect Materia";
    private const string FloorWayOut = "script-exit:546:24:547";
    private const string LedgeWayBack = "script-exit:547:21:546";

    /// <summary>The player's own position when "Route unavailable" was said, 2026-09-25 14:14:02Z.</summary>
    private static FieldPositionSnapshot LoggedFloorPosition => Position(CaveFloor, -204, 724, 169, -98);

    private static FieldNavigationTarget AddedEffectTarget => new(
        CaveFloor, FieldNavigationCategory.Objects, AddedEffect, -637, 687, 121, "object:546:22:Materia",
        TriggerEntityId: 22, InteractionRadius: 40);

    private static void AddedEffectLedgeNativeEvidence()
    {
        var world = new Caves();
        // TAKARAA's Main places the materia at (-637,687,121) on triangle 20; its Talk gives it
        // and sets 1[58] bit 1, the catalog's collected flag.
        AssertOpcode(world.Scripts, CaveFloor, 22, 0, 2, "A5000083FDAF0279001400", "TAKARAA stands at (-637,687) t20");
        Equal(true, world.Scripts.ReadScriptOpcodes(CaveFloor, 22, 1).Any(op => Hex(op) == "82103A01"), "its Talk sets 1[58] bit 1");
        var floor = world.Mesh(CaveFloor);
        var ledge = Walkable(floor, 20);
        Equal(23, ledge.Count, "the ledge is twenty-three triangles");
        Equal(false, ledge.Contains(169), "and the logged floor triangle is not one of them");
        Equal(true, ledge.Contains(6), "547's LINEJA lands on it");
        // The ways between the two caves, from their own lines' Go scripts.
        AssertOpcode(world.Scripts, CaveFloor, 24, 5, 0, "602302C2014EFA9A0094", "546 LINEJB map jumps to 547 (450,-1458) t154");
        AssertOpcode(world.Scripts, CaveFloor, 23, 5, 0, "60230257FE3AFA700080", "546 LINEJA, on the ledge, to 547 (-425,-1478) t112");
        AssertOpcode(world.Scripts, NextCave, 21, 5, 0, "602202B1FEA302060000", "547 LINEJA map jumps to 546 (-335,675) t6");
        AssertOpcode(world.Scripts, NextCave, 22, 5, 0, "602202DE001E03920000", "547 LINEJB back to 546 (222,798) t146");
        Equal(false, ledge.Contains(146), "LINEJB's landing is the floor");
        Equal(1, Components(world.Mesh(NextCave)), "gidun_2 is one walkmesh, both passages on it");
        Equal(false, world.Scripts.ReadField(CaveFloor).Transitions.Any(), "546 has no ladder or jump onto the ledge");

        // From the logged position the ledge is out of reach, the passage open or shut...
        var planner = world.Planner(CaveFloor);
        foreach (var shut in new[] { true, false })
        {
            world.PassageShut = shut;
            Equal(false, planner.TryBuildRoute(LoggedFloorPosition, AddedEffectTarget, out _), $"passage shut={shut}: the floor cannot reach the materia");
            Equal(false, planner.LastFailureWasNativeBoundary, $"passage shut={shut}: and no lock is what stands in the way");
        }

        world.PassageShut = false;
        // ...and from every native arrival into gidun_1; only 547's LINEJA reaches it.
        var arrivals = new[] { 534, NextCave }.SelectMany(from => world.ArrivalsInto(from, CaveFloor).Select(a => (from, a))).ToArray();
        Equal(true, arrivals.Length >= 3, $"gidun_1 has its arrivals ({arrivals.Length})");
        foreach (var (from, arrival) in arrivals)
        {
            var start = world.At(CaveFloor, arrival.X, arrival.Y, arrival.Triangle);
            Equal(ledge.Contains(arrival.Triangle), planner.TryBuildRoute(start, AddedEffectTarget, out _),
                $"from {from}'s arrival ({arrival.X},{arrival.Y}) t{arrival.Triangle}: {planner.LastDiagnostic}");
        }

        Equal(true, arrivals.Any(entry => entry.from == NextCave && entry.a.Triangle == 6), "547's LINEJA arrival is one of them");
    }

    private static void AddedEffectIsWalkedToThroughTheNextCave()
    {
        var world = new Caves();
        var resolver = world.Resolver();
        var plan = resolver.Resolve(LoggedFloorPosition, AddedEffectTarget, world.Exits(CaveFloor));
        Equal($"{CaveFloor}:{FloorWayOut}:{NextCave}:{LedgeWayBack}",
            plan is null ? $"none ({resolver.LastDiagnostic})" : $"{plan.OriginFieldId}:{plan.OutboundExitStableId}:{plan.ViaFieldId}:{plan.ReturnExitStableId}",
            "out by the floor's passage, back by gidun_2's other one");

        // Before the opening is looked into, the passage is shut and there is no way yet.
        world.PassageShut = true;
        Equal(null, resolver.Resolve(LoggedFloorPosition, AddedEffectTarget, world.Exits(CaveFloor)),
            $"a shut passage is not a way: {resolver.LastDiagnostic}");
        world.PassageShut = false;

        // The whole walk through the controller both runtimes share.
        var source = new FieldNavigationTargetSource(
            [],
            objectTargetProvider: position => position.FieldId == CaveFloor ? [AddedEffectTarget] : [],
            exitTargetProvider: position => world.Exits(position.FieldId));
        FieldNavigationController Controller() => new(source, world.SharedPlanner)
        {
            CrossFieldApproach = (position, goal, exits) => resolver.Resolve(position, goal, exits)
        };
        var noInput = new FieldNavigationInputSnapshot(0, FieldNavigationInput.None);
        var noRotation = new FieldNavigationControlTransform(-128);
        var now = new DateTime(2026, 9, 25, 14, 14, 11, DateTimeKind.Utc);
        var controller = Controller();
        SelectCategory(controller, LoggedFloorPosition, FieldNavigationCategory.Objects);
        var started = controller.HandleAction(FieldNavigationAction.ToggleBeacon, LoggedFloorPosition, noRotation)?.Speech ?? string.Empty;
        Equal(true, started.StartsWith($"{AddedEffect} is reached from here only by going out through Exit to Cave of the Gi", StringComparison.Ordinal),
            $"the first leg says why and where: {started}");
        Equal(true, controller.BeaconEnabled, "navigation is on for the way out");
        Equal(AddedEffect, controller.CrossFieldGoalLabel, "and still for the materia");
        controller.NoteAutoWalkStarted();

        // Walked up to LINEJB and across it: gidun_2, at the native arrival.
        _ = controller.UpdateLiveTracking(world.Near(CaveFloor, 236, 1290), noInput, noRotation, isSuppressed: false, 80,
            observedAt: now.AddSeconds(4));
        var inNextCave = world.At(NextCave, 450, -1458, 154);
        _ = controller.UpdateLiveTracking(inNextCave, noInput, noRotation, isSuppressed: true, 80, observedAt: now.AddSeconds(5));
        Equal(false, controller.BeaconEnabled, "the way out is finished by the field change");
        var second = controller.UpdateLiveTracking(inNextCave, noInput, noRotation, isSuppressed: false, 80, observedAt: now.AddSeconds(6))?.Speech ?? string.Empty;
        Equal(true, second.StartsWith($"{AddedEffect}: now through", StringComparison.Ordinal), $"the second leg starts by itself: {second} [{controller.LastNavigationDiagnostic}; goal={controller.CrossFieldGoalLabel}]");
        Equal(true, controller.TryConsumeHeldAutoWalkRequest(), "and is walked, because the first one was");
        Equal(true, controller.BeaconEnabled, "navigation is on in gidun_2");

        // Up to 547's LINEJA and across it: back in gidun_1, on the ledge.
        _ = controller.UpdateLiveTracking(world.Near(NextCave, -400, -1600), noInput, noRotation, isSuppressed: false, 80,
            observedAt: now.AddSeconds(19));
        var onLedge = world.At(CaveFloor, -335, 675, 6);
        _ = controller.UpdateLiveTracking(onLedge, noInput, noRotation, isSuppressed: true, 80, observedAt: now.AddSeconds(20));
        var third = controller.UpdateLiveTracking(onLedge, noInput, noRotation, isSuppressed: false, 80, observedAt: now.AddSeconds(21))?.Speech ?? string.Empty;
        Equal(true, third.StartsWith($"Navigation on. {AddedEffect}.", StringComparison.Ordinal), $"and the materia itself last: {third}");
        Equal(true, controller.BeaconEnabled, "navigation is on to the materia");
        Equal(true, controller.TryConsumeHeldAutoWalkRequest(), "walked as well");
        Equal(string.Empty, controller.CrossFieldGoalLabel, "the approach is over once its target is routed");

        // Going anywhere else on the way ends it without starting anything.
        var wandering = Controller();
        SelectCategory(wandering, LoggedFloorPosition, FieldNavigationCategory.Objects);
        _ = wandering.HandleAction(FieldNavigationAction.ToggleBeacon, LoggedFloorPosition, noRotation);
        var elsewhere = world.At(534, 0, 0, 0);
        _ = wandering.UpdateLiveTracking(elsewhere, noInput, noRotation, isSuppressed: false, 80, observedAt: now.AddSeconds(5));
        Equal(null, wandering.UpdateLiveTracking(elsewhere, noInput, noRotation, isSuppressed: false, 80, observedAt: now.AddSeconds(6))?.Speech,
            "leaving by another way starts no leg");
        Equal(string.Empty, wandering.CrossFieldGoalLabel, "and ends the approach");

        // Turning navigation off in gidun_2 ends it too.
        var stopped = Controller();
        SelectCategory(stopped, LoggedFloorPosition, FieldNavigationCategory.Objects);
        _ = stopped.HandleAction(FieldNavigationAction.ToggleBeacon, LoggedFloorPosition, noRotation);
        _ = stopped.UpdateLiveTracking(world.Near(CaveFloor, 236, 1290), noInput, noRotation, isSuppressed: false, 80,
            observedAt: now.AddSeconds(4));
        _ = stopped.UpdateLiveTracking(inNextCave, noInput, noRotation, isSuppressed: false, 80, observedAt: now.AddSeconds(5));
        _ = stopped.UpdateLiveTracking(inNextCave, noInput, noRotation, isSuppressed: false, 80, observedAt: now.AddSeconds(6));
        Equal("Navigation off.", stopped.HandleAction(FieldNavigationAction.ToggleBeacon, inNextCave, noRotation)?.Speech, "B stops the second leg");
        _ = stopped.UpdateLiveTracking(onLedge, noInput, noRotation, isSuppressed: false, 80, observedAt: now.AddSeconds(20));
        Equal(null, stopped.UpdateLiveTracking(onLedge, noInput, noRotation, isSuppressed: false, 80, observedAt: now.AddSeconds(21))?.Speech,
            "and nothing starts behind it");

        // P turns auto walk off on the way while spoken navigation carries on: the later legs
        // still start, spoken, and crossing a doorway by hand switches nothing back on.
        var turnedOff = Controller();
        SelectCategory(turnedOff, LoggedFloorPosition, FieldNavigationCategory.Objects);
        _ = turnedOff.HandleAction(FieldNavigationAction.ToggleBeacon, LoggedFloorPosition, noRotation);
        turnedOff.NoteAutoWalkStarted();
        turnedOff.NoteAutoWalkStopped();
        Equal(true, turnedOff.BeaconEnabled, "spoken navigation stays on after P");
        _ = turnedOff.UpdateLiveTracking(world.Near(CaveFloor, 236, 1290), noInput, noRotation, isSuppressed: false, 80,
            observedAt: now.AddSeconds(4));
        _ = turnedOff.UpdateLiveTracking(inNextCave, noInput, noRotation, isSuppressed: true, 80, observedAt: now.AddSeconds(5));
        var spokenOnly = turnedOff.UpdateLiveTracking(inNextCave, noInput, noRotation, isSuppressed: false, 80,
            observedAt: now.AddSeconds(6))?.Speech ?? string.Empty;
        Equal(true, spokenOnly.StartsWith($"{AddedEffect}: now through", StringComparison.Ordinal), $"the next leg still starts: {spokenOnly}");
        Equal(false, turnedOff.TryConsumeHeldAutoWalkRequest(), "but auto walk does not come back on");
        _ = turnedOff.UpdateLiveTracking(world.Near(NextCave, -400, -1600), noInput, noRotation, isSuppressed: false, 80,
            observedAt: now.AddSeconds(19));
        _ = turnedOff.UpdateLiveTracking(onLedge, noInput, noRotation, isSuppressed: true, 80, observedAt: now.AddSeconds(20));
        _ = turnedOff.UpdateLiveTracking(onLedge, noInput, noRotation, isSuppressed: false, 80, observedAt: now.AddSeconds(21));
        Equal(true, turnedOff.BeaconEnabled, "the materia leg starts spoken");
        Equal(false, turnedOff.TryConsumeHeldAutoWalkRequest(), "and nothing restarts the walk on the last leg either");

        // Without the resolver the controller says what 0.7.0 said.
        var plain = new FieldNavigationController(source, world.SharedPlanner);
        SelectCategory(plain, LoggedFloorPosition, FieldNavigationCategory.Objects);
        Equal($"Route unavailable to {AddedEffect}. Navigation off.",
            plain.HandleAction(FieldNavigationAction.ToggleBeacon, LoggedFloorPosition, noRotation)?.Speech, "0.7.0's answer");
    }

    /// <summary>
    /// colne_6's bed is a part of the field nothing walks onto: no way into the bedroom lands
    /// there and no line jumps there, so Corneo's own model, which sits on it, stays unavailable
    /// rather than being given a detour. The trapdoor pit, which the rug's jump does reach, is
    /// routed directly and is given no detour either.
    /// </summary>
    private static void UnreachablePartWithNoWayBackStaysUnavailable()
    {
        var world = new Caves();
        var resolver = world.Resolver();
        var corneo = new FieldNavigationTarget(Bedroom, FieldNavigationCategory.Npcs, "Corneo", -14, 220, -151, "npc:211:12",
            TriggerEntityId: 12, InteractionRadius: 40);
        var arrival = world.At(Bedroom, 103, 38, 32);
        Equal(false, world.Planner(Bedroom).TryBuildRoute(arrival, corneo, out _), "Corneo on the bed is not walkable to");
        Equal(null, resolver.Resolve(arrival, corneo, world.Exits(Bedroom)), $"and no way back lands on the bed: {resolver.LastDiagnostic}");
        var pit = new FieldNavigationTarget(Bedroom, FieldNavigationCategory.Objects, "pit", 33, 9, -639, "object:211:pit");
        Equal(true, world.Planner(Bedroom).TryBuildRoute(arrival, pit, out _), "the pit is reached by the rug's own jump");
        Equal(null, resolver.Resolve(arrival, pit, world.Exits(Bedroom)), "so it needs no detour");
        Equal("reachable directly", resolver.LastDiagnostic, "and the resolver says why");
    }

    // --- 211 colne_6 ---------------------------------------------------------------------------

    private const int Bedroom = 211;
    private const string TalkToCorneo = "Go to Corneo's bedside and press OK to talk to him";
    private const string CrossTheRug = "Cross the rug in the middle of the bedroom";
    private static readonly FieldNavigationTriggerLine Bedside = new(128, 368, -237, 128, 207, -237);
    private static readonly FieldNavigationTriggerLine Rug = new(0, 83, -237, 76, -41, -237);

    private static void BedroomStoryFollowsTheBranchesAndLines()
    {
        var memory = new SaveMemory();
        memory.SetGameMoment(197);
        memory.SetLeaderRadius(30);
        var lines = new Dictionary<int, bool>();
        var reader = new FieldStoryTargetReader(memory.ReadInt32, memory.ReadInt16, memory.ReadByte,
            FieldStoryEventCatalog.CreateAllFields(), entity => lines.TryGetValue(entity, out var on) && on);
        var at = Position(Bedroom, 103, 38, 32, -237);
        string Labels() => string.Join(" | ", reader.ReadTargets(at).Select(target => target.Label));

        // Cloud chosen, on arrival: AD's Main has switched the rug off, BLINE is on.
        memory.SetBankByte(3, 162, 0x10);
        lines[16] = false;
        lines[18] = true;
        Equal(TalkToCorneo, Labels(), "Cloud's branch: the bedside, and not the rug");
        var talk = reader.ReadTargets(at).Single();
        Equal(Bedside, talk.TriggerLine!.Value, "the bedside is BLINE's own line");
        Equal(false, talk.CompletesOnArrival, "standing there is not the step; OK is");
        Equal((30, 0), (talk.LineActivationRadius, talk.InteractionRadius), "reached on the touch of BLINE, with the leader's own radius");

        // BLINE's [OK] switches it off; AD2's script 5 switches the rug on.
        lines[18] = false;
        lines[16] = true;
        memory.SetBankByte(3, 163, 0x02);
        Equal(CrossTheRug, Labels(), "after the conversation, the rug");
        Equal(Rug, reader.ReadTargets(at).Single().TriggerLine!.Value, "the rug is the trapdoor LINE");
        Equal((true, 0), (reader.ReadTargets(at).Single().CompletesOnArrival, reader.ReadTargets(at).Single().LineActivationRadius),
            "the rug is still crossed, not a pause on a touch");

        // Tifa or Aeris chosen: the scene plays on entry, and then the rug is the step.
        foreach (var chosen in new byte[] { 0x20, 0x40 })
        {
            memory.SetBankByte(3, 162, chosen);
            memory.SetBankByte(3, 163, 0);
            lines[18] = false;
            lines[16] = false;
            Equal(string.Empty, Labels(), $"0x{chosen:X2}: nothing while the scene still has the rug off");
            lines[16] = true;
            Equal(CrossTheRug, Labels(), $"0x{chosen:X2}: the rug once it is on");
        }

        // A line state that cannot be read offers neither: a dead line is not a step.
        memory.SetBankByte(3, 162, 0x10);
        lines.Clear();
        Equal(string.Empty, Labels(), "unknown line state offers nothing");

        // Without the leader's radius there is no reach to measure the bedside by.
        lines[18] = true;
        memory.SetBankByte(3, 163, 0);
        memory.SetLeaderRadius(0);
        Equal(string.Empty, Labels(), "no leader radius: the bedside is not offered");
    }

    private static void BedroomNativeEvidence()
    {
        var scripts = new FieldScriptNavigationCatalog(DataRoot!);
        AssertOpcode(scripts, Bedroom, 9, 0, 7, "0110C7", "AD's Main asks the rug for its script 7");
        AssertOpcode(scripts, Bedroom, 16, 7, 0, "D100", "which is LINOFF");
        AssertOpcode(scripts, Bedroom, 16, 8, 0, "D101", "and script 8 LINON");
        Equal(true, scripts.ReadScriptOpcodes(Bedroom, 10, 5).Any(op => Hex(op) == "0310C8"), "AD2's script 5 switches the rug on");
        AssertOpcode(scripts, Bedroom, 16, 5, 4, "030AC6", "walking onto the rug runs AD2's script 6");
        Equal(true, scripts.ReadScriptOpcodes(Bedroom, 10, 6).Any(op => Hex(op).StartsWith("600A01", StringComparison.Ordinal)),
            "which drops the party to 266");
        // BLINE: its line beside the bed, on only for Cloud's branch before the conversation.
        AssertOpcode(scripts, Bedroom, 18, 0, 6, "D08000700113FF8000CF0013FF", "BLINE is (128,368)-(128,207)");
        AssertOpcode(scripts, Bedroom, 18, 0, 19, "1430A3020605", "BLINE's Main: off once 3[163] bit 1 is set");
        AssertOpcode(scripts, Bedroom, 18, 0, 29, "1430A2100603", "and off unless 3[162] bit 0x10, Cloud chosen");
        AssertOpcode(scripts, Bedroom, 18, 1, 4, "D100", "its [OK] switches itself off");
        Equal(true, scripts.ReadScriptOpcodes(Bedroom, 18, 1).Any(op => Hex(op) == "030C8C"), "and runs Corneo's conversation");
        Equal(true, scripts.ReadScriptOpcodes(Bedroom, 12, 12).Any(op => op.Opcode == 0x48), "which asks its questions");
        Equal(1, scripts.ReadScriptOpcodes(Bedroom, 12, 1).Count, "Corneo's own Talk is empty");
        // 210's COLNEO script 4 is Cloud's branch into the bedroom.
        Equal(true, scripts.ReadScriptOpcodes(210, 7, 4).Any(op => op.Opcode == 0x60 && op.Bytes.Count == 10 &&
                BitConverter.ToUInt16(op.Bytes.ToArray(), 1) == Bedroom &&
                BitConverter.ToInt16(op.Bytes.ToArray(), 3) == 103 && BitConverter.ToInt16(op.Bytes.ToArray(), 5) == 38),
            "Cloud's branch: 210's COLNEO script 4 map jumps to the bedroom at (103,38)");
    }

    private static void BedroomStepsRouteFromEachArrival()
    {
        var world = new Caves();
        var planner = world.Planner(Bedroom);
        var arrivals = world.ArrivalsInto(210, Bedroom);
        var cloudsArrival = arrivals.Single(arrival => arrival.X == 103 && arrival.Y == 38);
        var doorArrival = arrivals.Single(arrival => arrival.X == -159 && arrival.Y == -89);
        var bedside = new FieldNavigationTarget(Bedroom, FieldNavigationCategory.Story, TalkToCorneo, 128, 288, -237,
            "story:211:bline", TriggerLine: Bedside);
        var rug = new FieldNavigationTarget(Bedroom, FieldNavigationCategory.Story, CrossTheRug, 38, 21, -237,
            "story:211:rug", TriggerLine: Rug, CompletesOnArrival: true);
        foreach (var (name, arrival) in new[] { ("Cloud's arrival", cloudsArrival), ("the north door", doorArrival) })
        {
            var start = world.At(Bedroom, arrival.X, arrival.Y, arrival.Triangle);
            foreach (var target in new[] { bedside, rug })
            {
                Equal(true, planner.TryBuildRoute(start, target, out var route), $"{name} to {target.Label}: {planner.LastDiagnostic}");
                Equal(target.TriggerLine, route.TargetTriggerLine, $"{name}: {target.Label} ends on its own line");
            }
        }

        // Corneo's own model is on the bed, off the floor: routing to him was never the way.
        AssertOpcode(world.Scripts, Bedroom, 12, 0, 2, "A50000F2FFDC0069FF3E00", "Corneo sits on the bed at (-14,220,-151) t62");
        Equal(false, Walkable(world.Mesh(Bedroom), cloudsArrival.Triangle).Contains(62), "and the bed is not the floor's walkmesh");
    }

    // --- follow-up: Talk pickups and scripted entrances -----------------------------------------

    private static void ArrivalHeightIsTheTrianglesPlane()
    {
        var slope = new FieldWalkmeshTriangle(0, new(0, 0, 0), new(100, 0, 100), new(0, 100, 0), -1, -1, -1);
        Equal(50, FieldCrossFieldApproachResolver.SurfaceZ(slope, 50, 10), "halfway up the slope");
        Equal(33, (int)Math.Round(slope.GetCentroid().Z), "which the centre would have put at 33");
        var edgeOn = new FieldWalkmeshTriangle(1, new(0, 0, 0), new(0, 0, 100), new(0, 0, 50), -1, -1, -1);
        Equal(50, FieldCrossFieldApproachResolver.SurfaceZ(edgeOn, 0, 0), "a triangle seen edge-on falls back to its centre");
    }

    /// <summary>
    /// mds7st1's Hi-Potion (144 e16), mds7st2's Ether (145 e20) and min51_1's television (174
    /// e7) are Talk interactions - TLKON and TALKR 120 in their Main, the pickup or messages in
    /// their Talk - and each stands off the floor's walkmesh. 0.7.0 gave them the fixed object
    /// radius of 48, which no native arrival can route within; the native talk range can.
    /// </summary>
    private static void TalkPickupsUseTheNativeTalkRange()
    {
        var world = new Caves();
        foreach (var (field, entity, x, y, z, starts) in new[]
                 {
                     (144, 16, 1349, -2662, 0, new[] { (1674, 511, 110), (1730, -3339, 87) }),
                     (145, 20, -481, 755, 0, new[] { (1537, 711, 128), (-2075, 3481, 4), (-1841, 2766, 34) }),
                     (174, 7, -230, 155, -255, new[] { (56, 339, 62), (71, -202, 4) })
                 })
        {
            var definition = FieldNavigationObjectCatalog.CreateAllFields().Single(o => o.FieldId == field && o.EntityId == entity);
            Equal(true, definition.UsesTalkInteraction, $"{field} e{entity} is a Talk interaction");
            var main = world.Scripts.ReadScriptOpcodes(field, entity, 0).Select(Hex).ToArray();
            Equal(true, main.Contains("7E00") && main.Contains("C50078"), $"{field} e{entity}: TLKON and TALKR 120");
            Equal(true, world.Scripts.ReadScriptOpcodes(field, entity, 1).Count > 1, $"{field} e{entity}: its Talk does something");
            // The native range: the player's collision radius and TALKR, both scaled by the
            // field's own model scale (0060BCFA, 0061813D). At 48 nothing reaches.
            var scale = BitConverter.ToInt16(world.Decoded(field), BitConverter.ToInt32(world.Decoded(field), 6) + 4 + 8);
            var native = (30 * scale + 120 * scale) / 512;
            foreach (var (sx, sy, triangle) in starts)
            {
                var start = world.At(field, sx, sy, triangle);
                foreach (var (radius, expected) in new[] { (FieldNavigationObjectReader.DefaultInteractionRadius, false), (native, true) })
                {
                    var target = new FieldNavigationTarget(field, FieldNavigationCategory.Objects, "pickup", x, y, z, $"object:{field}:{entity}",
                        TriggerEntityId: entity, InteractionRadius: radius);
                    Equal(expected, world.Planner(field).TryBuildRoute(start, target, out var route),
                        $"{field} e{entity} from ({sx},{sy}) t{triangle} at radius {radius}: {world.Planner(field).LastDiagnostic}");
                    if (expected)
                    {
                        var dx = route.FinalApproach.X - x;
                        var dy = route.FinalApproach.Y - y;
                        Equal(true, Math.Sqrt(dx * dx + dy * dy) <= radius, $"{field} e{entity}: the approach ends inside the talk range");
                    }
                }
            }
        }
    }

    /// <summary>
    /// md8_b1 (733) and md8_b2 (734) are one scaffold of ladders and ducts. Their MAPJUMPs put
    /// the party on a two-triangle ladder start, and the controlled character's Main then moves
    /// it on by the entrance code in 15[143] (733 e2 Main: 3 to (-3740,-908) t5, 1 to (-3878,-396)
    /// t165, 4 to (-2500,-675) t75, 6 to (1262,816) t25). From those native placements each
    /// scaffold item is reached from at least one entrance; from the raw ladder start nothing
    /// is, and the out-and-back resolver must not route through one.
    /// </summary>
    private static void ScaffoldEntrancesAreScriptedLadders()
    {
        var world = new Caves();
        var mesh = world.Mesh(733);
        Equal(2, Walkable(mesh, 14).Count, "734's init script 3 lands on a two-triangle ladder start (t14)");
        var main = world.Scripts.ReadScriptOpcodes(733, 2, 0).Select(Hex).ToArray();
        Equal(true, main.Contains("14F08F03003B") && main.Contains("C2000064F174FCBAFE05000104C001"),
            "733 cloud's Main: entrance 3 climbs to (-3740,-908,-326) t5");
        Equal(true, main.Contains("C20000EE0430034000190001038001"), "entrance 6 climbs to (1262,816,64) t25");
        var items = new[] { ("Elixir", -2136, 1457, 508), ("Max Ray", -528, 1052, 1185), ("Megalixir", 1073, 1528, 508) };
        var placements = new[] { (-3740, -908, 5), (-3878, -396, 165), (-2500, -675, 75), (1262, 816, 25) };
        foreach (var (label, x, y, z) in items)
        {
            var target = new FieldNavigationTarget(733, FieldNavigationCategory.Objects, label, x, y, z, $"object:733:{label}", InteractionRadius: 114);
            Equal(true, placements.Any(p => world.Planner(733).TryBuildRoute(world.At(733, p.Item1, p.Item2, p.Item3), target, out _)),
                $"{label} is reached from a native entrance placement");
            Equal(false, world.Planner(733).TryBuildRoute(world.At(733, -3712, -923, 14), target, out _),
                $"{label}: not from the raw ladder start");
        }

        // 734's init script 3 writes 15[143] = 3 and map jumps to the ladder start; 733's Main
        // climbs from there to (-3740,-908,-326) t5, reads its height back (AXYZI) and, not
        // being at -788, does not take the second ladder.
        var scripts = world.Scripts.ReadAllScriptOpcodes(733);
        Equal(new FieldEntryPlacementResult(-3740, -908, -326, 5),
            FieldEntryPlacement.Place(scripts, -3712, -923, 14, new Dictionary<(int, int), int> { [(15, 143)] = 3 }),
            "entrance 3 is placed at the top of its ladder");
        Equal(new FieldEntryPlacementResult(1262, 816, 64, 25),
            FieldEntryPlacement.Place(scripts, 1255, 805, 10, new Dictionary<(int, int), int> { [(15, 143)] = 6 }),
            "entrance 6 at the foot of the scaffold ladder");
        Equal(new FieldEntryPlacementResult(-181, 725, null, 122),
            FieldEntryPlacement.Place(scripts, -181, 725, 122, new Dictionary<(int, int), int>()),
            "a gateway writes nothing and lands where it says");
    }

    private static readonly (string Label, int X, int Y, int Z, int Entity)[] ScaffoldItems =
    [
        ("Elixir", -2136, 1457, 508, 18), ("Aegis Armlet", -3811, 1050, 512, 15),
        ("Max Ray", -528, 1052, 1185, 16), ("Megalixir", 1073, 1528, 508, 17)
    ];

    private static FieldNavigationTarget ScaffoldItem(string label)
    {
        var item = ScaffoldItems.Single(candidate => candidate.Label == label);
        return new FieldNavigationTarget(733, FieldNavigationCategory.Objects, label, item.X, item.Y, item.Z,
            $"object:733:{item.Entity}", TriggerEntityId: item.Entity, InteractionRadius: 114);
    }

    /// <summary>
    /// md8_b1's four items from every controllable start the scaffold gives: the placements of
    /// 733's own entrances and its two gateway landings. Each is either reached directly, or by
    /// the shortest chain of native exits through md8_b2 and back, or - where the scaffold is
    /// one way - not at all, and then nothing is offered. The four-leg chain is then walked
    /// through the controller both runtimes share, with its entry climbs suppressed as live.
    /// </summary>
    private static void ScaffoldItemsAreWalkedToThroughNativeChains()
    {
        var world = new Caves();
        var resolver = world.Resolver();
        var starts = new (string Name, int X, int Y, int Triangle)[]
        {
            ("entrance 1", -3878, -396, 165), ("entrance 3", -3740, -908, 5), ("entrance 4", -2500, -675, 75),
            ("entrance 5", -2613, 621, 162), ("entrance 6", 1262, 816, 25), ("gateway 0", -181, 725, 122),
            ("gateway 1", -3768, 1281, 124)
        };
        var expected = new Dictionary<(string, string), string>
        {
            [("entrance 1", "Elixir")] = "direct", [("entrance 4", "Elixir")] = "direct",
            [("gateway 1", "Elixir")] = "gateway:733:1:734>734 -> script-exit:734:9:733>733",
            [("entrance 1", "Aegis Armlet")] = "script-exit:733:5:734>734 -> gateway:734:1:733>733",
            [("entrance 4", "Aegis Armlet")] = "script-exit:733:5:734>734 -> gateway:734:1:733>733",
            [("gateway 1", "Aegis Armlet")] = "direct",
            [("entrance 6", "Max Ray")] = "direct",
            [("entrance 3", "Max Ray")] = "script-exit:733:11:734>734 -> script-exit:734:15:733>733",
            [("entrance 5", "Max Ray")] = "script-exit:733:11:734>734 -> script-exit:734:15:733>733",
            [("gateway 0", "Max Ray")] = "gateway:733:0:734>734 -> script-exit:734:15:733>733",
            [("entrance 1", "Max Ray")] =
                "script-exit:733:5:734>734 -> script-exit:734:8:733>733 -> script-exit:733:11:734>734 -> script-exit:734:15:733>733",
            [("entrance 4", "Max Ray")] =
                "script-exit:733:5:734>734 -> script-exit:734:8:733>733 -> script-exit:733:11:734>734 -> script-exit:734:15:733>733",
            [("gateway 1", "Max Ray")] =
                "gateway:733:1:734>734 -> script-exit:734:8:733>733 -> script-exit:733:11:734>734 -> script-exit:734:15:733>733",
            [("entrance 1", "Megalixir")] = "direct", [("entrance 4", "Megalixir")] = "direct",
            [("gateway 1", "Megalixir")] = "gateway:733:1:734>734 -> script-exit:734:9:733>733",
        };
        foreach (var (name, x, y, triangle) in starts)
        {
            var start = world.At(733, x, y, triangle);
            foreach (var item in ScaffoldItems)
            {
                var target = ScaffoldItem(item.Label);
                var direct = world.Planner(733).TryBuildRoute(start, target, out _);
                var plan = direct ? null : resolver.Resolve(start, target, world.Exits(733));
                var actual = direct ? "direct" : plan is null ? "none" : plan.ToString()["733: ".Length..];
                Equal(expected.GetValueOrDefault((name, item.Label), "none"), actual,
                    $"{item.Label} from {name}: {(direct ? "" : resolver.LastDiagnostic)}");
            }
        }

        // The four legs from entrance 1 to the Max Ray, through the controller.
        var maxRay = ScaffoldItem("Max Ray");
        var source = new FieldNavigationTargetSource(
            [],
            objectTargetProvider: position => position.FieldId == 733 ? [maxRay] : [],
            exitTargetProvider: position => world.Exits(position.FieldId));
        var controller = new FieldNavigationController(source, world.SharedPlanner)
        {
            CrossFieldApproach = (position, goal, exits) => resolver.Resolve(position, goal, exits)
        };
        var noInput = new FieldNavigationInputSnapshot(0, FieldNavigationInput.None);
        var noRotation = new FieldNavigationControlTransform(-128);
        var now = new DateTime(2026, 9, 25, 16, 0, 0, DateTimeKind.Utc);
        var here = world.At(733, -3878, -396, 165);
        SelectCategory(controller, here, FieldNavigationCategory.Objects);
        var first = controller.HandleAction(FieldNavigationAction.ToggleBeacon, here, noRotation)?.Speech ?? string.Empty;
        Equal(true, first.StartsWith("Max Ray is reached from here only by going out", StringComparison.Ordinal), first);
        controller.NoteAutoWalkStarted();
        var legs = new (int Field, string Exit, int Destination, int X, int Y, int Triangle)[]
        {
            (733, "script-exit:733:5:734", 734, -2668, -274, 58),
            (734, "script-exit:734:8:733", 733, -3740, -908, 5),
            (733, "script-exit:733:11:734", 734, -572, 576, 56),
            (734, "script-exit:734:15:733", 733, 1262, 816, 25)
        };
        var clock = now;
        for (var index = 0; index < legs.Length; index++)
        {
            var leg = legs[index];
            var exit = world.Exits(leg.Field).Single(candidate => candidate.StableId == leg.Exit);
            clock = clock.AddSeconds(5);
            _ = controller.UpdateLiveTracking(world.Near(leg.Field, exit.X, exit.Y), noInput, noRotation, isSuppressed: false, 80,
                observedAt: clock);
            // The entry climb holds the party: suppressed for longer than a leg's own wait.
            var landed = world.At(leg.Destination, leg.X, leg.Y, leg.Triangle);
            for (var frame = 0; frame < 8; frame++)
            {
                clock = clock.AddSeconds(1);
                _ = controller.UpdateLiveTracking(landed, noInput, noRotation, isSuppressed: true, 80, observedAt: clock);
            }

            clock = clock.AddSeconds(1);
            var speech = controller.UpdateLiveTracking(landed, noInput, noRotation, isSuppressed: false, 80, observedAt: clock)?.Speech ?? string.Empty;
            var last = index == legs.Length - 1;
            Equal(true, speech.StartsWith(last ? "Navigation on. Max Ray." : "Max Ray: now through", StringComparison.Ordinal),
                $"leg {index + 1} of {legs.Length}: {speech} [{controller.LastNavigationDiagnostic}]");
            Equal(true, controller.TryConsumeHeldAutoWalkRequest(), $"leg {index + 2} is walked too");
        }

        Equal(string.Empty, controller.CrossFieldGoalLabel, "the Max Ray is the goal to the end, and then the approach is over");
    }

    /// <summary>The entry evaluator decides only what the opcodes say; everything else fails closed.</summary>
    private static void EntryPlacementFailsClosed()
    {
        static FieldScriptDefinition Script(int entity, int script, params string[] hex)
        {
            var at = 0;
            var ops = new List<FieldScriptOpcodeDefinition>();
            foreach (var op in hex)
            {
                var bytes = Convert.FromHexString(op);
                ops.Add(new FieldScriptOpcodeDefinition(900, entity, $"e{entity}", script, at, bytes[0], bytes));
                at += bytes.Length;
            }

            return new FieldScriptDefinition(900, entity, $"e{entity}", script, ops);
        }

        var entrance = new Dictionary<(int, int), int> { [(15, 143)] = 3 };
        const string pc = "A000";
        const string ret = "00";
        const string ifEntrance3 = "14F08F030010"; // IFUB 15[143] == 3, else skip the LADER
        const string ladder = "C2000064F174FCBAFE05000104C001"; // LADER (-3740,-908,-326) t5
        var decided = new[] { Script(2, 0, pc, ret, ifEntrance3, ladder, ret) };
        Equal(new FieldEntryPlacementResult(-3740, -908, -326, 5), FieldEntryPlacement.Place(decided, 0, 0, 14, entrance), "the matching branch");
        Equal(new FieldEntryPlacementResult(0, 0, null, 14),
            FieldEntryPlacement.Place(decided, 0, 0, 14, new Dictionary<(int, int), int> { [(15, 143)] = 5 }), "another entrance leaves the landing");
        Equal(new FieldEntryPlacementResult(0, 0, null, 14), FieldEntryPlacement.Place(decided, 0, 0, 14, new Dictionary<(int, int), int>()),
            "nothing written, nothing followed");
        Equal(null, FieldEntryPlacement.Place([Script(2, 0, pc, ret, ifEntrance3, "14F0900100" + "05", ladder, ret)], 0, 0, 14, entrance),
            "a test on a byte nobody wrote is not guessed");
        Equal(null, FieldEntryPlacement.Place([Script(2, 0, pc, ret, ifEntrance3, "C2111164F174FCBAFE05000104C001", ret)], 0, 0, 14, entrance),
            "a ladder read from variables is not followed");
        Equal(null, FieldEntryPlacement.Place(
                [Script(2, 0, pc, ret, ifEntrance3, "0205C3", ladder, ret), Script(5, 3, "A500000000000000000100")], 0, 0, 14, entrance),
            "a request that could move the party is not followed");
        Equal(null, FieldEntryPlacement.Place(
                [Script(2, 0, pc, ret, ifEntrance3, ladder, ret), Script(3, 0, "A001", ret, ifEntrance3, "C20000000000000000000900000000", ret)],
                0, 0, 14, entrance),
            "two actors placing the party differently is ambiguous");
        Equal(null, FieldEntryPlacement.Place([Script(2, 0, pc, ret, ifEntrance3, "CB0105", ladder, ret)], 0, 0, 14, entrance),
            "a party test is not decided here");
    }

    // --- 760-762 las3: the crater's jump fields ----------------------------------------------------

    /// <summary>
    /// las3_1 (760), las3_2 (761) and las3_3 (762) are rows of LINE jumps. Each line's Move tests
    /// a temporary selector is 0, writes its own number, waits on the leader's script 3 (PRQEW)
    /// and clears it; script 3 is one IFUBL case per number. 0.6.8 onwards counted the selector as
    /// written concurrently, so every line was read as every case: in 762 the east lines all
    /// looked like the way out to 761, and 760's eighteen lines all had eighteen jumps.
    /// </summary>
    private static void CraterJumpLinesTakeTheirOwnJump()
    {
        var world = new Caves();
        AssertOpcode(world.Scripts, 762, 26, 2, 0, "14500200000C", "l21's Move: only while 5[2] is 0");
        AssertOpcode(world.Scripts, 762, 26, 2, 6, "80500215", "it claims 5[2] = 0x15");
        AssertOpcode(world.Scripts, 762, 26, 2, 10, "060043", "waits on the leader's script 3");
        AssertOpcode(world.Scripts, 762, 26, 2, 13, "80500200", "and hands 5[2] back");
        AssertOpcode(world.Scripts, 762, 0, 0, 17, "80500201", "dic's Init claims it for the entrance from 761 before any line runs");

        foreach (var (field, selector, lines, exits) in new[]
                 {
                     (760, 3, Enumerable.Range(5, 18).ToArray(), Array.Empty<string>()),
                     (761, 6, Enumerable.Range(6, 10).ToArray(), new[] { "script-exit:761:16:762", "script-exit:761:6:760" }),
                     (762, 2, Enumerable.Range(6, 23).ToArray(), new[] { "script-exit:762:6:761" })
                 })
        {
            var read = world.Scripts.ReadField(field);
            Equal(string.Join(",", exits), string.Join(",", read.Exits.Select(exit => exit.StableId).Order(StringComparer.Ordinal)),
                $"{field}: only the lines whose own case map jumps are exits");
            var jumps = read.Transitions.Where(t => t.StableId.StartsWith($"jump:{field}:", StringComparison.Ordinal))
                .GroupBy(t => int.Parse(t.StableId.Split(':')[2]))
                .ToDictionary(group => group.Key, group => group.Select(t => t.StableId).ToArray());
            foreach (var line in lines)
            {
                var claim = world.Scripts.ReadScriptOpcodes(field, line, 2).Select(Hex).ToArray();
                // 760's l15 first takes control away (UC 1, MENU2 1), then claims the same way.
                var test = Array.FindIndex(claim, hex => hex.StartsWith($"1450{selector:X2}0000", StringComparison.Ordinal));
                Equal(true, test is 0 || (test is 2 && claim[0] == "3301" && claim[1] == "4A01"),
                    $"{field} e{line} tests 5[{selector}] is 0");
                Equal("060043", claim[test + 2], $"{field} e{line} waits on the leader's script 3");
                // A line whose case holds two jumps is one two-hop jump (or a jump and a ladder
                // in 761's l2 and l3); never more than its own case gives.
                var own = jumps.GetValueOrDefault(line, []);
                Equal(true, own.Length <= 2, $"{field} e{line} has only its own jumps: {string.Join(" ", own)}");
            }
        }

        // The negative witness: dic's Main clear with a guard Init never claimed under (the field
        // 764 instead of 761) can empty 5[2] while a line holds it, so 5[2] is not a selector
        // and every line reads as every case again - the proof, not the pattern, decides.
        var bytes = world.Decoded(762).ToArray();
        var guard = Convert.FromHexString("16600000F902000D");
        var at = Enumerable.Range(0, bytes.Length - guard.Length).Where(i => bytes.AsSpan(i, guard.Length).SequenceEqual(guard)).ToArray();
        Equal(1, at.Length, "dic's Main guard, IFSW 6[0] == 761, is in 762 once");
        bytes[at[0] + 4] = 0xFC;
        var patched = FieldScriptNavigationCatalog.ReadFieldFromBytes(762, "las3_3", bytes).Transitions
            .Count(t => t.StableId.StartsWith("jump:762:26:", StringComparison.Ordinal));
        Equal(true, patched > 1, $"an unproven concurrent clear leaves the selector volatile ({patched} jumps for l21)");
        Equal(1, FieldScriptNavigationCatalog.ReadFieldFromBytes(762, "las3_3", world.Decoded(762)).Transitions
            .Count(t => t.StableId.StartsWith("jump:762:26:", StringComparison.Ordinal)), "the native bytes keep one");

        var crater = world.Scripts.ReadField(762).Transitions.Select(t => t.StableId).ToHashSet();
        Equal(true, crater.Contains("jump:762:26:1:3:279") && !crater.Any(id => id.StartsWith("jump:762:26:", StringComparison.Ordinal) && id != "jump:762:26:1:3:279"),
            "l21 lands only on t279, over the middle rock");
        Equal(true, crater.Contains("jump:762:27:1:3:185") && !crater.Any(id => id.StartsWith("jump:762:27:", StringComparison.Ordinal) && id != "jump:762:27:1:3:185"),
            "l22 lands only on t185");
    }

    /// <summary>
    /// las3_3's Mega All floats over the middle rock; its model (mat, e5) has an empty Talk and
    /// Contact. Cases 0x15 and 0x16 of the leader's script 3 JUMP to the rock (952,-439) t186,
    /// then, while 1[50] bit 4 is clear, test once for a fresh OK press (IFKEYON 0x0220) and ask
    /// mat's script 3 for the award (SMTRA 12, then the bit). So the targets are the two take-off
    /// lines, each reached from both ways into the field, and the label says what to press.
    /// </summary>
    private static void MegaAllIsOfferedAtItsTakeOffLines()
    {
        var world = new Caves();
        var leader = world.Scripts.ReadScriptOpcodes(762, 1, 3).Select(Hex).ToArray();
        foreach (var number in new[] { 0x15, 0x16 })
        {
            var at = Array.FindIndex(leader, hex => hex.StartsWith($"155002{number:X2}00", StringComparison.Ordinal));
            Equal(true, at >= 0, $"case {number:X2} is in the leader's script 3");
            var body = leader.Skip(at + 1).Take(10).ToArray();
            Equal("C00000B80349FEBA001100", body.First(hex => hex.StartsWith("C0", StringComparison.Ordinal)), $"case {number:X2} jumps to the rock t186");
            var test = Array.IndexOf(body, "141032040A11");
            // ASPED and the catch animation (CANMX1), then the one test.
            Equal(true, test >= 0 && body[test + 3] == "31200204" && body[test + 4] == "030543",
                $"case {number:X2}: once 1[50] bit 4 is clear, one OK test asks mat for the award");
        }

        AssertOpcode(world.Scripts, 762, 5, 3, 5, "82103204", "mat's script 3 sets 1[50] bit 4");
        AssertOpcode(world.Scripts, 762, 5, 3, 22, "5B00000C000000", "gives Mega All (SMTRA 12)");
        AssertOpcode(world.Scripts, 762, 5, 3, 33, "A400", "and hides the model");
        Equal(1, world.Scripts.ReadScriptOpcodes(762, 5, 1).Count, "mat's Talk is empty");

        var definitions = FieldNavigationObjectCatalog.CreateAllFields().Where(o => o.FieldId == 762).ToArray();
        Equal(false, definitions.Any(o => o.EntityId == 5), "the floating model is not a target");
        var arrivals = world.ArrivalsInto(761, 762).Concat(world.ArrivalsInto(764, 762)).ToArray();
        Equal(true, arrivals.Length >= 2, "762 is entered from 761 and from 764");
        var radius = world.CollisionRadius(762);
        var live = new LiveObjects(radius);
        foreach (var entity in new[] { 26, 27 })
        {
            var definition = definitions.Single(o => o.EntityId == entity);
            Equal((FieldNavigationObjectTargetKind.Line, true), (definition.TargetKind, definition.UsesPlayerCollisionRadius),
                $"e{entity} is its take-off line, inside the player's own collision range");
            Equal((1, 50, (byte)0x10), (definition.CollectedBank, definition.CollectedAddress, definition.CollectedMask),
                $"e{entity} goes once 1[50] bit 4 is set, as mat's own visibility does");
            Equal(true, definition.Label!.Contains("stepping onto", StringComparison.Ordinal) &&
                        definition.Label.Contains("press OK repeatedly", StringComparison.Ordinal),
                $"e{entity}'s label says the step starts the jump and what to press");
            Equal(false, world.Scripts.ReadAllScriptOpcodes(762).Where(s => s.EntityId == entity).SelectMany(s => s.Opcodes).Any(op => Hex(op) == "D100"),
                $"e{entity} is never switched off");
            var trigger = world.LineOf(762, entity);
            foreach (var arrival in arrivals)
            {
                var start = world.At(762, arrival.X, arrival.Y, arrival.Triangle);
                // What the game's own reader offers, not a target enriched with a line it never sends.
                var target = live.Read(definitions, start).Single(t => t.Label == definition.Label);
                Equal((radius - 1, (FieldNavigationTriggerLine?)null), (target.InteractionRadius, target.TriggerLine),
                    $"e{entity}: the reader's own target and range");
                Equal(true, world.Planner(762).TryBuildRoute(start, target, out var route),
                    $"e{entity} from ({arrival.X},{arrival.Y}) t{arrival.Triangle}: {world.Planner(762).LastDiagnostic}");
                Equal(true, DistanceToSegment(trigger, route.FinalApproach.X, route.FinalApproach.Y) < radius,
                    $"e{entity}: the walk ends touching the take-off, where its Move starts the jump (00637ABB)");
            }
        }
    }

    // --- 620-624 anfrst: the Ancient Forest --------------------------------------------------------

    /// <summary>
    /// The throwing spots: field, zone byte, the line that writes the zone, the zone, the
    /// creatures that branch on it, and - for a line that writes only when crossed - the line on
    /// the far side of its strip that puts the zone back to 1, and whether that one does it on touch.
    /// </summary>
    private static readonly (int Field, int ZoneByte, int Entity, byte Zone, int[] Creatures, int Partner, bool PartnerTouch)[] ForestSpots =
    [
        (620, 20, 18, 3, [7, 8, 9, 10], -1, false), (620, 20, 20, 5, [7, 8, 9, 10], -1, false), (620, 20, 22, 6, [7, 8, 9, 10], -1, false),
        (620, 20, 29, 7, [16], 30, false), (620, 20, 31, 8, [16], 32, false),
        (622, 19, 14, 2, [6, 7, 8, 9, 10, 11], -1, false), (622, 19, 15, 6, [6, 7, 8, 9, 10, 11], 16, true),
        (623, 22, 18, 2, [6, 7, 8, 9, 10], 17, false), (623, 22, 20, 5, [6, 7, 8, 9], 21, false), (623, 22, 23, 6, [6, 7, 8, 9, 10], 22, false),
        (623, 22, 25, 8, [6, 7, 8, 9, 10], 26, false), (623, 22, 29, 9, [6, 7, 8, 9, 10], 28, false),
        (623, 22, 35, 13, [16], 38, false), (623, 22, 36, 13, [16], 37, false)
    ];

    /// <summary>
    /// Each throwing spot is a LINE whose own script writes its zone into the byte the carried
    /// creatures' throw scripts branch on. A line that writes on touch is offered as itself, and
    /// the walk the reader's target gives ends inside the player's collision range of it. A line
    /// that writes only when crossed is offered as a point past it: from every start the reader
    /// offers it at, the walk crosses the line and does not cross (or touch, for bdl11) the line
    /// beyond that resets the zone. Starts where that was not proven are not offered - 622's bdl10
    /// from the plant's side, and parts of 623's upper side - and those are recorded as open.
    /// </summary>
    private static void ForestThrowingSpotsAreTheZoneLines()
    {
        var world = new Caves();
        var forest = TownInteractionObjectCatalog.Create().Where(o => o.FieldId is >= 620 and <= 623).ToArray();
        var spots = forest.Where(o => o.Label!.Contains("throwing spot", StringComparison.Ordinal)).ToArray();
        foreach (var (field, zoneByte, entity, zone, creatures, partner, partnerTouch) in ForestSpots)
        {
            var own = spots.Where(o => o.FieldId == field && o.EntityId == entity).ToArray();
            Equal(true, own.Length > 0, $"{field} e{entity} is offered");
            Equal(true, own.All(o => o.Label!.EndsWith("press OK to throw", StringComparison.Ordinal)), $"{field} e{entity}: the label says how to throw");
            var line = world.LineOf(field, entity);
            var slots = Enumerable.Range(1, 6).ToDictionary(slot => slot, slot => world.Scripts.ReadScriptOpcodes(field, entity, slot).Select(Hex).ToArray());
            var write = $"8050{zoneByte:X2}{zone:X2}";
            var onTouch = slots[2].Contains(write) || slots[4].Contains(write) || slots[5].Contains(write);
            var onCrossing = slots[3].Contains(write);
            Equal(true, onTouch || onCrossing, $"{field} e{entity} writes 5[{zoneByte}] = {zone}");
            Equal(true, creatures.Any(creature => Enumerable.Range(3, 2).SelectMany(slot => world.Scripts.ReadScriptOpcodes(field, creature, slot))
                    .Any(op => Hex(op).StartsWith($"1450{zoneByte:X2}{zone:X2}00", StringComparison.Ordinal) ||
                               // anfrst_4's zone 8 is its case 6's other side (IFUB == 8 then JMPF into 6's code).
                               (zone == 8 && Hex(op) == "145016080003"))),
                $"{field} e{entity}: a creature's throw branches on zone {zone}");
            Equal(false, world.Scripts.ReadAllScriptOpcodes(field).Where(s => s.EntityId == entity).SelectMany(s => s.Opcodes).Any(op => Hex(op) == "D100"),
                $"{field} e{entity} is never switched off");

            var radius = world.CollisionRadius(field);
            var live = new LiveObjects(radius);
            var walking = world.WalkingPlanner(field);
            if (onTouch && !onCrossing)
            {
                var definition = own.Single();
                Equal((FieldNavigationObjectTargetKind.Line, true), (definition.TargetKind, definition.UsesPlayerCollisionRadius), $"{field} e{entity} is its own line");
                var reached = 0;
                foreach (var start in world.Starts(field))
                {
                    var target = live.Read(own, start).Single();
                    if (!walking.TryBuildRoute(start, target, out var route))
                    {
                        continue;
                    }

                    reached++;
                    Equal(true, DistanceToSegment(line, route.FinalApproach.X, route.FinalApproach.Y) < radius,
                        $"{field} e{entity} from t{start.TriangleId}: the walk ends touching the line");
                }

                Equal(true, reached > 0, $"{field} e{entity} is walked to");
                continue;
            }

            Equal(true, partner >= 0 && own.All(o => o.TargetKind == FieldNavigationObjectTargetKind.Location), $"{field} e{entity} is offered past its line");
            foreach (var spot in own)
            {
                // Arrival is measured in three dimensions: the point is at the walkmesh's height.
                var under = FieldWalkmeshPathfinder.ResolveTriangle(world.Mesh(field), spot.StaticX, spot.StaticY, spot.StaticZ, -1);
                Equal(true, under >= 0 && FieldCrossFieldApproachResolver.SurfaceZ(world.Mesh(field).Triangles[under], spot.StaticX, spot.StaticY) == spot.StaticZ,
                    $"{field} e{entity} ({spot.StaticX},{spot.StaticY}) is on the walkmesh at its own height");
                Equal(true, spot.CrossingLine is { } crossing && crossing == line, $"{field} e{entity}: its crossing line is its own zone line");
            }
            var partnerLine = world.LineOf(field, partner);
            var reset = $"8050{zoneByte:X2}01";
            Equal(true, Enumerable.Range(1, 6).Any(slot => world.Scripts.ReadScriptOpcodes(field, partner, slot).Select(Hex).Contains(reset)),
                $"{field} e{partner} puts the zone back to 1");
            var offered = 0;
            foreach (var start in world.Starts(field))
            {
                foreach (var target in live.Read(own, start))
                {
                    if (!walking.TryBuildRoute(start, target, out var route))
                    {
                        continue;
                    }

                    offered++;
                    var path = new List<(double X, double Y)> { (start.X, start.Y) };
                    path.AddRange(route.Portals.Select(portal => ((portal.Left.X + portal.Right.X) / 2.0, (portal.Left.Y + portal.Right.Y) / 2.0)));
                    path.Add((route.FinalApproach.X, route.FinalApproach.Y));
                    // Every crossing of either line in walk order - within one step by where along
                    // it the line is met - so a step that crosses both is not misread.
                    var crossings = new List<(double Order, bool Setting)>();
                    for (var at = 1; at < path.Count; at++)
                    {
                        foreach (var (candidate, setting) in new[] { (line, true), (partnerLine, false) })
                        {
                            if (Crosses(path[at - 1], path[at], candidate))
                            {
                                crossings.Add((at + CrossingParameter(path[at - 1], path[at], candidate), setting));
                            }
                        }
                    }

                    crossings.Sort((a, b) => a.Order.CompareTo(b.Order));
                    Equal(true, crossings.Count > 0 && crossings[^1].Setting,
                        $"{field} e{entity} from t{start.TriangleId}: the last line the walk crosses is its own, so nothing resets it after");

                    // The production planner routes through the field's scripted jumps; whatever
                    // it jumps, the zone line has to be crossed on foot after its last jump.
                    if (world.Planner(field).TryBuildRoute(start, target, out var production))
                    {
                        var lastJump = -1;
                        for (var at = 0; at < production.Portals.Count; at++)
                        {
                            if (production.Portals[at].TransitionKind is not null)
                            {
                                lastJump = at;
                            }
                        }

                        var afoot = new List<(double X, double Y)>
                        {
                            lastJump < 0 ? (start.X, start.Y)
                                : ((production.Portals[lastJump].Left.X + production.Portals[lastJump].Right.X) / 2.0,
                                   (production.Portals[lastJump].Left.Y + production.Portals[lastJump].Right.Y) / 2.0)
                        };
                        afoot.AddRange(production.Portals.Skip(lastJump + 1)
                            .Select(portal => ((portal.Left.X + portal.Right.X) / 2.0, (portal.Left.Y + portal.Right.Y) / 2.0)));
                        afoot.Add((production.FinalApproach.X, production.FinalApproach.Y));
                        var order = new List<(double Order, bool Setting)>();
                        for (var at = 1; at < afoot.Count; at++)
                        {
                            foreach (var (candidate, setting) in new[] { (line, true), (partnerLine, false) })
                            {
                                if (Crosses(afoot[at - 1], afoot[at], candidate))
                                {
                                    order.Add((at + CrossingParameter(afoot[at - 1], afoot[at], candidate), setting));
                                }
                            }
                        }

                        order.Sort((a, b) => a.Order.CompareTo(b.Order));
                        Equal(true, order.Count > 0 && order[^1].Setting,
                            $"{field} e{entity} from t{start.TriangleId}: the production route crosses the line on foot after any jump");
                    }

                    if (partnerTouch)
                    {
                        Equal(true, DistanceToSegment(partnerLine, route.FinalApproach.X, route.FinalApproach.Y) >= radius,
                            $"{field} e{entity} from t{start.TriangleId}: and the walk stops short of touching e{partner}");
                    }
                }
            }

            Equal(true, offered > 0, $"{field} e{entity} is offered from somewhere a walk proves it");
        }

        // 623's ujp0 writes zone 3 while the leader stands on t0 or t1.
        var ujp0 = world.Scripts.ReadScriptOpcodes(623, 2, 0).Select(Hex).ToArray();
        Equal(true, ujp0.Contains("80501603"), "623's ujp0 writes zone 3 on utubo_1's top");
        Equal(1, spots.Count(o => o.FieldId == 623 && o.RequiredPlayerTriangles is [0, 1]), "and that is offered only there");
        Equal(false, forest.Any(o => o.FieldId == 620 && o.EntityId is 30 or 32), "the strips' far edges (writing 1) are not spots");

        ForestJumpsSayWhichKey(world, forest);
    }

    /// <summary>
    /// The forest's jumps the player makes. A take-off line runs the leader's jump script from its
    /// Go (touching) or its crossing script only with a key held (IFKEY) or pressed (IFKEYON), and
    /// most only while the pitcher plant it jumps onto is closed (its director's byte is 1). ujp0
    /// does the same for the leader standing on the triangles its Init names. Each is offered with
    /// its key, only while its gate holds, and never jumped for the player; every native one is
    /// offered.
    /// </summary>
    private static void ForestJumpsSayWhichKey(Caves world, FieldNavigationObjectDefinition[] forest)
    {
        var keys = new Dictionary<int, string> { [0x1000] = "Up", [0x2000] = "Right", [0x4000] = "Down", [0x8000] = "Left" };
        var jumps = forest.Where(o => o.Label!.StartsWith("Jump ", StringComparison.Ordinal)).ToArray();
        var covered = new HashSet<FieldNavigationObjectDefinition>();
        foreach (var field in new[] { 620, 622, 623 })
        {
            var scripts = world.Scripts.ReadAllScriptOpcodes(field);
            var radius = world.CollisionRadius(field);
            var leader = scripts.First(s => s.ScriptId == 0 && s.Opcodes.Any(op => Hex(op) == "A000")).EntityId;
            bool Jumps(int script) => world.Scripts.ReadScriptOpcodes(field, leader, script).Any(op => op.Opcode == 0xC0);

            // Take-off lines.
            foreach (var entity in scripts.Where(s => s.ScriptId == 0 && s.Opcodes.Any(op => op.Opcode == 0xD0)).Select(s => s.EntityId).Distinct())
            {
                foreach (var slot in new[] { 3, 4 })
                {
                    var ops = world.Scripts.ReadScriptOpcodes(field, entity, slot);
                    var key = ops.FirstOrDefault(op => op.Opcode is 0x30 or 0x31);
                    var request = ops.FirstOrDefault(op => op.Opcode is 0x04 or 0x05 or 0x06 && op.Bytes.Count == 3 && op.Bytes[1] == 0);
                    if (key.Bytes is null || request.Bytes is null || !keys.TryGetValue(key.Bytes[1] | (key.Bytes[2] << 8), out var name) ||
                        !Jumps(request.Bytes[2] & 0x1F))
                    {
                        continue;
                    }

                    var gate = ops.Where(op => op.Opcode == 0x14 && op.Bytes[1] == 0x50 && op.Bytes[3] == 1 && op.Bytes[4] == 0)
                        .Select(op => (int?)op.Bytes[2]).FirstOrDefault();
                    var definition = jumps.SingleOrDefault(o => o.FieldId == field && o.EntityId == entity && o.RequiredPlayerTriangles is null);
                    Equal(true, definition.Label is not null, $"{field} e{entity}'s take-off is offered");
                    covered.Add(definition);
                    var verb = key.Opcode == 0x31 ? "press" : "hold";
                    Equal(true, definition.Label!.Contains($"{verb} {name}", StringComparison.Ordinal) ||
                                (slot == 3 && definition.Label.Contains($"walk on holding {name}", StringComparison.Ordinal)),
                        $"{field} e{entity}: the label says {verb} {name}");
                    Equal(gate ?? -1, definition.RequiredAddress, $"{field} e{entity} is gated on its plant");
                    Equal(gate is null ? -1 : 5, definition.RequiredBank, $"{field} e{entity}: in the temporary block");
                    var landing = world.Mesh(field).Triangles[world.Scripts.ReadScriptOpcodes(field, leader, request.Bytes[2] & 0x1F)
                        .First(op => op.Opcode == 0xC0).Bytes[7] | (world.Scripts.ReadScriptOpcodes(field, leader, request.Bytes[2] & 0x1F)
                        .First(op => op.Opcode == 0xC0).Bytes[8] << 8)].GetCentroid();
                    CheckGateAndApproach(world, field, definition, radius, world.LineOf(field, entity), ((int)landing.X, (int)landing.Y));
                }
            }

            // ujp0's triangle polls: SETWORDs name the triangles, then each group's keys.
            var words = scripts.Where(s => s.ScriptId == 0).SelectMany(s => s.Opcodes)
                .Where(op => op.Opcode == 0x81 && op.Bytes.Count == 5 && op.Bytes[1] == 0x60)
                .ToDictionary(op => (int)op.Bytes[2], op => BitConverter.ToInt16(op.Bytes.ToArray(), 3));
            var poller = scripts.Single(s => s.ScriptId == 0 && s.EntityName == "ujp0");
            var triangles = new List<int>();
            string? pendingKey = null;
            int? pendingGate = null;
            var previous = (byte)0;
            foreach (var op in poller.Opcodes)
            {
                if (op.Opcode == 0x16 && op.Bytes[1] == 0x66 && op.Bytes[2] == 0)
                {
                    if (previous != 0x16 && previous != 0x10)
                    {
                        triangles.Clear();
                    }

                    triangles.Add(words[op.Bytes[4]]);
                }
                else if (op.Opcode == 0x30 && keys.TryGetValue(op.Bytes[1] | (op.Bytes[2] << 8), out var name))
                {
                    (pendingKey, pendingGate) = (name, null);
                }
                else if (op.Opcode == 0x14 && pendingKey is not null && op.Bytes[1] == 0x50 && op.Bytes[3] == 1)
                {
                    pendingGate = op.Bytes[2];
                }
                else if (op.Opcode is 0x04 or 0x05 or 0x06 && pendingKey is not null)
                {
                    var set = triangles.Distinct().Order().ToArray();
                    Equal(true, Jumps(op.Bytes[2] & 0x1F), $"{field} t{string.Join("/", set)} {pendingKey} runs a jump");
                    var definition = jumps.SingleOrDefault(o => o.FieldId == field && o.RequiredPlayerTriangles is { } required &&
                                                                 required.Order().SequenceEqual(set) &&
                                                                 o.RequiredAddress == (pendingGate ?? -1) &&
                                                                 o.Label!.Contains(pendingKey, StringComparison.Ordinal));
                    Equal(true, definition.Label is not null, $"{field} t{string.Join("/", set)}: hold {pendingKey} is offered");
                    covered.Add(definition);
                    var live = new LiveObjects(radius);
                    if (definition.RequiredAddress >= 0)
                    {
                        live.SetTemporary(definition.RequiredAddress, 1);
                    }

                    Equal(1, live.Read([definition], world.At(field, definition.StaticX, definition.StaticY, set[0])).Count,
                        $"{field} t{set[0]}: offered while the leader stands there");
                    Equal(0, live.Read([definition], world.Starts(field).First(start => !set.Contains(start.TriangleId))).Count,
                        $"{field} t{string.Join("/", set)}: and not elsewhere");
                    pendingKey = null;
                }

                previous = op.Opcode;
            }
        }

        Equal(jumps.Length, covered.Count, "every jump offered is a native one");

        // Walked onto or crossed with no key: the catalog's own jumps, not offered as objects.
        foreach (var (field, entity) in new[] { (620, 34), (622, 24), (622, 25), (623, 39), (623, 40) })
        {
            Equal(true, world.Scripts.ReadField(field).Transitions.Any(t => t.StableId.StartsWith($"jump:{field}:{entity}:", StringComparison.Ordinal)),
                $"{field} e{entity}'s keyless jump is a route traversal");
        }

        ForestConditionalJumpsAreNotRouteEdges(world);
    }

    /// <summary>
    /// A route edge is taken as the walk onto a line. The forest's stamen and rock jumps run only
    /// with a direction key held (and most only while the next pitcher plant is closed), and its
    /// Mutant Flytraps' lines are a bite - HPd on each member and a JUMP back out. None of those
    /// is a production transition, and the planner cannot cross them: the take-off Objects say
    /// which key. Derived from the native scripts: every line whose script tests a direction key
    /// or a temporary-block byte before requesting a leader jump, and every leader jump script
    /// that takes HP, is absent; every other line jump of these fields is still an edge.
    /// </summary>
    private static void ForestConditionalJumpsAreNotRouteEdges(Caves world)
    {
        foreach (var field in new[] { 620, 622, 623 })
        {
            var scripts = world.Scripts.ReadAllScriptOpcodes(field);
            var leader = scripts.First(s => s.ScriptId == 0 && s.Opcodes.Any(op => Hex(op) == "A000")).EntityId;
            var transitions = world.Scripts.ReadField(field).Transitions.Select(t => t.StableId).ToArray();
            var expectedAbsent = 0;
            foreach (var line in scripts.Where(s => s.ScriptId == 0 && s.Opcodes.Any(op => op.Opcode == 0xD0)).Select(s => s.EntityId).Distinct())
            {
                foreach (var slot in Enumerable.Range(1, 6))
                {
                    var guarded = false;
                    foreach (var op in world.Scripts.ReadScriptOpcodes(field, line, slot))
                    {
                        var bytes = op.Bytes;
                        guarded |= op.Opcode is 0x30 or 0x31 && (bytes[1] | (bytes[2] << 8)) >= 0x1000 ||
                                   op.Opcode is 0x14 or 0x15 && bytes[1] >> 4 is 5 or 6;
                        if (op.Opcode is not (0x04 or 0x05 or 0x06) || bytes[1] != 0)
                        {
                            continue;
                        }

                        var jump = world.Scripts.ReadScriptOpcodes(field, leader, bytes[2] & 0x1F);
                        if (!jump.Any(candidate => candidate.Opcode == 0xC0))
                        {
                            continue;
                        }

                        var bites = jump.Any(candidate => candidate.Opcode == 0x4F);
                        var edge = transitions.Any(id => id.StartsWith($"jump:{field}:{line}:", StringComparison.Ordinal));
                        if (guarded || bites)
                        {
                            expectedAbsent++;
                            Equal(false, edge, $"{field} e{line}: a {(bites ? "bite" : "keyed or gated jump")} is not a route edge");
                        }
                        else
                        {
                            Equal(true, edge, $"{field} e{line}: a keyless jump stays a route edge");
                        }
                    }
                }
            }

            Equal(true, expectedAbsent > 0, $"{field} has such jumps");
        }

        // The planner: 620's first pitcher plant top (t8) is reached from the ground only by
        // ltrkjp's keyed jump, so it is not routed; treejp0's keyless jump down still is.
        var ground = world.ArrivalsInto(622, 620).Single();
        var fromGround = world.At(620, ground.X, ground.Y, ground.Triangle);
        var plantTop = world.Mesh(620).Triangles[8].GetCentroid();
        Equal(false, world.Planner(620).TryBuildRoute(fromGround,
                new FieldNavigationTarget(620, FieldNavigationCategory.Objects, "plant top", (int)plantTop.X, (int)plantTop.Y, (int)plantTop.Z, "t8", InteractionRadius: 8), out _),
            "the closed plant is not a free bridge");
        var tree = world.Scripts.ReadField(620).Transitions.Single(t => t.StableId.StartsWith("jump:620:34:", StringComparison.Ordinal));
        var landing = world.Mesh(620).Triangles[tree.TargetTriangle].GetCentroid();
        var beforeTree = world.Starts(620).First(start => world.WalkingPlanner(620).TryBuildRoute(start,
            new FieldNavigationTarget(620, FieldNavigationCategory.Objects, "tree", tree.SourceX, tree.SourceY, tree.SourceZ, "tree", InteractionRadius: 30), out _));
        Equal(true, world.Planner(620).TryBuildRoute(beforeTree,
                new FieldNavigationTarget(620, FieldNavigationCategory.Objects, "below", (int)landing.X, (int)landing.Y, (int)landing.Z, "below", InteractionRadius: 8), out var down) &&
            down.Portals.Any(portal => portal.TransitionKind == FieldNavigationTransitionKind.Jump),
            "treejp0's keyless jump is still routed through");
    }

    // The take-off is offered only while its gate holds, and the walk to the reader's target
    // ends on the side the jump is made from, out of touch of the line - so auto walk holding
    // the key's direction on the way cannot set the jump off - and within reach of it.
    private static void CheckGateAndApproach(Caves world, int field, FieldNavigationObjectDefinition definition, int radius,
        FieldNavigationTriggerLine line, (int X, int Y) landing)
    {
        Equal(true, FieldNavigationObjectReader.SideOf(line, definition.StaticX, definition.StaticY) ==
                    -FieldNavigationObjectReader.SideOf(line, landing.X, landing.Y),
            $"{field} e{definition.EntityId}: the take-off point is on the side the jump is made from");
        var under = FieldWalkmeshPathfinder.ResolveTriangle(world.Mesh(field), definition.StaticX, definition.StaticY, definition.StaticZ, -1);
        Equal(true, under >= 0 && FieldCrossFieldApproachResolver.SurfaceZ(world.Mesh(field).Triangles[under], definition.StaticX, definition.StaticY) == definition.StaticZ,
            $"{field} e{definition.EntityId}: the take-off point is on the walkmesh at its own height");
        var live = new LiveObjects(radius);
        var starts = world.Starts(field).ToArray();
        if (definition.RequiredAddress >= 0)
        {
            Equal(0, live.Read([definition], starts[0]).Count, $"{field} e{definition.EntityId}: not while its plant is open");
            live.SetTemporary(definition.RequiredAddress, 1);
        }

        var walked = 0;
        foreach (var start in starts)
        {
            if (live.Read([definition], start).SingleOrDefault() is not { Label: not null } target ||
                !world.Planner(field).TryBuildRoute(start, target, out var route))
            {
                continue;
            }

            walked++;
            var distance = DistanceToSegment(line, route.FinalApproach.X, route.FinalApproach.Y);
            Equal(true, distance >= radius && distance <= radius + 32,
                $"{field} e{definition.EntityId} from t{start.TriangleId}: the walk stops out of touch of the take-off, within reach ({distance:F0})");
        }

        Equal(true, walked > 0, $"{field} e{definition.EntityId}: the take-off point is walked to");
    }

    // Where along a->b the line is met, 0 to 1.
    private static double CrossingParameter((double X, double Y) a, (double X, double Y) b, FieldNavigationTriggerLine line)
    {
        double rx = b.X - a.X, ry = b.Y - a.Y, sx = line.EndX - line.StartX, sy = line.EndY - line.StartY;
        var denominator = rx * sy - ry * sx;
        return denominator == 0 ? 0 : ((line.StartX - a.X) * sy - (line.StartY - a.Y) * sx) / denominator;
    }

    internal static bool Crosses((double X, double Y) a, (double X, double Y) b, FieldNavigationTriggerLine line)
    {
        static double Turn(double px, double py, double qx, double qy, double rx, double ry) => (qx - px) * (ry - py) - (qy - py) * (rx - px);
        var d1 = Turn(line.StartX, line.StartY, line.EndX, line.EndY, a.X, a.Y);
        var d2 = Turn(line.StartX, line.StartY, line.EndX, line.EndY, b.X, b.Y);
        var d3 = Turn(a.X, a.Y, b.X, b.Y, line.StartX, line.StartY);
        var d4 = Turn(a.X, a.Y, b.X, b.Y, line.EndX, line.EndY);
        return Math.Sign(d1) * Math.Sign(d2) < 0 && Math.Sign(d3) * Math.Sign(d4) < 0;
    }

    private static double DistanceToSegment(FieldNavigationTriggerLine line, double x, double y)
    {
        double vx = line.EndX - line.StartX, vy = line.EndY - line.StartY;
        var t = Math.Clamp(((x - line.StartX) * vx + (y - line.StartY) * vy) / (vx * vx + vy * vy), 0, 1);
        double dx = x - (line.StartX + t * vx), dy = y - (line.StartY + t * vy);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// The object reader the runtimes use, over the live state a test sets: the player's collision
    /// radius (event block +0x72), the temporary block, and every line on (each line these tests
    /// use is checked never to be switched off).
    /// </summary>
    private sealed class LiveObjects(int collisionRadius)
    {
        private const int Events = 0x02404000;
        private readonly Dictionary<int, byte> bytes = new()
        {
            [FieldPositionReader.AddressFieldNumModels] = 1,
            [Events + FieldNavigationNpcReader.CollisionRadiusOffset] = (byte)collisionRadius,
            [Events + FieldNavigationNpcReader.CollisionRadiusOffset + 1] = (byte)(collisionRadius >> 8)
        };

        public void SetTemporary(int address, byte value) => bytes[FieldNavigationObjectReader.AddressTemporaryFieldBankBase + address] = value;

        public IReadOnlyList<FieldNavigationTarget> Read(IEnumerable<FieldNavigationObjectDefinition> definitions, FieldPositionSnapshot position) =>
            new FieldNavigationObjectReader(
                address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? Events : 0,
                address => bytes.GetValueOrDefault(address),
                _ => null, _ => null, definitions, _ => true).ReadTargets(position);
    }

    /// <summary>
    /// The forest's seven rewards. Supershot ST and Spring Gun Clip (622), Slash-All (620),
    /// Typhoon (621), Apocalypse and Elixir (624) are walked to from a native arrival. The
    /// Minerva Band (620 box0) is not reachable from the ground: 621's gateway lands the party on
    /// t25 and dir's Main, polling the leader's triangle, jumps it at once to t29, from where the
    /// band is a short walk; entry placement follows that poll. The first Mutant Flytrap bites
    /// whoever steps between its lines while it is open, so Slash-All is walked to only once
    /// 5[51] says it has shut.
    /// </summary>
    private static void ForestRewardsAreRoutedHonestly()
    {
        var world = new Caves();
        var rewards = FieldNavigationObjectCatalog.CreateAllFields()
            .Where(o => o.FieldId is >= 620 and <= 624 && o.Kind is FieldNavigationObjectKind.Item or FieldNavigationObjectKind.Materia)
            .ToArray();
        Equal("620:35,620:36,620:36,621:9,622:26,622:27,624:4,624:5",
            string.Join(",", rewards.Select(o => $"{o.FieldId}:{o.EntityId}").Order(StringComparer.Ordinal)), "the seven rewards, Slash-All twice");

        var walked = new[] { (622, 26, 620), (622, 27, 620), (620, 36, 622), (621, 9, 623), (624, 4, 623), (624, 5, 623) };
        foreach (var (field, entity, from) in walked)
        {
            var xyzi = world.Scripts.ReadScriptOpcodes(field, entity, 0).First(op => op.Opcode == 0xA5).Bytes.ToArray();
            var target = new FieldNavigationTarget(field, FieldNavigationCategory.Objects, $"e{entity}", BitConverter.ToInt16(xyzi, 3),
                BitConverter.ToInt16(xyzi, 5), BitConverter.ToInt16(xyzi, 7), $"object:{field}:{entity}", TriggerEntityId: entity, InteractionRadius: 100);
            Equal(true, world.ArrivalsInto(from, field).Any(a => world.Planner(field).TryBuildRoute(world.At(field, a.X, a.Y, a.Triangle), target, out _)),
                $"{field} e{entity} is walked to from {from}'s arrival");
        }

        // Slash-All: two definitions, walked to only once the flytrap has shut.
        var slashAll = rewards.Where(o => o.FieldId == 620 && o.EntityId == 36).OrderBy(o => o.RequiredValue).ToArray();
        Equal((5, 51, (byte)0xFF, (byte)0, true), (slashAll[0].RequiredBank, slashAll[0].RequiredAddress, slashAll[0].RequiredMask, slashAll[0].RequiredValue,
            slashAll[0].ManualNavigationGuidance?.Contains("auto walk is unavailable", StringComparison.Ordinal) == true), "while the flytrap is open, guidance only");
        Equal((5, 51, (byte)0xFF, (byte)1, (string?)null), (slashAll[1].RequiredBank, slashAll[1].RequiredAddress, slashAll[1].RequiredMask, slashAll[1].RequiredValue,
            slashAll[1].ManualNavigationGuidance), "once it has shut, an ordinary target");
        foreach (var (line, script) in new[] { (25, 20), (26, 21) })
        {
            AssertOpcode(world.Scripts, 620, line, 4, 0, "145033000004", $"e{line}: only while 5[51] is 0");
            Equal(true, world.Scripts.ReadScriptOpcodes(620, line, 4).Any(op => Hex(op) == $"0600{0xC0 | script:X2}"), $"e{line} runs the leader's script {script}");
            Equal(true, world.Scripts.ReadScriptOpcodes(620, 4, script).Any(op => Hex(op) == "4F0000E803"), $"which takes 1000 HP");
        }

        Equal(true, world.Scripts.ReadScriptOpcodes(620, 16, 3).Any(op => Hex(op) == "80503301"), "only the beehive's throw shuts it");

        // The Minerva Band: 621's gateway into 620, then dir's poll.
        var minerva = rewards.Single(o => o.FieldId == 620 && o.EntityId == 35);
        Equal((FieldNavigationObjectKind.Item, 282), (minerva.Kind, minerva.NativeId), "620 box0 is the Minerva Band");
        var dir = world.Scripts.ReadScriptOpcodes(620, 0, 0).Select(Hex).ToArray();
        Equal(true, dir.Contains("8160351900") && dir.Contains("8160371A00") && dir.Contains("7566660003050700") &&
            dir.Contains("0600D9"), "dir: 6[53] = 25, 6[55] = 26, the leader's triangle into 6[0], and script 25 on either");
        var jumps = world.Scripts.ReadScriptOpcodes(620, 4, 25).Where(op => op.Opcode == 0xC0).Select(op => op.Bytes.ToArray())
            .Select(b => $"({BitConverter.ToInt16(b, 3)},{BitConverter.ToInt16(b, 5)})t{BitConverter.ToUInt16(b, 7)}").ToArray();
        Equal("(1090,-1106)t28,(1083,-1122)t29", string.Join(",", jumps), "cloud's script 25 jumps to t28, then t29");
        var fromMid = world.ArrivalsInto(621, 620).Single();
        Equal((1138, -1050, 25), fromMid, "621's gateway lands on t25");
        var scripts = world.Scripts.ReadAllScriptOpcodes(620);
        var placed = FieldEntryPlacement.Place(scripts, fromMid.X, fromMid.Y, fromMid.Triangle, new Dictionary<(int, int), int>());
        Equal(new FieldEntryPlacementResult(1083, -1122, null, 29), placed, "and the poll puts the party on t29");
        var band = new FieldNavigationTarget(620, FieldNavigationCategory.Objects, "Minerva Band", 954, -1251, 356, "object:620:35", TriggerEntityId: 35, InteractionRadius: 48);
        Equal(true, world.Planner(620).TryBuildRoute(world.At(620, 1083, -1122, 29), band, out _), "from t29 the band is walked to");
        Equal(false, world.Planner(620).TryBuildRoute(world.At(620, 1138, -1050, 25), band, out _), "from the raw landing it is not");
        foreach (var from in new[] { 622, 623 })
        {
            var arrival = world.ArrivalsInto(from, 620).Single();
            Equal(false, world.Planner(620).TryBuildRoute(world.At(620, arrival.X, arrival.Y, arrival.Triangle), band, out _),
                $"nor from {from}'s arrival on the ground");
        }

        // 623's way up to 621 is past the first plant: from 620's arrival it is not walked to,
        // so no chain of exits is offered from the ground; the puzzle is the player's.
        var upToMid = world.Exits(623).Single(exit => exit.StableId == "gateway:623:1:621");
        var fromGround = world.ArrivalsInto(620, 623).Single();
        Equal(false, world.Planner(623).TryBuildRoute(world.At(623, fromGround.X, fromGround.Y, fromGround.Triangle), upToMid, out _),
            "623's way up to 621 is not walked to from the ground");
        var groundStart = world.ArrivalsInto(622, 620).Single();
        Equal(null, world.Resolver().Resolve(world.At(620, groundStart.X, groundStart.Y, groundStart.Triangle), band, world.Exits(620)),
            "and no detour is invented");

        // What the poll does not decide: a key-held jump is the player's (ujp0 on t8), and a
        // poll behind a story test (Cosmo Canyon's AD8, 2[0] < 514, on t54) is not followed.
        Equal(new FieldEntryPlacementResult(-500, -150, null, 8),
            FieldEntryPlacement.Place(scripts, -500, -150, 8, new Dictionary<(int, int), int>()), "a key-held jump leaves the party where it lands");
        Equal((FieldEntryPlacementResult?)null,
            FieldEntryPlacement.Place(world.Scripts.ReadAllScriptOpcodes(534), 4, 117, 54, new Dictionary<(int, int), int>()), "a poll behind a story test fails closed");
    }

    // --- transfer writes -----------------------------------------------------------------------

    private static readonly byte[] SelectorThree = [0x80, 0xF0, 143, 3];
    private static readonly byte[] JumpToDestination = [0x60, 0xDD, 0x02, 0x10, 0, 0x20, 0, 7, 0, 0];

    private static FieldScriptDefinition Synthetic(int entity, int slot, params byte[][] operations)
    {
        var offset = 0;
        var decoded = new List<FieldScriptOpcodeDefinition>();
        foreach (var bytes in operations)
        {
            decoded.Add(new(733, entity, $"e{entity}", slot, offset, bytes[0], bytes));
            offset += bytes.Length;
        }

        return new(733, entity, $"e{entity}", slot, decoded);
    }

    private static FieldEntryWrites OnlyTransfer(params FieldScriptDefinition[] scripts)
    {
        var transfers = FieldSourceTransfers.Find(scripts, 1, 733);
        Equal(1, transfers.Select(transfer => transfer.Writes.Known.Count + ":" + transfer.Writes.Unknown.Count + ":" + transfer.Writes.AnyUnknown).Distinct().Count(),
            "one kind of transfer");
        return transfers[0].Writes;
    }

    /// <summary>
    /// What a line script wrote before its MAPJUMP counts only where every path to that jump
    /// wrote it: a write on one branch, one a request can overwrite, one a later writer changes
    /// unseen, is unknown - and an entry that tests an unknown byte is not given the raw landing.
    /// md8's own writes (a literal SETBYTE then the MAPJUMP) stay known.
    /// </summary>
    private static void TransferWritesAreProvenOnTheirPath()
    {
        var key = (15, 143);
        // The destination: its controlled actor places the party only for 15[143] == 3.
        var destination = Synthetic(1, 0, [0xA0, 0], [0x00], [0x14, 0xF0, 143, 3, 0, 12],
            [0xA5, 0, 0, 10, 0, 20, 0, 0, 0, 5, 0], [0x00]);

        var straight = OnlyTransfer(Synthetic(1, 2, SelectorThree, JumpToDestination));
        Equal(3, straight.Known.GetValueOrDefault(key, -1), "a literal write before the jump is known");
        Equal(new FieldEntryPlacementResult(10, 20, 0, 5), FieldEntryPlacement.Place([destination], 16, 32, 7, straight),
            "and the entry is followed from it");

        // IFUB 1[10] == 1 guards the write; the jump runs either way.
        var conditional = OnlyTransfer(Synthetic(1, 2, [0x14, 0x10, 10, 1, 0, 5], SelectorThree, JumpToDestination));
        Equal((false, true), (conditional.Known.ContainsKey(key), conditional.Unknown.Contains(key)), "a conditional write is unknown");
        Equal((FieldEntryPlacementResult?)null, FieldEntryPlacement.Place([destination], 16, 32, 7, conditional),
            "and an entry that tests it is not given the raw landing");

        // The write is on a branch that returns; the jump's own path never passes it.
        var skipped = OnlyTransfer(Synthetic(1, 2, [0x14, 0x10, 10, 1, 0, 6], SelectorThree, [0x00], JumpToDestination));
        Equal((false, false), (skipped.Known.ContainsKey(key), skipped.Unknown.Contains(key)), "a write on another path is not this jump's");

        var overwritten = OnlyTransfer(Synthetic(1, 2, SelectorThree, [0x80, 0xF0, 143, 5], JumpToDestination));
        Equal(5, overwritten.Known.GetValueOrDefault(key, -1), "the last literal write wins");

        // BITON 15[143] bit 2 after the literal: changed, value not followed.
        var changed = OnlyTransfer(Synthetic(1, 2, SelectorThree, [0x82, 0xF0, 143, 2], JumpToDestination));
        Equal((false, true), (changed.Known.ContainsKey(key), changed.Unknown.Contains(key)), "another writer leaves it unknown");

        // A requested script can overwrite it before the jump.
        var requested = OnlyTransfer(
            Synthetic(1, 2, SelectorThree, [0x03, 9, 3], JumpToDestination),
            Synthetic(9, 3, [0x80, 0xF0, 143, 4], [0x00]));
        Equal((false, true), (requested.Known.ContainsKey(key), requested.Unknown.Contains(key)), "a request's write is a side effect");
        Equal((FieldEntryPlacementResult?)null, FieldEntryPlacement.Place([destination], 16, 32, 7, requested), "and fails closed");

        // A jump in a script requested without waiting: the caller writes on beside it.
        var raced = FieldSourceTransfers.Find(
            [Synthetic(1, 2, SelectorThree, [0x01, 9, 3], [0x80, 0xF0, 143, 6], [0x00]), Synthetic(9, 3, JumpToDestination)], 1, 733);
        Equal(true, raced.Count == 1 && raced[0].Writes.Unknown.Contains(key), "a non-waiting request's jump does not trust its caller's write");

        // A loop that writes on its way round: at the jump the value is not one literal.
        var looped = OnlyTransfer(Synthetic(1, 2, SelectorThree, [0x14, 0x10, 10, 1, 0, 11], JumpToDestination,
            [0x80, 0xF0, 143, 4], [0x12, 20]));
        Equal(false, looped.Known.ContainsKey(key), "a loop's writes merge into unknown");

        // The entry itself: a loop back with a placement in it, and an unlisted writer, stop it.
        var known = new Dictionary<(int, int), int> { [key] = 3 };
        Equal((FieldEntryPlacementResult?)null, FieldEntryPlacement.Place(
            [Synthetic(1, 0, [0xA0, 0], [0x00], [0x14, 0xF0, 143, 3, 0, 12], [0xA5, 0, 0, 10, 0, 20, 0, 0, 0, 5, 0], [0x12, 17])],
            0, 0, 0, known), "a Main loop that can place the party again is not an end");
        Equal((FieldEntryPlacementResult?)null, FieldEntryPlacement.Place(
            [Synthetic(1, 0, [0xA0, 0], [0x00], [0x76, 0xF0, 143, 1], [0x14, 0xF0, 143, 3, 0, 12], [0xA5, 0, 0, 10, 0, 20, 0, 0, 0, 5, 0], [0x00])],
            0, 0, 0, known), "an entry that changes the tested byte with an unfollowed writer is not followed");
    }

    // --- shared plumbing -----------------------------------------------------------------------

    private static void SelectCategory(FieldNavigationController controller, FieldPositionSnapshot position, FieldNavigationCategory category)
    {
        for (var step = 0; step < 8 && controller.CurrentCategory != category; step++)
        {
            _ = controller.HandleAction(FieldNavigationAction.NextCategory, position, new FieldNavigationControlTransform(-128));
        }

        Equal(category, controller.CurrentCategory, "category selected");
    }

    private static HashSet<int> Walkable(FieldWalkmesh mesh, int from)
    {
        var seen = new HashSet<int> { from };
        var queue = new Queue<int>(seen);
        while (queue.TryDequeue(out var next))
        {
            var triangle = mesh.Triangles[next];
            for (var edge = 0; edge < 3; edge++)
            {
                var adjacent = triangle.GetAdjacentTriangle(edge);
                if (adjacent >= 0 && adjacent < mesh.Triangles.Count && seen.Add(adjacent))
                {
                    queue.Enqueue(adjacent);
                }
            }
        }

        return seen;
    }

    private static int Components(FieldWalkmesh mesh)
    {
        var seen = new HashSet<int>();
        var count = 0;
        for (var triangle = 0; triangle < mesh.Triangles.Count; triangle++)
        {
            if (!seen.Contains(triangle))
            {
                count++;
                seen.UnionWith(Walkable(mesh, triangle));
            }
        }

        return count;
    }

    private static FieldPositionSnapshot Position(int field, int x, int y, ushort triangle, int z) =>
        new(FieldPositionReader.FieldModule, field, 0, x, y, z, triangle, 0);

    private static string Hex(FieldScriptOpcodeDefinition op) => Convert.ToHexString(op.Bytes.ToArray());

    private static void AssertOpcode(FieldScriptNavigationCatalog scripts, int field, int entity, int script, int offset,
        string expected, string label)
    {
        var op = scripts.ReadScriptOpcodes(field, entity, script).FirstOrDefault(candidate => candidate.ByteIndex == offset);
        Equal(expected, op.Bytes is null ? "(none)" : Hex(op), label);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }

    /// <summary>
    /// The installed fields as the live runtimes see them: each field's own walkmesh, gidun_1's
    /// passage lock (triangle 16), their script exits and gateways, and one planner that serves
    /// whichever field a position is in.
    /// </summary>
    private sealed class Caves
    {
        private const int FieldDataBase = 0x02000000;
        private const int FieldState = 0x03200000;
        private readonly FlevelDataSource source = new(DataRoot!);
        private readonly Dictionary<int, byte[]> decoded = [];
        private readonly Dictionary<int, FieldWalkmeshRoutePlanner> planners = [];
        private readonly Dictionary<int, FieldWalkmesh> meshes = [];

        public Caves()
        {
            Scripts = new FieldScriptNavigationCatalog(DataRoot!);
            SharedPlanner = new ByField(this);
        }

        public FieldScriptNavigationCatalog Scripts { get; }

        public IFieldNavigationRoutePlanner SharedPlanner { get; }

        public byte[] Decoded(int field)
        {
            if (!decoded.TryGetValue(field, out var bytes))
            {
                Equal(true, source.TryReadField(field, out var encoded), $"field {field} is in the archive");
                decoded[field] = bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
            }

            return bytes;
        }

        public FieldWalkmesh Mesh(int field)
        {
            if (!meshes.TryGetValue(field, out var mesh))
            {
                meshes[field] = mesh = Reader(field).Read(Position(field, 0, 0, 0, 0)).Walkmesh
                    ?? throw new InvalidOperationException($"field {field} walkmesh must be readable");
            }

            return mesh;
        }

        public FieldWalkmeshRoutePlanner Planner(int field)
        {
            if (!planners.TryGetValue(field, out var planner))
            {
                var transitions = Scripts.ReadField(field).Transitions;
                planners[field] = planner = new FieldWalkmeshRoutePlanner(
                    Reader(field),
                    new FieldBoundaryStateReader(
                        address => address == FieldBoundaryStateReader.AddressFieldGlobalObjectPtr ? FieldState : 0,
                        address => LockByte(field, address),
                        (_, _) => true),
                    _ => transitions);
            }

            return planner;
        }

        /// <summary>The field's walkmesh alone: no scripted traversal, so a walk's path is the one to check.</summary>
        public FieldWalkmeshRoutePlanner WalkingPlanner(int field) => new(Reader(field));

        /// <summary>A standing position at the centre of every triangle of the field.</summary>
        public IEnumerable<FieldPositionSnapshot> Starts(int field) =>
            Mesh(field).Triangles.Select(triangle =>
            {
                var centre = triangle.GetCentroid();
                return At(field, (int)Math.Round(centre.X), (int)Math.Round(centre.Y), triangle.Index);
            });

        public FieldNavigationTriggerLine LineOf(int field, int entity)
        {
            var line = Scripts.ReadScriptOpcodes(field, entity, 0).Single(op => op.Opcode == 0xD0).Bytes.ToArray();
            return new(BitConverter.ToInt16(line, 1), BitConverter.ToInt16(line, 3), BitConverter.ToInt16(line, 5),
                BitConverter.ToInt16(line, 7), BitConverter.ToInt16(line, 9), BitConverter.ToInt16(line, 11));
        }

        /// <summary>
        /// The leader's collision radius (event +0x72) once the field has set it up: field
        /// initialisation (0060BCFA) makes it 30, and SLIDR (0061813D) its argument, each times
        /// the script header's scale / 512. The leader is the entity bound to Cloud (PC 0).
        /// </summary>
        public int CollisionRadius(int field)
        {
            var bytes = Decoded(field);
            var scale = BitConverter.ToInt16(bytes, BitConverter.ToInt32(bytes, 6) + 4 + 8);
            var leader = Scripts.ReadAllScriptOpcodes(field).First(s => s.ScriptId == 0 && s.Opcodes.Any(op => Hex(op) == "A000"));
            var slidr = leader.Opcodes.LastOrDefault(op => op.Opcode == 0xC6 && op.Bytes.Count == 3 && op.Bytes[1] == 0);
            var value = slidr.Bytes is null ? 30 : slidr.Bytes[2];
            return value * scale / 512;
        }

        public FieldCrossFieldApproachResolver Resolver() =>
            new(Scripts, field => Decoded(field), SharedPlanner, position => Mesh(position.FieldId));

        public FieldPositionSnapshot At(int field, int x, int y, int triangle) =>
            Position(field, x, y, (ushort)Math.Max(0, triangle),
                triangle >= 0 && triangle < Mesh(field).Triangles.Count
                    ? (int)Math.Round(Mesh(field).Triangles[triangle].GetCentroid().Z)
                    : 0);

        /// <summary>A standing position on the triangle whose centre is nearest (x, y).</summary>
        public FieldPositionSnapshot Near(int field, int x, int y)
        {
            var mesh = Mesh(field);
            var triangle = Enumerable.Range(0, mesh.Triangles.Count)
                .OrderBy(index =>
                {
                    var centre = mesh.Triangles[index].GetCentroid();
                    return (centre.X - x) * (centre.X - x) + (centre.Y - y) * (centre.Y - y);
                })
                .First();
            var at = mesh.Triangles[triangle].GetCentroid();
            return At(field, (int)Math.Round(at.X), (int)Math.Round(at.Y), triangle);
        }

        /// <summary>The exits a field offers: its script exits and native gateways.</summary>
        public IReadOnlyList<FieldNavigationTarget> Exits(int field)
        {
            var exits = Scripts.ReadField(field).Exits
                .Select(exit => exit with { Label = "Exit to Cave of the Gi" })
                .ToList();
            foreach (var gateway in Gateways(field))
            {
                exits.Add(new FieldNavigationTarget(field, FieldNavigationCategory.Exits, "Exit",
                    (gateway.Line.StartX + gateway.Line.EndX) / 2, (gateway.Line.StartY + gateway.Line.EndY) / 2,
                    (gateway.Line.StartZ + gateway.Line.EndZ) / 2, $"gateway:{field}:{gateway.Index}:{gateway.Destination}",
                    DestinationFieldIds: [gateway.Destination], TriggerLine: gateway.Line));
            }

            return exits;
        }

        /// <summary>Where <paramref name="from"/>'s gateways and map jumps put the party in <paramref name="to"/>.</summary>
        public IReadOnlyList<(int X, int Y, int Triangle)> ArrivalsInto(int from, int to)
        {
            var arrivals = Gateways(from).Where(gateway => gateway.Destination == to)
                .Select(gateway => (gateway.X, gateway.Y, gateway.Triangle)).ToList();
            foreach (var op in Scripts.ReadAllScriptOpcodes(from).SelectMany(script => script.Opcodes)
                         .Where(op => op.Opcode == 0x60 && op.Bytes.Count == 10))
            {
                var b = op.Bytes.ToArray();
                if (BitConverter.ToUInt16(b, 1) == to)
                {
                    arrivals.Add((BitConverter.ToInt16(b, 3), BitConverter.ToInt16(b, 5), BitConverter.ToUInt16(b, 7)));
                }
            }

            return arrivals.Distinct().ToArray();
        }

        private IEnumerable<(int Index, int Destination, FieldNavigationTriggerLine Line, int X, int Y, int Triangle)> Gateways(int field)
        {
            var bytes = Decoded(field);
            var section = BitConverter.ToInt32(bytes, 6 + 7 * 4) + 4;
            for (var index = 0; index < 12; index++)
            {
                var at = section + 0x38 + index * 24;
                var destination = BitConverter.ToInt16(bytes, at + 18);
                if (destination >= 0 && destination != short.MaxValue && destination != field)
                {
                    yield return (index, destination,
                        new FieldNavigationTriggerLine(
                            BitConverter.ToInt16(bytes, at), BitConverter.ToInt16(bytes, at + 2), BitConverter.ToInt16(bytes, at + 4),
                            BitConverter.ToInt16(bytes, at + 6), BitConverter.ToInt16(bytes, at + 8), BitConverter.ToInt16(bytes, at + 10)),
                        BitConverter.ToInt16(bytes, at + 12), BitConverter.ToInt16(bytes, at + 14), BitConverter.ToUInt16(bytes, at + 16));
                }
            }
        }

        private FieldWalkmeshReader Reader(int field)
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

        /// <summary>
        /// gidun_1's passage: AD's Main locks triangle 16 (6D100001) until 3[182] bit 4 is set,
        /// and SWITCHC unlocks it (6D100000) when the player looks into the opening. The log
        /// shows it locked until 14:13:55 and "Take the passage that has opened" at 14:14:05,
        /// six seconds before the failure, so it is open by default here.
        /// </summary>
        public bool PassageShut { get; set; }

        private byte LockByte(int field, int address)
        {
            // The boundary reader first checks that the loaded field is the one being planned.
            if (address == FieldPositionReader.AddressCurrentModule)
            {
                return (byte)FieldPositionReader.FieldModule;
            }

            if (address == FieldPositionReader.AddressFieldId || address == FieldPositionReader.AddressFieldId + 1)
            {
                return (byte)(field >> (address == FieldPositionReader.AddressFieldId ? 0 : 8));
            }

            var offset = address - FieldState - FieldBoundaryStateReader.BoundaryBitsOffset;
            return PassageShut && field == CaveFloor && offset == 16 / 8 ? (byte)(1 << (16 % 8)) : (byte)0;
        }

        private sealed class ByField(Caves world) : IFieldNavigationRoutePlanner
        {
            public string LastDiagnostic { get; private set; } = string.Empty;

            public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle) =>
                world.Planner(position.FieldId).TryResolvePlayerTriangle(position, out triangle);

            public bool TryBuildRoute(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRoutePlan plan)
            {
                var planner = world.Planner(position.FieldId);
                var built = planner.TryBuildRoute(position, target, out plan);
                LastDiagnostic = planner.LastDiagnostic;
                return built;
            }

            public bool TryGetNextWaypoint(FieldPositionSnapshot position, FieldNavigationTarget target, out FieldNavigationRouteWaypoint waypoint) =>
                world.Planner(position.FieldId).TryGetNextWaypoint(position, target, out waypoint);
        }
    }

    /// <summary>The saved banks the Story reader reads, and nothing else.</summary>
    private sealed class SaveMemory
    {
        private const int EventTable = 0x02404000;
        private readonly Dictionary<int, byte> bytes = [];
        private int leaderRadius;

        public SaveMemory()
        {
            bytes[FieldPositionReader.AddressCurrentModule] = (byte)FieldPositionReader.FieldModule;
        }

        /// <summary>A live field's event table with the leader (model 0) and its collision radius at +0x72.</summary>
        public void SetLeaderRadius(int radius)
        {
            leaderRadius = radius;
            bytes[FieldPositionReader.AddressFieldNumModels] = 1;
        }

        public void SetGameMoment(int moment)
        {
            bytes[FieldNavigationObjectReader.AddressFieldBankBase] = (byte)moment;
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(moment >> 8);
        }

        public void SetBankByte(int bank, int address, byte value) =>
            bytes[FieldNavigationObjectReader.AddressFieldBankBase + bank switch
            {
                1 => 0,
                3 => 0x100,
                11 => 0x200,
                13 => 0x300,
                15 => 0x400,
                _ => throw new ArgumentOutOfRangeException(nameof(bank))
            } + address] = value;

        public byte ReadByte(int address) => bytes.GetValueOrDefault(address);

        public int ReadInt32(int address) =>
            address == FieldNavigationObjectReader.AddressFieldEventDataPtr && leaderRadius > 0 ? EventTable : 0;

        public short ReadInt16(int address) =>
            address == EventTable + FieldNavigationNpcReader.CollisionRadiusOffset ? (short)leaderRadius : (short)0;
    }
}

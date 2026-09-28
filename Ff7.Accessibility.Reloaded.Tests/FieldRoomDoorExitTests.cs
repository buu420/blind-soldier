using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Shinra Mansion basement's coffin room, reported 2026-09-28: "It's not part of the main
/// story, the exits aren't showing up like they should in the basement." After the Key to the
/// Basement, every Exits list in sininb2 (303) read the stairs and two library doors and
/// nothing else, exactly as before the key; the room with Vincent's coffin is behind a door
/// that has no gateway, LINE or MAPJUMP, only the Director's <c>IDLCK 34</c>.
///
/// <para>Everything here is read from the installed archive: the lock and its key condition
/// from the field script, the room from the walkmesh, and the walk through the door with the
/// native step at both speeds.</para>
/// </summary>
internal static class FieldRoomDoorExitTests
{
    private const int Field = 303;
    private const int Door = 34;
    private const int Radius = 30;
    private const string EnterId = "room-door:303:34:enter";
    private const string LeaveId = "room-door:303:34:leave";

    internal static void Run(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner>? wrap = null,
        string runtime = "legacy")
    {
        ArgumentNullException.ThrowIfNull(createWalkmeshReader);
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            Console.WriteLine("room door exits: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        TheInstalledScriptLocksTheDoorUntilTheKey(dataRoot);
        var mesh = createWalkmeshReader(Field).Read(Position(0, 0, 0)).Walkmesh
                   ?? throw new InvalidOperationException("installed sininb2 walkmesh is unavailable");
        TheCatalogRoomIsTheRoomBehindTheInstalledDoor(mesh);
        EachSideOffersItsOwnWayThroughTheDoor();
        wrap ??= planner => planner;
        BeforeTheKeyTheDoorIsListedAndSaidToBeShut(createWalkmeshReader, wrap);
        AfterTheKeyTheRoomAndTheCoffinRouteThroughTheDoor(createWalkmeshReader, wrap);
        ReenteringAfterTheKeyReadsTheLockAfresh(createWalkmeshReader, wrap);
        AnUnreadableLockPublishesNoDoor(createWalkmeshReader, wrap);
        SelectingOnTheThresholdFinishesNothing(createWalkmeshReader, wrap);
        var room = FieldRoomDoorExitCatalog.RoomTriangles(Field, Door)!;
        var passage = FieldRoomDoorExitCatalog.PassageTriangles(Field, Door)!;
        var failures = new List<string>();
        // 12 frames a sample at running speed is 96 units: the sample that crosses the door
        // lands past the first triangle on the far side, and must still complete there.
        foreach (var (speed, frames) in new (ushort, int)[] { (1024, 2), (1024, 4), (2048, 2), (2048, 4), (2048, 12) })
        {
            failures.AddRange(WalkThroughTheDoor(createWalkmeshReader, wrap, mesh, EnterId, -55, -411, room, speed, frames));
            failures.AddRange(WalkThroughTheDoor(createWalkmeshReader, wrap, mesh, LeaveId, 303, -787, passage, speed, frames));
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException($"{runtime} room door exits:" + Environment.NewLine +
                                                string.Join(Environment.NewLine, failures));
        }

        Console.WriteLine($"PASS {runtime} coffin room door: offered by side, shut before the key, walked through at both native speeds.");
    }

    /// <summary>
    /// sininb2's Director Init: <c>IFUB B1[232] !bit 1</c> (1410E8010A05) and then
    /// <c>IDLCK 34 lock</c> (6D220001). That is the only IDLCK of triangle 34 in the field's
    /// scripts: nothing, including Vincent joining, locks or unlocks it again.
    /// </summary>
    private static void TheInstalledScriptLocksTheDoorUntilTheKey(string dataRoot)
    {
        var source = new FlevelDataSource(dataRoot);
        if (!source.TryReadField(Field, out var encoded))
        {
            throw new InvalidOperationException($"installed field {Field} is unavailable: {source.Diagnostic}");
        }

        var field = Ff7LzsDecoder.DecodeFieldFile(encoded);
        var scriptStart = BitConverter.ToInt32(field, 6) + 4;
        var scriptEnd = BitConverter.ToInt32(field, 10);
        var script = field.AsSpan(scriptStart, scriptEnd - scriptStart);
        byte[] keyThenLock = [0x14, 0x10, 0xE8, 0x01, 0x0A, 0x05, 0x6D, 0x22, 0x00, 0x01];
        Equal(true, script.IndexOf(keyThenLock) >= 0, "the Director locks triangle 34 unless B1[232] bit 1 is set");
        var locks = 0;
        for (var index = 0; index + 3 < script.Length; index++)
        {
            if (script[index] == 0x6D && script[index + 1] == Door && script[index + 2] == 0x00)
            {
                locks++;
            }
        }

        Equal(1, locks, "and nothing else in sininb2 locks or releases triangle 34");
    }

    private static void TheCatalogRoomIsTheRoomBehindTheInstalledDoor(FieldWalkmesh mesh)
    {
        var door = mesh.Triangles[Door];
        Equal((35, -1, 6), ((int)door.Adjacent0, (int)door.Adjacent1, (int)door.Adjacent2),
            "triangle 34 joins the room's 35 to the passage's 6 and nothing else");
        var room = Component(mesh, 35, Door);
        var catalog = FieldRoomDoorExitCatalog.RoomTriangles(Field, Door)
                      ?? throw new InvalidOperationException("the catalog has no sininb2 door");
        Equal(string.Join(',', room.Order()), string.Join(',', catalog.Order()),
            "the catalog's room is exactly what triangle 34 closes off");
        Equal(false, Component(mesh, 12, Door).Overlaps(room), "and the passage cannot reach it any other way");
        var passage = Component(mesh, 12, Door);
        passage.Remove(Door);
        Equal(string.Join(',', passage.Order()),
            string.Join(',', (FieldRoomDoorExitCatalog.PassageTriangles(Field, Door) ?? new HashSet<int>()).Order()),
            "the catalog's passage is exactly the other side of triangle 34");
        Equal(true, room.Contains(76), "the coffin (664,-934) is on triangle 76, inside it");
    }

    private static void EachSideOffersItsOwnWayThroughTheDoor()
    {
        string Ids(int triangle, int x, int y) => string.Join(',',
            FieldRoomDoorExitCatalog.ForPosition(Position(x, y, triangle)).Select(target => target.StableId));
        Equal(EnterId, Ids(12, -55, -411), "the passage (the arrival from 302) offers the way in");
        Equal(LeaveId, Ids(76, 664, -934), "beside the coffin, the way out");
        Equal($"{EnterId},{LeaveId}", Ids(Door, 218, -592), "on the threshold, both");
        Equal(LeaveId, Ids(35, 250, -587), "just inside the room, only the way out is offered");
        Equal(EnterId, Ids(6, 216, -513), "just outside it, only the way in");
        Equal(string.Empty, string.Join(',', FieldRoomDoorExitCatalog.ForPosition(
            new FieldPositionSnapshot(FieldPositionReader.FieldModule, 302, 0, 0, 0, 0, 12, 0)).Select(t => t.StableId)),
            "no other field has these doors");
        var enter = FieldRoomDoorExitCatalog.ForPosition(Position(-55, -411, 12)).Single();
        Equal("Enter the coffin room", enter.Label, "named for the room, not for Vincent");
        Equal(FieldNavigationCategory.Exits, enter.Category, "an Exit, not Story");
        Equal(string.Join(',', FieldRoomDoorExitCatalog.RoomTriangles(Field, Door)!.Order()),
            string.Join(',', (enter.CompletionTriangles ?? []).Order()),
            "finished only by standing in the room, anywhere in it");
    }

    private static void BeforeTheKeyTheDoorIsListedAndSaidToBeShut(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap)
    {
        var planner = wrap(new FieldWalkmeshRoutePlanner(createWalkmeshReader(Field),
            FieldBodyClearanceRouteTests.LockedTriangles(Field, [Door]), playerCollisionRadiusProvider: _ => Radius));
        var provider = new ReachableFieldExitTargetProvider(
            position => FieldRoomDoorExitCatalog.Append(Array.Empty<FieldNavigationTarget>(), position), planner);
        var passage = Position(-55, -411, 12);
        Begin(planner);
        var exits = provider.ReadTargets(passage);
        Equal(EnterId, string.Join(',', exits.Select(target => target.StableId)),
            "before the key the door is still in the Exits list");
        Equal(true, provider.LastDiagnostic.Contains("shut by native boundary=Enter the coffin room", StringComparison.Ordinal),
            $"and said to be shut, not open: {provider.LastDiagnostic}");
        Begin(planner);
        Equal(false, planner.TryBuildRoute(passage, Coffin(), out _), "nothing routes to the coffin through the lock");
        Equal(true, planner is IFieldNavigationNativeBoundaryStatus { LastFailureWasNativeBoundary: true },
            "and that is the game's own lock");
    }

    private static void AfterTheKeyTheRoomAndTheCoffinRouteThroughTheDoor(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap)
    {
        var planner = wrap(new FieldWalkmeshRoutePlanner(createWalkmeshReader(Field),
            FieldBodyClearanceRouteTests.LockedTriangles(Field, []), playerCollisionRadiusProvider: _ => Radius));
        var passage = Position(-55, -411, 12);
        var enter = FieldRoomDoorExitCatalog.ForPosition(passage).Single();
        Begin(planner);
        Equal(true, planner.TryBuildRoute(passage, enter, out var plan), $"a route into the room: {planner.LastDiagnostic}");
        Equal(true, plan.TrianglePath.Contains(Door) && plan.TrianglePath[^1] == 35,
            $"through the threshold to the room's first triangle: {string.Join(',', plan.TrianglePath)}");
        Begin(planner);
        Equal(true, planner.TryBuildRoute(passage, Coffin(), out var coffinPlan), "and on to the coffin");
        Equal(true, coffinPlan.TrianglePath.Contains(Door), "through the same door");
    }

    /// <summary>
    /// The lock is Init's, so it changes only across a field load. The same planner and
    /// provider must see the new state on the next read, not a cached one.
    /// </summary>
    private static void ReenteringAfterTheKeyReadsTheLockAfresh(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap)
    {
        var bits = new byte[FieldBoundaryStateReader.BoundaryByteCount];
        bits[Door >> 3] |= 1 << (Door & 7);
        const int fieldState = 0x00200000;
        var boundaries = new FieldBoundaryStateReader(
            readInt32: address => address == FieldBoundaryStateReader.AddressFieldGlobalObjectPtr ? fieldState : 0,
            readByte: address => address == FieldPositionReader.AddressCurrentModule ? (byte)FieldPositionReader.FieldModule
                : address == FieldPositionReader.AddressFieldId ? (byte)(Field & 0xFF)
                : address == FieldPositionReader.AddressFieldId + 1 ? (byte)(Field >> 8)
                : address - (fieldState + FieldBoundaryStateReader.BoundaryBitsOffset) is var offset && offset >= 0 && offset < bits.Length
                    ? bits[offset] : (byte)0,
            isReadableMemory: (_, _) => true);
        var planner = wrap(new FieldWalkmeshRoutePlanner(createWalkmeshReader(Field), boundaries,
            playerCollisionRadiusProvider: _ => Radius));
        var provider = new ReachableFieldExitTargetProvider(
            position => FieldRoomDoorExitCatalog.Append(Array.Empty<FieldNavigationTarget>(), position), planner);
        var passage = Position(-55, -411, 12);
        Begin(planner);
        provider.ReadTargets(passage);
        Equal(true, provider.LastDiagnostic.Contains("shut by native boundary", StringComparison.Ordinal), "shut before the key");
        bits[Door >> 3] = 0;
        Begin(planner);
        provider.ReadTargets(passage);
        Equal(false, provider.LastDiagnostic.Contains("shut", StringComparison.Ordinal),
            $"open on the next entry once Init leaves it unlocked: {provider.LastDiagnostic}");
    }

    /// <summary>Unknown is not open: a lock that cannot be read publishes no door at all.</summary>
    private static void AnUnreadableLockPublishesNoDoor(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap)
    {
        var unreadable = new FieldBoundaryStateReader(
            readInt32: _ => 0x00200000,
            readByte: _ => 0,
            isReadableMemory: (_, _) => false);
        var planner = wrap(new FieldWalkmeshRoutePlanner(createWalkmeshReader(Field), unreadable,
            playerCollisionRadiusProvider: _ => Radius));
        var provider = new ReachableFieldExitTargetProvider(
            position => FieldRoomDoorExitCatalog.Append(Array.Empty<FieldNavigationTarget>(), position), planner);
        Begin(planner);
        Equal(0, provider.ReadTargets(Position(-55, -411, 12)).Count, "no door is published from an unreadable lock");
        Equal(false, planner is IFieldNavigationNativeBoundaryStatus { LastFailureWasNativeBoundary: true },
            "and it is not reported as the game holding it shut");
    }

    /// <summary>
    /// Selected from the Exits list and walked by auto walk with the native step: finished on
    /// the frame the party stands in the far triangle, never before, and never cancelled on
    /// the way because the offer changed as the party crossed.
    /// </summary>
    private static IEnumerable<string> WalkThroughTheDoor(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap,
        FieldWalkmesh mesh,
        string targetId,
        int startX,
        int startY,
        IReadOnlySet<int> farSide,
        ushort speed,
        int framesPerSample)
    {
        var name = $"{targetId} from {startX},{startY} speed {speed}, {framesPerSample} frames/sample";
        var planner = wrap(new FieldWalkmeshRoutePlanner(createWalkmeshReader(Field),
            FieldBodyClearanceRouteTests.LockedTriangles(Field, []), playerCollisionRadiusProvider: _ => Radius));
        var provider = new ReachableFieldExitTargetProvider(
            position => FieldRoomDoorExitCatalog.Append(Array.Empty<FieldNavigationTarget>(), position), planner);
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([], exitTargetProvider: position =>
            {
                Begin(planner);
                return provider.ReadTargets(position);
            }),
            planner);
        var (flat, originalIds, flatIds) = FieldBodyClearanceRouteTests.FlatPortion(mesh);
        var startTriangle = FieldWalkmeshPathfinder.ResolveTriangle(mesh, startX, startY, 0, -1);
        if (!flatIds.TryGetValue(startTriangle, out var flatStart))
        {
            yield return $"{name}: the start is not on the flat floor";
            yield break;
        }

        var state = new FieldNavigationNativeMovementState(startX * 4096, startY * 4096, 0, flatStart, 0);
        FieldPositionSnapshot Here() => new(FieldPositionReader.FieldModule, Field, 0,
            state.FixedX >> 12, state.FixedY >> 12, 0, (ushort)originalIds[state.TriangleId], state.Heading)
        {
            NativeFixedPosition = new(state.FixedX, state.FixedY, 0)
        };
        var transform = new FieldNavigationControlTransform(0);
        while (controller.CurrentCategory != FieldNavigationCategory.Exits)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, Here(), transform);
        }

        var selection = controller.HandleAction(FieldNavigationAction.ToggleBeacon, Here(), transform);
        var expectedLabel = targetId == EnterId ? "Enter the coffin room" : "Leave the coffin room";
        if (!string.Equals(controller.CurrentTargetLabel, expectedLabel, StringComparison.Ordinal))
        {
            yield return $"{name}: selected \"{controller.CurrentTargetLabel}\" ({selection?.Speech})";
            yield break;
        }

        var input = FieldNavigationInput.None;
        for (var tick = 0; tick < 500; tick++)
        {
            var position = Here();
            var spoken = controller.UpdateLiveTracking(position, new(0, input), transform, false, 80,
                observedAt: DateTime.UnixEpoch.AddMilliseconds(tick * 80))?.Speech;
            if (spoken is not null)
            {
                if (spoken.Contains("reached", StringComparison.Ordinal) && farSide.Contains(position.TriangleId))
                {
                    yield break;
                }

                yield return $"{name}: said \"{spoken}\" at {position.X},{position.Y} triangle {position.TriangleId}";
                yield break;
            }

            if (!controller.TryResolveAutomaticInput(position, transform, 80, out input))
            {
                yield return $"{name}: auto walk stopped at {position.X},{position.Y} triangle {position.TriangleId}: " +
                             $"{controller.LastAutomaticInputHold} ({controller.LastNavigationDiagnostic})";
                yield break;
            }

            var (dx, dy) = FieldNavigationMovementObserver.PredictWorldDirection(input, transform);
            var heading = unchecked((byte)(int)Math.Round(Math.Atan2(dx, -dy) * 128 / Math.PI));
            for (var frame = 0; frame < framesPerSample; frame++)
            {
                state = FieldNavigationNativeProbeMovement.Step(flat, state, heading, speed, Radius,
                    Array.Empty<FieldNavigationDynamicObstacle>()).State;
            }
        }

        yield return $"{name}: not through the door in 500 samples, at {Here().X},{Here().Y} triangle {Here().TriangleId}";
    }

    /// <summary>
    /// On the threshold both ways are offered. Choosing either there starts a walk and
    /// finishes nothing: the party is on neither side yet.
    /// </summary>
    private static void SelectingOnTheThresholdFinishesNothing(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap)
    {
        foreach (var label in new[] { "Enter the coffin room", "Leave the coffin room" })
        {
            var planner = wrap(new FieldWalkmeshRoutePlanner(createWalkmeshReader(Field),
                FieldBodyClearanceRouteTests.LockedTriangles(Field, []), playerCollisionRadiusProvider: _ => Radius));
            var provider = new ReachableFieldExitTargetProvider(
                position => FieldRoomDoorExitCatalog.Append(Array.Empty<FieldNavigationTarget>(), position), planner);
            var controller = new FieldNavigationController(new FieldNavigationTargetSource([], exitTargetProvider: position =>
            {
                Begin(planner);
                return provider.ReadTargets(position);
            }), planner);
            var threshold = Position(218, -592, Door);
            var transform = new FieldNavigationControlTransform(0);
            while (controller.CurrentCategory != FieldNavigationCategory.Exits)
            {
                controller.HandleAction(FieldNavigationAction.NextCategory, threshold, transform);
            }

            for (var press = 0; press < 4 && controller.HandleAction(FieldNavigationAction.RepeatTarget, threshold,
                     transform)?.Speech?.Contains(label, StringComparison.Ordinal) != true; press++)
            {
                controller.HandleAction(FieldNavigationAction.NextTarget, threshold, transform);
            }

            var selected = controller.HandleAction(FieldNavigationAction.ToggleBeacon, threshold, transform);
            Equal(label, controller.CurrentTargetLabel, $"the threshold offers \"{label}\" to choose ({selected?.Speech})");
            var spoken = controller.UpdateLiveTracking(threshold, new(0, FieldNavigationInput.None), transform, false, 80,
                observedAt: DateTime.UnixEpoch)?.Speech ?? string.Empty;
            Equal(false, spoken.Contains("reached", StringComparison.Ordinal),
                $"choosing \"{label}\" on the threshold is not arriving: \"{spoken}\"");
            Equal(true, controller.BeaconEnabled, $"and \"{label}\" stays the walk under way");
        }
    }

    private static void Begin(IFieldNavigationRoutePlanner planner)
    {
        // The Steam 2026 wrapper reads per observation, as its coordinator drives it.
        planner.GetType().GetMethod("BeginObservation",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public)?.Invoke(planner, null);
    }

    private static FieldNavigationTarget Coffin() =>
        new(Field, FieldNavigationCategory.Objects, "Coffin in the mansion basement", 664, -934, 22,
            "object:303:coffin");

    private static FieldPositionSnapshot Position(int x, int y, int triangle) =>
        new(FieldPositionReader.FieldModule, Field, 0, x, y, 0, (ushort)triangle, 0);

    private static HashSet<int> Component(FieldWalkmesh mesh, int start, int closed)
    {
        var seen = new HashSet<int> { start };
        var pending = new Stack<int>([start]);
        while (pending.Count > 0)
        {
            var triangle = mesh.Triangles[pending.Pop()];
            for (var edge = 0; edge < 3; edge++)
            {
                var neighbour = triangle.GetAdjacentTriangle(edge);
                if (neighbour >= 0 && neighbour != closed && seen.Add(neighbour))
                {
                    pending.Push(neighbour);
                }
            }
        }

        return seen;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"room door exits: {label}: expected {expected}, got {actual}.");
        }
    }
}

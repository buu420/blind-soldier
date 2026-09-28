using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Cid's house, rktsid (558), on a return visit (GameMoment 566..999): Shera stands in the
/// kitchen on triangle 49, (330,321), behind a door the game holds shut with two locks.
///
/// <para>From the installed script (both archives): entity 4's main runs IDLCK 102 and IDLCK 106
/// lock on every load (and 70 and 71 once past moment 563). Entity 9 door1 declares
/// LINE (38,196,0)-(44,267,0); its OK slot and its Move slot are the same code, which runs
/// IFUB 5[13] == 0, INC 5[13], IDLCK 102 unlock, IDLCK 106 unlock. The temporary bank is zeroed on
/// every load, so the first walk onto the line opens the door. 102 joins 107 to 106 and 106 joins
/// 102 to 88: releasing either alone opens nothing, which is why the one-triangle question the
/// door approach used to ask could never pick this door.</para>
///
/// <para>The replay is the production controller walking the installed walkmesh with its own
/// emitted input and the native step. The locks are walls to every frame and are released only
/// when that movement is strictly inside the line's radius, measured here independently of the
/// code under test, and only once, as 5[13] allows.</para>
/// </summary>
internal static class CidHouseDoorApproachTests
{
    private const int Field = 558;
    private const int DoorLine = 9;
    private const int Radius = 30;
    private static readonly int[] DoorLocks = [102, 106];
    private static readonly int[] ReturnVisitLocks = [102, 106, 70, 71];
    private static readonly FieldNavigationTriggerLine Line = new(38, 196, 0, 44, 267, 0);
    private static readonly FieldPositionSnapshot Entry = new(FieldPositionReader.FieldModule, Field, 0, -199, -52, 0, 21, 128);

    private static FieldNavigationTarget Shera(int talkRadius) =>
        new(Field, FieldNavigationCategory.Npcs, "Shera", 330, 321, 0, $"npc:558:23:{talkRadius}",
            TriggerEntityId: 23, InteractionRadius: talkRadius);

    internal static void Run(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner>? wrap = null,
        string runtime = "legacy")
    {
        ArgumentNullException.ThrowIfNull(createWalkmeshReader);
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            Console.WriteLine("Cid's house door: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        wrap ??= planner => planner;
        TheInstalledScriptOpensBothLocksFromTheLine(dataRoot);
        OnlyTheWholeGroupOpensTheKitchen(createWalkmeshReader, wrap);
        var failures = new List<string>();
        foreach (var talkRadius in new[] { 60, 90, 120 })
        {
            foreach (var (speed, frames) in new (ushort, int)[] { (1024, 2), (2048, 2), (2048, 4) })
            {
                failures.AddRange(WalkToShera(createWalkmeshReader, wrap, talkRadius, speed, frames));
            }
        }

        failures.AddRange(ADisabledOrUnreadableLineHolds(createWalkmeshReader, wrap));
        failures.AddRange(AnUnreadableLockStateIsNotALock(createWalkmeshReader, wrap));
        failures.AddRange(ADifferentLockIsNotThisDoor(createWalkmeshReader, wrap));
        failures.AddRange(TwoDoorsAreKeptApart(createWalkmeshReader, wrap));
        failures.AddRange(CancellingStopsTheWalkForGood(createWalkmeshReader, wrap));
        if (failures.Count > 0)
        {
            throw new InvalidOperationException($"{runtime} Cid's house door:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }

        Console.WriteLine($"PASS {runtime} Cid's house door: walked onto its line with both locks on, " +
                          "opened only by the line, then on to Shera within talking range at both native speeds.");
    }

    private static void TheInstalledScriptOpensBothLocksFromTheLine(string dataRoot)
    {
        var source = new FlevelDataSource(dataRoot);
        if (!source.TryReadField(Field, out var encoded))
        {
            throw new InvalidOperationException($"installed field {Field} is unavailable: {source.Diagnostic}");
        }

        var field = Ff7LzsDecoder.DecodeFieldFile(encoded);
        var sectionStart = BitConverter.ToInt32(field, 6) + 4;
        var sectionEnd = BitConverter.ToInt32(field, 10);
        var script = field[sectionStart..sectionEnd];
        byte[] lineDeclaration = [0xD0, 0x26, 0x00, 0xC4, 0x00, 0x00, 0x00, 0x2C, 0x00, 0x0B, 0x01, 0x00, 0x00];
        byte[] opening = [0x14, 0x50, 0x0D, 0x00, 0x00, 0x14, 0x95, 0x05, 0x0D, 0x6D, 0x66, 0x00, 0x00, 0x6D, 0x6A, 0x00, 0x00];
        byte[] locking = [0x6D, 0x66, 0x00, 0x01, 0x6D, 0x6A, 0x00, 0x01];
        Equal(true, script.AsSpan().IndexOf(lineDeclaration) >= 0, "door1 declares LINE (38,196,0)-(44,267,0)");
        var openAt = script.AsSpan().IndexOf(opening);
        Equal(true, openAt >= 0, "and its script releases 102 and 106 together once 5[13] is 0");
        Equal(true, script.AsSpan().IndexOf(locking) >= 0, "the field's main locks the same two triangles");

        // The entity table: each entity has 32 script pointers, relative to the section start,
        // after the header, the entity names and the AKAO offsets.
        var entities = script[2];
        var akao = BitConverter.ToUInt16(script, 6);
        var table = 0x20 + entities * 8 + akao * 4;
        int Slot(int entity, int slot) => BitConverter.ToUInt16(script, table + (entity * 32 + slot) * 2);
        Equal(Slot(DoorLine, 1), Slot(DoorLine, 2), "door1's Move slot is its OK slot's own code, so walking onto the line runs it");
        Equal(openAt, Slot(DoorLine, 1), "and that code is the opening script");
        Equal(true, FieldNativeDoorOpeningLines.TryFind(Field, 102, out var row), "the catalog has the kitchen door");
        Equal("102,106", string.Join(',', row.Releases), "as the pair its script releases");
        Equal(Line, row.Line, "on its own line");
        Equal(DoorLine, row.LineEntityId, "whose live LINON state is read");
    }

    private static void OnlyTheWholeGroupOpensTheKitchen(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap)
    {
        var locks = new MutableLocks(ReturnVisitLocks);
        var planner = wrap(Planner(createWalkmeshReader, locks));
        var status = (IFieldNavigationNativeBoundaryStatus)planner;
        foreach (var talkRadius in new[] { 60, 90, 120 })
        {
            Begin(planner);
            Equal(false, planner.TryBuildRoute(Entry, Shera(talkRadius), out _), $"radius {talkRadius}: no way to Shera with the door shut");
            Equal(true, status.LastFailureWasNativeBoundary, "and it is the game's own locks");
            Begin(planner);
            Equal(false, status.WouldRouteIfBoundaryReleased(Entry, Shera(talkRadius), 102), "releasing 102 alone opens nothing");
            Begin(planner);
            Equal(false, status.WouldRouteIfBoundaryReleased(Entry, Shera(talkRadius), 106), "nor does 106 alone");
            Begin(planner);
            Equal(true, status.WouldRouteIfBoundaryGroupReleased(Entry, Shera(talkRadius), DoorLocks), "the pair together does");
        }

        var toTheLine = new FieldNavigationTarget(Field, FieldNavigationCategory.Objects, "kitchen door", 44, 267, 0,
            "door:558:9", TriggerLine: Line);
        Begin(planner);
        Equal(true, planner.TryBuildRoute(Entry, toTheLine, out _), "the opening line is reachable with both locks on");

        // A coherent observation may contain just the secondary lock. The known opening
        // script still releases it; lookup must not require its primary table key to be shut.
        var secondaryPlanner = wrap(Planner(createWalkmeshReader, new MutableLocks([106, 70, 71])));
        Begin(secondaryPlanner);
        Equal(false, secondaryPlanner.TryBuildRoute(Entry, Shera(90), out _), "the secondary kitchen lock alone still blocks Shera");
        var secondaryStatus = (IFieldNavigationNativeBoundaryStatus)secondaryPlanner;
        var secondaryLocks = secondaryStatus.LastBlockingBoundaryTriangles.ToArray();
        Equal(true, FieldNativeDoorOpeningLines.TryFindGroupForGoal(Field, secondaryLocks,
            group =>
            {
                Begin(secondaryPlanner);
                return secondaryStatus.WouldRouteIfBoundaryGroupReleased(Entry, Shera(90), group);
            }, out var secondaryDoor), "the kitchen opening is recognized when only its secondary lock remains");
        Equal(DoorLine, secondaryDoor.LineEntityId, "the same native door script releases that remaining lock");
    }

    private static IEnumerable<string> WalkToShera(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap,
        int talkRadius,
        ushort speed,
        int framesPerSample)
    {
        var name = $"talk radius {talkRadius}, speed {speed}, {framesPerSample} frames/sample";
        var locks = new MutableLocks(ReturnVisitLocks);
        var planner = wrap(Planner(createWalkmeshReader, locks));
        var shera = Shera(talkRadius);
        var controller = Controller(planner, shera, lineEnabled: true);
        var transform = new FieldNavigationControlTransform(0);
        var mesh = createWalkmeshReader(Field).Read(Entry).Walkmesh!;
        var (flat, originalIds, flatIds) = FieldBodyClearanceRouteTests.FlatPortion(mesh);
        var state = new FieldNavigationNativeMovementState(Entry.X * 4096, Entry.Y * 4096, 0, flatIds[Entry.TriangleId], 0);
        FieldPositionSnapshot Here() => new(FieldPositionReader.FieldModule, Field, 0, state.FixedX >> 12, state.FixedY >> 12, 0,
            (ushort)originalIds[state.TriangleId], state.Heading) { NativeFixedPosition = new(state.FixedX, state.FixedY, 0) };

        Begin(planner);
        var selected = Select(controller, shera, transform);
        if (selected?.Speech?.Contains("Approaching the door", StringComparison.Ordinal) != true || !controller.BeaconEnabled)
        {
            yield return $"{name}: selecting Shera did not start the walk to the door: \"{selected?.Speech}\"";
            yield break;
        }

        controller.RequestAutoWalkForHeldRoute();
        var input = FieldNavigationInput.None;
        var opened = false;
        var temporary13 = 0;
        for (var tick = 0; tick < 700; tick++)
        {
            var position = Here();
            Begin(planner);
            var spoken = controller.UpdateLiveTracking(position, new(0, input), transform, false, 80,
                observedAt: DateTime.UnixEpoch.AddMilliseconds(tick * 80))?.Speech;
            controller.TryConsumeHeldAutoWalkRequest();
            if (!opened)
            {
                if (spoken?.Contains("reached", StringComparison.Ordinal) == true || spoken?.Contains("Navigation off", StringComparison.Ordinal) == true)
                {
                    yield return $"{name}: said \"{spoken}\" before the door opened, at {position.X},{position.Y}";
                    yield break;
                }

                if (controller.HeldForNativeBoundaryLabel != shera.Label)
                {
                    yield return $"{name}: Shera stopped being the destination before the door opened";
                    yield break;
                }
            }
            else if (spoken?.Contains("reached", StringComparison.Ordinal) == true)
            {
                var distance = Math.Sqrt(Math.Pow(position.X - shera.X, 2) + Math.Pow(position.Y - shera.Y, 2));
                if (!spoken.Contains("Shera", StringComparison.Ordinal) || distance > talkRadius)
                {
                    yield return $"{name}: \"{spoken}\" at {distance:0} units from Shera, outside talking range {talkRadius}";
                }

                yield break;
            }

            if (!controller.TryResolveAutomaticInput(position, transform, 80, out input))
            {
                input = FieldNavigationInput.None;
                continue;
            }

            var (dx, dy) = FieldNavigationMovementObserver.PredictWorldDirection(input, transform);
            var heading = unchecked((byte)(int)Math.Round(Math.Atan2(dx, -dy) * 128 / Math.PI));
            for (var frame = 0; frame < framesPerSample; frame++)
            {
                var result = FieldNavigationNativeProbeMovement.Step(flat, state, heading, speed, Radius,
                    Array.Empty<FieldNavigationDynamicObstacle>(),
                    flatTriangle => locks.IsLocked(originalIds[flatTriangle]));
                state = result.State;
                if (DoorLocks.Contains(originalIds[state.TriangleId]) && !opened)
                {
                    yield return $"{name}: the party stood inside a locked triangle";
                    yield break;
                }

                // The native Move slot: strictly inside the line's radius while moving, once per load.
                if (result.Moved && temporary13 == 0 &&
                    NativeLineDistanceSquared(state.FixedX >> 12, state.FixedY >> 12) < (long)Radius * Radius)
                {
                    temporary13 = 1;
                    opened = true;
                    locks.Unlock(DoorLocks);
                }
            }
        }

        yield return opened
            ? $"{name}: the door opened but the walk never reached Shera; at {Here().X},{Here().Y} ({controller.LastNavigationDiagnostic})"
            : $"{name}: the walk never reached the door's line; at {Here().X},{Here().Y} ({controller.LastNavigationDiagnostic})";
    }

    /// <summary>A line the game has switched off, or whose state cannot be read, opens nothing: hold, walk nowhere.</summary>
    private static IEnumerable<string> ADisabledOrUnreadableLineHolds(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap)
    {
        foreach (var lineState in new bool?[] { false, null })
        {
            var planner = wrap(Planner(createWalkmeshReader, new MutableLocks(ReturnVisitLocks)));
            var controller = Controller(planner, Shera(90), lineState);
            var transform = new FieldNavigationControlTransform(0);
            Begin(planner);
            var spoken = Select(controller, Shera(90), transform)?.Speech ?? string.Empty;
            Begin(planner);
            if (controller.BeaconEnabled || controller.HeldForNativeBoundaryLabel != "Shera" ||
                !spoken.Contains("shut", StringComparison.Ordinal) ||
                controller.TryResolveAutomaticInput(Entry, transform, 80, out _))
            {
                yield return $"line {(lineState is null ? "unreadable" : "off")}: expected a silent hold, got \"{spoken}\", " +
                             $"beacon {controller.BeaconEnabled}, held '{controller.HeldForNativeBoundaryLabel}'";
            }
        }
    }

    /// <summary>A lock state that cannot be read is no lock: nothing is held, nothing is walked.</summary>
    private static IEnumerable<string> AnUnreadableLockStateIsNotALock(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap)
    {
        var unreadable = new FieldBoundaryStateReader(_ => 0x00200000, _ => 0, (_, _) => false);
        var planner = wrap(new FieldWalkmeshRoutePlanner(createWalkmeshReader(Field), unreadable,
            playerCollisionRadiusProvider: _ => Radius));
        var controller = Controller(planner, Shera(90), lineEnabled: true);
        Begin(planner);
        var spoken = Select(controller, Shera(90), new FieldNavigationControlTransform(0))?.Speech ?? string.Empty;
        if (controller.IsHoldingForNativeBoundary || controller.BeaconEnabled)
        {
            yield return $"an unreadable lock state was treated as a door to open: \"{spoken}\"";
        }
    }

    /// <summary>
    /// A destination behind another lock (triangle 70, the pocket the field also holds shut
    /// after moment 563) is not sent to the kitchen door: releasing that door would not reach it.
    /// </summary>
    private static IEnumerable<string> ADifferentLockIsNotThisDoor(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap)
    {
        var pocket = new FieldNavigationTarget(Field, FieldNavigationCategory.Objects, "the locked pocket", 14, 886, 0,
            "synthetic:558:70");
        var planner = wrap(Planner(createWalkmeshReader, new MutableLocks(ReturnVisitLocks)));
        var controller = Controller(planner, pocket, lineEnabled: true);
        Begin(planner);
        var spoken = Select(controller, pocket, new FieldNavigationControlTransform(0))?.Speech ?? string.Empty;
        if (controller.BeaconEnabled || spoken.Contains("Approaching the door", StringComparison.Ordinal))
        {
            yield return $"a destination behind a different lock was walked to the kitchen door: \"{spoken}\"";
        }
    }

    /// <summary>
    /// Two doors in one room: each destination is sent to the door whose own group opens its
    /// way, never to the other, and one both doors stand in front of is sent to neither.
    /// </summary>
    private static IEnumerable<string> TwoDoorsAreKeptApart(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap)
    {
        var planner = wrap(Planner(createWalkmeshReader, new MutableLocks(ReturnVisitLocks)));
        var status = (IFieldNavigationNativeBoundaryStatus)planner;
        var kitchen = new FieldNativeDoorOpeningLine(Field, 102, DoorLine, Line, "test", [102, 106]);
        var pocketDoor = new FieldNativeDoorOpeningLine(Field, 70, 99, new(0, 0, 0, 1, 1, 0), "test", [70, 71]);
        var rows = new[] { pocketDoor, kitchen };
        var pocket = new FieldNavigationTarget(Field, FieldNavigationCategory.Objects, "the locked pocket", 14, 886, 0, "synthetic:558:70");
        FieldNativeDoorOpeningLine? Pick(FieldNavigationTarget target)
        {
            Begin(planner);
            planner.TryBuildRoute(Entry, target, out _);
            var blocked = status.LastBlockingBoundaryTriangles.ToArray();
            return FieldNativeDoorOpeningLines.TryFindGroupForGoal(Field, blocked,
                group => { Begin(planner); return status.WouldRouteIfBoundaryGroupReleased(Entry, target, group); },
                out var opening, rows) ? opening : null;
        }

        if (Pick(Shera(90))?.LockedTriangle != 102)
        {
            yield return "Shera was not sent to the kitchen door when another door is listed first";
        }

        if (Pick(pocket)?.LockedTriangle != 70)
        {
            yield return "the pocket was not sent to its own door";
        }

        var bothRows = new[] { pocketDoor with { ReleasedTriangles = [70] }, kitchen with { ReleasedTriangles = [102] } };
        Begin(planner);
        planner.TryBuildRoute(Entry, Shera(90), out _);
        var blockedNow = status.LastBlockingBoundaryTriangles.ToArray();
        if (FieldNativeDoorOpeningLines.TryFindGroupForGoal(Field, blockedNow,
                group => { Begin(planner); return status.WouldRouteIfBoundaryGroupReleased(Entry, Shera(90), group); },
                out var wrongly, bothRows))
        {
            yield return $"with only part of each door's locks known, Shera was sent to door {wrongly.LockedTriangle}";
        }
    }

    /// <summary>B during the walk to the door ends it; the door opening afterwards starts nothing.</summary>
    private static IEnumerable<string> CancellingStopsTheWalkForGood(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<IFieldNavigationRoutePlanner, IFieldNavigationRoutePlanner> wrap)
    {
        var locks = new MutableLocks(ReturnVisitLocks);
        var planner = wrap(Planner(createWalkmeshReader, locks));
        var controller = Controller(planner, Shera(90), lineEnabled: true);
        var transform = new FieldNavigationControlTransform(0);
        Begin(planner);
        Select(controller, Shera(90), transform);
        controller.RequestAutoWalkForHeldRoute();
        controller.CancelHeldBoundaryTarget();
        locks.Unlock(DoorLocks);
        Begin(planner);
        controller.UpdateLiveTracking(Entry, new(0, FieldNavigationInput.None), transform, false, 80, observedAt: DateTime.UnixEpoch);
        Begin(planner);
        if (controller.BeaconEnabled || controller.IsHoldingForNativeBoundary ||
            controller.TryResolveAutomaticInput(Entry, transform, 80, out _))
        {
            yield return "a cancelled walk to the door came back when the door opened";
        }
    }

    private static FieldNavigationActionResult? Select(
        FieldNavigationController controller, FieldNavigationTarget target, FieldNavigationControlTransform transform)
    {
        for (var guard = 0; guard < 8 && controller.CurrentCategory != target.Category; guard++)
        {
            controller.HandleAction(FieldNavigationAction.NextCategory, Entry, transform);
        }

        return controller.HandleAction(FieldNavigationAction.ToggleBeacon, Entry, transform);
    }

    private static FieldNavigationController Controller(IFieldNavigationRoutePlanner planner, FieldNavigationTarget target, bool? lineEnabled)
    {
        var source = target.Category switch
        {
            FieldNavigationCategory.Npcs => new FieldNavigationTargetSource([], npcTargetProvider: _ => [target]),
            FieldNavigationCategory.Objects => new FieldNavigationTargetSource([], objectTargetProvider: _ => [target]),
            _ => new FieldNavigationTargetSource([target])
        };
        return new FieldNavigationController(source, planner)
        {
            NativeLineIsEnabled = (field, entity) => field == Field && entity == DoorLine ? lineEnabled : null,
        };
    }

    private static FieldWalkmeshRoutePlanner Planner(Func<int, FieldWalkmeshReader> createWalkmeshReader, MutableLocks locks) =>
        new(createWalkmeshReader(Field), locks.Reader, playerCollisionRadiusProvider: _ => Radius);

    private static void Begin(IFieldNavigationRoutePlanner planner) =>
        planner.GetType().GetMethod("BeginObservation",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public)?.Invoke(planner, null);

    // Independent of the code under test: the native line step projects the leader onto the
    // segment in 1/256 steps, rejects a foot outside the segment's extent, and counts the leader
    // as on the line only while the squared distance is strictly under the radius squared.
    private static long NativeLineDistanceSquared(int x, int y)
    {
        long abx = Line.EndX - Line.StartX, aby = Line.EndY - Line.StartY;
        var lengthSquared = abx * abx + aby * aby;
        var projection = ((x - Line.StartX) * abx + (y - Line.StartY) * aby) * 256 / lengthSquared;
        var px = Line.StartX + ((projection * abx) >> 8);
        var py = Line.StartY + ((projection * aby) >> 8);
        if (px < Math.Min(Line.StartX, Line.EndX) || px > Math.Max(Line.StartX, Line.EndX) ||
            py < Math.Min(Line.StartY, Line.EndY) || py > Math.Max(Line.StartY, Line.EndY))
        {
            return long.MaxValue;
        }

        return (x - px) * (x - px) + (y - py) * (y - py);
    }

    private sealed class MutableLocks
    {
        private const int FieldState = 0x00200000;
        private readonly HashSet<int> locked;

        public MutableLocks(IEnumerable<int> triangles)
        {
            locked = triangles.ToHashSet();
            Reader = new FieldBoundaryStateReader(
                readInt32: address => address == FieldBoundaryStateReader.AddressFieldGlobalObjectPtr ? FieldState : 0,
                readByte: ReadByte,
                isReadableMemory: (_, _) => true);
        }

        public FieldBoundaryStateReader Reader { get; }

        public bool IsLocked(int triangle) => locked.Contains(triangle);

        public void Unlock(IEnumerable<int> triangles) => locked.ExceptWith(triangles);

        private byte ReadByte(int address)
        {
            if (address == FieldPositionReader.AddressCurrentModule) return (byte)FieldPositionReader.FieldModule;
            if (address == FieldPositionReader.AddressFieldId) return (byte)(Field & 0xFF);
            if (address == FieldPositionReader.AddressFieldId + 1) return (byte)(Field >> 8);
            var offset = address - (FieldState + FieldBoundaryStateReader.BoundaryBitsOffset);
            if (offset < 0 || offset >= FieldBoundaryStateReader.BoundaryByteCount) return 0;
            byte value = 0;
            foreach (var triangle in locked.Where(triangle => triangle >> 3 == offset))
            {
                value |= (byte)(1 << (triangle & 7));
            }

            return value;
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Cid's house door: {label}: expected {expected}, got {actual}.");
        }
    }
}

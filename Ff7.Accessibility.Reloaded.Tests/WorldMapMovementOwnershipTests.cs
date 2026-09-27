using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Who owns world movement is read on its own. Both hosts used to hold automatic walking only
/// inside a successful read of every world window's visible text, so a torn window during the
/// party menu let it run even with the native control flag (DE6B5C) cleared. The party menu is
/// also held by its own session flag (DC12F0, set by FUN_006CB56A and cleared only after the
/// closing slide), and an ownership read that cannot be taken holds movement without dropping
/// the route. Menu time is not counted as a walk that failed to get closer.
/// </summary>
internal static class WorldMapMovementOwnershipTests
{
    internal static void Run()
    {
        ATornWindowDoesNotHideThatControlWasTaken();
        ThePartyMenuHoldsMovementUntilItHasClosed();
        AnUnreadableOwnershipHoldsMovement();
        OrdinaryControlWithNoWindowLetsMovementRun();
        HoldingKeepsTheRouteAndCountsNoStall();
        ResumingAfterAPauseActsLikeAFreshStart();
        ARefusedKeyIsForgottenWhenTheCameraTurnsIt();
        ThePaceLineKeepsTheNativeMaskApartFromTheMotionEstimate();
        Console.WriteLine("PASS world movement ownership: control, party menu, torn windows and held routes.");
    }

    private static void ATornWindowDoesNotHideThatControlWasTaken()
    {
        var memory = WorldMemory(control: 0, session: 0, phase: -1);
        // A window whose owner byte says free while its state says open: TryRead refuses it.
        memory.WriteUInt16(WorldMapDialogueReader.WindowStateAddress, 6);
        memory.WriteByte((uint)FieldMessageReader.AddressFieldWindowStates, FieldMessageReader.FreeWindowState);
        var reader = new WorldMapDialogueReader(memory);
        Equal(false, reader.TryRead(out _), "the torn window is not read");
        Equal(true, reader.TryReadMovementOwnership(out var ownership), "ownership is still read");
        Equal(true, WorldMapMovementGate.ShouldHold(true, ownership, false, null),
            "with control taken, automatic walking is held although the window could not be read");
    }

    private static void ThePartyMenuHoldsMovementUntilItHasClosed()
    {
        foreach (var phase in new[] { 0, 1, 2 })
        {
            var reader = new WorldMapDialogueReader(WorldMemory(control: 1, session: 1, phase: phase));
            Equal(true, reader.TryReadMovementOwnership(out var ownership), $"phase {phase}: read");
            Equal(true, ownership.MainMenuSession, $"phase {phase}: the session is open");
            Equal(true, WorldMapMovementGate.ShouldHold(true, ownership, true, new WorldMapDialogueSnapshot(true, [])),
                $"phase {phase}: the party menu holds movement, closing included");
        }

        var closed = new WorldMapDialogueReader(WorldMemory(control: 1, session: 0, phase: -1));
        Equal(true, closed.TryReadMovementOwnership(out var after), "closed: read");
        Equal(false, WorldMapMovementGate.ShouldHold(true, after, true, new WorldMapDialogueSnapshot(true, [])),
            "once the session flag is cleared movement is free again");
    }

    private static void AnUnreadableOwnershipHoldsMovement()
    {
        var notWorld = WorldMemory(control: 1, session: 0, phase: -1);
        notWorld.WriteByte((uint)WorldMapStateReader.AddressCurrentModule, 1);
        Equal(false, new WorldMapDialogueReader(notWorld).TryReadMovementOwnership(out _), "not the world module");
        var missing = new FakeMemory();
        missing.WriteByte((uint)WorldMapStateReader.AddressCurrentModule, (byte)WorldMapStateReader.WorldModule);
        Equal(false, new WorldMapDialogueReader(missing).TryReadMovementOwnership(out _), "an unreadable control flag");
        Equal(true, WorldMapMovementGate.ShouldHold(false, default, true, new WorldMapDialogueSnapshot(true, [])),
            "an ownership read that failed holds automatic walking");
        Equal(false, WorldMapMovementGate.ShouldPauseSample(false, default, true, new WorldMapDialogueSnapshot(true, [])),
            "but does not pause the rest of the sample, whose own failed-read handling carries on");
    }

    private static void OrdinaryControlWithNoWindowLetsMovementRun()
    {
        var reader = new WorldMapDialogueReader(WorldMemory(control: 1, session: 0, phase: -1));
        Equal(true, reader.TryReadMovementOwnership(out var ownership), "read");
        Equal(true, reader.TryRead(out var dialogue), "no window up reads cleanly");
        Equal(false, WorldMapMovementGate.ShouldHold(true, ownership, true, dialogue), "the party walks");
        Equal(true, WorldMapMovementGate.ShouldHold(true, ownership, true,
                new WorldMapDialogueSnapshot(true, [new WorldMapDialogueWindow(0, 6, 1, "text")])),
            "an open world window still holds it");
    }

    /// <summary>
    /// The hold is PauseForNativeControl plus observing with automatic walking inactive: the
    /// destination stays, and ten seconds of menu cannot fail-stop the walk on its return.
    /// </summary>
    private static void HoldingKeepsTheRouteAndCountsNoStall()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") ??
            @"C:\Games\Final Fantasy VII\workingdir";
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT") ??
            @"C:\FF7A11Y\accessibility_prototype";
        var map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "wm0.map"), 0, 0);
        var catalog = WorldMapTargetCatalog.Load(map,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        var target = catalog.Locations.Single(candidate => candidate.Label == "Old Man's House (Mythril)");
        var controller = new WorldMapNavigationController(map, new WorldMapRoutePlanner(map), (_, _) => [target]);
        // 20:47:30 in the log: the party stood at 202069,131317 while the menu was up.
        var state = new WorldMapStateSnapshot(3, 0, 0, 415, 202069, 2191, 131317, 0, 0, 16, 2, 0, 30, 3952,
            new FieldNavigationControlTransform(-(3952 / 16) + 256));
        var now = new DateTime(2026, 9, 26, 20, 47, 29, DateTimeKind.Utc);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, state, now);
        controller.Observe(state, now.AddMilliseconds(100), automaticWalkActive: true);
        for (var sample = 1; sample <= 100; sample++)
        {
            controller.PauseForNativeControl();
            var held = controller.Observe(state, now.AddMilliseconds(100 + sample * 200), automaticWalkActive: false);
            Equal(false, held?.StopAutoWalk == true, $"sample {sample}: no stall is counted while held");
        }

        Equal(true, controller.BeaconEnabled, "the destination is kept through the hold");
        var resumed = controller.Observe(state, now.AddSeconds(21), automaticWalkActive: true);
        Equal(false, resumed?.StopAutoWalk == true, "returning from the menu does not fail-stop at once");
    }

    private static readonly (int X, int Y, int Z, int Camera)[] HouseStarts =
        [(204063, 1821, 134761, 3584), (199141, 1750, 133518, 3584), (199507, 1772, 133712, 2848)];

    private static (WorldMapData Map, WorldMapNavigationTarget Target) LoadHouse()
    {
        var dataRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT") ??
            @"C:\Games\Final Fantasy VII\workingdir";
        var sourceRoot = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT") ??
            @"C:\FF7A11Yccessibility_prototype";
        var map = WorldMapDataLoader.Load(Path.Combine(dataRoot, "data", "wm", "wm0.map"), 0, 0);
        var catalog = WorldMapTargetCatalog.Load(map,
            Path.Combine(sourceRoot, "external", "kujata", "field-id-to-world-map-coords.json"),
            Path.Combine(sourceRoot, "external", "kujata", "wm-field-menu-names.txt"),
            Path.Combine(sourceRoot, "Ff7.Accessibility.Reloaded", "Assets", "world", "world-map-location-triggers.json"));
        return (map, catalog.Locations.Single(candidate => candidate.Label == "Old Man's House (Mythril)"));
    }

    private static WorldMapStateSnapshot HouseState(int x, int y, int z, int camera)
    {
        var direction = -(((camera % 4096) + 4096) % 4096 / 16);
        if (direction < -128)
        {
            direction += 256;
        }

        return new WorldMapStateSnapshot(3, 0, 0, 415, x, y, z, 0, 0, 0, 2, 0, 30, camera,
            new FieldNavigationControlTransform(direction));
    }

    private static WorldMapNavigationController Started(WorldMapData map, WorldMapNavigationTarget target, WorldMapStateSnapshot state)
    {
        var controller = new WorldMapNavigationController(map, new WorldMapRoutePlanner(map), (_, _) => [target]);
        controller.HandleAction(FieldNavigationAction.ToggleBeacon, state, DateTime.UnixEpoch);
        return controller;
    }

    /// <summary>
    /// Root's pause probe, promoted. Held still by a menu or a world script, the party does not
    /// move while keys are released; the first automatic sample after that pause must not read
    /// the released keys as walls. After two to eight unmoved samples and a pause, the first
    /// resumed key is the key a fresh start at the same spot and route would press. (Before
    /// this, five stale samples made it press nothing at all at each of these starts.)
    /// </summary>
    private static void ResumingAfterAPauseActsLikeAFreshStart()
    {
        var (map, target) = LoadHouse();
        foreach (var (x, y, z, camera) in HouseStarts)
        {
            var state = HouseState(x, y, z, camera);
            Started(map, target, state).TryResolveAutomaticInput(state, out var fresh);
            for (var stale = 2; stale <= 8; stale++)
            {
                var controller = Started(map, target, state);
                for (var sample = 0; sample < stale; sample++)
                {
                    controller.TryResolveAutomaticInput(state, out _);
                }

                controller.PauseForNativeControl();
                Equal(true, controller.BeaconEnabled, $"{x},{z}: the destination survives the pause");
                controller.TryResolveAutomaticInput(state, out var resumed);
                Equal(fresh, resumed, $"{x},{z} after {stale} unmoved samples and a pause: the resumed key");
            }
        }
    }

    /// <summary>
    /// A refused key is a refused world direction, not a raw key. At each logged house start,
    /// sampling without moving until one key has been held twice confirms it as refused, and
    /// the controller turns to another key (pushing world direction W2). If the camera turns a
    /// quarter, half or three quarters instead, the raw keys all point elsewhere; the answer
    /// must still push world direction W2, never the refused world direction - which a ban on
    /// the raw key would get wrong both ways.
    /// </summary>
    private static void ARefusedKeyIsForgottenWhenTheCameraTurnsIt()
    {
        var (map, target) = LoadHouse();
        var compared = 0;
        foreach (var (x, y, z, camera) in HouseStarts)
        {
            var state = HouseState(x, y, z, camera);
            (WorldMapNavigationController Controller, FieldNavigationInput Refused)? Prime()
            {
                var controller = Started(map, target, state);
                var previous = FieldNavigationInput.None;
                for (var sample = 0; sample < 12; sample++)
                {
                    controller.TryResolveAutomaticInput(state, out var key);
                    if (key != FieldNavigationInput.None && key == previous)
                    {
                        return (controller, key);
                    }

                    previous = key;
                }

                return null;
            }

            if (Prime() is not { } unturned)
            {
                continue;
            }

            unturned.Controller.TryResolveAutomaticInput(state, out var second);
            Equal(false, second == unturned.Refused, $"{x},{z}: {unturned.Refused} held twice without moving is not pressed again");
            if (second == FieldNavigationInput.None)
            {
                continue;
            }

            var refusedWay = Heading(unturned.Refused, camera);
            var secondWay = Heading(second, camera);
            foreach (var turn in new[] { 1024, 2048, 3072 })
            {
                var primed = Prime()!.Value.Controller;
                var turned = HouseState(x, y, z, camera + turn);
                primed.TryResolveAutomaticInput(turned, out var answer);
                Equal(false, answer == FieldNavigationInput.None, $"{x},{z}, camera turned {turn}: a key is still offered");
                var way = Heading(answer, camera + turn);
                Equal(true, Turn(way, secondWay) <= 64,
                    $"{x},{z}, camera turned {turn}: {answer} pushes world {way}, expected the same way as {second} ({secondWay})");
                Equal(true, Turn(way, refusedWay) > 256,
                    $"{x},{z}, camera turned {turn}: {answer} must not push the refused way {refusedWay}");
                compared++;
            }
        }

        Equal(true, compared >= 6, $"camera-turn cases were actually compared ({compared})");
    }

    private static int Turn(int a, int b) => Math.Abs((((a - b) % 4096) + 6144) % 4096 - 2048);

    /// <summary>
    /// The pace line gives the engine's own world input mask (DAT_009A85D4) apart from the
    /// direction estimated from displacement, reports an unread mask as unknown, and writes a
    /// new line when only the native mask changes - which is how a key the engine did not
    /// receive shows up.
    /// </summary>
    private static void ThePaceLineKeepsTheNativeMaskApartFromTheMotionEstimate()
    {
        var memory = WorldMemory(control: 1, session: 0, phase: -1);
        var reader = new WorldMapDialogueReader(memory);
        Equal(false, reader.TryReadNativeWorldInput(out _), "an unwritten input mask is unknown");
        memory.WriteUInt32(WorldMapDialogueReader.NativeWorldInputAddress, 0x2000);
        Equal(true, reader.TryReadNativeWorldInput(out var mask), "the input mask is read");
        Equal(0x2000u, mask, "the Right bit");

        var (map, target) = LoadHouse();
        var (x, y, z, camera) = HouseStarts[1];
        var state = HouseState(x, y, z, camera);
        var controller = Started(map, target, state);
        var first = controller.DescribeAutomaticPace(state, true, FieldNavigationInput.Left, null) ?? string.Empty;
        Equal(true, first.Contains("native=unknown", StringComparison.Ordinal) &&
                    first.Contains("motionEstimate=none", StringComparison.Ordinal) &&
                    !first.Contains("observed", StringComparison.Ordinal),
            $"an unread mask is unknown and the estimate is labelled as one: {first}");
        var withMask = controller.DescribeAutomaticPace(state, true, FieldNavigationInput.Left, 0x8000u) ?? string.Empty;
        Equal(true, withMask.Contains("native=Left(0x8000)", StringComparison.Ordinal), $"the native Left bit is named: {withMask}");
        Equal(null, controller.DescribeAutomaticPace(state, true, FieldNavigationInput.Left, 0x8000u), "nothing changed, no line");
        var mismatch = controller.DescribeAutomaticPace(state, true, FieldNavigationInput.Left, 0x2000u) ?? string.Empty;
        Equal(true, mismatch.Contains("commanded=Left", StringComparison.Ordinal) &&
                    mismatch.Contains("native=Right(0x2000)", StringComparison.Ordinal),
            $"a native mask that disagrees with the command writes its own line: {mismatch}");
    }

    private static int Heading(FieldNavigationInput key, int camera)
    {
        var x = key is FieldNavigationInput.Left or FieldNavigationInput.UpLeft or FieldNavigationInput.DownLeft ? -1d :
            key is FieldNavigationInput.Right or FieldNavigationInput.UpRight or FieldNavigationInput.DownRight ? 1d : 0d;
        var z = key is FieldNavigationInput.Up or FieldNavigationInput.UpLeft or FieldNavigationInput.UpRight ? -1d :
            key is FieldNavigationInput.Down or FieldNavigationInput.DownLeft or FieldNavigationInput.DownRight ? 1d : 0d;
        var angle = camera * Math.PI * 2d / 4096d;
        var worldX = x * Math.Cos(angle) - z * Math.Sin(angle);
        var worldZ = x * Math.Sin(angle) + z * Math.Cos(angle);
        return ((int)Math.Round(Math.Atan2(worldX, -worldZ) * 4096d / (2d * Math.PI)) % 4096 + 4096) % 4096;
    }

    private static FakeMemory WorldMemory(int control, int session, int phase)
    {
        var memory = new FakeMemory();
        memory.WriteByte((uint)WorldMapStateReader.AddressCurrentModule, (byte)WorldMapStateReader.WorldModule);
        memory.WriteInt32(WorldMapDialogueReader.ControlAddress, control);
        memory.WriteInt32((uint)MenuGilStateReader.AddressMainMenuSession, session);
        memory.WriteInt32((uint)MenuGilStateReader.AddressMainMenuPhase, phase);
        for (var window = 0; window < 4; window++)
        {
            memory.WriteUInt16(WorldMapDialogueReader.WindowStateAddress + (uint)(window * 0x30), 0);
            memory.WriteByte((uint)FieldMessageReader.AddressFieldWindowStates + (uint)window, 0);
            memory.WriteUInt32(WorldMapDialogueReader.TextPointerAddress + (uint)(window * 4), 0);
        }

        return memory;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private sealed class FakeMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(virtualAddress + (uint)index, out var value))
                {
                    return false;
                }

                destination[index] = value;
            }

            return true;
        }

        public void WriteByte(uint address, byte value) => bytes[address] = value;

        public void WriteUInt16(uint address, ushort value) => Write(address, BitConverter.GetBytes(value));

        public void WriteInt32(uint address, int value) => Write(address, BitConverter.GetBytes(value));

        public void WriteUInt32(uint address, uint value) => Write(address, BitConverter.GetBytes(value));

        private void Write(uint address, byte[] value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                bytes[address + (uint)index] = value[index];
            }
        }
    }
}

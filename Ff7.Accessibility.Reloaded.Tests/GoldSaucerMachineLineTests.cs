using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The G Bike machines (games_2 bikeg 19, LINE 272,195-210,154; bikeg2 20, LINE 121,171-177,119;
/// both LineOk). 2026-09-29: "G Bike reached. Interact here." at 12:24:13, :31, :38 and :45 and
/// "G Bike, second machine reached" at :49 and :59, yet OK opened nothing until 12:25:14.
///
/// <para>Touch is not the whole native rule. 00637ABB sets the line's byte 0x15 only while the
/// leader faces within 64 units of the direction to its foot on the line (00636515), and
/// 00637D35 runs the OK script only when, in addition, the facing is within 32 units of that
/// direction and OK is newly pressed. The logged OK at 12:25:05, touching bikeg2 and facing 192
/// where the line lay at 100, did nothing; the one at 12:25:14, facing 128 where it lay at 154,
/// opened the machine's card (dialog 24).</para>
/// </summary>
internal static class GoldSaucerMachineLineTests
{
    private const int Field = 507;
    private const int Radius = 34;
    private static readonly FieldNavigationTriggerLine BikeG = new(272, 195, 0, 210, 154, 0);
    private static readonly FieldNavigationTriggerLine BikeG2 = new(121, 171, 0, 177, 119, 0);

    public static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        TheLoggedOkPressesFollowTheNativeFacingRule();
        AutoWalkTurnsToFaceTheLineBeforeSayingReached(createWalkmeshReader);
        TheTurnSettlesAtEveryStepSize(createWalkmeshReader);
        AGoLineIsStillReachedOnTheTouch(createWalkmeshReader);
        TheAngleIsTheNativeTableNotAtan2();
        StandingOnTheFootIsNotProvenWithoutTheNativeState();
        TheNativeLineStateDecidesOverTheRenderedDirection(createWalkmeshReader);
        TheReaderReadsTheNativeOkStateAndEventHeading();
        FirstVisitMachineRowsUseTheirNativeHandler();
        FirstVisitStoryTargetsCarryTheOkState(createWalkmeshReader);
        AnUnknownNativeVerdictIsNeverReached(createWalkmeshReader);
        StartingOnTheLineUsesTheNativeVerdictBeforeMovement(createWalkmeshReader);
        Console.WriteLine("Gold Saucer machine line tests passed.");
    }

    private static void TheLoggedOkPressesFollowTheNativeFacingRule()
    {
        Equal(false, FieldNativeLineContact.AcceptsOk(BikeG, At(217, 149, 88), Radius),
            "12:24:13 'G Bike reached': touching, but facing 88 with the line at 156");
        Equal(true, FieldNativeLineContact.Touches(BikeG, At(217, 149, 88), Radius), "and it was touching");
        Equal(false, FieldNativeLineContact.AcceptsOk(BikeG2, At(163, 120, 192), Radius),
            "12:25:05 OK on the second machine did nothing: facing 192 with the line at 100");
        Equal(true, FieldNativeLineContact.AcceptsOk(BikeG, At(280, 182, 128), Radius),
            "12:25:14 OK opened the G Bike card: facing 128 with the line at 154");

        // The 00637D35 window, (angle - facing + 0x20) & 0xFF < 0x40: facing 31 below to 32 above.
        Equal(true, FieldNativeLineContact.TryFoot(BikeG, 280, 182, -10, out var footX, out var footY), "the foot is on the segment");
        var angle = FieldNativeLineContact.NativeAngle(footX - 280, footY - 182);
        foreach (var (offset, accepted) in new[] { (32, true), (33, false), (-31, true), (-32, false), (0, true) })
        {
            Equal(accepted, FieldNativeLineContact.AcceptsOk(BikeG, At(280, 182, (angle + offset) & 255), Radius),
                $"facing the line's direction {offset:+0;-0} units");
        }

        Equal(false, FieldNativeLineContact.AcceptsOk(BikeG, At(288, 162, 153), Radius), "facing it from outside the radius is no touch");
        Equal(false, FieldNativeLineContact.AcceptsOk(BikeG, At(283, 184, 200), Radius), "past the end of the segment is no touch");
    }

    /// <summary>
    /// Auto walk arrived at the logged 12:24:13 spot still facing its approach (88). It must not
    /// call that reached; it turns the leader toward the line - one press toward it, as a player
    /// does - and only then says "reached", where OK works. Nothing is pressed for the player.
    /// </summary>
    private static void AutoWalkTurnsToFaceTheLineBeforeSayingReached(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var reader = createWalkmeshReader(Field);
        var mesh = reader.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var planner = new FieldWalkmeshRoutePlanner(reader, playerCollisionRadiusProvider: _ => Radius);
        var target = new FieldNavigationTarget(Field, FieldNavigationCategory.Objects, "G Bike", 241, 174, 0,
            StableId: "object:507:19", TriggerLine: BikeG, TriggerEntityId: 19, LineActivationRadius: Radius, LineActivatesOnOk: true);
        var controller = new FieldNavigationController(
            new FieldNavigationTargetSource([], objectTargetProvider: position => [WithNativeVerdict(target, position)]), planner);
        var transform = new FieldNavigationControlTransform(-128);
        FieldPositionSnapshot Walked(int x, int y, byte direction)
        {
            var triangle = FieldWalkmeshPathfinder.ResolveTriangle(mesh, x, y, -10, -1);
            return new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, 0, x, y, -10, (ushort)Math.Max(0, triangle), direction);
        }

        // The logged approach, 12:24:13: (190,131) (203,140) (213,147) (217,149), all facing 88.
        var start = Walked(190, 131, 88);
        for (var step = 0; step < 8 && controller.CurrentCategory != FieldNavigationCategory.Objects; step++)
        {
            _ = controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
        }

        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
        Equal(true, controller.BeaconEnabled, "navigation to the G Bike started");
        controller.NoteAutoWalkStarted();
        var clock = new DateTime(2026, 9, 29, 12, 24, 13, DateTimeKind.Utc);
        var tick = 0;
        string? Update(FieldPositionSnapshot position) => controller.UpdateLiveTracking(position,
            new FieldNavigationInputSnapshot(0, FieldNavigationInput.None), transform, false, 80,
            observedAt: clock.AddMilliseconds(50 * tick++))?.Speech;

        foreach (var (x, y) in new[] { (190, 131), (203, 140), (213, 147) })
        {
            var position = Walked(x, y, 88);
            _ = Update(position);
            _ = controller.TryResolveAutomaticInput(position, transform, 80, out _);
        }

        var logged = Walked(217, 149, 88);
        var spoken = Update(logged) ?? string.Empty;
        Equal(false, spoken.Contains("reached", StringComparison.Ordinal),
            $"touching but facing away is not reached; said \"{spoken}\"");
        Equal(true, controller.TryResolveAutomaticInput(logged, transform, 80, out _), "auto walk still has a move to make");
        Console.WriteLine($"Gold Saucer machine line: at the logged 217,149 facing 88, {controller.LastNavigationDiagnostic.Split(", ").Last()}");
    }

    /// <summary>
    /// The turn, closed loop: from the logged spot the leader moves each sample by the step the
    /// input gives (walking 4, running 8, and the 12 and 16 a slow sample can show) and faces
    /// that way, as the native step does. Within a few samples it must stand on the line facing
    /// it - where OK works - and only then hear "reached"; it never leaves the line or crosses to
    /// face away for good.
    /// </summary>
    private static void TheTurnSettlesAtEveryStepSize(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        foreach (var stepSize in new[] { 4d, 8d, 12d, 16d })
        {
            var (controller, transform, walked, update) = Approach(createWalkmeshReader);
            double x = 217, y = 149;
            var position = walked(217, 149, 88);
            var trail = new List<string>();
            var reached = false;
            for (var sample = 0; sample < 10 && !reached; sample++)
            {
                var spoken = update(position) ?? string.Empty;
                trail.Add($"{position.X},{position.Y}@{position.Direction}");
                if (spoken.Contains("G Bike reached", StringComparison.Ordinal))
                {
                    Equal(true, FieldNativeLineContact.AcceptsOk(BikeG, position, Radius),
                        $"step {stepSize}: 'reached' only where OK works ({string.Join(" ", trail)})");
                    reached = true;
                    break;
                }

                Equal(true, FieldNativeLineContact.Touches(BikeG, position, Radius), $"step {stepSize}: the turn keeps the leader on the line ({string.Join(" ", trail)})");
                Equal(true, controller.TryResolveAutomaticInput(position, transform, 80, out var press), $"step {stepSize}: a press is made");
                var (worldX, worldY) = FieldNavigationMovementObserver.PredictWorldDirection(press, transform);
                var length = Math.Sqrt(worldX * worldX + worldY * worldY);
                x += worldX / length * stepSize;
                y += worldY / length * stepSize;
                var heading = (byte)((int)Math.Round(Math.Atan2(worldX, -worldY) * 128d / Math.PI) & 255);
                position = walked((int)Math.Round(x), (int)Math.Round(y), heading);
            }

            Equal(true, reached, $"step {stepSize}: the turn settles facing the line ({string.Join(" ", trail)})");
            Console.WriteLine($"Gold Saucer machine line: step {stepSize} turn {string.Join(" -> ", trail)}");
        }
    }

    /// <summary>
    /// A Go line runs on the touch alone (00637ABB), whatever the facing: the 3D Battler (kakul1,
    /// LINE -152,-121,32 to -181,-168,32) keeps its touch arrival, with no turn.
    /// </summary>
    private static void AGoLineIsStillReachedOnTheTouch(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var battler = new FieldNavigationTriggerLine(-152, -121, 32, -181, -168, 32);
        var reader = createWalkmeshReader(Field);
        var mesh = reader.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var target = new FieldNavigationTarget(Field, FieldNavigationCategory.Objects, "3D Battler", -166, -144, 32,
            StableId: "object:507:10", TriggerLine: battler, LineActivationRadius: Radius, LineOkAccepted: false);
        var controller = new FieldNavigationController(new FieldNavigationTargetSource([], objectTargetProvider: _ => [target]),
            new FieldWalkmeshRoutePlanner(reader, playerCollisionRadiusProvider: _ => Radius));
        var transform = new FieldNavigationControlTransform(-128);
        // Beside the line, facing along it (direction 0) rather than at it.
        var triangle = FieldWalkmeshPathfinder.ResolveTriangle(mesh, -150, -150, 32, -1);
        var beside = new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, 0, -150, -150, 32, (ushort)Math.Max(0, triangle), 0);
        Equal(true, FieldNativeLineContact.Touches(battler, beside, Radius), "the spot touches the 3D Battler line");
        Equal(false, FieldNativeLineContact.AcceptsOk(battler, beside, Radius), "facing along it, not at it");
        for (var step = 0; step < 8 && controller.CurrentCategory != FieldNavigationCategory.Objects; step++)
        {
            _ = controller.HandleAction(FieldNavigationAction.NextCategory, beside, transform);
        }

        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, beside, transform);
        controller.NoteAutoWalkStarted();
        _ = controller.TryResolveAutomaticInput(beside, transform, 80, out _);
        var spoken = controller.UpdateLiveTracking(beside, new FieldNavigationInputSnapshot(0, FieldNavigationInput.None), transform, false, 80,
            observedAt: new DateTime(2026, 9, 29, 12, 30, 0, DateTimeKind.Utc))?.Speech ?? string.Empty;
        Equal(true, spoken.Contains("3D Battler reached", StringComparison.Ordinal), $"a Go line is reached on the touch; said \"{spoken}\"");
    }

    /// <summary>
    /// 00636515 takes its angle from the installed table at 009067C0, not from atan2. Root's
    /// oracle (root-native-line-oracle.py): at (217,149) the foot is (212,155) and the native angle
    /// 160, where atan2 gives 156; a native heading of 127 is outside the OK sector for 160
    /// (127 - 160 = -33) though inside it for 156.
    /// </summary>
    private static void TheAngleIsTheNativeTableNotAtan2()
    {
        Equal(true, FieldNativeLineContact.TryFoot(BikeG, 217, 149, -10, out var footX, out var footY), "the foot is on the segment");
        Equal((212, 155), (footX, footY), "the native nearest foot (00637879)");
        Equal(160, FieldNativeLineContact.NativeAngle(footX - 217, footY - 149), "the native angle (00636515 with the installed table)");
        Equal(false, FieldNativeLineContact.AcceptsOk(BikeG, At(217, 149, 127), Radius), "heading 127 is outside the native OK sector");
        Equal(true, FieldNativeLineContact.AcceptsOk(BikeG, At(217, 149, 129), Radius), "129 is inside it (160 - 31)");
        Equal(true, FieldNativeLineContact.AcceptsOk(BikeG, At(217, 149, 192), Radius), "192 is inside it (160 + 32)");
        Equal(false, FieldNativeLineContact.AcceptsOk(BikeG, At(217, 149, 193), Radius), "193 is not");
        foreach (var (dx, dy, expected) in new[] { (1, 0, 64), (0, 1, 128), (-1, 0, 192), (0, -1, 0), (5, 5, 96) })
        {
            Equal(expected, FieldNativeLineContact.NativeAngle(dx, dy), $"the native angle of ({dx},{dy})");
        }
    }

    /// <summary>
    /// On the foot itself 00637ABB sets the ready byte but keeps the angle stored from an earlier
    /// frame, and 00637D35 still tests it. Without that stored angle nothing is proven, so the
    /// computed rule does not claim it.
    /// </summary>
    private static void StandingOnTheFootIsNotProvenWithoutTheNativeState()
    {
        // The line's integer points are its ends (62 by 41 has no common factor); (210,154) is its
        // own foot in 00637879's arithmetic.
        const int footX = 210, footY = 154;
        Equal(true, FieldNativeLineContact.TryFoot(BikeG, footX, footY, 0, out var againX, out var againY) &&
                    (againX, againY) == (footX, footY), "the end is its own foot");
        foreach (var heading in new[] { 0, 64, 128, 156, 192 })
        {
            Equal(false, FieldNativeLineContact.AcceptsOk(BikeG, At(footX, footY, heading) with { Z = 0 }, Radius),
                $"on the foot, heading {heading}: the stored angle is unknown, so OK is not claimed");
        }
    }

    /// <summary>
    /// The ready byte (+0x15), the stored angle (+0x14) and the event heading (+0x36) are what
    /// 00637D35 tests; the direction in the position snapshot is the rendered +0x38. When the
    /// reader has the native verdict, arrival follows it even where the rendered direction says
    /// otherwise, in both directions.
    /// </summary>
    private static void TheNativeLineStateDecidesOverTheRenderedDirection(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        foreach (var (nativeAccepts, renderedDirection) in new[] { (false, (byte)160), (true, (byte)88) })
        {
            var verdict = false;
            var (controller, transform, walked, update) = Approach(createWalkmeshReader,
                target => target with { LineOkAccepted = verdict });
            verdict = nativeAccepts;
            var spoken = update(walked(217, 149, renderedDirection)) ?? string.Empty;
            Equal(nativeAccepts, spoken.Contains("G Bike reached", StringComparison.Ordinal),
                $"native OK state {nativeAccepts} with rendered direction {renderedDirection}: said \"{spoken}\"");
        }
    }

    private static void TheReaderReadsTheNativeOkStateAndEventHeading()
    {
        const int events = 0x02500000;
        var bytes = new Dictionary<int, byte>
        {
            [FieldPositionReader.AddressFieldNumModels] = 1,
            [events + FieldNavigationNpcReader.CollisionRadiusOffset] = Radius,
            [FieldNavigationObjectReader.AddressFieldBankBase] = 0xF4,
            [FieldNavigationObjectReader.AddressFieldBankBase + 1] = 0x01
        };
        FieldNavigationLineOkState? state = new(160, true);
        var definition = FieldNavigationObjectCatalog.CreateAllFields().Single(d => d.FieldId == Field && d.EntityId == 19);
        var reader = new FieldNavigationObjectReader(
            address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events : bytes.GetValueOrDefault(address) | bytes.GetValueOrDefault(address + 1) << 8,
            address => bytes.GetValueOrDefault(address),
            _ => null, _ => null, [definition], _ => true,
            readLiveLine: _ => BikeG,
            readLineOkState: _ => state);
        bool? Accepted(byte eventHeading)
        {
            bytes[events + FieldNavigationObjectReader.EventHeadingOffset] = eventHeading;
            // The rendered direction in the snapshot is deliberately a different value.
            return reader.ReadTargets(new FieldPositionSnapshot(1, Field, 0, 217, 149, -10, 92, (byte)(eventHeading + 64)))
                .Single().LineOkAccepted;
        }

        Equal<bool?>(false, Accepted(127), "ready, angle 160, event heading 127: outside the sector");
        Equal<bool?>(true, Accepted(150), "event heading 150: inside");
        state = new FieldNavigationLineOkState(160, false);
        Equal<bool?>(false, Accepted(150), "not ready: no OK whatever the heading");
        state = null;
        Equal<bool?>(null, Accepted(150), "an unreadable LINE state is unknown, not refused");
    }
    /// <summary>
    /// The first-visit Story rows for the same cabinets (moments 440..444) were crossing rows:
    /// completesOnArrival with a trigger line, which the controller treats as "cross the line",
    /// while the native handlers are OK slots (udel, ufo1, ufo2, mogu, bikeg, la) or Go slots
    /// (bsl, kakul1, kakul2) that run on the touch. Each is now reached the way its own handler
    /// runs: on the touch within the leader's radius, and for OK also facing the line.
    /// </summary>
    private static void FirstVisitMachineRowsUseTheirNativeHandler()
    {
        var rows = FieldStoryEventCatalog.CreateAllFields()
            .Where(r => r.FieldId is 506 or 507 && r.MinimumGameMoment == 440 && r.TriggerLine is not null)
            .ToDictionary(r => (r.FieldId, r.EntityId));
        foreach (var (field, entity, okSlot) in new[]
                 {
                     (506, 11, true), (506, 14, true), (506, 15, true), (506, 16, false),
                     (507, 10, false), (507, 11, false), (507, 18, true), (507, 19, true), (507, 24, true)
                 })
        {
            var row = rows[(field, entity)];
            Equal((okSlot ? "[OK]" : "Go", false, true, okSlot),
                (row.SourceScriptType, row.CompletesOnArrival, row.UsesPlayerCollisionRadius, row.ActivatesOnOk),
                $"{field}/{entity} {row.Label}: reached as its native handler runs");
            Equal((440, 444), (row.MinimumGameMoment, row.MaximumGameMoment), $"{row.Label} keeps its first-visit window");
        }
    }

    private static void FirstVisitStoryTargetsCarryTheOkState(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        const int events = 0x02500000;
        var bytes = new Dictionary<int, byte>
        {
            [FieldPositionReader.AddressFieldNumModels] = 1,
            [events + FieldNavigationNpcReader.CollisionRadiusOffset] = Radius,
            [FieldNavigationObjectReader.AddressFieldBankBase] = 442 & 0xFF,
            [FieldNavigationObjectReader.AddressFieldBankBase + 1] = 442 >> 8,
            [events + FieldNavigationObjectReader.EventHeadingOffset] = 127
        };
        var reader = new FieldStoryTargetReader(
            address => address == FieldNavigationObjectReader.AddressFieldEventDataPtr ? events : bytes.GetValueOrDefault(address) | bytes.GetValueOrDefault(address + 1) << 8,
            address => (short)(bytes.GetValueOrDefault(address) | bytes.GetValueOrDefault(address + 1) << 8),
            address => bytes.GetValueOrDefault(address),
            FieldStoryEventCatalog.CreateAllFields(),
            _ => true,
            readLineOkState: entity => entity == 19 ? new FieldNavigationLineOkState(160, true) : null);
        var position = new FieldPositionSnapshot(1, Field, 0, 217, 149, -10, 92, 160);
        var targets = reader.ReadTargets(position);
        var bike = targets.Single(t => t.Label == "Play G Bike (optional)");
        Equal((Radius, true, (bool?)false), (bike.LineActivationRadius, bike.LineActivatesOnOk, bike.LineOkAccepted),
            "first visit: the G Bike row is a touch line that runs on OK, with the engine's own verdict (event heading 127)");
        var battler = targets.Single(t => t.Label == "Play 3D Battler (optional)");
        Equal((Radius, false, (bool?)null), (battler.LineActivationRadius, battler.LineActivatesOnOk, battler.LineOkAccepted),
            "the 3D Battler row runs on Go: the touch alone");

        // Through the controller: the first-visit G Bike is not reached while the engine says OK
        // would not work, whatever the rendered direction; it is once the engine says it would.
        foreach (var (accepted, rendered) in new[] { (false, (byte)160), (true, (byte)88) })
        {
            var verdict = false;
            var (controller, _, walked, update) = Approach(createWalkmeshReader,
                _ => bike with { LineOkAccepted = verdict }, FieldNavigationCategory.Story);
            verdict = accepted;
            var spoken = update(walked(217, 149, rendered)) ?? string.Empty;
            Equal(accepted, spoken.Contains("reached", StringComparison.Ordinal),
                $"first-visit Story G Bike, native OK {accepted}: said \"{spoken}\"");
        }
    }
    private static (FieldNavigationController Controller, FieldNavigationControlTransform Transform,
        Func<int, int, byte, FieldPositionSnapshot> Walked, Func<FieldPositionSnapshot, string?> Update) Approach(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        Func<FieldNavigationTarget, FieldNavigationTarget>? adjust = null,
        FieldNavigationCategory category = FieldNavigationCategory.Objects,
        bool startsAtLine = false)
    {
        var reader = createWalkmeshReader(Field);
        var mesh = reader.Read(new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, 0, 0, 0, 0, 0, 0)).Walkmesh!;
        var planner = new FieldWalkmeshRoutePlanner(reader, playerCollisionRadiusProvider: _ => Radius);
        var target = new FieldNavigationTarget(Field, FieldNavigationCategory.Objects, "G Bike", 241, 174, 0,
            StableId: "object:507:19", TriggerLine: BikeG, TriggerEntityId: 19, LineActivationRadius: Radius, LineActivatesOnOk: true);
        // The reader's verdict, read afresh each sample: by default the exact native rule for the
        // position, which a caller can override (an unknown or a fixed verdict).
        IReadOnlyList<FieldNavigationTarget> Read(FieldPositionSnapshot position)
        {
            var read = WithNativeVerdict(target, position);
            return [adjust?.Invoke(read) ?? read];
        }

        var controller = new FieldNavigationController(
            category == FieldNavigationCategory.Story
                ? new FieldNavigationTargetSource([], storyTargetProvider: Read)
                : new FieldNavigationTargetSource([], objectTargetProvider: Read), planner);
        var transform = new FieldNavigationControlTransform(-128);
        FieldPositionSnapshot Walked(int x, int y, byte direction)
        {
            var triangle = FieldWalkmeshPathfinder.ResolveTriangle(mesh, x, y, -10, -1);
            return new FieldPositionSnapshot(FieldPositionReader.FieldModule, Field, 0, x, y, -10, (ushort)Math.Max(0, triangle), direction);
        }

        var start = startsAtLine ? Walked(217, 149, 160) : Walked(190, 131, 88);
        for (var step = 0; step < 8 && controller.CurrentCategory != category; step++)
        {
            _ = controller.HandleAction(FieldNavigationAction.NextCategory, start, transform);
        }

        _ = controller.HandleAction(FieldNavigationAction.ToggleBeacon, start, transform);
        controller.NoteAutoWalkStarted();
        var clock = new DateTime(2026, 9, 29, 12, 24, 13, DateTimeKind.Utc);
        var tick = 0;
        string? Update(FieldPositionSnapshot position) => controller.UpdateLiveTracking(position,
            new FieldNavigationInputSnapshot(0, FieldNavigationInput.None), transform, false, 80,
            observedAt: clock.AddMilliseconds(50 * tick++))?.Speech;
        foreach (var (x, y) in startsAtLine ? Array.Empty<(int, int)>() : new[] { (190, 131), (203, 140), (213, 147) })
        {
            var position = Walked(x, y, 88);
            _ = Update(position);
            _ = controller.TryResolveAutomaticInput(position, transform, 80, out _);
        }

        return (controller, transform, Walked, Update);
    }
    /// <summary>
    /// What the production readers give when the native LINE state is readable: the engine's own
    /// verdict. Geometry-only tests have no game memory, so they stand in for it with the exact
    /// native rule (00637879 foot, 00636515 table angle, 00637D35 sector) - explicitly, as the
    /// assistant no longer computes it for itself.
    /// </summary>
    private static FieldNavigationTarget WithNativeVerdict(FieldNavigationTarget target, FieldPositionSnapshot position) =>
        target.TriggerLine is { } line && target.LineActivatesOnOk
            ? target with { LineOkAccepted = FieldNativeLineContact.AcceptsOk(line, position, target.LineActivationRadius) }
            : target;

    /// <summary>
    /// The readers return no verdict when the LINE state cannot be read coherently. Then OK is
    /// unproven: at (217,149) the rendered heading 160 looks right (the native angle there is
    /// 160), but the event heading 00637D35 tests may differ, and the original false "reached"
    /// came from exactly that. Auto walk neither says "reached" nor turns or backs off on an
    /// unknown verdict; it holds on the line and completes as soon as a true verdict is read.
    /// Both the Objects and the first-visit Story targets.
    /// </summary>
    private static void AnUnknownNativeVerdictIsNeverReached(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        Equal(true, FieldNativeLineContact.AcceptsOk(BikeG, At(217, 149, 160), Radius),
            "the rendered facing 160 at 217,149 would pass the computed rule");
        foreach (var category in new[] { FieldNavigationCategory.Objects, FieldNavigationCategory.Story })
        {
            bool? verdict = null;
            var (controller, transform, walked, update) = Approach(createWalkmeshReader,
                target => target with { Category = category, LineOkAccepted = verdict }, category);
            var onTheLine = walked(217, 149, 160);
            var heard = new List<string>();
            for (var sample = 0; sample < 6; sample++)
            {
                var spoken = update(onTheLine) ?? string.Empty;
                heard.Add($"[{spoken}|{controller.LastNavigationDiagnostic}]");
                Equal(false, spoken.Contains("reached", StringComparison.Ordinal),
                    $"{category}, native verdict unknown, sample {sample}: not reached; said \"{spoken}\"");
                Equal(false, controller.TryResolveAutomaticInput(onTheLine, transform, 80, out var press),
                    $"{category}, native verdict unknown, sample {sample}: no turn or back-off is pressed ({press})");
            }

            verdict = true;
            var then = update(onTheLine) ?? string.Empty;
            Equal(true, then.Contains("G Bike reached", StringComparison.Ordinal),
                $"{category}: once the engine's verdict reads true it is reached; said \"{then}\" after {string.Join(" ", heard)}; now {controller.LastNavigationDiagnostic}");
        }
    }

    /// <summary>
    /// Selecting autowalk while already beside the cabinet must use the native readiness
    /// immediately, before any movement is emitted. The warmed-up approach tests cannot expose
    /// this: starting at (217,149), rendered heading 160, previously announced reached
    /// for null, false and true alike because the automatic flag was set only after movement.
    /// </summary>
    private static void StartingOnTheLineUsesTheNativeVerdictBeforeMovement(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        foreach (var category in new[] { FieldNavigationCategory.Objects, FieldNavigationCategory.Story })
        foreach (bool? verdict in new bool?[] { null, false, true })
        {
            var (controller, transform, walked, update) = Approach(createWalkmeshReader,
                target => target with { Category = category, LineOkAccepted = verdict }, category, startsAtLine: true);
            var position = walked(217, 149, 160);
            var spoken = update(position) ?? string.Empty;
            Equal(verdict == true, spoken.Contains("G Bike reached", StringComparison.Ordinal),
                $"{category}: start on the line, native verdict {verdict?.ToString() ?? "unknown"}, before movement: said [{spoken}]");
            if (verdict is null)
            {
                Equal(false, controller.TryResolveAutomaticInput(position, transform, 80, out var press),
                    $"{category}: unreadable readiness at the starting position holds safely");
                Equal(FieldNavigationInput.None, press, $"{category}: no direction is pressed while unreadable");
            }

            if (verdict == false)
            {
                // P can stop autowalk while spoken navigation continues. Its manual touch
                // guidance must no longer use the automatic-facing requirement.
                controller.NoteAutoWalkStopped();
                var manual = update(position) ?? string.Empty;
                Equal(true, manual.Contains("G Bike reached", StringComparison.Ordinal),
                    $"{category}: stopping autowalk restores spoken touch guidance: said [{manual}]");
            }
        }
    }

    private static FieldPositionSnapshot At(int x, int y, int direction) =>
        new(FieldPositionReader.FieldModule, Field, 0, x, y, -10, 92, (byte)direction);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Gold Saucer machine line - {label}: expected {expected}, got {actual}.");
        }
    }
}

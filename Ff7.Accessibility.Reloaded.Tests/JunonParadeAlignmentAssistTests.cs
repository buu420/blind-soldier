using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class JunonParadeAlignmentAssistTests
{
    /// <summary>
    /// Everything the parade assist can be held to without a licensed copy of the
    /// game on the machine. These are the cases a build server runs.
    /// </summary>
    public static void Run()
    {
        AlignmentAssistDefaultsOnButCanBeDisabled();
        AssistModeExplainsRunAndOkWithoutDirectionChatter();
        ApproachesTheLiveJoinLineWithoutEverPressingOk();
        WaitsOnTheNativeLineForTheJoinedLatch();
        TracksTheMovingFormationAfterCloudJoins();
        PersistentRatingsWindowDoesNotBlockTheNativeMovablePhase();
        KeepsTrackingDuringTheNativeNowPrompt();
        PausesForOrdinaryDialogueAndResumesAfterItCloses();
        TreatsRunAsHarmlessAndManualDirectionAsATemporaryYield();
        IgnoresOneHeldDirectionOnTheScriptedLockReleaseEdge();
        RoutesAroundSolidSoldiersBeforeApproachingTheLine();
        WaitsForAParadeGapAndResumesWhenItOpens();
        AnnouncesWhenTheMovingLineArrivesDuringAWait();
        UnreadableRouteStateRemainsATerminalFault();
        AStallRecordsTheNativeTargetAndHeldDirection();
        FailsOutLoudWhenTheGameDoesNotAcknowledgeTheDirection();
        FailsOutLoudWhenMappedInputCannotBeSent();
        RepeatedResetRetriesARejectedKeyRelease();
        ManualModeKeepsTheExistingSpokenDirections();
    }

    /// <summary>
    /// The whole suite, including the regression that walks the real junonr4
    /// walkmesh out of the installed flevel.
    ///
    /// <para>This is deliberately a separate entry point rather than a skip. A
    /// machine with the game installed must run the native traversal - it is the
    /// only case that proves the assist crosses the parade ground the game
    /// actually has - so this throws rather than passing quietly when
    /// FF7_ACCESSIBILITY_DATA_ROOT is missing. A build server calls
    /// <see cref="Run()"/> instead, and gets every case that does not need a
    /// licensed copy of the game.</para>
    /// </summary>
    public static void RunWithInstalledGameData()
    {
        Run();
        TraversesTheNativeWalkmeshWhileTheRanksMove();
    }

    private static void AlignmentAssistDefaultsOnButCanBeDisabled()
    {
        Equal(true, new AccessibilityConfig().EnableJunonParadeAlignmentAssist, "alignment assist default");
        Equal(
            true,
            System.Text.Json.JsonSerializer.Deserialize<AccessibilityConfig>("{}")!
                .EnableJunonParadeAlignmentAssist,
            "an existing config without the new key inherits the enabled default");

        var sink = new RecordingSink();
        using var assist = CreateAssist(sink);
        var step = assist.Observe(Parade(), enabled: false);

        Equal(false, step.ClaimsFieldInput, "disabled assist does not own field input");
        Equal(false, step.IsAssistActive, "disabled assist stays inactive");
        Equal(0, sink.Transitions.Count, "disabled assist emits no keyboard transition");
    }

    private static void ApproachesTheLiveJoinLineWithoutEverPressingOk()
    {
        var sink = new RecordingSink();
        using var assist = CreateAssist(sink);

        var step = assist.Observe(Parade(playerY: 0, targetY: -100), enabled: true);

        Equal(true, step.ClaimsFieldInput, "active parade is owned");
        Equal(true, step.IsAssistActive, "assist starts automatically");
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeUp },
            sink.HeldScanCodes(),
            "assist approaches the join line");
        Equal(
            true,
            sink.Transitions.All(transition =>
                transition.ScanCode is HighwayAutoSteeringController.ScanCodeUp or
                    HighwayAutoSteeringController.ScanCodeRight or
                    HighwayAutoSteeringController.ScanCodeDown or
                    HighwayAutoSteeringController.ScanCodeLeft),
            "the alignment assist has no path to OK or any non-direction key");
    }

    private static void WaitsOnTheNativeLineForTheJoinedLatch()
    {
        var sink = new RecordingSink();
        using var assist = CreateAssist(sink);
        _ = assist.Observe(Parade(playerY: 0, targetY: -100), enabled: true);

        var arrived = assist.Observe(
            Parade(playerY: -100, targetY: -100,
                heldInput: FieldNavigationInputReader.UpMask), enabled: true);

        Equal(true, arrived.IsAssistActive, "arrival waits for the native proximity latch");
        Equal(0, sink.HeldScanCodes().Length,
            "the assist releases movement on the line instead of pushing 24 units beyond it");
        Equal(null, arrived.Speech, "one native latch delay is not a failure");

        var joined = assist.Observe(
            Parade(joined: true, playerY: -100, targetY: -100), enabled: true);
        Equal(true, joined.IsAssistActive, "the native joined latch continues formation tracking");
        Equal(0, sink.HeldScanCodes().Length, "aligned Cloud remains still until the line moves");
    }

    private static void TracksTheMovingFormationAfterCloudJoins()
    {
        var sink = new RecordingSink();
        using var assist = CreateAssist(sink);
        _ = assist.Observe(
            Parade(joined: true, playerX: 0, playerY: -100, targetX: 0, targetY: -100),
            enabled: true);

        var step = assist.Observe(
            Parade(
                joined: true,
                playerX: 0,
                playerY: -100,
                targetX: -40,
                targetY: -100,
                heldInput: 0),
            enabled: true);

        Equal(true, step.IsAssistActive, "joined formation remains assisted");
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeRight },
            sink.HeldScanCodes(),
            "camera transform is applied while following the moving formation line");
    }

    private static void KeepsTrackingDuringTheNativeNowPrompt()
    {
        var sink = new RecordingSink();
        using var assist = CreateAssist(sink);
        _ = assist.Observe(Parade(joined: true, playerY: -539, targetY: -759,
            activeMessageCount: 1), enabled: true);

        for (var sample = 0; sample < 5; sample++)
        {
            var prompt = assist.Observe(Parade(joined: true,
                playerY: -539 - sample * 12, targetY: -759,
                heldInput: FieldNavigationInputReader.UpMask | 0x20,
                activeMessageCount: 2, isNowPromptVisible: true), enabled: true);
            Equal(true, prompt.IsAssistActive, "the native movable Now interval stays assisted");
            Equal(new[] { HighwayAutoSteeringController.ScanCodeUp }, sink.HeldScanCodes(),
                "Now must not freeze tracking while the line keeps moving");
        }

        _ = assist.Observe(Parade(joined: true, playerY: -599, targetY: -759,
            activeMessageCount: 2, heldInput: FieldNavigationInputReader.UpMask), enabled: true);
        Equal(0, sink.HeldScanCodes().Length,
            "ordinary two-window dialogue after joining still releases movement");
        _ = assist.Observe(Parade(joined: true, playerY: -599, targetY: -759,
            activeMessageCount: 2, isNowPromptVisible: true), enabled: true);
        Equal(new[] { HighwayAutoSteeringController.ScanCodeUp }, sink.HeldScanCodes(),
            "tracking resumes when the exact native Now window returns");

        _ = assist.Observe(Parade(joined: true, activeMessageCount: 2,
            isNowPromptVisible: true, userControl: 1), enabled: true);
        Equal(0, sink.HeldScanCodes().Length, "a native control lock still overrides the Now exception");
        _ = assist.Observe(Parade(joined: true, activeMessageCount: 2,
            isNowPromptVisible: true, movieActive: 1), enabled: true);
        Equal(0, sink.HeldScanCodes().Length, "a movie still overrides the Now exception");
        _ = assist.Observe(Parade(joined: true, activeMessageCount: 3,
            isNowPromptVisible: true), enabled: true);
        Equal(0, sink.HeldScanCodes().Length, "an additional dialog still blocks steering");
        _ = assist.Observe(Parade(joined: false, activeMessageCount: 2,
            isNowPromptVisible: true), enabled: true);
        Equal(0, sink.HeldScanCodes().Length, "a timing window cannot bypass the pre-join dialogue gate");
    }

    private static void PausesForOrdinaryDialogueAndResumesAfterItCloses()
    {
        var sink = new RecordingSink();
        using var assist = CreateAssist(sink);
        _ = assist.Observe(Parade(activeMessageCount: 1), enabled: true);
        Equal(1, sink.HeldScanCodes().Length, "direction held before ordinary dialogue");

        var paused = assist.Observe(
            Parade(activeMessageCount: 2, heldInput: FieldNavigationInputReader.UpMask),
            enabled: true);
        Equal(true, paused.ClaimsFieldInput, "ordinary dialogue keeps field navigation out");
        Equal(0, sink.HeldScanCodes().Length, "all directions released during ordinary dialogue");
        Equal(null, paused.Speech, "ordinary dialogue does not add pause chatter");

        _ = assist.Observe(Parade(activeMessageCount: 1, heldInput: 0), enabled: true);
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeUp },
            sink.HeldScanCodes(),
            "alignment resumes after ordinary dialogue closes");
    }

    private static void PersistentRatingsWindowDoesNotBlockTheNativeMovablePhase()
    {
        var sink = new RecordingSink();
        using var assist = CreateAssist(sink);

        var locked = assist.Observe(
            Parade(userControl: 1, activeMessageCount: 1),
            enabled: true);
        Equal(true, locked.IsAssistActive, "script-owned parade remains assisted");
        Equal(0, sink.HeldScanCodes().Length, "directions stay released before Charge");

        var movable = assist.Observe(
            Parade(userControl: 0, activeMessageCount: 1),
            enabled: true);
        Equal(true, movable.IsAssistActive, "native movable phase remains assisted");
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeUp },
            sink.HeldScanCodes(),
            "the permanent Live TV Ratings window does not suppress steering");
    }

    private static void TreatsRunAsHarmlessAndManualDirectionAsATemporaryYield()
    {
        var sink = new RecordingSink();
        using var assist = CreateAssist(sink);
        _ = assist.Observe(Parade(), enabled: true);

        var running = assist.Observe(
            Parade(heldInput: FieldNavigationInputReader.UpMask | FieldNavigationInputReader.RunMask),
            enabled: true);
        Equal(true, running.IsAssistActive, "run bit is not a manual-direction override");

        var manual = assist.Observe(
            Parade(
                heldInput: FieldNavigationInputReader.UpMask |
                    FieldNavigationInputReader.RightMask),
            enabled: true);
        Equal(false, manual.IsAssistActive, "unowned direction yields to player");
        Equal(
            "Parade alignment assist paused for manual control. Release the direction keys to resume.",
            manual.Speech,
            "temporary manual override is explained");
        Equal(0, sink.HeldScanCodes().Length, "manual override releases every owned key");

        var stillHeld = assist.Observe(
            Parade(heldInput: FieldNavigationInputReader.RightMask),
            enabled: true);
        Equal(false, stillHeld.IsAssistActive, "assist remains yielded while a direction is held");
        Equal(null, stillHeld.Speech, "manual yield is announced only once");

        var firstClear = assist.Observe(Parade(heldInput: 0), enabled: true);
        Equal(false, firstClear.IsAssistActive, "one clear sample cannot re-arm into a bouncing key");

        var resumed = assist.Observe(Parade(heldInput: 0), enabled: true);
        Equal(true, resumed.IsAssistActive, "assist re-arms within the same parade after release");
        Equal(
            "Parade alignment assist resumed.",
            resumed.Speech,
            "the player hears that automatic alignment is active again");

        _ = assist.Observe(Parade(heldInput: 0), enabled: true);
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeUp },
            sink.HeldScanCodes(),
            "automatic alignment drives again after the temporary yield");
    }

    private static void IgnoresOneHeldDirectionOnTheScriptedLockReleaseEdge()
    {
        var sink = new RecordingSink();
        using var assist = CreateAssist(sink);

        _ = assist.Observe(
            Parade(userControl: 1, activeMessageCount: 1),
            enabled: true);
        var releaseEdge = assist.Observe(
            Parade(
                userControl: 0,
                activeMessageCount: 1,
                heldInput: FieldNavigationInputReader.UpMask |
                    FieldNavigationInputReader.RunMask),
            enabled: true);

        Equal(true, releaseEdge.IsAssistActive, "one lock-edge direction sample is not an opt-out");
        Equal(null, releaseEdge.Speech, "a one-frame input latch creates no override chatter");
        Equal(0, sink.HeldScanCodes().Length, "assist waits rather than fighting a lock-edge direction");

        var clear = assist.Observe(
            Parade(userControl: 0, activeMessageCount: 1, heldInput: 0),
            enabled: true);
        Equal(true, clear.IsAssistActive, "assist remains available after the latch clears");
        Equal(
            new[] { HighwayAutoSteeringController.ScanCodeUp },
            sink.HeldScanCodes(),
            "alignment starts on the first trustworthy clear sample");
    }

    private static void FailsOutLoudWhenTheGameDoesNotAcknowledgeTheDirection()
    {
        var sink = new RecordingSink();
        using var assist = CreateAssist(sink);
        _ = assist.Observe(Parade(), enabled: true);

        JunonParadeAlignmentStep step = default;
        for (var sample = 0; sample <= JunonParadeAlignmentAssist.AcknowledgementLimit; sample++)
        {
            step = assist.Observe(Parade(heldInput: 0), enabled: true);
        }

        Equal(false, step.IsAssistActive, "unacknowledged injection stops assist");
        NotBlank(step.Speech, "unacknowledged injection is never silent");
        Equal(0, sink.HeldScanCodes().Length, "unacknowledged injection releases keys");

        for (var sample = 0; sample < 4; sample++)
        {
            step = assist.Observe(Parade(heldInput: 0), enabled: true);
        }

        Equal(false, step.IsAssistActive, "a genuine acknowledgement fault stays terminal");
        Equal(0, sink.HeldScanCodes().Length, "terminal fault never re-arms movement");
    }

    private static void RoutesAroundSolidSoldiersBeforeApproachingTheLine()
    {
        var sink = new RecordingSink();
        var planner = new FieldWalkmeshRoutePlanner(
            CreateOpenWalkmeshReader(),
            dynamicObstacleProvider: (_, _) => [new(1, 919, -460, 0, 75)]);
        using var assist = new JunonParadeAlignmentAssist(
            new HighwayAutoSteeringController(sink), routePlanner: planner);

        var step = assist.Observe(
            Parade(playerX: 919, playerY: -359, targetX: 919, targetY: -759), true);

        Equal(true, step.IsAssistActive, "an available path around a soldier keeps the assist active");
        Equal(true, sink.HeldScanCodes().Any(scan =>
                scan is HighwayAutoSteeringController.ScanCodeLeft or HighwayAutoSteeringController.ScanCodeRight),
            "the real native obstacle planner steers around the soldier instead of straight into him");
    }

    private static FieldWalkmeshReader CreateOpenWalkmeshReader(Func<bool>? isUnreadable = null)
    {
        const int field = 0x100000;
        const int sectionOffset = 64;
        const int payload = field + sectionOffset + 4;
        var integers = new Dictionary<int, int>
        {
            [FieldWalkmeshReader.AddressFieldDataPtr] = field,
            [field + 22] = sectionOffset,
            [field + 26] = sectionOffset + 38,
            [payload] = 1
        };
        var shorts = new Dictionary<int, short>();
        // A single open floor triangle isolates native model collision from
        // boundary routing; the production planner still parses the real layout.
        (short X, short Y)[] vertices = [(-4000, 4000), (6000, 4000), (1000, -6000)];
        for (var index = 0; index < vertices.Length; index++)
        {
            var address = payload + 4 + index * 8;
            shorts[address] = vertices[index].X;
            shorts[address + 2] = vertices[index].Y;
            shorts[address + 4] = 0;
            shorts[payload + 28 + index * 2] = -1;
        }

        return new FieldWalkmeshReader(
            address => isUnreadable?.Invoke() == true ? 0 : integers[address],
            address => shorts[address]);
    }

    private static void WaitsForAParadeGapAndResumesWhenItOpens()
    {
        var sink = new RecordingSink();
        IReadOnlyList<FieldNavigationDynamicObstacle> obstacles = [];
        var planner = new FieldWalkmeshRoutePlanner(
            CreateOpenWalkmeshReader(), dynamicObstacleProvider: (_, _) => obstacles);
        using var assist = new JunonParadeAlignmentAssist(
            new HighwayAutoSteeringController(sink), routePlanner: planner);
        _ = assist.Observe(Parade(), enabled: true);
        Equal(1, sink.HeldScanCodes().Length, "clear native route starts moving");

        obstacles = [new(1, 0, -100, 0, 1000)];
        var blocked = assist.Observe(
            Parade(heldInput: FieldNavigationInputReader.UpMask), enabled: true);
        Equal(true, blocked.IsAssistActive, "a passing rank keeps the assist available");
        Equal(
            "Parade alignment assist waiting for an opening.",
            blocked.Speech,
            "waiting for a clear native route is announced");
        Equal(0, sink.HeldScanCodes().Length, "waiting releases the previously held key");

        for (var sample = 0; sample < 30; sample++)
        {
            blocked = assist.Observe(Parade(), enabled: true);
            Equal(true, blocked.IsAssistActive, "temporary crowd obstruction is not a movement stall");
            Equal(null, blocked.Speech, "unchanged crowd obstruction does not repeat speech");
        }

        obstacles = [];
        var resumed = assist.Observe(Parade(), enabled: true);
        Equal(true, resumed.IsAssistActive, "cleared crowd obstruction resumes in the same parade");
        Equal("Parade alignment assist resumed.", resumed.Speech, "movement resumption is audible");
        Equal(1, sink.HeldScanCodes().Length, "the assist actually sends movement after the opening appears");
    }

    private static void UnreadableRouteStateRemainsATerminalFault()
    {
        var sink = new RecordingSink();
        var unreadable = false;
        var planner = new FieldWalkmeshRoutePlanner(CreateOpenWalkmeshReader(() => unreadable));
        using var assist = new JunonParadeAlignmentAssist(
            new HighwayAutoSteeringController(sink), routePlanner: planner);
        _ = assist.Observe(Parade(), enabled: true);

        unreadable = true;
        var stopped = assist.Observe(
            Parade(heldInput: FieldNavigationInputReader.UpMask), enabled: true);
        Equal(false, stopped.IsAssistActive, "unreadable native geometry is a genuine terminal fault");
        NotBlank(stopped.Speech, "unreadable native geometry never fails silently");
        Equal(0, sink.HeldScanCodes().Length, "unreadable geometry releases movement");

        unreadable = false;
        for (var sample = 0; sample < 4; sample++)
        {
            stopped = assist.Observe(Parade(), enabled: true);
        }

        Equal(false, stopped.IsAssistActive, "a genuine read fault does not silently rearm");
        Equal(0, sink.HeldScanCodes().Length, "a terminal read fault keeps movement released");
    }

    private static void AnnouncesWhenTheMovingLineArrivesDuringAWait()
    {
        var sink = new RecordingSink();
        var planner = new FieldWalkmeshRoutePlanner(
            CreateOpenWalkmeshReader(), dynamicObstacleProvider: (_, _) => [new(1, 0, -100, 0, 1000)]);
        using var assist = new JunonParadeAlignmentAssist(
            new HighwayAutoSteeringController(sink), routePlanner: planner);
        _ = assist.Observe(Parade(), enabled: true);

        var arrived = assist.Observe(Parade(targetY: 0, joined: true), enabled: true);
        Equal("Parade alignment assist resumed.", arrived.Speech,
            "a moving line that arrives at Cloud ends the announced wait");
        Equal(0, sink.HeldScanCodes().Length, "already aligned Cloud does not need a movement key");
    }

    private static void TraversesTheNativeWalkmeshWhileTheRanksMove()
    {
        var runtime = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? throw new InvalidOperationException("The native parade regression needs FF7_ACCESSIBILITY_DATA_ROOT.");
        var source = new FlevelDataSource(runtime);
        Equal(true, source.TryReadField("junonr4", out var encoded), "native Junon field fixture readable");
        var field = Ff7LzsDecoder.DecodeFieldFile(encoded);
        const int fieldAddress = 0x100000;
        var reader = new FieldWalkmeshReader(
            address => address == FieldWalkmeshReader.AddressFieldDataPtr
                ? fieldAddress : BitConverter.ToInt32(field, address - fieldAddress),
            address => BitConverter.ToInt16(field, address - fieldAddress));
        var floor = reader.Read(new FieldPositionSnapshot(1, 363, 0, 728, -128, 0, 11, 0));
        Equal(true, floor.IsUsable, "real Junon walkmesh parsed");
        Equal(106, floor.Walkmesh!.Triangles.Count, "fixture matches the live parade triangle count");
        const int triggerSectionIndex = 7;
        var triggerSection = BitConverter.ToInt32(field, 6 + triggerSectionIndex * sizeof(int));
        var control = unchecked((sbyte)field[triggerSection + sizeof(int) +
            FieldNavigationControlReader.ControlDirectionOffset]);
        Equal((sbyte)-128, control, "native Junon field control rotation");
        Equal(true, NativeFloorAllowsStep(floor.Walkmesh, 15, new(887, -378, 0), new(888, -379, 0)),
            "native adjacency permits a crossing outside the planner's artificial portal inset");
        Equal(false, NativeFloorAllowsStep(floor.Walkmesh, 11, new(728, -128, 0), new(5000, 5000, 0)),
            "the movement oracle still rejects leaving the native floor");

        // Native Script 5 rank spacing, plus several starting offsets after
        // Charge. Rank speed 4 and player speeds 12/16 are explicit simulation
        // parameters, not a replacement for the native live timing test.
        foreach (var playerStep in new[] { 12, 16 })
        foreach (var startingAnchor in new[] { 1100, 1200, 1300 })
        {
            var sink = new RecordingSink();
            IReadOnlyList<FieldNavigationDynamicObstacle> actors = [];
            var planner = new FieldWalkmeshRoutePlanner(reader, dynamicObstacleProvider: (_, _) => actors);
            var diagnostics = new List<string>();
            using var assist = new JunonParadeAlignmentAssist(
                new HighwayAutoSteeringController(sink), diagnostics.Add, planner);
            var player = new FieldNavigationRouteWaypoint(728, -128, 0);
            var joined = false;
            var joinedSamples = 0;
            var anchor = startingAnchor;
            for (var frame = 0; frame < 230 && (anchor > 456 || joined); frame++, anchor -= 4)
            {
                actors =
                [
                    new(4, anchor - 493, -619, 0, 40),
                    new(5, anchor - 330, -427, 0, 40),
                    new(6, anchor - 330, -619, 0, 40),
                    new(7, anchor - 330, -815, 0, 40),
                    new(8, anchor - 167, -441, 0, 40),
                    new(9, anchor - 167, -619, 0, 40),
                    new(10, anchor - 167, -815, 0, 40),
                    new(11, anchor, -427, 0, 40),
                    new(12, anchor, -619, 0, 40)
                ];
                var lowerX = (anchor & ~255) | Math.Max(0, (anchor & 255) - 32);
                var targetX = (int)Math.Round(anchor + (lowerX - anchor) * (332d / 388d));
                var triangle = FieldWalkmeshPathfinder.ResolveTriangle(
                    floor.Walkmesh, player.X, player.Y, player.Z, -1);
                Equal(true, triangle >= 0, "simulated Cloud stays on the native walkmesh");
                var held = SinkHeldMask(sink) | FieldNavigationInputReader.RunMask;
                // The visible native Now prompt must not freeze the remaining
                // traversal after Cloud joins the passing line.
                var snapshot = Parade(joined, player.X, player.Y, targetX, -759, held,
                    activeMessageCount: joined ? (byte)2 : (byte)1, isNowPromptVisible: joined);
                snapshot = JunonMinigameSnapshot.FromWelcomeParade(snapshot.Parade with
                {
                    ControlTransform = new FieldNavigationControlTransform(control),
                    NavigationPosition = snapshot.Parade.NavigationPosition with { TriangleId = (ushort)triangle }
                });
                var step = assist.Observe(snapshot, enabled: true);
                Equal(true, step.IsAssistActive,
                    $"moving-rank traversal remains active, start={startingAnchor}, frame={frame}, player={player}, last={diagnostics.LastOrDefault()}");

                var keys = sink.HeldScanCodes();
                // Native control byte -128: Right is +X and Down is -Y.
                var dx = (keys.Contains(HighwayAutoSteeringController.ScanCodeRight) ? 1 : 0) -
                    (keys.Contains(HighwayAutoSteeringController.ScanCodeLeft) ? 1 : 0);
                var dy = (keys.Contains(HighwayAutoSteeringController.ScanCodeUp) ? 1 : 0) -
                    (keys.Contains(HighwayAutoSteeringController.ScanCodeDown) ? 1 : 0);
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length > 0)
                {
                    var start = player;
                    // Raw native triangle adjacency and modeled actor collision
                    // stop each unit step. The planner's extra portal clearance
                    // is a routing margin, not a physical wall in the game.
                    for (var unit = 1; unit <= playerStep; unit++)
                    {
                        var next = new FieldNavigationRouteWaypoint(
                            start.X + (int)Math.Round(dx * unit / length),
                            start.Y + (int)Math.Round(dy * unit / length), 0);
                        triangle = FieldWalkmeshPathfinder.ResolveTriangle(
                            floor.Walkmesh, player.X, player.Y, player.Z, -1);
                        if (!NativeFloorAllowsStep(floor.Walkmesh, triangle, player, next) ||
                            FieldNavigationDynamicObstacleGeometry.IntersectsAny(player, next, actors))
                        {
                            break;
                        }

                        player = next;
                    }
                }

                var lineX = lowerX - anchor;
                const int lineY = -388;
                var projection = ((player.X - anchor) * (double)lineX +
                    (player.Y + 427) * (double)lineY) / (lineX * lineX + lineY * lineY);
                var distanceSquared = Math.Pow(player.X - (anchor + projection * lineX), 2) +
                    Math.Pow(player.Y - (-427 + projection * lineY), 2);
                if (projection is >= 0 and <= 1 && distanceSquared < 50 * 50)
                {
                    joined = true;
                    joinedSamples++;
                }
            }

            Equal(true, joined,
                $"running Cloud reaches the native join band, start={startingAnchor}, player={player}, last={diagnostics.LastOrDefault()}");
            Equal(true, joinedSamples > 30,
                $"movement continues to track the passing formation, start={startingAnchor}, samples={joinedSamples}");
        }
    }

    private static bool NativeFloorAllowsStep(
        FieldWalkmesh floor, int startTriangle,
        FieldNavigationRouteWaypoint start, FieldNavigationRouteWaypoint end)
    {
        static long Cross(int ax, int ay, int bx, int by, int px, int py) =>
            ((long)bx - ax) * (py - ay) - ((long)by - ay) * (px - ax);

        static bool Contains(FieldWalkmeshTriangle triangle, FieldNavigationRouteWaypoint point)
        {
            var positive = false;
            var negative = false;
            for (var edgeIndex = 0; edgeIndex < 3; edgeIndex++)
            {
                var edge = triangle.GetEdge(edgeIndex);
                var side = Cross(edge.Start.X, edge.Start.Y, edge.End.X, edge.End.Y, point.X, point.Y);
                positive |= side > 0;
                negative |= side < 0;
            }

            return !(positive && negative);
        }

        if (startTriangle < 0 || !Contains(floor.Triangles[startTriangle], start))
        {
            return false;
        }

        var pending = new Queue<int>();
        var visited = new HashSet<int> { startTriangle };
        pending.Enqueue(startTriangle);
        while (pending.TryDequeue(out var index))
        {
            var triangle = floor.Triangles[index];
            if (Contains(triangle, end))
            {
                return true;
            }

            for (var edgeIndex = 0; edgeIndex < 3; edgeIndex++)
            {
                var adjacent = triangle.GetAdjacentTriangle(edgeIndex);
                if (adjacent < 0 || visited.Contains(adjacent))
                {
                    continue;
                }

                var edge = triangle.GetEdge(edgeIndex);
                var first = Cross(start.X, start.Y, end.X, end.Y, edge.Start.X, edge.Start.Y);
                var second = Cross(start.X, start.Y, end.X, end.Y, edge.End.X, edge.End.Y);
                var third = Cross(edge.Start.X, edge.Start.Y, edge.End.X, edge.End.Y, start.X, start.Y);
                var fourth = Cross(edge.Start.X, edge.Start.Y, edge.End.X, edge.End.Y, end.X, end.Y);
                if (Math.Sign(first) * Math.Sign(second) > 0 || Math.Sign(third) * Math.Sign(fourth) > 0 ||
                    Math.Max(start.X, end.X) < Math.Min(edge.Start.X, edge.End.X) ||
                    Math.Min(start.X, end.X) > Math.Max(edge.Start.X, edge.End.X) ||
                    Math.Max(start.Y, end.Y) < Math.Min(edge.Start.Y, edge.End.Y) ||
                    Math.Min(start.Y, end.Y) > Math.Max(edge.Start.Y, edge.End.Y))
                {
                    continue;
                }

                visited.Add(adjacent);
                pending.Enqueue(adjacent);
            }
        }

        return false;
    }

    private static uint SinkHeldMask(RecordingSink sink)
    {
        uint mask = 0;
        foreach (var scan in sink.HeldScanCodes())
        {
            mask |= scan switch
            {
                HighwayAutoSteeringController.ScanCodeUp => FieldNavigationInputReader.UpMask,
                HighwayAutoSteeringController.ScanCodeRight => FieldNavigationInputReader.RightMask,
                HighwayAutoSteeringController.ScanCodeDown => FieldNavigationInputReader.DownMask,
                HighwayAutoSteeringController.ScanCodeLeft => FieldNavigationInputReader.LeftMask,
                _ => 0
            };
        }

        return mask;
    }

    private static void AStallRecordsTheNativeTargetAndHeldDirection()
    {
        var sink = new RecordingSink();
        var diagnostics = new List<string>();
        using var assist = new JunonParadeAlignmentAssist(
            new HighwayAutoSteeringController(sink), diagnostics.Add);
        _ = assist.Observe(Parade(playerX: 919, playerY: -359, targetX: 919, targetY: -759), true);
        JunonParadeAlignmentStep step = default;
        for (var sample = 0; sample <= JunonParadeAlignmentAssist.StallLimit; sample++)
        {
            step = assist.Observe(Parade(playerX: 919, playerY: -359,
                targetX: 919, targetY: -759, heldInput: FieldNavigationInputReader.UpMask), true);
        }

        Equal(false, step.IsAssistActive, "an acknowledged direction with no movement still stops");
        var failure = diagnostics.Last(line => line.Contains("Cloud stopped moving", StringComparison.Ordinal));
        Contains(failure, "formation=919,-759", "stall evidence includes the observed native formation target");
        Contains(failure, "held=0x1000", "stall evidence distinguishes acknowledged input from an input failure");
        Equal(0, sink.HeldScanCodes().Length, "diagnostic capture does not retain the direction key");
    }

    private static void FailsOutLoudWhenMappedInputCannotBeSent()
    {
        var sink = new RecordingSink { RefuseEverything = true };
        using var assist = CreateAssist(sink);

        var step = assist.Observe(Parade(), enabled: true);

        Equal(false, step.IsAssistActive, "input failure stops assist");
        NotBlank(step.Speech, "input failure is never silent");
    }

    private static void RepeatedResetRetriesARejectedKeyRelease()
    {
        var sink = new RecordingSink();
        using var assist = CreateAssist(sink);
        _ = assist.Observe(Parade(), enabled: true);
        Equal(1, sink.HeldScanCodes().Length, "assist owns one direction before teardown");

        sink.RefuseKeyUps = true;
        assist.Reset("first failed teardown");
        var firstAttempts = sink.KeyUpAttemptCount;
        Equal(true, firstAttempts > 0, "first reset tries to release the key");

        assist.Reset("second failed teardown");
        Equal(
            true,
            sink.KeyUpAttemptCount > firstAttempts,
            "a later reset retries a key that may still be held");

        sink.RefuseKeyUps = false;
        assist.Reset("successful teardown retry");
        Equal(0, sink.HeldScanCodes().Length, "a later successful reset clears the held key");
    }

    private static void ManualModeKeepsTheExistingSpokenDirections()
    {
        var tracker = new JunonMinigameSpeechTracker();
        var cues = tracker.Observe(Parade(), paradeAlignmentAssistActive: false);

        Contains(One(cues), "Move into the open place", "manual-mode formation guidance");
    }

    private static void AssistModeExplainsRunAndOkWithoutDirectionChatter()
    {
        var tracker = new JunonMinigameSpeechTracker();
        var first = tracker.Observe(Parade(), paradeAlignmentAssistActive: true);
        Equal(
            "Welcome parade. Alignment assist on. Hold Run while moving into position. Press OK when you hear Now or the timing tone. Live TV rating, 24 percent.",
            One(first),
            "assist opening instruction");

        var movedLine = Parade(targetX: 80, targetY: -140);
        Equal(
            0,
            tracker.Observe(movedLine, paradeAlignmentAssistActive: true).Count,
            "moving formation line does not produce spoken direction chatter under assist");
    }

    private static JunonParadeAlignmentAssist CreateAssist(RecordingSink sink) =>
        new(new HighwayAutoSteeringController(sink));

    private static JunonMinigameSnapshot Parade(
        bool joined = false,
        int playerX = 0,
        int playerY = 0,
        int targetX = 0,
        int targetY = -100,
        uint heldInput = 0,
        byte userControl = 0,
        byte activeMessageCount = 0,
        ushort movieActive = 0,
        bool isNowPromptVisible = false) =>
        JunonMinigameSnapshot.FromWelcomeParade(new JunonWelcomeParadeState(
            IsActive: true,
            JoinedFormation: joined,
            Rating: 24,
            FinalRating: 0,
            HasFormationTarget: true,
            PlayerX: playerX,
            PlayerY: playerY,
            FormationTargetX: targetX,
            FormationTargetY: targetY,
            ControlTransform: new FieldNavigationControlTransform(0),
            HeldInput: heldInput,
            UserControl: userControl,
            ActiveMessageCount: activeMessageCount,
            MovieActive: movieActive)
        {
            IsNowPromptVisible = isNowPromptVisible,
            NavigationPosition = new FieldPositionSnapshot(
                1, JunonMinigameStateReader.WelcomeParadeFieldId, 0,
                playerX, playerY, 0, 0, 0)
        });

    private static string One(IReadOnlyList<JunonMinigameSpeechCue> cues)
    {
        Equal(1, cues.Count, "one Junon cue");
        return cues[0].Text;
    }

    private static void NotBlank(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label}: expected speech, got silence.");
        }
    }

    private static void Contains(string value, string expected, string label)
    {
        if (!value.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label}: expected '{expected}' in '{value}'.");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void Equal(ushort[] expected, ushort[] actual, string label)
    {
        expected = expected.Order().ToArray();
        actual = actual.Order().ToArray();
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
        }
    }

    private sealed class RecordingSink : IHighwayKeyboardInputSink
    {
        private readonly HashSet<ushort> held = [];

        internal List<HighwayKeyboardTransition> Transitions { get; } = [];
        internal bool RefuseEverything { get; init; }
        internal bool RefuseKeyUps { get; set; }
        internal int KeyUpAttemptCount { get; private set; }

        public HighwayKeyboardSendResult Send(IReadOnlyList<HighwayKeyboardTransition> transitions)
        {
            if (RefuseEverything)
            {
                return new HighwayKeyboardSendResult(0, 5);
            }

            KeyUpAttemptCount += transitions.Count(transition => !transition.IsKeyDown);
            if (RefuseKeyUps && transitions.Any(transition => !transition.IsKeyDown))
            {
                return new HighwayKeyboardSendResult(0, 5);
            }

            foreach (var transition in transitions)
            {
                Transitions.Add(transition);
                if (transition.IsKeyDown)
                {
                    held.Add(transition.ScanCode);
                }
                else
                {
                    held.Remove(transition.ScanCode);
                }
            }

            return new HighwayKeyboardSendResult(transitions.Count, 0);
        }

        internal ushort[] HeldScanCodes() => held.Order().ToArray();
    }
}

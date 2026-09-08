using Ff7.Accessibility.Reloaded;

/// <summary>
/// The lifecycle of the field's no-progress guard.
///
/// <para>The guard itself is easy; knowing when it applies is the whole problem, and root's
/// review named three ways the first version got it wrong. It kept its measurement across a
/// quick off and on, so a walk that was stalling when the player switched it off could stop
/// the next one seconds later. It took only a portal index and a distance, so a new target
/// with a larger remaining distance inherited the old target's five-second deadline. And -
/// worst - it reset itself on every sample where auto walk produced no direction, which is
/// precisely the shape of the failure it exists to catch, so it could never fire for it.</para>
///
/// <para>The distinction that makes it work is why the route had no direction. Waiting at an
/// interaction point or a ladder prompt is auto walk behaving correctly and nobody should be
/// told about it. A route, a waypoint, and no direction that probes clear is the party
/// wedged against something.</para>
/// </summary>
internal static class FieldAutoWalkConvergenceTrackerTests
{
    private static readonly DateTime Start = new(2026, 9, 8, 12, 40, 25, DateTimeKind.Utc);

    internal static void Run()
    {
        AWalkThatGetsNowhereIsStopped();
        AClearanceDeadlockIsMeasuredRatherThanExcused();
        AnInteractionOrLadderPauseIsNeverCountedAgainstTheRoute();
        AFlickeringPauseCannotHideAPartyGoingNowhere();
        RealProgressForgivesEverythingBeforeIt();
        SwitchingAutoWalkOffAndOnStartsAFreshMeasurement();
        ANewTargetDoesNotInheritTheOldTargetsDeadline();
        ArrivingIsNotFailingToArrive();
        AScriptedLockIsTheGamesDoingAndNotAutoWalks();
        ARouteThatIsWorkingIsLeftAlone();
        AToggleTooFastToBeSampledStillStartsAgain();
        TwoTargetsWithTheSameNameAreStillTwoTargets();
        BothRuntimesActuallyRunTheGuard();
    }

    /// <summary>
    /// The hole root found in the first version of these tests: they proved the guard
    /// restarts when it <i>sees</i> a sample saying auto walk is off, and the x64
    /// coordinator only samples on its own throttled cadence. Switch it off and straight
    /// back on inside one interval and no such sample ever exists - so the runtimes have to
    /// reset on the key press itself, and that is what this measures.
    /// </summary>
    private static void AToggleTooFastToBeSampledStillStartsAgain()
    {
        var tracker = new FieldAutoWalkConvergenceTracker();
        Equal(false, Drive(tracker, seconds: 4.5d, remaining: 300d),
            "a walk that has been getting nowhere for four and a half seconds");

        // No disabled sample at all - just the stop and the start, as the toggle does them.
        tracker.Reset();
        Equal(false, Drive(tracker, seconds: 4.5d, remaining: 300d, from: 4.6d),
            "the new walk is a new walk even though the guard never saw one go by");
    }

    /// <summary>
    /// A room with two men in it has two targets called "Man". An identity built from the
    /// spoken label cannot tell the player changing between them from the player standing
    /// still, which is exactly the case the identity reset exists for.
    /// </summary>
    private static void TwoTargetsWithTheSameNameAreStillTwoTargets()
    {
        var tracker = new FieldAutoWalkConvergenceTracker();
        Equal(false, Drive(tracker, seconds: 4.5d, remaining: 300d, identity: "487|Npcs|npc:487:13"),
            "the first Man has been going nowhere");
        Equal(false,
            Drive(tracker, seconds: 4.5d, remaining: 900d, from: 4.6d, identity: "487|Npcs|npc:487:14"),
            "the second Man is a different journey, however identically he is announced");
    }

    private static void AWalkThatGetsNowhereIsStopped()
    {
        // The Chocobo Square case: a direction every sample, the party moving, and the
        // remaining distance sitting still because the gateway it is aimed at is switched
        // off. Ninety-nine samples of this went by in silence.
        var tracker = new FieldAutoWalkConvergenceTracker();
        Equal(false, Drive(tracker, seconds: 4.5d, remaining: 25d),
            "four and a half seconds of no progress is not yet enough to give up");
        Equal(true, Drive(tracker, seconds: 1d, remaining: 25d, from: 4.5d),
            "five seconds of it is");
    }

    private static void AClearanceDeadlockIsMeasuredRatherThanExcused()
    {
        // jetin1: a route to the counter attendant, a waypoint, and every candidate
        // direction failing the native probe. The first version of this guard called that
        // "nothing to measure" and reset, on every single sample, forever.
        var tracker = new FieldAutoWalkConvergenceTracker();
        var tripped = false;
        for (var sample = 0; sample <= 60; sample++)
        {
            tripped |= tracker.Observe(
                Sample(hold: FieldAutoWalkHoldReason.NoClearDirection, remaining: 113d),
                Start.AddMilliseconds(sample * 100));
        }

        Equal(true, tripped,
            "a route with nowhere clear to step is the failure this guard is for, and it " +
            "must not be mistaken for a pause");
    }

    private static void AnInteractionOrLadderPauseIsNeverCountedAgainstTheRoute()
    {
        // Standing at an interaction point or a ladder prompt is the route working. Auto
        // walk must not press these, and the player must not be told their walk failed for
        // taking as long as they like over it.
        var tracker = new FieldAutoWalkConvergenceTracker();
        for (var sample = 0; sample <= 600; sample++)
        {
            if (tracker.Observe(
                    Sample(hold: FieldAutoWalkHoldReason.PlayerAction, remaining: 40d),
                    Start.AddMilliseconds(sample * 100)))
            {
                throw new InvalidOperationException(
                    "Field auto walk convergence: a minute of waiting at an interaction point " +
                    "was reported as a route that could not get closer. The player is the one " +
                    "holding it up, on purpose.");
            }
        }
    }

    private static void AFlickeringPauseCannotHideAPartyGoingNowhere()
    {
        // This is why a pause suspends rather than resets. Something that legitimately
        // holds the party for one sample every second - a repeating script, a dialogue
        // that opens and closes - would otherwise restart the clock forever and the guard
        // could never trip, which is the same hole in a different shape.
        var tracker = new FieldAutoWalkConvergenceTracker();
        var tripped = false;
        for (var sample = 0; sample <= 80; sample++)
        {
            var at = Start.AddMilliseconds(sample * 100);
            tripped |= tracker.Observe(
                sample % 10 == 0
                    ? Sample(hold: FieldAutoWalkHoldReason.PlayerAction, remaining: 25d)
                    : Sample(hold: FieldAutoWalkHoldReason.None, remaining: 25d),
                at);
        }

        Equal(true, tripped,
            "a pause every second must exempt its own moment and no more; the stall either " +
            "side of it is still a stall");
    }

    private static void RealProgressForgivesEverythingBeforeIt()
    {
        var tracker = new FieldAutoWalkConvergenceTracker();
        Equal(false, Drive(tracker, seconds: 4.5d, remaining: 400d),
            "four and a half seconds of nothing");

        // One real step - sixteen units, one walking sample - and the party is travelling.
        Equal(false, tracker.Observe(Sample(remaining: 380d), Start.AddSeconds(4.6d)),
            "a genuine gain clears the stall rather than merely postponing it");
        Equal(false, Drive(tracker, seconds: 4d, remaining: 380d, from: 4.6d),
            "and the four seconds that follow are measured from the gain, not from before it");
    }

    private static void SwitchingAutoWalkOffAndOnStartsAFreshMeasurement()
    {
        // Root's finding: "Mod.UpdateFieldAutoWalk returns early when disabled and otherwise
        // retains tracker across quick off/on."
        var tracker = new FieldAutoWalkConvergenceTracker();
        Equal(false, Drive(tracker, seconds: 4.5d, remaining: 300d),
            "nearly out of patience");

        _ = tracker.Observe(
            new FieldAutoWalkConvergenceSample(false, string.Empty, false, FieldAutoWalkHoldReason.NoRoute, 0, 0d),
            Start.AddSeconds(4.6d));
        Equal(false, Drive(tracker, seconds: 4.5d, remaining: 300d, from: 4.7d),
            "switching it off and on again is a new walk and gets its own five seconds");
    }

    private static void ANewTargetDoesNotInheritTheOldTargetsDeadline()
    {
        // "A new target with a larger remaining distance can inherit an old 5 sec timeout."
        var tracker = new FieldAutoWalkConvergenceTracker();
        Equal(false, Drive(tracker, seconds: 4.5d, remaining: 300d, identity: "487|Ticket office"),
            "the first target has been going nowhere");
        Equal(false, Drive(tracker, seconds: 4.5d, remaining: 900d, from: 4.6d, identity: "487|High Score Cloud"),
            "picking a different target starts again; its distance is a different number " +
            "about a different journey");
        Equal(true, Drive(tracker, seconds: 1d, remaining: 900d, from: 9.1d, identity: "487|High Score Cloud"),
            "and once the new target has had its own five seconds, it is judged on them");
    }

    private static void ArrivingIsNotFailingToArrive()
    {
        var tracker = new FieldAutoWalkConvergenceTracker();
        Equal(false, Drive(tracker, seconds: 4.5d, remaining: 30d),
            "closing in on the target");
        for (var sample = 0; sample <= 100; sample++)
        {
            if (tracker.Observe(
                    Sample(hold: FieldAutoWalkHoldReason.Arrived, remaining: 30d),
                    Start.AddSeconds(4.6d).AddMilliseconds(sample * 100)))
            {
                throw new InvalidOperationException(
                    "Field auto walk convergence: arriving was reported as being unable to get " +
                    "closer. The route is finished; there is nothing left to fail at.");
            }
        }
    }

    private static void AScriptedLockIsTheGamesDoingAndNotAutoWalks()
    {
        var tracker = new FieldAutoWalkConvergenceTracker();
        for (var sample = 0; sample <= 300; sample++)
        {
            if (tracker.Observe(
                    Sample(remaining: 250d) with { IsHeldByGame = true },
                    Start.AddMilliseconds(sample * 100)))
            {
                throw new InvalidOperationException(
                    "Field auto walk convergence: half a minute of cutscene was blamed on auto " +
                    "walk. The party cannot move during it and that is not auto walk's failure.");
            }
        }
    }

    private static void ARouteThatIsWorkingIsLeftAlone()
    {
        // Sixteen units a sample is the measured walking pace of the recorded session. A
        // guard that stops a walk which is actually walking is worse than no guard.
        var tracker = new FieldAutoWalkConvergenceTracker();
        var remaining = 1600d;
        for (var sample = 1; sample <= 90; sample++)
        {
            remaining -= 16d;
            if (tracker.Observe(Sample(remaining: remaining), Start.AddMilliseconds(sample * 100)))
            {
                throw new InvalidOperationException(
                    $"Field auto walk convergence: a party walking at a normal pace was stopped " +
                    $"after {sample} samples with {remaining:0} units left.");
            }
        }

        // A portal passed counts even when the straight-line distance has not improved,
        // because a route is entitled to go around.
        tracker.Reset();
        for (var sample = 1; sample <= 90; sample++)
        {
            if (tracker.Observe(
                    Sample(remaining: 500d) with { PortalIndex = sample / 20 },
                    Start.AddMilliseconds(sample * 100)))
            {
                throw new InvalidOperationException(
                    "Field auto walk convergence: a route working its way through portals was " +
                    "stopped for not shortening the straight line.");
            }
        }
    }

    /// <summary>
    /// Root's words: "linking the tracker class alone is not parity". The x64 coordinator
    /// compiled the class for a while without ever calling it, which looks identical from
    /// the outside and leaves half the players with no guard at all.
    /// </summary>
    private static void BothRuntimesActuallyRunTheGuard()
    {
        var root = FindSourceRoot();
        var coordinator = File.ReadAllText(Path.Combine(
            root,
            "Ff7.Accessibility.Steam2026X64",
            "Runtime",
            "Field",
            "Steam2026FieldNavigationCoordinator.cs"));
        var mod = File.ReadAllText(Path.Combine(root, "Ff7.Accessibility.Reloaded", "Mod.cs"));
        var csproj = File.ReadAllText(Path.Combine(
            root,
            "Ff7.Accessibility.Steam2026X64",
            "Ff7.Accessibility.Steam2026X64.csproj"));

        if (!csproj.Contains("FieldAutoWalkConvergenceTracker.cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The x64 runtime does not compile FieldAutoWalkConvergenceTracker at all.");
        }

        foreach (var (name, text) in new[] { ("x64 coordinator", coordinator), ("Mod", mod) })
        {
            if (!text.Contains("new FieldAutoWalkConvergenceSample(", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{name} never builds a convergence sample, so it never runs the guard. " +
                    "Compiling the class into a runtime is not the same as that runtime using it.");
            }

            if (!text.Contains("Could not get closer to ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{name} never tells the player the walk was stopped. Stopping in silence is " +
                    "the failure this whole guard was written for.");
            }
        }

        // The three lifecycle verbs the coordinator has to reach: reset when the route is
        // over, suspend when the game takes the party, and observe every frame otherwise.
        foreach (var call in new[]
                 {
                     "autoWalkConvergence.Reset();",
                     "autoWalkConvergence.Suspend();",
                     "autoWalkConvergence.Observe(",
                 })
        {
            if (!coordinator.Contains(call, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The x64 coordinator never calls {call}, so its guard has no lifecycle. " +
                    "Root asked for runtime parity, not a shared file.");
            }
        }

        // The reset has to be attached to the toggle itself. Both runtimes stop auto walk
        // in more than one place, and the x64 one samples on a throttled cadence, so a
        // reset that only happens when a sample arrives can miss a fast off and on
        // entirely.
        if (CountOf(coordinator, "autoWalkConvergence.Reset();") < 5)
        {
            throw new InvalidOperationException(
                "The x64 coordinator resets the guard in fewer places than it stops auto " +
                "walk. The toggle, the selection change, the parade hand-over, the " +
                "disabled path and the coordinator's own reset are all real stops.");
        }

        if (CountOf(mod, "fieldAutoWalkConvergence.Reset();") < 4 ||
            !mod.Contains("fieldAutoWalkConvergence.Suspend();", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Mod resets the guard on the sample rather than on the stop and the start, " +
                "and suspends it nowhere. A toggle inside one scan interval would then " +
                "hand the new walk the old walk's deadline.");
        }

        // And the identity has to be something two targets cannot share.
        var assistant = File.ReadAllText(Path.Combine(
            root, "Ff7.Accessibility.Reloaded", "FieldNavigationAssistant.cs"));
        if (!assistant.Contains("public string CurrentRouteIdentity", StringComparison.Ordinal) ||
            !assistant.Contains("beaconTargetId}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The route identity is not built from the stable target id. Two NPCs in one " +
                "room can be announced by the same label, and an identity that cannot tell " +
                "them apart cannot tell a new walk from a stalled one.");
        }

        foreach (var (name, text) in new[] { ("x64 coordinator", coordinator), ("Mod", mod) })
        {
            if (!text.Contains("CurrentRouteIdentity", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{name} builds its own route identity instead of using the one the " +
                    "route knows, so the two runtimes can disagree about what a new walk is.");
            }
        }
    }

    private static int CountOf(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

    private static bool Drive(
        FieldAutoWalkConvergenceTracker tracker,
        double seconds,
        double remaining,
        double from = 0d,
        string identity = "512|Exit to Ticket Office")
    {
        var tripped = false;
        for (var sample = 1; sample <= (int)Math.Round(seconds * 10d); sample++)
        {
            tripped |= tracker.Observe(
                Sample(remaining: remaining, identity: identity),
                Start.AddSeconds(from).AddMilliseconds(sample * 100));
        }

        return tripped;
    }

    private static FieldAutoWalkConvergenceSample Sample(
        FieldAutoWalkHoldReason hold = FieldAutoWalkHoldReason.None,
        double remaining = 100d,
        string identity = "512|Exit to Ticket Office") =>
        new(
            IsAutoWalkEnabled: true,
            RouteIdentity: identity,
            IsHeldByGame: false,
            Hold: hold,
            PortalIndex: 0,
            RemainingDistance: remaining);

    private static string FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var marker = Path.Combine(
                directory.FullName,
                "Ff7.Accessibility.Steam2026X64",
                "Ff7.Accessibility.Steam2026X64.csproj");
            if (File.Exists(marker))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not find the repository root from " + AppContext.BaseDirectory + ".");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Field auto walk convergence: {message}. Expected {expected}, actual {actual}.");
        }
    }
}

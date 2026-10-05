using System.Text;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The submarine pursuit tracker turns what the mission is drawing into a held direction
/// and throttle, and into fire guidance. Every case is about what a sighted player could
/// know: a submarine is placed only from a sighting, a remembered one is never moved by a
/// record the game is not drawing, and the fire prompt follows the game's own lock marker
/// and loaded lamps rather than anything decided here.
/// </summary>
internal static class SubmarinePursuitTrackerTests
{
    private static readonly DateTime Start = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private const SubmarineTargetModel Yellow = SubmarineTargetModel.Yellow;
    private const SubmarineTargetModel Red = SubmarineTargetModel.Red;
    private const SubmarineTargetModel Leader = SubmarineTargetModel.RedLeader;

    internal static void Run()
    {
        TheRedLeaderIsKnownByItsHullNotItsSlot();
        AllTwelveSubmarinesCanBeSelected();
        SelectionIsStableAndAnExplicitChoiceIsKept();
        StartingWithoutASightingIsRefused();
        StartAndApplyUseTheLastCoherentSnapshot();
        YawTurnsTheShortWayAndPitchIsInverted();
        ThrottleBoostsFarBrakesCloseAndNeverReverses();
        TheThrottleDoesNotHuntAtARangeEdge();
        AHeldTargetIsMatchedRatherThanHunted();
        AStaleGoalIsChasedFlatOut();
        ABehindTargetIsReacquiredFromItsLastSeenPoint();
        HiddenRecordsNeverMoveARememberedTarget();
        TheOverviewBlinksWithoutLosingTheGoalAndCannotLock();
        TheOverviewSaysWhenTheTargetIsCloseEnoughToLock();
        RememberedSightingsExpireOnMissionTime();
        OnlyAPauseStopsTheSightingClock();
        ReachingAStalePointStops();
        AnOverviewChaseHoldsCourseThroughAStalePoint();
        ANativeLockOnAnotherSubmarineIsReported();
        ReloadingThenLoadedPromptsFire();
        TheFirePromptNeedsTheSelectedTargetVisibleAndLocked();
        AnExistingOverviewLockStillAllowsManualFire();
        PauseResultSuspendResetAndUnavailableViewsStopDriving();
        ASinkingTargetIsReportedOnlyWhenSeen();
        HullContactStopsAndAnnounces();
        AimRemindersAreBounded();
        NothingExactIsEverSpoken();
        StatusDescribesPursuitAndLock();
    }

    private static void AnExistingOverviewLockStillAllowsManualFire()
    {
        var tracker = new SubmarinePursuitTracker();
        _ = tracker.Observe(Mission(viewMode: 1,
            targets: [Sub(0, Leader, 0, 512, 400, locked: true)]), Start);
        var started = tracker.StartPursuit();
        Contains("Press Switch to fire", started, "the visible existing lock is usable in overview");
        Lacks("cannot lock", started, "an existing lock must not be contradicted by overview guidance");
        var reloading = tracker.Observe(Mission(viewMode: 1, ready: 0, reloading: 4,
            targets: [Sub(0, Leader, 0, 512, 400, locked: true)]), Start.AddSeconds(1)).Speech ?? "";
        Contains("reloading", reloading, "the existing overview lock follows the drawn loaded lamps");
        Lacks("Press Switch", reloading, "an empty torpedo rack has no fire prompt");
    }

    private static void TheRedLeaderIsKnownByItsHullNotItsSlot()
    {
        // Arcade table 2 keeps its leader in slot 7, and ordinary red hulls (model 3)
        // share the colour without being the leader.
        var arcade = new SubmarinePursuitTracker();
        _ = arcade.Observe(Mission(arcade: true, targets:
        [
            Sub(0, Yellow, 100, 512, 300),
            Sub(1, Red, -100, 512, 300),
            Sub(7, Leader, 0, 512, 300)
        ]), Start);
        var selected = arcade.Apply(FieldNavigationAction.RepeatTarget) ?? "";
        Contains("Red Leader", selected, "the leader's own hull is the default choice");
        Lacks("Huge Materia", selected, "the arcade has no Huge Materia");
        var list = arcade.Apply(FieldNavigationAction.NextCategory) ?? "";
        Contains("red submarine 1", list, "an ordinary red hull is not the leader");
        Contains("yellow submarine 1", list, "yellow hulls are named by their colour");
        Lacks("Huge Materia", list, "nor is it named in the arcade list");
        Equal(1, Occurrences(list, "Red Leader"), $"exactly one leader in '{list}'");

        var story = new SubmarinePursuitTracker();
        _ = story.Observe(Mission(targets:
        [
            Sub(0, Leader, 0, 512, 300),
            Sub(9, Red, 50, 512, 300)
        ]), Start);
        Contains("Huge Materia", story.Apply(FieldNavigationAction.RepeatTarget) ?? "",
            "the story's leader is the one carrying the Huge Materia");
        var storyList = story.Apply(FieldNavigationAction.PreviousCategory) ?? "";
        Contains("red submarine 1", storyList, "a red hull in the story is still not the leader");
        Equal(1, Occurrences(storyList, "Huge Materia"), $"only the leader carries it: '{storyList}'");
        Equal(null, story.Apply(FieldNavigationAction.ToggleBeacon),
            "the beacon toggle is not a pursuit command");
    }

    private static void AllTwelveSubmarinesCanBeSelected()
    {
        var targets = Enumerable.Range(0, SubmarineMissionStateReader.EnemyRecordCount)
            .Select(slot => Sub(slot, slot == 0 ? Leader : Yellow, (slot - 6) * 40, 512, 400))
            .ToArray();
        var tracker = new SubmarinePursuitTracker();
        _ = tracker.Observe(Mission(targets: targets), Start);

        var announcements = Enumerable.Range(0, 12)
            .Select(_ => tracker.Apply(FieldNavigationAction.NextTarget) ?? "")
            .ToArray();
        Equal(12, announcements.Select(Subject).Distinct().Count(),
            $"every slot is its own choice: {string.Join(" | ", announcements)}");
        foreach (var announcement in announcements)
        {
            Contains(" of 12", announcement, "the list position is given");
        }

        Equal(Subject(announcements[0]), Subject(tracker.Apply(FieldNavigationAction.NextTarget) ?? ""),
            "the thirteenth press wraps to the first again");
        Equal(Subject(announcements[^1]),
            Subject(tracker.Apply(FieldNavigationAction.PreviousTarget) ?? ""),
            "previous walks back the same way");
    }

    private static void SelectionIsStableAndAnExplicitChoiceIsKept()
    {
        var first = Sub(3, Yellow, -100, 512, 300);
        var second = Sub(5, Yellow, 100, 512, 300);
        var leader = Sub(0, Leader, 0, 512, 500);

        // Implicit: the first sighting is chosen, and the leader replaces it once seen,
        // which a pursuit in progress says out loud.
        var implicitChoice = new SubmarinePursuitTracker();
        _ = implicitChoice.Observe(Mission(targets: [first, second]), Start);
        Contains("yellow submarine 1", implicitChoice.Apply(FieldNavigationAction.RepeatTarget) ?? "",
            "before any leader the first sighting is the default");
        _ = implicitChoice.StartPursuit();
        var retarget = implicitChoice.Observe(Mission(targets: [first, second, leader]), Start.AddSeconds(1)).Speech ?? "";
        Contains("Now pursuing Red Leader", retarget, "the leader is preferred once it is seen");

        // Explicit: a choice the player made is never overridden by the leader appearing.
        var explicitChoice = new SubmarinePursuitTracker();
        _ = explicitChoice.Observe(Mission(targets: [first, second]), Start);
        Contains("yellow submarine 2", explicitChoice.Apply(FieldNavigationAction.NextTarget) ?? "",
            "next moves from the default to the second sighting");
        _ = explicitChoice.Observe(Mission(targets: [first, second, leader]), Start.AddSeconds(0.5));
        Contains("yellow submarine 2", explicitChoice.Apply(FieldNavigationAction.RepeatTarget) ?? "",
            "the leader appearing does not take the player's choice away");

        // Names belong to the run: leaving view, and even being forgotten, keeps them.
        _ = ObserveFor(explicitChoice, Start.AddSeconds(1), 20, Mission(targets: [first, leader]));
        _ = explicitChoice.Observe(Mission(targets: [first, second, leader]), Start.AddSeconds(21.5));
        var list = explicitChoice.Apply(FieldNavigationAction.NextCategory) ?? "";
        Contains("yellow submarine 2", list, $"the same hull keeps its name when seen again: '{list}'");
        Lacks("yellow submarine 3", list, $"a returning hull is not renamed: '{list}'");
        var firstAt = list.IndexOf("yellow submarine 1", StringComparison.OrdinalIgnoreCase);
        Equal(true, firstAt >= 0 && firstAt < list.IndexOf("yellow submarine 2", StringComparison.OrdinalIgnoreCase),
            $"the list keeps the order of first sighting: '{list}'");

        // A new run starts the names again.
        explicitChoice.Reset();
        _ = explicitChoice.Observe(Mission(targets: [second]), Start.AddSeconds(30));
        Contains("yellow submarine 1", explicitChoice.Apply(FieldNavigationAction.RepeatTarget) ?? "",
            "a reset run names from one again");
    }

    private static void StartingWithoutASightingIsRefused()
    {
        var tracker = new SubmarinePursuitTracker();
        _ = tracker.Observe(Mission(), Start);
        var refused = tracker.StartPursuit();
        Contains("No submarine", refused, "nothing has been seen to pursue");
        Contains("overview control", refused, "the game's overview can reveal contacts without inventing a physical key binding");
        Equal(false, tracker.IsPursuing, "the pursuit does not start");
        Equal(false, tracker.Plan.CanDrive, "and nothing is driven");
        Contains("No submarines", tracker.Apply(FieldNavigationAction.NextTarget) ?? "",
            "there is nothing to select either");
    }

    private static void StartAndApplyUseTheLastCoherentSnapshot()
    {
        var tracker = new SubmarinePursuitTracker();
        var seen = Mission(targets: [Sub(2, Yellow, -300, 512, 300)]);
        _ = tracker.Observe(seen, Start);
        var started = tracker.StartPursuit();
        Contains("Pursuing yellow submarine 1", started, "the start names the target");
        Equal(true, tracker.IsPursuing, "the pursuit is running");
        Equal(true, tracker.Plan.CanDrive, "a start drives from the last coherent view at once");
        Equal(true, IsLeft(tracker.Plan.Direction), $"ahead and to the left, got {tracker.Plan.Direction}");

        tracker.Suspend();
        Equal(false, tracker.Plan.CanDrive, "a suspension drops the view and every held key");
        Equal(true, tracker.IsPursuing, "the player's intent survives a suspension");
        _ = tracker.Apply(FieldNavigationAction.RepeatTarget);
        _ = tracker.StartPursuit();
        Equal(false, tracker.Plan.CanDrive, "no command revives a view that was dropped");

        _ = tracker.Observe(seen, Start.AddSeconds(1));
        Equal(true, tracker.Plan.CanDrive, "a fresh coherent view resumes driving");

        Contains("Pursuit stopped", tracker.TogglePursuit(), "toggling a running pursuit stops it");
        Equal(false, tracker.Plan.CanDrive, "a stopped pursuit holds nothing");
        Contains("not running", tracker.StopPursuit(), "stopping twice says so");
        Contains("Pursuing", tracker.TogglePursuit(), "toggling again starts it");
        Equal(true, tracker.Plan.CanDrive, "from the last coherent view, without another observation");
    }

    private static void YawTurnsTheShortWayAndPitchIsInverted()
    {
        var (x, z) = At(-2000, 300);
        var plan = PlanFor(Sub(4, Yellow, x, 512, z), yaw: 2000);
        Equal(true, IsRight(plan.Direction),
            $"from 2000 to -2000 is 96 units to the right across the wrap, got {plan.Direction}");

        (x, z) = At(2000, 300);
        plan = PlanFor(Sub(4, Yellow, x, 512, z), yaw: -2000);
        Equal(true, IsLeft(plan.Direction), $"and the mirror image turns left, got {plan.Direction}");

        // 798580: Down adds 4 to 98734C, and a larger pitch is nose up.
        plan = PlanFor(Sub(4, Yellow, 0, 512 - 150, 300));
        Equal(HighwaySteeringDirection.Down, plan.Direction, "a target above needs the nose up, which is Down");
        plan = PlanFor(Sub(4, Yellow, 0, 512 + 150, 300));
        Equal(HighwaySteeringDirection.Up, plan.Direction, "a target below needs the nose down, which is Up");

        (x, z) = At(512, 300);
        plan = PlanFor(Sub(4, Yellow, x, 512 - 150, z));
        Equal(HighwaySteeringDirection.DownRight, plan.Direction, "right and above at once");

        (x, z) = At(20, 300);
        plan = PlanFor(Sub(4, Yellow, x, 512, z));
        Equal(HighwaySteeringDirection.None, plan.Direction, "inside the deadband nothing is held");
        Equal(true, plan.CanDrive, "holding nothing is still a valid plan");

        // Straight up past the pitch stop: no key is held against the clamp, and no
        // bearing is invented from a point almost directly overhead.
        plan = PlanFor(Sub(4, Yellow, 0, 0, 5), pitch: 0x3F0);
        Equal(HighwaySteeringDirection.None, plan.Direction, "nothing is held against the 0x3F0 stop");
    }

    private static void ThrottleBoostsFarBrakesCloseAndNeverReverses()
    {
        var far = PlanFor(Sub(4, Yellow, 0, 512, 1500), speedIndex: 17);
        Equal(true, far.Accelerate, "far and lined up holds Menu for the boost");
        Equal(false, far.Brake, "and does not brake");
        Equal(true, PlanFor(Sub(4, Yellow, 0, 512, 1500), speedIndex: 18).Accelerate,
            "the boost lasts only while Menu is held");

        var close = PlanFor(Sub(4, Yellow, 0, 512, 100), speedIndex: 17);
        Equal(true, close.Brake, "close in, full ahead is too fast to hold the lock");
        Equal(false, close.Accelerate, "and Menu is released");
        var settled = PlanFor(Sub(4, Yellow, 0, 512, 100), speedIndex: 14);
        Equal(false, settled.Brake || settled.Accelerate, "one step of overshoot is left alone");
        Equal(true, PlanFor(Sub(4, Yellow, 0, 512, 100), speedIndex: 11).Accelerate,
            "below the close-in speed it accelerates");
        Equal(true, PlanFor(Sub(4, Yellow, 0, 512, -300), speedIndex: 17).Brake,
            "a sharp turn is made slowly so the circle is tight");
        Equal(true, PlanFor(Sub(4, Yellow, 0, 512, 100), speedIndex: 3).Accelerate,
            "a submarine already going astern is brought forward");

        SubmarineVisibleTarget[] geometry =
        [
            Sub(4, Yellow, 0, 512, 60),
            Sub(4, Yellow, 0, 512, 100),
            Sub(4, Yellow, 0, 512, -300),
            Sub(4, Yellow, 300, 512, 0),
            Sub(4, Yellow, 0, 512, 600),
            Sub(4, Yellow, 0, 512, 1500)
        ];
        for (var index = 0; index <= 18; index++)
        {
            foreach (var target in geometry)
            {
                var plan = PlanFor(target, speedIndex: index);
                Equal(false, plan.Accelerate && plan.Brake, $"never both throttles at index {index}");
                if (index <= 13)
                {
                    Equal(false, plan.Brake, $"no brake at index {index}: going astern is never sustained");
                }
            }
        }

        Equal(false, PlanFor(Sub(4, Yellow, 0, 512, 100), speedIndex: -5).Brake || PlanFor(Sub(4, Yellow, 0, 512, 100), speedIndex: -5).Accelerate,
            "a throttle word outside 0..18 is not acted on");
    }

    /// <summary>
    /// Holding station behind a target that keeps drifting across a range threshold must
    /// not flip Menu and Cancel every tick (seen in the offline simulation): a band is only
    /// left once the range is clearly past its edge.
    /// </summary>
    private static void TheThrottleDoesNotHuntAtARangeEdge()
    {
        var tracker = new SubmarinePursuitTracker();
        _ = tracker.Observe(Mission(speedIndex: 14, targets: [Sub(1, Yellow, 0, 512, 150)]), Start);
        _ = tracker.StartPursuit();
        Equal(false, tracker.Plan.Accelerate || tracker.Plan.Brake, "settled close in");

        for (var tick = 1; tick <= 8; tick++)
        {
            var range = tick % 2 == 0 ? 150 : 170;
            _ = tracker.Observe(Mission(speedIndex: 14, targets: [Sub(1, Yellow, 0, 512, range)]), Start.AddSeconds(tick * 0.25));
            Equal(false, tracker.Plan.Accelerate, $"a range wobbling across the edge ({range}) does not open the throttle");
        }

        _ = tracker.Observe(Mission(speedIndex: 14, targets: [Sub(1, Yellow, 0, 512, 450)]), Start.AddSeconds(3));
        Equal(true, tracker.Plan.Accelerate, "a target clearly beyond the holding range does");
    }

    /// <summary>
    /// Inside the lock range the throttle follows whether the visible target is closing or
    /// opening, rather than a step per range band: the leader outruns full ahead and a
    /// slow hull is outrun by it, and banding both made Menu and Cancel alternate about
    /// twice a second in the offline simulation.
    /// </summary>
    private static void AHeldTargetIsMatchedRatherThanHunted()
    {
        var tracker = new SubmarinePursuitTracker();
        var at = Start;
        _ = tracker.Observe(Mission(speedIndex: 17, targets: [Sub(0, Leader, 0, 512, 300)]), at);
        _ = tracker.StartPursuit();

        // Opening at full ahead: never braked, and the boost comes once it is too far.
        foreach (var range in new[] { 310, 320, 330, 340, 350, 360 })
        {
            at = at.AddSeconds(0.25);
            _ = tracker.Observe(Mission(speedIndex: 17, targets: [Sub(0, Leader, 0, 512, range)]), at);
            Equal(false, tracker.Plan.Brake, $"an opening target at {range} is not braked for");
        }

        Equal(true, tracker.Plan.Accelerate, "past the holding range and not closing, the boost is taken");

        // Closing under the boost: Menu is kept until it is well inside again, then let go.
        foreach (var (range, accelerating) in new[] { (330, true), (300, true), (280, true), (260, false) })
        {
            at = at.AddSeconds(0.25);
            _ = tracker.Observe(Mission(speedIndex: 18, targets: [Sub(0, Leader, 0, 512, range)]), at);
            Equal(accelerating, tracker.Plan.Accelerate, $"the boost at {range} while closing");
            Equal(false, tracker.Plan.Brake, $"and no brake at {range}");
        }

        // Closing fast near in: braked, but never below step 12.
        foreach (var range in new[] { 230, 200, 180 })
        {
            at = at.AddSeconds(0.25);
            _ = tracker.Observe(Mission(speedIndex: 17, targets: [Sub(0, Leader, 0, 512, range)]), at);
        }

        Equal(true, tracker.Plan.Brake, "too near and still closing, it brakes");
        at = at.AddSeconds(0.25);
        _ = tracker.Observe(Mission(speedIndex: 12, targets: [Sub(0, Leader, 0, 512, 160)]), at);
        Equal(false, tracker.Plan.Brake, "not below step 12");
    }

    /// <summary>
    /// A seen point is where the target was, not where it is: it has kept moving. Braking
    /// because that point is near let the leader open the gap again in the offline
    /// simulation, so a goal out of view is approached as if far (boost when lined up),
    /// and only a sharp turn slows it.
    /// </summary>
    private static void AStaleGoalIsChasedFlatOut()
    {
        var tracker = new SubmarinePursuitTracker();
        _ = tracker.Observe(Mission(speedIndex: 17, targets: [Sub(1, Yellow, 0, 512, 200)]), Start);
        _ = tracker.StartPursuit();
        Equal(true, tracker.Plan.Brake, "a target in view close ahead is approached slowly");

        _ = tracker.Observe(Mission(speedIndex: 17), Start.AddSeconds(0.25));
        Equal(false, tracker.Plan.Brake, "a seen point is not braked for");
        Equal(true, tracker.Plan.Accelerate, "it is chased at the boost when lined up");

        _ = tracker.Observe(Mission(speedIndex: 17, yaw: 2048), Start.AddSeconds(0.5));
        Equal(true, tracker.Plan.Brake, "a sharp turn back toward it is still made slowly");

        // Off to the side and near, the turn is the tight one: step 15 circles at 138.
        _ = tracker.Observe(Mission(speedIndex: 16, yaw: 700), Start.AddSeconds(0.75));
        Equal(true, tracker.Plan.Brake, "a near point well off the bow is turned onto at the tight step");
    }

    private static void ABehindTargetIsReacquiredFromItsLastSeenPoint()
    {
        var tracker = new SubmarinePursuitTracker();
        _ = tracker.Observe(Mission(targets: [Sub(2, Yellow, -300, 512, 300)]), Start);
        _ = tracker.StartPursuit();
        Equal(true, IsLeft(tracker.Plan.Direction), "it was seen ahead and to the left");

        var gone = ObserveFor(tracker, Start.AddSeconds(0.25), 2.5, Mission());
        Equal(true, tracker.Plan.CanDrive, "a remembered sighting is still a goal");
        Equal(true, IsLeft(tracker.Plan.Direction), "turning toward where it was seen");
        Contains("out of view", gone, "losing sight of it is said");
        Contains("last seen", gone, "and the goal is said to be a remembered position");

        _ = tracker.Observe(Mission(yaw: -512), Start.AddSeconds(3));
        Equal(HighwaySteeringDirection.None, tracker.Plan.Direction, "facing the seen point holds nothing");

        var back = tracker.Observe(
            Mission(yaw: -512, targets: [Sub(2, Yellow, 300, 512, 300)]),
            Start.AddSeconds(3.25)).Speech ?? "";
        Contains("back in view", back, "reacquiring it is said");
        Equal(true, IsRight(tracker.Plan.Direction), "the new sighting is to the right of the heading");
    }

    private static void HiddenRecordsNeverMoveARememberedTarget()
    {
        var tracker = new SubmarinePursuitTracker();
        _ = tracker.Observe(Mission(targets: [Sub(6, Yellow, 0, 512, 400)]), Start);
        _ = tracker.StartPursuit();

        // No longer drawn. The submarine moves past where it was; the goal stays put.
        _ = tracker.Observe(Mission(x: 200, z: 400), Start.AddSeconds(0.5));
        Equal(true, IsLeft(tracker.Plan.Direction), "from the east the seen point is on the left");
        _ = tracker.Observe(Mission(x: -200, z: 400), Start.AddSeconds(1));
        Equal(true, IsRight(tracker.Plan.Direction), "from the west it is on the right: it did not move");
    }

    private static void TheOverviewBlinksWithoutLosingTheGoalAndCannotLock()
    {
        var tracker = new SubmarinePursuitTracker();
        _ = tracker.Observe(Mission(viewMode: 1, targets: [Sub(0, Leader, -600, 512, 1200)]), Start);
        var started = tracker.StartPursuit();
        Contains("cannot lock", started, "the overview cannot lock");
        Equal(false, tracker.Plan.ReturnToFiringView, "a distant target stays within the overview's longer sonar range");

        var blink = ObserveFor(tracker, Start.AddSeconds(0.25), 3, Mission(viewMode: 1));
        Equal(true, tracker.Plan.CanDrive, "the seen point is held between sweeps");
        Equal(true, IsLeft(tracker.Plan.Direction), "still turning toward the seen point");
        Lacks("out of view", blink, "a sweep passing in the overview is not news");
        Lacks("Lost", blink, "nor a loss");

        _ = tracker.Observe(Mission(viewMode: 1, targets: [Sub(0, Leader, 600, 512, 1200)]), Start.AddSeconds(4));
        Equal(true, IsRight(tracker.Plan.Direction), "the next sweep's sighting is the goal");

        // Changing to the overview mid-pursuit is said once.
        var normal = new SubmarinePursuitTracker();
        _ = normal.Observe(Mission(targets: [Sub(1, Yellow, 0, 512, 300)]), Start);
        _ = normal.StartPursuit();
        var entered = normal.Observe(Mission(viewMode: 1, targets: [Sub(1, Yellow, 0, 512, 300)]), Start.AddSeconds(0.5)).Speech ?? "";
        Equal(true, normal.Plan.ReturnToFiringView, "a close overview target requests the ordinary firing view");
        Equal(null, normal.Observe(Mission(viewMode: 1, targets: [Sub(1, Yellow, 0, 512, 300)]), Start.AddSeconds(0.75)).Speech,
            "and not repeated");
    }

    /// <summary>
    /// The overview is where a lost submarine is found, but it cannot lock. A sighted
    /// player sees the blip close to their own hull and switches back; the pursuit says
    /// when the point is close and asks the host to return through the ordinary Target action.
    /// </summary>
    private static void TheOverviewSaysWhenTheTargetIsCloseEnoughToLock()
    {
        var tracker = new SubmarinePursuitTracker();
        _ = tracker.Observe(Mission(viewMode: 1, targets: [Sub(0, Leader, 0, 512, 1500)]), Start);
        _ = tracker.StartPursuit();
        var far = tracker.Observe(Mission(viewMode: 1, targets: [Sub(0, Leader, 0, 512, 900)]), Start.AddSeconds(0.5)).Speech ?? "";
        Lacks("close", far, "a far sighting is not close");

        var close = tracker.Observe(Mission(viewMode: 1, targets: [Sub(0, Leader, 0, 512, 400)]), Start.AddSeconds(1)).Speech ?? "";
        Contains("Red Leader is close", close, "a sighting within lock range is said");
        Equal(true, tracker.Plan.ReturnToFiringView, "the native view action is requested when the target is close");

        // Between sweeps it blinks out; that is not the target moving away.
        Equal(null, tracker.Observe(Mission(viewMode: 1), Start.AddSeconds(1.25)).Speech, "a blink is not news");
        Equal(null, tracker.Observe(Mission(viewMode: 1, targets: [Sub(0, Leader, 0, 512, 470)]), Start.AddSeconds(1.5)).Speech,
            "nor is drifting a little further off");

        var started = new SubmarinePursuitTracker();
        _ = started.Observe(Mission(viewMode: 1, targets: [Sub(0, Leader, 0, 512, 300)]), Start);
        Contains("Red Leader is close", started.StartPursuit(), "a start in the overview close to the target says so");
    }

    private static void RememberedSightingsExpireOnMissionTime()
    {
        var tracker = new SubmarinePursuitTracker();
        _ = tracker.Observe(Mission(targets: [Sub(5, Yellow, 0, 512, 2000)]), Start);
        _ = tracker.StartPursuit();

        _ = ObserveFor(tracker, Start.AddSeconds(0.5), 10, Mission());
        Equal(true, tracker.IsPursuing, "ten seconds after a sighting it is still a goal");

        // A pause freezes the sea, so it does not age the sighting.
        _ = ObserveFor(tracker, Start.AddSeconds(10.5), 60, Mission(paused: true));
        _ = ObserveFor(tracker, Start.AddSeconds(71), 4, Mission());
        Equal(true, tracker.IsPursuing, "fourteen and a half seconds of mission time is still remembered");

        var lost = ObserveFor(tracker, Start.AddSeconds(75.25), 3, Mission());
        Equal(false, tracker.IsPursuing, "past the bound the sighting is let go");
        Equal(false, tracker.Plan.CanDrive, "and nothing is driven toward it");
        Contains("Lost yellow submarine 1", lost, "the loss is said");
        Contains("Pursuit stopped", lost, "and the pursuit ends");
        Lacks("sunk", lost, "an unseen loss is not a sinking");
        Lacks("destroyed", lost, "nor a kill");
        Contains("No submarines", tracker.Apply(FieldNavigationAction.RepeatTarget) ?? "",
            "an expired sighting is no longer selectable");
    }

    /// <summary>
    /// The sea only stands still while the mission is paused. A suspension (an unreadable
    /// frame, focus, a menu) or an unreadable view leaves the game running, so a reading
    /// that keeps flapping must not keep a sighting alive by stopping its clock.
    /// </summary>
    private static void OnlyAPauseStopsTheSightingClock()
    {
        var suspended = new SubmarinePursuitTracker();
        _ = suspended.Observe(Mission(targets: [Sub(5, Yellow, 0, 512, 2000)]), Start);
        _ = suspended.StartPursuit();
        for (var second = 1; second <= 20; second++)
        {
            suspended.Suspend();
            _ = suspended.Observe(Mission(), Start.AddSeconds(second));
        }

        Equal(false, suspended.IsPursuing, "suspensions between readings do not stop the clock");

        var unreadable = new SubmarinePursuitTracker();
        _ = unreadable.Observe(Mission(targets: [Sub(5, Yellow, 0, 512, 2000)]), Start);
        _ = unreadable.StartPursuit();
        for (var second = 1; second <= 20; second++)
        {
            _ = unreadable.Observe(Mission(placeable: second % 2 == 0), Start.AddSeconds(second));
        }

        Equal(false, unreadable.IsPursuing, "nor do unreadable views between readable ones");
    }

    private static void ReachingAStalePointStops()
    {
        var tracker = new SubmarinePursuitTracker();
        _ = tracker.Observe(Mission(targets: [Sub(8, Yellow, 0, 512, 300)]), Start);
        _ = tracker.StartPursuit();
        _ = tracker.Observe(Mission(), Start.AddSeconds(0.25));

        // Just after the sighting the normal view's own sweep gets a chance to redraw it.
        Equal(null, tracker.Observe(Mission(z: 290), Start.AddSeconds(0.5)).Speech, "a fresh sighting is waited for");
        Equal(true, tracker.IsPursuing, "for one sweep's grace");
        Equal(HighwaySteeringDirection.None, tracker.Plan.Direction, "holding the course meanwhile");

        var reached = ObserveFor(tracker, Start.AddSeconds(0.75), 1.5, Mission(z: 290));
        Contains("Reached where yellow submarine 1 was last seen", reached, "arriving at a stale point is said");
        Equal(false, tracker.IsPursuing, "and the pursuit stops there");
        Equal(false, tracker.Plan.CanDrive, "holding nothing");

        // A point inside the tight turning circle (61 units at step 13) can be circled but
        // never driven onto, so arriving that close is arriving (the offline simulation
        // orbited one until the sighting expired).
        var beside = new SubmarinePursuitTracker();
        _ = beside.Observe(Mission(targets: [Sub(8, Yellow, 300, 512, 300)]), Start);
        _ = beside.StartPursuit();
        var besideSpeech = ObserveFor(beside, Start.AddSeconds(0.25), 2, Mission(x: 230, z: 250));
        Contains("Reached where yellow submarine 1 was last seen", besideSpeech, "a point 86 units off is reached");

        // That sighting has been searched: starting again does not head back to it (the
        // offline simulation restarted into the same stop every tick).
        Contains("No submarines", tracker.StartPursuit(), "a searched sighting is not pursued again");
        Equal(false, tracker.IsPursuing, "so the pursuit stays stopped");
        _ = tracker.Observe(Mission(z: 290, targets: [Sub(8, Yellow, 0, 512, 700)]), Start.AddSeconds(3));
        Contains("Pursuing yellow submarine 1", tracker.StartPursuit(), "a new sighting can be pursued, under the same name");

        // A target in view at the same distance is not a stale point.
        var visible = new SubmarinePursuitTracker();
        _ = visible.Observe(Mission(targets: [Sub(8, Yellow, 0, 512, 300)]), Start);
        _ = visible.StartPursuit();
        _ = visible.Observe(Mission(z: 290, targets: [Sub(8, Yellow, 0, 512, 300)]), Start.AddSeconds(0.25));
        Equal(true, visible.IsPursuing, "a visible target close by is still pursued");
    }

    /// <summary>
    /// In the overview a sighting is refreshed every sweep. Stopping, or turning back,
    /// at each reached point would never catch the leader, which outruns full ahead (the
    /// offline simulation stalled about 500 units behind it). A reached point holds the
    /// course until the next sweep or until the sighting is forgotten.
    /// </summary>
    private static void AnOverviewChaseHoldsCourseThroughAStalePoint()
    {
        var tracker = new SubmarinePursuitTracker();
        _ = tracker.Observe(Mission(viewMode: 1, targets: [Sub(0, Leader, 0, 512, 300)]), Start);
        _ = tracker.StartPursuit();
        _ = tracker.Observe(Mission(viewMode: 1), Start.AddSeconds(0.25));
        var reached = tracker.Observe(Mission(viewMode: 1, z: 290, speedIndex: 18), Start.AddSeconds(0.5)).Speech ?? "";
        Contains("Holding course for the next sweep", reached, "a reached point in the overview holds the course");
        Equal(true, tracker.IsPursuing, "without ending the pursuit");
        Equal(HighwaySteeringDirection.None, tracker.Plan.Direction, "no turn is held");
        Equal(true, tracker.Plan.ReturnToFiringView, "a reached remembered point now requests the normal firing view");
        Equal(false, tracker.Plan.Accelerate, "propulsion is released while requesting that view");

        _ = tracker.Observe(Mission(viewMode: 1, z: 360, speedIndex: 18), Start.AddSeconds(1));
        Equal(HighwaySteeringDirection.None, tracker.Plan.Direction, "past the point it does not turn back to it");
        Equal(true, tracker.IsPursuing, "and keeps waiting for the sweep");

        _ = tracker.Observe(Mission(viewMode: 1, z: 360, speedIndex: 18, targets: [Sub(0, Leader, -400, 512, 900)]), Start.AddSeconds(2));
        Equal(true, IsLeft(tracker.Plan.Direction), "the next sweep's sighting is steered toward again");

        // Back in the normal view, a reached point is waited on for one sweep's grace and
        // then means there is nothing more to go on.
        _ = tracker.Observe(Mission(viewMode: 0, x: -400, z: 890), Start.AddSeconds(2.25));
        Equal(true, tracker.IsPursuing, "the normal view's own sweep gets its chance first");
        var stopped = ObserveFor(tracker, Start.AddSeconds(2.5), 1.5, Mission(viewMode: 0, x: -400, z: 890));
        Contains("Reached where Red Leader was last seen", stopped, "then the reached point is said");
        Equal(false, tracker.IsPursuing, "and the pursuit stops there");
    }

    private static void ANativeLockOnAnotherSubmarineIsReported()
    {
        var tracker = new SubmarinePursuitTracker();
        var leader = Sub(0, Leader, 0, 512, 450);
        var other = Sub(3, Yellow, 40, 512, 200, locked: true);
        _ = tracker.Observe(Mission(targets: [leader, other]), Start);
        var started = tracker.StartPursuit();
        Contains("Lock is on yellow submarine 1, not Red Leader", started, "the game's own lock is reported");
        Lacks("Switch", started, "and no fire prompt is given for it");

        var moved = tracker.Observe(
            Mission(targets: [leader with { IsLocked = true }, other with { IsLocked = false }]),
            Start.AddSeconds(1));
        Contains("Red Leader locked", moved.Speech ?? "", "the lock moving to the target is said");
        Contains("Press Switch to fire", moved.Speech ?? "", "with the fire prompt");
        Equal(true, moved.PlayLockCue, "and the protected cue");

        var back = tracker.Observe(Mission(targets: [leader, other]), Start.AddSeconds(2));
        Contains("Lock is on yellow submarine 1, not Red Leader", back.Speech ?? "", "losing it to another is said");
        Equal(false, back.PlayLockCue, "a lock on the wrong submarine is not the cue");
    }

    private static void ReloadingThenLoadedPromptsFire()
    {
        var tracker = new SubmarinePursuitTracker();
        var unlocked = Sub(0, Leader, 0, 512, 300);
        var locked = unlocked with { IsLocked = true };
        _ = tracker.Observe(Mission(targets: [unlocked], ready: 0, reloading: 4), Start);
        _ = tracker.StartPursuit();

        var reloading = tracker.Observe(Mission(targets: [locked], ready: 0, reloading: 4), Start.AddSeconds(0.5));
        Contains("Red Leader locked", reloading.Speech ?? "", "the lock is said");
        Contains("reloading", reloading.Speech ?? "", "with the empty lamps");
        Lacks("Switch", reloading.Speech ?? "", "an empty tube is not a fire prompt");
        Equal(true, reloading.PlayLockCue, "acquiring the lock takes the protected cue");

        var loaded = tracker.Observe(Mission(targets: [locked], ready: 1, reloading: 3), Start.AddSeconds(1));
        Contains("Torpedo loaded. Press Switch to fire at Red Leader", loaded.Speech ?? "",
            "a lamp lighting under the lock is the fire prompt");
        Equal(false, loaded.PlayLockCue, "the lock itself is not new");
        Equal(null, tracker.Observe(Mission(targets: [locked], ready: 1, reloading: 3), Start.AddSeconds(1.25)).Speech,
            "an unchanged state is not repeated");

        var fired = tracker.Observe(Mission(targets: [locked], ready: 0, reloading: 4), Start.AddSeconds(1.5)).Speech ?? "";
        Contains("reloading", fired, "the last loaded tube firing is said as a reload");
        Lacks("Press Switch", fired, "not as another fire prompt");

        var lost = tracker.Observe(Mission(targets: [unlocked], ready: 0, reloading: 4), Start.AddSeconds(2)).Speech ?? "";
        Contains("Lock on Red Leader lost", lost, "losing the lock is said");
    }

    private static void TheFirePromptNeedsTheSelectedTargetVisibleAndLocked()
    {
        // The leader has slipped out of view; a yellow in view holds the game's lock.
        var tracker = new SubmarinePursuitTracker();
        _ = tracker.Observe(Mission(targets: [Sub(0, Leader, 0, 512, 300)]), Start);
        _ = tracker.StartPursuit();
        var speech = tracker.Observe(
            Mission(targets: [Sub(4, Yellow, 50, 512, 200, locked: true)]),
            Start.AddSeconds(0.5)).Speech ?? "";
        Contains("Lock is on yellow submarine 1, not Red Leader", speech, "the lock in view is not the target's");
        Lacks("Press Switch", speech, "so there is no fire prompt");

        // Paused, the lamps and the lock are frozen and nothing is prompted.
        var paused = new SubmarinePursuitTracker();
        _ = paused.Observe(Mission(targets: [Sub(0, Leader, 0, 512, 300)]), Start);
        _ = paused.StartPursuit();
        Equal(default, paused.Observe(
                Mission(paused: true, targets: [Sub(0, Leader, 0, 512, 300, locked: true)]),
                Start.AddSeconds(1)),
            "a paused mission is not prompted to fire");

        // Without a pursuit or a choice of its own, the player is not lectured about
        // whatever the game happens to lock; the readout already says the lock.
        var idle = new SubmarinePursuitTracker();
        _ = idle.Observe(Mission(targets: [Sub(0, Leader, 0, 512, 300)]), Start);
        Equal(default, idle.Observe(
                Mission(targets: [Sub(0, Leader, 0, 512, 300), Sub(2, Yellow, 0, 512, 200, locked: true)]),
                Start.AddSeconds(1)),
            "an idle tracker leaves the lock to the readout");
        _ = idle.Apply(FieldNavigationAction.NextTarget);
        _ = idle.Apply(FieldNavigationAction.NextTarget);
        var chosen = idle.Observe(
            Mission(targets: [Sub(0, Leader, 0, 512, 300, locked: true), Sub(2, Yellow, 0, 512, 200)]),
            Start.AddSeconds(2));
        Contains("Red Leader locked", chosen.Speech ?? "", "a target the player chose gets its fire guidance");
    }

    private static void PauseResultSuspendResetAndUnavailableViewsStopDriving()
    {
        var tracker = new SubmarinePursuitTracker();
        var seen = Mission(targets: [Sub(1, Yellow, -300, 512, 300)]);
        _ = tracker.Observe(seen, Start);
        _ = tracker.StartPursuit();
        Equal(true, tracker.Plan.CanDrive, "a running pursuit drives");

        _ = tracker.Observe(Mission(paused: true), Start.AddSeconds(1));
        Equal(false, tracker.Plan.CanDrive, "nothing is held while paused");
        Equal(true, tracker.IsPursuing, "the pursuit resumes with the mission");
        _ = tracker.Observe(seen, Start.AddSeconds(2));
        Equal(true, tracker.Plan.CanDrive, "and drives again from a fresh view");

        _ = tracker.Observe(Mission(quitPrompt: true, paused: true), Start.AddSeconds(2.5));
        Equal(false, tracker.Plan.CanDrive, "the arcade quit prompt holds nothing (Left would be Yes)");
        _ = tracker.Observe(seen, Start.AddSeconds(2.75));

        _ = tracker.Observe(Mission(placeable: false), Start.AddSeconds(3));
        Equal(false, tracker.Plan.CanDrive, "an unreadable view drives nothing");
        _ = tracker.Observe(seen, Start.AddSeconds(4));
        Equal(true, tracker.Plan.CanDrive, "a readable one drives again");

        _ = tracker.Observe(Mission(result: true), Start.AddSeconds(5));
        Equal(false, tracker.IsPursuing, "a result ends the pursuit");
        Equal(false, tracker.Plan.CanDrive, "and holds nothing");

        tracker.Reset();
        Equal(false, tracker.IsPursuing, "a reset is not pursuing");
        Contains("No submarines", tracker.Apply(FieldNavigationAction.RepeatTarget) ?? "", "a reset forgets every sighting");

        var leaving = new SubmarinePursuitTracker();
        _ = leaving.Observe(seen, Start);
        _ = leaving.StartPursuit();
        _ = leaving.Observe(SubmarineMissionSnapshot.Inactive, Start.AddSeconds(1));
        Equal(false, leaving.IsPursuing, "leaving the mission ends the pursuit");
        Equal(false, leaving.Plan.CanDrive, "and holds nothing");
        Contains("No submarines", leaving.Apply(FieldNavigationAction.RepeatTarget) ?? "",
            "and the next run starts from nothing");
    }

    private static void ASinkingTargetIsReportedOnlyWhenSeen()
    {
        var tracker = new SubmarinePursuitTracker();
        _ = tracker.Observe(Mission(targets: [Sub(4, Yellow, 0, 512, 300)]), Start);
        _ = tracker.StartPursuit();
        var sinking = tracker.Observe(
            Mission(targets: [Sub(4, Yellow, 0, 530, 300, sinking: true)]),
            Start.AddSeconds(1)).Speech ?? "";
        Contains("yellow submarine 1 is sinking", sinking, "a hull seen going down is said");
        Equal(false, tracker.IsPursuing, "and is not pursued further");
        Lacks("yellow submarine 1", tracker.Apply(FieldNavigationAction.NextCategory) ?? "",
            "a sinking hull is not a target");

        var unseen = new SubmarinePursuitTracker();
        _ = unseen.Observe(Mission(targets: [Sub(9, Yellow, 0, 512, 3000)]), Start);
        _ = unseen.StartPursuit();
        var speech = ObserveFor(unseen, Start.AddSeconds(0.25), 20, Mission());
        foreach (var word in new[] { "sink", "sunk", "destroyed" })
        {
            Lacks(word, speech, "nothing on screen says an unseen submarine went down");
        }

        Contains("Lost yellow submarine 1", speech, "it is only lost from view");
    }

    private static void HullContactStopsAndAnnounces()
    {
        var tracker = new SubmarinePursuitTracker();
        var seen = Sub(1, Yellow, 0, 512, 300);
        _ = tracker.Observe(Mission(targets: [seen]), Start);
        _ = tracker.StartPursuit();

        var terrain = tracker.Observe(Mission(warnings: 0x1, targets: [seen]), Start.AddSeconds(0.5)).Speech ?? "";
        Lacks("terrain", terrain, "the terrain lamp is the readout's to say");
        Equal(true, tracker.IsPursuing, "and does not stop the pursuit");

        var hit = tracker.Observe(Mission(warnings: 0x2, targets: [seen]), Start.AddSeconds(1)).Speech ?? "";
        Contains("Hull contact", hit, "a collision is said");
        Contains("Pursuit stopped", hit, "and stops the pursuit rather than pushing on blind");
        Equal(false, tracker.IsPursuing, "the pursuit is over");
        Equal(false, tracker.Plan.CanDrive, "nothing is held");
    }

    private static void AimRemindersAreBounded()
    {
        var tracker = new SubmarinePursuitTracker();
        var target = Sub(1, Yellow, -300, 512, 300);
        _ = tracker.Observe(Mission(targets: [target]), Start);
        _ = tracker.StartPursuit();

        var spoken = new List<(double At, string Text)>();
        for (var seconds = 0.25; seconds <= 20.001; seconds += 0.25)
        {
            if (tracker.Observe(Mission(targets: [target]), Start.AddSeconds(seconds)).Speech is { } text)
            {
                spoken.Add((seconds, text));
            }
        }

        Equal(true, spoken.Count >= 3, $"reminders do come: {spoken.Count}");
        for (var index = 1; index < spoken.Count; index++)
        {
            Equal(true, spoken[index].At - spoken[index - 1].At >= SubmarinePursuitTracker.AimReminderInterval.TotalSeconds - 0.001,
                $"never closer than the interval: {spoken[index - 1].At} then {spoken[index].At}");
        }

        Contains("yellow submarine 1", spoken[0].Text, "a reminder names the target");
        Contains("left", spoken[0].Text, "and which way it is");
    }

    private static void NothingExactIsEverSpoken()
    {
        var tracker = new SubmarinePursuitTracker();
        var target = Sub(2, Yellow, 1234, 512 - 77, 987);
        var spoken = new StringBuilder();
        _ = tracker.Observe(Mission(x: 4321, z: 666, targets: [target]), Start);
        spoken.Append(tracker.StartPursuit()).Append('|');
        spoken.Append(tracker.DescribeStatus()).Append('|');
        spoken.Append(tracker.Apply(FieldNavigationAction.RepeatTarget)).Append('|');
        spoken.Append(tracker.Apply(FieldNavigationAction.NextCategory)).Append('|');
        spoken.Append(ObserveFor(tracker, Start.AddSeconds(0.25), 10, Mission(x: 4321, z: 666)));
        spoken.Append(tracker.DescribeStatus());
        var text = spoken.ToString();
        foreach (var forbidden in new[] { "1234", "987", "4321", "666", "435", "77", "percent", "health", "units", "distance" })
        {
            Lacks(forbidden, text, "coordinates, distances and health are never spoken");
        }
    }

    private static void StatusDescribesPursuitAndLock()
    {
        var tracker = new SubmarinePursuitTracker();
        Contains("No submarines", tracker.DescribeStatus(), "nothing seen yet");
        _ = tracker.Observe(Mission(ready: 2, targets: [Sub(0, Leader, 0, 512, 300, locked: true)]), Start);
        var idle = tracker.DescribeStatus();
        Contains("Pursuit off", idle, "the status says the pursuit is off");
        Contains("Red Leader", idle, "and what is selected");
        _ = tracker.StartPursuit();
        var pursuing = tracker.DescribeStatus();
        Contains("Pursuing Red Leader", pursuing, "a running pursuit is described");
        Contains("Press Switch to fire", pursuing, "with the fire guidance the lamps support");
    }

    // -- fixtures -----------------------------------------------------------------

    private static SubmarinePursuitPlan PlanFor(
        SubmarineVisibleTarget target,
        int yaw = 0,
        int pitch = 0,
        int speedIndex = 13)
    {
        var tracker = new SubmarinePursuitTracker();
        _ = tracker.Observe(Mission(targets: [target], yaw: yaw, pitch: pitch, speedIndex: speedIndex), Start);
        _ = tracker.StartPursuit();
        Equal(true, tracker.Plan.CanDrive, "a fresh pursuit with a target in view drives");
        return tracker.Plan;
    }

    /// <summary>Observes the same snapshot every quarter second, collecting what was said.</summary>
    private static string ObserveFor(
        SubmarinePursuitTracker tracker,
        DateTime from,
        double seconds,
        SubmarineMissionSnapshot snapshot)
    {
        var spoken = new StringBuilder();
        for (var elapsed = 0.0; elapsed <= seconds + 0.0001; elapsed += 0.25)
        {
            if (tracker.Observe(snapshot, from.AddSeconds(elapsed)).Speech is { } text)
            {
                spoken.Append(text).Append(' ');
            }
        }

        return spoken.ToString();
    }

    private static SubmarineMissionSnapshot Mission(
        IReadOnlyList<SubmarineVisibleTarget>? targets = null,
        bool arcade = false,
        int yaw = 0,
        int pitch = 0,
        int x = 0,
        int y = 512,
        int z = 0,
        int speedIndex = 17,
        int ready = 4,
        int reloading = 0,
        int viewMode = 0,
        int warnings = 0,
        bool paused = false,
        bool quitPrompt = false,
        bool result = false,
        bool placeable = true)
    {
        var drawn = placeable ? targets ?? Array.Empty<SubmarineVisibleTarget>() : Array.Empty<SubmarineVisibleTarget>();
        return new SubmarineMissionSnapshot(
            IsActive: true,
            IsArcade: arcade,
            IsPaused: paused,
            HasResult: result,
            Outcome: result ? SubmarineMissionOutcome.Success : SubmarineMissionOutcome.Active,
            IsQuitPromptOpen: quitPrompt,
            QuitPromptYesSelected: false,
            RemainingSeconds: 500,
            HealthPercent: 100,
            Depth: y,
            Speed: 100,
            Pitch: pitch,
            Yaw: yaw,
            ReadyTorpedoes: ready,
            ReloadingTorpedoes: reloading,
            Warnings: warnings,
            HasLockedTarget: drawn.Any(target => target.IsLocked),
            CanPlaceTargets: placeable,
            VisibleTargets: drawn,
            ViewportOriginX: 0,
            ViewportOriginY: 0,
            ViewportWidth: 320,
            ViewportHeight: 240)
        {
            PlayerX = x,
            PlayerY = y,
            PlayerZ = z,
            SpeedIndex = speedIndex,
            ViewMode = viewMode
        };
    }

    private static SubmarineVisibleTarget Sub(
        int slot,
        SubmarineTargetModel model,
        int x,
        int y,
        int z,
        bool locked = false,
        bool sinking = false) =>
        new(slot, locked, 160, 120) { Model = model, X = x, Y = y, Z = z, IsSinking = sinking };

    /// <summary>A point at a bearing (4096 a turn, 0 along +Z, growing toward +X) and range.</summary>
    private static (int X, int Z) At(int bearing, int range)
    {
        var radians = bearing * Math.PI * 2 / 4096;
        return ((int)Math.Round(Math.Sin(radians) * range), (int)Math.Round(Math.Cos(radians) * range));
    }

    private static bool IsLeft(HighwaySteeringDirection direction) =>
        direction is HighwaySteeringDirection.Left or HighwaySteeringDirection.UpLeft or HighwaySteeringDirection.DownLeft;

    private static bool IsRight(HighwaySteeringDirection direction) =>
        direction is HighwaySteeringDirection.Right or HighwaySteeringDirection.UpRight or HighwaySteeringDirection.DownRight;

    /// <summary>The part of an announcement before its first colon: who it is about.</summary>
    private static string Subject(string announcement)
    {
        var colon = announcement.IndexOf(':');
        return colon < 0 ? announcement : announcement[..colon];
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>Case-insensitive: a name that starts a sentence is capitalised there.</summary>
    private static void Contains(string expected, string actual, string label)
    {
        if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{label}: expected '{expected}' in '{actual}'");
        }
    }

    private static void Lacks(string unexpected, string actual, string label)
    {
        if (actual.Contains(unexpected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{label}: did not expect '{unexpected}' in '{actual}'");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }
}

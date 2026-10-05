using Ff7.Accessibility.Core;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// What the host should hold this tick. Nothing is held unless <see cref="CanDrive"/>.
/// <see cref="Direction"/> combines a turn (Left/Right) with a pitch (Up dives, Down
/// climbs); <see cref="Accelerate"/> is the Menu action and <see cref="Brake"/> the
/// Cancel action of 798580. Fire is never part of a plan.
/// </summary>
internal readonly record struct SubmarinePursuitPlan(
    bool CanDrive,
    HighwaySteeringDirection Direction,
    bool Accelerate,
    bool Brake)
{
    internal static SubmarinePursuitPlan Idle { get; } =
        new(false, HighwaySteeringDirection.None, false, false);
}

/// <summary>
/// Chooses a submarine the player can see, steers toward it with the mission's own turn,
/// pitch and throttle actions, and says when the game's lock and loaded lamps make a shot
/// possible. It owns no input and writes nothing: the host reads <see cref="Plan"/> and
/// presses the ordinary actions itself.
///
/// <para><b>Only what is drawn.</b> A submarine is known only from a sighting: a target
/// the reader reports because the game is drawing it inside the viewport. Its position is
/// remembered from that sighting and never updated from anything else, so a submarine
/// out of view is steered toward where it was seen - the way a sighted player turns back
/// toward where they last saw it - and is forgotten after <see cref="RememberFor"/> of
/// mission time. Nothing out of view is ever called destroyed; a hull is only said to be
/// sinking while the game draws it going down.</para>
///
/// <para><b>Steering</b> (798580; 4096 units a turn). Yaw grows toward +X with Right at 8
/// a frame; a larger pitch is nose up and Down adds 4 a frame, so a target above needs
/// Down. The deadbands are wider than one 30 ms worker tick of turning at 60 frames a
/// second (about 14 units of yaw, 7 of pitch), so a held key is released before it can
/// carry the nose past the target. The throttle is the 9873D8 step: 9 stopped, 17 full
/// ahead, 18 the boost that lasts only while Menu is held at 17. Lined up and outside the
/// lock range it boosts, toward a seen point as well, because the story's leader outruns
/// full ahead; in sharp turns it slows, because the turning circle grows with speed; inside
/// the lock range it holds station by whether the target is closing or opening. It never
/// brakes below step 12, so it never backs the submarine astern. In the overview a reached
/// seen point holds the course until the next sweep rather than turning back to it.</para>
///
/// <para><b>Firing</b> is the player's. 78E2F8 locks the nearest marked contact inside a
/// 51 degree cone ahead and never from the overview; 79F114 fires only at a lock. The
/// prompt is given only when the selected submarine is in view carrying the native lock
/// marker and a torpedo lamp is lit; a lock on any other submarine is said as such. The
/// free torpedo object 79F114 also needs is not shown anywhere, so the prompt describes
/// the lamps rather than promising a launch.</para>
/// </summary>
internal sealed class SubmarinePursuitTracker
{
    /// <summary>How long a sighting is steered toward, in mission time (a pause does not count).</summary>
    internal static readonly TimeSpan RememberFor = TimeSpan.FromSeconds(15);

    /// <summary>A target this long out of view in the normal camera is said to be out of view.</summary>
    internal static readonly TimeSpan OutOfViewGrace = TimeSpan.FromSeconds(1.5);

    /// <summary>The fastest a pursuit repeats where its target is.</summary>
    internal static readonly TimeSpan AimReminderInterval = TimeSpan.FromSeconds(4);

    internal const int YawDeadband = 32;
    internal const int PitchDeadband = 16;

    /// <summary>
    /// A seen point this close has been reached without finding the target. It is about
    /// the tight turning circle's diameter (61 units of radius at step 13): a point inside
    /// it can be circled but not driven onto, and it was only where the target was.
    /// </summary>
    internal const int ReachedRadius = 96;

    private const int FullTurn = 4096;
    private const int HalfTurn = 2048;

    // 798580 clamps the pitch word to this.
    private const int PitchLimit = 0x3F0;

    // Below this the bearing of a point almost directly above or below is noise.
    private const int MinimumSteerableHorizontal = 12;

    private const int SpeedIndexMaximum = 18;
    private const int SpeedIndexBoost = 18;
    private const int SpeedIndexFull = 17;
    private const int SpeedIndexTurning = 15;
    private const int SpeedIndexCloseIn = 13;
    private const int SpeedIndexSlowest = 12;

    // Holding range inside the 512 lock range, for a target in view and lined up.
    private const int HoldNear = 192;
    private const int HoldMid = 272;
    private const int HoldFar = 352;
    private const double ClosingRateSampleSeconds = 0.2;
    private const double MaximumClosingRateGapSeconds = 1.5;

    // Units a second of range change treated as holding steady (a third of a unit a frame).
    private const double ClosingRateDeadband = 20;

    // Menu at 16 reaches 17 and, held, the boost; matching a slower hull stays below it.
    private const int SpeedIndexMatchingCeiling = 16;

    // Two closing-rate samples: long enough for the rate to show the last step's change.
    private static readonly TimeSpan MatchAdjustInterval = TimeSpan.FromSeconds(0.5);

    // A sighting this high or low of the submarine's own level is said to be above or below.
    private const int VerticalWordsElevation = 170;

    // Throttle bands by range: close in, holding, approaching, far. A band is left only
    // once the range is clearly past its edge, so a target drifting across one does not
    // flip Menu and Cancel every tick.
    private static readonly int[] RangeBandLimits = [160, 384, 768];
    private const int RangeBandHysteresis = 32;

    // The normal views' sonar and lock reach 512 (98724C/9873F8). An overview sighting
    // inside this is said to be close; it stops being close only past 512.
    private const int OverviewCloseRange = 448;
    private const int LockRange = 512;

    private readonly object sync = new();
    private readonly Dictionary<int, Contact> contacts = [];
    private readonly Dictionary<(int Slot, SubmarineTargetModel Model), (string Name, int Order)> identities = [];
    private readonly Dictionary<SubmarineTargetModel, int> ordinals = [];

    private bool isPursuing;
    private SubmarinePursuitPlan plan = SubmarinePursuitPlan.Idle;
    private SubmarineMissionSnapshot? current;
    private int? selectedSlot;
    private bool selectionExplicit;
    private bool missionSeen;
    private bool anySighting;
    private bool isArcade;
    private int nextOrder;
    private TimeSpan missionClock;
    private DateTime? clockBaseline;
    private bool clockWasPaused;
    private DateTime lastNow;
    private DateTime lastSpokeAt = DateTime.MinValue;
    private FireState fireState = FireState.Unknown;
    private int lockedOtherSlot = -1;
    private bool outOfViewAnnounced;
    private int rangeBand = -1;

    // Overview only: the last sighting's point has been reached, and the course is held
    // until the next sweep draws the target again.
    private bool holdingCourse;

    private bool hasGoalRange;
    private double lastGoalRange;
    private TimeSpan lastGoalRangeAt;
    private double? closingRate;
    private bool boostLatched;
    private TimeSpan? lastMatchAdjustAt;

    private enum FireState
    {
        Unknown,
        None,
        Overview,
        OverviewClose,
        OtherLocked,
        LockedReloading,
        LockedReady
    }

    private sealed class Contact(int slot, SubmarineTargetModel model, string name, int order)
    {
        internal int Slot { get; } = slot;
        internal SubmarineTargetModel Model { get; } = model;
        internal string Name { get; } = name;
        internal int Order { get; } = order;
        internal int X { get; set; }
        internal int Y { get; set; }
        internal int Z { get; set; }
        internal TimeSpan LastSeen { get; set; }
        internal bool Visible { get; set; }
        internal bool Locked { get; set; }
        internal bool Sinking { get; set; }
        internal bool SinkingSaid { get; set; }
    }

    internal bool IsPursuing
    {
        get
        {
            lock (sync)
            {
                return isPursuing;
            }
        }
    }

    internal SubmarinePursuitPlan Plan
    {
        get
        {
            lock (sync)
            {
                return plan;
            }
        }
    }

    internal SubmarineMissionCue Observe(SubmarineMissionSnapshot snapshot, DateTime now)
    {
        lock (sync)
        {
            lastNow = now;
            if (!snapshot.IsActive)
            {
                if (missionSeen || isPursuing || contacts.Count != 0)
                {
                    ResetCore();
                }

                return default;
            }

            missionSeen = true;
            isArcade = snapshot.IsArcade;

            // The banner is the readout's to say; the pursuit simply ends with the mission.
            if (snapshot.HasResult)
            {
                StopCore();
                DropView();
                return default;
            }

            // Paused (the arcade's quit prompt only opens while paused, and Left there is
            // Yes) or a view that could not be read: nothing is driven, and nothing about
            // the sea is assumed until a coherent view comes back. Only the pause stops
            // the clock sightings age by; behind an unreadable view the mission runs on.
            var paused = snapshot.IsPaused || snapshot.IsQuitPromptOpen;
            AdvanceClock(now, paused);
            if (paused || !snapshot.CanPlaceTargets)
            {
                DropView();
                return default;
            }

            current = snapshot;

            var spoken = new List<string>(4);
            var lockCue = false;
            UpdateContacts(snapshot);
            HandleSinking(spoken);
            ExpireContacts(spoken);
            ChooseDefault(spoken);
            if (isPursuing)
            {
                CheckStops(snapshot, spoken);
            }

            plan = ComputePlan();
            if (isPursuing || selectionExplicit)
            {
                ReportFireState(snapshot, spoken, ref lockCue);
            }

            if (isPursuing)
            {
                ReportVisibility(snapshot, spoken);
                if (spoken.Count == 0)
                {
                    RemindAim(snapshot, spoken, now);
                }
            }

            if (spoken.Count == 0 && !lockCue)
            {
                return default;
            }

            if (spoken.Count != 0)
            {
                lastSpokeAt = now;
            }

            return new SubmarineMissionCue(spoken.Count == 0 ? null : string.Join(" ", spoken), lockCue);
        }
    }

    internal string? Apply(FieldNavigationAction action)
    {
        lock (sync)
        {
            var text = action switch
            {
                FieldNavigationAction.PreviousTarget => Cycle(-1),
                FieldNavigationAction.NextTarget => Cycle(1),
                FieldNavigationAction.RepeatTarget => DescribeSelection(),
                FieldNavigationAction.PreviousCategory or FieldNavigationAction.NextCategory => DescribeList(),
                _ => null
            };
            plan = ComputePlan();
            if (text is not null)
            {
                lastSpokeAt = lastNow;
            }

            return text;
        }
    }

    internal string StartPursuit()
    {
        lock (sync)
        {
            ChooseDefault(spoken: null);
            var target = Selected();
            if (target is null)
            {
                return Spoken(NoSubmarines());
            }

            if (isPursuing)
            {
                return Spoken($"Already pursuing {Describe(target)}.");
            }

            isPursuing = true;
            outOfViewAnnounced = false;
            rangeBand = -1;
            holdingCourse = false;
            ForgetClosingRate();
            plan = ComputePlan();
            var text = current is null
                ? $"Pursuing {Describe(target)}."
                : $"Pursuing {Describe(target)}: {Where(target, current.Value)}.";
            if (current is { } snapshot)
            {
                var state = FireStateOf(target, snapshot, out var otherSlot);
                fireState = state;
                lockedOtherSlot = otherSlot;
                if (FireSentence(state, target, otherSlot, starting: true) is { } fire)
                {
                    text = $"{text} {fire}";
                }
            }
            else
            {
                fireState = FireState.Unknown;
                lockedOtherSlot = -1;
            }

            return Spoken(text);
        }
    }

    internal string TogglePursuit()
    {
        lock (sync)
        {
            return isPursuing ? StopPursuit() : StartPursuit();
        }
    }

    internal string StopPursuit()
    {
        lock (sync)
        {
            if (!isPursuing)
            {
                return Spoken("Pursuit is not running.");
            }

            StopCore();
            return Spoken("Pursuit stopped.");
        }
    }

    /// <summary>
    /// Drops the current view and every key, and forgets what the lock was, so nothing is
    /// driven or announced from a picture of the sea that may be stale. The selection, the
    /// sightings and whether the player wants a pursuit are kept.
    /// </summary>
    internal void Suspend()
    {
        lock (sync)
        {
            DropView();
        }
    }

    internal void Reset()
    {
        lock (sync)
        {
            ResetCore();
        }
    }

    internal string DescribeStatus()
    {
        lock (sync)
        {
            ChooseDefault(spoken: null);
            var target = Selected();
            if (target is null)
            {
                return Spoken(NoSubmarines());
            }

            var where = current is { } snapshot ? $": {Where(target, snapshot)}" : string.Empty;
            var text = isPursuing
                ? $"Pursuing {Describe(target)}{where}."
                : $"Pursuit off. Selected {Describe(target)}{where}.";
            if (current is { } view &&
                FireSentence(FireStateOf(target, view, out var otherSlot), target, otherSlot, starting: true) is { } fire)
            {
                text = $"{text} {fire}";
            }

            return Spoken(text);
        }
    }

    // -- observation --------------------------------------------------------------

    /// <summary>
    /// Mission time: wall time between observations, except across a pause. 77DF72 stops
    /// the mission's own clock and every update while paused, so a sighting does not age
    /// then. Everything else - a suspension, focus, a menu, an unreadable frame - leaves
    /// the game running, and is counted, so no interruption can keep a sighting alive.
    /// </summary>
    private void AdvanceClock(DateTime now, bool paused)
    {
        if (clockBaseline is { } baseline && now > baseline && !clockWasPaused && !paused)
        {
            missionClock += now - baseline;
        }

        clockBaseline = now;
        clockWasPaused = paused;
    }

    private void UpdateContacts(SubmarineMissionSnapshot snapshot)
    {
        var seen = new HashSet<int>();
        foreach (var target in snapshot.VisibleTargets)
        {
            if (target.Slot is < 0 or >= SubmarineMissionStateReader.EnemyRecordCount)
            {
                continue;
            }

            if (!contacts.TryGetValue(target.Slot, out var contact) || contact.Model != target.Model)
            {
                // A hull first seen already going down was never a target to choose.
                if (target.IsSinking)
                {
                    contacts.Remove(target.Slot);
                    continue;
                }

                var identity = IdentityFor(target.Slot, target.Model);
                contact = new Contact(target.Slot, target.Model, identity.Name, identity.Order);
                contacts[target.Slot] = contact;
                anySighting = true;
            }

            seen.Add(target.Slot);
            contact.Visible = true;
            contact.Locked = target.IsLocked;
            contact.X = target.X;
            contact.Y = target.Y;
            contact.Z = target.Z;
            contact.LastSeen = missionClock;
            contact.Sinking |= target.IsSinking;
        }

        foreach (var contact in contacts.Values)
        {
            if (!seen.Contains(contact.Slot))
            {
                contact.Visible = false;
                contact.Locked = false;
            }
        }
    }

    private (string Name, int Order) IdentityFor(int slot, SubmarineTargetModel model)
    {
        if (identities.TryGetValue((slot, model), out var known))
        {
            return known;
        }

        var ordinal = ordinals.GetValueOrDefault(model) + 1;
        ordinals[model] = ordinal;
        var name = model switch
        {
            SubmarineTargetModel.RedLeader => ordinal == 1 ? "Red Leader" : $"Red Leader {ordinal}",
            SubmarineTargetModel.Red => $"red submarine {ordinal}",
            SubmarineTargetModel.Yellow => $"yellow submarine {ordinal}",
            _ => $"submarine {ordinal}"
        };
        var identity = (name, nextOrder++);
        identities[(slot, model)] = identity;
        return identity;
    }

    private void HandleSinking(List<string> spoken)
    {
        foreach (var contact in contacts.Values.Where(contact => contact.Sinking).ToList())
        {
            if (!contact.SinkingSaid && contact.Visible)
            {
                contact.SinkingSaid = true;
                if (contact.Slot == selectedSlot)
                {
                    if (isPursuing)
                    {
                        spoken.Add($"{Capitalised(contact.Name)} is sinking. Pursuit stopped.");
                        StopCore();
                    }
                    else if (selectionExplicit)
                    {
                        spoken.Add($"{Capitalised(contact.Name)} is sinking.");
                    }

                    selectedSlot = null;
                    selectionExplicit = false;
                }
            }

            if (!contact.Visible)
            {
                contacts.Remove(contact.Slot);
            }
        }
    }

    private void ExpireContacts(List<string> spoken)
    {
        foreach (var contact in contacts.Values
                     .Where(contact => !contact.Visible && missionClock - contact.LastSeen > RememberFor)
                     .ToList())
        {
            contacts.Remove(contact.Slot);
            if (contact.Slot != selectedSlot)
            {
                continue;
            }

            if (isPursuing)
            {
                spoken.Add($"Lost {contact.Name}. Pursuit stopped.{OverviewHint()}");
                StopCore();
            }

            selectedSlot = null;
            selectionExplicit = false;
        }
    }

    /// <summary>
    /// The leader once it has been seen, unless the player chose; otherwise whatever was
    /// already chosen, or the first sighting. A pursuit that changes target says so.
    /// </summary>
    private void ChooseDefault(List<string>? spoken)
    {
        var selected = Selected();
        if (selected is null)
        {
            selectedSlot = null;
            selectionExplicit = false;
        }

        if (selectionExplicit)
        {
            return;
        }

        var choice = Selectable().Where(contact => contact.Model == SubmarineTargetModel.RedLeader)
                         .OrderBy(contact => contact.Order).FirstOrDefault() ??
                     selected ??
                     Selectable().OrderBy(contact => contact.Order).FirstOrDefault();
        if (choice?.Slot == selectedSlot)
        {
            return;
        }

        var previous = selectedSlot;
        selectedSlot = choice?.Slot;
        fireState = FireState.Unknown;
        lockedOtherSlot = -1;
        outOfViewAnnounced = false;
        rangeBand = -1;
        holdingCourse = false;
        ForgetClosingRate();
        if (spoken is not null && isPursuing && choice is not null && previous is not null)
        {
            spoken.Add($"Now pursuing {Describe(choice)}.");
        }
    }

    private void CheckStops(SubmarineMissionSnapshot snapshot, List<string> spoken)
    {
        var target = Selected();
        if (target is null)
        {
            spoken.Add($"Nothing left to pursue. Pursuit stopped.{OverviewHint()}");
            StopCore();
            return;
        }

        // 7991C6 sets this when the hull is stopped by the sea bed or a wall. Nothing here
        // knows the terrain, so the pursuit stops rather than pushing on into it.
        if (snapshot.HullContact)
        {
            spoken.Add("Hull contact. Pursuit stopped.");
            StopCore();
            return;
        }

        if (target.Visible)
        {
            holdingCourse = false;
            return;
        }

        if (!holdingCourse && Range(target, snapshot) > ReachedRadius)
        {
            return;
        }

        // The overview's next sweep is seconds away, and the leader outruns full ahead:
        // turning back to a point it has left, or stopping there, would never catch it.
        // The course is held instead, and the sighting is still forgotten on time. Just
        // after a sighting the normal view's own sweep (64 frames) gets the same chance.
        var recentlySeen = missionClock - target.LastSeen < OutOfViewGrace;
        if (snapshot.IsOverview || recentlySeen)
        {
            if (!holdingCourse)
            {
                holdingCourse = true;
                if (snapshot.IsOverview)
                {
                    spoken.Add($"Reached where {target.Name} was last seen. Holding course for the next sweep.");
                }
            }

            return;
        }

        // In the normal view there is nothing more to go on. The sighting has been
        // searched, so it is let go rather than offered again.
        spoken.Add($"Reached where {target.Name} was last seen, but it is not in view. Pursuit stopped.{OverviewHint()}");
        contacts.Remove(target.Slot);
        selectedSlot = null;
        selectionExplicit = false;
        StopCore();
    }

    private void ReportFireState(SubmarineMissionSnapshot snapshot, List<string> spoken, ref bool lockCue)
    {
        var target = Selected();
        if (target is null)
        {
            return;
        }

        var state = FireStateOf(target, snapshot, out var otherSlot);
        if (state == fireState && (state != FireState.OtherLocked || otherSlot == lockedOtherSlot))
        {
            return;
        }

        var previous = fireState;
        fireState = state;
        lockedOtherSlot = otherSlot;
        var wasLocked = previous is FireState.LockedReady or FireState.LockedReloading;
        switch (state)
        {
            case FireState.Overview or FireState.OverviewClose when !isPursuing:
                return;
            case FireState.Overview when previous == FireState.OverviewClose:
                return;
            case FireState.LockedReady when previous == FireState.LockedReloading:
                spoken.Add($"Torpedo loaded. Press Switch to fire at {target.Name}.");
                return;
            case FireState.LockedReloading when previous == FireState.LockedReady:
                spoken.Add($"{Capitalised(target.Name)} still locked. Torpedoes reloading.");
                return;
            case FireState.None:
                if (wasLocked)
                {
                    spoken.Add($"Lock on {target.Name} lost.");
                }

                return;
        }

        if (FireSentence(state, target, otherSlot, starting: false) is { } sentence)
        {
            spoken.Add(sentence);
        }

        lockCue |= !wasLocked && state is FireState.LockedReady or FireState.LockedReloading;
    }

    private void ReportVisibility(SubmarineMissionSnapshot snapshot, List<string> spoken)
    {
        var target = Selected();
        if (target is null)
        {
            return;
        }

        if (target.Visible)
        {
            if (outOfViewAnnounced)
            {
                spoken.Add($"{Capitalised(target.Name)} back in view.");
                outOfViewAnnounced = false;
            }

            return;
        }

        // In the overview a sweep that has passed is expected, not news.
        if (!outOfViewAnnounced && !snapshot.IsOverview && missionClock - target.LastSeen >= OutOfViewGrace)
        {
            spoken.Add($"{Capitalised(target.Name)} out of view. Heading for where it was last seen.");
            outOfViewAnnounced = true;
        }
    }

    private void RemindAim(SubmarineMissionSnapshot snapshot, List<string> spoken, DateTime now)
    {
        if (now - lastSpokeAt < AimReminderInterval || fireState == FireState.LockedReady)
        {
            return;
        }

        if (Selected() is not { } target)
        {
            return;
        }

        spoken.Add(holdingCourse && !target.Visible
            ? $"{Capitalised(target.Name)}: {LastSeen(target)}. Holding course for the next sweep."
            : $"{Capitalised(target.Name)}: {Where(target, snapshot)}.");
    }

    // -- steering -----------------------------------------------------------------

    private SubmarinePursuitPlan ComputePlan()
    {
        if (!isPursuing || current is not { } snapshot || Selected() is not { } target)
        {
            return SubmarinePursuitPlan.Idle;
        }

        if (holdingCourse && !target.Visible)
        {
            // Straight on, keeping full ahead or the boost if that is what it was making.
            return new SubmarinePursuitPlan(
                true,
                HighwaySteeringDirection.None,
                snapshot.SpeedIndex is >= SpeedIndexFull and <= SpeedIndexMaximum,
                false);
        }

        var dx = target.X - snapshot.PlayerX;
        var dy = target.Y - snapshot.PlayerY;
        var dz = target.Z - snapshot.PlayerZ;
        var horizontal = Math.Sqrt((double)dx * dx + (double)dz * dz);
        var range = Math.Sqrt(horizontal * horizontal + (double)dy * dy);
        var yawError = horizontal >= MinimumSteerableHorizontal
            ? WrapAngle(BearingOf(dx, dz) - snapshot.Yaw)
            : 0;
        var pitchError = Math.Clamp(ElevationOf(dy, horizontal), -PitchLimit, PitchLimit) - snapshot.Pitch;

        var turn = yawError > YawDeadband ? 1 : yawError < -YawDeadband ? -1 : 0;
        var climb = pitchError > PitchDeadband ? 1 : pitchError < -PitchDeadband ? -1 : 0;

        // A seen point is where the target was; it has kept moving since. Only a target in
        // view is slowed for by range, so a fleeing one is not handed back the gap.
        var absoluteYawError = Math.Abs(yawError);
        bool accelerate;
        bool brake;
        if (target.Visible)
        {
            (accelerate, brake) = VisibleThrottle(snapshot.SpeedIndex, range, absoluteYawError);
        }
        else
        {
            ForgetClosingRate();
            (accelerate, brake) = Throttle(
                snapshot.SpeedIndex,
                DesiredSpeedIndex(RangeBandLimits.Length, absoluteYawError, range));
        }

        return new SubmarinePursuitPlan(true, Combine(turn, climb), accelerate, brake);
    }

    /// <summary>
    /// For a target in view. Turning, approaching or with no closing rate yet, the step
    /// comes from the range band. Lined up inside the holding range it follows whether the
    /// target is closing or opening instead, so neither a leader that outruns full ahead
    /// nor a slow hull makes Menu and Cancel alternate (the offline simulation measured
    /// both):
    /// <list type="bullet">
    /// <item>past <see cref="HoldFar"/> and not closing, the boost is taken and kept to
    /// <see cref="HoldMid"/>;</item>
    /// <item>inside <see cref="HoldNear"/> and still closing, Cancel is held;</item>
    /// <item>between, at step 16 or below, Cancel while closing and Menu while opening
    /// match a slower hull's speed. Full ahead or the boost is never braked there, because
    /// a target that outruns it is held by short boosts, not by slowing down;</item>
    /// <item>never below step 12, and a step slower than that is brought forward.</item>
    /// </list>
    /// </summary>
    private (bool Accelerate, bool Brake) VisibleThrottle(int index, double range, int absoluteYawError)
    {
        var band = RangeBandFor(range);
        SampleClosingRate(range);
        if (index is < 0 or > SpeedIndexMaximum)
        {
            boostLatched = false;
            return (false, false);
        }

        if (absoluteYawError > FullTurn / 8 || band >= 2 || closingRate is not { } rate)
        {
            boostLatched = false;
            return Throttle(index, DesiredSpeedIndex(band, absoluteYawError, range));
        }

        if ((boostLatched && range >= HoldMid) || (range > HoldFar && rate <= ClosingRateDeadband))
        {
            boostLatched = true;
            return (true, false);
        }

        boostLatched = false;
        var canBrake = index > SpeedIndexSlowest;
        if (range < HoldNear && rate > 0)
        {
            return (false, canBrake);
        }

        // Matching moves one step at a time and then waits for the closing rate to show
        // the change: a tick moves the step faster than the rate is measured, and acting
        // on a rate from before the last change only overshoots.
        var matchDue = lastMatchAdjustAt is not { } last || missionClock - last >= MatchAdjustInterval;
        if (index <= SpeedIndexMatchingCeiling && range < HoldMid && rate > ClosingRateDeadband)
        {
            return matchDue && canBrake ? MatchAdjusted(false, true) : (false, false);
        }

        if (index < SpeedIndexMatchingCeiling && range > HoldNear && rate < -ClosingRateDeadband)
        {
            return matchDue ? MatchAdjusted(true, false) : (false, false);
        }

        return (index < SpeedIndexSlowest, false);
    }

    private (bool Accelerate, bool Brake) MatchAdjusted(bool accelerate, bool brake)
    {
        lastMatchAdjustAt = missionClock;
        return (accelerate, brake);
    }

    /// <summary>
    /// How fast the range to the target in view is shrinking, in units a second of mission
    /// time, from the sightings themselves. A gap in the sightings starts it again.
    /// </summary>
    private void SampleClosingRate(double range)
    {
        var elapsed = hasGoalRange ? (missionClock - lastGoalRangeAt).TotalSeconds : double.MaxValue;
        if (!hasGoalRange || elapsed > MaximumClosingRateGapSeconds)
        {
            ForgetClosingRate();
            hasGoalRange = true;
            lastGoalRange = range;
            lastGoalRangeAt = missionClock;
            return;
        }

        if (elapsed < ClosingRateSampleSeconds)
        {
            return;
        }

        var sample = (lastGoalRange - range) / elapsed;
        closingRate = closingRate is { } previous ? (previous + sample) / 2 : sample;
        lastGoalRange = range;
        lastGoalRangeAt = missionClock;
    }

    private void ForgetClosingRate()
    {
        hasGoalRange = false;
        closingRate = null;
        boostLatched = false;
        lastMatchAdjustAt = null;
    }

    /// <summary>
    /// 0 close in (to 160), 1 holding (to 384, inside the 512 lock range), 2 approaching
    /// (to 768), 3 far. Moving out a band needs the range past its edge by the hysteresis,
    /// and moving in needs it that far inside.
    /// </summary>
    private int RangeBandFor(double range)
    {
        var band = rangeBand;
        if (band < 0)
        {
            band = RangeBandLimits.Count(limit => range > limit);
        }
        else
        {
            while (band < RangeBandLimits.Length && range > RangeBandLimits[band] + RangeBandHysteresis)
            {
                band++;
            }

            while (band > 0 && range < RangeBandLimits[band - 1] - RangeBandHysteresis)
            {
                band--;
            }
        }

        rangeBand = band;
        return band;
    }

    /// <summary>
    /// The throttle step wanted for this band and bearing. A turn is sized by the real
    /// range, because a point nearer than the turning circle (138 units of radius at step
    /// 15, 61 at step 13) is circled rather than reached. Within the lock range a slower
    /// submarine turns inside a target it would otherwise circle past. Outside it, lined
    /// up, it takes the boost: the story's leader runs at 14000, faster than full ahead's
    /// 12288, so anything less would never close to lock range.
    /// </summary>
    private static int DesiredSpeedIndex(int band, int absoluteYawError, double range)
    {
        if (absoluteYawError > FullTurn / 4)
        {
            return SpeedIndexCloseIn;
        }

        if (absoluteYawError > FullTurn / 8)
        {
            return range > LockRange ? SpeedIndexTurning : SpeedIndexCloseIn;
        }

        return band switch
        {
            >= 2 => absoluteYawError <= FullTurn / 16 ? SpeedIndexBoost : SpeedIndexFull,
            1 => SpeedIndexTurning,
            _ => SpeedIndexCloseIn
        };
    }

    /// <summary>
    /// Menu raises the step one a frame while held, Cancel lowers it. One step over is
    /// left alone so the two never alternate, and nothing is done with a word outside the
    /// table 798580 indexes.
    /// </summary>
    private static (bool Accelerate, bool Brake) Throttle(int index, int desired)
    {
        if (index is < 0 or > SpeedIndexMaximum)
        {
            return (false, false);
        }

        if (desired >= SpeedIndexBoost || index < desired)
        {
            return (true, false);
        }

        return index > desired + 1 ? (false, true) : (false, false);
    }

    private static HighwaySteeringDirection Combine(int turn, int climb) => (turn, climb) switch
    {
        (1, 1) => HighwaySteeringDirection.DownRight,
        (1, -1) => HighwaySteeringDirection.UpRight,
        (-1, 1) => HighwaySteeringDirection.DownLeft,
        (-1, -1) => HighwaySteeringDirection.UpLeft,
        (1, _) => HighwaySteeringDirection.Right,
        (-1, _) => HighwaySteeringDirection.Left,
        (_, 1) => HighwaySteeringDirection.Down,
        (_, -1) => HighwaySteeringDirection.Up,
        _ => HighwaySteeringDirection.None
    };

    /// <summary>799969's forward vector is (sin yaw, -, cos yaw), so yaw 0 looks along +Z.</summary>
    private static int BearingOf(int dx, int dz) =>
        (int)Math.Round(Math.Atan2(dx, dz) * FullTurn / (2 * Math.PI));

    /// <summary>Depth grows downward, so a target with a smaller Y is above: positive pitch.</summary>
    private static int ElevationOf(int dy, double horizontal) =>
        (int)Math.Round(Math.Atan2(-dy, horizontal) * FullTurn / (2 * Math.PI));

    private static int WrapAngle(int angle) =>
        ((angle + HalfTurn) % FullTurn + FullTurn) % FullTurn - HalfTurn;

    private static double Range(Contact target, SubmarineMissionSnapshot snapshot)
    {
        double dx = target.X - snapshot.PlayerX;
        double dy = target.Y - snapshot.PlayerY;
        double dz = target.Z - snapshot.PlayerZ;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    // -- lock and fire ------------------------------------------------------------

    private FireState FireStateOf(Contact target, SubmarineMissionSnapshot snapshot, out int otherSlot)
    {
        otherSlot = -1;
        if (snapshot.IsOverview)
        {
            // Between sweeps the overview stops drawing it; that says nothing about range.
            if (!target.Visible)
            {
                return fireState == FireState.OverviewClose ? FireState.OverviewClose : FireState.Overview;
            }

            var limit = fireState == FireState.OverviewClose ? LockRange : OverviewCloseRange;
            return Range(target, snapshot) <= limit ? FireState.OverviewClose : FireState.Overview;
        }

        if (target.Visible && target.Locked)
        {
            return snapshot.ReadyTorpedoes > 0 ? FireState.LockedReady : FireState.LockedReloading;
        }

        if (contacts.Values.FirstOrDefault(contact =>
                contact.Visible && contact.Locked && contact.Slot != target.Slot) is { } other)
        {
            otherSlot = other.Slot;
            return FireState.OtherLocked;
        }

        return FireState.None;
    }

    private string? FireSentence(FireState state, Contact target, int otherSlot, bool starting) => state switch
    {
        FireState.Overview => starting
            ? "Torpedoes cannot lock in the overview. PageDown returns to the normal view."
            : "Overview: torpedoes cannot lock here. PageDown returns to the normal view.",
        FireState.OverviewClose =>
            $"{Capitalised(target.Name)} is close. PageDown returns to the normal view, where torpedoes can lock.",
        FireState.LockedReady => $"{Capitalised(target.Name)} locked with a torpedo loaded. Press Switch to fire.",
        FireState.LockedReloading => $"{Capitalised(target.Name)} locked. Torpedoes reloading.",
        FireState.OtherLocked when contacts.TryGetValue(otherSlot, out var other) =>
            $"Lock is on {other.Name}, not {target.Name}.",
        _ => null
    };

    // -- speech -------------------------------------------------------------------

    private string Cycle(int step)
    {
        ChooseDefault(spoken: null);
        var list = Selectable().OrderBy(contact => contact.Order).ToList();
        if (list.Count == 0)
        {
            return NoSubmarines();
        }

        var index = list.FindIndex(contact => contact.Slot == selectedSlot);
        index = index < 0
            ? step > 0 ? 0 : list.Count - 1
            : (index + step + list.Count) % list.Count;
        var chosen = list[index];
        if (chosen.Slot != selectedSlot)
        {
            fireState = FireState.Unknown;
            lockedOtherSlot = -1;
            outOfViewAnnounced = false;
            rangeBand = -1;
            holdingCourse = false;
            ForgetClosingRate();
        }

        selectedSlot = chosen.Slot;
        selectionExplicit = true;
        var where = current is { } snapshot ? Where(chosen, snapshot) : chosen.Visible ? "in view" : LastSeen(chosen);
        var text = $"{Describe(chosen)}: {where}. {index + 1} of {list.Count}.";
        return isPursuing ? $"Pursuing {text}" : Capitalised(text);
    }

    private string DescribeSelection()
    {
        ChooseDefault(spoken: null);
        if (Selected() is not { } target)
        {
            return NoSubmarines();
        }

        var where = current is { } snapshot ? Where(target, snapshot) : target.Visible ? "in view" : LastSeen(target);
        var locked = target.Visible && target.Locked ? ", locked" : string.Empty;
        return Capitalised($"{Describe(target)}: {where}{locked}.");
    }

    private string DescribeList()
    {
        var list = Selectable().OrderBy(contact => contact.Order).ToList();
        if (list.Count == 0)
        {
            return NoSubmarines();
        }

        var entries = list.Select(contact =>
        {
            var where = !contact.Visible
                ? LastSeen(contact)
                : current is { } snapshot ? HorizontalWords(contact, snapshot) : "in view";
            return $"{Describe(contact)}, {where}";
        });
        return Capitalised(
            $"{list.Count} submarine{(list.Count == 1 ? string.Empty : "s")}: {string.Join("; ", entries)}.");
    }

    /// <summary>Coarse, like a glance at the screen: a side and above or below, never a number.</summary>
    private string Where(Contact target, SubmarineMissionSnapshot snapshot)
    {
        var place = HorizontalWords(target, snapshot);
        double dx = target.X - snapshot.PlayerX;
        double dz = target.Z - snapshot.PlayerZ;
        var elevation = ElevationOf(target.Y - snapshot.PlayerY, Math.Sqrt(dx * dx + dz * dz));
        var vertical = elevation > VerticalWordsElevation ? ", above"
            : elevation < -VerticalWordsElevation ? ", below"
            : string.Empty;
        return target.Visible ? $"{place}{vertical}" : $"{LastSeen(target)}, {place}{vertical}";
    }

    private static string HorizontalWords(Contact target, SubmarineMissionSnapshot snapshot)
    {
        var dx = target.X - snapshot.PlayerX;
        var dz = target.Z - snapshot.PlayerZ;
        if (Math.Sqrt((double)dx * dx + (double)dz * dz) < MinimumSteerableHorizontal)
        {
            var dy = target.Y - snapshot.PlayerY;
            return dy < -MinimumSteerableHorizontal ? "straight up"
                : dy > MinimumSteerableHorizontal ? "straight down"
                : "right here";
        }

        var error = WrapAngle(BearingOf(dx, dz) - snapshot.Yaw);
        var side = error > 0 ? "right" : "left";
        return Math.Abs(error) switch
        {
            <= 128 => "dead ahead",
            <= 512 => $"ahead, slightly {side}",
            <= 1024 => $"to the {side}",
            <= 1792 => $"behind you to the {side}",
            _ => "behind you"
        };
    }

    private string LastSeen(Contact target)
    {
        var seconds = Math.Max(1, (int)Math.Round((missionClock - target.LastSeen).TotalSeconds));
        return $"last seen {seconds} second{(seconds == 1 ? string.Empty : "s")} ago";
    }

    /// <summary>The name, and in the story what the leader is carrying (field 406 dialogue 45).</summary>
    private string Describe(Contact contact) =>
        contact.Model == SubmarineTargetModel.RedLeader && !isArcade
            ? $"{contact.Name}, carrying the Huge Materia"
            : contact.Name;

    private string NoSubmarines() =>
        (anySighting ? "No submarines in view or remembered." : "No submarines seen yet.") + OverviewHint();

    private string OverviewHint() =>
        current is { IsOverview: true } ? string.Empty : " PageDown shows the overview.";

    private string Spoken(string text)
    {
        lastSpokeAt = lastNow;
        return text;
    }

    private static string Capitalised(string text) =>
        text.Length == 0 || char.IsUpper(text[0]) ? text : char.ToUpperInvariant(text[0]) + text[1..];

    // -- state --------------------------------------------------------------------

    private Contact? Selected() =>
        selectedSlot is { } slot && contacts.TryGetValue(slot, out var contact) && !contact.Sinking
            ? contact
            : null;

    private IEnumerable<Contact> Selectable() => contacts.Values.Where(contact => !contact.Sinking);

    private void StopCore()
    {
        isPursuing = false;
        plan = SubmarinePursuitPlan.Idle;
        outOfViewAnnounced = false;
        rangeBand = -1;
        holdingCourse = false;
        ForgetClosingRate();
    }

    private void DropView()
    {
        current = null;
        plan = SubmarinePursuitPlan.Idle;
        fireState = FireState.Unknown;
        lockedOtherSlot = -1;
        rangeBand = -1;
        ForgetClosingRate();
    }

    private void ResetCore()
    {
        contacts.Clear();
        identities.Clear();
        ordinals.Clear();
        isPursuing = false;
        plan = SubmarinePursuitPlan.Idle;
        current = null;
        selectedSlot = null;
        selectionExplicit = false;
        missionSeen = false;
        anySighting = false;
        isArcade = false;
        nextOrder = 0;
        missionClock = TimeSpan.Zero;
        clockBaseline = null;
        clockWasPaused = false;
        lastSpokeAt = DateTime.MinValue;
        fireState = FireState.Unknown;
        lockedOtherSlot = -1;
        outOfViewAnnounced = false;
        rangeBand = -1;
        holdingCourse = false;
        ForgetClosingRate();
    }
}

namespace Ff7.Accessibility.Reloaded;

/// <summary>What the coaster aim readout wants delivered this tick.</summary>
/// <param name="Speech">Spoken text, or null.</param>
/// <param name="Beacon">
/// A directional cue for the nearest visible target, played on the mod's own spatial
/// device. It exists because the shot button is held and pressed constantly, and any
/// press interrupts screen-reader speech; a tone that cannot be cut off is the only
/// way aiming feedback survives actual play.
/// </param>
public readonly record struct SpeedSquareCoasterAimCue(
    string? Speech,
    NavigationBeaconCue? Beacon)
{
    public bool IsEmpty => Speech is null && Beacon is null;
}

/// <summary>
/// Says where the currently drawn targets are relative to the player's own sight,
/// whether the reticle is over one of them right now, what the displayed score is,
/// and when a shot has actually connected.
///
/// All of those are things a sighted player reads straight off the screen. Nothing
/// here looks at spawn queues, future outcomes or hidden weak points, and nothing
/// moves the sight or fires.
///
/// The wording is deliberately about the present. "On target" means the reticle
/// currently overlaps that target's drawn box and the native acceptance gate is
/// passing for it - not that a shot will connect, which depends on when the shot is
/// released and on the object's own state by then.
/// </summary>
public sealed class SpeedSquareCoasterAimReadout
{
    /// <summary>
    /// Sight units. Coarse enough that small jitter does not chatter, fine enough to
    /// steer by: the sight travels 320 units across the whole screen.
    /// </summary>
    public const int DirectionStep = 20;

    /// <summary>How often the directional cue may repeat while nothing changes.</summary>
    public static readonly TimeSpan BeaconInterval = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Said once as the ride starts. It covers only what this readout adds; the
    /// ride's own instruction window already explains the shot button and the power
    /// meter, and the buttons themselves are whatever the player has bound.
    /// </summary>
    public const string CueExplanation =
        "A tone marks the nearest target on screen, to your left or right as it lies. " +
        "You will hear when your sight is over it.";

    private bool wasActive;
    private int lastTargetIndex = -1;
    private SpeedSquareCoasterTargetIdentity lastTargetIdentity;
    private int lastColumn = int.MinValue;
    private int lastRow = int.MinValue;
    private bool lastUnderSight;
    private int lastScore = int.MinValue;
    private DateTime lastBeaconUtc = DateTime.MinValue;

    // Which objects have already been counted as hit during the shot in progress.
    // Keyed by the object's own identity rather than by its slot, because the slot's
    // active word is a boolean the next occupant inherits unchanged: counting set
    // flags would miss a hit moving from one target to another while the total stayed
    // at one, and keying on the slot alone would let a reused slot inherit the
    // previous object's answer.
    private readonly HashSet<SpeedSquareCoasterTargetIdentity> reportedHits = [];

    public SpeedSquareCoasterAimCue Observe(
        SpeedSquareCoasterState state,
        SpeedSquareCoasterTargetSnapshot snapshot,
        DateTime nowUtc)
    {
        if (!state.IsActive)
        {
            Reset();
            return default;
        }

        // The native input routine ignores everything while the ride is suspended,
        // so the sight cannot move and the result window owns the screen.
        if (state.IsSuspended)
        {
            return default;
        }

        // A frame the engine never presented. Its boxes describe an image nobody saw,
        // and nothing about it can be newer than what has already been said - so it
        // is held rather than described. The next presented frame arrives within a
        // few refreshes and speaks for itself.
        if (!snapshot.WasPresented)
        {
            return default;
        }

        var entering = !wasActive;
        wasActive = true;

        var lines = new List<string>(4);
        if (entering)
        {
            // Only what the native text does not already say. The ride's own
            // instruction window explains the shot button and the power meter, and
            // the controls are whatever the player has bound, so neither is repeated
            // here; this explains the mod's own additions instead.
            lines.Add(CueExplanation);
        }

        var targets = snapshot.Targets;
        var firing = snapshot.Firing;

        // A hit is announced only where the conditions the engine itself used to set
        // the flag still hold in this same snapshot: the acceptance gate passing, the
        // sight strictly inside the drawn box, and the shot in progress. That is what
        // makes the flag this frame's answer rather than one left in the slot from an
        // earlier pass that the gate has not run to clear.
        if (firing)
        {
            foreach (var target in targets)
            {
                if (target.IsResolvedHit(firing) && reportedHits.Add(target.Identity))
                {
                    lines.Add("Hit.");
                }
            }

            // Objects that have gone are no longer anything to suppress. Without
            // this the set grows for the length of a held shot, and an identity that
            // comes back later would be silently swallowed.
            reportedHits.RemoveWhere(identity =>
                !targets.Any(target => target.Identity == identity));
        }
        else
        {
            reportedHits.Clear();
        }

        var score = snapshot.Score;
        if (score != lastScore)
        {
            if (!entering && lastScore != int.MinValue && score > lastScore)
            {
                lines.Add($"Score {score}.");
            }

            lastScore = score;
        }

        if (targets.Count == 0)
        {
            if (lastTargetIndex != -1)
            {
                lastTargetIndex = -1;
                lastColumn = int.MinValue;
                lastRow = int.MinValue;
                lastUnderSight = false;
                lines.Add("No targets in sight.");
            }

            return new SpeedSquareCoasterAimCue(Join(lines), null);
        }

        // The cursor that belongs to this observation, not whichever one a later
        // poll happens to hold. Pairing one frame's boxes with another frame's sight
        // describes a position the player was never in.
        var nearest = targets[0];
        var column = Quantise(nearest.CentreX - snapshot.CursorX);
        var row = Quantise(nearest.CentreY - snapshot.CursorY);
        var changed = entering ||
                      nearest.Index != lastTargetIndex ||
                      nearest.Identity != lastTargetIdentity ||
                      column != lastColumn ||
                      row != lastRow ||
                      nearest.IsUnderSight != lastUnderSight;

        if (changed)
        {
            lines.Add(nearest.IsUnderSight
                ? "Sight on target."
                : $"Target {DescribeDirection(column, row)}.");
            lastTargetIndex = nearest.Index;
            lastTargetIdentity = nearest.Identity;
            lastColumn = column;
            lastRow = row;
            lastUnderSight = nearest.IsUnderSight;
        }

        NavigationBeaconCue? beacon = null;
        if (changed || nowUtc - lastBeaconUtc >= BeaconInterval)
        {
            lastBeaconUtc = nowUtc;
            beacon = CreateBeacon(snapshot.CursorX, snapshot.CursorY, nearest);
        }

        return new SpeedSquareCoasterAimCue(Join(lines), beacon);
    }

    public void Reset()
    {
        wasActive = false;
        lastTargetIndex = -1;
        lastTargetIdentity = default;
        lastColumn = int.MinValue;
        lastRow = int.MinValue;
        lastUnderSight = false;
        lastScore = int.MinValue;
        lastBeaconUtc = DateTime.MinValue;
        reportedHits.Clear();
    }

    /// <summary>
    /// Places the target around the listener. Two separate conventions meet here and
    /// are kept apart on purpose.
    ///
    /// The Steam Audio direction is a unit vector in the listener's own frame, where
    /// ahead is negative Z - the convention Valve's guide gives and the one the
    /// existing highway cue tests already assert. A target the sight has to rise to
    /// reach is above the listener, which is positive Y.
    ///
    /// The legacy stick vector is not that vector. <c>StickY</c> is consumed by
    /// <see cref="NavigationBeaconSpatialEmphasis"/>, where positive means down and
    /// behind, so it carries the screen's own downward sense rather than the HRTF
    /// elevation. Feeding the HRTF elevation into it would make a target above the
    /// sight sound like one behind the listener.
    /// </summary>
    private static NavigationBeaconCue CreateBeacon(
        int cursorX,
        int cursorY,
        SpeedSquareCoasterTarget target)
    {
        var dx = target.CentreX - cursorX;
        var dy = target.CentreY - cursorY;
        var horizontal = Math.Clamp(dx / (SpeedSquareCoasterState.ScreenWidth / 2f), -1f, 1f);

        // Screen down is positive dy, and that is also the stick convention.
        var stickDown = Math.Clamp(dy / (SpeedSquareCoasterState.ScreenHeight / 2f), -1f, 1f);

        // Steam Audio: up is positive Y, ahead is negative Z. The forward component
        // keeps a centred target in front of the listener rather than inside their
        // head, and the whole thing is normalised because the API takes a unit
        // direction.
        var elevation = -stickDown;
        const float forward = -1f;
        var length = MathF.Sqrt((horizontal * horizontal) + (elevation * elevation) + (forward * forward));
        var distance = Math.Sqrt((double)(dx * dx) + (dy * dy));
        return new NavigationBeaconCue(
            TargetLabel: "coaster target",
            Direction: DescribeDirection(Quantise(dx), Quantise(dy)),
            StickX: horizontal,
            StickY: stickDown,
            SteamAudioX: horizontal / length,
            SteamAudioY: elevation / length,
            SteamAudioZ: forward / length,
            MovementState: target.IsUnderSight
                ? NavigationBeaconMovementState.OnCourse
                : NavigationBeaconMovementState.Correcting,
            DurationMs: 90,
            DistanceUnits: distance);
    }

    private static string? Join(List<string> lines) =>
        lines.Count == 0 ? null : string.Join(" ", lines);

    private static int Quantise(int offset) =>
        offset >= 0 ? offset / DirectionStep : -((-offset) / DirectionStep);

    private static string DescribeDirection(int column, int row)
    {
        if (column == 0 && row == 0)
        {
            return "centred";
        }

        var horizontal = column == 0
            ? string.Empty
            : $"{(column < 0 ? "left" : "right")} {Math.Abs(column)}";
        var vertical = row == 0
            ? string.Empty
            : $"{(row < 0 ? "up" : "down")} {Math.Abs(row)}";
        return horizontal.Length == 0
            ? vertical
            : vertical.Length == 0
                ? horizontal
                : $"{horizontal}, {vertical}";
    }
}

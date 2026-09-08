namespace Ff7.Accessibility.Reloaded;

/// <param name="Speech">What to say now, or nothing.</param>
/// <param name="PlayLockCue">
/// Play the short protected cue on its own device. The mission is played with a button
/// held down, so the one signal a player cannot afford to lose - the moment a target
/// becomes the one the torpedoes will actually fire at - does not go through speech that
/// the next press would cut off.
/// </param>
public readonly record struct SubmarineMissionCue(string? Speech, bool PlayLockCue);

/// <summary>
/// Speaks the submarine mission's own instruments and the markers it is drawing.
///
/// <para>A sighted player reads the clock, the damage bar, the depth and speed numbers,
/// the four torpedo lamps and the warning lamps continuously, and sees coloured squares
/// over the enemies the game has detected and is drawing. This says the same things,
/// bounded so it cannot talk over the mission: a change of situation is said once, the
/// instruments are refreshed no faster than a listener can follow, and the full readout
/// is there on request.</para>
///
/// <para>It never fires, never steers, never names an enemy's health, never gives a
/// distance the game does not print, and never counts down a reload the game keeps to
/// itself. A target that will not project into the current camera and viewport is not
/// mentioned, because the game is not drawing it.</para>
/// </summary>
public sealed class SubmarineMissionReadout
{
    /// <summary>Instrument refreshes share this, so nothing streams a line per frame.</summary>
    public static readonly TimeSpan InstrumentInterval = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Markers move while the mission is being played, so they get a beat of their own
    /// rather than waiting for the instrument paragraph. It is short enough to track an
    /// enemy crossing the screen and long enough not to stream a line per frame.
    /// </summary>
    public static readonly TimeSpan TargetInterval = TimeSpan.FromSeconds(1.5);

    /// <summary>The damage bar is spoken in tenths, the way a bar reads at a glance.</summary>
    public const int HealthStep = 10;

    private bool wasActive;
    private bool wasPaused;
    private bool announcedResult;
    private bool wasLocked;
    private bool? lastQuitSelection;
    private int lastHealthStep = int.MinValue;
    private int lastWarnings = int.MinValue;
    private string? lastInstruments;
    private string? lastTargetKey;
    private DateTime lastInstrumentsAt = DateTime.MinValue;
    private DateTime lastTargetsAt = DateTime.MinValue;

    public void Reset()
    {
        wasActive = false;
        wasPaused = false;
        announcedResult = false;
        wasLocked = false;
        lastQuitSelection = null;
        lastHealthStep = int.MinValue;
        lastWarnings = int.MinValue;
        lastInstruments = null;
        lastTargetKey = null;
        lastInstrumentsAt = DateTime.MinValue;
        lastTargetsAt = DateTime.MinValue;
    }

    public SubmarineMissionCue Observe(SubmarineMissionSnapshot snapshot) =>
        Observe(snapshot, DateTime.UtcNow);

    public SubmarineMissionCue Observe(SubmarineMissionSnapshot snapshot, DateTime now)
    {
        if (!snapshot.IsActive)
        {
            var leaving = wasActive;
            Reset();
            return leaving ? new SubmarineMissionCue("Submarine mission over.", false) : default;
        }

        var entering = !wasActive;
        wasActive = true;

        // The mission's own word says what happened. A reward flag is not a result, and
        // nothing here calls it one before the game does.
        if (snapshot.HasResult)
        {
            if (announcedResult)
            {
                return default;
            }

            announcedResult = true;
            return new SubmarineMissionCue(DescribeOutcome(snapshot.Outcome), false);
        }

        if (snapshot.IsPaused)
        {
            // Only the arcade cabinet offers the quit prompt, and which answer is
            // highlighted is the one thing on that screen that moves.
            if (snapshot.IsQuitPromptOpen)
            {
                if (lastQuitSelection == snapshot.QuitPromptYesSelected)
                {
                    return default;
                }

                lastQuitSelection = snapshot.QuitPromptYesSelected;
                return new SubmarineMissionCue(
                    $"Quit the mission? {(snapshot.QuitPromptYesSelected ? "Yes" : "No")} selected.",
                    false);
            }

            lastQuitSelection = null;
            if (wasPaused)
            {
                return default;
            }

            wasPaused = true;
            return new SubmarineMissionCue("Paused.", false);
        }

        var resuming = wasPaused;
        wasPaused = false;
        lastQuitSelection = null;

        // Arriving is its own situation and carries everything, including whatever the
        // mission is already locked on to. Letting the lock check run first would have
        // replaced a player's only introduction to the screen with two words.
        if (entering || resuming)
        {
            wasLocked = snapshot.HasLockedTarget;
            lastHealthStep = snapshot.HealthPercent / HealthStep;
            lastWarnings = snapshot.Warnings;
            lastInstrumentsAt = now;
            lastInstruments = DescribeInstruments(snapshot);
            lastTargetsAt = now;
            lastTargetKey = snapshot.CanPlaceTargets ? DescribeTargetKey(snapshot) : null;
            return new SubmarineMissionCue(
                (entering ? "Submarine mission. " : "Resumed. ") + Describe(snapshot),
                snapshot.HasLockedTarget);
        }

        // The lock is what the fire button actually needs, and it comes and goes with
        // the forward cone. It jumps the queue and takes the protected cue with it.
        //
        // A view that could not be read holds no opinion about the lock. Letting an
        // unreadable frame count as "no lock" would announce the loss of a lock the
        // mission still has, which is worse than saying nothing for a frame.
        if (snapshot.CanPlaceTargets && snapshot.HasLockedTarget != wasLocked)
        {
            var acquired = snapshot.HasLockedTarget;
            wasLocked = acquired;
            lastInstrumentsAt = now;

            // The lock carries where the locked square is, so the news and the place it
            // is in arrive together rather than as two sentences a beat apart.
            lastTargetKey = DescribeTargetKey(snapshot);
            lastTargetsAt = now;
            var locked = snapshot.VisibleTargets.FirstOrDefault(target => target.IsLocked);
            return new SubmarineMissionCue(
                acquired
                    ? $"Target locked, {DescribeTarget(snapshot, locked)}."
                    : "Lock lost.",
                acquired);
        }

        // Taking damage is the change a player most needs told at once, and the bar it
        // is read from moves in visible steps.
        var healthStep = snapshot.HealthPercent / HealthStep;
        if (healthStep < lastHealthStep)
        {
            lastHealthStep = healthStep;
            lastInstrumentsAt = now;
            return new SubmarineMissionCue($"Hull {snapshot.HealthPercent} percent.", false);
        }

        lastHealthStep = healthStep;

        // The warning lamps are on the screen, so a lamp lighting or going out is a
        // visible change and is said when it happens rather than on the slow beat.
        var visibleWarnings = snapshot.Warnings & VisibleWarningMask;
        if (lastWarnings >= 0 && (lastWarnings & VisibleWarningMask) != visibleWarnings)
        {
            lastWarnings = snapshot.Warnings;
            lastInstrumentsAt = now;
            var warnings = DescribeWarnings(snapshot);
            return new SubmarineMissionCue(warnings ?? "Warnings clear.", false);
        }

        lastWarnings = snapshot.Warnings;

        // Where the marked enemies are is the part of this screen that actually moves,
        // and it is not in the instrument paragraph. An unreadable view is left alone
        // rather than reported as an empty sea.
        if (snapshot.CanPlaceTargets)
        {
            var targetKey = DescribeTargetKey(snapshot);
            if (!string.Equals(targetKey, lastTargetKey, StringComparison.Ordinal) &&
                now - lastTargetsAt >= TargetInterval)
            {
                lastTargetKey = targetKey;
                lastTargetsAt = now;
                lastInstrumentsAt = now;
                return new SubmarineMissionCue(DescribeTargets(snapshot), false);
            }
        }

        if (now - lastInstrumentsAt < InstrumentInterval)
        {
            return default;
        }

        var instruments = DescribeInstruments(snapshot);
        lastInstrumentsAt = now;
        if (string.Equals(instruments, lastInstruments, StringComparison.Ordinal))
        {
            return default;
        }

        lastInstruments = instruments;
        return new SubmarineMissionCue(instruments, false);
    }

    /// <summary>Everything on the screen at once, for the on-demand key.</summary>
    public string Describe(SubmarineMissionSnapshot snapshot)
    {
        if (!snapshot.IsActive)
        {
            return "The submarine mission is not running.";
        }

        if (snapshot.HasResult)
        {
            return DescribeOutcome(snapshot.Outcome);
        }

        var parts = new List<string>(6) { DescribeInstruments(snapshot) };
        if (DescribeWarnings(snapshot) is { } warnings)
        {
            parts.Add(warnings);
        }

        parts.Add(DescribeTargets(snapshot));
        if (snapshot.IsPaused)
        {
            parts.Insert(0, "Paused.");
        }

        return string.Join(" ", parts);
    }

    private const int VisibleWarningMask = 0x1 | 0x2 | 0x4 | 0x400 | 0x800;

    private static string DescribeInstruments(SubmarineMissionSnapshot snapshot)
    {
        var minutes = snapshot.RemainingSeconds / 60;
        var seconds = snapshot.RemainingSeconds % 60;
        var speed = snapshot.Speed switch
        {
            0 => "stopped",
            < 0 => $"astern {Math.Abs(snapshot.Speed)}",
            _ => $"ahead {snapshot.Speed}"
        };
        return
            $"{minutes}:{seconds:00} left. Hull {snapshot.HealthPercent} percent. " +
            $"Depth {snapshot.Depth}. Speed {speed}. {DescribePitch(snapshot.Pitch)}, " +
            $"{DescribeHeading(snapshot.Yaw)}. {DescribeTorpedoes(snapshot)}.";
    }

    private static string DescribeTorpedoes(SubmarineMissionSnapshot snapshot) =>
        snapshot.ReadyTorpedoes switch
        {
            0 when snapshot.ReloadingTorpedoes > 0 => "No torpedoes loaded, reloading",
            0 => "No torpedoes loaded",
            1 => "1 torpedo loaded",
            _ => $"{snapshot.ReadyTorpedoes} torpedoes loaded"
        };

    /// <summary>
    /// The pitch gauge, coarsely. 798580 clamps the word to plus or minus 0x3F0, so the
    /// fraction of that range is what the needle shows, and 792216 draws it at
    /// <c>-(pitch / 32) * 2 - 0x50</c>: a larger word puts the marker further up a screen
    /// whose Y grows downward, which is what fixes the direction of these words.
    /// </summary>
    private static string DescribePitch(int pitch)
    {
        const int limit = 0x3F0;
        var eighths = pitch * 8 / limit;
        return eighths switch
        {
            <= -6 => "nose down hard",
            <= -2 => "nose down",
            <= 1 and >= -1 => "level",
            < 6 => "nose up",
            _ => "nose up hard"
        };
    }

    /// <summary>The compass. A full turn is 4096, so eight points are 512 apart.</summary>
    private static string DescribeHeading(int yaw)
    {
        string[] points =
        [
            "north", "north east", "east", "south east",
            "south", "south west", "west", "north west"
        ];
        var wrapped = ((yaw % 4096) + 4096) % 4096;
        return $"heading {points[(wrapped + 256) / 512 % 8]}";
    }

    private static string? DescribeWarnings(SubmarineMissionSnapshot snapshot)
    {
        var lamps = new List<string>(5);
        if (snapshot.HullContact)
        {
            lamps.Add("hull contact");
        }

        if (snapshot.TerrainClose)
        {
            lamps.Add("terrain close");
        }

        if (snapshot.MineNearby)
        {
            lamps.Add("mine nearby");
        }

        if (snapshot.TorpedoInTheWater)
        {
            // 792CE6 lights this for any active enemy torpedo; distance only changes the
            // sound. Calling it an imminent hit would be a claim the lamp does not make.
            lamps.Add("torpedo in the water");
        }

        if (snapshot.EnemyDetected)
        {
            // 792F2F lights this for any active enemy carrying a marker, in front or
            // not. It is a detection lamp, not an attack.
            lamps.Add("enemy detected");
        }

        return lamps.Count == 0 ? null : "Warning: " + string.Join(", ", lamps) + ".";
    }

    /// <summary>
    /// What would have to change before the markers are worth saying again: which slots
    /// are drawn, which is locked, and which coarse part of the screen each is in. It
    /// deliberately does not carry pixels, so an enemy drifting within one third of the
    /// screen does not produce a sentence a frame.
    /// </summary>
    private static string DescribeTargetKey(SubmarineMissionSnapshot snapshot) =>
        string.Join(
            "|",
            snapshot.VisibleTargets
                .OrderBy(target => target.Slot)
                .Select(target =>
                    $"{target.Slot}:{target.IsLocked}:{DescribeTarget(snapshot, target)}"));

    private static string DescribeTargets(SubmarineMissionSnapshot snapshot)
    {
        if (!snapshot.CanPlaceTargets)
        {
            return "The view cannot be read, so marked enemies cannot be placed on screen.";
        }

        if (snapshot.VisibleTargets.Count == 0)
        {
            return "No marked enemy on screen.";
        }

        var described = snapshot.VisibleTargets
            .OrderBy(target => target.Slot)
            .Select(target => DescribeTarget(snapshot, target))
            .ToArray();
        return described.Length == 1
            ? $"Enemy {described[0]}."
            : $"{described.Length} marked enemies: {string.Join("; ", described)}.";
    }

    private static string DescribeTarget(
        SubmarineMissionSnapshot snapshot,
        SubmarineVisibleTarget target)
    {
        var horizontal = Third(
            target.ScreenX - snapshot.ViewportOriginX,
            snapshot.ViewportWidth,
            "left",
            "centre",
            "right");
        var vertical = Third(
            target.ScreenY - snapshot.ViewportOriginY,
            snapshot.ViewportHeight,
            "high",
            "level",
            "low");
        return target.IsLocked
            ? $"locked, {horizontal} and {vertical}"
            : $"{horizontal} and {vertical}";
    }

    private static string Third(int offset, int extent, string low, string middle, string high)
    {
        if (extent <= 0)
        {
            return middle;
        }

        var third = offset * 3 / extent;
        return third switch
        {
            <= 0 => low,
            1 => middle,
            _ => high
        };
    }

    private static string DescribeOutcome(SubmarineMissionOutcome outcome) =>
        outcome switch
        {
            SubmarineMissionOutcome.Success => "Mission complete.",
            SubmarineMissionOutcome.Destroyed => "The submarine has been destroyed.",
            SubmarineMissionOutcome.TimedOut => "Out of time.",
            SubmarineMissionOutcome.Quit => "Mission abandoned.",
            _ => "The mission has ended."
        };

    /// <summary>
    /// What the mission does with each action, by the game's own action names rather
    /// than by any particular key, so a remapped control still reads correctly. 798580
    /// is the handler these come from.
    /// </summary>
    public static string DescribeControls() =>
        "Switch fires a torpedo at a locked target. Menu is forward and Cancel is " +
        "reverse. Up and Down change pitch, Left and Right turn. PageUp rises and " +
        "Camera dives. Target changes the view and PageDown toggles the overview. " +
        "Start pauses.";
}

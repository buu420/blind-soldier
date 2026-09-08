namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// What the player's own chocobo is doing while they are riding it.
///
/// <param name="IsPlayerRacing">
/// 0x00E71128 is non-zero: the player is a jockey rather than a spectator betting on six
/// other racers. It is emphatically <b>not</b> the automatic/manual flag, which is a
/// different word entirely.
/// </param>
/// <param name="HasStarted">
/// 0x00E710F8 is non-zero, which the native code sets at frame 60 when the countdown
/// finishes. Before that the chocobos are on the line, so pace and dash have nothing to
/// report - but the control mode and the stamina gauge are already on screen and worth
/// reading.
/// </param>
/// <param name="IsAutomatic">
/// The player record's word at +0x60, 0x00E711B8. FUN_0076E2B0 draws the automatic sprite
/// when it is non-zero and the manual one when it is zero; FUN_0077C462 toggles it on the
/// Assist button, native input bit 0x100, and only in a player race.
/// </param>
/// <param name="Speed">
/// The record's own speed at +0x04, 0x00E7115C. FUN_00773DD8 uses it to translate the
/// chocobo and to drive its run animation, so it is what the chocobo is really doing
/// rather than what a button asked for. It is a signed short in the native record.
/// </param>
/// <param name="Stamina">Current stamina, the record's int at +0x68, 0x00E711C0.</param>
/// <param name="StaminaMaximum">
/// Its maximum, at +0x6C, 0x00E711C4. Zero means the gauge cannot be worked out - the
/// native renderer divides by it - and an unknown gauge is not an empty one.
/// </param>
/// <param name="IsDashing">
/// The record's animation at +0x80 is the turbo one. FUN_00773DD8 sets it when a dash is
/// accepted and stamina is being spent on it; the button alone proves nothing, because
/// stamina, docility and the track segment can all refuse.
/// </param>
/// <param name="HasFinished">The record's finished word at +0x7E, 0x00E711D6.</param>
/// <param name="Place">
/// The player's own place, matched by jockey number against the drawn ranking strip, or
/// zero while the strip is not settled.
/// </param>
public readonly record struct ChocoboRaceControlState(
    bool IsPlayerRacing,
    bool HasStarted,
    bool IsAutomatic,
    int Speed,
    int Stamina,
    int StaminaMaximum,
    bool IsDashing,
    bool HasFinished,
    int Place);

/// <summary>
/// Speaks the parts of a player's own race a sighted player can see and a blind one cannot.
///
/// <para>The reported gap was exact: "I could not really tell if I was speeding up or
/// slowing down during the chocobo race, also I didn't know if I was in manual or auto
/// mode". Both are on screen - the mode as a sprite the HUD swaps, the pace as the
/// chocobo's visible motion - and the mod said neither. It announced the finishing order
/// and nothing else, sixty-five times in the recorded race.</para>
///
/// <para>Everything here is change-based, and the order is deliberate: the control mode
/// and the pace come before the field order, because the order was already drowning them.
/// Nothing is announced before the countdown finishes except the mode and the stamina,
/// which are the two things already drawn at the starting line.</para>
///
/// <para>What this does not do: it does not read the other racers' hidden speed or
/// stamina, does not infer anything from which key was pressed, does not offer tactics, and
/// does not press anything. Speeding up and slowing down are reported from the chocobo's
/// own speed word, which is the same quantity that moves it on screen.</para>
/// </summary>
public sealed class ChocoboRaceControlReadout
{
    /// <summary>
    /// How much the speed has to move before it counts as a change of pace.
    ///
    /// <para>The native speed word moves a little every frame - the run animation is driven
    /// from it - so a band keeps this off the noise floor.</para>
    /// </summary>
    private const int SpeedChangeThreshold = 96;

    /// <summary>
    /// The shortest gap between two spoken pace lines.
    ///
    /// <para>A threshold alone is not enough. Root's harness alternates the speed by a
    /// hundred every hundred milliseconds - which a real track with a docile chocobo can do
    /// - and a bare threshold turns that into twenty sentences in two seconds, over the top
    /// of everything else. A change inside this window is not thrown away: it is held and
    /// spoken as soon as the window is up, so a real reversal is still heard, just not
    /// twenty times.</para>
    /// </summary>
    private static readonly TimeSpan MinimumTimeBetweenPaceLines = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long the pace has to hold steady before picking up again counts as fresh news.
    ///
    /// <para>The first version latched on direction alone: once "speeding up" had been
    /// said, accelerating again was never mentioned until the player slowed down first.
    /// That is wrong, and root's harness catches it - hold a steady pace for two seconds
    /// down the back straight, then accelerate again, and a sighted player sees the
    /// chocobo pull away while a blind one hears nothing.</para>
    /// </summary>
    private static readonly TimeSpan PaceSettlingTime = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// The quarters of the stamina gauge. Crossing one is the event; the number spoken is
    /// the gauge's actual reading, because announcing "fifty percent" while it shows
    /// seventy-four would be telling the player something untrue.
    /// </summary>
    private static readonly int[] StaminaBands = [75, 50, 25, 10];

    private bool wasRacing;
    private bool wasStarted;
    private bool? lastAutomatic;
    private int lastSpokenSpeed;
    private int lastStaminaBand = int.MaxValue;
    private bool wasDashing;
    private bool wasFinished;
    private int lastPlace;

    // Which way the pace was last reported to be going, what is waiting to be reported,
    // when something last was, and how long it has been holding still. Together these turn
    // a noisy speed word into the handful of sentences a sighted player would notice.
    private int lastPaceTrend;
    private int pendingPaceTrend;
    private DateTime lastPaceLineAt = DateTime.MinValue;
    private DateTime? steadyPaceSince;

    // An empty gauge is its own event. Five percent and nothing are the same band but not
    // the same news, and running out is the moment the player most needs telling.
    private bool wasEmpty;

    /// <summary>
    /// Whether the last observation reported the player's own place changing. The field
    /// order is worth reading out during a player's race when their place moves, and is
    /// otherwise chatter over the top of the things they asked for.
    /// </summary>
    public bool PlaceChangedOnLastObservation { get; private set; }

    public IReadOnlyList<string> Observe(ChocoboRaceControlState state) =>
        Observe(state, DateTime.UtcNow);

    /// <summary>
    /// One sample of the player's own race. The clock is a parameter because the pace
    /// coalescing is a matter of time rather than of samples, and a test that cannot
    /// control the clock cannot check it.
    /// </summary>
    public IReadOnlyList<string> Observe(ChocoboRaceControlState state, DateTime observedAt)
    {
        var lines = new List<string>(3);
        PlaceChangedOnLastObservation = false;
        if (!state.IsPlayerRacing)
        {
            Reset();
            return lines;
        }

        var entering = !wasRacing;
        wasRacing = true;

        // The mode first, every time the race opens and every time it changes. This is the
        // one the player asked for by name and it must never be buried under the order.
        if (entering || lastAutomatic != state.IsAutomatic)
        {
            lines.Add(state.IsAutomatic
                ? "Automatic control."
                : "Manual control.");
            lastAutomatic = state.IsAutomatic;
        }

        if (entering)
        {
            lines.Add(DescribeStamina(state));
            lastStaminaBand = BandOf(state);
            lastSpokenSpeed = state.Speed;
        }

        if (state.HasFinished)
        {
            if (!wasFinished)
            {
                wasFinished = true;
                lastPlace = state.Place;
                lines.Add(state.Place > 0
                    ? $"Finished {Ordinal(state.Place)}."
                    : "Finished.");
            }
            else if (state.Place > 0 && state.Place != lastPlace)
            {
                // The ranking strip refreshes about sixteen frames after a chocobo crosses
                // the line, so the first finished sample can still be carrying the place
                // from before it. The strip is what a sighted player is looking at, and if
                // it disagrees with what was said, what was said has to be corrected.
                lastPlace = state.Place;
                PlaceChangedOnLastObservation = true;
                lines.Add($"Finished {Ordinal(state.Place)}.");
            }

            return lines;
        }

        if (!state.HasStarted)
        {
            // Still on the line. The countdown has not finished, so nothing is moving and
            // a "slowing down" here would be an invention.
            wasStarted = false;
            lastSpokenSpeed = state.Speed;
            return lines;
        }

        if (!wasStarted)
        {
            wasStarted = true;
            lastSpokenSpeed = state.Speed;
        }

        if (state.IsDashing != wasDashing)
        {
            wasDashing = state.IsDashing;
            if (state.IsDashing)
            {
                // The native turbo animation, which only runs when the dash was accepted
                // and stamina is going into it.
                lines.Add("Dashing.");
            }
        }

        ObservePace(state, observedAt, lines);
        ObserveStamina(state, lines);

        if (state.Place > 0 && state.Place != lastPlace)
        {
            lastPlace = state.Place;
            PlaceChangedOnLastObservation = true;
            lines.Add($"{Ordinal(state.Place)} place.");
        }

        return lines;
    }

    /// <summary>
    /// Speeding up and slowing down, as often as it is worth saying and no oftener.
    ///
    /// <para>Three rules, and each is there because leaving it out is wrong in a way a
    /// player would notice. A change of direction is news. Picking the pace up again after
    /// it has held steady is news, because a sighted player watches the chocobo pull away
    /// and the first version of this said nothing. And nothing is worth saying twice in a
    /// second, so anything caught inside that window waits its turn rather than being
    /// thrown away.</para>
    /// </summary>
    private void ObservePace(ChocoboRaceControlState state, DateTime observedAt, List<string> lines)
    {
        var difference = state.Speed - lastSpokenSpeed;
        if (Math.Abs(difference) >= SpeedChangeThreshold)
        {
            var trend = difference > 0 ? 1 : -1;
            lastSpokenSpeed = state.Speed;
            var hasSettled = steadyPaceSince is { } since &&
                observedAt - since >= PaceSettlingTime;
            if (trend != lastPaceTrend || hasSettled)
            {
                pendingPaceTrend = trend;
            }
            else if (pendingPaceTrend != 0)
            {
                // A change that was still waiting its turn has been undone before it could
                // be said. Saying it now would report something that has stopped being
                // true: the chocobo has gone back to doing what it was already doing.
                pendingPaceTrend = 0;
            }

            steadyPaceSince = null;
        }
        else
        {
            steadyPaceSince ??= observedAt;
        }

        if (pendingPaceTrend == 0)
        {
            return;
        }

        if (lastPaceLineAt != DateTime.MinValue &&
            observedAt - lastPaceLineAt < MinimumTimeBetweenPaceLines)
        {
            return;
        }

        lastPaceTrend = pendingPaceTrend;
        lastPaceLineAt = observedAt;
        lines.Add(pendingPaceTrend > 0 ? "Speeding up." : "Slowing down.");
        pendingPaceTrend = 0;
    }

    /// <summary>
    /// The stamina gauge, when it crosses a quarter and when it runs out.
    ///
    /// <para>An unknown maximum is not an empty gauge. The native renderer divides by it,
    /// so a zero means the gauge cannot be worked out at all - and saying "empty" then
    /// would be inventing the single most alarming reading it has.</para>
    /// </summary>
    private void ObserveStamina(ChocoboRaceControlState state, List<string> lines)
    {
        if (state.StaminaMaximum <= 0)
        {
            return;
        }

        var band = BandOf(state);
        var percent = Percent(state);
        if (percent <= 0)
        {
            if (!wasEmpty)
            {
                wasEmpty = true;
                lastStaminaBand = band;
                lines.Add("Stamina empty.");
            }

            return;
        }

        // Anything at all on the gauge re-arms the empty announcement. Running out is the
        // moment the player most needs telling, and running out a second time after
        // scraping a little back is a second time they need telling - the first version
        // said it once a race and then never again.
        wasEmpty = false;
        if (band < lastStaminaBand)
        {
            lastStaminaBand = band;
            lines.Add($"Stamina {percent} percent.");
            return;
        }

        if (band > lastStaminaBand)
        {
            // Stamina recovers when the player stops spending it, and a sighted player
            // watches the bar climb back. Only the return to the top of the gauge is worth
            // a word, and the word is what the gauge actually reads: seventy-six percent is
            // not "recovered".
            lastStaminaBand = band;
            if (band >= 75)
            {
                lines.Add($"Stamina {percent} percent.");
            }
        }
    }

    /// <summary>
    /// The whole of it on demand, for the status key, in the order a player would want it:
    /// what is steering, where they are, how much is left, and what the chocobo is doing.
    /// </summary>
    public static string Describe(ChocoboRaceControlState state)
    {
        if (!state.IsPlayerRacing)
        {
            return "You are not riding in this race.";
        }

        var parts = new List<string>(4)
        {
            state.IsAutomatic ? "Automatic control." : "Manual control."
        };

        if (state.HasFinished)
        {
            parts.Add(state.Place > 0 ? $"Finished {Ordinal(state.Place)}." : "Finished.");
            return string.Join(" ", parts);
        }

        if (!state.HasStarted)
        {
            parts.Add("Waiting for the start.");
        }
        else if (state.Place > 0)
        {
            parts.Add($"{Ordinal(state.Place)} place.");
        }

        parts.Add(DescribeStamina(state));
        if (state.HasStarted && state.IsDashing)
        {
            parts.Add("Dashing.");
        }

        return string.Join(" ", parts);
    }

    public void Reset()
    {
        wasRacing = false;
        wasStarted = false;
        lastAutomatic = null;
        lastSpokenSpeed = 0;
        lastPaceTrend = 0;
        pendingPaceTrend = 0;
        lastPaceLineAt = DateTime.MinValue;
        steadyPaceSince = null;
        wasEmpty = false;
        lastStaminaBand = int.MaxValue;
        PlaceChangedOnLastObservation = false;
        wasDashing = false;
        wasFinished = false;
        lastPlace = 0;
    }

    private static string DescribeStamina(ChocoboRaceControlState state)
    {
        if (state.StaminaMaximum <= 0)
        {
            return "Stamina unknown.";
        }

        var percent = Percent(state);
        return percent <= 0 ? "Stamina empty." : $"Stamina {percent} percent.";
    }

    private static int Percent(ChocoboRaceControlState state) =>
        state.StaminaMaximum <= 0
            ? 0
            : (int)Math.Clamp(
                Math.Round(state.Stamina * 100d / state.StaminaMaximum),
                0d,
                100d);

    private static int BandOf(ChocoboRaceControlState state)
    {
        var percent = Percent(state);
        foreach (var band in StaminaBands)
        {
            if (percent >= band)
            {
                return band;
            }
        }

        return 0;
    }

    private static string Ordinal(int place) => place switch
    {
        1 => "first",
        2 => "second",
        3 => "third",
        4 => "fourth",
        5 => "fifth",
        6 => "sixth",
        _ => place.ToString()
    };
}

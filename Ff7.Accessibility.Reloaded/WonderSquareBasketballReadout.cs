namespace Ff7.Accessibility.Reloaded;

/// <summary>What the Basketball readout wants delivered this tick.</summary>
/// <param name="RiseStarted">The ball has begun to rise.</param>
/// <param name="RiseSettled">The rise has run out and the ball is being held still.</param>
public readonly record struct WonderSquareBasketballCue(
    string? Speech,
    bool RiseStarted,
    bool RiseSettled)
{
    public bool IsEmpty => Speech is null && !RiseStarted && !RiseSettled;
}

/// <summary>
/// Gives a blind player the two landmarks a sighted player watches during the
/// Basketball Game's wind-up, and only those two.
///
/// What is on screen, from installed field 506 games_1: the moment Bank[5][25] goes
/// up, cloud(1) script 10 byte 72 requests ball(13) script 5 - a single
/// <c>OFST</c> raising the ball by 112 at speed 8 - and byte 75 plays animation 12
/// frames 8..16 on Cloud. After that the pose is held: the partial animation holds
/// its last frame while byte 80 waits for the release, and the ball's offset is not
/// moved again. So the screen shows one short rise and then nothing at all until the
/// button is let go.
///
/// That is why there is no per-step tick. The counter behind the wind-up keeps
/// climbing for as long as [OK] is held, but nothing on screen climbs with it, and
/// reporting it would hand the player a strength meter the game never draws. The
/// script's own success value is never read.
///
/// Both landmarks now come from Cloud's displayed model - the animation id and the
/// current and end frames the native <c>CANM!2</c> handler writes - so neither
/// depends on how often a helper script's loop happens to run.
/// </summary>
public sealed class WonderSquareBasketballReadout
{
    private enum Phase
    {
        /// <summary>No wind-up is being followed. The next one may be described.</summary>
        Waiting,

        /// <summary>A rise was caught from its start and is being followed.</summary>
        Rising,

        /// <summary>This wind-up is finished with, whether described or not.</summary>
        Spent
    }

    private bool wasRunning;
    private Phase phase = Phase.Waiting;

    public WonderSquareBasketballCue Observe(WonderSquareBasketballState state)
    {
        if (!state.IsShotRunning)
        {
            Reset();
            return default;
        }

        if (!wasRunning)
        {
            wasRunning = true;
            phase = Phase.Waiting;
        }

        // The throw ends one wind-up. The script's Double Chance rounds re-arm and
        // run the whole sequence again from bytes 224 and 536 without ever leaving
        // script 10, so each of those rises has to be describable in its own right.
        if (state.HasThrown)
        {
            phase = Phase.Waiting;
            return default;
        }

        switch (phase)
        {
            case Phase.Waiting when state.IsRising:
                phase = Phase.Rising;
                return new WonderSquareBasketballCue("Winding up.", RiseStarted: true, RiseSettled: false);

            case Phase.Waiting when state.HasSettled:
                // Seen for the first time already settled: the start is past, and a
                // landmark announced after it happened is worse than silence.
                phase = Phase.Spent;
                return default;

            case Phase.Rising when state.HasSettled:
                phase = Phase.Spent;
                return new WonderSquareBasketballCue(null, RiseStarted: false, RiseSettled: true);

            default:
                // Held pose, or a segment this readout does not describe.
                return default;
        }
    }

    public void Reset()
    {
        wasRunning = false;
        phase = Phase.Waiting;
    }
}

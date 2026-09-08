namespace Ff7.Accessibility.Reloaded;

/// <param name="IsDirectorListening">
/// 128:0's script 3 is running: the field is holding the party still and watching for a
/// fresh Confirm. It stops running once the moment has passed 128 or the temporary byte
/// says the buttons are done, so this alone is the whole of "the game is waiting".
/// </param>
/// <param name="Barret">128:2 script 3's controller and pose.</param>
/// <param name="Tifa">128:3 script 3's controller and pose.</param>
public readonly record struct Reactor5ButtonObservation(
    bool IsDirectorListening,
    FieldScriptControllerState Barret,
    FieldScriptControllerState Tifa);

/// <param name="PlayCue">Play the short tone on its own device.</param>
public readonly record struct Reactor5ButtonCueResult(bool PlayCue);

/// <summary>
/// The moment to push the button in Reactor 5, taken from what the other two are
/// visibly doing.
///
/// <para>Field 128's LINE 6 tells the player, in the game's own words, to push at the
/// same time as the others, and then hands over to 128:0's script 3, which holds
/// everyone still and waits for a fresh Confirm. A sighted player watches Barret and
/// Tifa raise their arms and presses with them. There is nothing to hear: the sound the
/// script plays is at frame 25, when their hands are already on the button, and the
/// byte that records a success is set later still.</para>
///
/// <para>So the cue is the start of their raise. The installed scripts are exact about
/// it: 128:2:3 and 128:3:3 both open with <c>CANM 03 00 19 01</c> - animation 3, frames
/// 0 to 25 - and 128:1:4, Cloud's own press, opens with the identical segment. The
/// player therefore needs the whole of that run to match it, which is why a cue is only
/// worth giving near the beginning of the segment. Attaching to it late, when the arms
/// are nearly down on the button, would be telling the player to press now for a press
/// that can no longer land, so nothing is played at all in that case.</para>
///
/// <para>Nothing here reads the success byte, predicts the next attempt, or presses
/// anything. Each retry starts the segment again and is cued again.</para>
/// </summary>
public sealed class Reactor5ButtonCueTracker
{
    public const int FieldId = 128;
    public const int DirectorEntityId = 0;
    public const int DirectorScriptId = 3;
    public const int BarretEntityId = 2;
    public const int TifaEntityId = 3;
    public const int PartnerScriptId = 3;

    /// <summary>
    /// 128:2:3 and 128:3:3 byte 0, from the installed smkin_1: <c>BC 03 00 19 01</c> -
    /// CANM!2, animation 3, frames 0 to 25, speed 1. Byte 10 is the same animation's
    /// 25 to 39, which is the contact and the recovery rather than the raise.
    /// </summary>
    public const int RaiseAnimationId = 3;
    public const int RaiseEndFrame = 25;

    /// <summary>
    /// The animation run states that mean a partial segment is genuinely playing.
    /// FUN_0060CE26 tells them apart explicitly: 1 loops, 3 and 4 clamp at the end, and
    /// 2 and 6 are the partial playbacks. FUN_00614E3E writes 2 for the blocking form -
    /// which is what opcode BC takes, and BC is what both partners use here - and 6 for
    /// the asynchronous B1. A pose reported in any other state is idle, looping or held,
    /// and none of those is an arm on its way up.
    /// </summary>
    public const int BlockingPartialRunState = 2;
    public const int AsynchronousPartialRunState = 6;

    /// <summary>
    /// The last frame at which a press could still land, for a player who needed no time
    /// at all to hear a tone and act on it. This is a theoretical endpoint, and it is
    /// <b>not</b> the bound the cue uses; see <see cref="OnsetFrame"/>.
    ///
    /// <para>A press does not join the partners' run part-way through: 128:1:4 byte 0
    /// starts Cloud's <b>own complete</b> <c>BC 03 00 19 01</c>, and 5[12] only becomes 1
    /// at byte 5, once that whole twenty-five-frame segment has finished. So a press made
    /// when the partners are at frame f lands at f + 25, not at 25.</para>
    ///
    /// <para>What it has to land inside is 128:6:3 - the LINE's own Move, not the
    /// director's input script. That loop waits 30, requests both partners' script 3,
    /// waits 24, clears 5[12] at byte 62, and then runs its six checks with a backward
    /// jump. The scheduler makes those numbers ticks rather than guesses: FUN_0060C94D
    /// gives an entity up to eight opcodes a tick and stops on a non-zero handler return,
    /// the JMPB handler FUN_00613030 returns 1 so every backward branch yields the tick,
    /// and WAIT FUN_00610818 decrements once a tick. The per-model animation step is
    /// initialised to 0x10 by FUN_0060BCFA and BC divides it by a slowness of 1, and 128
    /// overrides no animation speed, so one frame really is one tick.</para>
    ///
    /// <para>That gives roughly thirty ticks between the partners starting and the checks
    /// running out, and a press at frame f finishing at f + 25, so five is the largest f
    /// whose press still finishes inside the window. It is the instant the arithmetic
    /// ends at, with nothing left over: a cue delivered at frame five would be asking for
    /// a press in the same instant it becomes impossible, and it also assumes the
    /// scheduler's endpoints are exact when the counts above could each move by a tick.
    /// So five is recorded here as the limit of the calculation and the cue is kept
    /// strictly inside it.</para>
    /// </summary>
    public const int LatestUsefulFrame = 5;

    /// <summary>
    /// How far into the raise a first sight of it still counts as its beginning, and the
    /// only window in which a cue is given.
    ///
    /// <para>Three frames of the twenty-five, and the gap between this and
    /// <see cref="LatestUsefulFrame"/> is the whole of the margin. That margin is small -
    /// the field runs a frame a tick, so it is a few hundredths of a second - and it is
    /// not offered as an allowance for human response time, because no offline reading of
    /// the scripts can establish one. What it does mean is that a cue is only ever given
    /// while the arms are visibly starting up, never at the instant the arithmetic runs
    /// out, and never on the strength of an endpoint that might be a tick out.</para>
    ///
    /// <para>The honest statement of what this cue is: it marks the moment a sighted
    /// player would see the raise begin, at the earliest point it can be seen. Whether a
    /// given press then lands is a matter of live response time, and nothing here has
    /// been timed in a running game. A first reading taken past this window is silent
    /// rather than misleading - that attempt gets no tone at all - and each retry starts
    /// the segment again and is judged again from its own beginning.</para>
    /// </summary>
    public const int OnsetFrame = 2;

    private bool cuedThisRaise;

    public void Reset()
    {
        cuedThisRaise = false;
    }

    public Reactor5ButtonCueResult Observe(Reactor5ButtonObservation observation)
    {
        if (!observation.IsDirectorListening)
        {
            // The event is over, has not begun, or the field has gone. Either way the
            // next raise is a fresh one.
            Reset();
            return default;
        }

        var frames = new[] { observation.Barret, observation.Tifa }
            .Where(IsRaising)
            .Select(CurrentFrame)
            .ToArray();
        if (frames.Length == 0)
        {
            // Nobody is visibly raising, so whatever comes next is a fresh attempt.
            cuedThisRaise = false;
            return default;
        }

        if (cuedThisRaise)
        {
            return default;
        }

        // One decision per raise, taken the first time it is seen. Only the beginning of
        // the raise is a moment to press with; a first reading taken any later than that
        // is deliberately silent rather than misleading, and this attempt gets no tone at
        // all. The bound is the onset window, not the frame the arithmetic ends at.
        cuedThisRaise = true;
        return frames.Min() <= OnsetFrame
            ? new Reactor5ButtonCueResult(true)
            : default;
    }

    private static bool IsRaising(FieldScriptControllerState state) =>
        state.IsControllerActive &&
        state.IsPlayingSegment(RaiseAnimationId, RaiseEndFrame) &&
        state.AnimationRunState is BlockingPartialRunState or AsynchronousPartialRunState &&
        CurrentFrame(state) >= 0 &&
        CurrentFrame(state) <= RaiseEndFrame;

    private static int CurrentFrame(FieldScriptControllerState state) =>
        state.CurrentFrameFixed >> FieldScriptControllerReader.FrameFixedShift;
}

using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Reactor 5's three buttons. The field tells the player in its own words to push at the
/// same time as the others and then holds everyone still while it listens for a Confirm;
/// what is missing without sight is the moment, which is the start of Barret and Tifa's
/// raise. These cases are about giving that moment and no other: not the sound the script
/// plays when their hands are already down, not the byte that records a success, and
/// nothing at all when the tone would arrive too late to be pressed with.
/// </summary>
internal static class Reactor5ButtonCueTests
{
    internal static void Run()
    {
        TheStartOfTheRaiseIsCued();
        ALateFirstLookIsSilentRatherThanMisleading();
        TheArithmeticsEndpointIsNotItselfACue();
        EachRetryIsCuedAgain();
        NothingIsCuedWhileTheFieldIsNotListening();
        AnIdleOrDifferentPoseIsNotTheRaise();
        OnlyANativePartialPlaybackStateIsARaise();
        AnImpossibleFrameIsNotAnEarlyRaise();
    }

    private static void TheStartOfTheRaiseIsCued()
    {
        var tracker = new Reactor5ButtonCueTracker();
        Equal(false, tracker.Observe(Waiting()).PlayCue, "waiting on its own is not the moment");

        var start = Raising(frame: 0);
        Equal(true, tracker.Observe(start).PlayCue, "the raise beginning is the moment");
        Equal(false, tracker.Observe(Raising(frame: 4)).PlayCue, "the same raise is cued once");
        Equal(false, tracker.Observe(Raising(frame: 20)).PlayCue, "and not again as it finishes");
    }

    private static void ALateFirstLookIsSilentRatherThanMisleading()
    {
        // Cloud's own press takes the same twenty-five frames, so a tone raised when
        // their arms are nearly down asks for a press that cannot land in time.
        var tracker = new Reactor5ButtonCueTracker();
        Equal(false, tracker.Observe(Raising(frame: 22)).PlayCue,
            "attaching late must not claim this is the moment");
        Equal(false, tracker.Observe(Raising(frame: 24)).PlayCue,
            "and must not change its mind part way through the same raise");
    }

    /// <summary>
    /// The cue window and the arithmetic's endpoint are two different numbers, and this
    /// is the difference between them.
    ///
    /// <para><see cref="Reactor5ButtonCueTracker.LatestUsefulFrame"/> is the last frame
    /// whose press finishes inside 128:6:3's checking window, for a player who needed no
    /// time at all to hear a tone and act on it. A cue given there asks for a press in
    /// the same instant it stops being possible, and it also treats the scheduler's
    /// counts as exact when each of them could be a tick out. So the tone belongs to the
    /// onset of the raise, strictly inside that endpoint, and the endpoint itself is
    /// never a fresh cue.</para>
    /// </summary>
    private static void TheArithmeticsEndpointIsNotItselfACue()
    {
        Equal(true, Reactor5ButtonCueTracker.OnsetFrame < Reactor5ButtonCueTracker.LatestUsefulFrame,
            "the onset window has to leave a margin before the frame the arithmetic ends at");

        for (var frame = 0; frame <= Reactor5ButtonCueTracker.OnsetFrame; frame++)
        {
            var tracker = new Reactor5ButtonCueTracker();
            Equal(true, tracker.Observe(Raising(frame)).PlayCue,
                $"frame {frame} is still the raise beginning and is the moment to give");
        }

        var endpoint = new Reactor5ButtonCueTracker();
        Equal(false, endpoint.Observe(Raising(Reactor5ButtonCueTracker.LatestUsefulFrame)).PlayCue,
            "the last frame a press could theoretically land is not a useful fresh cue");
        Equal(false, endpoint.Observe(Raising(frame: 0)).PlayCue,
            "and having been silent for this raise it stays silent for the rest of it");

        for (var frame = Reactor5ButtonCueTracker.OnsetFrame + 1;
             frame <= Reactor5ButtonCueTracker.LatestUsefulFrame;
             frame++)
        {
            var tracker = new Reactor5ButtonCueTracker();
            Equal(false, tracker.Observe(Raising(frame)).PlayCue,
                $"frame {frame} is inside the margin, not the onset, and must be silent");
        }
    }

    private static void EachRetryIsCuedAgain()
    {
        var tracker = new Reactor5ButtonCueTracker();
        Equal(true, tracker.Observe(Raising(frame: 0)).PlayCue, "the first attempt is cued");
        Equal(false, tracker.Observe(Raising(frame: 18)).PlayCue, "and not repeated");

        // The loop comes back round: the pose stops being the raise and then starts again.
        Equal(false, tracker.Observe(Waiting()).PlayCue, "the gap between attempts is quiet");
        Equal(true, tracker.Observe(Raising(frame: 1)).PlayCue, "the next attempt is cued too");
    }

    private static void NothingIsCuedWhileTheFieldIsNotListening()
    {
        var tracker = new Reactor5ButtonCueTracker();

        // Before the event, after it, and while the field is being torn down, 128:0's
        // script 3 is not running and there is nothing to press with.
        Equal(false, tracker.Observe(Raising(frame: 0) with { IsDirectorListening = false }).PlayCue,
            "a pose without the listening director is not this event");

        // And having been through one raise does not leave anything behind.
        Equal(true, tracker.Observe(Raising(frame: 0)).PlayCue, "the event proper is still cued");
        Equal(false, tracker.Observe(default).PlayCue, "teardown is silent");
        Equal(true, tracker.Observe(Raising(frame: 0)).PlayCue, "and a fresh event starts clean");
    }

    private static void AnIdleOrDifferentPoseIsNotTheRaise()
    {
        var tracker = new Reactor5ButtonCueTracker();

        // The same animation is played again from frame 25 to 39 once their hands are on
        // the button. That segment is not the moment to press with.
        var afterwards = Raising(frame: 26) with
        {
            Barret = Segment(Reactor5ButtonCueTracker.RaiseAnimationId, 39, 26),
            Tifa = Segment(Reactor5ButtonCueTracker.RaiseAnimationId, 39, 26)
        };
        Equal(false, tracker.Observe(afterwards).PlayCue,
            "the second half of the animation is not the raise");

        // A model that is not being drawn is not evidence of a pose at all.
        var hidden = Raising(frame: 0) with
        {
            Barret = Segment(Reactor5ButtonCueTracker.RaiseAnimationId, 25, 0, visible: false),
            Tifa = Segment(Reactor5ButtonCueTracker.RaiseAnimationId, 25, 0, visible: false)
        };
        Equal(false, tracker.Observe(hidden).PlayCue, "an undrawn model is not a raise");
    }

    /// <summary>
    /// FUN_0060CE26 distinguishes the run states, and only the partial-playback ones mean
    /// a segment is on its way somewhere: 1 loops, 3 and 4 clamp at the end. Opcode BC -
    /// which is what both partners use at 128:2:3 and 128:3:3 - takes the blocking form
    /// and FUN_00614E3E writes 2 for it. An idle, looping or held model showing an early
    /// frame is a stale record, not an arm going up.
    /// </summary>
    private static void OnlyANativePartialPlaybackStateIsARaise()
    {
        foreach (var runState in new[] { 0, 1, 3, 4, 5 })
        {
            var tracker = new Reactor5ButtonCueTracker();
            var stale = Raising(frame: 0) with
            {
                Barret = Segment(
                    Reactor5ButtonCueTracker.RaiseAnimationId,
                    Reactor5ButtonCueTracker.RaiseEndFrame,
                    0,
                    runState: runState),
                Tifa = Segment(
                    Reactor5ButtonCueTracker.RaiseAnimationId,
                    Reactor5ButtonCueTracker.RaiseEndFrame,
                    0,
                    runState: runState)
            };
            Equal(false, tracker.Observe(stale).PlayCue,
                $"run state {runState} is not a partial playback and is not a raise");
        }

        // The asynchronous form of the same handler is still a real partial playback.
        var asynchronous = new Reactor5ButtonCueTracker();
        var playing = Raising(frame: 0) with
        {
            Barret = Segment(
                Reactor5ButtonCueTracker.RaiseAnimationId,
                Reactor5ButtonCueTracker.RaiseEndFrame,
                0,
                runState: Reactor5ButtonCueTracker.AsynchronousPartialRunState),
            Tifa = Segment(
                Reactor5ButtonCueTracker.RaiseAnimationId,
                Reactor5ButtonCueTracker.RaiseEndFrame,
                0,
                runState: Reactor5ButtonCueTracker.AsynchronousPartialRunState)
        };
        Equal(true, asynchronous.Observe(playing).PlayCue, "run state 6 is a partial playback too");
    }

    /// <summary>
    /// The segment runs from 0 to 25. A negative current frame is a record that could not
    /// be believed, not a pose that has not started yet, and a frame past the end belongs
    /// to some other play of the animation.
    /// </summary>
    private static void AnImpossibleFrameIsNotAnEarlyRaise()
    {
        foreach (var frame in new[] { -1, -16, 26, 100 })
        {
            var tracker = new Reactor5ButtonCueTracker();
            Equal(false, tracker.Observe(Raising(frame)).PlayCue,
                $"frame {frame} is outside the segment and is not an early raise");
        }
    }

    private static Reactor5ButtonObservation Waiting() =>
        new(true, Segment(-1, 0, 0, hasModel: true), Segment(-1, 0, 0, hasModel: true));

    private static Reactor5ButtonObservation Raising(int frame) =>
        new(
            true,
            Segment(Reactor5ButtonCueTracker.RaiseAnimationId, Reactor5ButtonCueTracker.RaiseEndFrame, frame),
            Segment(Reactor5ButtonCueTracker.RaiseAnimationId, Reactor5ButtonCueTracker.RaiseEndFrame, frame));

    private static FieldScriptControllerState Segment(
        int animationId,
        int endFrame,
        int frame,
        bool visible = true,
        bool hasModel = true,
        int runState = Reactor5ButtonCueTracker.BlockingPartialRunState) =>
        new(
            IsControllerActive: true,
            HasModel: hasModel,
            IsModelVisible: visible,
            AnimationId: animationId,
            CurrentFrameFixed: FieldScriptControllerReader.ToFixedFrame(frame),
            EndFrameIndex: endFrame,
            AnimationRunState: runState);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }
}

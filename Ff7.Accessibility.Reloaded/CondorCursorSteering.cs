using Ff7.Accessibility.Core;

namespace Ff7.Accessibility.Reloaded;

/// <summary>What a steering step did.</summary>
internal enum CondorSteeringOutcome
{
    /// <summary>No jump is running.</summary>
    Idle,

    /// <summary>Still travelling.</summary>
    Steering,

    /// <summary>The cursor is on the target and the keys are released.</summary>
    Arrived,

    /// <summary>Given up on, keys released, with a reason to say out loud.</summary>
    Abandoned
}

internal readonly record struct CondorSteeringStep(
    CondorSteeringOutcome Outcome,
    string? Speech);

/// <summary>
/// Takes the Fort Condor cursor to a point by holding the game's own direction
/// keys, and lets go when it arrives.
/// </summary>
/// <remarks>
/// <para>The cursor global at 0x00CBCCC0 is camera-relative and cannot be
/// written directly: <c>FUN_005FE91B</c> derives the cursor as
/// <c>cursor - camera</c>, clamps the relative value and moves the camera origin
/// and the scroll accumulators in lockstep, so a value stored into the world
/// global alone is carried straight through and never brought back. The cursor
/// also <em>is</em> the hire position, so a teleport followed by a purchase
/// spends real gil placing a unit off the field. See
/// <see cref="CondorCursorMover"/>, which refuses that write and always will.
/// </para>
///
/// <para>Pressing the game's own keys is correct by construction, because the
/// game moves camera, accumulators and cursor together itself. What this class
/// owns is knowing when to stop.</para>
///
/// <para><b>Closed loop, never open.</b> Every step reads where the cursor
/// actually is and decides again. Nothing here assumes a keystroke landed, that
/// the cursor moved, or that it moved the way it was asked to - each of those is
/// checked, and any of them failing abandons the jump and releases every key.
/// The failure this must never produce is a key left held down in the player's
/// game.</para>
/// </remarks>
internal sealed class CondorCursorSteering : IDisposable
{
    /// <summary>
    /// How close counts as arrived, on each axis.
    ///
    /// <para>The game's own unit selection box is 13 wide and runs from 10 above
    /// the cursor to 14 below, so landing inside this tolerance puts the cursor
    /// on the unit by the game's own reckoning rather than merely near it.</para>
    /// </summary>
    internal const int ArrivalTolerance = 4;

    /// <summary>
    /// Consecutive readings with the cursor not moving while a key is held. The
    /// battlefield has edges and the cursor stops dead at them, so this is the
    /// ordinary way a jump to an unreachable point ends.
    /// </summary>
    internal const int StallLimit = 24;

    /// <summary>
    /// An absolute ceiling on one jump, so a mistake cannot hold keys forever.
    /// Generous: the far corner of the field is well under this.
    /// </summary>
    internal const int SampleLimit = 1500;

    /// <summary>
    /// How much further from the target than it started the cursor may drift
    /// before the jump is abandoned.
    ///
    /// <para>This is the guard against the direction mapping being wrong. If
    /// holding Down were to decrease Y rather than increase it, the cursor would
    /// run away from the target, and the honest response is to stop and say so
    /// rather than to drive the cursor to the edge of the map.</para>
    /// </summary>
    internal const int DivergenceSlack = 48;

    /// <summary>
    /// The direction bits the battle reads out of its own held mask at
    /// 0x00C72E80, which <c>FUN_005FE771</c> repeats from.
    /// </summary>
    internal const uint MaskUp = 0x1000;
    internal const uint MaskRight = 0x2000;
    internal const uint MaskDown = 0x4000;
    internal const uint MaskLeft = 0x8000;

    /// <summary>
    /// How many readings a synthesized key gets to show up in the game's own
    /// held mask before the jump is abandoned.
    /// </summary>
    /// <remarks>
    /// <para>The single most important check here. Module 9 does not read the
    /// keyboard the way the rest of the mod does: it polls DirectInput's
    /// immediate state, applies the player's own <c>ff7input.cfg</c> mapping, and
    /// only then sets the bits this mask exposes. A keystroke can therefore be
    /// accepted by Windows and still mean nothing to the battle - because the
    /// injection was filtered, or because the player has that direction bound to
    /// a key we did not press.</para>
    ///
    /// <para>Without this the loop would be open: press, hope, and drive a cursor
    /// that is not moving until the stall check happens to notice. With it the
    /// jump fails in a fraction of a second and says so.</para>
    /// </remarks>
    internal const int AcknowledgementLimit = 3;

    private readonly HighwayAutoSteeringController keys;
    private readonly Action<string> log;

    private (int X, int Y)? target;
    private (int X, int Y)? lastCursor;
    private int startingDistance;
    private int stalled;
    private int samples;
    private uint awaitingMask;
    private int unacknowledged;
    private bool disposed;

    internal CondorCursorSteering(
        HighwayAutoSteeringController keys,
        Action<string>? log = null)
    {
        this.keys = keys ?? throw new ArgumentNullException(nameof(keys));
        this.log = log ?? (_ => { });
    }

    /// <summary>Whether a jump is running.</summary>
    internal bool IsSteering => target is not null;

    /// <summary>
    /// Starts a jump. Replaces any jump already running, because the player
    /// pressing the key again means they changed their mind.
    /// </summary>
    internal void Begin(int targetX, int targetY, int cursorX, int cursorY)
    {
        target = (targetX, targetY);
        lastCursor = null;
        stalled = 0;
        samples = 0;
        awaitingMask = 0;
        unacknowledged = 0;
        startingDistance = Distance(targetX - cursorX, targetY - cursorY);
        log($"Fort Condor steering: going to {targetX}, {targetY} from {cursorX}, {cursorY}.");
    }

    /// <summary>
    /// One closed-loop step.
    /// </summary>
    /// <param name="cursorReadable">
    /// Whether the cursor could be read at all this pass. A jump steered on a
    /// stale position is a jump steered blind.
    /// </param>
    /// <param name="underCursorControl">
    /// Whether the battle is still in the state where direction keys move the
    /// cursor - module 9, foreground, ordinary cursor mode, no modal overlay and
    /// no report. If a menu opened, the same keys now move something else.
    /// </param>
    /// <param name="heldDirectionMask">
    /// The battle's own held mask at 0x00C72E80, masked to its direction bits.
    /// This is the game telling us which directions it currently believes are
    /// down - the only trustworthy evidence that a synthesized key arrived.
    /// </param>
    internal CondorSteeringStep Step(
        bool cursorReadable,
        bool underCursorControl,
        int cursorX,
        int cursorY,
        uint heldDirectionMask)
    {
        if (target is not { } destination)
        {
            return new CondorSteeringStep(CondorSteeringOutcome.Idle, null);
        }

        if (!underCursorControl)
        {
            // Not a failure worth a sentence: the player opened a menu or the
            // battle ended, and they know they did.
            return Stop(CondorSteeringOutcome.Abandoned, null, "cursor control was taken away");
        }

        if (!cursorReadable)
        {
            return Stop(
                CondorSteeringOutcome.Abandoned,
                "Lost track of the cursor.",
                "the cursor could not be read");
        }

        if (++samples > SampleLimit)
        {
            return Stop(
                CondorSteeringOutcome.Abandoned,
                "Could not get there.",
                $"gave up after {SampleLimit} readings");
        }

        // Before anything else: did the keys we pressed last time actually reach
        // the battle? Checked first so a jump that is pressing keys into the void
        // fails in a fraction of a second rather than waiting for the stall
        // count, and so nothing downstream reasons about a cursor that was never
        // going to move.
        if (awaitingMask != 0)
        {
            if ((heldDirectionMask & awaitingMask) == awaitingMask)
            {
                awaitingMask = 0;
                unacknowledged = 0;
            }
            else if (++unacknowledged > AcknowledgementLimit)
            {
                return Stop(
                    CondorSteeringOutcome.Abandoned,
                    "The game is not taking the direction keys.",
                    $"the battle never reported holding 0x{awaitingMask:X4}; " +
                    $"its mask was 0x{heldDirectionMask:X4}");
            }
        }

        var dx = destination.X - cursorX;
        var dy = destination.Y - cursorY;

        if (Math.Abs(dx) <= ArrivalTolerance && Math.Abs(dy) <= ArrivalTolerance)
        {
            // Deliberately silent. The cursor readout announces where the cursor
            // came to rest and what is standing there, the moment the keys are
            // released - saying it here as well would be the same duplicate that
            // the opening line had to be fixed for.
            return Stop(
                CondorSteeringOutcome.Arrived,
                null,
                $"arrived at {cursorX}, {cursorY} after {samples} readings");
        }

        if (Distance(dx, dy) > startingDistance + DivergenceSlack)
        {
            return Stop(
                CondorSteeringOutcome.Abandoned,
                "Could not get there.",
                $"the cursor moved away from the target, now at {cursorX}, {cursorY}");
        }

        if (lastCursor == (cursorX, cursorY))
        {
            if (++stalled > StallLimit)
            {
                return Stop(
                    CondorSteeringOutcome.Abandoned,
                    "Could not get there.",
                    $"the cursor stopped moving at {cursorX}, {cursorY}");
            }
        }
        else
        {
            stalled = 0;
        }

        lastCursor = (cursorX, cursorY);

        var direction = ResolveDirection(dx, dy);
        var result = keys.Apply(direction);
        if (!result.Success)
        {
            return Stop(
                CondorSteeringOutcome.Abandoned,
                "Could not get there.",
                $"the keystrokes were refused: {result.Diagnostic}");
        }

        // Whatever we just asked to be held is what the battle must report back.
        // Only start waiting on a fresh request: re-arming every step would reset
        // the count each pass and the check would never fire.
        var requested = MaskFor(direction);
        if (requested != awaitingMask && requested != 0)
        {
            awaitingMask = requested;
            unacknowledged = 0;
        }

        return new CondorSteeringStep(CondorSteeringOutcome.Steering, null);
    }

    /// <summary>
    /// Ends any running jump and releases every key, for a caller that knows
    /// something has changed - leaving the battle, unloading, a fault elsewhere.
    /// </summary>
    internal void Cancel(string reason)
    {
        if (target is null)
        {
            return;
        }

        Stop(CondorSteeringOutcome.Abandoned, null, reason);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        target = null;
        keys.Dispose();
    }

    /// <summary>
    /// Higher Y is further down the mountain, towards the enemy, so a larger
    /// target Y means Down. An axis already inside the tolerance is left alone
    /// rather than nudged, so the finish on one axis does not disturb the other.
    /// </summary>
    private static HighwaySteeringDirection ResolveDirection(int dx, int dy)
    {
        var horizontal = Math.Abs(dx) > ArrivalTolerance ? Math.Sign(dx) : 0;
        var vertical = Math.Abs(dy) > ArrivalTolerance ? Math.Sign(dy) : 0;

        return (horizontal, vertical) switch
        {
            (0, < 0) => HighwaySteeringDirection.Up,
            (0, > 0) => HighwaySteeringDirection.Down,
            (< 0, 0) => HighwaySteeringDirection.Left,
            (> 0, 0) => HighwaySteeringDirection.Right,
            (< 0, < 0) => HighwaySteeringDirection.UpLeft,
            (> 0, < 0) => HighwaySteeringDirection.UpRight,
            (< 0, > 0) => HighwaySteeringDirection.DownLeft,
            (> 0, > 0) => HighwaySteeringDirection.DownRight,
            _ => HighwaySteeringDirection.None
        };
    }

    private static int Distance(int dx, int dy) => Math.Abs(dx) + Math.Abs(dy);

    /// <summary>The bits the battle should report while this direction is held.</summary>
    private static uint MaskFor(HighwaySteeringDirection direction) => direction switch
    {
        HighwaySteeringDirection.Up => MaskUp,
        HighwaySteeringDirection.Down => MaskDown,
        HighwaySteeringDirection.Left => MaskLeft,
        HighwaySteeringDirection.Right => MaskRight,
        HighwaySteeringDirection.UpLeft => MaskUp | MaskLeft,
        HighwaySteeringDirection.UpRight => MaskUp | MaskRight,
        HighwaySteeringDirection.DownLeft => MaskDown | MaskLeft,
        HighwaySteeringDirection.DownRight => MaskDown | MaskRight,
        _ => 0
    };

    private CondorSteeringStep Stop(
        CondorSteeringOutcome outcome,
        string? speech,
        string diagnostic)
    {
        target = null;
        lastCursor = null;
        stalled = 0;
        samples = 0;
        awaitingMask = 0;
        unacknowledged = 0;

        // Released before anything else can go wrong. Every exit from a jump
        // comes through here for exactly this reason.
        var release = keys.ReleaseAll();
        if (!release.Success)
        {
            log($"Fort Condor steering: keys may still be held: {release.Diagnostic}");
        }

        log($"Fort Condor steering: {diagnostic}.");
        return new CondorSteeringStep(outcome, speech);
    }
}

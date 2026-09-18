using Ff7.Accessibility.Core;

namespace Ff7.Accessibility.Reloaded;

internal readonly record struct JunonParadeAlignmentStep(
    bool ClaimsFieldInput,
    bool IsAssistActive,
    string? Speech);

/// <summary>
/// Keeps Cloud in Junon's moving welcome-parade formation by holding only the
/// player's own mapped direction keys. The player remains the sole owner of OK.
/// </summary>
/// <remarks>
/// The native join/score line is rebuilt every field-script pass from soldier
/// entity 27. The state reader exposes an open interior point on that live line, so
/// this controller follows the formation rather than a coordinate remembered
/// from an earlier frame.
/// </remarks>
internal sealed class JunonParadeAlignmentAssist : IDisposable
{
    internal const int AcknowledgementLimit = 3;
    internal const int StallLimit = 24;
    internal const int UnreadableLimit = 3;
    internal const int JoinSampleLimit = 1000;
    internal const int JoinSettlementLimit = 8;
    internal const int TrackingTolerance = 8;
    internal const int ManualReleaseSettlementSamples = 2;

    private const int ReleasedDirectionGraceSamples = 2;
    // junonr4 keeps window 2 open as the Live TV Ratings HUD throughout the
    // minigame. Its Now window is also movable gameplay; other windows pause.
    private const byte PersistentRatingsWindowCount = 1;
    private const float SecondaryAxisRatio = 0.35f;
    private const uint DirectionMask =
        FieldNavigationInputReader.UpMask |
        FieldNavigationInputReader.RightMask |
        FieldNavigationInputReader.DownMask |
        FieldNavigationInputReader.LeftMask;

    private readonly HighwayAutoSteeringController keys;
    private readonly Action<string> log;
    private readonly IFieldNavigationRoutePlanner? routePlanner;

    private bool inParade;
    private bool disabledForParade;
    private bool manualOverrideActive;
    private bool waitingForRoute;
    private bool guardNextUnpausedDirection;
    private bool disposed;
    private (int X, int Y)? lastPlayer;
    private int stalledSamples;
    private int joinSamples;
    private int joinSettlementSamples;
    private int unreadableSamples;
    private int manualReleaseSamples;
    private uint requestedMask;
    private uint awaitingMask;
    private int unacknowledgedSamples;
    private uint releaseGraceMask;
    private int releaseGraceSamples;

    internal JunonParadeAlignmentAssist(
        HighwayAutoSteeringController keys,
        Action<string>? log = null,
        IFieldNavigationRoutePlanner? routePlanner = null)
    {
        this.keys = keys ?? throw new ArgumentNullException(nameof(keys));
        this.log = log ?? (_ => { });
        this.routePlanner = routePlanner;
    }

    internal bool IsAssistActive =>
        inParade && !disabledForParade && !manualOverrideActive && !disposed;

    internal JunonParadeAlignmentStep Observe(
        JunonMinigameSnapshot snapshot,
        bool enabled)
    {
        if (disposed)
        {
            return default;
        }

        if (!enabled ||
            snapshot.Kind != JunonMinigameKind.WelcomeParade ||
            !snapshot.Parade.IsActive)
        {
            Reset("welcome parade inactive");
            return default;
        }

        if (!inParade)
        {
            BeginParade();
        }

        if (disabledForParade)
        {
            return new JunonParadeAlignmentStep(true, false, null);
        }

        var state = snapshot.Parade;
        var hasBlockingMessage = state.ActiveMessageCount > PersistentRatingsWindowCount &&
            !(state.JoinedFormation && state.ActiveMessageCount == 2 && state.IsNowPromptVisible);
        unreadableSamples = 0;
        if (state.UserControl != 0 ||
            hasBlockingMessage ||
            state.MovieActive != 0)
        {
            Pause(
                hasBlockingMessage
                    ? "native dialogue window"
                    : state.MovieActive != 0
                        ? "field movie"
                        : "scripted control lock",
                guardDirectionOnResume: true);
            return new JunonParadeAlignmentStep(true, true, null);
        }

        var heldDirections = state.HeldInput & DirectionMask;
        if (manualOverrideActive)
        {
            return ObserveManualOverride(heldDirections);
        }

        if (guardNextUnpausedDirection)
        {
            guardNextUnpausedDirection = false;
            if (heldDirections != 0)
            {
                Pause("direction held on scripted-lock release edge");
                return new JunonParadeAlignmentStep(true, true, null);
            }
        }

        if (!state.HasFormationTarget)
        {
            return ObserveUnavailable(enabled);
        }

        var acknowledged = awaitingMask == 0 ||
            (heldDirections & awaitingMask) == awaitingMask;
        if (acknowledged)
        {
            awaitingMask = 0;
            unacknowledgedSamples = 0;
        }
        else if (++unacknowledgedSamples > AcknowledgementLimit)
        {
            return Disable(
                "Parade alignment assist stopped. The game is not taking the direction keys.",
                $"direction 0x{awaitingMask:X4} was not acknowledged");
        }

        var allowedHeld = requestedMask | releaseGraceMask;
        var unownedHeld = heldDirections & ~allowedHeld;
        if (unownedHeld != 0)
        {
            return YieldToManualControl(
                "Parade alignment assist paused for manual control. Release the direction keys to resume.",
                $"unowned held direction 0x{unownedHeld:X4}");
        }

        if (releaseGraceSamples > 0)
        {
            releaseGraceSamples--;
            if (releaseGraceSamples == 0 || (heldDirections & releaseGraceMask) == 0)
            {
                releaseGraceMask = 0;
            }
        }

        // FUN_00637D35 sets the join event on proximity to the line. There is
        // no crossing-side condition; stop on the live interior target and let
        // the native script acknowledge the join before continuing to track it.
        var target = (X: state.FormationTargetX, Y: state.FormationTargetY);
        var dx = target.X - state.PlayerX;
        var dy = target.Y - state.PlayerY;
        var distance = Math.Sqrt(dx * (double)dx + dy * (double)dy);
        if (!state.JoinedFormation && ++joinSamples > JoinSampleLimit)
        {
            return Disable(
                "Parade alignment assist stopped. Could not join the formation.",
                "the join-line approach timed out");
        }

        if (!state.JoinedFormation && distance <= TrackingTolerance)
        {
            if (++joinSettlementSamples > JoinSettlementLimit)
            {
                return Disable(
                    "Parade alignment assist stopped. Could not join the formation.",
                    "Cloud reached the native line without setting the joined latch");
            }
        }
        else
        {
            joinSettlementSamples = 0;
        }

        var steeringX = dx;
        var steeringY = dy;
        var routeResumed = waitingForRoute && distance <= TrackingTolerance;
        if (routeResumed)
        {
            waitingForRoute = false;
        }
        if (distance > TrackingTolerance && routePlanner is not null)
        {
            var navigationTarget = new FieldNavigationTarget(
                JunonMinigameStateReader.WelcomeParadeFieldId,
                FieldNavigationCategory.Story,
                "Parade formation",
                target.X, target.Y, state.NavigationPosition.Z,
                StableId: "junon-parade-formation");
            try
            {
                if (!routePlanner.TryGetNextWaypoint(state.NavigationPosition, navigationTarget, out var waypoint))
                {
                    if (routePlanner is IFieldNavigationRouteReadStatus { LastReadWasCoherent: true })
                    {
                        return WaitForClearRoute(routePlanner.LastDiagnostic);
                    }

                    return Disable(
                        "Parade alignment assist stopped. Could not read the path into the formation.",
                        $"native parade route unreadable: {routePlanner.LastDiagnostic}");
                }

                steeringX = waypoint.X - state.PlayerX;
                steeringY = waypoint.Y - state.PlayerY;
                routeResumed = waitingForRoute;
                waitingForRoute = false;
            }
            catch (InvalidDataException exception)
            {
                return Disable(
                    "Parade alignment assist stopped. Could not read the path into the formation.",
                    $"native parade route unreadable: {exception.Message}");
            }
        }

        var direction = distance <= TrackingTolerance
            ? HighwaySteeringDirection.None
            : ResolveDirection(state.ControlTransform.TransformWorldVector(steeringX, steeringY));

        if (direction != HighwaySteeringDirection.None && lastPlayer == (state.PlayerX, state.PlayerY))
        {
            if (++stalledSamples > StallLimit)
            {
                return Disable(
                    "Parade alignment assist stopped. Could not stay with the formation.",
                    $"Cloud stopped moving at {state.PlayerX}, {state.PlayerY}; " +
                    $"formation={state.FormationTargetX},{state.FormationTargetY}, " +
                    $"target={target.X},{target.Y}, held=0x{heldDirections:X4}, " +
                    $"requested=0x{requestedMask:X4}, joined={state.JoinedFormation}, " +
                    $"control={state.UserControl}, messages={state.ActiveMessageCount}, movie={state.MovieActive}");
            }
        }
        else
        {
            stalledSamples = 0;
        }

        lastPlayer = (state.PlayerX, state.PlayerY);
        var priorRequested = requestedMask;
        var result = keys.Apply(direction);
        if (!result.Success)
        {
            return Disable(
                "Parade alignment assist stopped. Directional input is unavailable.",
                result.Diagnostic);
        }

        var nextRequested = MaskFor(direction);
        if (nextRequested != requestedMask)
        {
            if (priorRequested != 0)
            {
                releaseGraceMask |= priorRequested;
                releaseGraceSamples = ReleasedDirectionGraceSamples;
            }

            requestedMask = nextRequested;
            awaitingMask = nextRequested;
            unacknowledgedSamples = 0;
        }

        if (routeResumed)
        {
            log("Junon parade alignment assist: native path cleared; resumed.");
        }

        return new JunonParadeAlignmentStep(
            true, true, routeResumed ? "Parade alignment assist resumed." : null);
    }

    internal JunonParadeAlignmentStep ObserveUnavailable(bool enabled)
    {
        if (disposed || !enabled || !inParade)
        {
            Reset("parade snapshot unavailable while inactive");
            return default;
        }

        // Preserve the original fault until a lifecycle reset. Later unreadable frames
        // must not repeat the warning or interrupt the remaining manual guidance.
        if (disabledForParade)
        {
            return new JunonParadeAlignmentStep(true, false, null);
        }

        Pause("parade snapshot unavailable");
        if (++unreadableSamples <= UnreadableLimit)
        {
            return new JunonParadeAlignmentStep(true, true, null);
        }

        return Disable(
            "Parade alignment assist stopped. Lost track of the parade.",
            "too many consecutive unreadable parade snapshots");
    }

    internal void Reset(string reason = "reset")
    {
        if (!inParade && requestedMask == 0 && !keys.HasOwnedKeys)
        {
            return;
        }

        var release = keys.ReleaseAll();
        if (!release.Success)
        {
            log($"Junon parade alignment assist: keys may still be held: {release.Diagnostic}");
        }

        inParade = false;
        disabledForParade = false;
        manualOverrideActive = false;
        waitingForRoute = false;
        guardNextUnpausedDirection = false;
        lastPlayer = null;
        stalledSamples = 0;
        joinSamples = 0;
        joinSettlementSamples = 0;
        unreadableSamples = 0;
        manualReleaseSamples = 0;
        requestedMask = 0;
        awaitingMask = 0;
        unacknowledgedSamples = 0;
        releaseGraceMask = 0;
        releaseGraceSamples = 0;
        log($"Junon parade alignment assist: {reason}.");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Reset("disposed");
        keys.Dispose();
        disposed = true;
    }

    private void BeginParade()
    {
        inParade = true;
        disabledForParade = false;
        manualOverrideActive = false;
        guardNextUnpausedDirection = false;
        manualReleaseSamples = 0;
        log("Junon parade alignment assist: active; OK remains under player control.");
    }

    private void Pause(string reason, bool guardDirectionOnResume = false)
    {
        var priorRequested = requestedMask;
        var release = keys.ReleaseAll();
        if (!release.Success)
        {
            log($"Junon parade alignment assist: key release failed during {reason}: {release.Diagnostic}");
        }

        if (priorRequested != 0)
        {
            releaseGraceMask |= priorRequested;
            releaseGraceSamples = ReleasedDirectionGraceSamples;
        }

        requestedMask = 0;
        awaitingMask = 0;
        unacknowledgedSamples = 0;
        stalledSamples = 0;
        lastPlayer = null;
        if (guardDirectionOnResume)
        {
            guardNextUnpausedDirection = true;
        }
    }

    private JunonParadeAlignmentStep ObserveManualOverride(uint heldDirections)
    {
        Pause("manual control");
        if (heldDirections != 0)
        {
            manualReleaseSamples = 0;
            return new JunonParadeAlignmentStep(true, false, null);
        }

        if (++manualReleaseSamples < ManualReleaseSettlementSamples)
        {
            return new JunonParadeAlignmentStep(true, false, null);
        }

        manualOverrideActive = false;
        manualReleaseSamples = 0;
        log("Junon parade alignment assist: manual direction released; resumed.");
        return new JunonParadeAlignmentStep(
            true,
            true,
            "Parade alignment assist resumed.");
    }

    private JunonParadeAlignmentStep YieldToManualControl(string speech, string diagnostic)
    {
        Pause(diagnostic);
        manualOverrideActive = true;
        waitingForRoute = false;
        manualReleaseSamples = 0;
        releaseGraceMask = 0;
        releaseGraceSamples = 0;
        log($"Junon parade alignment assist: {diagnostic}; temporarily yielding.");
        return new JunonParadeAlignmentStep(true, false, speech);
    }

    private JunonParadeAlignmentStep Disable(string speech, string diagnostic)
    {
        Pause(diagnostic);
        disabledForParade = true;
        manualOverrideActive = false;
        waitingForRoute = false;
        guardNextUnpausedDirection = false;
        manualReleaseSamples = 0;
        log($"Junon parade alignment assist: {diagnostic}.");
        return new JunonParadeAlignmentStep(true, false, speech);
    }

    private JunonParadeAlignmentStep WaitForClearRoute(string diagnostic)
    {
        Pause("waiting for the native parade route");
        if (waitingForRoute)
        {
            return new JunonParadeAlignmentStep(true, true, null);
        }

        waitingForRoute = true;
        log($"Junon parade alignment assist: waiting for an opening: {diagnostic}.");
        return new JunonParadeAlignmentStep(
            true, true, "Parade alignment assist waiting for an opening.");
    }

    private static HighwaySteeringDirection ResolveDirection(FieldNavigationStickDirection stick)
    {
        var horizontalMagnitude = Math.Abs(stick.X);
        var verticalMagnitude = Math.Abs(stick.Y);
        var dominant = Math.Max(horizontalMagnitude, verticalMagnitude);
        if (dominant <= 0.001f)
        {
            return HighwaySteeringDirection.None;
        }

        var horizontal = horizontalMagnitude >= dominant * SecondaryAxisRatio
            ? Math.Sign(stick.X)
            : 0;
        var vertical = verticalMagnitude >= dominant * SecondaryAxisRatio
            ? Math.Sign(stick.Y)
            : 0;
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

    private static uint MaskFor(HighwaySteeringDirection direction) => direction switch
    {
        HighwaySteeringDirection.Up => FieldNavigationInputReader.UpMask,
        HighwaySteeringDirection.Right => FieldNavigationInputReader.RightMask,
        HighwaySteeringDirection.Down => FieldNavigationInputReader.DownMask,
        HighwaySteeringDirection.Left => FieldNavigationInputReader.LeftMask,
        HighwaySteeringDirection.UpRight =>
            FieldNavigationInputReader.UpMask | FieldNavigationInputReader.RightMask,
        HighwaySteeringDirection.DownRight =>
            FieldNavigationInputReader.DownMask | FieldNavigationInputReader.RightMask,
        HighwaySteeringDirection.DownLeft =>
            FieldNavigationInputReader.DownMask | FieldNavigationInputReader.LeftMask,
        HighwaySteeringDirection.UpLeft =>
            FieldNavigationInputReader.UpMask | FieldNavigationInputReader.LeftMask,
        _ => 0
    };
}

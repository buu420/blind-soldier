namespace Ff7.Accessibility.Reloaded;

public enum Floor60GuardSpeechCue
{
    None,
    FindFirstHidingSpot,
    MoveNow,
    SignalNow,
    GuardSetPassed,
    HidingSpotReached,
    FirstGuardSectionPassed,
    SecondGuardSectionPassed
}

public readonly record struct Floor60HideSpot(
    int SequenceIndex,
    string Label,
    int X,
    int Y,
    int Z,
    ushort TriangleId,
    int GuardLineEntityId)
{
    public FieldNavigationTarget ToNavigationTarget(int interactionRadius) =>
        new(
            Floor60SoldierTurnCueTracker.FloorId,
            FieldNavigationCategory.Story,
            Label,
            X,
            Y,
            Z,
            $"floor60:statue:{SequenceIndex}",
            CompletesOnArrival: true,
            InteractionRadius: Math.Max(0, interactionRadius));
}

public readonly record struct Floor60GuardCueDecision(
    Floor60GuardSpeechCue SpeechCue,
    Floor60HideSpot? HideSpotTarget,
    bool PlayHideSpotBeacon,
    bool PlayActionCue,
    bool StopHideSpotBeacon)
{
    public static Floor60GuardCueDecision None { get; } =
        new(Floor60GuardSpeechCue.None, null, false, false, false);
}

public static class Floor60NavigationTargetMerger
{
    public static IReadOnlyList<FieldNavigationTarget> Merge(
        IReadOnlyList<FieldNavigationTarget> ordinaryTargets,
        FieldNavigationTarget? currentStatue)
    {
        ArgumentNullException.ThrowIfNull(ordinaryTargets);
        return currentStatue is
            {
                FieldId: Floor60SoldierTurnCueTracker.FloorId,
                Category: FieldNavigationCategory.Story
            } target
            ? [target]
            : ordinaryTargets;
    }
}

/// <summary>
/// Tracks the native floor 60 security-room sequence.
///
/// The field script moves Barret and Tifa through the same seven golden-statue
/// destinations Cloud must use. Each guarded gap has its own OLINE trigger:
/// 22-24 for the first crossing and 25-27 for the second. A gap is safe only
/// while its corresponding line is disabled; unrelated patrol lines do not
/// release the player.
/// </summary>
public sealed class Floor60SoldierTurnCueTracker
{
    public const ushort FloorId = 239;
    public const int BarretSignalingProgressBank = 5;
    public const int BarretSignalingProgressIndex = 12;
    public const int TifaSignalingProgressBank = 5;
    public const int TifaSignalingProgressIndex = 13;
    public const int SignalingProgressBank = TifaSignalingProgressBank;
    public const int SignalingProgressIndex = TifaSignalingProgressIndex;
    public const int MinigameActiveBank = 5;
    public const int MinigameActiveIndex = 14;
    public const int GuardsClearedBank = 3;
    public const int GuardsClearedIndex = 172;
    public const byte GuardsClearedMask = 0x08;
    public const byte SecondCrossingProgress = 3;
    public const byte SignalingCompleteProgress = 6;
    public const byte CaughtProgress = 7;
    public const int FirstCompletionLineEntityId = 21;
    public const int SecondCompletionLineEntityId = 28;
    public const int DefaultArrivalDistanceUnits = 60;
    public const int NativeFieldTicksPerSecond = 30;
    public const int DefaultGuardReactionLeadMilliseconds = 500;
    public const int DefaultGuardReactionLeadTicks = 15;
    public const int GuardPassDistanceUnits = 8;

    private static readonly Floor60HideSpot[] NativeHideSpots =
    [
        // The native field has six guarded gaps: three before the midpoint and
        // three after it. Index zero is the setup cover; each later destination
        // is tied to the OLINE trigger for the guarded gap immediately before it.
        // The triangle ids come from the native blin60_1 walkmesh.
        new(0, "Starting cover statue", -551, 248, 0, 152, -1),
        new(1, "First section, hiding statue 1 of 3", -396, 259, 0, 303, 22),
        new(2, "First section, hiding statue 2 of 3", -260, 251, 0, 307, 23),
        new(3, "First section, midpoint hiding statue 3 of 3", 7, 204, 0, 140, 24),
        new(4, "Second section, hiding statue 1 of 3", 267, 256, 0, 101, 25),
        new(5, "Second section, hiding statue 2 of 3", 407, 256, 0, 99, 26),
        new(6, "Second section, final hiding statue 3 of 3", 547, 252, 0, 314, 27)
    ];

    private static readonly Floor60GuardGap[] NativeGuardGaps =
    [
        new(22, -474, 292, -454, -176),
        new(23, -333, 292, -325, -184),
        new(24, -164, 398, -160, -142),
        new(25, 182, 392, 186, -176),
        new(26, 346, 292, 322, -176),
        new(27, 473, 292, 461, -188)
    ];

    public static IReadOnlyList<int> FirstLineEntityIds { get; } =
        Array.AsReadOnly([22, 23, 24]);

    public static IReadOnlyList<int> SecondLineEntityIds { get; } =
        Array.AsReadOnly([25, 26, 27]);

    public static IReadOnlyList<Floor60HideSpot> HideSpots { get; } =
        Array.AsReadOnly(NativeHideSpots);

    private readonly TimeSpan beaconPulseInterval;
    private readonly int arrivalDistanceUnits;
    private readonly int guardReactionLeadTicks;
    private Floor60GuardStage activeStage;
    private bool hasActiveObservation;
    private bool secondSectionAnnounced;
    private int activeTargetIndex = -1;
    private int waitingAtSpotIndex = -1;
    private bool targetReleased;
    private int observedGuardLineIndex = -1;
    private bool observedGuardLineEnabled;
    private ushort observedGuardWaitTicks;
    private bool guardCycleCueIssued;
    private bool guardPassAnnounced;
    private DateTime nextBeaconPulseAt;

    public Floor60SoldierTurnCueTracker()
        : this(
            TimeSpan.FromMilliseconds(500),
            DefaultArrivalDistanceUnits,
            DefaultGuardReactionLeadTicks)
    {
    }

    public Floor60SoldierTurnCueTracker(
        TimeSpan beaconPulseInterval,
        int arrivalDistanceUnits = DefaultArrivalDistanceUnits,
        int guardReactionLeadTicks = DefaultGuardReactionLeadTicks)
    {
        this.beaconPulseInterval = beaconPulseInterval < TimeSpan.Zero
            ? TimeSpan.Zero
            : beaconPulseInterval;
        this.arrivalDistanceUnits = Math.Max(0, arrivalDistanceUnits);
        this.guardReactionLeadTicks = Math.Clamp(guardReactionLeadTicks, 1, 59);
    }

    public static int ReactionLeadMillisecondsToTicks(int milliseconds) =>
        Math.Clamp(
            (int)Math.Round(
                Math.Max(0, milliseconds) *
                NativeFieldTicksPerSecond /
                1000.0,
                MidpointRounding.AwayFromZero),
            1,
            59);

    public DateTime LastObservedAt { get; private set; }

    public FieldNavigationTarget? CurrentNavigationTarget
    {
        get
        {
            if (!hasActiveObservation ||
                activeStage is not (
                    Floor60GuardStage.FirstCrossing or
                    Floor60GuardStage.SecondCrossing))
            {
                return null;
            }

            var targetIndex = waitingAtSpotIndex >= 0
                ? waitingAtSpotIndex
                : activeTargetIndex;
            return targetIndex >= 0 && targetIndex < NativeHideSpots.Length
                ? NativeHideSpots[targetIndex].ToNavigationTarget(arrivalDistanceUnits)
                : null;
        }
    }

    public Floor60GuardCueDecision Observe(
        FieldPositionSnapshot position,
        byte barretSignalingProgress,
        byte tifaSignalingProgress,
        bool minigameActive,
        bool guardsCleared,
        bool userControlLocked,
        bool firstCompletionLineEnabled,
        bool secondCompletionLineEnabled,
        bool firstLeftEnabled,
        bool firstMiddleEnabled,
        bool firstRightEnabled,
        bool secondLeftEnabled,
        bool secondMiddleEnabled,
        bool secondRightEnabled,
        Floor60GuardTimingSnapshot guardTiming,
        DateTime observedAt)
    {
        LastObservedAt = observedAt;
        if (!FieldPositionReader.IsUsable(position) ||
            position.CurrentModule != FieldPositionReader.FieldModule ||
            position.FieldId != FloorId ||
            !minigameActive)
        {
            Reset();
            return Floor60GuardCueDecision.None;
        }

        if (guardsCleared)
        {
            var shouldAnnounce = hasActiveObservation && !secondSectionAnnounced;
            secondSectionAnnounced = true;
            hasActiveObservation = false;
            activeStage = Floor60GuardStage.Completed;
            ClearBeaconState();
            return shouldAnnounce
                ? new Floor60GuardCueDecision(
                    Floor60GuardSpeechCue.SecondGuardSectionPassed,
                    null,
                    false,
                    false,
                    true)
                : Floor60GuardCueDecision.None;
        }

        if (barretSignalingProgress >= CaughtProgress ||
            tifaSignalingProgress >= CaughtProgress)
        {
            var stopBeacon = targetReleased || waitingAtSpotIndex >= 0;
            hasActiveObservation = false;
            activeStage = Floor60GuardStage.Caught;
            ClearBeaconState();
            return stopBeacon
                ? new Floor60GuardCueDecision(
                    Floor60GuardSpeechCue.None,
                    null,
                    false,
                    false,
                    true)
                : Floor60GuardCueDecision.None;
        }

        secondSectionAnnounced = false;
        var stage = ResolveStage(
            position,
            barretSignalingProgress,
            tifaSignalingProgress,
            userControlLocked,
            firstCompletionLineEnabled,
            secondCompletionLineEnabled);
        var lineStates = new[]
        {
            firstLeftEnabled,
            firstMiddleEnabled,
            firstRightEnabled,
            secondLeftEnabled,
            secondMiddleEnabled,
            secondRightEnabled
        };

        if (!hasActiveObservation)
        {
            hasActiveObservation = true;
            activeStage = stage;
            return InitializeStage(
                stage,
                position,
                barretSignalingProgress,
                tifaSignalingProgress,
                userControlLocked,
                lineStates,
                guardTiming,
                observedAt);
        }

        if (stage != activeStage)
        {
            var previousStage = activeStage;
            activeStage = stage;
            return ChangeStage(
                previousStage,
                stage,
                position,
                barretSignalingProgress,
                tifaSignalingProgress,
                userControlLocked,
                lineStates,
                guardTiming,
                observedAt);
        }

        return stage switch
        {
            Floor60GuardStage.FirstCrossing or Floor60GuardStage.SecondCrossing =>
                ObserveCloudCrossing(
                    position,
                    userControlLocked,
                    lineStates,
                    guardTiming,
                    observedAt),
            Floor60GuardStage.FirstSignaling or Floor60GuardStage.SecondSignaling =>
                ObservePartySignaling(
                    stage,
                    barretSignalingProgress,
                    tifaSignalingProgress,
                    lineStates,
                    guardTiming),
            _ => Floor60GuardCueDecision.None
        };
    }

    public void Reset()
    {
        hasActiveObservation = false;
        secondSectionAnnounced = false;
        activeStage = Floor60GuardStage.Inactive;
        ClearBeaconState();
        LastObservedAt = default;
    }

    private Floor60GuardCueDecision InitializeStage(
        Floor60GuardStage stage,
        FieldPositionSnapshot position,
        byte barretSignalingProgress,
        byte tifaSignalingProgress,
        bool userControlLocked,
        IReadOnlyList<bool> lineStates,
        Floor60GuardTimingSnapshot guardTiming,
        DateTime observedAt)
    {
        ClearBeaconState();
        switch (stage)
        {
            case Floor60GuardStage.FirstCrossing:
                InitializeCrossing(position, firstSpotIndex: 0, finalSpotIndex: 3);
                if (activeTargetIndex == 0)
                {
                    ReleaseTarget(observedAt);
                    return CreateCrossingDecision(
                        Floor60GuardSpeechCue.FindFirstHidingSpot,
                        userControlLocked,
                        observedAt);
                }

                return InitializeGuardedCrossingTarget(
                    userControlLocked,
                    lineStates,
                    guardTiming,
                    observedAt);

            case Floor60GuardStage.FirstSignaling:
                ObserveRequiredLine(
                    ResolveSignalLineIndex(
                        stage,
                        barretSignalingProgress,
                        tifaSignalingProgress),
                    lineStates,
                    guardTiming,
                    announceIfAlreadySafe: false);
                return new Floor60GuardCueDecision(
                    Floor60GuardSpeechCue.FirstGuardSectionPassed,
                    null,
                    false,
                    false,
                    true);

            case Floor60GuardStage.SecondCrossing:
                InitializeCrossing(position, firstSpotIndex: 4, finalSpotIndex: 6);
                return InitializeGuardedCrossingTarget(
                    userControlLocked,
                    lineStates,
                    guardTiming,
                    observedAt);

            case Floor60GuardStage.SecondSignaling:
                ObserveRequiredLine(
                    ResolveSignalLineIndex(
                        stage,
                        barretSignalingProgress,
                        tifaSignalingProgress),
                    lineStates,
                    guardTiming,
                    announceIfAlreadySafe: false);
                return new Floor60GuardCueDecision(
                    Floor60GuardSpeechCue.None,
                    null,
                    false,
                    false,
                    true);

            default:
                return Floor60GuardCueDecision.None;
        }
    }

    private Floor60GuardCueDecision ChangeStage(
        Floor60GuardStage previousStage,
        Floor60GuardStage stage,
        FieldPositionSnapshot position,
        byte barretSignalingProgress,
        byte tifaSignalingProgress,
        bool userControlLocked,
        IReadOnlyList<bool> lineStates,
        Floor60GuardTimingSnapshot guardTiming,
        DateTime observedAt)
    {
        ClearBeaconState();
        switch (stage)
        {
            case Floor60GuardStage.FirstSignaling:
                ObserveRequiredLine(
                    ResolveSignalLineIndex(
                        stage,
                        barretSignalingProgress,
                        tifaSignalingProgress),
                    lineStates,
                    guardTiming,
                    announceIfAlreadySafe: false);
                return new Floor60GuardCueDecision(
                    Floor60GuardSpeechCue.FirstGuardSectionPassed,
                    null,
                    false,
                    false,
                    true);

            case Floor60GuardStage.SecondCrossing:
                InitializeCrossing(position, firstSpotIndex: 4, finalSpotIndex: 6);
                return InitializeGuardedCrossingTarget(
                    userControlLocked,
                    lineStates,
                    guardTiming,
                    observedAt);

            case Floor60GuardStage.SecondSignaling:
                ObserveRequiredLine(
                    ResolveSignalLineIndex(
                        stage,
                        barretSignalingProgress,
                        tifaSignalingProgress),
                    lineStates,
                    guardTiming,
                    announceIfAlreadySafe: false);
                return new Floor60GuardCueDecision(
                    Floor60GuardSpeechCue.None,
                    null,
                    false,
                    false,
                    previousStage == Floor60GuardStage.SecondCrossing);

            default:
                return Floor60GuardCueDecision.None;
        }
    }

    private Floor60GuardCueDecision InitializeGuardedCrossingTarget(
        bool userControlLocked,
        IReadOnlyList<bool> lineStates,
        Floor60GuardTimingSnapshot guardTiming,
        DateTime observedAt)
    {
        if (activeTargetIndex < 0)
        {
            return Floor60GuardCueDecision.None;
        }

        if (waitingAtSpotIndex >= 0)
        {
            var nextIndex = waitingAtSpotIndex + 1;
            if (nextIndex >= NativeHideSpots.Length ||
                !IsIndexInStage(nextIndex, activeStage))
            {
                return Floor60GuardCueDecision.None;
            }

            activeTargetIndex = nextIndex;
            guardPassAnnounced = false;
        }

        var requiredLine = ResolveTargetGuardLineIndex(activeTargetIndex);
        var isSafe = ObserveRequiredLine(
            requiredLine,
            lineStates,
            guardTiming,
            announceIfAlreadySafe: true);
        if (!isSafe)
        {
            return Floor60GuardCueDecision.None;
        }

        waitingAtSpotIndex = -1;
        ReleaseTarget(observedAt);
        return CreateCrossingDecision(
            Floor60GuardSpeechCue.MoveNow,
            userControlLocked,
            observedAt);
    }

    private Floor60GuardCueDecision ObserveCloudCrossing(
        FieldPositionSnapshot position,
        bool userControlLocked,
        IReadOnlyList<bool> lineStates,
        Floor60GuardTimingSnapshot guardTiming,
        DateTime observedAt)
    {
        if (targetReleased &&
            activeTargetIndex >= 0 &&
            IsAtHideSpot(position, activeTargetIndex) &&
            !IsFinalSpotForStage(activeTargetIndex, activeStage))
        {
            waitingAtSpotIndex = activeTargetIndex;
            targetReleased = false;
            nextBeaconPulseAt = DateTime.MinValue;
            var nextTargetIndex = activeTargetIndex + 1;
            if (IsIndexInStage(nextTargetIndex, activeStage))
            {
                activeTargetIndex = nextTargetIndex;
                guardPassAnnounced = false;
                ObserveRequiredLine(
                    ResolveTargetGuardLineIndex(activeTargetIndex),
                    lineStates,
                    guardTiming,
                    announceIfAlreadySafe: false);
            }
            else
            {
                activeTargetIndex = -1;
                observedGuardLineIndex = -1;
            }

            return new Floor60GuardCueDecision(
                Floor60GuardSpeechCue.HidingSpotReached,
                null,
                false,
                false,
                true);
        }

        if (waitingAtSpotIndex >= 0)
        {
            if (!IsAtHideSpot(position, waitingAtSpotIndex))
            {
                activeTargetIndex = waitingAtSpotIndex;
                waitingAtSpotIndex = -1;
                guardPassAnnounced = false;
                ReleaseTarget(observedAt);
                return CreateCrossingDecision(
                    Floor60GuardSpeechCue.None,
                    userControlLocked,
                    observedAt);
            }

            var becameSafe = ObserveRequiredLine(
                ResolveTargetGuardLineIndex(activeTargetIndex),
                lineStates,
                guardTiming,
                announceIfAlreadySafe: false);
            if (becameSafe)
            {
                waitingAtSpotIndex = -1;
                ReleaseTarget(observedAt);
                return CreateCrossingDecision(
                    Floor60GuardSpeechCue.MoveNow,
                    userControlLocked,
                    observedAt);
            }

            return Floor60GuardCueDecision.None;
        }

        if (!targetReleased || activeTargetIndex < 0)
        {
            return Floor60GuardCueDecision.None;
        }

        var requiredLine = ResolveTargetGuardLineIndex(activeTargetIndex);
        var movementCue = ObserveRequiredLine(
            requiredLine,
            lineStates,
            guardTiming,
            announceIfAlreadySafe: false);
        if (!guardPassAnnounced &&
            HasPassedRequiredGuard(position, requiredLine, lineStates))
        {
            guardPassAnnounced = true;
            return CreateCrossingDecision(
                Floor60GuardSpeechCue.GuardSetPassed,
                userControlLocked,
                observedAt);
        }

        if (movementCue && !guardPassAnnounced)
        {
            return CreateCrossingDecision(
                Floor60GuardSpeechCue.MoveNow,
                userControlLocked,
                observedAt);
        }

        return CreateCrossingDecision(
            Floor60GuardSpeechCue.None,
            userControlLocked,
            observedAt);
    }

    private Floor60GuardCueDecision ObservePartySignaling(
        Floor60GuardStage stage,
        byte barretSignalingProgress,
        byte tifaSignalingProgress,
        IReadOnlyList<bool> lineStates,
        Floor60GuardTimingSnapshot guardTiming)
    {
        var lineIndex = ResolveSignalLineIndex(
            stage,
            barretSignalingProgress,
            tifaSignalingProgress);
        return ObserveRequiredLine(
            lineIndex,
            lineStates,
            guardTiming,
            announceIfAlreadySafe: true)
            ? new Floor60GuardCueDecision(
                Floor60GuardSpeechCue.SignalNow,
                null,
                false,
                true,
                false)
            : Floor60GuardCueDecision.None;
    }

    private Floor60GuardCueDecision CreateCrossingDecision(
        Floor60GuardSpeechCue speechCue,
        bool userControlLocked,
        DateTime observedAt)
    {
        if (!targetReleased || activeTargetIndex < 0)
        {
            return new Floor60GuardCueDecision(speechCue, null, false, false, false);
        }

        var target = NativeHideSpots[activeTargetIndex];
        var play = !userControlLocked && observedAt >= nextBeaconPulseAt;
        if (play)
        {
            nextBeaconPulseAt = observedAt + beaconPulseInterval;
        }

        return new Floor60GuardCueDecision(speechCue, target, play, false, false);
    }

    private void InitializeCrossing(
        FieldPositionSnapshot position,
        int firstSpotIndex,
        int finalSpotIndex)
    {
        guardPassAnnounced = false;
        var currentSpot = FindCurrentSpot(position, firstSpotIndex, finalSpotIndex);
        if (currentSpot >= 0 && currentSpot < finalSpotIndex)
        {
            waitingAtSpotIndex = currentSpot;
            activeTargetIndex = currentSpot + 1;
            targetReleased = false;
            return;
        }

        activeTargetIndex = InferNextSpot(position.X, firstSpotIndex, finalSpotIndex);
        waitingAtSpotIndex = -1;
        targetReleased = false;
    }

    private void ReleaseTarget(DateTime observedAt)
    {
        targetReleased = activeTargetIndex >= 0;
        nextBeaconPulseAt = observedAt;
    }

    private bool ObserveRequiredLine(
        int lineIndex,
        IReadOnlyList<bool> lineStates,
        Floor60GuardTimingSnapshot guardTiming,
        bool announceIfAlreadySafe)
    {
        if (lineIndex < 0 || lineIndex >= lineStates.Count)
        {
            observedGuardLineIndex = -1;
            observedGuardWaitTicks = 0;
            guardCycleCueIssued = false;
            return false;
        }

        var enabled = lineStates[lineIndex];
        var waitTicks = guardTiming.IsUsable
            ? guardTiming.GetRemainingTicks(lineIndex)
            : (ushort)0;
        if (observedGuardLineIndex != lineIndex)
        {
            observedGuardLineIndex = lineIndex;
            observedGuardLineEnabled = enabled;
            observedGuardWaitTicks = waitTicks;
            guardCycleCueIssued = announceIfAlreadySafe && !enabled;
            return guardCycleCueIssued;
        }

        if ((!observedGuardLineEnabled && enabled) ||
            (enabled &&
             waitTicks > observedGuardWaitTicks &&
             waitTicks > guardReactionLeadTicks))
        {
            guardCycleCueIssued = false;
        }

        var enteredReactionWindow =
            enabled &&
            guardReactionLeadTicks > 0 &&
            waitTicks is > 0 &&
            waitTicks <= guardReactionLeadTicks &&
            !guardCycleCueIssued;
        var becameSafe =
            observedGuardLineEnabled &&
            !enabled &&
            !guardCycleCueIssued;
        var alreadySafe =
            announceIfAlreadySafe &&
            !enabled &&
            !guardCycleCueIssued;

        observedGuardLineEnabled = enabled;
        observedGuardWaitTicks = waitTicks;
        if (enteredReactionWindow || becameSafe || alreadySafe)
        {
            guardCycleCueIssued = true;
            return true;
        }

        return false;
    }

    private bool HasPassedRequiredGuard(
        FieldPositionSnapshot position,
        int lineIndex,
        IReadOnlyList<bool> lineStates)
    {
        if (lineIndex < 0 ||
            lineIndex >= NativeGuardGaps.Length ||
            lineIndex >= lineStates.Count ||
            lineStates[lineIndex] ||
            activeTargetIndex <= 0 ||
            activeTargetIndex >= NativeHideSpots.Length)
        {
            return false;
        }

        var gap = NativeGuardGaps[lineIndex];
        var target = NativeHideSpots[activeTargetIndex];
        if (gap.EntityId != target.GuardLineEntityId)
        {
            return false;
        }

        var targetSide = gap.SignedSide(target.X, target.Y);
        var playerSide = gap.SignedSide(position.X, position.Y);
        if (targetSide == 0 ||
            playerSide == 0 ||
            Math.Sign(targetSide) != Math.Sign(playerSide))
        {
            return false;
        }

        var lineDx = gap.X2 - (long)gap.X1;
        var lineDy = gap.Y2 - (long)gap.Y1;
        var lineLengthSquared = lineDx * lineDx + lineDy * lineDy;
        var requiredCrossMagnitude =
            GuardPassDistanceUnits * Math.Sqrt(lineLengthSquared);
        return Math.Abs((double)playerSide) >= requiredCrossMagnitude;
    }

    private bool IsAtHideSpot(FieldPositionSnapshot position, int spotIndex)
    {
        if (spotIndex < 0 || spotIndex >= NativeHideSpots.Length)
        {
            return false;
        }

        var spot = NativeHideSpots[spotIndex];
        if (position.TriangleId != spot.TriangleId)
        {
            return false;
        }

        var dx = position.X - (long)spot.X;
        var dy = position.Y - (long)spot.Y;
        return dx * dx + dy * dy <= arrivalDistanceUnits * (long)arrivalDistanceUnits;
    }

    private int FindCurrentSpot(
        FieldPositionSnapshot position,
        int firstSpotIndex,
        int finalSpotIndex)
    {
        for (var index = firstSpotIndex; index <= finalSpotIndex; index++)
        {
            if (IsAtHideSpot(position, index))
            {
                return index;
            }
        }

        return -1;
    }

    private static int InferNextSpot(
        int playerX,
        int firstSpotIndex,
        int finalSpotIndex)
    {
        for (var index = firstSpotIndex; index <= finalSpotIndex; index++)
        {
            if (playerX < NativeHideSpots[index].X)
            {
                return index;
            }
        }

        return finalSpotIndex;
    }

    private static int ResolveTargetGuardLineIndex(int targetIndex) =>
        targetIndex switch
        {
            1 => 0,
            2 => 1,
            3 => 2,
            4 => 3,
            5 => 4,
            6 => 5,
            _ => -1
        };

    private static int ResolveSignalLineIndex(
        Floor60GuardStage stage,
        byte barretSignalingProgress,
        byte tifaSignalingProgress)
    {
        if (stage == Floor60GuardStage.FirstSignaling)
        {
            return barretSignalingProgress < SecondCrossingProgress
                ? barretSignalingProgress
                : tifaSignalingProgress;
        }

        if (stage != Floor60GuardStage.SecondSignaling)
        {
            return -1;
        }

        var progress = barretSignalingProgress < SignalingCompleteProgress
            ? barretSignalingProgress
            : tifaSignalingProgress;
        return 3 + Math.Clamp(progress - SecondCrossingProgress, 0, 2);
    }

    private bool IsFinalSpotForStage(int spotIndex, Floor60GuardStage stage) =>
        (stage == Floor60GuardStage.FirstCrossing && spotIndex == 3) ||
        (stage == Floor60GuardStage.SecondCrossing && spotIndex == 6);

    private static bool IsIndexInStage(int spotIndex, Floor60GuardStage stage) =>
        stage switch
        {
            Floor60GuardStage.FirstCrossing => spotIndex is >= 0 and <= 3,
            Floor60GuardStage.SecondCrossing => spotIndex is >= 4 and <= 6,
            _ => false
        };

    private bool IsAtFinalSecondSpot(FieldPositionSnapshot position) =>
        IsAtHideSpot(position, 6);

    private Floor60GuardStage ResolveStage(
        FieldPositionSnapshot position,
        byte barretSignalingProgress,
        byte tifaSignalingProgress,
        bool userControlLocked,
        bool firstCompletionLineEnabled,
        bool secondCompletionLineEnabled)
    {
        if (firstCompletionLineEnabled)
        {
            return Floor60GuardStage.FirstCrossing;
        }

        if (barretSignalingProgress < SecondCrossingProgress ||
            tifaSignalingProgress < SecondCrossingProgress)
        {
            return Floor60GuardStage.FirstSignaling;
        }

        if (secondCompletionLineEnabled &&
            barretSignalingProgress == SecondCrossingProgress &&
            tifaSignalingProgress == SecondCrossingProgress &&
            (!userControlLocked || !IsAtFinalSecondSpot(position)))
        {
            return Floor60GuardStage.SecondCrossing;
        }

        return Floor60GuardStage.SecondSignaling;
    }

    private void ClearBeaconState()
    {
        activeTargetIndex = -1;
        waitingAtSpotIndex = -1;
        targetReleased = false;
        observedGuardLineIndex = -1;
        observedGuardLineEnabled = false;
        observedGuardWaitTicks = 0;
        guardCycleCueIssued = false;
        guardPassAnnounced = false;
        nextBeaconPulseAt = DateTime.MinValue;
    }

    private readonly record struct Floor60GuardGap(
        int EntityId,
        int X1,
        int Y1,
        int X2,
        int Y2)
    {
        public long SignedSide(int x, int y) =>
            (X2 - (long)X1) * (y - (long)Y1) -
            (Y2 - (long)Y1) * (x - (long)X1);
    }

    private enum Floor60GuardStage
    {
        Inactive,
        FirstCrossing,
        FirstSignaling,
        SecondCrossing,
        SecondSignaling,
        Completed,
        Caught
    }
}

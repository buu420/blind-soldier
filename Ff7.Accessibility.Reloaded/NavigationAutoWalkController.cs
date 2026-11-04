using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

internal enum NavigationAutoWalkDomain
{
    None,
    Field,
    WorldMap
}

/// <summary>A count of the game's polls taken before a position is read, and whether the latest one acted.</summary>
internal readonly record struct AutoWalkPollMark(long Polls, bool LatestActed);

/// <summary>How many polls from <see cref="Mark"/> a decision's direction may still be acted on in.</summary>
internal readonly record struct AutoWalkPollBudget(AutoWalkPollMark Mark, int SafePolls);

/// <summary>
/// A field decision's bound as measured just before it is pressed: none (unbounded), a budget,
/// the reason to hand the walk to the player, or the reason not to press this time.
/// </summary>
internal readonly record struct AutoWalkFieldBudget(AutoWalkPollBudget? Budget, string? Handoff, string? Blocked);

/// <summary>
/// Where auto walk's hold of a direction ends near a Mutant Flytrap, counted in the game's own
/// input polls.
///
/// <para>A decision checks only the first eight units of a direction, and the hosts went on
/// pressing it until the next decision: up to 500 ms on Steam 2026, and on the legacy host for as
/// long as the monitor took to come back. At the 351 to 530 ms between decisions 0.10.8 logged in
/// 622 that walked the leader into both jaws in replay. So where a jaw bites, each decision arms
/// this with how many polls any path from the measured position - a wall slide included - stays
/// out of the touch (FieldWalkmeshRoutePlanner.FlytrapSafeFrames), and the game's own poll spends
/// it. The measure is taken just before the press (<see cref="ResolveFreshFieldBudget"/>): a
/// budget counted from before a slow scan's route work would be spent before it was pressed.</para>
///
/// <para>The legacy field loop FUN_0063C17F reads its keys once per iteration through
/// FUN_006499F7, its only caller, and moves the leader by that mask in FUN_006342C6. FFNx's
/// 60 fps divides each step's speed and its speedhack multiplies the frame limiter's rate, so a
/// poll is never more than one step there. Steam 2026 rebuilds the guest's keyboard state in its
/// GetDeviceState, reached from the same FUN_006499F7 through FUN_00649876, FUN_0041A21E and
/// FUN_0041F55E; whether its 3x speed runs three iterations or moves three steps on one poll is
/// not proven, so its polls are budgeted at three steps each. Disarmed, every poll acts, as it
/// always has.</para>
/// </summary>
internal sealed class AutoWalkPollLease
{
    /// <summary>FUN_006342C6's direction bits in the field mask: Up 0x1000, Right 0x2000, Down 0x4000, Left 0x8000.</summary>
    internal const uint FieldDirectionBits = 0xF000;

    /// <summary>The most one native step moves the leader: a run's 8 units (2048); FFNx's 60 fps halves it.</summary>
    internal const double MaximumUnitsPerPoll = 8d;

    private const long Unbounded = long.MaxValue;

    // Let go of while bounded, where the game can still read the keys for a poll or more.
    private const long ReleasePending = long.MinValue;

    private readonly bool releaseLagsThePoll;
    private Func<bool>? isPolled;

    // The polls so far, shifted left one, with whether the latest acted in the low bit: a mark
    // reads both at once.
    private long counter;
    private long lastActingPoll = Unbounded;

    // The direction bits a lagging release still holds out; written before the state that
    // announces them.
    private uint releasedBits;

    /// <param name="releaseLagsThePoll">
    /// Whether a released key can still be read on the game's next polls - the legacy host's
    /// SendInput. The direction auto walk let go of while bounded is then held out of the mask
    /// until a poll reads it gone; any other direction acts at once.
    /// </param>
    /// <param name="unitsPerPoll">The most one of the host's polls can move the leader.</param>
    internal AutoWalkPollLease(bool releaseLagsThePoll = false, double unitsPerPoll = MaximumUnitsPerPoll)
    {
        this.releaseLagsThePoll = releaseLagsThePoll;
        UnitsPerPoll = unitsPerPoll > 0d ? unitsPerPoll : throw new ArgumentOutOfRangeException(nameof(unitsPerPoll));
    }

    /// <summary>The most one of the host's polls can move the leader, which a budget is measured in.</summary>
    internal double UnitsPerPoll { get; }

    /// <summary>Whether the game's own polls reach this lease, so that arming it bounds anything.</summary>
    internal bool IsPolled
    {
        get
        {
            try { return isPolled?.Invoke() == true; }
            catch { return false; }
        }
    }

    internal bool IsBounded => Interlocked.Read(ref lastActingPoll) != Unbounded;

    /// <summary>The game's polls so far.</summary>
    internal long Polls => Interlocked.Read(ref counter) >> 1;

    /// <summary>The poll that spends this lease, from the host that owns it.</summary>
    internal void AttachPoller(Func<bool> polled) => isPolled = polled ?? throw new ArgumentNullException(nameof(polled));

    /// <summary>Read before the position a budget is measured at.</summary>
    internal AutoWalkPollMark Mark()
    {
        var value = Interlocked.Read(ref counter);
        return new AutoWalkPollMark(value >> 1, (value & 1) != 0);
    }

    /// <summary>
    /// A direction may act in <see cref="AutoWalkPollBudget.SafePolls"/> polls counted from the
    /// mark: the latest one's step, when it acted, may not be in the position yet.
    /// </summary>
    internal void Arm(AutoWalkPollBudget budget) =>
        Interlocked.Exchange(ref lastActingPoll,
            budget.Mark.Polls + Math.Max(0, budget.SafePolls) - (budget.Mark.LatestActed ? 1 : 0));

    /// <summary>Every poll acts again.</summary>
    internal void Disarm() => Interlocked.Exchange(ref lastActingPoll, Unbounded);

    /// <summary>
    /// Auto walk has let go of <paramref name="directionBits"/>. Unbounded, nothing changes.
    /// Bounded, every poll acts again at once - unless the keys can still be read, when those
    /// directions, and only those, stay out until a poll reads them gone.
    /// </summary>
    internal void Release(uint directionBits)
    {
        if (!IsBounded)
        {
            return;
        }

        if (!releaseLagsThePoll || (directionBits & FieldDirectionBits) == 0)
        {
            Disarm();
            return;
        }

        Volatile.Write(ref releasedBits, directionBits & FieldDirectionBits);
        Interlocked.Exchange(ref lastActingPoll, ReleasePending);
    }

    /// <summary>One of the game's polls: whether a direction may be acted on in it.</summary>
    internal bool Poll()
    {
        while (true)
        {
            var value = Interlocked.Read(ref counter);
            var poll = (value >> 1) + 1;
            var acts = poll <= Interlocked.Read(ref lastActingPoll);
            if (Interlocked.CompareExchange(ref counter, (poll << 1) | (acts ? 1L : 0L), value) == value)
            {
                return acts;
            }
        }
    }

    /// <summary>
    /// The legacy field loop's poll (FUN_006499F7's mask): while auto walk is bounded and spent,
    /// its directions taken out; after a lagging release, only the released direction, until a
    /// poll reads it gone.
    /// </summary>
    internal uint FilterFieldKeys(uint keys)
    {
        if (Poll())
        {
            return keys;
        }

        if (Interlocked.Read(ref lastActingPoll) != ReleasePending)
        {
            return keys & ~FieldDirectionBits;
        }

        var released = Volatile.Read(ref releasedBits);
        if ((keys & released) == 0)
        {
            // Up as far as the game can tell: unbounded again, unless a decision has armed a
            // new lease since.
            _ = Interlocked.CompareExchange(ref lastActingPoll, Unbounded, ReleasePending);
            return keys;
        }

        return keys & ~released;
    }

    /// <summary>The field mask bits a direction holds.</summary>
    internal static uint FieldBits(FieldNavigationInput input) => input switch
    {
        FieldNavigationInput.Up => 0x1000,
        FieldNavigationInput.UpRight => 0x3000,
        FieldNavigationInput.Right => 0x2000,
        FieldNavigationInput.DownRight => 0x6000,
        FieldNavigationInput.Down => 0x4000,
        FieldNavigationInput.DownLeft => 0xC000,
        FieldNavigationInput.Left => 0x8000,
        FieldNavigationInput.UpLeft => 0x9000,
        _ => 0
    };

    /// <summary>
    /// A field decision's bound, measured just before its direction is pressed - after the
    /// scan's route work, however long that took. Where no jaw bites at the decided position,
    /// none, with no further read: every other field drives as it always has. Where one does, a
    /// mark, then the position <paramref name="readFresh"/> returns now; a read that fails, or
    /// that finds the leader in another field or module, holds the press back this time.
    /// </summary>
    internal AutoWalkFieldBudget ResolveFreshFieldBudget(
        IFieldNavigationRoutePlanner? planner,
        FieldPositionSnapshot decidedAt,
        Func<FieldPositionSnapshot?> readFresh)
    {
        ArgumentNullException.ThrowIfNull(readFresh);
        if (planner is not IFieldNavigationFlytrapHoldBound bound ||
            bound.FlytrapSafeFrames(decidedAt, UnitsPerPoll) is null)
        {
            return default;
        }

        var mark = Mark();
        FieldPositionSnapshot? fresh;
        try
        {
            fresh = readFresh();
        }
        catch
        {
            fresh = null;
        }

        if (fresh is not { } now)
        {
            return new AutoWalkFieldBudget(null, null, "the position read before pressing failed");
        }

        if (now.FieldId != decidedAt.FieldId || now.CurrentModule != decidedAt.CurrentModule)
        {
            return new AutoWalkFieldBudget(null, null,
                $"the leader is now in field {now.FieldId}, module {now.CurrentModule}, not the decision's");
        }

        if (bound.FlytrapSafeFrames(now, UnitsPerPoll) is not { } safe)
        {
            return default;
        }

        var budget = new AutoWalkPollBudget(mark, safe);
        return new AutoWalkFieldBudget(budget, ResolveFieldHandoff(budget), null);
    }

    /// <summary>
    /// Why auto walk may not drive here and hands the walk to the player, or null: a budget of
    /// no polls is the leader at a jaw's touch, and a budget no game poll spends bounds nothing.
    /// Either way the player is told to let go of the direction keys first, as a key the game is
    /// still reading may be held out until it is let go.
    /// </summary>
    internal string? ResolveFieldHandoff(AutoWalkPollBudget? budget) => budget switch
    {
        null => null,
        { SafePolls: <= 0 } =>
            "Auto walk stopped beside the Mutant Flytrap: it cannot take another step here safely. Let go of the direction keys, then walk the rest yourself. Navigation is still on.",
        _ when !IsPolled =>
            "Auto walk stopped near the Mutant Flytrap: this game version cannot stop it in time. Let go of the direction keys, then walk this part yourself. Navigation is still on.",
        _ => null
    };
}

internal static class NavigationAutoWalkKeyRouter
{
    internal const int VirtualKeyP = 0x50;

    internal static bool ObserveToggle(Func<int, bool> observeRisingEdge)
    {
        ArgumentNullException.ThrowIfNull(observeRisingEdge);
        return observeRisingEdge(VirtualKeyP);
    }
}

/// <summary>
/// Owns only the player's configured directional keyboard keys during automatic
/// navigation. Route selection, safety, arrival, and speech remain with the
/// field/world adapters.
/// </summary>
internal sealed class NavigationAutoWalkController : IDisposable
{
    private const int MissingObservedDirectionSamplesBeforeReassert = 3;

    private readonly HighwayAutoSteeringController directionalInput;
    private readonly AutoWalkPollLease pollLease;
    private NavigationAutoWalkDomain domain;
    private FieldNavigationInput commandedInput;
    private bool commandedFlightAction;
    private int missingObservedDirectionSamples;
    private bool disposed;

    internal NavigationAutoWalkController(IHighwayKeyboardInputSink sink, AutoWalkPollLease? pollLease = null)
        : this(new HighwayAutoSteeringController(sink), pollLease ?? new AutoWalkPollLease())
    {
    }

    /// <summary>A delivery and a control table the caller chooses: deterministic tests.</summary>
    internal NavigationAutoWalkController(IHighwayKeyboardInputSink sink, IHighwayDirectionInputMappingResolver mappingResolver)
        : this(new HighwayAutoSteeringController(sink, mappingResolver), new AutoWalkPollLease())
    {
    }

    private NavigationAutoWalkController(HighwayAutoSteeringController directionalInput, AutoWalkPollLease pollLease)
    {
        this.directionalInput = directionalInput ?? throw new ArgumentNullException(nameof(directionalInput));
        this.pollLease = pollLease ?? throw new ArgumentNullException(nameof(pollLease));
    }

    internal static NavigationAutoWalkController CreateCurrentProcess() =>
        new(HighwayAutoSteeringController.CreateCurrentProcess(), new AutoWalkPollLease());

    /// <summary>
    /// The legacy host's: its keys go through SendInput, so the lease its field loop's hook
    /// spends outlives any one controller and knows a release can lag the game's read.
    /// </summary>
    internal static NavigationAutoWalkController CreateCurrentProcess(AutoWalkPollLease pollLease) =>
        new(HighwayAutoSteeringController.CreateCurrentProcess(), pollLease);

    internal static NavigationAutoWalkController CreateCurrentProcess(
        ILegacyAddressSpace addressSpace) =>
        new(HighwayAutoSteeringController.CreateCurrentProcess(addressSpace), new AutoWalkPollLease());

    /// <summary>
    /// The same controller over a host-chosen delivery for the keys it resolves. Used by
    /// the Steam 2026 runtime, whose keys are marked in the native keyboard state rather
    /// than pressed on the player's keyboard.
    /// </summary>
    /// <param name="pollLease">
    /// The lease the delivery's own polls spend, for the one controller that drives the field:
    /// any other controller on the same delivery - Steam 2026's world map shares it - keeps a
    /// lease of its own, so its pauses and resets never lift a field direction's bound.
    /// </param>
    internal static NavigationAutoWalkController CreateCurrentProcess(
        ILegacyAddressSpace addressSpace,
        IHighwayKeyboardInputSink sink,
        AutoWalkPollLease? pollLease = null) =>
        new(HighwayAutoSteeringController.CreateCurrentProcess(addressSpace, sink), pollLease ?? new AutoWalkPollLease());

    /// <summary>The polls that end this controller's field directions near a Mutant Flytrap.</summary>
    internal AutoWalkPollLease PollLease => pollLease;

    internal bool Enabled => !disposed && domain != NavigationAutoWalkDomain.None;

    internal string LastDiagnostic { get; private set; } = string.Empty;

    internal bool IsEnabledFor(NavigationAutoWalkDomain owner) =>
        Enabled && domain == owner;

    internal bool TryStart(NavigationAutoWalkDomain owner, bool routeActive)
    {
        if (disposed || owner == NavigationAutoWalkDomain.None || !routeActive)
        {
            return false;
        }

        if (domain != NavigationAutoWalkDomain.None && domain != owner)
        {
            _ = directionalInput.ReleaseAll();
            pollLease.Release(CommandedBits);
        }

        domain = owner;
        LastDiagnostic = $"auto walk active for {owner}";
        return true;
    }

    /// <param name="pollBudget">
    /// In a field where a Mutant Flytrap bites, how many of the game's polls the direction may be
    /// acted on in (<see cref="AutoWalkPollLease"/>); null, as everywhere else, leaves it unbounded.
    /// </param>
    internal HighwayAutoSteeringInputResult Drive(
        FieldNavigationInput input,
        bool canMove,
        bool routeActive,
        FieldNavigationInput? observedInput = null,
        bool holdFlightAction = false,
        bool worldSubmarine = false,
        bool holdSubmarineThrust = false,
        bool requestSubmarineDive = false,
        AutoWalkPollBudget? pollBudget = null)
    {
        if (disposed)
        {
            return directionalInput.Apply(HighwaySteeringDirection.None);
        }

        if (!routeActive)
        {
            var release = directionalInput.ReleaseAll();
            pollLease.Release(CommandedBits | AutoWalkPollLease.FieldBits(input));
            domain = NavigationAutoWalkDomain.None;
            ResetDirectionObservation();
            LastDiagnostic = release.Success
                ? "auto walk stopped because navigation is inactive"
                : release.Diagnostic;
            return release;
        }

        if (worldSubmarine)
        {
            if (!IsEnabledFor(NavigationAutoWalkDomain.WorldMap) || !canMove ||
                (!IsDirectional(input) && !holdSubmarineThrust && !requestSubmarineDive))
            {
                var release = directionalInput.ReleaseAll();
                pollLease.Release(CommandedBits);
                ResetDirectionObservation();
                LastDiagnostic = release.Success ? "world submarine movement suspended" : release.Diagnostic;
                return release;
            }
            pollLease.Disarm();
            var worldResult = requestSubmarineDive
                ? directionalInput.ApplyWorldSubmarineDive()
                : directionalInput.ApplyWorldSubmarine(Map(input), holdSubmarineThrust);
            if (!worldResult.Success)
            {
                _ = directionalInput.ReleaseAll();
                domain = NavigationAutoWalkDomain.None;
            }
            ResetDirectionObservation();
            LastDiagnostic = worldResult.Success ? $"world submarine {input}, thrust={holdSubmarineThrust}" : worldResult.Diagnostic;
            return worldResult;
        }

        if (Enabled && holdFlightAction && input == FieldNavigationInput.None)
        {
            // The Highwind's brake: the flight action alone, no direction.
            var brake = directionalInput.Apply(HighwaySteeringDirection.None, holdFlightAction: true);
            pollLease.Release(CommandedBits);
            if (!brake.Success)
            {
                _ = directionalInput.ReleaseAll();
                domain = NavigationAutoWalkDomain.None;
                ResetDirectionObservation();
                LastDiagnostic = brake.Diagnostic;
                return brake;
            }

            commandedInput = FieldNavigationInput.None;
            commandedFlightAction = true;
            missingObservedDirectionSamples = 0;
            LastDiagnostic = "auto walk holding the flight action alone to stop the Highwind";
            return brake;
        }

        if (!Enabled || !canMove || !IsDirectional(input))
        {
            var release = directionalInput.ReleaseAll();
            pollLease.Release(CommandedBits);
            ResetDirectionObservation();
            LastDiagnostic = release.Success
                ? !Enabled
                    ? "auto walk inactive"
                    : "auto walk direction suspended"
                : release.Diagnostic;
            return release;
        }

        var directionChanged = input != commandedInput || holdFlightAction != commandedFlightAction;
        // Bounded before the keys change, so no poll acts on them unbounded in between.
        if (pollBudget is { } budget)
        {
            pollLease.Arm(budget);
        }
        else
        {
            pollLease.Disarm();
        }

        var result = directionalInput.Apply(Map(input), holdFlightAction);
        if (!result.Success)
        {
            _ = directionalInput.ReleaseAll();
            pollLease.Release(CommandedBits | AutoWalkPollLease.FieldBits(input));
            domain = NavigationAutoWalkDomain.None;
            ResetDirectionObservation();
            LastDiagnostic = result.Diagnostic;
            return result;
        }

        if (directionChanged)
        {
            commandedInput = input;
            commandedFlightAction = holdFlightAction;
            missingObservedDirectionSamples = 0;
        }
        else if (observedInput == FieldNavigationInput.None)
        {
            missingObservedDirectionSamples++;
            if (missingObservedDirectionSamples >= MissingObservedDirectionSamplesBeforeReassert)
            {
                // Pressed again inside this decision, so the lease armed for it stands.
                var release = directionalInput.ReleaseAll();
                if (!release.Success)
                {
                    pollLease.Release(CommandedBits | AutoWalkPollLease.FieldBits(input));
                    domain = NavigationAutoWalkDomain.None;
                    ResetDirectionObservation();
                    LastDiagnostic = release.Diagnostic;
                    return release;
                }

                result = directionalInput.Apply(Map(input), holdFlightAction);
                if (!result.Success)
                {
                    _ = directionalInput.ReleaseAll();
                    pollLease.Release(CommandedBits | AutoWalkPollLease.FieldBits(input));
                    domain = NavigationAutoWalkDomain.None;
                    ResetDirectionObservation();
                    LastDiagnostic = result.Diagnostic;
                    return result;
                }

                missingObservedDirectionSamples = 0;
                LastDiagnostic = $"auto walk reasserted swallowed direction {input}";
                return result;
            }
        }
        else if (observedInput.HasValue)
        {
            missingObservedDirectionSamples = 0;
        }

        LastDiagnostic = $"auto walk direction {input}";
        return result;
    }

    internal void Suspend()
    {
        if (disposed)
        {
            return;
        }

        var result = directionalInput.ReleaseAll();
        pollLease.Release(CommandedBits);
        ResetDirectionObservation();
        LastDiagnostic = result.Success
            ? Enabled ? "auto walk suspended" : "auto walk inactive"
            : result.Diagnostic;
    }

    internal bool Stop()
    {
        if (disposed)
        {
            return false;
        }

        var wasEnabled = Enabled;
        var result = directionalInput.ReleaseAll();
        pollLease.Release(CommandedBits);
        domain = NavigationAutoWalkDomain.None;
        ResetDirectionObservation();
        LastDiagnostic = result.Success ? "auto walk stopped" : result.Diagnostic;
        return wasEnabled;
    }

    internal void Reset()
    {
        if (disposed)
        {
            return;
        }

        _ = directionalInput.ReleaseAll();
        pollLease.Release(CommandedBits);
        domain = NavigationAutoWalkDomain.None;
        ResetDirectionObservation();
        LastDiagnostic = "auto walk reset";
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        directionalInput.Dispose();
        pollLease.Release(CommandedBits);
        domain = NavigationAutoWalkDomain.None;
        ResetDirectionObservation();
        disposed = true;
    }

    private void ResetDirectionObservation()
    {
        commandedInput = FieldNavigationInput.None;
        commandedFlightAction = false;
        missingObservedDirectionSamples = 0;
    }

    /// <summary>The field bits of the direction auto walk last pressed: what a release lets go of.</summary>
    private uint CommandedBits => AutoWalkPollLease.FieldBits(commandedInput);

    private static bool IsDirectional(FieldNavigationInput input) =>
        input is >= FieldNavigationInput.Up and <= FieldNavigationInput.UpLeft;

    private static HighwaySteeringDirection Map(FieldNavigationInput input) => input switch
    {
        FieldNavigationInput.Up => HighwaySteeringDirection.Up,
        FieldNavigationInput.UpRight => HighwaySteeringDirection.UpRight,
        FieldNavigationInput.Right => HighwaySteeringDirection.Right,
        FieldNavigationInput.DownRight => HighwaySteeringDirection.DownRight,
        FieldNavigationInput.Down => HighwaySteeringDirection.Down,
        FieldNavigationInput.DownLeft => HighwaySteeringDirection.DownLeft,
        FieldNavigationInput.Left => HighwaySteeringDirection.Left,
        FieldNavigationInput.UpLeft => HighwaySteeringDirection.UpLeft,
        _ => HighwaySteeringDirection.None
    };
}

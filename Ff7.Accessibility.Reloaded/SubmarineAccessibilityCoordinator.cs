using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>One owner for submarine speech, target selection and ordinary steering input.</summary>
internal sealed class SubmarineAccessibilityCoordinator : IDisposable
{
    private const uint AddressHeldInput = 0x009A85D4;
    private const uint AddressKeyboardState = 0x009ADAE4;
    private const int PauseActionSlot = 11;
    private const int ManualSteeringMask = 0xF055;
    private static readonly TimeSpan InputDrainInterval = TimeSpan.FromMilliseconds(100);
    private readonly DateTime[] drainingUntil = new DateTime[16];
    private readonly ILegacyAddressSpace memory;
    private readonly SubmarineMissionStateReader reader;
    private readonly SubmarineMissionReadout readout = new();
    private readonly SubmarinePursuitTracker pursuit = new();
    private readonly HighwayAutoSteeringController? input;
    private readonly Action<DateTime>? renewInput;
    private readonly ControllerNavigationDispatcher dispatcher;
    private ControllerNavigationCapture? lastCapture;
    private string? commandSpeech;
    private bool maySpeak;
    private bool wasActive;
    private int ownedNativeMask;
    private int inputOwnershipActive;
    private int inputDeliveryRequested;
    private int disposed;

    internal SubmarineAccessibilityCoordinator(ILegacyAddressSpace memory,
        HighwayAutoSteeringController? input = null, Action<DateTime>? renewInput = null,
        Action<string>? log = null)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
        reader = new(memory);
        this.input = input;
        this.renewInput = renewInput;
        dispatcher = new(new DelegatedControllerNavigationTarget(
            () => pursuit.IsPursuing, () => pursuit.IsPursuing,
            pursuit.Apply, pursuit.StartPursuit, pursuit.StartPursuit,
            pursuit.StopPursuit, ReleaseInput), speech =>
            {
                if (!maySpeak) return false;
                commandSpeech = speech;
                return true;
            }, log);
    }

    internal string Diagnostic => $"{reader.LastDiagnostic}, torn captures: " +
        $"identity={reader.TornIdentityCaptures}, view={reader.TornViewCaptures}, " +
        $"last tear={reader.LastTear ?? "none"}";
    internal bool IsPursuing => pursuit.IsPursuing;
    internal bool HasOwnedInput => input?.HasOwnedKeys == true;

    // Evaluated outside the native sink's lock, on the actual game keyboard poll.
    // A result or field transition can precede the next worker observation.
    internal bool MayDeliverInputNow => Volatile.Read(ref inputOwnershipActive) == 0 ||
        (Volatile.Read(ref inputDeliveryRequested) != 0 &&
         Volatile.Read(ref disposed) == 0 &&
         memory.TryReadByte(SubmarineMissionStateReader.AddressCurrentModule, out var module) && module == 10 &&
         memory.TryReadInt32(SubmarineMissionStateReader.AddressActiveRun, out var run) && run != 0 &&
         memory.TryReadInt32(SubmarineMissionStateReader.AddressResult, out var result) && result == 0 &&
         memory.TryReadInt32(SubmarineMissionStateReader.AddressSessionFlags, out var flags) && (flags & 0xF) == 0 &&
         NativePauseIsReleased());

    private bool NativePauseIsReleased()
    {
        // The x64 overlay runs after the host fills this original keyboard buffer,
        // before the translated game consumes it. Block this poll's Pause press
        // as well as an already-published pause flag, so a turn cannot hit Quit/Yes.
        for (var bank = 0; bank < HighwayDirectionInputMappingResolver.MappingBankCount; bank++)
        {
            var address = HighwayDirectionInputMappingResolver.MappingTableAddress +
                (uint)(bank * HighwayDirectionInputMappingResolver.MappingBankStride + PauseActionSlot * sizeof(uint));
            if (!memory.TryReadUInt32(address, out var token)) return false;
            if (token == 0 || token >= 0xDE || (token & 0x7F) == 0) continue;
            if (!memory.TryReadByte(AddressKeyboardState + token, out var state) || (state & 0x80) != 0)
                return false;
        }
        return true;
    }

    internal SubmarineMissionCue Observe(int module, bool foreground, bool enabled,
        DateTime now, Func<int, bool> pressed, ControllerNavigationCapture? capture, out bool interrupt)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        try { return ObserveCore(module, foreground, enabled, now, pressed, capture, out interrupt); }
        catch
        {
            Reset();
            throw;
        }
    }

    private SubmarineMissionCue ObserveCore(int module, bool foreground, bool enabled,
        DateTime now, Func<int, bool> pressed, ControllerNavigationCapture? capture, out bool interrupt)
    {
        interrupt = false;
        commandSpeech = null;
        maySpeak = foreground && enabled;
        lastCapture = capture;
        if (module != SubmarineMissionStateReader.MinigameModule)
        {
            Reset();
            capture?.PublishUnavailable(ControllerNavigationDomain.Submarine, now);
            return default;
        }

        // Observe edges even while reads fail, speech is off, or focus is lost.
        // A key held across recovery must be released before it can start pursuit.
        var previous = pressed(0x4A);
        var next = pressed(0x4C);
        var toggle = pressed(0x50);
        var start = pressed(0x49);
        var repeat = pressed(0x4B);
        if (!maySpeak)
        {
            SuspendInput();
            readout.Reset();
            wasActive = false;
            capture?.PublishUnavailable(ControllerNavigationDomain.Submarine, now);
            return default;
        }
        if (!reader.TryRead(out var snapshot))
        {
            SuspendInput();
            capture?.PublishUnavailable(ControllerNavigationDomain.Submarine, now);
            if (capture is not null) dispatcher.Drain(capture, ControllerNavigationDomain.Submarine, now);
            if (toggle && pursuit.IsPursuing) commandSpeech = pursuit.StopPursuit();
            if (repeat) commandSpeech = "Submarine status is temporarily unavailable. Pursuit input is released.";
            interrupt = commandSpeech is not null;
            return commandSpeech is null ? default : new(commandSpeech, false);
        }
        if (!snapshot.IsActive)
        {
            Reset();
            capture?.PublishUnavailable(ControllerNavigationDomain.Submarine, now);
            return default;
        }

        var entering = !wasActive;
        wasActive = true;
        var assist = pursuit.Observe(snapshot, now);
        // The tracker speaks a selected lock. Ordinary manual play still gets
        // the instrument reader's lock announcements when it has no such cue.
        readout.PursuitOwnsTargets = pursuit.IsPursuing || assist.Speech is not null || assist.PlayLockCue;
        var instruments = readout.Observe(snapshot, now);
        var available = !snapshot.IsPaused && !snapshot.IsQuitPromptOpen && !snapshot.HasResult && snapshot.CanPlaceTargets;
        if (!available) SuspendInput();
        capture?.PublishContext(ControllerNavigationDomain.Submarine, true, true,
            gameIsBusy: !available, now, identity: 10);
        if (capture is not null) dispatcher.Drain(capture, ControllerNavigationDomain.Submarine, now);

        if (available)
        {
            if (previous) commandSpeech = pursuit.Apply(FieldNavigationAction.PreviousTarget);
            if (next) commandSpeech = pursuit.Apply(FieldNavigationAction.NextTarget);
            if (toggle) commandSpeech = pursuit.TogglePursuit();
            if (start) commandSpeech = pursuit.StartPursuit();
        }
        else if (toggle && pursuit.IsPursuing)
        {
            commandSpeech = pursuit.StopPursuit();
        }
        if (capture?.IsOpen == true) ReleaseInput();
        else if (available) Drive(now);
        if (repeat)
        {
            commandSpeech = pursuit.DescribeStatus() + " " + readout.Describe(snapshot);
            interrupt = true;
        }
        if (commandSpeech is not null)
        {
            interrupt = true;
            var speech = commandSpeech;
            if (!repeat && assist.Speech is { } transition && !speech.Contains(transition, StringComparison.Ordinal))
                speech = transition + " " + speech;
            if (!repeat && !entering && instruments.Speech is { } changedInstruments)
                speech += " " + changedInstruments;
            return new(speech, instruments.PlayLockCue || assist.PlayLockCue);
        }
        if (entering && instruments.Speech is { } introduction)
        {
            var speech = assist.Speech is { } initialGuidance && !introduction.Contains(initialGuidance, StringComparison.Ordinal)
                ? introduction + " " + initialGuidance : introduction;
            return new(speech + " " + DescribeAssistanceControls(), instruments.PlayLockCue || assist.PlayLockCue);
        }
        if (assist.Speech is { } guidance && instruments.Speech is { } instrumentsSpeech)
            return new(guidance + " " + instrumentsSpeech, assist.PlayLockCue || instruments.PlayLockCue);
        return assist.Speech is not null ? assist : instruments;
    }

    private void Drive(DateTime now)
    {
        var plan = pursuit.Plan;
        if (!plan.CanDrive || input is null)
        {
            ReleaseInput();
            if (plan.CanDrive && input is null)
            {
                pursuit.StopPursuit();
                commandSpeech = "Pursuit is unavailable: the game input connection is not installed.";
            }
            return;
        }
        if (!memory.TryReadInt32(AddressHeldInput, out var held))
        {
            SuspendInput();
            commandSpeech = "Pursuit paused: the game controls cannot be read.";
            return;
        }
        var drainingMask = 0;
        for (var bit = 0; bit < drainingUntil.Length; bit++)
            if (drainingUntil[bit] > now && drainingUntil[bit] - now <= InputDrainInterval)
                drainingMask |= 1 << bit;
        if ((held & ManualSteeringMask & ~(ownedNativeMask | drainingMask)) != 0)
        {
            pursuit.StopPursuit();
            ReleaseInput();
            commandSpeech = "Pursuit stopped: manual steering or throttle.";
            return;
        }
        Volatile.Write(ref inputOwnershipActive, 1);
        Volatile.Write(ref inputDeliveryRequested, 1);
        var result = input.ApplySubmarine(plan.Direction, plan.Accelerate, plan.Brake);
        if (!result.Success)
        {
            if (!MayDeliverInputNow)
            {
                // The captured view may still be live (Start unpauses on its
                // pressed edge). Withhold input without resetting lock speech.
                ReleaseInput();
                return;
            }
            pursuit.StopPursuit();
            ReleaseInput();
            commandSpeech = "Pursuit stopped: " + result.Diagnostic + ".";
            return;
        }
        var nextMask = NativeMask(plan);
        var releasedMask = ownedNativeMask & ~nextMask;
        for (var bit = 0; bit < drainingUntil.Length; bit++)
            if ((releasedMask & (1 << bit)) != 0) drainingUntil[bit] = now + InputDrainInterval;
        ownedNativeMask = nextMask;
        renewInput?.Invoke(now);
    }

    private static int NativeMask(SubmarinePursuitPlan plan)
    {
        var mask = (plan.Accelerate ? 0x10 : 0) | (plan.Brake ? 0x40 : 0);
        if (plan.Direction is HighwaySteeringDirection.Up or HighwaySteeringDirection.UpLeft or HighwaySteeringDirection.UpRight) mask |= 0x1000;
        if (plan.Direction is HighwaySteeringDirection.Down or HighwaySteeringDirection.DownLeft or HighwaySteeringDirection.DownRight) mask |= 0x4000;
        if (plan.Direction is HighwaySteeringDirection.Left or HighwaySteeringDirection.UpLeft or HighwaySteeringDirection.DownLeft) mask |= 0x8000;
        if (plan.Direction is HighwaySteeringDirection.Right or HighwaySteeringDirection.UpRight or HighwaySteeringDirection.DownRight) mask |= 0x2000;
        return mask;
    }

    internal void SuspendInput()
    {
        pursuit.Suspend();
        ReleaseInput();
    }

    private void ReleaseInput()
    {
        Volatile.Write(ref inputDeliveryRequested, 0);
        var released = input?.ReleaseAll();
        if (released is null || released.Value.Success)
        {
            ownedNativeMask = 0;
            Array.Clear(drainingUntil);
            Volatile.Write(ref inputOwnershipActive, 0);
        }
    }

    internal void Reset()
    {
        ReleaseInput();
        pursuit.Reset();
        readout.Reset();
        wasActive = false;
        lastCapture?.RequestClose(ControllerNavigationDomain.Submarine);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Reset();
        input?.Dispose();
    }

    internal static string DescribeAssistanceControls() =>
        "J and L select a submarine. P pursues automatically or stops; I starts pursuit. " +
        "K repeats status. On a controller, R3 opens targets, Up and Down select, " +
        "A or X pursues, and B or R3 from the list stops. PageDown opens the overview to spot contacts; " +
        "return to normal view to lock. Fire manually with Switch when your selected target is locked.";
}

using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Dialogue;

/// <summary>
/// Reads ordinary field-dialogue pages from the Steam 2026 translated x86
/// guest address space. This research component creates no hooks, publishes no
/// events, and owns no speech or runtime-capability lifecycle.
/// </summary>
public sealed class Steam2026FieldDialogueObservationReader
{
    private readonly object sync = new();
    private readonly ILegacyAddressSpace addressSpace;
    private readonly FieldMessageReader messageReader;
    private readonly FieldAudibleCueStateReader cueReader;
    private readonly FieldOpcodeParameterReader opcodeReader;

    private DialoguePageIdentity? currentPage;
    private readonly Dictionary<int, DialogueWindowIdentity> observedWindows = [];
    private int selectedWindowId = -1;
    private ushort observedFieldId = ushort.MaxValue;
    private int pageRevision;
    private Steam2026AskCursorIngressSnapshot? latestAskCursor;
    private long lastAskCursorSequence;
    private AskWindowSnapshot? lastExactAskWindow;
    private AskWindowSnapshot? retiredAskWindow;
    private readonly Dictionary<MessageIngressIdentity, ActiveMessageIngress> activeMessageIngresses = [];
    private FieldOpcodeMessageObservation? activeMessageIngress;
    private long lastMessageIngressSequence;
    private long lastDialogueIngressSequence;

    internal string LastDiagnostic { get; private set; } = "not observed";

    public Steam2026FieldDialogueObservationReader(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        INativeMemoryReader memory)
        : this(ValidatedTranslatedX86AddressSpaceFactory.Create(
            fingerprint,
            moduleBase,
            memory))
    {
    }

    internal Steam2026FieldDialogueObservationReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        messageReader = new FieldMessageReader(addressSpace);
        cueReader = new FieldAudibleCueStateReader(addressSpace);
        opcodeReader = new FieldOpcodeParameterReader(addressSpace);
    }

    internal void ObserveAskCursorCapture(Steam2026AskCursorIngressSnapshot snapshot)
    {
        if (snapshot.Sequence <= 0
            || snapshot.TimestampUtc.Kind != DateTimeKind.Utc
            || snapshot.Capture.FieldId < 0
            || snapshot.Capture.WindowId is < 0 or >= FieldMessageReader.WindowCount
            || snapshot.Capture.DialogId < 0
            || snapshot.Capture.FirstQuestionLine < 0
            || snapshot.Capture.LastQuestionLine < snapshot.Capture.FirstQuestionLine
            || snapshot.Capture.LastQuestionLine
                >= FieldOpcodeParameterReader.MaximumAskVisibleLineCount
            || snapshot.Capture.CurrentQuestionLine < snapshot.Capture.FirstQuestionLine
            || snapshot.Capture.CurrentQuestionLine > snapshot.Capture.LastQuestionLine)
        {
            return;
        }

        lock (sync)
        {
            if (snapshot.Sequence <= lastAskCursorSequence)
            {
                return;
            }

            lastAskCursorSequence = snapshot.Sequence;
            if (snapshot.Sequence <= lastDialogueIngressSequence)
            {
                return;
            }

            lastDialogueIngressSequence = snapshot.Sequence;
            activeMessageIngress = null;
            latestAskCursor = snapshot;
        }
    }

    internal void ObserveMessageLifecycle(
        Steam2026FieldMessageIngressSnapshot snapshot)
    {
        var observation = snapshot.Observation;
        if (snapshot.Sequence <= 0
            || snapshot.TimestampUtc.Kind != DateTimeKind.Utc
            || observation.Kind != FieldOpcodeKind.Message
            || observation.FieldId < 0
            || observation.WindowId is < 0 or >= FieldMessageReader.WindowCount
            || observation.DialogId < 0)
        {
            return;
        }

        lock (sync)
        {
            if (snapshot.Sequence <= lastMessageIngressSequence)
            {
                return;
            }

            lastMessageIngressSequence = snapshot.Sequence;
            var identity = MessageIngressIdentity.From(observation);
            if (snapshot.Result != 0 && activeMessageIngresses.ContainsKey(identity))
            {
                // A blocked MESSAGE opcode is called again on every field
                // script pass. Re-entry for an already-active identity is a
                // lifecycle poll, not a newly opened page, and must not steal
                // focus from a newer overlapping window.
                return;
            }

            if (snapshot.Sequence <= lastDialogueIngressSequence)
            {
                return;
            }

            lastDialogueIngressSequence = snapshot.Sequence;
            if (snapshot.Result != 0)
            {
                activeMessageIngresses[identity] = new ActiveMessageIngress(
                    observation,
                    snapshot.Sequence);
                activeMessageIngress = observation;
                selectedWindowId = observation.WindowId;
                retiredAskWindow = lastExactAskWindow;
                lastExactAskWindow = null;
                latestAskCursor = null;
            }
            else
            {
                activeMessageIngresses.Remove(identity);
                if (activeMessageIngress is { } active
                    && SameMessageIdentity(active, observation))
                {
                    activeMessageIngress = latestAskCursor is null
                        ? SelectLatestActiveMessage()
                        : null;
                    if (activeMessageIngress is { } restored)
                    {
                        selectedWindowId = restored.WindowId;
                    }
                }
            }
        }
    }

    internal void ResetAskCursorIngress()
    {
        lock (sync)
        {
            retiredAskWindow = lastExactAskWindow;
            lastExactAskWindow = null;
            latestAskCursor = null;
            lastAskCursorSequence = 0;
        }
    }

    internal void ResetMessageIngress()
    {
        lock (sync)
        {
            activeMessageIngresses.Clear();
            activeMessageIngress = null;
            lastMessageIngressSequence = 0;
        }
    }

    public bool TryRead(out DialoguePageObservation observation)
    {
        observation = null!;
        if (!TryReadUpdate(out var update)
            || update.Kind != RuntimeDomainUpdateKind.Present
            || update.Value is null)
        {
            return false;
        }

        observation = update.Value;
        return true;
    }

    /// <summary>
    /// Distinguishes a coherently observed closed dialogue domain from a
    /// transiently unavailable or ambiguous observation.  Callers must retain
    /// their prior state when this method returns false.
    /// </summary>
    public bool TryReadUpdate(out RuntimeDomainUpdate<DialoguePageObservation> update)
    {
        lock (sync)
        {
            update = RuntimeDomainUpdate<DialoguePageObservation>.Unchanged;
            if (!TryReadCompleteFrame(out var candidate) ||
                !TryReadCompleteFrame(out var confirmation) ||
                !candidate.Equals(confirmation))
            {
                LastDiagnostic = "coherent field-dialogue frame unavailable";
                return false;
            }

            var activeWindowCount = candidate.Ownership.CountActiveWindowSlots();
            if (candidate.Ownership.ActiveMessageCount == 0 &&
                activeWindowCount == 0 &&
                candidate.Windows.Length == 0)
            {
                ResetPageRevision();
                ClearAskLifecycle();
                update = RuntimeDomainUpdate<DialoguePageObservation>.Closed;
                LastDiagnostic = "closed: no active message or assigned window";
                return true;
            }

            if (candidate.Ownership.ActiveMessageCount == 0 ||
                candidate.Ownership.MessageDataPointer == 0)
            {
                LastDiagnostic = DescribeOwnership(
                    candidate,
                    "unavailable: active-message count or message table is absent");
                return false;
            }

            if (TryCreateExactMessagePage(
                    candidate,
                    out var messageWindowId,
                    out var messageText,
                    out var messagePage,
                    out var exactMessageDiagnostic))
            {
                var published = TryPublishPage(
                    messageWindowId,
                    messageText,
                    Array.Empty<DialogueChoiceObservation>(),
                    messagePage,
                    out update);
                LastDiagnostic = DescribeOwnership(
                    candidate,
                    $"{exactMessageDiagnostic}; publish={(published ? "present" : "rejected")}");
                return published;
            }

            if (activeWindowCount == 0)
            {
                LastDiagnostic = DescribeOwnership(
                    candidate,
                    $"{exactMessageDiagnostic}; unavailable: no assigned window");
                return false;
            }

            if (candidate.Windows.Length == 0 ||
                !TrySelectVisibleWindow(candidate, out var window))
            {
                LastDiagnostic = DescribeOwnership(
                    candidate,
                    $"{exactMessageDiagnostic}; unavailable: no unambiguous visible window");
                return false;
            }

            var askStatus = TryCreateExactAskPage(
                candidate,
                window,
                out var askPrompt,
                out var askChoices,
                out var askPage,
                out var successorWindowId);
            if (askStatus == ExactAskPageReadStatus.Unavailable)
            {
                LastDiagnostic = DescribeOwnership(
                    candidate,
                    $"{exactMessageDiagnostic}; unavailable: exact ASK state is incoherent");
                return false;
            }

            if (askStatus == ExactAskPageReadStatus.Ended &&
                successorWindowId >= 0)
            {
                selectedWindowId = successorWindowId;
                var successorWindowIndex = Array.FindIndex(
                    candidate.Windows,
                    candidateWindow => candidateWindow.WindowId == successorWindowId);
                if (successorWindowIndex < 0)
                {
                    LastDiagnostic = DescribeOwnership(
                        candidate,
                        $"{exactMessageDiagnostic}; unavailable: ASK successor window is not readable");
                    return false;
                }

                window = candidate.Windows[successorWindowIndex];
            }

            if (string.IsNullOrWhiteSpace(window.Text))
            {
                LastDiagnostic = DescribeOwnership(
                    candidate,
                    $"{exactMessageDiagnostic}; unavailable: selected window text is blank");
                return false;
            }

            string visibleText;
            IReadOnlyList<DialogueChoiceObservation> choices;
            DialoguePageIdentity page;
            if (askStatus == ExactAskPageReadStatus.Exact)
            {
                visibleText = askPrompt;
                choices = askChoices;
                page = askPage;
            }
            else
            {
                visibleText = window.Text;
                choices = Array.Empty<DialogueChoiceObservation>();
                page = new DialoguePageIdentity(
                    candidate.Ownership.FieldId,
                    window.WindowId,
                    window.NativeState,
                    window.GuestPointer,
                    window.Text,
                    NativeMessageDialogId: -1,
                    AskDialogId: -1,
                    AskFirstQuestionLine: -1,
                    AskLastQuestionLine: -1);
                if (ShouldSuppressRetiredAskWindow(candidate.Ownership.FieldId, window))
                {
                    LastDiagnostic = DescribeOwnership(
                        candidate,
                        $"{exactMessageDiagnostic}; retired ASK mirror suppressed");
                    return false;
                }
            }

            var genericPublished = TryPublishPage(
                window.WindowId,
                visibleText,
                choices,
                page,
                out update);
            LastDiagnostic = DescribeOwnership(
                candidate,
                $"{exactMessageDiagnostic}; generic {(askStatus == ExactAskPageReadStatus.Exact ? "ASK" : "window")} " +
                $"window={window.WindowId}, publish={(genericPublished ? "present" : "rejected")}, " +
                $"text={Preview(visibleText)}");
            return genericPublished;
        }
    }

    private bool TryCreateExactMessagePage(
        DialogueCompleteFrame candidate,
        out int windowId,
        out string visibleText,
        out DialoguePageIdentity page,
        out string diagnostic)
    {
        windowId = -1;
        visibleText = string.Empty;
        page = default;
        diagnostic = "exact MESSAGE unavailable";

        FieldOpcodeMessageObservation message;
        var hasAuthoritativeIngress = false;
        if (activeMessageIngress is { } ingress
            && ingress.Kind == FieldOpcodeKind.Message
            && ingress.FieldId == candidate.Ownership.FieldId)
        {
            // Callback ingress is captured at the translated MESSAGE entry.
            // The interpreter globals are shared by every field entity and can
            // already point at a different script by the time the worker polls.
            message = ingress;
            hasAuthoritativeIngress = true;
        }
        else if (opcodeReader.TryReadMessage(out var currentMessage)
                 && currentMessage.Kind == FieldOpcodeKind.Message
                 && currentMessage.FieldId == candidate.Ownership.FieldId)
        {
            message = currentMessage;
        }
        else
        {
            diagnostic = "exact MESSAGE unavailable: no same-field callback ingress or current opcode";
            return false;
        }

        if (message.WindowId is < 0 or >= FieldMessageReader.WindowCount)
        {
            diagnostic =
                $"exact MESSAGE rejected: invalid window={message.WindowId}, dialog={message.DialogId}";
            return false;
        }

        var targetWindowIsAssigned =
            candidate.Ownership.States[message.WindowId] != FieldMessageReader.FreeWindowState;
        if (!targetWindowIsAssigned && !hasAuthoritativeIngress)
        {
            diagnostic =
                $"exact current opcode rejected: unassigned window={message.WindowId}, dialog={message.DialogId}";
            return false;
        }

        var visibleWindowIndex = Array.FindIndex(
            candidate.Windows,
            window => window.WindowId == message.WindowId);
        byte nativeState;
        uint guestPointer;
        var visibleMirrorIsRetiredAsk = visibleWindowIndex >= 0
            && retiredAskWindow is { } retired
            && retired.FieldId == candidate.Ownership.FieldId
            && retired.WindowId == message.WindowId
            && string.Equals(
                retired.RawText,
                candidate.Windows[visibleWindowIndex].Text,
                StringComparison.Ordinal);
        if (visibleWindowIndex >= 0 && !visibleMirrorIsRetiredAsk)
        {
            var window = candidate.Windows[visibleWindowIndex];
            visibleText = window.Text;
            nativeState = window.NativeState;
            guestPointer = window.GuestPointer;
        }
        else
        {
            // MESSAGE can become active before its reusable native window slot
            // is assigned, and an ASK-to-MESSAGE transition can temporarily
            // leave the selected ASK text in that slot. The checked callback
            // identity plus checked live message table is the exact sighted
            // successor during that interval.
            if (!messageReader.TryReadMessageById(message.DialogId, out var exactMessage)
                || string.IsNullOrWhiteSpace(exactMessage.Text))
            {
                diagnostic =
                    $"exact MESSAGE table unreadable: source={(hasAuthoritativeIngress ? "callback" : "current-opcode")}, " +
                    $"window={message.WindowId}, dialog={message.DialogId}, assigned={targetWindowIsAssigned}, " +
                    $"retiredAskMirror={visibleMirrorIsRetiredAsk}";
                return false;
            }

            visibleText = exactMessage.Text;
            nativeState = candidate.Ownership.States[message.WindowId];
            guestPointer = candidate.Ownership.Pointers[message.WindowId];
        }

        selectedWindowId = message.WindowId;
        ReplaceObservedWindows(candidate.Windows);
        retiredAskWindow = lastExactAskWindow;
        lastExactAskWindow = null;
        latestAskCursor = null;
        windowId = message.WindowId;
        page = new DialoguePageIdentity(
            candidate.Ownership.FieldId,
            message.WindowId,
            nativeState,
            guestPointer,
            visibleText,
            NativeMessageDialogId: message.DialogId,
            AskDialogId: -1,
            AskFirstQuestionLine: -1,
            AskLastQuestionLine: -1);
        diagnostic =
            $"exact MESSAGE source={(hasAuthoritativeIngress ? "callback" : "current-opcode")}, " +
            $"window={message.WindowId}, dialog={message.DialogId}, assigned={targetWindowIsAssigned}, " +
            $"textSource={(visibleWindowIndex >= 0 && !visibleMirrorIsRetiredAsk ? "visible-window" : "message-table")}, " +
            $"text={Preview(visibleText)}";
        return true;
    }

    private static string DescribeOwnership(
        DialogueCompleteFrame frame,
        string detail) =>
        $"field={frame.Ownership.FieldId}, active={frame.Ownership.ActiveMessageCount}, " +
        $"states=[{string.Join(',', frame.Ownership.States.Select(state => state.ToString("X2")))}], " +
        $"windows=[{string.Join(',', frame.Windows.Select(window => $"{window.WindowId}:{Preview(window.Text)}"))}]; " +
        detail;

    private static string Preview(string text)
    {
        var normalized = text.Replace('\u001f', ' ').Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Length <= 96 ? normalized : normalized[..96] + "...";
    }

    private bool TryPublishPage(
        int windowId,
        string visibleText,
        IReadOnlyList<DialogueChoiceObservation> choices,
        DialoguePageIdentity page,
        out RuntimeDomainUpdate<DialoguePageObservation> update)
    {
        update = RuntimeDomainUpdate<DialoguePageObservation>.Unchanged;
        if (windowId is < 0 or >= FieldMessageReader.WindowCount
            || string.IsNullOrWhiteSpace(visibleText) && choices.Count == 0)
        {
            return false;
        }

        if (currentPage is null || currentPage.Value != page)
        {
            pageRevision = pageRevision == int.MaxValue ? 1 : pageRevision + 1;
            currentPage = page;
        }

        update = RuntimeDomainUpdate<DialoguePageObservation>.Present(
            new DialoguePageObservation(
                isOpen: true,
                windowId: windowId,
                pageRevision: pageRevision,
                speaker: string.Empty,
                visibleText: visibleText,
                choices: choices));
        return true;
    }

    private bool TrySelectVisibleWindow(
        DialogueCompleteFrame candidate,
        out FieldVisibleWindowSnapshot selected)
    {
        selected = default;
        if (observedFieldId != candidate.Ownership.FieldId)
        {
            observedFieldId = candidate.Ownership.FieldId;
            observedWindows.Clear();
            selectedWindowId = -1;
            if (activeMessageIngress is { } active
                && active.FieldId != candidate.Ownership.FieldId)
            {
                activeMessageIngresses.Clear();
                activeMessageIngress = null;
            }
        }

        var windows = candidate.Windows
            .OrderBy(window => window.WindowId)
            .ToArray();
        if (windows.Length == 1)
        {
            selected = windows[0];
            selectedWindowId = selected.WindowId;
            ReplaceObservedWindows(windows);
            return true;
        }

        if (activeMessageIngress is { } message
            && message.FieldId == candidate.Ownership.FieldId)
        {
            var messageWindowIndex = Array.FindIndex(
                windows,
                window => window.WindowId == message.WindowId);
            selectedWindowId = message.WindowId;
            ReplaceObservedWindows(windows);
            if (messageWindowIndex < 0)
            {
                return false;
            }

            selected = windows[messageWindowIndex];
            return true;
        }

        if (latestAskCursor is { } askSnapshot &&
            askSnapshot.Capture.FieldId == candidate.Ownership.FieldId)
        {
            var askWindowIndex = Array.FindIndex(
                windows,
                window => window.WindowId == askSnapshot.Capture.WindowId);
            if (askWindowIndex >= 0)
            {
                selected = windows[askWindowIndex];
                selectedWindowId = selected.WindowId;
                ReplaceObservedWindows(windows);
                return true;
            }
        }

        var newlyVisible = windows
            .Where(window =>
                !observedWindows.TryGetValue(window.WindowId, out var previous) ||
                previous.GuestPointer != window.GuestPointer)
            .ToArray();
        if (newlyVisible.Length == 1)
        {
            selected = newlyVisible[0];
            selectedWindowId = selected.WindowId;
            ReplaceObservedWindows(windows);
            return true;
        }

        if (newlyVisible.Length > 1)
        {
            ReplaceObservedWindows(windows);
            selectedWindowId = -1;
            return false;
        }

        var continuingSelectionIndex = Array.FindIndex(
            windows,
            window => window.WindowId == selectedWindowId);
        if (continuingSelectionIndex >= 0)
        {
            selected = windows[continuingSelectionIndex];
            ReplaceObservedWindows(windows);
            return true;
        }

        var changed = windows
            .Where(window =>
                observedWindows.TryGetValue(window.WindowId, out var previous) &&
                previous != DialogueWindowIdentity.From(window))
            .ToArray();
        ReplaceObservedWindows(windows);
        if (changed.Length != 1)
        {
            selectedWindowId = -1;
            return false;
        }

        selected = changed[0];
        selectedWindowId = selected.WindowId;
        return true;
    }

    private void ReplaceObservedWindows(IReadOnlyList<FieldVisibleWindowSnapshot> windows)
    {
        observedWindows.Clear();
        foreach (var window in windows)
        {
            observedWindows[window.WindowId] = DialogueWindowIdentity.From(window);
        }
    }

    private ExactAskPageReadStatus TryCreateExactAskPage(
        DialogueCompleteFrame candidate,
        FieldVisibleWindowSnapshot window,
        out string prompt,
        out IReadOnlyList<DialogueChoiceObservation> choices,
        out DialoguePageIdentity page,
        out int successorWindowId)
    {
        prompt = string.Empty;
        choices = Array.Empty<DialogueChoiceObservation>();
        page = default;
        successorWindowId = -1;
        if (latestAskCursor is not { } snapshot)
        {
            return ExactAskPageReadStatus.NotApplicable;
        }

        var capture = snapshot.Capture;
        if (capture.FieldId != candidate.Ownership.FieldId
            || capture.WindowId != window.WindowId)
        {
            return ExactAskPageReadStatus.NotApplicable;
        }

        if (!TryReadStableWindowLifecyclePhase(window.WindowId, out var lifecyclePhase))
        {
            return ExactAskPageReadStatus.Unavailable;
        }

        if (!opcodeReader.TryReadAsk(out var ownedAsk))
        {
            if (opcodeReader.TryReadMessage(out var ownedMessage)
                && ownedMessage.Kind == FieldOpcodeKind.Message
                && ownedMessage.FieldId == capture.FieldId)
            {
                successorWindowId = ownedMessage.WindowId;
                RetireAsk(candidate.Ownership.FieldId, window);
                return ExactAskPageReadStatus.Ended;
            }

            if (lifecyclePhase != Steam2026FieldAudibleCueStateReader.CompletedTextPhase)
            {
                RetireAsk(candidate.Ownership.FieldId, window);
                return ExactAskPageReadStatus.Ended;
            }

            return ExactAskPageReadStatus.Unavailable;
        }

        if (lifecyclePhase != Steam2026FieldAudibleCueStateReader.CompletedTextPhase)
        {
            RetireAsk(candidate.Ownership.FieldId, window);
            return ExactAskPageReadStatus.Ended;
        }

        if (ownedAsk.Kind != FieldOpcodeKind.Ask
            || ownedAsk.FieldId != capture.FieldId
            || ownedAsk.WindowId != capture.WindowId
            || ownedAsk.DialogId != capture.DialogId
            || ownedAsk.FirstQuestionLine != capture.FirstQuestionLine
            || ownedAsk.LastQuestionLine != capture.LastQuestionLine)
        {
            return ExactAskPageReadStatus.Unavailable;
        }

        if (!messageReader.TryReadMessagePagesById(capture.DialogId, out var pages)
            || !FieldAskTextFormatter.TryResolveChoicePage(
                pages,
                capture.FirstQuestionLine,
                capture.LastQuestionLine,
                out var lines))
        {
            return ExactAskPageReadStatus.Unavailable;
        }

        if (pages.Count > 1
            && !FieldAskTextFormatter.IsChoicePageVisible(lines, window.Text))
        {
            // ASK may display ordinary pages before the page containing the
            // selector. Preserve that sighted text through the checked generic
            // window path, but do not expose the later choices early.
            return ExactAskPageReadStatus.NotApplicable;
        }

        prompt = FieldAskTextFormatter.FormatPrompt(
            lines,
            capture.FirstQuestionLine,
            capture.LastQuestionLine);
        var exactChoices = new List<DialogueChoiceObservation>(
            capture.LastQuestionLine - capture.FirstQuestionLine + 1);
        for (var line = capture.FirstQuestionLine;
             line <= capture.LastQuestionLine;
             line++)
        {
            var text = FieldAskTextFormatter.GetChoice(
                lines,
                capture.FirstQuestionLine,
                capture.LastQuestionLine,
                line);
            if (text.Length == 0)
            {
                prompt = string.Empty;
                return ExactAskPageReadStatus.Unavailable;
            }

            exactChoices.Add(new DialogueChoiceObservation(
                line - capture.FirstQuestionLine,
                text,
                Enabled: true,
                Selected: line == capture.CurrentQuestionLine));
        }

        if (exactChoices.Count == 0
            || exactChoices.Count(choice => choice.Selected) != 1)
        {
            prompt = string.Empty;
            return ExactAskPageReadStatus.Unavailable;
        }

        choices = exactChoices;
        var identityText = string.Join(
            '\u001f',
            new[] { prompt }.Concat(exactChoices.Select(choice => choice.Text)));
        lastExactAskWindow = new AskWindowSnapshot(
            candidate.Ownership.FieldId,
            window.WindowId,
            window.Text);
        retiredAskWindow = null;
        page = new DialoguePageIdentity(
            candidate.Ownership.FieldId,
            window.WindowId,
            NativeState: 0,
            GuestPointer: 0,
            Text: identityText,
            NativeMessageDialogId: -1,
            AskDialogId: capture.DialogId,
            AskFirstQuestionLine: capture.FirstQuestionLine,
            AskLastQuestionLine: capture.LastQuestionLine);
        return ExactAskPageReadStatus.Exact;
    }

    private void RetireAsk(ushort fieldId, FieldVisibleWindowSnapshot window)
    {
        retiredAskWindow = lastExactAskWindow is { } exact
            && exact.FieldId == fieldId
            && exact.WindowId == window.WindowId
                ? exact
                : new AskWindowSnapshot(fieldId, window.WindowId, window.Text);
        lastExactAskWindow = null;
        latestAskCursor = null;
    }

    private bool ShouldSuppressRetiredAskWindow(
        ushort fieldId,
        FieldVisibleWindowSnapshot window)
    {
        if (retiredAskWindow is not { } retired)
        {
            return false;
        }

        if (retired.FieldId != fieldId)
        {
            retiredAskWindow = null;
            return false;
        }

        if (retired.WindowId != window.WindowId)
        {
            return false;
        }

        if (string.Equals(retired.RawText, window.Text, StringComparison.Ordinal))
        {
            return true;
        }

        retiredAskWindow = null;
        return false;
    }

    private bool TryReadStableWindowLifecyclePhase(int windowId, out ushort phase)
    {
        phase = 0;
        if (windowId is < 0 or >= FieldMessageReader.WindowCount)
        {
            return false;
        }

        var address = Steam2026FieldAudibleCueStateReader.AddressFieldWindowLifecyclePhases
            + ((uint)windowId * Steam2026FieldAudibleCueStateReader.FieldWindowLifecycleStride);
        if (!addressSpace.TryReadUInt16(address, out var before)
            || !addressSpace.TryReadUInt16(address, out var after)
            || before != after)
        {
            return false;
        }

        phase = before;
        return true;
    }

    private bool TryReadCompleteFrame(out DialogueCompleteFrame frame)
    {
        frame = default;
        if (!TryCaptureOwnership(out var before) ||
            before.Module != FieldPositionReader.FieldModule ||
            !messageReader.TryReadVisibleWindows(out var windows) ||
            !cueReader.TryRead(out var cue) ||
            !TryCaptureOwnership(out var after) ||
            !before.Equals(after) ||
            cue.Module != before.Module ||
            cue.ActiveMessageCount != before.ActiveMessageCount)
        {
            return false;
        }

        frame = new DialogueCompleteFrame(before, cue, windows.ToArray());
        return true;
    }

    private bool TryCaptureOwnership(out DialogueOwnershipFrame frame)
    {
        frame = default;
        if (!addressSpace.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !addressSpace.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId) ||
            !addressSpace.TryReadByte(
                (uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount,
                out var activeMessageCount) ||
            !addressSpace.TryReadUInt32(
                (uint)FieldMessageReader.AddressFieldMessageDataPointer,
                out var messageDataPointer))
        {
            return false;
        }

        var states = new byte[FieldMessageReader.WindowCount];
        var pointers = new uint[FieldMessageReader.WindowCount];
        for (var index = 0; index < FieldMessageReader.WindowCount; index++)
        {
            if (!addressSpace.TryReadByte(
                    (uint)(FieldMessageReader.AddressFieldWindowStates + index),
                    out states[index]) ||
                !addressSpace.TryReadUInt32(
                    (uint)(FieldMessageReader.AddressFieldWindowMessagePointers + index * sizeof(uint)),
                    out pointers[index]))
            {
                return false;
            }
        }

        frame = new DialogueOwnershipFrame(
            module,
            fieldId,
            activeMessageCount,
            messageDataPointer,
            states,
            pointers);
        return true;
    }

    private void ResetPageRevision()
    {
        currentPage = null;
        observedWindows.Clear();
        selectedWindowId = -1;
        observedFieldId = ushort.MaxValue;
        pageRevision = 0;
    }

    private void ClearAskLifecycle()
    {
        latestAskCursor = null;
        lastExactAskWindow = null;
        retiredAskWindow = null;
        activeMessageIngresses.Clear();
        activeMessageIngress = null;
    }

    private FieldOpcodeMessageObservation? SelectLatestActiveMessage()
    {
        ActiveMessageIngress? latest = null;
        foreach (var candidate in activeMessageIngresses.Values)
        {
            if (latest is null || candidate.ActivationSequence > latest.Value.ActivationSequence)
            {
                latest = candidate;
            }
        }

        return latest?.Observation;
    }

    private static bool SameMessageIdentity(
        FieldOpcodeMessageObservation left,
        FieldOpcodeMessageObservation right) =>
        left.Kind == right.Kind
        && left.FieldId == right.FieldId
        && left.WindowId == right.WindowId
        && left.DialogId == right.DialogId;

    private readonly record struct MessageIngressIdentity(
        int FieldId,
        int WindowId,
        int DialogId)
    {
        internal static MessageIngressIdentity From(FieldOpcodeMessageObservation observation) =>
            new(observation.FieldId, observation.WindowId, observation.DialogId);
    }

    private readonly record struct ActiveMessageIngress(
        FieldOpcodeMessageObservation Observation,
        long ActivationSequence);

    private readonly record struct DialoguePageIdentity(
        ushort FieldId,
        int WindowId,
        byte NativeState,
        uint GuestPointer,
        string Text,
        int NativeMessageDialogId,
        int AskDialogId,
        int AskFirstQuestionLine,
        int AskLastQuestionLine);

    private readonly record struct AskWindowSnapshot(
        ushort FieldId,
        int WindowId,
        string RawText);

    private enum ExactAskPageReadStatus
    {
        NotApplicable,
        Exact,
        Unavailable,
        Ended
    }

    private readonly record struct DialogueWindowIdentity(
        byte NativeState,
        uint GuestPointer,
        string Text)
    {
        internal static DialogueWindowIdentity From(FieldVisibleWindowSnapshot window) =>
            new(window.NativeState, window.GuestPointer, window.Text);
    }

    private readonly struct DialogueCompleteFrame : IEquatable<DialogueCompleteFrame>
    {
        public DialogueCompleteFrame(
            DialogueOwnershipFrame ownership,
            FieldAudibleCueState cue,
            FieldVisibleWindowSnapshot[] windows)
        {
            Ownership = ownership;
            Cue = cue;
            Windows = windows;
        }

        public DialogueOwnershipFrame Ownership { get; }

        public FieldAudibleCueState Cue { get; }

        public FieldVisibleWindowSnapshot[] Windows { get; }

        public bool Equals(DialogueCompleteFrame other) =>
            Ownership.Equals(other.Ownership) &&
            Cue == other.Cue &&
            Windows.AsSpan().SequenceEqual(other.Windows);
    }

    private readonly struct DialogueOwnershipFrame : IEquatable<DialogueOwnershipFrame>
    {
        public DialogueOwnershipFrame(
            byte module,
            ushort fieldId,
            byte activeMessageCount,
            uint messageDataPointer,
            byte[] states,
            uint[] pointers)
        {
            Module = module;
            FieldId = fieldId;
            ActiveMessageCount = activeMessageCount;
            MessageDataPointer = messageDataPointer;
            States = states;
            Pointers = pointers;
        }

        public byte Module { get; }

        public ushort FieldId { get; }

        public byte ActiveMessageCount { get; }

        public uint MessageDataPointer { get; }

        public byte[] States { get; }

        public uint[] Pointers { get; }

        public int CountActiveWindowSlots()
        {
            var count = 0;
            foreach (var state in States)
            {
                if (state != FieldMessageReader.FreeWindowState)
                {
                    count++;
                }
            }

            return count;
        }

        public bool Equals(DialogueOwnershipFrame other) =>
            Module == other.Module &&
            FieldId == other.FieldId &&
            ActiveMessageCount == other.ActiveMessageCount &&
            MessageDataPointer == other.MessageDataPointer &&
            States.AsSpan().SequenceEqual(other.States) &&
            Pointers.AsSpan().SequenceEqual(other.Pointers);
    }
}

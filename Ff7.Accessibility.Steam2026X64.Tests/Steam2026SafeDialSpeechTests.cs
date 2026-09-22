using System.Buffers.Binary;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime;

/// <summary>
/// The Steam 2026 side of the Shinra Mansion safe dial.
///
/// <para>Two things are runtime-specific here and neither is in the shared readout. The
/// dialogue pipeline has to leave the dial's own window alone while the dial is up - the
/// native message path re-posts that blank page every frame it is drawn, 2,634 times in
/// the captured attempts - and the pump holds a line that has been composed but not yet
/// delivered, because this runtime speaks from a different loop than the one that reads.
/// A held line is the one place a number the dial has already turned past could come out
/// late, so it is kept only while it still describes what is on screen.</para>
/// </summary>
internal static class Steam2026SafeDialSpeechTests
{
    private static readonly DateTime Start = new(2026, 9, 21, 20, 59, 40, DateTimeKind.Utc);

    public static void Run()
    {
        TheDialsOwnPlaceholderPageIsAcknowledgedWithoutSpeech();
        TheQuestionThatOpensTheSafeIsOrdinaryDialogue();
        SuccessAndFailAreOrdinaryDialogueWhileTheDialIsStillUp();
        ANewNumberReplacesOneThatWasNeverDelivered();
        AnUndeliveredNumberIsRetriedWhileItIsStillOnScreen();
        AnUndeliveredNumberIsDroppedOnceTheDialHasMovedPastIt();
        AnUndeliveredNumberIsDroppedWhileTheReadIsUnreliable();
        AClosedDialDropsWhateverWasWaiting();
        ADisabledReadoutLooksAtNothingAndSoOwnsNothing();
        TheDialReadsThroughTheTranslatedGuestAddressSpace();
    }

    private static void TheDialsOwnPlaceholderPageIsAcknowledgedWithoutSpeech()
    {
        var dial = OpenDial();
        var page = Page(0, "");
        var acknowledgements = new List<DialoguePageObservation>();

        var filtered = Steam2026ResearchObservationPump.SuppressSafeDialWindowDialogue(
            RuntimeDomainUpdate<DialoguePageObservation>.Present(page),
            dial,
            delivered =>
            {
                acknowledgements.Add(delivered);
                return true;
            });

        Equal(RuntimeDomainUpdateKind.Unchanged, filtered.Kind, "the dial's placeholder page is silent");
        Equal(1, acknowledgements.Count, "the placeholder page is acknowledged exactly once");
        Equal(page, acknowledgements[0], "the acknowledgement preserves the exact stable page");
    }

    private static void TheQuestionThatOpensTheSafeIsOrdinaryDialogue()
    {
        // Window 0 again, but no dial: the safe's own question is not a numeric window,
        // so the readout never owned it and the choice reaches the player.
        var dial = new ShinraMansionSafeDialReadout();
        var page = Page(0, "Open the safe");
        var acknowledged = false;

        var filtered = Steam2026ResearchObservationPump.SuppressSafeDialWindowDialogue(
            RuntimeDomainUpdate<DialoguePageObservation>.Present(page),
            dial,
            _ =>
            {
                acknowledged = true;
                return true;
            });

        Equal(RuntimeDomainUpdateKind.Present, filtered.Kind, "the safe's question remains present");
        Equal(page, filtered.Value, "the safe's question remains exact");
        Equal(false, acknowledged, "the question is acknowledged only after actual speech");
    }

    private static void SuccessAndFailAreOrdinaryDialogueWhileTheDialIsStillUp()
    {
        var dial = OpenDial();
        var page = Page(1, "Fail");

        var filtered = Steam2026ResearchObservationPump.SuppressSafeDialWindowDialogue(
            RuntimeDomainUpdate<DialoguePageObservation>.Present(page),
            dial,
            _ => true);

        Equal(RuntimeDomainUpdateKind.Present, filtered.Kind, "the result page remains present");
        Equal(page, filtered.Value, "the result page remains exact");
    }

    private static void ANewNumberReplacesOneThatWasNeverDelivered()
    {
        var dial = OpenDial();
        var opening = "Safe dial 0.";

        var moved = Turn(dial, 8, Start + ShinraMansionSafeDialReadout.MovingSpeechInterval);
        if (moved.Speech is null)
        {
            throw new InvalidOperationException("expected the turning dial to compose a number.");
        }

        Equal(
            moved.Speech,
            Steam2026ResearchObservationPump.RetainSafeDialSpeech(opening, moved, dial),
            "a fresh number replaces one that never got out");
    }

    private static void AnUndeliveredNumberIsRetriedWhileItIsStillOnScreen()
    {
        // The host was not in the foreground when "Safe dial 0." was composed. The dial
        // has not moved, so the player has still not been told what it says.
        var dial = OpenDial();
        var quiet = dial.Observe(
            ShinraMansionSafeDialReadout.FieldId,
            Window(0),
            Start.AddMilliseconds(35));

        Equal(null, quiet.Speech, "an unchanged dial composes nothing new");
        Equal(
            "Safe dial 0.",
            Steam2026ResearchObservationPump.RetainSafeDialSpeech("Safe dial 0.", quiet, dial),
            "the undelivered line is still what the dial says, so it is retried");
    }

    private static void AnUndeliveredNumberIsDroppedOnceTheDialHasMovedPastIt()
    {
        var dial = OpenDial();
        var quiet = Turn(dial, 8, Start.AddMilliseconds(35));

        Equal(null, quiet.Speech, "the throttle has not come round yet");
        Equal(
            null,
            Steam2026ResearchObservationPump.RetainSafeDialSpeech("Safe dial 0.", quiet, dial),
            "a number the dial has turned past is dropped, not spoken late");
    }

    private static void AnUndeliveredNumberIsDroppedWhileTheReadIsUnreliable()
    {
        // A torn read keeps the attempt open but vouches for no number. Speaking the
        // held line here would be asserting a value nothing currently supports.
        var dial = OpenDial();
        var unavailable = dial.ObserveUnavailable();

        Equal(true, unavailable.IsDialOnScreen, "a torn read does not close the dial");
        Equal(
            null,
            Steam2026ResearchObservationPump.RetainSafeDialSpeech("Safe dial 0.", unavailable, dial),
            "nothing is retried while no sample vouches for it");
    }

    private static void AClosedDialDropsWhateverWasWaiting()
    {
        var dial = OpenDial();
        var closed = dial.Observe(
            ShinraMansionSafeDialReadout.FieldId,
            default,
            Start.AddMilliseconds(35));

        Equal(false, closed.IsDialOnScreen, "a coherently closed window ends the attempt");
        Equal(
            null,
            Steam2026ResearchObservationPump.RetainSafeDialSpeech("Safe dial 0.", closed, dial),
            "a closed dial leaves nothing waiting to be said over the result page");
    }

    /// <summary>
    /// The option that decides whether the dial's numbers are spoken is the same one
    /// that decides whether it takes the clock's voice and its own window. When it is
    /// off the pump does not observe at all, so the readout stays reset and the timer
    /// and dialogue pipelines see the field exactly as they see it with no safe open.
    /// </summary>
    private static void ADisabledReadoutLooksAtNothingAndSoOwnsNothing()
    {
        var enabled = new AccessibilityConfig { EnableFieldActivityReadout = true };
        var disabled = new AccessibilityConfig { EnableFieldActivityReadout = false };

        Equal(
            true,
            Steam2026ResearchObservationPump.ShouldObserveSafeDial(enabled, FieldPositionReader.FieldModule),
            "an enabled readout observes the field module");
        Equal(
            false,
            Steam2026ResearchObservationPump.ShouldObserveSafeDial(disabled, FieldPositionReader.FieldModule),
            "a disabled readout does not observe, so it cannot own the clock or the window");
        Equal(
            false,
            Steam2026ResearchObservationPump.ShouldObserveSafeDial(enabled, FieldPositionReader.FieldModule + 1),
            "nothing is observed outside the field module");

        // And what "not observing" leaves behind: a reset readout answers no to every
        // ownership question the timer, the dialogue pipeline and the repeat key ask.
        var dial = OpenDial();
        Equal(true, dial.SuppressesCountdownSpeech, "an open dial owns the clock");
        dial.Reset();
        Equal(false, dial.SuppressesCountdownSpeech, "a reset readout hands the clock back");
        Equal(false, dial.OwnsWindow(ShinraMansionSafeDialReadout.DialWindowId), "a reset readout hands the window back");
        Equal(null, dial.Describe(), "a reset readout does not answer the repeat key");
    }

    /// <summary>
    /// The x86 addresses the readout depends on, read the way this runtime actually
    /// reads them: through the guest page table rather than directly.
    /// </summary>
    private static void TheDialReadsThroughTheTranslatedGuestAddressSpace()
    {
        const ulong moduleBase = 0x0000000140000000;
        var memory = new FakeNativeMemoryReader();
        var guest = new GuestImage(memory, moduleBase);

        guest.WriteByte((uint)FieldPositionReader.AddressCurrentModule, FieldPositionReader.FieldModule);
        guest.WriteUInt16((uint)FieldPositionReader.AddressFieldId, ShinraMansionSafeDialReadout.FieldId);
        guest.WriteUInt32((uint)FieldScriptControllerReader.AddressFieldScriptPointer, GuestImage.ScriptSection);
        guest.WriteByte(GuestImage.ScriptSection + FieldActivityStateReader.ScriptHeaderEntityCountOffset, 13);

        var stride = (uint)(ShinraMansionSafeDialReadout.DialWindowId * FieldActivityStateReader.WindowRecordStride);
        guest.WriteByte(
            (uint)(FieldActivityStateReader.AddressWindowStates + ShinraMansionSafeDialReadout.DialWindowId),
            3);
        guest.WriteByte(
            (uint)FieldActivityStateReader.AddressWindowDisplayType + stride,
            FieldActivityStateReader.NumericWindowDisplayType);
        guest.WriteByte((uint)FieldActivityStateReader.AddressWindowDigitLimit + stride, 2);
        guest.WriteUInt32((uint)FieldActivityStateReader.AddressWindowNumericValue + stride, 59);

        var reader = new FieldActivityStateReader(new TranslatedX86AddressSpace(moduleBase, memory));
        Equal(
            true,
            reader.TryReadNumericWindow(
                ShinraMansionSafeDialReadout.FieldId,
                ShinraMansionSafeDialReadout.DialWindowId,
                out var window),
            "the dial is read coherently through the translated address space");
        Equal(true, window.IsUsable, "the translated read finds the numeric window");
        Equal(59, window.Value, "the translated read finds WNUMB's value");
        Equal(2, window.DigitLimit, "the translated read finds WNUMB's digit count");

        var dial = new ShinraMansionSafeDialReadout();
        Equal(
            "Safe dial 59.",
            dial.Observe(ShinraMansionSafeDialReadout.FieldId, window, Start).Speech,
            "and the readout speaks it");

        // An unmapped guest page is a failed read, not a closed safe.
        var unmapped = new FieldActivityStateReader(
            new TranslatedX86AddressSpace(moduleBase, new FakeNativeMemoryReader()));
        Equal(
            false,
            unmapped.TryReadNumericWindow(
                ShinraMansionSafeDialReadout.FieldId,
                ShinraMansionSafeDialReadout.DialWindowId,
                out _),
            "an unmapped guest page reads as unknown");
    }

    /// <summary>Guest pages backed by host pages, laid out one page at a time.</summary>
    private sealed class GuestImage(FakeNativeMemoryReader memory, ulong moduleBase)
    {
        public const uint ScriptSection = 0x00A00000;

        private const ulong HostBase = 0x0000000200000000;

        private readonly Dictionary<uint, ulong> pages = new();

        public void WriteByte(uint virtualAddress, int value) =>
            Write(virtualAddress, [(byte)value]);

        public void WriteUInt16(uint virtualAddress, int value)
        {
            Span<byte> bytes = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
            Write(virtualAddress, bytes.ToArray());
        }

        public void WriteUInt32(uint virtualAddress, uint value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            Write(virtualAddress, bytes.ToArray());
        }

        private void Write(uint virtualAddress, byte[] bytes)
        {
            var page = virtualAddress >> 12;
            if (!pages.TryGetValue(page, out var host))
            {
                host = HostBase + ((ulong)pages.Count * TranslatedX86AddressSpace.PageSize);
                pages[page] = host;
                memory.MapVirtualPage(moduleBase, page, host);
            }

            memory.Write(host + (virtualAddress & 0xFFF), bytes);
        }
    }

    /// <summary>A readout that has just seen the native dial window open at zero.</summary>
    private static ShinraMansionSafeDialReadout OpenDial()
    {
        var dial = new ShinraMansionSafeDialReadout();
        dial.Observe(ShinraMansionSafeDialReadout.FieldId, Window(0), Start);
        return dial;
    }

    private static ShinraMansionSafeDialCue Turn(
        ShinraMansionSafeDialReadout dial,
        int number,
        DateTime now) =>
        dial.Observe(ShinraMansionSafeDialReadout.FieldId, Window(number), now);

    private static FieldActivityNumericWindow Window(int number) => new(true, number, 2);

    private static DialoguePageObservation Page(int windowId, string text) =>
        new(true, windowId, 7, string.Empty, text, Array.Empty<DialogueChoiceObservation>());

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}

using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Which save a playthrough's room history belongs to, and the popup that is the only
/// native evidence a save actually completed.
///
/// <para>Root's Ghidra pass established the two facts these turn on. <c>FUN_006FEB6D</c>
/// returns to page 1 on success <b>and</b> on failure, and it updates the savemap checksum
/// and preview before it opens the file, so neither the page transition nor a changed
/// checksum can be used. What separates them is the call afterwards -
/// <c>FUN_006C497C(0x00925B50, 7)</c> for success, <c>0x00925EA8</c> for failure - which
/// sets the popup byte at <c>0x00DC1310</c> and the text identity at <c>0x00DC1214</c>.</para>
/// </summary>
internal static class FieldAreaDescriptionSaveTrackerTests
{
    public static void Run()
    {
        TheResultPopupIsReadFromItsNativeIdentity();
        AnUnreadablePopupIsNotAFailedSave();
        ATornPopupReadIsRejected();
        AConfirmedSaveBindsThePlaythroughToItsSlot();
        AFailedSaveBindsNothing();
        APopupLeftOverFromAnEarlierSaveCannotBindTheNextOne();
        AnUnreadablePopupLeavesTheTransactionWaiting();
        ACancelledSaveBindsNothing();
        ALoadBindsOnlyOnceTheGameIsActuallyPlaying();
        ACancelledLoadBindsNothing();
        AFailedLoadThatReturnsToTheGamesPageBindsNothing();
        ASelectionFromAnotherFileIsNotThisLoad();
        ANewGameDoesNotInheritTheLastPlaythrough();
        ATitleNewGameIsDetectedByTheHostSequence();
        ACancelledSaveDoesNotLeaveItsSlotPending();
        AnActiveByteThatIsNotOneIsNotAPopup();
        TheFirstPlayableScanOfASuccessfulLoadBinds();
        TheFirstPlayableScanWithNoLoadIsANewGame();
        RoomsAreGatedButEventsAreNot();
        OutputThatRefusesARoomDoesNotSpendIt();
    }

    // --- the native popup read --------------------------------------------------------

    private static void TheResultPopupIsReadFromItsNativeIdentity()
    {
        var success = new PopupMemory { Active = 1, Identity = NativeSaveResultPopupReader.SuccessTextIdentity };
        Equal(true, new NativeSaveResultPopupReader(success).TryRead(out var read), "the popup reads coherently");
        Equal(true, read.IsSuccess, "the success identity is a successful save");
        Equal(false, read.IsFailure, "and is not a failure");

        var failure = new PopupMemory { Active = 1, Identity = NativeSaveResultPopupReader.FailureTextIdentity };
        Equal(true, new NativeSaveResultPopupReader(failure).TryRead(out var failed), "the failure popup reads coherently");
        Equal(true, failed.IsFailure, "the failure identity is a failed save");
        Equal(false, failed.IsSuccess, "and is not a success");
    }

    private static void AnUnreadablePopupIsNotAFailedSave()
    {
        var memory = new PopupMemory { IsReadable = false };
        Equal(false, new NativeSaveResultPopupReader(memory).TryRead(out _), "a failed read is not an answer");
    }

    private static void ATornPopupReadIsRejected()
    {
        // The popup goes down between the two bookend reads of the active byte.
        var memory = new PopupMemory { Active = 1, Identity = NativeSaveResultPopupReader.SuccessTextIdentity };
        memory.ActivePerRead = new Queue<byte>(new byte[] { 1, 0 });
        Equal(false, new NativeSaveResultPopupReader(memory).TryRead(out _), "a torn popup read is rejected");
    }

    // --- save ------------------------------------------------------------------------

    private static void AConfirmedSaveBindsThePlaythroughToItsSlot()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        var tracker = new FieldAreaDescriptionSaveTracker(history);
        history.MarkHeard(547);

        Confirm(tracker, file: 2, game: 11, NativeSaveResultPopupReader.SuccessTextIdentity);

        Equal(true, tracker.LastDiagnostic.Contains("2:11", StringComparison.Ordinal), "the save bound its own slot");

        // What binding means: the same slot loaded later remembers this room.
        history.BeginNewGame();
        Equal(false, history.HasHeard(547), "a fresh playthrough starts empty");
        history.LoadSave(2, 11);
        Equal(true, history.HasHeard(547), "loading that save restores the room it saved with");
    }

    private static void AFailedSaveBindsNothing()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        var tracker = new FieldAreaDescriptionSaveTracker(history);
        history.MarkHeard(547);

        Confirm(tracker, file: 3, game: 4, NativeSaveResultPopupReader.FailureTextIdentity);

        history.BeginNewGame();
        history.LoadSave(3, 4);
        Equal(false, history.HasHeard(547), "a save that reported failure carried nothing into its slot");
    }

    private static void APopupLeftOverFromAnEarlierSaveCannotBindTheNextOne()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        var tracker = new FieldAreaDescriptionSaveTracker(history);
        history.MarkHeard(547);

        // A success popup is already on screen when the next transaction arms. It is the
        // previous save's, and it must not complete this one.
        var stale = new NativeSaveResultPopup(true, NativeSaveResultPopupReader.SuccessTextIdentity);
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Games, 5, 6), stale);
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Confirmation, 5, 6), stale);
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Saving, 5, 6), stale);
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Saving, 5, 6), stale);
        tracker.ObserveSaveMenu(null, stale);

        history.BeginNewGame();
        history.LoadSave(5, 6);
        Equal(false, history.HasHeard(547), "a stale popup cannot bind a save");
    }

    private static void AnUnreadablePopupLeavesTheTransactionWaiting()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        var tracker = new FieldAreaDescriptionSaveTracker(history);
        history.MarkHeard(547);

        tracker.ObserveSaveMenu(Page(SaveMenuPage.Games, 7, 2), Down());
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Confirmation, 7, 2), Down());
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Saving, 7, 2), Down());

        // Two scans nobody could read, then the real success.
        tracker.ObserveSaveMenu(null, null);
        tracker.ObserveSaveMenu(null, null);
        tracker.ObserveSaveMenu(null, new NativeSaveResultPopup(true, NativeSaveResultPopupReader.SuccessTextIdentity));

        history.BeginNewGame();
        history.LoadSave(7, 2);
        Equal(true, history.HasHeard(547), "an unreadable scan waits for the result rather than losing it");
    }

    private static void ACancelledSaveBindsNothing()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        var tracker = new FieldAreaDescriptionSaveTracker(history);
        history.MarkHeard(547);

        // Highlighted and backed out: the page never reaches Saving.
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Games, 9, 9), Down());
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Confirmation, 9, 9), Down());
        tracker.ObserveSaveMenu(null, Down());

        history.BeginNewGame();
        history.LoadSave(9, 9);
        Equal(false, history.HasHeard(547), "a cancelled save binds nothing");
    }

    // --- load ------------------------------------------------------------------------

    private static void ALoadBindsOnlyOnceTheGameIsActuallyPlaying()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.SaveGame(2, 11);
        history.MarkHeard(547);
        history.BeginNewGame();

        var tracker = new FieldAreaDescriptionSaveTracker(history);
        tracker.ObserveLoadMenu(saveFile: 2, gameSlot: 11, loadingPageActive: false);
        Equal(false, history.HasHeard(547), "highlighting a save binds nothing");

        tracker.ObserveLoadMenu(saveFile: 2, gameSlot: null, loadingPageActive: true);
        Equal(false, history.HasHeard(547), "the loading page alone still binds nothing");

        tracker.ObserveEnteredPlayableModule();
        Equal(false, history.HasHeard(547), "gameplay without a readiness 2 binds nothing");

        tracker.ObserveLoadMenu(saveFile: 2, gameSlot: 11, loadingPageActive: false);
        tracker.ObserveLoadMenu(saveFile: 2, gameSlot: null, loadingPageActive: true);
        tracker.ObserveLoadReadiness(FieldAreaDescriptionSaveTracker.LoadedReadiness);
        tracker.ObserveEnteredPlayableModule();
        Equal(true, history.HasHeard(547), "readiness 2 then a playable module binds the load");
    }

    private static void ACancelledLoadBindsNothing()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.SaveGame(2, 11);
        history.MarkHeard(547);
        history.BeginNewGame();

        var tracker = new FieldAreaDescriptionSaveTracker(history);
        tracker.ObserveLoadMenu(saveFile: 2, gameSlot: 11, loadingPageActive: true);
        tracker.ObserveLoadReadiness(FieldAreaDescriptionSaveTracker.LoadedReadiness);
        tracker.ObserveLoadAbandoned();
        tracker.ObserveEnteredPlayableModule();

        Equal(false, history.HasHeard(547), "a load that was abandoned binds nothing");
    }

    /// <summary>A failed checksum returns to the Games page, not the file list.</summary>
    private static void AFailedLoadThatReturnsToTheGamesPageBindsNothing()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.SaveGame(2, 11);
        history.MarkHeard(547);
        history.BeginNewGame();

        var tracker = new FieldAreaDescriptionSaveTracker(history);
        tracker.ObserveLoadMenu(2, 11, loadingPageActive: false);
        tracker.ObserveLoadMenu(2, null, loadingPageActive: true);
        tracker.ObserveLoadMenu(2, 11, loadingPageActive: false);
        tracker.ObserveLoadReadiness(FieldAreaDescriptionSaveTracker.LoadedReadiness);
        tracker.ObserveEnteredPlayableModule();

        Equal(false, history.HasHeard(547), "a load that fell back to the games page binds nothing");
    }

    private static void ASelectionFromAnotherFileIsNotThisLoad()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.SaveGame(2, 11);
        history.MarkHeard(547);
        history.BeginNewGame();

        var tracker = new FieldAreaDescriptionSaveTracker(history);
        tracker.ObserveLoadMenu(2, 11, loadingPageActive: false);

        // The player switched files before loading; the Loading page is file 3.
        tracker.ObserveLoadMenu(3, null, loadingPageActive: true);
        tracker.ObserveLoadReadiness(FieldAreaDescriptionSaveTracker.LoadedReadiness);
        tracker.ObserveEnteredPlayableModule();

        Equal(false, history.HasHeard(547), "file 2's slot is not carried into a file 3 load");
    }

    /// <summary>The host sequence, not a direct call: title on screen, then gameplay.</summary>
    private static void ATitleNewGameIsDetectedByTheHostSequence()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.SaveGame(1, 1);
        history.MarkHeard(547);

        var tracker = new FieldAreaDescriptionSaveTracker(history);
        tracker.ObserveLoadMenu(saveFile: null, gameSlot: null, loadingPageActive: false);
        tracker.ObserveEnteredPlayableModule();

        Equal(false, history.HasHeard(547), "title then gameplay with no load is a new game");
        history.LoadSave(1, 1);
        Equal(true, history.HasHeard(547), "and the save it came from is untouched");
    }

    private static void ACancelledSaveDoesNotLeaveItsSlotPending()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        var tracker = new FieldAreaDescriptionSaveTracker(history);
        history.MarkHeard(547);

        // Selected file 4 game 5, backed out to the file list, then the next save is
        // confirmed from a page that never re-selected a slot.
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Games, 4, 5), Down());
        tracker.ObserveSaveMenu(Page(SaveMenuPage.SaveFiles, 0, 0), Down());
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Saving, 4, 5), Down());
        tracker.ObserveSaveMenu(null, new NativeSaveResultPopup(true, NativeSaveResultPopupReader.SuccessTextIdentity));

        history.BeginNewGame();
        history.LoadSave(4, 5);
        Equal(false, history.HasHeard(547), "a cancelled selection does not arm a later save");
    }

    private static void AnActiveByteThatIsNotOneIsNotAPopup()
    {
        var memory = new PopupMemory { Active = 2, Identity = NativeSaveResultPopupReader.SuccessTextIdentity };
        Equal(false, new NativeSaveResultPopupReader(memory).TryRead(out _), "only the byte FUN_006C497C writes counts");

        var noIdentity = new PopupMemory { Active = 1, Identity = 0 };
        Equal(false, new NativeSaveResultPopupReader(noIdentity).TryRead(out _), "a null text identity is not a coherent popup");
    }

    private static void ANewGameDoesNotInheritTheLastPlaythrough()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.SaveGame(1, 1);
        history.MarkHeard(547);

        var tracker = new FieldAreaDescriptionSaveTracker(history);
        tracker.ObserveNewGame();

        Equal(false, history.HasHeard(547), "a new game hears every room again");
        history.LoadSave(1, 1);
        Equal(true, history.HasHeard(547), "and the save it came from is untouched");
    }

    /// <summary>
    /// The scan that actually happens on a successful Continue: the module is already
    /// playable and readiness already reads 2, in the <b>same</b> snapshot. Observing the
    /// module first made that scan look like a new game, which cleared the pending load
    /// so the readiness in the same scan had nothing left to bind.
    /// </summary>
    private static void TheFirstPlayableScanOfASuccessfulLoadBinds()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.SaveGame(2, 11);
        history.MarkHeard(547);
        history.BeginNewGame();

        var tracker = new FieldAreaDescriptionSaveTracker(history);
        var playableSeen = false;

        // Title, save 2 game 11 highlighted, still interactive.
        FieldAreaDescriptionSaveObserver.Observe(
            tracker, TitleModule, null, Down(), 1,
            Title(TitleLoadMenuPage.Games, 2, 11), ref playableSeen);

        // The loading page, whose game number reads zero.
        FieldAreaDescriptionSaveObserver.Observe(
            tracker, TitleModule, null, Down(), 1,
            Title(TitleLoadMenuPage.Loading, 2, 0), ref playableSeen);

        // The one scan this is all about: the field is up and readiness is 2 together.
        // The menu reader reports nothing, because it only reports while readiness is 1.
        FieldAreaDescriptionSaveObserver.Observe(
            tracker, FieldPositionReader.FieldModule, null, Down(),
            FieldAreaDescriptionSaveTracker.LoadedReadiness, null, ref playableSeen);

        Equal(true, history.HasHeard(547), "the loaded save's history is bound on that scan");
        Equal(true, playableSeen, "and the scan counts as the arrival");
    }

    private static void TheFirstPlayableScanWithNoLoadIsANewGame()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        history.SaveGame(1, 1);
        history.MarkHeard(547);

        var tracker = new FieldAreaDescriptionSaveTracker(history);
        var playableSeen = false;

        FieldAreaDescriptionSaveObserver.Observe(
            tracker, TitleModule, null, Down(), 1,
            Title(TitleLoadMenuPage.TitleRoot, 0, 0), ref playableSeen);
        FieldAreaDescriptionSaveObserver.Observe(
            tracker, FieldPositionReader.FieldModule, null, Down(), 1, null, ref playableSeen);

        Equal(false, history.HasHeard(547), "a new game hears every room again");
        history.LoadSave(1, 1);
        Equal(true, history.HasHeard(547), "and the save it came from is untouched");
    }

    // --- the description gate ---------------------------------------------------------

    private static void RoomsAreGatedButEventsAreNot()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        var gate = new FieldAreaDescriptionHistoryGate(history);
        var room = Room(547, "Narrow stone paths and small bridges cross among rock columns.");
        var scene = new FieldCutsceneDescriptionCue(547, 2, 0, 0, "Red XIII turns away.");

        Equal(true, gate.ShouldOffer(room), "an unheard room is offered");
        gate.NoteSpoken(room);
        Equal(false, gate.ShouldOffer(room), "a room this playthrough has heard is not offered again");

        Equal(true, gate.ShouldOffer(scene), "a scene is not a room and is never gated");
        gate.NoteSpoken(scene);
        Equal(true, gate.ShouldOffer(scene), "a scene still happens at its own native event");

        Equal(true, new FieldAreaDescriptionHistoryGate(null).ShouldOffer(room),
            "with no history nothing is gated at all");
    }

    private static void OutputThatRefusesARoomDoesNotSpendIt()
    {
        var history = new FieldAreaDescriptionHistory(path: null);
        var gate = new FieldAreaDescriptionHistoryGate(history);
        var room = Room(525, "Cosmo Canyon.");

        // Offered, and output refused it: nothing was said, so nothing is recorded.
        Equal(true, gate.ShouldOffer(room), "the room is offered");
        Equal(true, gate.ShouldOffer(room), "a refusal leaves it to be offered again");

        gate.NoteSpoken(room);
        Equal(false, gate.ShouldOffer(room), "only an accepted delivery spends the room");
    }

    // --- helpers ----------------------------------------------------------------------

    /// <summary>A complete confirmed save transaction ending in the given result.</summary>
    private static void Confirm(
        FieldAreaDescriptionSaveTracker tracker,
        int file,
        int game,
        uint resultIdentity)
    {
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Games, file, game), Down());
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Confirmation, file, game), Down());
        tracker.ObserveSaveMenu(Page(SaveMenuPage.Saving, file, game), Down());
        tracker.ObserveSaveMenu(null, new NativeSaveResultPopup(true, resultIdentity));
    }

    private static SaveMenuStateSnapshot Page(SaveMenuPage page, int file, int game) =>
        new(page, file, game, null, 0);

    private static NativeSaveResultPopup Down() => new(false, 0);

    /// <summary>The native title module used by both runtimes.</summary>
    private const int TitleModule = TitleMenuCursorReader.TitleModule;

    private static TitleLoadMenuStateSnapshot Title(TitleLoadMenuPage page, int file, int game) =>
        new(page, file, file > 0, game, null);

    private static FieldCutsceneDescriptionCue Room(int fieldId, string text) =>
        new(fieldId, 0, 0, 0, text, FieldOpcodeAddressResolver.OpcodeMapNameIndex);

    /// <summary>Just the two popup bytes, with the knobs the cases above turn.</summary>
    private sealed class PopupMemory : ILegacyAddressSpace
    {
        public byte Active { get; set; }
        public uint Identity { get; set; }
        public bool IsReadable { get; set; } = true;
        public Queue<byte>? ActivePerRead { get; set; }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (!IsReadable)
            {
                return false;
            }

            if (virtualAddress == NativeSaveResultPopupReader.AddressPopupActive && destination.Length == 1)
            {
                destination[0] = ActivePerRead is { Count: > 0 } queued ? queued.Dequeue() : Active;
                return true;
            }

            if (virtualAddress == NativeSaveResultPopupReader.AddressPopupTextIdentity && destination.Length == 4)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(destination, Identity);
                return true;
            }

            return false;
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}

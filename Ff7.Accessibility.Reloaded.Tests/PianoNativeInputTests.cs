using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
#if !BLIND_SOLDIER_LEGACY_TESTS
using Ff7.Accessibility.Steam2026X64.Runtime.Input;
#endif

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The game's two pianos read the controller as an instrument: Tifa's (niv_ti2, field 287)
/// and the Shinra Mansion's (sinin1_2, field 298). Their [OK] handler starts a note thread
/// (entity "molody", script 2: the four face buttons, with L1 or R1 held for the upper
/// octave) and a chord thread (entity "code", script 2: the D-pad), then waits for Start;
/// Start switches both threads to their script 3. While both threads run, a resting
/// trigger - the mod's modifier - must not take any of those buttons from the game, and no
/// controller page may open over them. R3 stays reserved, and the rest of the room, every
/// other busy scene and the keyboard settings keep their ordinary behaviour.
/// </summary>
internal static class PianoNativeInputTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private const GamepadButton LeftTrigger = (GamepadButton)0x10000;
    private const GamepadButton RightTrigger = (GamepadButton)0x20000;

    /// <summary>Every button a piano reads: notes, the octave shift, chords and the end.</summary>
    private static readonly GamepadButton[] PianoButtons =
    [
        GamepadButton.A, GamepadButton.B, GamepadButton.X, GamepadButton.Y,
        GamepadButton.LeftShoulder, GamepadButton.RightShoulder,
        GamepadButton.DPadUp, GamepadButton.DPadDown, GamepadButton.DPadLeft, GamepadButton.DPadRight,
        GamepadButton.Start,
    ];

    public static void Run()
    {
        // The native state.
        ThePianoIsPlayingOnlyWhileBothNativeThreadsRun();
        TheMansionPianoIsRecognisedByItsOwnEntities();
        ARoomThatOnlyLooksLikeThePianoIsNotThePiano();
        AnUnreadableFrameIsNotAPerformance();

        // The capture policy.
        WithoutThePianoARestingTriggerStillGuardsTheButtons();
        DuringThePianoARestingTriggerTakesNothing();
        ThePianoLeaseLapsesWithoutRenewal();
        OpenNavigationClosesAndOnlyItsHeldButtonsDrain();
        TheFirstPianoReadPassesFreshNotesAndStart();
        TheSettingsPageStopsTakingThePadButStaysOpen();
        OnlyAPressTheGameNeverSawStaysPrivate();
        TheSettingsChordCannotOpenDuringThePiano();

        // The real input hooks.
#if BLIND_SOLDIER_LEGACY_TESTS
        TheXInputHookGivesThePianoItsButtonsAndKeepsR3();
#else
        TheSdlHookGivesThePianoItsButtonsAndKeepsR3();
#endif
        Console.WriteLine("PASS piano native input: both pianos' note and chord threads, resting triggers, settings and R3.");
    }

    // --- the native state ----------------------------------------------------------------

    private static void ThePianoIsPlayingOnlyWhileBothNativeThreadsRun()
    {
        var room = PianoRoom.Tifa();
        Equal(false, Read(room).IsPlaying, "before the piano is used, Tifa's room is an ordinary room");

        room.Run(PianoRoom.TifaNotes, priority: 6, script: 2);
        Equal(false, Read(room).IsPlaying, "the note thread alone is not yet the piano");

        room.Run(PianoRoom.TifaChords, priority: 6, script: 2);
        var playing = Read(room);
        Equal(true, playing.IsPlaying, "both native threads running is the piano being played");
        Equal(287, playing.FieldId, "it names the field it read");

        // Start: REQSW molody s3 / code s3, each a RETTO.
        room.Run(PianoRoom.TifaNotes, priority: 0, script: 3);
        room.Run(PianoRoom.TifaChords, priority: 0, script: 3);
        Equal(false, Read(room).IsPlaying, "once Start ends it, the room is ordinary again");
    }

    private static void TheMansionPianoIsRecognisedByItsOwnEntities()
    {
        var mansion = PianoRoom.Mansion();
        mansion.Run(11, priority: 6, script: 2);
        mansion.Run(12, priority: 6, script: 2);
        var playing = Read(mansion);
        Equal(true, playing.IsPlaying, "the Shinra Mansion piano plays through entities 11 and 12");
        Equal(298, playing.FieldId, "and is named as field 298");

        var wrongEntities = PianoRoom.Mansion();
        wrongEntities.Run(PianoRoom.TifaNotes, priority: 6, script: 2);
        wrongEntities.Run(PianoRoom.TifaChords, priority: 6, script: 2);
        Equal(false, Read(wrongEntities).IsPlaying, "Tifa's entity numbers mean nothing in the mansion");
    }

    private static void ARoomThatOnlyLooksLikeThePianoIsNotThePiano()
    {
        var otherField = PianoRoom.Tifa(fieldId: 286);
        otherField.Run(PianoRoom.TifaNotes, priority: 6, script: 2);
        otherField.Run(PianoRoom.TifaChords, priority: 6, script: 2);
        Equal(false, Read(otherField).IsPlaying, "another field with the same layout is not a piano");

        var renamed = PianoRoom.Tifa(noteName: "molodx");
        renamed.Run(PianoRoom.TifaNotes, priority: 6, script: 2);
        renamed.Run(PianoRoom.TifaChords, priority: 6, script: 2);
        Equal(false, Read(renamed).IsPlaying, "a modified script whose entity is not the native molody is not trusted");

        var resized = PianoRoom.Tifa(entityCount: 21);
        resized.Run(PianoRoom.TifaNotes, priority: 6, script: 2);
        resized.Run(PianoRoom.TifaChords, priority: 6, script: 2);
        Equal(false, Read(resized).IsPlaying, "a script with a different entity table is not trusted");

        var battle = PianoRoom.Tifa();
        battle.Run(PianoRoom.TifaNotes, priority: 6, script: 2);
        battle.Run(PianoRoom.TifaChords, priority: 6, script: 2);
        battle.Byte(FieldPositionReader.AddressCurrentModule, 2);
        Equal(false, Read(battle).IsPlaying, "outside the field module nothing is a piano");
    }

    private static void AnUnreadableFrameIsNotAPerformance()
    {
        var room = PianoRoom.Tifa();
        room.Run(PianoRoom.TifaNotes, priority: 6, script: 2);
        room.Run(PianoRoom.TifaChords, priority: 6, script: 2);
        room.Unreadable(FieldScriptControllerReader.AddressEntityScriptPriorities + PianoRoom.TifaChords);
        Equal(false, new FieldPianoPerformanceReader(room).TryRead(out _),
            "a frame that cannot be read is not reported as a performance");

        var torn = PianoRoom.Tifa();
        torn.Run(PianoRoom.TifaNotes, priority: 6, script: 2);
        torn.Run(PianoRoom.TifaChords, priority: 6, script: 2);
        torn.ChangeAfterFirstRead(FieldScriptControllerReader.AddressEntityScriptPriorities + PianoRoom.TifaNotes, 0);
        var reader = new FieldPianoPerformanceReader(torn);
        Equal(false, reader.TryRead(out var tornRead) && tornRead.IsPlaying,
            "a thread seen changing between captures is not a performance");
    }

    // --- the capture policy -----------------------------------------------------------------

    private static ControllerNavigationCapture PianoRoomCapture(bool playing)
    {
        var capture = new ControllerNavigationCapture(suppressor => new ControllerAccessibilityMenu(suppressor), () => true);

        // The piano's [OK] handler holds UC 1, so the field host publishes the room busy.
        capture.PublishContext(ControllerNavigationDomain.Field, true, true, true, Now, 287);
        capture.PublishNativeInputExclusive(playing, Now);
        _ = capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.None), Now);
        return capture;
    }

    private static void WithoutThePianoARestingTriggerStillGuardsTheButtons()
    {
        foreach (var button in PianoButtons)
        {
            var capture = PianoRoomCapture(playing: false);
            var strip = capture.ObserveRawPoll(new(true, 0, 1, LeftTrigger | button), Now);
            Equal(button, strip & button, $"outside the piano a trigger chord with {button} stays private");
        }
    }

    private static void DuringThePianoARestingTriggerTakesNothing()
    {
        foreach (var button in PianoButtons)
        {
            var capture = PianoRoomCapture(playing: true);
            _ = capture.ObserveRawPoll(new(true, 0, 1, LeftTrigger), Now);
            var strip = capture.ObserveRawPoll(new(true, 0, 2, LeftTrigger | button), Now);
            Equal(GamepadButton.None, strip & (button | LeftTrigger),
                $"while the piano plays, {button} and the resting trigger reach the game");
            Equal(false, capture.IsOpen, "no controller page opens over the piano");
            Equal(0, capture.Commands.Count + capture.AccessibilityCommands.Count, "and no command is taken from it");
        }

        var chord = PianoRoomCapture(playing: true);
        var upper = chord.ObserveRawPoll(new(true, 0, 1, RightTrigger | GamepadButton.LeftShoulder | GamepadButton.A), Now);
        Equal(GamepadButton.None, upper & (GamepadButton.LeftShoulder | GamepadButton.A), "an upper-octave chord reaches the game");

        // R3 itself is withheld by both input hooks on every read (see the hook tests below);
        // here the point is that a resting trigger plus R3 starts nothing over the piano.
        var stick = PianoRoomCapture(playing: true);
        _ = stick.ObserveRawPoll(new(true, 0, 1, LeftTrigger), Now);
        _ = stick.ObserveRawPoll(new(true, 0, 2, LeftTrigger | GamepadButton.RightThumb), Now);
        Equal(0, stick.Commands.Count, "a resting trigger and R3 do not start auto walk over the piano");
    }

    private static void ThePianoLeaseLapsesWithoutRenewal()
    {
        var capture = PianoRoomCapture(playing: true);
        Equal(true, capture.IsNativeInputExclusive(Now + TimeSpan.FromMilliseconds(500)), "a fresh lease stands");
        Equal(false, capture.IsNativeInputExclusive(Now + ControllerNavigationCapture.ContextFreshness + TimeSpan.FromMilliseconds(1)),
            "a lease nobody renews lapses");

        var later = Now + TimeSpan.FromSeconds(1);
        capture.PublishContext(ControllerNavigationDomain.Field, true, true, true, later, 287);
        _ = capture.ObserveRawPoll(new(true, 0, 1, GamepadButton.None), later);
        var strip = capture.ObserveRawPoll(new(true, 0, 2, LeftTrigger | GamepadButton.A), later);
        Equal(GamepadButton.A, strip & GamepadButton.A, "after it lapses a trigger chord is private again");

        capture.PublishNativeInputExclusive(true, later);
        capture.PublishNativeInputExclusive(false, later);
        Equal(false, capture.IsNativeInputExclusive(later), "the host withdraws it the moment the piano ends");
    }

    private static void OpenNavigationClosesAndOnlyItsHeldButtonsDrain()
    {
        var capture = new ControllerNavigationCapture(suppressor => new ControllerAccessibilityMenu(suppressor), () => true);
        capture.PublishContext(ControllerNavigationDomain.Field, true, true, false, Now, 287);
        _ = capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.None), Now);
        _ = capture.ObserveRawPoll(new(true, 0, 1, LeftTrigger), Now);
        Equal(true, capture.IsOpen, "browsing was open in Tifa's room");
        _ = capture.ObserveRawPoll(new(true, 0, 2, LeftTrigger | GamepadButton.DPadRight), Now);
        capture.Commands.ClearExceptStops();

        capture.PublishNativeInputExclusive(true, Now);
        var strip = capture.ObserveRawPoll(new(true, 0, 3, LeftTrigger | GamepadButton.DPadRight), Now);
        Equal(false, capture.IsOpen, "the piano closes browsing");
        Equal(GamepadButton.DPadRight, strip & GamepadButton.DPadRight, "the button that was browsing stays private until released");
        Equal(GamepadButton.None, strip & LeftTrigger, "but the resting trigger itself is the game's");

        strip = capture.ObserveRawPoll(new(true, 0, 4, LeftTrigger), Now);
        Equal(GamepadButton.None, strip, "once released nothing is held back");
        strip = capture.ObserveRawPoll(new(true, 0, 5, LeftTrigger | GamepadButton.DPadRight), Now);
        Equal(GamepadButton.None, strip, "the next press is a chord for the piano");
        Equal(false, capture.IsOpen, "and browsing does not reopen over it");
    }

    private static void TheFirstPianoReadPassesFreshNotesAndStart()
    {
        var capture = new ControllerNavigationCapture(suppressor => new ControllerAccessibilityMenu(suppressor), () => true);
        capture.PublishContext(ControllerNavigationDomain.Field, true, true, false, Now, 287);
        _ = capture.ObserveRawPoll(new(true, 0, 0, GamepadButton.None), Now);
        _ = capture.ObserveRawPoll(new(true, 0, 1, RightTrigger), Now);
        Equal(true, capture.IsOpen, "navigation is open before the piano starts");
        capture.PublishNativeInputExclusive(true, Now);
        var fresh = GamepadButton.A | GamepadButton.Start;
        var strip = capture.ObserveRawPoll(new(true, 0, 2, RightTrigger | fresh), Now);
        Equal(GamepadButton.None, strip & fresh,
            "fresh notes and Start on the first exclusive read do not become menu release tails");
        Equal(false, capture.IsOpen, "navigation yields to the performance");
    }

    private static void TheSettingsPageStopsTakingThePadButStaysOpen()
    {
        var capture = PianoRoomCapture(playing: false);
        capture.SetSettingsOpen(true);
        var strip = capture.ObserveRawPoll(new(true, 0, 1, GamepadButton.DPadDown), Now);
        Equal(GamepadButton.DPadDown, strip & GamepadButton.DPadDown, "the open settings page normally takes the pad");
        _ = capture.ObserveRawPoll(new(true, 0, 2, GamepadButton.None), Now);
        capture.AccessibilityCommands.ClearExceptStops();

        capture.PublishNativeInputExclusive(true, Now);
        foreach (var button in PianoButtons)
        {
            strip = capture.ObserveRawPoll(new(true, 0, 3, button), Now);
            Equal(GamepadButton.None, strip & button, $"during the piano the settings page leaves {button} to the game");
            _ = capture.ObserveRawPoll(new(true, 0, 4, GamepadButton.None), Now);
        }

        Equal(0, capture.AccessibilityCommands.Count, "no controller settings command is taken from the piano");
        Equal(true, capture.SettingsIsOpen, "the settings page stays open for the keyboard");

        capture.PublishNativeInputExclusive(false, Now);
        strip = capture.ObserveRawPoll(new(true, 0, 5, GamepadButton.DPadDown), Now);
        Equal(GamepadButton.DPadDown, strip & GamepadButton.DPadDown, "after the piano the page takes the pad again");
        Equal(true, capture.AccessibilityCommands.TryDequeue(out var command) && command == ControllerNavigationCommand.SettingsNext,
            "a fresh press after the piano moves through the settings again");
    }

    private static void OnlyAPressTheGameNeverSawStaysPrivate()
    {
        var capture = PianoRoomCapture(playing: false);
        capture.SetSettingsOpen(true);
        _ = capture.ObserveRawPoll(new(true, 0, 1, GamepadButton.DPadDown), Now);

        capture.PublishNativeInputExclusive(true, Now);
        var strip = capture.ObserveRawPoll(new(true, 0, 2, GamepadButton.DPadDown), Now);
        Equal(GamepadButton.DPadDown, strip & GamepadButton.DPadDown,
            "a direction the settings page was holding is not handed to the piano as a fresh chord mid-hold");
        _ = capture.ObserveRawPoll(new(true, 0, 3, GamepadButton.None), Now);
        strip = capture.ObserveRawPoll(new(true, 0, 4, GamepadButton.DPadDown), Now);
        Equal(GamepadButton.None, strip, "pressed again, it is the piano's chord");

        strip = capture.ObserveRawPoll(new(true, 0, 5, GamepadButton.DPadDown | GamepadButton.A), Now);
        Equal(GamepadButton.None, strip, "a chord and a note reach the game under the open page");
        capture.SetSettingsOpen(false);
        strip = capture.ObserveRawPoll(new(true, 0, 6, GamepadButton.DPadDown | GamepadButton.A), Now);
        Equal(GamepadButton.None, strip, "closing the page from the keyboard does not take a held note or chord away");
        Equal(false, capture.SettingsIsOpen, "the page closed");
    }

    private static void TheSettingsChordCannotOpenDuringThePiano()
    {
        var capture = PianoRoomCapture(playing: true);
        _ = capture.ObserveRawPoll(new(true, 0, 1, LeftTrigger | RightTrigger), Now);
        var strip = capture.ObserveRawPoll(new(true, 0, 2, LeftTrigger | RightTrigger | GamepadButton.Y), Now);
        Equal(false, capture.SettingsIsOpen, "both triggers and Y do not open settings over the piano");
        Equal(GamepadButton.None, strip & GamepadButton.Y, "Y is the piano's Mi");
        Equal(0, capture.AccessibilityCommands.Count, "no settings command is queued");
    }

    // --- the real input hooks ---------------------------------------------------------------

#if BLIND_SOLDIER_LEGACY_TESTS
    private static void TheXInputHookGivesThePianoItsButtonsAndKeepsR3()
    {
        foreach (var playing in new[] { false, true })
        {
            var physical = new XInputCaptureHook.XInputState();
            using var hook = XInputCaptureHook.CreateForDetourTest(installed =>
                new ControllerNavigationCapture(s => new ControllerAccessibilityMenu(s), installed), ["test-xinput"], () => Now);
            hook.SetOriginalForTest(0, (int slot, out XInputCaptureHook.XInputState state) =>
            {
                state = physical;
                return 0;
            });
            hook.Capture.PublishContext(ControllerNavigationDomain.Field, true, true, true, Now, 287);
            hook.Capture.PublishNativeInputExclusive(playing, Now);
            _ = hook.InvokeDetourForTest(0, 0, out _);

            // A trigger resting a third of the way down: far below FFNx's own L2 threshold
            // (0.85), past the mod's modifier threshold (64).
            physical.Gamepad.LeftTrigger = 90;
            physical.Gamepad.ThumbLX = 20000;
            var piano = GamepadButton.A | GamepadButton.LeftShoulder | GamepadButton.Start | GamepadButton.DPadDown;
            physical.Gamepad.Buttons = (ushort)(piano | GamepadButton.RightThumb);
            _ = hook.InvokeDetourForTest(0, 0, out var returned);
            if (playing)
            {
                Equal((ushort)piano, returned.Gamepad.Buttons, "FFNx sees every piano button while the piano plays");
                Equal((byte)90, returned.Gamepad.LeftTrigger, "and the resting trigger itself");
                Equal((short)20000, returned.Gamepad.ThumbLX, "and the stick that also plays chords");
            }
            else
            {
                Equal((ushort)0, returned.Gamepad.Buttons, "outside the piano the trigger chord stays private");
                Equal((byte)0, returned.Gamepad.LeftTrigger, "including the trigger");
            }
        }
    }
#else
    private static void TheSdlHookGivesThePianoItsButtonsAndKeepsR3()
    {
        foreach (var playing in new[] { false, true })
        {
            var buttons = new byte[15];
            var axes = new short[6];
            using var hook = Steam2026SdlControllerCaptureHook.CreateForDecideTest(
                installed => new(s => new ControllerAccessibilityMenu(s), installed),
                (_, b) => buttons[b], _ => 1, () => Now, originalGetAxis: (_, a) => axes[a]);
            hook.Capture.PublishContext(ControllerNavigationDomain.Field, true, true, true, Now, 287);
            hook.Capture.PublishNativeInputExclusive(playing, Now);
            _ = hook.InvokeGetButtonForTest(0x1000, 0);

            // A trigger resting a third of the way down, past the mod's modifier threshold (8000).
            axes[4] = 11000;
            axes[0] = 17000;
            foreach (var sdlButton in new[] { 0, 1, 2, 3, 6, 9, 10, 11, 12, 13, 14, 8 })
            {
                buttons[sdlButton] = 1;
            }

            foreach (var sdlButton in new[] { 0, 1, 2, 3, 6, 9, 10, 11, 12, 13, 14 })
            {
                Equal(playing ? (byte)1 : (byte)0, hook.InvokeGetButtonForTest(0x1000, sdlButton),
                    $"SDL button {sdlButton} {(playing ? "reaches the piano" : "stays private outside the piano")}");
            }

            Equal((byte)0, hook.InvokeGetButtonForTest(0x1000, 8), "R3 stays reserved against Battle Assist");
            Equal(playing ? (short)11000 : (short)0, hook.InvokeGetAxisForTest(0x1000, 4), "the resting trigger axis");
            Equal(playing ? (short)17000 : (short)0, hook.InvokeGetAxisForTest(0x1000, 0), "the stick that also plays chords");
        }
    }
#endif

    // --- helpers ---------------------------------------------------------------------------

    private static FieldPianoPerformance Read(PianoRoom room)
    {
        Equal(true, new FieldPianoPerformanceReader(room).TryRead(out var performance), "the room is readable");
        return performance;
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Piano native input: {message}: expected {expected}, got {actual}");
    }

    /// <summary>
    /// The guest memory a loaded piano room shows the script engine: module, field id, the
    /// loaded script (entity count at +2, 8-byte entity names from +0x20), each entity's
    /// current priority at 0x00CC0B30 and the script at that priority at 0x00CBF9E8, and the
    /// entity-to-model bytes (0xFF: no model).
    /// </summary>
    private sealed class PianoRoom : ILegacyAddressSpace
    {
        public const int TifaNotes = 18;
        public const int TifaChords = 19;
        private const uint ScriptPointer = 0x01400000;

        private readonly Dictionary<uint, byte> bytes = [];
        private readonly HashSet<uint> unreadable = [];
        private readonly Dictionary<uint, byte> changeAfterFirstRead = [];
        private readonly HashSet<uint> readOnce = [];

        private PianoRoom(int fieldId, int entityCount, IReadOnlyDictionary<int, string> names)
        {
            Byte(FieldPositionReader.AddressCurrentModule, FieldPositionReader.FieldModule);
            UInt16(FieldPositionReader.AddressFieldId, (ushort)fieldId);
            UInt32(FieldScriptControllerReader.AddressFieldScriptPointer, ScriptPointer);
            UInt16(ScriptPointer, 0x0502);
            Byte(ScriptPointer + 2, (byte)entityCount);
            for (var entity = 0; entity < entityCount; entity++)
            {
                var name = names.TryGetValue(entity, out var known) ? known : "e" + entity;
                for (var i = 0; i < 8; i++)
                {
                    Byte(ScriptPointer + 0x20 + (uint)(entity * 8 + i), i < name.Length ? (byte)name[i] : (byte)0);
                }

                // Idle: priority 7, running the entity's main script.
                Byte((uint)(FieldScriptControllerReader.AddressEntityScriptPriorities + entity), 7);
                Byte((uint)(FieldScriptControllerReader.AddressEntityScriptIds + (entity * 8) + 7), 0);
                Byte((uint)(FieldScriptControllerReader.AddressEntityModelIds + entity), FieldScriptControllerReader.NoModel);
            }
        }

        public static PianoRoom Tifa(int fieldId = 287, int entityCount = 22, string noteName = "molody") =>
            new(fieldId, entityCount, new Dictionary<int, string> { [17] = "piano", [TifaNotes] = noteName, [TifaChords] = "code", [20] = "mesboad" });

        public static PianoRoom Mansion() =>
            new(298, 16, new Dictionary<int, string> { [10] = "plin0", [11] = "molody", [12] = "code", [13] = "mesboad" });

        /// <summary>The entity runs <paramref name="script"/> at <paramref name="priority"/>, as REQ/REQSW leave it.</summary>
        public void Run(int entity, int priority, int script)
        {
            Byte((uint)(FieldScriptControllerReader.AddressEntityScriptPriorities + entity), (byte)priority);
            Byte((uint)(FieldScriptControllerReader.AddressEntityScriptIds + (entity * 8) + priority), (byte)script);
        }

        public void Byte(uint address, int value) => bytes[address] = (byte)value;

        public void Byte(int address, int value) => Byte((uint)address, value);

        public void Unreadable(int address) => unreadable.Add((uint)address);

        public void ChangeAfterFirstRead(int address, byte value) => changeAfterFirstRead[(uint)address] = value;

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var i = 0; i < destination.Length; i++)
            {
                var address = virtualAddress + (uint)i;
                if (unreadable.Contains(address))
                {
                    return false;
                }

                if (changeAfterFirstRead.TryGetValue(address, out var later) && !readOnce.Add(address))
                {
                    destination[i] = later;
                    continue;
                }

                destination[i] = bytes.TryGetValue(address, out var value) ? value : (byte)0;
            }

            return true;
        }

        private void UInt16(uint address, ushort value)
        {
            Byte(address, value & 0xFF);
            Byte(address + 1, value >> 8);
        }

        private void UInt16(int address, ushort value) => UInt16((uint)address, value);

        private void UInt32(int address, uint value)
        {
            UInt16((uint)address, (ushort)value);
            UInt16((uint)address + 2, (ushort)(value >> 16));
        }
    }
}

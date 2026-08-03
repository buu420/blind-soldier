using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Dialogue;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal static class Steam2026FieldDialogueObservationTests
{
    private static Steam2026FingerprintResult supportedFingerprint = null!;
    private static Steam2026FingerprintResult unsupportedFingerprint = null!;

    public static void Run(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        supportedFingerprint = supported;
        unsupportedFingerprint = unsupported;
        ReadsOneOwnedVisibleWindowEquallyFromDirectAndTranslatedMemory();
        ReadsChangedDialogueBesidePersistentTimer();
        DeduplicatesPagesAndResetsRevisionOnlyOnExplicitClosure();
        RejectsZeroMultipleBlankAndStaleOwnedWindows();
        RejectsPreviewTableAndLineBufferOnlyCandidates();
        RejectsUnmappedDialogueState();
        RejectsTranslatedDialoguePageRemapping();
        RejectsTornDialogueState();
        ReadsExactNativeAskPromptAndChoices();
        ReadsMultiPageAskFromExactChoicePage();
        TransfersAskOwnershipToCrossWindowMessageSuccessor();
        ReadsExactCrossWindowMessageWhileItsVisibleMirrorIsStillBlank();
        ReadsExactMessageWhenItsWindowStillContainsTheRetiredAskMirror();
        ReadsExactMessageBeforeItsTargetWindowSlotIsAssigned();
        NativeMessageIngressOutranksStaleCurrentOpcodeGlobals();
        TransfersAskOwnershipFromNativeMessageIngressWhenPollingMissesSuccessor();
        RejectsOlderQueuedAskAfterNativeMessageIngress();
        PreservesNativeAskOwnershipAcrossTransientReadsAndPointerChurn();
        PublicReaderRequiresExactTranslatedResolver();
        KeepsDialogueReaderOrdinaryResearchOnly();
    }

    private static void ReadsExactNativeAskPromptAndChoices()
    {
        const uint scriptPointer = 0x00400000;
        var fixture = DialogueObservationFixture.CreateOpen("Flower girl What happened? Buy one Forget it");
        fixture.WriteMessageTableBytes(
            [
                .. Encode("Flower girl What happened?")[..^1],
                0xE7,
                0xE0,
                .. Encode("Buy one")[..^1],
                0xE7,
                0xE0,
                .. Encode("Forget it")
            ]);
        fixture.Write((uint)FieldOpcodeParameterReader.AddressFieldScriptPtr, BitConverter.GetBytes(scriptPointer));
        fixture.Write(scriptPointer + 2, [1]);
        fixture.WriteByte(FieldOpcodeParameterReader.AddressCurrentEntityId, 0);
        fixture.Write(
            (uint)FieldOpcodeParameterReader.AddressFieldCurrScriptPosition,
            BitConverter.GetBytes((ushort)0x20));
        fixture.Write(
            scriptPointer + 0x20,
            [FieldOpcodeParameterReader.AskOpcode, 0, 0, 0, 1, 2, 6]);
        fixture.WriteWindowLifecyclePhase(0, 6);

        Equal(
            true,
            new FieldOpcodeParameterReader(fixture.Direct).TryReadAsk(out var ownedAsk),
            "fixture owns the exact native ASK instruction");
        Equal(1, ownedAsk.FirstQuestionLine, "fixture ASK first choice row");
        Equal(2, ownedAsk.LastQuestionLine, "fixture ASK last choice row");
        Equal(
            true,
            new FieldMessageReader(fixture.Direct).TryReadMessageLinesById(0, out var nativeLines),
            "fixture owns the exact native ASK message text");
        SequenceEqual(
            ["Flower girl What happened?", "Buy one", "Forget it"],
            nativeLines,
            "fixture ASK native line split");

        var reader = CreateTranslatedReader(fixture);
        reader.ObserveAskCursorCapture(new Steam2026AskCursorIngressSnapshot(
            1,
            new DateTime(2026, 7, 22, 21, 0, 0, DateTimeKind.Utc),
            new Steam2026AskCursorCapture(116, 0, 0, 1, 2, 1)));

        Equal(true, reader.TryRead(out var first), "native ASK page");
        Equal("Flower girl What happened?", first.VisibleText, "ASK prompt excludes sighted choice rows");
        Equal(2, first.Choices.Length, "ASK exposes both native choice rows");
        Equal("Buy one", first.Choices[0].Text, "first native choice text");
        Equal(true, first.Choices[0].Selected, "first native choice selected");
        Equal("Forget it", first.Choices[1].Text, "second native choice text");
        Equal(false, first.Choices[1].Selected, "second native choice initially unselected");

        reader.ObserveAskCursorCapture(new Steam2026AskCursorIngressSnapshot(
            2,
            new DateTime(2026, 7, 22, 21, 0, 1, DateTimeKind.Utc),
            new Steam2026AskCursorCapture(116, 0, 0, 1, 2, 2)));
        Equal(true, reader.TryRead(out var moved), "native ASK cursor move");
        Equal(first.PageRevision, moved.PageRevision, "ASK cursor movement does not create a new page");
        Equal(true, moved.Choices[1].Selected, "second native choice selected after cursor move");

        reader.ObserveAskCursorCapture(new Steam2026AskCursorIngressSnapshot(
            3,
            new DateTime(2026, 7, 22, 21, 0, 2, DateTimeKind.Utc),
            new Steam2026AskCursorCapture(116, 0, 0, 1, 2, 2)));
        Equal(true, reader.TryRead(out var repeated), "repeated native ASK callback remains readable");
        Equal(moved.PageRevision, repeated.PageRevision, "unchanged ASK callback preserves page identity");
        Equal(moved.VisibleText, repeated.VisibleText, "unchanged ASK callback preserves prompt");
        Equal(moved.Choices.Length, repeated.Choices.Length, "unchanged ASK callback preserves choice count");
        Equal(true, repeated.Choices[1].Selected, "unchanged ASK callback preserves native selection");

        fixture.WriteWindowLifecyclePhase(0, 7);
        fixture.WriteVisibleText(0, "Flower girl Take care.");
        Equal(true, reader.TryRead(out var successor), "ordinary dialogue after confirmed ASK");
        Equal("Flower girl Take care.", successor.VisibleText, "confirmed ASK cannot replace successor dialogue");
        Equal(0, successor.Choices.Length, "confirmed ASK choices retire at the native lifecycle boundary");

        fixture.WriteWindowLifecyclePhase(0, 6);
        Equal(true, reader.TryRead(out var recycledPhase), "successor dialogue after lifecycle phase reuse");
        Equal("Flower girl Take care.", recycledPhase.VisibleText, "retired ASK capture cannot revive on a reused phase");
        Equal(0, recycledPhase.Choices.Length, "retired ASK remains absent after phase reuse");
    }

    private static void ReadsMultiPageAskFromExactChoicePage()
    {
        const uint scriptPointer = 0x00400000;
        var fixture = DialogueObservationFixture.CreateOpen(
            "There's just one condition.");
        fixture.WriteMessageTableBytes(
        [
            .. Encode("There's just one condition.")[..^1],
            0xE8,
            .. Encode("You gotta give me some cash.")[..^1],
            0xE7,
            0xE0,
            .. Encode("1 gil")[..^1],
            0xE7,
            0xE0,
            .. Encode("10 gil")[..^1],
            0xE7,
            0xE0,
            .. Encode("nothin'")
        ]);
        fixture.Write((uint)FieldOpcodeParameterReader.AddressFieldScriptPtr, BitConverter.GetBytes(scriptPointer));
        fixture.Write(scriptPointer + 2, [1]);
        fixture.WriteByte(FieldOpcodeParameterReader.AddressCurrentEntityId, 0);
        fixture.Write(
            (uint)FieldOpcodeParameterReader.AddressFieldCurrScriptPosition,
            BitConverter.GetBytes((ushort)0x20));
        fixture.Write(
            scriptPointer + 0x20,
            [FieldOpcodeParameterReader.AskOpcode, 0, 0, 0, 1, 3, 6]);
        fixture.WriteWindowLifecyclePhase(0, 6);

        var reader = CreateTranslatedReader(fixture);
        reader.ObserveAskCursorCapture(new Steam2026AskCursorIngressSnapshot(
            1,
            new DateTime(2026, 7, 25, 19, 30, 0, DateTimeKind.Utc),
            new Steam2026AskCursorCapture(116, 0, 0, 1, 3, 1)));

        Equal(true, reader.TryRead(out var leadingPage), "multi-page ASK leading ordinary page");
        Equal("There's just one condition.", leadingPage.VisibleText, "multi-page ASK leading page text");
        Equal(0, leadingPage.Choices.Length, "leading page cannot expose later choices");

        fixture.WriteVisibleText(
            0,
            "You gotta give me some cash. 1 gil 10 gil nothin'");
        Equal(true, reader.TryRead(out var page), "multi-page ASK exact choice page");
        Equal("You gotta give me some cash.", page.VisibleText, "multi-page ASK excludes the prior page");
        Equal(3, page.Choices.Length, "multi-page ASK choice count");
        Equal("1 gil", page.Choices[0].Text, "multi-page ASK first choice");
        Equal("10 gil", page.Choices[1].Text, "multi-page ASK second choice");
        Equal("nothin'", page.Choices[2].Text, "multi-page ASK third choice");
        Equal(true, page.Choices[0].Selected, "multi-page ASK native highlight");
    }

    private static void TransfersAskOwnershipToCrossWindowMessageSuccessor()
    {
        const uint scriptPointer = 0x00400000;
        var fixture = DialogueObservationFixture.CreateOpen(
            "Flower girl Oh, these? Do you like them? They're only one gil. Buy one Forget it");
        fixture.WriteMessageTableBytes(
        [
            .. Encode("Flower girl Oh, these? Do you like them? They're only one gil.")[..^1],
            0xE7,
            0xE0,
            .. Encode("Buy one")[..^1],
            0xE7,
            0xE0,
            .. Encode("Forget it")
        ]);
        fixture.Write((uint)FieldOpcodeParameterReader.AddressFieldScriptPtr, BitConverter.GetBytes(scriptPointer));
        fixture.Write(scriptPointer + 2, [1]);
        fixture.WriteByte(FieldOpcodeParameterReader.AddressCurrentEntityId, 0);
        fixture.Write(
            (uint)FieldOpcodeParameterReader.AddressFieldCurrScriptPosition,
            BitConverter.GetBytes((ushort)0x20));
        fixture.Write(
            scriptPointer + 0x20,
            [FieldOpcodeParameterReader.AskOpcode, 0, 0, 0, 1, 2, 6]);
        fixture.WriteWindowLifecyclePhase(0, 6);

        var reader = CreateTranslatedReader(fixture);
        reader.ObserveAskCursorCapture(new Steam2026AskCursorIngressSnapshot(
            1,
            new DateTime(2026, 7, 23, 11, 0, 59, DateTimeKind.Utc),
            new Steam2026AskCursorCapture(116, 0, 0, 1, 2, 1)));

        Equal(true, reader.TryRead(out var ask), "flower purchase ASK page");
        Equal("Buy one", ask.Choices[0].Text, "flower purchase native buy choice");

        fixture.Write(
            scriptPointer + 0x20,
            [FieldOpcodeParameterReader.MessageOpcode, 2, 8]);
        fixture.OpenWindow(2, "Flower girl Oh, thank you!");
        fixture.WriteWindowLifecyclePhase(2, 6);

        Equal(
            true,
            reader.TryRead(out var successor),
            "native MESSAGE on a different window supersedes the completed ASK");
        Equal(2, successor.WindowId, "cross-window successor owns its native window");
        Equal(
            "Flower girl Oh, thank you!",
            successor.VisibleText,
            "flower purchase response remains readable after the choice");
        Equal(0, successor.Choices.Length, "cross-window successor has no retired ASK choices");
        Equal(
            ask.PageRevision + 1,
            successor.PageRevision,
            "cross-window successor creates exactly one new dialogue page");

        fixture.Write(
            scriptPointer + 0x20,
            [FieldOpcodeParameterReader.MessageOpcode, 2, 9]);
        fixture.WriteVisibleText(2, "Flower girl Here you are!");
        Equal(true, reader.TryRead(out var secondSuccessor), "second flower response remains readable");
        Equal(
            "Flower girl Here you are!",
            secondSuccessor.VisibleText,
            "dialogue continues normally after cross-window ownership transfer");
        Equal(
            successor.PageRevision + 1,
            secondSuccessor.PageRevision,
            "second flower response advances the dialogue page once");
    }

    private static void ReadsExactCrossWindowMessageWhileItsVisibleMirrorIsStillBlank()
    {
        const uint scriptPointer = 0x00400000;
        var fixture = DialogueObservationFixture.CreateOpen("Buy one");
        fixture.WriteMessageTableTexts(
            "Buy one",
            "Flower girl Oh, thank you!");
        fixture.Write((uint)FieldOpcodeParameterReader.AddressFieldScriptPtr, BitConverter.GetBytes(scriptPointer));
        fixture.Write(scriptPointer + 2, [1]);
        fixture.WriteByte(FieldOpcodeParameterReader.AddressCurrentEntityId, 0);
        fixture.Write(
            (uint)FieldOpcodeParameterReader.AddressFieldCurrScriptPosition,
            BitConverter.GetBytes((ushort)0x20));
        fixture.Write(
            scriptPointer + 0x20,
            [FieldOpcodeParameterReader.AskOpcode, 0, 0, 0, 0, 0, 6]);
        fixture.WriteWindowLifecyclePhase(0, 6);

        var reader = CreateTranslatedReader(fixture);
        reader.ObserveAskCursorCapture(new Steam2026AskCursorIngressSnapshot(
            1,
            new DateTime(2026, 7, 23, 19, 0, 0, DateTimeKind.Utc),
            new Steam2026AskCursorCapture(116, 0, 0, 0, 0, 0)));
        Equal(true, reader.TryRead(out _), "flower ASK is established before its response");

        fixture.Write(
            scriptPointer + 0x20,
            [FieldOpcodeParameterReader.MessageOpcode, 2, 1]);
        fixture.OpenWindow(2, string.Empty);

        Equal(
            true,
            reader.TryRead(out var successor),
            "exact active MESSAGE survives a blank cross-window text mirror");
        Equal(2, successor.WindowId, "blank-mirror successor retains native window ownership");
        Equal(
            "Flower girl Oh, thank you!",
            successor.VisibleText,
            "blank-mirror successor uses the exact active native message text");
        Equal(0, successor.Choices.Length, "blank-mirror successor retires ASK choices");
    }

    private static void ReadsExactMessageWhenItsWindowStillContainsTheRetiredAskMirror()
    {
        const uint scriptPointer = 0x00400000;
        var fixture = DialogueObservationFixture.CreateOpen("Buy one");
        fixture.WriteMessageTableTexts(
            "Buy one",
            "unused 1",
            "unused 2",
            "unused 3",
            "unused 4",
            "unused 5",
            "unused 6",
            "unused 7",
            "Flower girl Oh, thank you!");
        fixture.Write((uint)FieldOpcodeParameterReader.AddressFieldScriptPtr, BitConverter.GetBytes(scriptPointer));
        fixture.Write(scriptPointer + 2, [1]);
        fixture.WriteByte(FieldOpcodeParameterReader.AddressCurrentEntityId, 0);
        fixture.Write(
            (uint)FieldOpcodeParameterReader.AddressFieldCurrScriptPosition,
            BitConverter.GetBytes((ushort)0x20));
        fixture.Write(
            scriptPointer + 0x20,
            [FieldOpcodeParameterReader.AskOpcode, 0, 0, 0, 0, 0, 6]);
        fixture.WriteWindowLifecyclePhase(0, 6);

        var reader = CreateTranslatedReader(fixture);
        reader.ObserveAskCursorCapture(new Steam2026AskCursorIngressSnapshot(
            1,
            new DateTime(2026, 7, 23, 20, 30, 0, DateTimeKind.Utc),
            new Steam2026AskCursorCapture(116, 0, 0, 0, 0, 0)));
        Equal(true, reader.TryRead(out _), "flower ASK establishes its native mirror");

        // The native callback has moved to MESSAGE 8, but the reusable window
        // buffer can retain the final ASK selection until the response draw.
        fixture.Write(scriptPointer + 0x20, [0]);
        reader.ObserveMessageLifecycle(new Steam2026FieldMessageIngressSnapshot(
            2,
            new DateTime(2026, 7, 23, 20, 30, 1, DateTimeKind.Utc),
            new FieldOpcodeMessageObservation(
                FieldOpcodeKind.Message,
                FieldId: 116,
                WindowId: 0,
                DialogId: 8),
            Result: 1));

        Equal(
            true,
            reader.TryRead(out var response),
            "exact MESSAGE remains readable while its window mirror still contains the retired ASK");
        Equal(
            "Flower girl Oh, thank you!",
            response.VisibleText,
            "retired ASK mirror cannot replace the exact active MESSAGE text");
        Equal(0, response.Choices.Length, "retired ASK choices stay retired");
    }

    private static void ReadsExactMessageBeforeItsTargetWindowSlotIsAssigned()
    {
        const uint scriptPointer = 0x00400000;
        var fixture = DialogueObservationFixture.CreateOpen("Buy one");
        fixture.WriteMessageTableTexts(
            "Buy one",
            "unused 1",
            "unused 2",
            "unused 3",
            "unused 4",
            "unused 5",
            "unused 6",
            "unused 7",
            "Flower girl Oh, thank you!");
        fixture.Write((uint)FieldOpcodeParameterReader.AddressFieldScriptPtr, BitConverter.GetBytes(scriptPointer));
        fixture.Write(scriptPointer + 2, [1]);
        fixture.WriteByte(FieldOpcodeParameterReader.AddressCurrentEntityId, 0);
        fixture.Write(
            (uint)FieldOpcodeParameterReader.AddressFieldCurrScriptPosition,
            BitConverter.GetBytes((ushort)0x20));
        fixture.Write(
            scriptPointer + 0x20,
            [FieldOpcodeParameterReader.AskOpcode, 0, 0, 0, 0, 0, 6]);
        fixture.WriteWindowLifecyclePhase(0, 6);

        var reader = CreateTranslatedReader(fixture);
        reader.ObserveAskCursorCapture(new Steam2026AskCursorIngressSnapshot(
            1,
            new DateTime(2026, 7, 23, 20, 31, 0, DateTimeKind.Utc),
            new Steam2026AskCursorCapture(116, 0, 0, 0, 0, 0)));
        Equal(true, reader.TryRead(out _), "flower ASK is readable before the response");

        fixture.Write(scriptPointer + 0x20, [0]);
        reader.ObserveMessageLifecycle(new Steam2026FieldMessageIngressSnapshot(
            2,
            new DateTime(2026, 7, 23, 20, 31, 1, DateTimeKind.Utc),
            new FieldOpcodeMessageObservation(
                FieldOpcodeKind.Message,
                FieldId: 116,
                WindowId: 2,
                DialogId: 8),
            Result: 1));

        Equal(
            true,
            reader.TryRead(out var response),
            "checked MESSAGE ingress remains readable before its target slot is assigned");
        Equal(2, response.WindowId, "pre-assignment response retains native target window");
        Equal(
            "Flower girl Oh, thank you!",
            response.VisibleText,
            "pre-assignment response resolves its exact native message table entry");
    }

    private static void NativeMessageIngressOutranksStaleCurrentOpcodeGlobals()
    {
        const uint scriptPointer = 0x00400000;
        var fixture = DialogueObservationFixture.CreateOpen("stale current message");
        fixture.WriteMessageTableTexts(
            "stale current message",
            "unused 1",
            "unused 2",
            "unused 3",
            "unused 4",
            "unused 5",
            "unused 6",
            "unused 7",
            "Flower girl Oh, thank you!");
        fixture.Write((uint)FieldOpcodeParameterReader.AddressFieldScriptPtr, BitConverter.GetBytes(scriptPointer));
        fixture.Write(scriptPointer + 2, [1]);
        fixture.WriteByte(FieldOpcodeParameterReader.AddressCurrentEntityId, 0);
        fixture.Write(
            (uint)FieldOpcodeParameterReader.AddressFieldCurrScriptPosition,
            BitConverter.GetBytes((ushort)0x20));
        fixture.Write(
            scriptPointer + 0x20,
            [FieldOpcodeParameterReader.MessageOpcode, 0, 0]);

        var reader = CreateTranslatedReader(fixture);
        reader.ObserveMessageLifecycle(new Steam2026FieldMessageIngressSnapshot(
            2,
            new DateTime(2026, 7, 23, 20, 32, 0, DateTimeKind.Utc),
            new FieldOpcodeMessageObservation(
                FieldOpcodeKind.Message,
                FieldId: 116,
                WindowId: 2,
                DialogId: 8),
            Result: 1));

        Equal(
            true,
            reader.TryRead(out var response),
            "native callback ingress remains readable beside stale interpreter globals");
        Equal(2, response.WindowId, "callback ingress owns the exact active window");
        Equal(
            "Flower girl Oh, thank you!",
            response.VisibleText,
            "stale current-opcode globals cannot replace newer callback ingress");
    }

    private static void TransfersAskOwnershipFromNativeMessageIngressWhenPollingMissesSuccessor()
    {
        const uint scriptPointer = 0x00400000;
        var fixture = DialogueObservationFixture.CreateOpen("Buy one Forget it");
        fixture.WriteMessageTableBytes(
        [
            0xE0,
            .. Encode("Buy one")[..^1],
            0xE7,
            0xE0,
            .. Encode("Forget it")
        ]);
        fixture.Write((uint)FieldOpcodeParameterReader.AddressFieldScriptPtr, BitConverter.GetBytes(scriptPointer));
        fixture.Write(scriptPointer + 2, [1]);
        fixture.WriteByte(FieldOpcodeParameterReader.AddressCurrentEntityId, 0);
        fixture.Write(
            (uint)FieldOpcodeParameterReader.AddressFieldCurrScriptPosition,
            BitConverter.GetBytes((ushort)0x20));
        fixture.Write(
            scriptPointer + 0x20,
            [FieldOpcodeParameterReader.AskOpcode, 0, 0, 0, 0, 1, 6]);
        fixture.WriteWindowLifecyclePhase(0, 6);

        var reader = CreateTranslatedReader(fixture);
        reader.ObserveAskCursorCapture(new Steam2026AskCursorIngressSnapshot(
            1,
            new DateTime(2026, 7, 23, 11, 35, 48, DateTimeKind.Utc),
            new Steam2026AskCursorCapture(116, 0, 0, 0, 1, 0)));
        Equal(true, reader.TryRead(out _), "native ASK owns the flower purchase choices");

        // The live x64 poll can occur after the interpreter leaves MESSAGE,
        // so the callback must preserve the exact lifecycle independently of
        // the transient current-opcode globals.
        fixture.Write(scriptPointer + 0x20, [0]);
        fixture.OpenWindow(2, "Flower girl Oh, thank you!");
        reader.ObserveMessageLifecycle(new Steam2026FieldMessageIngressSnapshot(
            3,
            new DateTime(2026, 7, 23, 11, 35, 49, DateTimeKind.Utc),
            new FieldOpcodeMessageObservation(
                FieldOpcodeKind.Message,
                FieldId: 116,
                WindowId: 2,
                DialogId: 8),
            Result: 1));

        Equal(
            true,
            reader.TryRead(out var successor),
            "native MESSAGE ingress survives a missed polling opcode");
        Equal(2, successor.WindowId, "native MESSAGE ingress selects the successor window");
        Equal(
            "Flower girl Oh, thank you!",
            successor.VisibleText,
            "native MESSAGE ingress preserves the actual visible buffer text");
        Equal(0, successor.Choices.Length, "native MESSAGE ingress retires the old ASK choices");
    }

    private static void RejectsOlderQueuedAskAfterNativeMessageIngress()
    {
        const uint scriptPointer = 0x00400000;
        var fixture = DialogueObservationFixture.CreateOpen("Buy one Forget it");
        fixture.WriteMessageTableBytes(
        [
            0xE0,
            .. Encode("Buy one")[..^1],
            0xE7,
            0xE0,
            .. Encode("Forget it")
        ]);
        fixture.Write((uint)FieldOpcodeParameterReader.AddressFieldScriptPtr, BitConverter.GetBytes(scriptPointer));
        fixture.Write(scriptPointer + 2, [1]);
        fixture.WriteByte(FieldOpcodeParameterReader.AddressCurrentEntityId, 0);
        fixture.Write(
            (uint)FieldOpcodeParameterReader.AddressFieldCurrScriptPosition,
            BitConverter.GetBytes((ushort)0x20));
        fixture.Write(
            scriptPointer + 0x20,
            [FieldOpcodeParameterReader.AskOpcode, 0, 0, 0, 0, 1, 6]);
        fixture.WriteWindowLifecyclePhase(0, 6);

        var reader = CreateTranslatedReader(fixture);
        reader.ObserveAskCursorCapture(new Steam2026AskCursorIngressSnapshot(
            1,
            new DateTime(2026, 7, 23, 11, 35, 47, DateTimeKind.Utc),
            new Steam2026AskCursorCapture(116, 0, 0, 0, 1, 0)));
        Equal(true, reader.TryRead(out _), "ASK is established before the response");

        fixture.Write(scriptPointer + 0x20, [0]);
        fixture.OpenWindow(2, "Flower girl Oh, thank you!");
        reader.ObserveMessageLifecycle(new Steam2026FieldMessageIngressSnapshot(
            2,
            new DateTime(2026, 7, 23, 11, 35, 49, DateTimeKind.Utc),
            new FieldOpcodeMessageObservation(
                FieldOpcodeKind.Message,
                FieldId: 116,
                WindowId: 2,
                DialogId: 8),
            Result: 1));

        // The session drains MESSAGE and ASK queues independently. An ASK
        // cursor event captured before the MESSAGE must not reclaim ownership
        // merely because its queue is drained second.
        reader.ObserveAskCursorCapture(new Steam2026AskCursorIngressSnapshot(
            2,
            // A post-call timestamp can be later even though this outer ASK
            // entered before the nested successor MESSAGE. Native entry order,
            // not callback completion time, decides current ownership.
            new DateTime(2026, 7, 23, 11, 35, 50, DateTimeKind.Utc),
            new Steam2026AskCursorCapture(116, 0, 0, 0, 1, 0)));

        Equal(
            true,
            reader.TryRead(out var successor),
            "an older queued ASK event cannot hide the newer native MESSAGE");
        Equal(2, successor.WindowId, "newer MESSAGE retains native window ownership");
        Equal(
            "Flower girl Oh, thank you!",
            successor.VisibleText,
            "newer MESSAGE remains the visible speech source");
        Equal(0, successor.Choices.Length, "stale ASK choices do not return");
    }

    private static void PreservesNativeAskOwnershipAcrossTransientReadsAndPointerChurn()
    {
        const uint scriptPointer = 0x00400000;
        var fixture = DialogueObservationFixture.CreateOpen(
            "Flower girl What happened? You'd better get out of here Nothing... hey...");
        fixture.WriteMessageTableBytes(
        [
            .. Encode("Flower girl What happened?")[..^1],
            0xE7,
            0xE0,
            .. Encode("You'd better get out of here")[..^1],
            0xE7,
            0xE0,
            .. Encode("Nothing... hey...")
        ]);
        fixture.Write((uint)FieldOpcodeParameterReader.AddressFieldScriptPtr, BitConverter.GetBytes(scriptPointer));
        fixture.Write(scriptPointer + 2, [1]);
        fixture.WriteByte(FieldOpcodeParameterReader.AddressCurrentEntityId, 0);
        fixture.Write(
            (uint)FieldOpcodeParameterReader.AddressFieldCurrScriptPosition,
            BitConverter.GetBytes((ushort)0x20));
        fixture.Write(
            scriptPointer + 0x20,
            [FieldOpcodeParameterReader.AskOpcode, 0, 0, 0, 1, 2, 6]);
        fixture.WriteWindowLifecyclePhase(0, 6);

        var faultingMemory = new OneShotReadFailureAddressSpace(fixture.Direct);
        var reader = new Steam2026FieldDialogueObservationReader(faultingMemory);
        reader.ObserveAskCursorCapture(new Steam2026AskCursorIngressSnapshot(
            1,
            new DateTime(2026, 7, 22, 23, 16, 25, DateTimeKind.Utc),
            new Steam2026AskCursorCapture(116, 0, 0, 1, 2, 1)));

        Equal(true, reader.TryRead(out var first), "native ASK establishes one structured page");
        Equal(1, first.PageRevision, "native ASK initial page revision");

        fixture.Write(
            (uint)FieldMessageReader.AddressFieldWindowMessagePointers,
            BitConverter.GetBytes(DialogueObservationFixture.MessageDataPointer + 0x40u));
        Equal(true, reader.TryRead(out var pointerChanged), "native ASK remains readable after guest stream pointer churn");
        Equal(
            first.PageRevision,
            pointerChanged.PageRevision,
            "volatile guest stream pointer must not invent a new ASK page");

        faultingMemory.FailNextRead(
            Steam2026FieldAudibleCueStateReader.AddressFieldWindowLifecyclePhases);
        Equal(
            false,
            reader.TryRead(out _),
            "a transient ASK lifecycle read failure must retain prior ownership instead of exposing raw choice text");
        Equal(true, reader.TryRead(out var recovered), "native ASK recovers after transient lifecycle read failure");
        Equal(
            first.PageRevision,
            recovered.PageRevision,
            "transient ASK lifecycle failure must not advance the page revision");

        fixture.WriteWindowLifecyclePhase(0, 7);
        Equal(
            false,
            reader.TryRead(out _),
            "retiring ASK window must not expose its unchanged raw prompt and choice rows as ordinary dialogue");

        fixture.WriteWindowLifecyclePhase(0, 6);
        reader.ObserveAskCursorCapture(new Steam2026AskCursorIngressSnapshot(
            2,
            new DateTime(2026, 7, 22, 23, 16, 26, DateTimeKind.Utc),
            new Steam2026AskCursorCapture(116, 0, 0, 1, 2, 1)));
        Equal(true, reader.TryRead(out var phaseRecovered), "same native ASK can recover after lifecycle wobble");
        Equal(
            first.PageRevision,
            phaseRecovered.PageRevision,
            "unchanged ASK lifecycle wobble must not replay its prompt or selected choice");

        fixture.WriteWindowLifecyclePhase(0, 7);
        Equal(false, reader.TryRead(out _), "recovered ASK retires without exposing raw choice rows");
        fixture.WriteVisibleText(0, "Flower girl Really? I don't know what's going on, but all right.");
        Equal(true, reader.TryRead(out var successor), "changed successor dialogue is readable after ASK retirement");
        Equal(
            "Flower girl Really? I don't know what's going on, but all right.",
            successor.VisibleText,
            "ASK retirement admits only the changed successor page");
        Equal(0, successor.Choices.Length, "successor dialogue has no stale ASK choices");
    }

    private static void ReadsOneOwnedVisibleWindowEquallyFromDirectAndTranslatedMemory()
    {
        var fixture = DialogueObservationFixture.CreateOpen("ABC");
        var directReader = new Steam2026FieldDialogueObservationReader(fixture.Direct);
        var translatedReader = new Steam2026FieldDialogueObservationReader(
            supportedFingerprint,
            DialogueObservationFixture.ModuleBase,
            fixture.Native);

        Equal(true, directReader.TryRead(out var direct), "direct ordinary dialogue page");
        Equal(true, translatedReader.TryRead(out var translated), "translated ordinary dialogue page");
        Equal(direct, translated, "direct and translated dialogue pages match");
        Equal(true, translated.IsOpen, "ordinary dialogue is open");
        Equal(0, translated.WindowId, "native visible window id");
        Equal(1, translated.PageRevision, "initial deterministic page revision");
        Equal(string.Empty, translated.Speaker, "speaker remains absent without native evidence");
        Equal("ABC", translated.VisibleText, "exact visible native buffer text");
        Equal(0, translated.Choices.Length, "ordinary dialogue exposes no guessed ASK choices");

        foreach (var property in typeof(DialoguePageObservation).GetProperties())
        {
            Equal(false, property.Name.Contains("Pointer", StringComparison.OrdinalIgnoreCase), $"dialogue output {property.Name} is pointer-free");
            Equal(false, property.Name.Contains("Address", StringComparison.OrdinalIgnoreCase), $"dialogue output {property.Name} is address-free");
        }
    }

    private static void ReadsChangedDialogueBesidePersistentTimer()
    {
        var fixture = DialogueObservationFixture.CreateOpen("Time 09:59");
        var reader = CreateTranslatedReader(fixture);

        Equal(true, reader.TryRead(out var timer), "persistent timer establishes one owned window");
        Equal(0, timer.WindowId, "timer window identity");

        fixture.OpenWindow(1, "Jessie My leg got stuck.");
        fixture.WriteByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 2);
        Equal(
            true,
            reader.TryRead(out var firstJessiePage),
            "a newly visible dialogue window remains readable beside the timer");
        Equal(1, firstJessiePage.WindowId, "new dialogue window owns speech");
        Equal(
            "Jessie My leg got stuck.",
            firstJessiePage.VisibleText,
            "exact first Jessie page beside timer");

        fixture.WriteVisibleText(0, "Time 09:58");
        fixture.WriteVisibleText(1, "Jessie Thanks!");
        Equal(
            true,
            reader.TryRead(out var secondJessiePage),
            "the selected dialogue window retains ownership while the timer also changes");
        Equal(1, secondJessiePage.WindowId, "dialogue ownership does not alternate back to timer");
        Equal("Jessie Thanks!", secondJessiePage.VisibleText, "exact second Jessie page beside timer");
    }

    private static void DeduplicatesPagesAndResetsRevisionOnlyOnExplicitClosure()
    {
        var fixture = DialogueObservationFixture.CreateOpen("ABC");
        var reader = new Steam2026FieldDialogueObservationReader(
            supportedFingerprint,
            DialogueObservationFixture.ModuleBase,
            fixture.Native);

        Equal(true, reader.TryRead(out var first), "initial dialogue page");
        Equal(true, reader.TryRead(out var duplicate), "duplicate dialogue page");
        Equal(1, duplicate.PageRevision, "duplicate page keeps revision");

        fixture.WriteByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 0);
        Equal(false, reader.TryRead(out _), "stale ownership is not an explicit closure");
        fixture.WriteByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 1);
        Equal(true, reader.TryRead(out var afterStaleOwnership), "page recovers after stale ownership");
        Equal(1, afterStaleOwnership.PageRevision, "incoherent failure does not reset revision");

        fixture.WriteVisibleText(0, "ABD");
        Equal(true, reader.TryRead(out var changed), "changed dialogue page");
        Equal(2, changed.PageRevision, "changed page increments revision");
        Equal(true, reader.TryRead(out var changedDuplicate), "changed page duplicate");
        Equal(2, changedDuplicate.PageRevision, "changed duplicate keeps revision");

        fixture.CloseAll();
        Equal(false, reader.TryRead(out var closed), "explicit native dialogue closure");
        Equal<DialoguePageObservation?>(null, closed, "closure returns no synthetic closed page");

        fixture.OpenWindow(0, "ABD");
        Equal(true, reader.TryRead(out var reopened), "dialogue reopened after observed close");
        Equal(1, reopened.PageRevision, "observed closure resets private revision");
        Equal(first.WindowId, reopened.WindowId, "reopen retains native window id");
    }

    private static void RejectsZeroMultipleBlankAndStaleOwnedWindows()
    {
        var zeroOwnership = DialogueObservationFixture.CreateOpen("ABC");
        zeroOwnership.WriteByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 0);
        Equal(false, CreateTranslatedReader(zeroOwnership).TryRead(out _), "visible window without active-message ownership rejected");

        var staleOwnership = DialogueObservationFixture.CreateClosed();
        staleOwnership.WriteByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 1);
        Equal(false, CreateTranslatedReader(staleOwnership).TryRead(out _), "stale active-message count without visible window rejected");

        var multiple = DialogueObservationFixture.CreateOpen("ABC");
        multiple.OpenWindow(1, "DEF");
        Equal(false, CreateTranslatedReader(multiple).TryRead(out _), "multiple active visible windows rejected");

        var blank = DialogueObservationFixture.CreateOpen("ABC");
        blank.WriteVisibleBytes(0, [0x00, 0xff]);
        Equal(false, CreateTranslatedReader(blank).TryRead(out _), "blank active window rejected");
    }

    private static void RejectsPreviewTableAndLineBufferOnlyCandidates()
    {
        var fixture = DialogueObservationFixture.CreateClosed();
        fixture.WriteVisibleText(0, "PREVIEW");
        fixture.WriteEncodedText((uint)FieldMessageReader.AddressFieldMessageLineBuffer, "LINE");
        fixture.WriteMessageTableText("TABLE");
        fixture.WriteByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 1);

        Equal(
            false,
            CreateTranslatedReader(fixture).TryRead(out _),
            "preview, message-table, and line-buffer-only text rejected");
    }

    private static void RejectsUnmappedDialogueState()
    {
        var addresses = CriticalDialogueAddresses();
        foreach (var testCase in addresses)
        {
            var fixture = DialogueObservationFixture.CreateOpen("ABC");
            fixture.UnmapGuestPage(testCase.GuestAddress);
            Equal(false, CreateTranslatedReader(fixture).TryRead(out _), $"unmapped {testCase.Label}");
        }
    }

    private static void RejectsTranslatedDialoguePageRemapping()
    {
        var addresses = CriticalDialogueAddresses();
        for (var index = 0; index < addresses.Length; index++)
        {
            var testCase = addresses[index];
            var fixture = DialogueObservationFixture.CreateOpen("ABC");
            var entryAddress = fixture.GetPageTableEntryAddress(testCase.GuestAddress);
            var remapping = new RemappingNativeMemoryReader(
                fixture.Native,
                entryAddress,
                triggerRead: 2,
                () => fixture.MapGuestPage(
                    testCase.GuestAddress,
                    0x0000000900000000 + ((ulong)index * 0x2000)));
            var reader = new Steam2026FieldDialogueObservationReader(
                supportedFingerprint,
                DialogueObservationFixture.ModuleBase,
                remapping);

            Equal(false, reader.TryRead(out _), $"remapped {testCase.Label}");
        }
    }

    private static void RejectsTornDialogueState()
    {
        var cases = new (uint GuestAddress, byte[] Replacement, int TriggerRead, string Label)[]
        {
            ((uint)FieldPositionReader.AddressCurrentModule, [2], 2, "module"),
            ((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)117), 2, "field id"),
            ((uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount, [2], 2, "active-message ownership"),
            ((uint)FieldMessageReader.AddressFieldMessageDataPointer, BitConverter.GetBytes(0x00710000u), 2, "message-data pointer"),
            ((uint)FieldMessageReader.AddressFieldWindowStates, [1], 2, "window state"),
            ((uint)FieldMessageReader.AddressFieldWindowMessagePointers, BitConverter.GetBytes(0x00700030u), 2, "window pointer"),
            ((uint)FieldMessageReader.AddressFieldWindowTextBuffers, Encode("ABD"), 3, "visible text"),
            ((uint)FieldAudibleCueStateReader.AddressUserControl, [1], 2, "cue state")
        };

        foreach (var testCase in cases)
        {
            var fixture = DialogueObservationFixture.CreateOpen("ABC");
            var tearing = new TearingNativeMemoryReader(
                fixture.Native,
                fixture.GetHostAddress(testCase.GuestAddress),
                triggerRead: testCase.TriggerRead,
                () => fixture.Write(testCase.GuestAddress, testCase.Replacement));
            var reader = new Steam2026FieldDialogueObservationReader(
                supportedFingerprint,
                DialogueObservationFixture.ModuleBase,
                tearing);

            Equal(false, reader.TryRead(out _), $"torn {testCase.Label}");
        }
    }

    private static void PublicReaderRequiresExactTranslatedResolver()
    {
        var constructors = typeof(Steam2026FieldDialogueObservationReader).GetConstructors();
        Equal(1, constructors.Length, "dialogue facade public constructor count");
        Equal(
            typeof(Steam2026FingerprintResult),
            constructors[0].GetParameters()[0].ParameterType,
            "dialogue facade public constructor requires fingerprint");

        var unsupportedFixture = DialogueObservationFixture.CreateOpen("ABC");
        var unsupportedRejected = false;
        try
        {
            _ = new Steam2026FieldDialogueObservationReader(
                unsupportedFingerprint,
                DialogueObservationFixture.ModuleBase,
                unsupportedFixture.Native);
        }
        catch (ArgumentException)
        {
            unsupportedRejected = true;
        }

        Equal(true, unsupportedRejected, "public dialogue reader rejects unsupported fingerprint");

        var fixture = DialogueObservationFixture.CreateOpen("ABC");
        fixture.Native.Write(
            DialogueObservationFixture.ModuleBase + TranslatedX86AddressSpace.ResolverRva,
            [0x90]);

        var rejected = false;
        try
        {
            _ = new Steam2026FieldDialogueObservationReader(
                supportedFingerprint,
                DialogueObservationFixture.ModuleBase,
                fixture.Native);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        Equal(true, rejected, "public dialogue reader requires exact translated resolver");
    }

    private static void KeepsDialogueReaderOrdinaryResearchOnly()
    {
        var type = typeof(Steam2026FieldDialogueObservationReader);
        Equal(false, typeof(IFf7RuntimeBackend).IsAssignableFrom(type), "dialogue reader is not a backend");
        Equal(false, typeof(IRuntimeEventSink).IsAssignableFrom(type), "dialogue reader is not an event sink");
        Equal(false, type.GetMethods().Any(method => method.Name.Contains("Hook", StringComparison.OrdinalIgnoreCase)), "dialogue reader exposes no hook API");
        Equal(false, type.GetMethods().Any(method => method.Name.Contains("Speak", StringComparison.OrdinalIgnoreCase)), "dialogue reader exposes no speech API");
    }

    private static Steam2026FieldDialogueObservationReader CreateTranslatedReader(DialogueObservationFixture fixture) =>
        new(supportedFingerprint, DialogueObservationFixture.ModuleBase, fixture.Native);

    private static (uint GuestAddress, string Label)[] CriticalDialogueAddresses() =>
    [
        ((uint)FieldPositionReader.AddressCurrentModule, "module state"),
        ((uint)FieldPositionReader.AddressFieldId, "field state"),
        ((uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount, "active-message ownership"),
        ((uint)FieldMessageReader.AddressFieldMessageDataPointer, "message-data pointer"),
        ((uint)FieldMessageReader.AddressFieldWindowStates, "window states"),
        ((uint)FieldMessageReader.AddressFieldWindowMessagePointers, "window pointers"),
        ((uint)FieldMessageReader.AddressFieldWindowTextBuffers, "visible window buffer"),
        ((uint)FieldAudibleCueStateReader.AddressUserControl, "cue state")
    ];

    private static byte[] Encode(string text) =>
        text.Select(character => checked((byte)(character - 0x20))).Append((byte)0xff).ToArray();

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void SequenceEqual<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual,
        string label)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(',', expected)}], got [{string.Join(',', actual)}].");
        }
    }

    private sealed class OneShotReadFailureAddressSpace(ILegacyAddressSpace inner)
        : ILegacyAddressSpace
    {
        private uint failedAddress;
        private bool shouldFail;

        internal void FailNextRead(uint virtualAddress)
        {
            failedAddress = virtualAddress;
            shouldFail = true;
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (shouldFail && virtualAddress == failedAddress)
            {
                shouldFail = false;
                destination.Clear();
                return false;
            }

            return inner.TryRead(virtualAddress, destination);
        }
    }
}

internal sealed class DialogueObservationFixture
{
    public const ulong ModuleBase = 0x0000000140000000;
    public const uint MessageDataPointer = 0x00700000;

    private readonly Dictionary<uint, ulong> hostPages = [];
    private ulong nextHostPage = 0x0000000800000000;

    private DialogueObservationFixture()
    {
        Direct = new DirectGuestMemory();
        Native = new FakeNativeMemoryReader();
        Native.Write(
            ModuleBase + TranslatedX86AddressSpace.ResolverRva,
            Convert.FromHexString(
                "85C9750333C0C38BC1488D15609F6F018BC948C1E90C488B14CA4885D2740925FF0F00004803C2C3488D05319C6F01C3"));
        WriteByte(FieldPositionReader.AddressCurrentModule, FieldPositionReader.FieldModule);
        Write((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)116));
        Write((uint)FieldMessageReader.AddressFieldMessageDataPointer, BitConverter.GetBytes(MessageDataPointer));
        Write((uint)FieldMessageReader.AddressFieldWindowStates, [0xff, 0xff, 0xff, 0xff]);
        for (var index = 0; index < FieldMessageReader.WindowCount; index++)
        {
            Write(
                (uint)(FieldMessageReader.AddressFieldWindowMessagePointers + index * sizeof(uint)),
                BitConverter.GetBytes(0u));
        }

        WriteByte(FieldAudibleCueStateReader.AddressUserControl, 0);
        WriteByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 0);
        Write(
            (uint)FieldAudibleCueStateReader.AddressFieldMovieActive,
            BitConverter.GetBytes((ushort)0));
    }

    public DirectGuestMemory Direct { get; }

    public FakeNativeMemoryReader Native { get; }

    public static DialogueObservationFixture CreateOpen(string text)
    {
        var fixture = new DialogueObservationFixture();
        fixture.OpenWindow(0, text);
        return fixture;
    }

    public static DialogueObservationFixture CreateClosed() => new();

    public void OpenWindow(int windowId, string text)
    {
        Write(
            (uint)FieldMessageReader.AddressFieldMessageDataPointer,
            BitConverter.GetBytes(MessageDataPointer));
        WriteByte(FieldMessageReader.AddressFieldWindowStates + windowId, 0);
        Write(
            (uint)(FieldMessageReader.AddressFieldWindowMessagePointers + windowId * sizeof(uint)),
            BitConverter.GetBytes(MessageDataPointer + 0x20u + ((uint)windowId * 0x20u)));
        WriteVisibleText(windowId, text);
        WriteByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 1);
    }

    public void CloseAll()
    {
        Write(
            (uint)FieldMessageReader.AddressFieldMessageDataPointer,
            BitConverter.GetBytes(0u));
        Write((uint)FieldMessageReader.AddressFieldWindowStates, [0xff, 0xff, 0xff, 0xff]);
        for (var index = 0; index < FieldMessageReader.WindowCount; index++)
        {
            Write(
                (uint)(FieldMessageReader.AddressFieldWindowMessagePointers + index * sizeof(uint)),
                BitConverter.GetBytes(0u));
        }

        WriteByte(FieldAudibleCueStateReader.AddressActiveFieldMessageCount, 0);
    }

    public void WriteVisibleText(int windowId, string text) =>
        WriteVisibleBytes(windowId, Encode(text));

    public void WriteWindowLifecyclePhase(int windowId, ushort phase) =>
        Write(
            0x00CFF5E4u + ((uint)windowId * 0x30u),
            BitConverter.GetBytes(phase));

    public void WriteVisibleBytes(int windowId, IReadOnlyList<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Count > FieldMessageReader.FieldTextBufferLength)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        // A real active FFVII window owns a readable fixed-size native buffer.
        // Populate the entire range so a fixture cannot accidentally depend on
        // the removed byte-at-a-time fallback after a failed bounded read.
        var buffer = new byte[FieldMessageReader.FieldTextBufferLength];
        for (var index = 0; index < bytes.Count; index++)
        {
            buffer[index] = bytes[index];
        }

        Write(
            (uint)(FieldMessageReader.AddressFieldWindowTextBuffers + windowId * FieldMessageReader.WindowTextBufferStride),
            buffer);
    }

    public void WriteEncodedText(uint address, string text) => Write(address, Encode(text));

    public void WriteMessageTableText(string text)
    {
        WriteMessageTableBytes(Encode(text));
    }

    public void WriteMessageTableBytes(IReadOnlyList<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        const int textOffset = 0x10;
        var pageRemainder = 0x1000 - (int)((MessageDataPointer + textOffset) & 0xfff);
        if (bytes.Count > pageRemainder)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        Write(MessageDataPointer, BitConverter.GetBytes((ushort)1));
        Write(MessageDataPointer + 2, BitConverter.GetBytes((ushort)textOffset));
        var mappedTextPage = new byte[pageRemainder];
        for (var index = 0; index < bytes.Count; index++)
        {
            mappedTextPage[index] = bytes[index];
        }

        // The checked reader performs a bounded page read before locating the
        // terminator, matching real committed game memory rather than a sparse
        // byte dictionary.
        Write(MessageDataPointer + textOffset, mappedTextPage);
    }

    public void WriteMessageTableTexts(params string[] texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(texts));
        }

        var pointerTableLength = sizeof(ushort) + texts.Length * sizeof(ushort);
        var encoded = texts.Select(Encode).ToArray();
        var totalLength = pointerTableLength + encoded.Sum(bytes => bytes.Length);
        var table = new byte[totalLength];
        BitConverter.GetBytes(checked((ushort)texts.Length)).CopyTo(table, 0);
        var textOffset = pointerTableLength;
        for (var index = 0; index < encoded.Length; index++)
        {
            BitConverter.GetBytes(checked((ushort)textOffset))
                .CopyTo(table, sizeof(ushort) + index * sizeof(ushort));
            encoded[index].CopyTo(table, textOffset);
            textOffset += encoded[index].Length;
        }

        var pageRemainder = 0x1000 - (int)(MessageDataPointer & 0xfff);
        if (table.Length > pageRemainder)
        {
            throw new ArgumentOutOfRangeException(nameof(texts));
        }

        var mappedPage = new byte[pageRemainder];
        table.CopyTo(mappedPage, 0);
        Write(MessageDataPointer, mappedPage);
    }

    public void WriteByte(int address, byte value) => Write((uint)address, [value]);

    public void Write(uint address, IReadOnlyList<byte> values)
    {
        Direct.Write(address, values);
        for (var index = 0; index < values.Count; index++)
        {
            var guestAddress = checked(address + (uint)index);
            Native.Write(GetOrMapHostAddress(guestAddress), [values[index]]);
        }
    }

    public ulong GetHostAddress(uint guestAddress)
    {
        if (!hostPages.TryGetValue(guestAddress >> 12, out var hostPage))
        {
            throw new InvalidOperationException($"Guest address 0x{guestAddress:X8} is not mapped.");
        }

        return hostPage + (guestAddress & 0xfff);
    }

    public ulong GetPageTableEntryAddress(uint guestAddress) =>
        ModuleBase + TranslatedX86AddressSpace.PageTableRva + ((guestAddress >> 12) * sizeof(ulong));

    public void MapGuestPage(uint guestAddress, ulong hostPage)
    {
        hostPages[guestAddress >> 12] = hostPage;
        Native.MapVirtualPage(ModuleBase, guestAddress >> 12, hostPage);
    }

    public void UnmapGuestPage(uint guestAddress) => MapGuestPage(guestAddress, 0);

    private ulong GetOrMapHostAddress(uint guestAddress)
    {
        var pageIndex = guestAddress >> 12;
        if (!hostPages.TryGetValue(pageIndex, out var hostPage))
        {
            hostPage = nextHostPage;
            nextHostPage += 0x3000;
            MapGuestPage(guestAddress, hostPage);
        }

        return hostPage + (guestAddress & 0xfff);
    }

    private static byte[] Encode(string text) =>
        text.Select(character => checked((byte)(character - 0x20))).Append((byte)0xff).ToArray();
}

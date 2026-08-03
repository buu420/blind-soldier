using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

internal static class FieldDialogueScriptLayoutTests
{
    public static void Run()
    {
        AssertReadersLiveInSharedAssembly();
        AssertDialogReaderUsesCheckedGuestPointers();
        AssertMessageReaderUsesOnlyActiveVisibleBuffers();
        AssertMessageReaderRejectsPartialAndTornFrames();
        AssertMessageReaderRejectsActiveBulkReadFailure();
        AssertMessageIdReaderBoundsHighGuestText();
        AssertMessageIdReaderPreservesAskPagesAndChoiceIndent();
        AssertMessageIdReaderRejectsInvalidMessageTables();
        AssertMessageIdReaderRejectsTornMessageTables();
        AssertScriptContextReaderChecksNestedState();
        AssertScriptLineReaderChecksMappingAndBookends();
        AssertOpcodeParameterReaderChecksOneCoherentFrame();
    }

    private static void AssertReadersLiveInSharedAssembly()
    {
        var expected = typeof(ILegacyAddressSpace).Assembly;
        Type[] types =
        [
            typeof(FieldDialogStringReader),
            typeof(FieldMessageReader),
            typeof(FieldVisibleWindowSnapshot),
            typeof(FieldMessageCandidate),
            typeof(FieldMessageDiagnostics),
            typeof(FieldMessageWindowDiagnostic),
            typeof(FieldScriptContextReader),
            typeof(FieldScriptContext),
            typeof(FieldScriptLineStateReader),
            typeof(FieldOpcodeParameterReader),
            typeof(FieldOpcodeMessageObservation),
            typeof(FieldOpcodeKind)
        ];

        foreach (var type in types)
        {
            Equal(expected, type.Assembly, $"shared dialogue/script type {type.Name}");
        }

        Equal(typeof(uint), typeof(FieldVisibleWindowSnapshot).GetProperty(nameof(FieldVisibleWindowSnapshot.GuestPointer))!.PropertyType, "visible window guest pointer width");
        Equal(typeof(uint), typeof(FieldMessageDiagnostics).GetProperty(nameof(FieldMessageDiagnostics.MessageDataPointer))!.PropertyType, "diagnostic message pointer width");
        Equal(typeof(uint), typeof(FieldMessageWindowDiagnostic).GetProperty(nameof(FieldMessageWindowDiagnostic.Pointer))!.PropertyType, "diagnostic window pointer width");
        Equal(typeof(uint), typeof(FieldMessageWindowDiagnostic).GetProperty(nameof(FieldMessageWindowDiagnostic.BufferAddress))!.PropertyType, "diagnostic buffer pointer width");
    }

    private static void AssertDialogReaderUsesCheckedGuestPointers()
    {
        const uint text = 0x80000FFF;
        var denseText = new byte[FieldMessageReader.FieldTextBufferLength];
        denseText[0] = 0x21;
        denseText[1] = 0x22;
        denseText[2] = 0xff;
        var memory = OwnedDialogMemory(0x80000000u, text, denseText);

        var reader = new FieldDialogStringReader(memory);
        Equal(true, reader.TryReadCurrent(out var candidate), "checked dialog read");
        Equal("AB", candidate.Text, "cross-page high guest dialog text");

        const uint lowText = 0x3000;
        var low = OwnedDialogMemory(0x2000, lowText, [0x21, 0xff]);
        Equal(true, new FieldDialogStringReader(low).TryReadCurrent(out candidate), "mapped low guest dialog pointer");
        Equal("A", candidate.Text, "mapped low guest dialog text");

        var empty = OwnedDialogMemory(0x2000, lowText, [0xff]);
        Equal(false, new FieldDialogStringReader(empty).TryReadCurrent(out _), "terminator-only dialog is absent");

        var counted = new CountingLegacyAddressSpace(memory);
        Equal(true, new FieldDialogStringReader(counted).TryReadCurrent(out _), "batched dialog read");
        Equal(true, counted.ReadCount <= 16, "dialog text uses bounded batch reads");

        const uint mutableText = 0x5000;
        var firstText = new byte[FieldMessageReader.FieldTextBufferLength];
        firstText[0] = 0x21;
        firstText[1] = 0xff;
        var mutable = OwnedDialogMemory(0x4000, mutableText, firstText);
        var replacementText = new byte[FieldMessageReader.FieldTextBufferLength];
        replacementText[0] = 0x22;
        replacementText[1] = 0xff;
        var contentTear = new TearingLegacyAddressSpace(
            mutable,
            (uint)FieldMessageReader.AddressFieldWindowTextBuffers,
            replacementText);
        Equal(false, new FieldDialogStringReader(contentTear).TryReadCurrent(out _), "stable pointer with changing dialog bytes fails");

        var partial = OwnedDialogMemory(0x80000000u, text, [0x21, 0x22], mapFullBuffer: false);
        Equal(false, new FieldDialogStringReader(partial).TryReadCurrent(out _), "unterminated partial dialog fails");

        var partiallyReadableOverlap = MessageMemory();
        U32(partiallyReadableOverlap, FieldMessageReader.AddressFieldMessageDataPointer, 0x2000);
        partiallyReadableOverlap.Write(
            (uint)FieldMessageReader.AddressFieldWindowStates,
            [0, 1, FieldMessageReader.FreeWindowState, FieldMessageReader.FreeWindowState]);
        U32(partiallyReadableOverlap, FieldMessageReader.AddressFieldWindowMessagePointers, 0x3000);
        U32(partiallyReadableOverlap, FieldMessageReader.AddressFieldWindowMessagePointers + sizeof(uint), 0x3100);
        WriteWindowText(partiallyReadableOverlap, 0, [0x21, 0xff]);
        partiallyReadableOverlap.Write(
            (uint)(FieldMessageReader.AddressFieldWindowTextBuffers + FieldMessageReader.WindowTextBufferStride),
            [0x22, 0x23]);
        Equal(
            false,
            new FieldDialogStringReader(partiallyReadableOverlap).TryReadCurrent(out var rejectedOverlap),
            "one unreadable active dialog invalidates all overlapping dialog output");
        Equal(string.Empty, rejectedOverlap.Text, "failed overlapping dialog observation clears candidate text");

        var torn = new TearingLegacyAddressSpace(
            memory,
            (uint)FieldDialogStringReader.AddressCurrentDialogStringPointer,
            BitConverter.GetBytes(0x80002000u));
        Equal(false, new FieldDialogStringReader(torn).TryReadCurrent(out _), "dialog pointer remap fails");

        var fieldTorn = new TearingLegacyAddressSpace(
            memory,
            (uint)FieldPositionReader.AddressFieldId,
            BitConverter.GetBytes((ushort)117));
        Equal(false, new FieldDialogStringReader(fieldTorn).TryReadCurrent(out _), "dialog field tear fails");
    }

    private static void AssertMessageReaderUsesOnlyActiveVisibleBuffers()
    {
        var memory = MessageMemory();
        memory.Write((uint)FieldMessageReader.AddressFieldWindowStates, [0, 1, 0xff, 0xff]);
        WriteWindowText(memory, 0, [0x21, 0xff]);
        WriteWindowText(memory, 1, [0x21, 0x22, 0x23, 0xff]);
        memory.Write((uint)(FieldMessageReader.AddressFieldWindowTextBuffers + 2 * FieldMessageReader.WindowTextBufferStride), [0x24, 0x25, 0x26, 0x27, 0xff]);
        memory.Write((uint)FieldMessageReader.AddressFieldMessageLineBuffer, [0x28, 0x29, 0xff]);

        var reader = new FieldMessageReader(memory);
        Equal(true, reader.TryReadVisibleWindows(out var windows), "active visible message read");
        Equal(2, windows.Count, "overlapping active windows remain distinct");
        Equal(0, windows[0].WindowId, "first active window keeps native order");
        Equal("A", windows[0].Text, "first active window visible text");
        Equal(1, windows[1].WindowId, "second active window keeps native order");
        Equal("ABC", windows[1].Text, "second active window visible text");
        Equal(true, reader.TryHasReadableActiveWindow(out var readable), "active window state read");
        Equal(true, readable, "active visible window readable");

        memory.Write((uint)FieldMessageReader.AddressFieldWindowStates, [0xff, 0xff, 0xff, 0xff]);
        Equal(true, reader.TryReadVisibleWindows(out windows), "closed window frame is readable");
        Equal(0, windows.Count, "closed and transient buffers are not candidates");
    }

    private static void AssertMessageReaderRejectsPartialAndTornFrames()
    {
        var partial = MessageMemory();
        partial.Write((uint)FieldMessageReader.AddressFieldWindowStates, [0, 0xff, 0xff, 0xff]);
        partial.Write((uint)FieldMessageReader.AddressFieldWindowTextBuffers, [0x21, 0x22]);
        Equal(false, new FieldMessageReader(partial).TryReadVisibleWindows(out var partialWindows), "unterminated active buffer invalidates the frame");
        Equal(0, partialWindows.Count, "failed active frame clears all visible output");

        var valid = MessageMemory();
        valid.Write((uint)FieldMessageReader.AddressFieldWindowStates, [0, 0xff, 0xff, 0xff]);
        WriteWindowText(valid, 0, [0x21, 0xff]);
        var stateTear = new TearingLegacyAddressSpace(
            valid,
            (uint)FieldMessageReader.AddressFieldWindowStates,
            [1]);
        Equal(false, new FieldMessageReader(stateTear).TryReadVisibleWindows(out _), "window state tear fails");

        var pointerTear = new TearingLegacyAddressSpace(
            valid,
            (uint)FieldMessageReader.AddressFieldWindowMessagePointers,
            BitConverter.GetBytes(0x700100u));
        Equal(false, new FieldMessageReader(pointerTear).TryReadVisibleWindows(out _), "window pointer tear fails");

        var mutable = MessageMemory();
        mutable.Write((uint)FieldMessageReader.AddressFieldWindowStates, [0, 0xff, 0xff, 0xff]);
        var firstText = new byte[FieldMessageReader.FieldTextBufferLength];
        firstText[0] = 0x21;
        firstText[1] = 0xff;
        mutable.Write((uint)FieldMessageReader.AddressFieldWindowTextBuffers, firstText);
        var replacementText = new byte[FieldMessageReader.FieldTextBufferLength];
        replacementText[0] = 0x22;
        replacementText[1] = 0xff;
        var contentTear = new TearingLegacyAddressSpace(
            mutable,
            (uint)FieldMessageReader.AddressFieldWindowTextBuffers,
            replacementText);
        Equal(false, new FieldMessageReader(contentTear).TryReadVisibleWindows(out _), "stable window pointer with changing visible bytes fails");
    }

    private static void AssertMessageReaderRejectsActiveBulkReadFailure()
    {
        var memory = MessageMemory();
        memory.Write((uint)FieldMessageReader.AddressFieldWindowStates, [0, 1, 0xff, 0xff]);
        WriteWindowText(memory, 0, [0x21, 0xff]);
        WriteWindowText(memory, 1, [0x22, 0xff]);

        var bulkFailure = new RejectBulkReadAtAddress(
            memory,
            (uint)FieldMessageReader.AddressFieldWindowTextBuffers);
        var result = new FieldMessageReader(bulkFailure).TryReadVisibleWindows(out var windows);

        Equal(false, result, "a failed full read for one active window invalidates the observation");
        Equal(0, windows.Count, "an invalid active window cannot expose another window as a partial observation");
    }

    private static void AssertMessageIdReaderBoundsHighGuestText()
    {
        const uint data = 0x80000FF0;
        var memory = MessageMemory();
        U32(memory, FieldMessageReader.AddressFieldMessageDataPointer, data);
        U16(memory, data, 1);
        U16(memory, data + 2, 0x000f);
        memory.Write(data + 0x0f, [0x21]);
        var nextPage = new byte[0x1000];
        Array.Fill(nextPage, (byte)0xff);
        nextPage[0] = 0x22;
        memory.Write(data + 0x10, nextPage);

        var reader = new FieldMessageReader(memory);
        Equal(true, reader.TryReadMessageById(0, out var candidate), "high cross-page message by id");
        Equal("AB", candidate.Text, "bounded message by id text");
        nextPage[0] = 0xe8;
        nextPage[1] = 0x22;
        memory.Write(data + 0x10, nextPage);
        Equal(true, reader.TryReadMessageLinesById(0, out var lines), "cross-page ASK lines by id");
        Equal(2, lines.Count, "cross-page ASK line count");
        Equal("A", lines[0], "first ASK line");
        Equal("B", lines[1], "second ASK line");

        var noTerminator = MessageMemory();
        U32(noTerminator, FieldMessageReader.AddressFieldMessageDataPointer, data);
        U16(noTerminator, data, 1);
        U16(noTerminator, data + 2, 0xfffe);
        noTerminator.Write(data + 0xfffe, [0x21, 0x22]);
        Equal(false, new FieldMessageReader(noTerminator).TryReadMessageById(0, out _), "message text cannot cross resource end");

        var wrapped = MessageMemory();
        U32(wrapped, FieldMessageReader.AddressFieldMessageDataPointer, 0xfffffff0u);
        U16(wrapped, 0xfffffff0u, 1);
        U16(wrapped, 0xfffffff2u, 0x20);
        Equal(false, new FieldMessageReader(wrapped).TryReadMessageById(0, out _), "message resource arithmetic cannot wrap");

        var empty = MessageMemory();
        U32(empty, FieldMessageReader.AddressFieldMessageDataPointer, data);
        U16(empty, data, 1);
        U16(empty, data + 2, 0x20);
        var emptyText = new byte[0xfe0];
        Array.Fill(emptyText, (byte)0xff);
        empty.Write(data + 0x20, emptyText);
        Equal(false, new FieldMessageReader(empty).TryReadMessageById(0, out _), "terminator-only message is absent");
    }

    private static void AssertMessageIdReaderPreservesAskPagesAndChoiceIndent()
    {
        const uint data = 0x705000;
        var memory = MessageMemory();
        U32(memory, FieldMessageReader.AddressFieldMessageDataPointer, data);
        U16(memory, data, 1);
        U16(memory, data + 2, 0x10);
        var encoded = new byte[0xff0];
        Array.Fill(encoded, (byte)0xff);
        byte[] content =
        [
            0x21,       // A
            0xe8,       // next page
            0x22,       // B
            0xe7,       // next line
            0xe0, 0x23, // indented choice C
            0xff
        ];
        content.CopyTo(encoded, 0);
        memory.Write(data + 0x10, encoded);

        var reader = new FieldMessageReader(memory);
        Equal(true, reader.TryReadMessagePagesById(0, out var pages), "checked ASK pages by id");
        Equal(2, pages.Count, "ASK page count");
        Equal("A", pages[0].Lines[0].Text, "first ASK page text");
        Equal(false, pages[0].Lines[0].IsChoice, "ordinary page line is not a choice");
        Equal("B", pages[1].Lines[0].Text, "second ASK page prompt");
        Equal(false, pages[1].Lines[0].IsChoice, "ASK prompt is not a choice");
        Equal("C", pages[1].Lines[1].Text, "second ASK page choice");
        Equal(true, pages[1].Lines[1].IsChoice, "native choice indent is preserved");
    }

    private static void AssertMessageIdReaderRejectsInvalidMessageTables()
    {
        const uint data = 0x710000;

        var zeroCount = MessageMemory();
        U32(zeroCount, FieldMessageReader.AddressFieldMessageDataPointer, data);
        U16(zeroCount, data, 0);
        U16(zeroCount, data + 2, 0x10);
        zeroCount.Write(data + 0x10, [0x21, 0xff]);
        Equal(false, new FieldMessageReader(zeroCount).TryReadMessageById(0, out _), "zero-count message table fails");
        Equal(false, new FieldMessageReader(zeroCount).TryReadMessageLinesById(0, out _), "zero-count ASK message table fails");

        var pastCount = MessageMemory();
        U32(pastCount, FieldMessageReader.AddressFieldMessageDataPointer, data);
        U16(pastCount, data, 1);
        U16(pastCount, data + 4, 0x10);
        pastCount.Write(data + 0x10, [0x21, 0xff]);
        Equal(false, new FieldMessageReader(pastCount).TryReadMessageById(1, out _), "message id at native count fails");

        var oversizedTable = MessageMemory();
        U32(oversizedTable, FieldMessageReader.AddressFieldMessageDataPointer, data);
        U16(oversizedTable, data, ushort.MaxValue);
        U16(oversizedTable, data + 2, 0x10);
        oversizedTable.Write(data + 0x10, [0x21, 0xff]);
        Equal(false, new FieldMessageReader(oversizedTable).TryReadMessageById(0, out _), "message offset table cannot exceed resource bounds");

        var metadataAlias = MessageMemory();
        U32(metadataAlias, FieldMessageReader.AddressFieldMessageDataPointer, data);
        U16(metadataAlias, data, 2);
        U16(metadataAlias, data + 2, 4);
        metadataAlias.Write(data + 4, [0x21, 0xff]);
        Equal(false, new FieldMessageReader(metadataAlias).TryReadMessageById(0, out _), "message text cannot alias offset-table metadata");

        var unmappedCount = MessageMemory();
        U32(unmappedCount, FieldMessageReader.AddressFieldMessageDataPointer, data);
        U16(unmappedCount, data + 2, 0x10);
        unmappedCount.Write(data + 0x10, [0x21, 0xff]);
        Equal(false, new FieldMessageReader(unmappedCount).TryReadMessageById(0, out _), "unmapped message count fails");

        var unmappedEntry = MessageMemory();
        U32(unmappedEntry, FieldMessageReader.AddressFieldMessageDataPointer, data);
        U16(unmappedEntry, data, 1);
        Equal(false, new FieldMessageReader(unmappedEntry).TryReadMessageById(0, out _), "unmapped selected message offset fails");

        var unmappedText = MessageMemory();
        U32(unmappedText, FieldMessageReader.AddressFieldMessageDataPointer, data);
        U16(unmappedText, data, 1);
        U16(unmappedText, data + 2, 0x10);
        Equal(false, new FieldMessageReader(unmappedText).TryReadMessageById(0, out _), "unmapped message text fails");
    }

    private static void AssertMessageIdReaderRejectsTornMessageTables()
    {
        const uint data = 0x720000;
        var memory = MessageMemory();
        U32(memory, FieldMessageReader.AddressFieldMessageDataPointer, data);
        U16(memory, data, 1);
        U16(memory, data + 2, 0x10);
        var firstText = new byte[0xff0];
        firstText[0] = 0x21;
        firstText[1] = 0xff;
        memory.Write(data + 0x10, firstText);
        memory.Write(data + 0x20, [0x22, 0xff]);

        var countTear = new TearingLegacyAddressSpace(
            memory,
            data,
            BitConverter.GetBytes((ushort)2));
        Equal(false, new FieldMessageReader(countTear).TryReadMessageById(0, out _), "message count tear fails");

        var offsetTear = new TearingLegacyAddressSpace(
            memory,
            data + 2,
            BitConverter.GetBytes((ushort)0x20));
        Equal(false, new FieldMessageReader(offsetTear).TryReadMessageById(0, out _), "selected message offset remap fails");

        var replacementText = new byte[firstText.Length];
        replacementText[0] = 0x22;
        replacementText[1] = 0xff;
        var textTear = new TearingLegacyAddressSpace(
            memory,
            data + 0x10,
            replacementText);
        Equal(false, new FieldMessageReader(textTear).TryReadMessageById(0, out _), "message text tear fails");

        var baseRemap = new TearingLegacyAddressSpace(
            memory,
            (uint)FieldMessageReader.AddressFieldMessageDataPointer,
            BitConverter.GetBytes(0x730000u));
        Equal(false, new FieldMessageReader(baseRemap).TryReadMessageById(0, out _), "message table base remap fails");
    }

    private static void AssertScriptContextReaderChecksNestedState()
    {
        const uint script = 0x80002000;
        var memory = FieldMemory();
        U32(memory, FieldScriptContextReader.AddressFieldScriptPtr, script);
        memory.Write(script + 2, [2]);
        U16(memory, script + 6, 4);
        memory.Write((uint)FieldScriptContextReader.AddressCurrentEntityId, [1]);
        memory.Write((uint)(FieldScriptContextReader.AddressCurrentEntityScriptPriority + 1), [2]);
        memory.Write((uint)(FieldScriptContextReader.AddressCurrentEntityScriptId + 10), [3]);
        U16(memory, FieldScriptContextReader.AddressFieldCurrScriptPosition + 2, 0x90);
        var table = script + 16 + FieldScriptContextReader.ScriptOffsetTableHeaderSize + 16 + FieldScriptContextReader.ScriptOffsetEntityStride;
        U16(memory, table + 6, 0x80);
        memory.Write(script + 0x90, [0x40]);

        var reader = new FieldScriptContextReader(memory);
        Equal(true, reader.TryRead(out var context), "checked script context");
        Equal(0x10, context.ByteIndex, "script-relative byte index");
        Equal(0x40, context.Opcode, "script opcode");

        var zeroCount = FieldMemory();
        U32(zeroCount, FieldScriptContextReader.AddressFieldScriptPtr, script);
        zeroCount.Write(script + 2, [0]);
        Equal(false, new FieldScriptContextReader(zeroCount).TryRead(out _), "zero entity count fails");

        var remapped = new TearingLegacyAddressSpace(
            memory,
            (uint)FieldScriptContextReader.AddressFieldScriptPtr,
            BitConverter.GetBytes(0x80003000u));
        Equal(false, new FieldScriptContextReader(remapped).TryRead(out _), "script pointer remap fails");

        var opcodeTear = new TearingLegacyAddressSpace(memory, script + 0x90, [0x41]);
        Equal(false, new FieldScriptContextReader(opcodeTear).TryRead(out _), "stable context with changing opcode fails");

        var overflow = FieldMemory();
        U32(overflow, FieldScriptContextReader.AddressFieldScriptPtr, 0xfffffff0u);
        Equal(false, new FieldScriptContextReader(overflow).TryRead(out _), "script pointer arithmetic cannot wrap");
    }

    private static void AssertScriptLineReaderChecksMappingAndBookends()
    {
        var memory = FieldMemory();
        memory.Write((uint)(FieldScriptLineStateReader.AddressFieldLineIndexByEntity + 2), [3]);
        memory.Write((uint)(FieldScriptLineStateReader.AddressFieldLineStates + 3 * FieldScriptLineStateReader.LineStateStride), [1]);
        var reader = new FieldScriptLineStateReader(memory);
        Equal(true, reader.TryRead(2, out var enabled), "checked line-state read");
        Equal(true, enabled, "line enabled");

        Equal(false, new FieldScriptLineStateReader(FieldMemory()).TryRead(2, out _), "unmapped line mapping fails");
        var mappingTear = new TearingLegacyAddressSpace(
            memory,
            (uint)(FieldScriptLineStateReader.AddressFieldLineIndexByEntity + 2),
            [4]);
        Equal(false, new FieldScriptLineStateReader(mappingTear).TryRead(2, out _), "line mapping tear fails");
    }

    private static void AssertOpcodeParameterReaderChecksOneCoherentFrame()
    {
        const uint script = 0x80004000;
        var memory = FieldMemory();
        U32(memory, FieldOpcodeParameterReader.AddressFieldScriptPtr, script);
        memory.Write(script + 2, [4]);
        memory.Write((uint)FieldOpcodeParameterReader.AddressCurrentEntityId, [2]);
        U16(memory, FieldOpcodeParameterReader.AddressFieldCurrScriptPosition + 4, 0x20);
        memory.Write(script + 0x20, [FieldOpcodeParameterReader.MessageOpcode, 1, 7]);
        memory.Write((uint)(FieldMessageReader.AddressFieldWindowStates + 1), [2]);

        var reader = new FieldOpcodeParameterReader(memory);
        Equal(true, reader.TryReadMessage(out var message), "checked MESSAGE parameters");
        Equal(1, message.WindowId, "MESSAGE window");
        Equal(7, message.DialogId, "MESSAGE id");

        var askMemory = FieldMemory();
        U32(askMemory, FieldOpcodeParameterReader.AddressFieldScriptPtr, script);
        askMemory.Write(script + 2, [4]);
        askMemory.Write((uint)FieldOpcodeParameterReader.AddressCurrentEntityId, [2]);
        U16(askMemory, FieldOpcodeParameterReader.AddressFieldCurrScriptPosition + 4, 0x20);
        askMemory.Write(script + 0x20, [FieldOpcodeParameterReader.AskOpcode, 0, 2, 8, 1, 3, 6]);
        askMemory.Write((uint)(FieldMessageReader.AddressFieldWindowStates + 2), [2]);
        Equal(true, new FieldOpcodeParameterReader(askMemory).TryReadAsk(out var ask), "checked ASK parameters");
        Equal(2, ask.WindowId, "ASK window");
        Equal(8, ask.DialogId, "ASK id");
        Equal(1, ask.FirstQuestionLine, "ASK first line");
        Equal(3, ask.LastQuestionLine, "ASK last line");

        var partial = FieldMemory();
        U32(partial, FieldOpcodeParameterReader.AddressFieldScriptPtr, script);
        partial.Write(script + 2, [4]);
        partial.Write((uint)FieldOpcodeParameterReader.AddressCurrentEntityId, [2]);
        U16(partial, FieldOpcodeParameterReader.AddressFieldCurrScriptPosition + 4, 0x20);
        partial.Write(script + 0x20, [FieldOpcodeParameterReader.MessageOpcode, 1]);
        partial.Write((uint)(FieldMessageReader.AddressFieldWindowStates + 1), [2]);
        Equal(false, new FieldOpcodeParameterReader(partial).TryReadMessage(out _), "partial MESSAGE parameters fail");

        var positionTear = new TearingLegacyAddressSpace(
            memory,
            (uint)(FieldOpcodeParameterReader.AddressFieldCurrScriptPosition + 4),
            BitConverter.GetBytes((ushort)0x21));
        Equal(false, new FieldOpcodeParameterReader(positionTear).TryReadMessage(out _), "opcode position tear fails");

        var parameterTear = new TearingLegacyAddressSpace(
            memory,
            script + 0x20,
            [FieldOpcodeParameterReader.MessageOpcode, 2, 8]);
        Equal(false, new FieldOpcodeParameterReader(parameterTear).TryReadMessage(out _), "stable opcode context with changing parameters fails");

        var overflow = FieldMemory();
        U32(overflow, FieldOpcodeParameterReader.AddressFieldScriptPtr, 0xffffffffu);
        overflow.Write((uint)FieldOpcodeParameterReader.AddressCurrentEntityId, [0]);
        U16(overflow, FieldOpcodeParameterReader.AddressFieldCurrScriptPosition, 0);
        Equal(false, new FieldOpcodeParameterReader(overflow).TryReadMessage(out _), "opcode parameter arithmetic cannot wrap");
    }

    private static ContiguousLegacyAddressSpace FieldMemory()
    {
        var memory = new ContiguousLegacyAddressSpace();
        memory.Write((uint)FieldPositionReader.AddressCurrentModule, [FieldPositionReader.FieldModule]);
        U16(memory, FieldPositionReader.AddressFieldId, 116);
        return memory;
    }

    private static ContiguousLegacyAddressSpace MessageMemory()
    {
        var memory = FieldMemory();
        U32(memory, FieldMessageReader.AddressFieldMessageDataPointer, 0x700000);
        memory.Write((uint)FieldMessageReader.AddressFieldWindowStates, [0xff, 0xff, 0xff, 0xff]);
        for (var index = 0; index < FieldMessageReader.WindowCount; index++)
        {
            U32(memory, FieldMessageReader.AddressFieldWindowMessagePointers + index * sizeof(uint), 0);
        }

        return memory;
    }

    private static ContiguousLegacyAddressSpace OwnedDialogMemory(
        uint messageDataPointer,
        uint currentPointer,
        byte[] visibleText,
        bool mapFullBuffer = true)
    {
        var memory = MessageMemory();
        U32(memory, FieldMessageReader.AddressFieldMessageDataPointer, messageDataPointer);
        memory.Write(
            (uint)FieldMessageReader.AddressFieldWindowStates,
            [0, FieldMessageReader.FreeWindowState, FieldMessageReader.FreeWindowState, FieldMessageReader.FreeWindowState]);
        U32(memory, FieldMessageReader.AddressFieldWindowMessagePointers, currentPointer);
        if (mapFullBuffer)
        {
            WriteWindowText(memory, 0, visibleText);
        }
        else
        {
            memory.Write((uint)FieldMessageReader.AddressFieldWindowTextBuffers, visibleText);
        }
        return memory;
    }

    private static void WriteWindowText(
        ContiguousLegacyAddressSpace memory,
        int windowId,
        IReadOnlyList<byte> encodedText)
    {
        var buffer = new byte[FieldMessageReader.FieldTextBufferLength];
        Array.Fill(buffer, (byte)0xff);
        for (var index = 0; index < encodedText.Count; index++)
        {
            buffer[index] = encodedText[index];
        }

        memory.Write(
            (uint)(FieldMessageReader.AddressFieldWindowTextBuffers +
                windowId * FieldMessageReader.WindowTextBufferStride),
            buffer);
    }

    private static void U16(ContiguousLegacyAddressSpace memory, int address, ushort value) =>
        memory.Write((uint)address, BitConverter.GetBytes(value));

    private static void U16(ContiguousLegacyAddressSpace memory, uint address, ushort value) =>
        memory.Write(address, BitConverter.GetBytes(value));

    private static void U32(ContiguousLegacyAddressSpace memory, int address, uint value) =>
        memory.Write((uint)address, BitConverter.GetBytes(value));

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private sealed class CountingLegacyAddressSpace(ILegacyAddressSpace inner) : ILegacyAddressSpace
    {
        public int ReadCount { get; private set; }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            ReadCount++;
            return inner.TryRead(virtualAddress, destination);
        }
    }

    private sealed class RejectBulkReadAtAddress(
        ILegacyAddressSpace inner,
        uint rejectedAddress) : ILegacyAddressSpace
    {
        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (virtualAddress == rejectedAddress && destination.Length > 1)
            {
                destination.Clear();
                return false;
            }

            return inner.TryRead(virtualAddress, destination);
        }
    }
}

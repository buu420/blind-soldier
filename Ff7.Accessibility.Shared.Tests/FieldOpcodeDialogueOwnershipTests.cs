using Ff7.Accessibility.Reloaded;

internal static class FieldOpcodeDialogueOwnershipTests
{
    private const uint Script = 0x80004000;
    private const ushort ScriptPosition = 0x20;
    private const byte EntityId = 2;

    public static void Run()
    {
        AssertOpcodeReaderRequiresExactActiveOwnership();
        AssertAskReaderRequiresBoundedStableChoices();
        AssertDialogReaderUsesOnlyActiveVisibleWindowText();
        AssertDialogReaderRejectsTornOrUnboundedOwnership();
    }

    private static void AssertOpcodeReaderRequiresExactActiveOwnership()
    {
        var valid = MessageMemory();
        Equal(true, new FieldOpcodeParameterReader(valid).TryReadMessage(out var message), "owned MESSAGE opcode");
        Equal(1, message.WindowId, "owned MESSAGE window");
        Equal(7, message.DialogId, "owned MESSAGE dialog");

        var wrongOpcode = MessageMemory();
        wrongOpcode.Write(Script + ScriptPosition, [0x48, 1, 7]);
        Equal(false, new FieldOpcodeParameterReader(wrongOpcode).TryReadMessage(out _), "wrong opcode cannot masquerade as MESSAGE");

        var foreignOwner = MessageMemory();
        foreignOwner.Write((uint)(FieldMessageReader.AddressFieldWindowStates + 1), [3]);
        Equal(false, new FieldOpcodeParameterReader(foreignOwner).TryReadMessage(out _), "foreign entity window cannot own MESSAGE");

        var freeWindow = MessageMemory();
        freeWindow.Write((uint)(FieldMessageReader.AddressFieldWindowStates + 1), [FieldMessageReader.FreeWindowState]);
        Equal(true, new FieldOpcodeParameterReader(freeWindow).TryReadMessage(out var openingMessage), "exact opening MESSAGE may precede native owner assignment");
        Equal(1, openingMessage.WindowId, "opening MESSAGE window");
        Equal(7, openingMessage.DialogId, "opening MESSAGE dialog");

        var invalidWindow = OpcodeMemory([0x40, FieldMessageReader.WindowCount, 7]);
        Equal(false, new FieldOpcodeParameterReader(invalidWindow).TryReadMessage(out _), "MESSAGE window index is bounded");

        var invalidEntity = MessageMemory();
        invalidEntity.Write(Script + 2, [EntityId]);
        Equal(false, new FieldOpcodeParameterReader(invalidEntity).TryReadMessage(out _), "current entity must exist in field script");

        var opcodeTear = new TearingLegacyAddressSpace(
            valid,
            Script + ScriptPosition,
            [0x48, 1, 7]);
        Equal(false, new FieldOpcodeParameterReader(opcodeTear).TryReadMessage(out _), "MESSAGE opcode frame cannot tear");

        var ownerTear = new TearingLegacyAddressSpace(
            valid,
            (uint)(FieldMessageReader.AddressFieldWindowStates + 1),
            [3]);
        Equal(false, new FieldOpcodeParameterReader(ownerTear).TryReadMessage(out _), "MESSAGE owner cannot tear");
    }

    private static void AssertAskReaderRequiresBoundedStableChoices()
    {
        var valid = AskMemory(first: 1, last: 3);
        Equal(true, new FieldOpcodeParameterReader(valid).TryReadAsk(out var ask), "owned ASK opcode");
        Equal(2, ask.WindowId, "owned ASK window");
        Equal(8, ask.DialogId, "owned ASK dialog");
        Equal(1, ask.FirstQuestionLine, "owned ASK first choice line");
        Equal(3, ask.LastQuestionLine, "owned ASK last choice line");

        Equal(true, new FieldOpcodeParameterReader(AskMemory(first: 0, last: 12)).TryReadAsk(out _), "ASK may use all thirteen native visible lines");
        Equal(false, new FieldOpcodeParameterReader(AskMemory(first: 3, last: 2)).TryReadAsk(out _), "ASK choice range cannot be reversed");
        Equal(false, new FieldOpcodeParameterReader(AskMemory(first: 0, last: 13)).TryReadAsk(out _), "ASK choice range fits native visible lines");

        var hojoOpeningAsk = OpcodeMemory([0x48, 5, 0, 152, 2, 3, 8]);
        hojoOpeningAsk.Write(
            (uint)FieldMessageReader.AddressFieldWindowStates,
            [FieldMessageReader.FreeWindowState]);
        Equal(
            true,
            new FieldOpcodeParameterReader(hojoOpeningAsk).TryReadAsk(out var hojoAsk),
            "Hojo Sample H0512 ASK may precede native owner assignment");
        Equal(0, hojoAsk.WindowId, "Hojo ASK window");
        Equal(152, hojoAsk.DialogId, "Hojo ASK dialog");
        Equal(2, hojoAsk.FirstQuestionLine, "Hojo ASK first choice line");
        Equal(3, hojoAsk.LastQuestionLine, "Hojo ASK last choice line");

        var foreignOwner = AskMemory(first: 1, last: 3);
        foreignOwner.Write((uint)(FieldMessageReader.AddressFieldWindowStates + 2), [1]);
        Equal(false, new FieldOpcodeParameterReader(foreignOwner).TryReadAsk(out _), "foreign entity window cannot own ASK");

        var instructionTear = new TearingLegacyAddressSpace(
            valid,
            Script + ScriptPosition,
            [0x48, 1, 2, 8, 1, 3, 0]);
        Equal(false, new FieldOpcodeParameterReader(instructionTear).TryReadAsk(out _), "unexposed ASK parameters still belong to stable instruction");
    }

    private static void AssertDialogReaderUsesOnlyActiveVisibleWindowText()
    {
        const uint messageData = 0x80000000;
        var memory = FieldMemory();
        WriteUInt32(memory, FieldMessageReader.AddressFieldMessageDataPointer, messageData);
        memory.Write((uint)FieldMessageReader.AddressFieldWindowStates, [FieldMessageReader.FreeWindowState, EntityId, FieldMessageReader.FreeWindowState, FieldMessageReader.FreeWindowState]);
        WriteUInt32(memory, FieldMessageReader.AddressFieldWindowMessagePointers, messageData + 0x100);
        WriteUInt32(memory, FieldMessageReader.AddressFieldWindowMessagePointers + sizeof(uint), messageData + 0x200);
        WriteUInt32(memory, FieldMessageReader.AddressFieldWindowMessagePointers + 2 * sizeof(uint), 0);
        WriteUInt32(memory, FieldMessageReader.AddressFieldWindowMessagePointers + 3 * sizeof(uint), 0);
        memory.Write(messageData + 0x100, [0x23, 0xff]);
        memory.Write(messageData + 0x200, [0x24, 0xff]);
        memory.Write((uint)FieldMessageReader.AddressFieldWindowTextBuffers, [0x25, 0xff]);
        WriteVisibleBuffer(memory, windowId: 1, [0x22, 0xff]);

        var reader = new FieldDialogStringReader(memory);
        Equal(true, reader.TryReadCurrent(out var candidate), "active visible dialog buffer");
        Equal("B", candidate.Text, "active visible buffer owns speech over hidden and preview text");

        var hidden = FieldMemory();
        WriteUInt32(hidden, FieldMessageReader.AddressFieldMessageDataPointer, messageData);
        hidden.Write((uint)FieldMessageReader.AddressFieldWindowStates, [FieldMessageReader.FreeWindowState, FieldMessageReader.FreeWindowState, FieldMessageReader.FreeWindowState, FieldMessageReader.FreeWindowState]);
        WriteUInt32(hidden, FieldMessageReader.AddressFieldWindowMessagePointers, messageData + 0x100);
        WriteUInt32(hidden, FieldMessageReader.AddressFieldWindowMessagePointers + sizeof(uint), 0);
        WriteUInt32(hidden, FieldMessageReader.AddressFieldWindowMessagePointers + 2 * sizeof(uint), 0);
        WriteUInt32(hidden, FieldMessageReader.AddressFieldWindowMessagePointers + 3 * sizeof(uint), 0);
        hidden.Write(messageData + 0x100, [0x23, 0xff]);
        hidden.Write((uint)FieldMessageReader.AddressFieldWindowTextBuffers, [0x21, 0xff]);
        Equal(false, new FieldDialogStringReader(hidden).TryReadCurrent(out _), "hidden pointer and buffer cannot speak");

        var foreignPointer = DialogMemory(messageData, messageData + FieldMessageReader.FieldMessageDataRange, [0x21, 0xff]);
        Equal(false, new FieldDialogStringReader(foreignPointer).TryReadCurrent(out _), "active window pointer must belong to current field message data");
    }

    private static void AssertDialogReaderRejectsTornOrUnboundedOwnership()
    {
        const uint messageData = 0x710000;
        var valid = DialogMemory(messageData, messageData + 0x20, [0x21, 0xff]);

        var stateTear = new TearingLegacyAddressSpace(
            valid,
            (uint)FieldMessageReader.AddressFieldWindowStates,
            [FieldMessageReader.FreeWindowState, FieldMessageReader.FreeWindowState, FieldMessageReader.FreeWindowState, FieldMessageReader.FreeWindowState]);
        Equal(false, new FieldDialogStringReader(stateTear).TryReadCurrent(out _), "dialog owner state cannot tear");

        var replacement = new byte[FieldMessageReader.FieldTextBufferLength];
        Array.Fill(replacement, (byte)0xff);
        replacement[0] = 0x22;
        var visibleTextTear = new TearingLegacyAddressSpace(
            valid,
            (uint)FieldMessageReader.AddressFieldWindowTextBuffers,
            replacement);
        Equal(false, new FieldDialogStringReader(visibleTextTear).TryReadCurrent(out _), "visible dialog buffer cannot tear");

        var messageBaseTear = new TearingLegacyAddressSpace(
            valid,
            (uint)FieldMessageReader.AddressFieldMessageDataPointer,
            BitConverter.GetBytes(0x720000u));
        Equal(false, new FieldDialogStringReader(messageBaseTear).TryReadCurrent(out _), "dialog message base cannot tear");

        var overflow = DialogMemory(0xfffffff0u, 0xfffffff8u, [0x21, 0xff]);
        Equal(false, new FieldDialogStringReader(overflow).TryReadCurrent(out _), "dialog message range arithmetic cannot wrap");
    }

    private static ContiguousLegacyAddressSpace MessageMemory() => OpcodeMemory([0x40, 1, 7]);

    private static ContiguousLegacyAddressSpace AskMemory(byte first, byte last) =>
        OpcodeMemory([0x48, 0, 2, 8, first, last, 6]);

    private static ContiguousLegacyAddressSpace OpcodeMemory(byte[] instruction)
    {
        var memory = FieldMemory();
        WriteUInt32(memory, FieldOpcodeParameterReader.AddressFieldScriptPtr, Script);
        memory.Write(Script + 2, [4]);
        memory.Write((uint)FieldOpcodeParameterReader.AddressCurrentEntityId, [EntityId]);
        WriteUInt16(
            memory,
            FieldOpcodeParameterReader.AddressFieldCurrScriptPosition + EntityId * sizeof(ushort),
            ScriptPosition);
        memory.Write(Script + ScriptPosition, instruction);

        var window = instruction[0] == 0x40 ? instruction[1] : instruction[2];
        if (window < FieldMessageReader.WindowCount)
        {
            memory.Write((uint)(FieldMessageReader.AddressFieldWindowStates + window), [EntityId]);
        }

        return memory;
    }

    private static ContiguousLegacyAddressSpace DialogMemory(uint messageData, uint pointer, byte[] visibleText)
    {
        var memory = FieldMemory();
        WriteUInt32(memory, FieldMessageReader.AddressFieldMessageDataPointer, messageData);
        memory.Write((uint)FieldMessageReader.AddressFieldWindowStates, [EntityId, FieldMessageReader.FreeWindowState, FieldMessageReader.FreeWindowState, FieldMessageReader.FreeWindowState]);
        WriteUInt32(memory, FieldMessageReader.AddressFieldWindowMessagePointers, pointer);
        WriteUInt32(memory, FieldMessageReader.AddressFieldWindowMessagePointers + sizeof(uint), 0);
        WriteUInt32(memory, FieldMessageReader.AddressFieldWindowMessagePointers + 2 * sizeof(uint), 0);
        WriteUInt32(memory, FieldMessageReader.AddressFieldWindowMessagePointers + 3 * sizeof(uint), 0);
        memory.Write(pointer, [0x23, 0xff]);
        WriteVisibleBuffer(memory, windowId: 0, visibleText);
        return memory;
    }

    private static void WriteVisibleBuffer(
        ContiguousLegacyAddressSpace memory,
        int windowId,
        IReadOnlyList<byte> visibleText)
    {
        var buffer = new byte[FieldMessageReader.FieldTextBufferLength];
        Array.Fill(buffer, (byte)0xff);
        for (var index = 0; index < visibleText.Count; index++)
        {
            buffer[index] = visibleText[index];
        }

        memory.Write(
            (uint)(FieldMessageReader.AddressFieldWindowTextBuffers +
                windowId * FieldMessageReader.WindowTextBufferStride),
            buffer);
    }

    private static ContiguousLegacyAddressSpace FieldMemory()
    {
        var memory = new ContiguousLegacyAddressSpace();
        memory.Write((uint)FieldPositionReader.AddressCurrentModule, [FieldPositionReader.FieldModule]);
        WriteUInt16(memory, FieldPositionReader.AddressFieldId, 116);
        return memory;
    }

    private static void WriteUInt16(ContiguousLegacyAddressSpace memory, int address, ushort value) =>
        memory.Write((uint)address, BitConverter.GetBytes(value));

    private static void WriteUInt32(ContiguousLegacyAddressSpace memory, int address, uint value) =>
        memory.Write((uint)address, BitConverter.GetBytes(value));

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}

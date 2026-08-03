using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime;
using Ff7.Accessibility.Steam2026X64.Runtime.Dialogue;

internal static class Steam2026AskCursorCallbackTests
{
    private const ulong ModuleImageSize = 0x02100000;
    private const uint Esp = 0x00180000;
    private const uint CurrentLinePointer = 0x00300000;
    private const uint ScriptPointer = 0x00400000;
    private const uint WindowLifecyclePhaseAddress = 0x00CFF5E4;
    private const uint WindowLifecycleStride = 0x30;
    private const ushort AskSelectionPhase = 6;

    public static void Run(Steam2026FingerprintResult supportedRuntime)
    {
        CapturesExactOwnedAskSelection();
        RejectsMismatchedAndInvalidAskState();
        RejectsPostSelectionLifecyclePhase();
        RejectsRemappedGuestStateAndTornEsp();
        RevalidatesCallbackAndResolverIdentity();
        ActiveHookLeasePreservesCheckedCapture(supportedRuntime);
        DetourCapturesBeforeOriginalAndPublishesPointerFreeState(supportedRuntime);
        DetourAlwaysCallsOriginalAndFailsClosed(supportedRuntime);
        HookSurfaceUsesTheTranslatedNoArgumentHostAbi();
        ContractHasNoPublicHookOrBackendSurface();
    }

    private static void ActiveHookLeasePreservesCheckedCapture(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = CreateFixture();
        var contract = CreateContract(fixture, supportedRuntime: supportedRuntime);
        Equal(
            true,
            contract.TryValidateCaptureIdentity(out var hostAddress),
            "exact ASK hook identity before detour");
        Equal(
            TranslatedCallCaptureFixture.ModuleBase
            + Steam2026AskCursorCallbackContract.FunctionMap.HostRva,
            hostAddress,
            "exact ASK host address");

        fixture.Native.Write(hostAddress, [0xE9, 0, 0, 0, 0]);
        contract.ActivateHookLease(() => true);
        Equal(
            true,
            contract.TryCaptureAskCursor(out var capture),
            "active ASK hook lease validates the mapped target instead of the patched prefix");
        Equal(2, capture.CurrentQuestionLine, "leased ASK cursor line");

        contract.RevokeHookLease();
        Equal(
            false,
            contract.TryCaptureAskCursor(out _),
            "revoked ASK lease cannot authorize a patched callback");
    }

    private static void DetourCapturesBeforeOriginalAndPublishesPointerFreeState(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = CreateFixture();
        var contract = CreateContract(fixture, supportedRuntime: supportedRuntime);
        var queue = new BoundedNativeIngressQueue<Steam2026AskCursorIngressSnapshot>(4);
        var dialogueIngressSequencer = new Steam2026DialogueIngressSequencer();
        var originalCalls = 0;
        var nestedMessageSequence = 0L;
        using var ingress = new Steam2026AskCursorDetourIngressCoordinator(
            contract,
            () =>
            {
                originalCalls++;
                Equal(
                    true,
                    dialogueIngressSequencer.TryReserve(out nestedMessageSequence),
                    "nested MESSAGE reserves its native entry order");
                fixture.WriteGuest(CurrentLinePointer, BitConverter.GetBytes((ushort)3));
            },
            dialogueIngressSequencer,
            () => new DateTime(2026, 7, 22, 21, 0, 0, DateTimeKind.Utc),
            queue);

        contract.ActivateHookLease(() => true);
        ingress.OnAskCursor();

        Equal(1, originalCalls, "ASK detour calls the translated original exactly once");
        Equal(true, queue.TryDequeue(out var snapshot), "ASK detour publishes one checked snapshot");
        Equal(1L, snapshot.Sequence, "ASK ingress sequence");
        Equal(2L, nestedMessageSequence, "nested MESSAGE follows the outer ASK entry");
        Equal(2, snapshot.Capture.CurrentQuestionLine, "ASK state is copied before the original mutates it");
        Equal(false, typeof(Steam2026AskCursorIngressSnapshot).GetProperties()
            .Any(property => property.Name.Contains("Pointer", StringComparison.OrdinalIgnoreCase)
                             || property.Name.Contains("Address", StringComparison.OrdinalIgnoreCase)),
            "ASK ingress snapshot is pointer-free");
        contract.RevokeHookLease();
    }

    private static void DetourAlwaysCallsOriginalAndFailsClosed(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = CreateFixture(currentQuestionLine: 9);
        var contract = CreateContract(fixture, supportedRuntime: supportedRuntime);
        var queue = new BoundedNativeIngressQueue<Steam2026AskCursorIngressSnapshot>(1);
        var originalCalls = 0;
        using var ingress = new Steam2026AskCursorDetourIngressCoordinator(
            contract,
            () => originalCalls++,
            new Steam2026DialogueIngressSequencer(),
            () => DateTime.UtcNow,
            queue);

        contract.ActivateHookLease(() => true);
        ingress.OnAskCursor();
        Equal(1, originalCalls, "invalid ASK capture still calls original once");
        Equal(false, queue.TryDequeue(out _), "invalid ASK capture publishes nothing");
        Equal(false, ingress.IsFatallyDegraded, "ordinary invalid ASK state is not fatal");
        contract.RevokeHookLease();
    }

    private static void HookSurfaceUsesTheTranslatedNoArgumentHostAbi()
    {
        var invoke = typeof(TranslatedAskCursorCallbackOriginal).GetMethod("Invoke")!;
        Equal(typeof(void), invoke.ReturnType, "ASK host callback returns void");
        Equal(0, invoke.GetParameters().Length, "ASK host callback has no host arguments");
        Equal(
            true,
            typeof(TranslatedAskCursorCallbackOriginal).GetCustomAttributes(false)
                .Any(attribute => attribute.GetType().Name == "FunctionAttribute"),
            "ASK host callback carries Reloaded x64 ABI metadata");
    }

    private static void CapturesExactOwnedAskSelection()
    {
        var fixture = CreateFixture();
        var contract = CreateContract(fixture);

        Equal(true, contract.TryCaptureAskCursor(out var capture), "coherent translated ASK cursor capture");
        Equal(134, capture.FieldId, "ASK field identity");
        Equal(2, capture.WindowId, "ASK window identity");
        Equal(8, capture.DialogId, "ASK dialogue identity");
        Equal(1, capture.FirstQuestionLine, "ASK first visible choice line");
        Equal(3, capture.LastQuestionLine, "ASK last visible choice line");
        Equal(2, capture.CurrentQuestionLine, "ASK exact highlighted line");
    }

    private static void RejectsMismatchedAndInvalidAskState()
    {
        var mismatched = CreateFixture(callbackDialogId: 9);
        Equal(false, CreateContract(mismatched).TryCaptureAskCursor(out _), "callback dialogue must match owned ASK instruction");

        var outsideRange = CreateFixture(currentQuestionLine: 4);
        Equal(false, CreateContract(outsideRange).TryCaptureAskCursor(out _), "highlighted ASK line must remain inside native choice range");

        var foreignOwner = CreateFixture();
        foreignOwner.WriteGuest((uint)FieldMessageReader.AddressFieldWindowStates + 2, [1]);
        Equal(false, CreateContract(foreignOwner).TryCaptureAskCursor(out _), "foreign entity cannot own ASK window");

        var unmappedPointer = CreateFixture(currentLinePointer: 0x00500000, mapCurrentLine: false);
        Equal(false, CreateContract(unmappedPointer).TryCaptureAskCursor(out _), "unmapped current-line pointer is rejected");
    }

    private static void RejectsPostSelectionLifecyclePhase()
    {
        var postSelection = CreateFixture();
        postSelection.WriteGuest(
            WindowLifecyclePhaseAddress + (2 * WindowLifecycleStride),
            BitConverter.GetBytes((ushort)7));

        Equal(
            false,
            CreateContract(postSelection).TryCaptureAskCursor(out _),
            "ASK cursor capture stops when native Confirm advances the window beyond selection phase");
    }

    private static void RejectsRemappedGuestStateAndTornEsp()
    {
        var remapped = CreateFixture();
        var scriptPageEntry = remapped.GetPageTableEntryAddress(ScriptPointer);
        var remappingMemory = new RemappingNativeMemoryReader(
            remapped.Native,
            scriptPageEntry,
            triggerRead: 3,
            () => remapped.MapGuestPage(ScriptPointer, 0));
        Equal(
            false,
            CreateContract(remapped, remappingMemory).TryCaptureAskCursor(out _),
            "ASK instruction page remap during capture is rejected");

        var tornEsp = CreateFixture();
        const uint otherEsp = 0x00181000;
        tornEsp.WriteStack(otherEsp, [2, 8, 1, 3, CurrentLinePointer]);
        var tearingMemory = new TearingNativeMemoryReader(
            tornEsp.Native,
            TranslatedCallCaptureFixture.ModuleBase + TranslatedX86CallFrameReader.EspRva,
            triggerRead: 3,
            () => tornEsp.SetEsp(otherEsp));
        Equal(
            false,
            CreateContract(tornEsp, tearingMemory).TryCaptureAskCursor(out _),
            "guest ESP change during ASK capture is rejected");
    }

    private static void RevalidatesCallbackAndResolverIdentity()
    {
        var staleIdentity = CreateFixture();
        var hostAddress = TranslatedCallCaptureFixture.ModuleBase
                          + Steam2026AskCursorCallbackContract.FunctionMap.HostRva;
        var tearingIdentityMemory = new TearingNativeMemoryReader(
            staleIdentity.Native,
            hostAddress,
            triggerRead: 3,
            () => staleIdentity.Native.Write(hostAddress, [0x90]));
        Equal(
            false,
            CreateContract(staleIdentity, tearingIdentityMemory).TryCaptureAskCursor(out _),
            "ASK callback identity is revalidated after capture");

        var badResolver = CreateFixture();
        badResolver.CorruptResolverSignature();
        Throws<InvalidDataException>(
            () => _ = CreateContract(badResolver),
            "ASK callback contract requires translated resolver identity");
    }

    private static void ContractHasNoPublicHookOrBackendSurface()
    {
        var assembly = typeof(TranslatedX86AddressSpace).Assembly;
        var contract = assembly.GetType(
            "Ff7.Accessibility.Steam2026X64.Runtime.Dialogue.Steam2026AskCursorCallbackContract",
            throwOnError: true)!;
        var token = assembly.GetType(
            "Ff7.Accessibility.Steam2026X64.Runtime.Dialogue.Steam2026AskCaptureToken",
            throwOnError: true)!;
        Equal(false, contract.IsPublic, "ASK capture contract remains research-internal");
        Equal(false, token.IsPublic, "ASK capture authority token remains non-public");
        Equal(0, contract.GetConstructors().Length, "ASK capture contract has no public constructor");
        Equal(0, token.GetConstructors().Length, "ASK capture token has no public constructor");
        Equal(false, typeof(Ff7.Accessibility.Runtime.Abstractions.IFf7RuntimeBackend).IsAssignableFrom(contract), "ASK capture contract is not a runtime backend");
    }

    private static TranslatedCallCaptureFixture CreateFixture(
        byte callbackDialogId = 8,
        ushort currentQuestionLine = 2,
        uint currentLinePointer = CurrentLinePointer,
        bool mapCurrentLine = true)
    {
        var fixture = new TranslatedCallCaptureFixture();
        var map = Steam2026AskCursorCallbackContract.FunctionMap;
        fixture.Native.MapRegion(
            TranslatedCallCaptureFixture.ModuleBase + 0x00C00000,
            0x00100000,
            TranslatedCallCaptureFixture.ModuleBase,
            isCommitted: true,
            isExecutable: true);
        fixture.Native.Write(
            TranslatedCallCaptureFixture.ModuleBase + map.MappingRecordRva,
            BitConverter.GetBytes((ulong)map.LegacyVirtualAddress));
        fixture.Native.Write(
            TranslatedCallCaptureFixture.ModuleBase + map.MappingRecordRva + sizeof(ulong),
            BitConverter.GetBytes(TranslatedCallCaptureFixture.ModuleBase + map.HostRva));
        fixture.Native.Write(
            TranslatedCallCaptureFixture.ModuleBase + map.HostRva,
            Convert.FromHexString(map.ExpectedPrefixHex));

        fixture.WriteGuest((uint)FieldPositionReader.AddressCurrentModule, [FieldPositionReader.FieldModule]);
        fixture.WriteGuest((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)134));
        fixture.WriteGuest((uint)FieldOpcodeParameterReader.AddressFieldScriptPtr, BitConverter.GetBytes(ScriptPointer));
        fixture.WriteGuest(ScriptPointer + 2, [1]);
        fixture.WriteGuest((uint)FieldOpcodeParameterReader.AddressCurrentEntityId, [0]);
        fixture.WriteGuest((uint)FieldOpcodeParameterReader.AddressFieldCurrScriptPosition, BitConverter.GetBytes((ushort)0x20));
        fixture.WriteGuest(
            ScriptPointer + 0x20,
            [FieldOpcodeParameterReader.AskOpcode, 0, 2, 8, 1, 3, 6]);
        fixture.WriteGuest((uint)FieldMessageReader.AddressFieldWindowStates + 2, [0]);
        fixture.WriteGuest(
            WindowLifecyclePhaseAddress + (2 * WindowLifecycleStride),
            BitConverter.GetBytes(AskSelectionPhase));
        if (mapCurrentLine)
        {
            fixture.WriteGuest(currentLinePointer, BitConverter.GetBytes(currentQuestionLine));
        }

        fixture.WriteCall(Esp, [2, callbackDialogId, 1, 3, currentLinePointer]);
        return fixture;
    }

    private static Steam2026AskCursorCallbackContract CreateContract(
        TranslatedCallCaptureFixture fixture,
        INativeMemoryReader? memory = null,
        Steam2026FingerprintResult? supportedRuntime = null) =>
        supportedRuntime is null
            ? new Steam2026AskCursorCallbackContract(
                TranslatedCallCaptureFixture.ModuleBase,
                ModuleImageSize,
                memory ?? fixture.Native)
            : new Steam2026AskCursorCallbackContract(
                supportedRuntime,
                TranslatedCallCaptureFixture.ModuleBase,
                ModuleImageSize,
                memory ?? fixture.Native);

    private static void Throws<TException>(Action action, string label)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{label}: expected {typeof(TException).Name}.");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}

using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime;
using Ff7.Accessibility.Steam2026X64.Runtime.Dialogue;

internal static class Steam2026FieldMessageCallbackTests
{
    private const ulong ModuleImageSize = 0x02100000;
    private const uint ScriptPointer = 0x00400000;

    internal static void Run(Steam2026FingerprintResult supportedRuntime)
    {
        ExactCallbackCapturesMessageBeforeOriginalAndPublishesResult(supportedRuntime);
        InvalidMessageStateStillInvokesOriginal(supportedRuntime);
        HookSurfaceUsesTranslatedNoArgumentHostAbi();
    }

    private static void ExactCallbackCapturesMessageBeforeOriginalAndPublishesResult(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = CreateFixture();
        var contract = new Steam2026FieldMessageCallbackContract(
            supportedRuntime,
            TranslatedCallCaptureFixture.ModuleBase,
            ModuleImageSize,
            fixture.Native);
        var queue = new BoundedNativeIngressQueue<Steam2026FieldMessageIngressSnapshot>(4);
        var dialogueIngressSequencer = new Steam2026DialogueIngressSequencer();
        var originalCalls = 0;
        var nestedAskSequence = 0L;
        using var ingress = new Steam2026FieldMessageDetourIngressCoordinator(
            contract,
            () =>
            {
                originalCalls++;
                Equal(
                    true,
                    dialogueIngressSequencer.TryReserve(out nestedAskSequence),
                    "nested ASK reserves its native entry order");
                fixture.WriteGuest(ScriptPointer + 0x20, [0]);
                fixture.Native.Write(
                    TranslatedCallCaptureFixture.ModuleBase
                    + TranslatedX86CallFrameReader.EaxRva,
                    BitConverter.GetBytes(1u));
            },
            dialogueIngressSequencer,
            () => new DateTime(2026, 7, 23, 11, 35, 49, DateTimeKind.Utc),
            queue);

        Equal(
            0x00618DBDu,
            Steam2026FieldMessageCallbackContract.FunctionMap.LegacyVirtualAddress,
            "MESSAGE exact legacy function");
        Equal(
            0x016EA870ul,
            Steam2026FieldMessageCallbackContract.FunctionMap.MappingRecordRva,
            "MESSAGE exact mapping record");
        Equal(
            0x00BCD3F0ul,
            Steam2026FieldMessageCallbackContract.FunctionMap.HostRva,
            "MESSAGE exact translated host function");

        contract.ActivateHookLease(() => true);
        ingress.OnMessage();

        Equal(1, originalCalls, "MESSAGE detour invokes translated original exactly once");
        Equal(true, queue.TryDequeue(out var snapshot), "MESSAGE detour publishes one lifecycle");
        Equal(1L, snapshot.Sequence, "MESSAGE ingress sequence");
        Equal(2L, nestedAskSequence, "nested ASK follows the outer MESSAGE entry");
        Equal(FieldOpcodeKind.Message, snapshot.Observation.Kind, "MESSAGE ingress kind");
        Equal(134, snapshot.Observation.FieldId, "MESSAGE ingress field");
        Equal(2, snapshot.Observation.WindowId, "MESSAGE ingress window");
        Equal(8, snapshot.Observation.DialogId, "MESSAGE ingress dialog");
        Equal(1, snapshot.Result, "MESSAGE ingress reads logical guest EAX after original");
        Equal(
            false,
            typeof(Steam2026FieldMessageIngressSnapshot).GetProperties()
                .Any(property =>
                    property.Name.Contains("Pointer", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Contains("Address", StringComparison.OrdinalIgnoreCase)),
            "MESSAGE ingress remains pointer-free");
        contract.RevokeHookLease();
    }

    private static void InvalidMessageStateStillInvokesOriginal(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = CreateFixture();
        fixture.WriteGuest(ScriptPointer + 0x20, [0]);
        var contract = new Steam2026FieldMessageCallbackContract(
            supportedRuntime,
            TranslatedCallCaptureFixture.ModuleBase,
            ModuleImageSize,
            fixture.Native);
        var queue = new BoundedNativeIngressQueue<Steam2026FieldMessageIngressSnapshot>(1);
        var originalCalls = 0;
        using var ingress = new Steam2026FieldMessageDetourIngressCoordinator(
            contract,
            () => originalCalls++,
            new Steam2026DialogueIngressSequencer(),
            () => DateTime.UtcNow,
            queue);

        contract.ActivateHookLease(() => true);
        ingress.OnMessage();
        Equal(1, originalCalls, "invalid MESSAGE capture still invokes original");
        Equal(false, queue.TryDequeue(out _), "invalid MESSAGE capture publishes nothing");
        Equal(false, ingress.IsFatallyDegraded, "ordinary invalid MESSAGE state is not fatal");
        contract.RevokeHookLease();
    }

    private static void HookSurfaceUsesTranslatedNoArgumentHostAbi()
    {
        var invoke = typeof(TranslatedFieldMessageCallbackOriginal).GetMethod("Invoke")!;
        Equal(typeof(void), invoke.ReturnType, "MESSAGE host callback returns void");
        Equal(0, invoke.GetParameters().Length, "MESSAGE host callback has no host arguments");
        Equal(
            true,
            typeof(TranslatedFieldMessageCallbackOriginal).GetCustomAttributes(false)
                .Any(attribute => attribute.GetType().Name == "FunctionAttribute"),
            "MESSAGE host callback carries Reloaded x64 ABI metadata");
    }

    private static TranslatedCallCaptureFixture CreateFixture()
    {
        var fixture = new TranslatedCallCaptureFixture();
        var map = Steam2026FieldMessageCallbackContract.FunctionMap;
        fixture.Native.MapRegion(
            TranslatedCallCaptureFixture.ModuleBase + 0x00B00000,
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

        fixture.WriteGuest(
            (uint)FieldPositionReader.AddressCurrentModule,
            [FieldPositionReader.FieldModule]);
        fixture.WriteGuest(
            (uint)FieldPositionReader.AddressFieldId,
            BitConverter.GetBytes((ushort)134));
        fixture.WriteGuest(
            (uint)FieldOpcodeParameterReader.AddressFieldScriptPtr,
            BitConverter.GetBytes(ScriptPointer));
        fixture.WriteGuest(ScriptPointer + 2, [1]);
        fixture.WriteGuest((uint)FieldOpcodeParameterReader.AddressCurrentEntityId, [0]);
        fixture.WriteGuest(
            (uint)FieldOpcodeParameterReader.AddressFieldCurrScriptPosition,
            BitConverter.GetBytes((ushort)0x20));
        fixture.WriteGuest(
            ScriptPointer + 0x20,
            [FieldOpcodeParameterReader.MessageOpcode, 2, 8]);
        fixture.WriteGuest((uint)FieldMessageReader.AddressFieldWindowStates + 2, [0]);
        fixture.Native.Write(
            TranslatedCallCaptureFixture.ModuleBase + TranslatedX86CallFrameReader.EaxRva,
            BitConverter.GetBytes(0u));
        return fixture;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}

using System.Reflection;
using System.Runtime.InteropServices;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using Reloaded.Hooks.Definitions;

internal static class Steam2026FieldCutsceneWaitTests
{
    private const ulong ModuleImageSize = 0x02100000;
    private static readonly DateTime Timestamp =
        new(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc);

    public static void Run(Steam2026FingerprintResult supportedRuntime)
    {
        CatalogAndDelegateExposeEveryExactDescriptionCallback();
        ContractCapturesAStableWaitContextAcrossAnActiveHookLease(supportedRuntime);
        ContractCapturesAStableSoundContextAcrossAnActiveHookLease(supportedRuntime);
        ContractAcceptsBothOpcodesForSharedTranslatedHandlers(supportedRuntime);
        IngressCopiesContextBeforeCallingTheOriginalExactlyOnce(supportedRuntime);
        SoundIngressCopiesContextBeforeCallingTheOriginalExactlyOnce(supportedRuntime);
        IngressContainsOriginalAndQueueFailures(supportedRuntime);
        DescriptionSpeechRetriesWithoutLosingTheCue();
        DescriptionSpeechDefersForReadableDialogueAndFailsClosed();
        DescriptionSpeechAcceptsExactSoundAndRejectsMessage();
        HookSetOwnsCatalogDrivenProvenDetours();
    }

    private static void CatalogAndDelegateExposeEveryExactDescriptionCallback()
    {
        var expected = new[]
        {
            (Steam2026FieldCutsceneCallbackKind.Request, 0x006123E2u, 0x016EA2E0ul, 0x00BB0C30ul, "48895C2408574883EC208B0D88894801"),
            (Steam2026FieldCutsceneCallbackKind.RequestSw, 0x0061246Au, 0x016EA2F0ul, 0x00BB0F30ul, "48895C2408574883EC208B0D88864801"),
            (Steam2026FieldCutsceneCallbackKind.RequestEw, 0x006124F2u, 0x016EA300ul, 0x00BB1230ul, "48895C2408574883EC208B0D88834801"),
            (Steam2026FieldCutsceneCallbackKind.Split, 0x0061CE0Cu, 0x016EAE30ul, 0x00BE1A70ul, "40534883EC208B0D4C7B45018B1D4A7B"),
            (Steam2026FieldCutsceneCallbackKind.Wait, 0x00610818u, 0x016EA110ul, 0x00BA8A70ul, "48895C2408574883EC208B0D480B4901"),
            (Steam2026FieldCutsceneCallbackKind.Scroll2D, 0x0061A7F9u, 0x016EABE0ul, 0x00BD5FC0ul, "48895C2408574883EC208B0DF8354601"),
            (Steam2026FieldCutsceneCallbackKind.Fade, 0x0061DDB4u, 0x016EAEA0ul, 0x00BE6490ul, "48895C2408574883EC208B0D28314501"),
            (Steam2026FieldCutsceneCallbackKind.Anime1, 0x0061484Au, 0x016EA5B0ul, 0x00BBB7C0ul, "48895C2408574883EC208B0DF8DD4701"),
            (Steam2026FieldCutsceneCallbackKind.Visibility, 0x00618A01u, 0x016EA820ul, 0x00BCC320ul, "48895C2408574883EC208B0D98D24601"),
            (Steam2026FieldCutsceneCallbackKind.AnimOnceOrHold, 0x006149A5u, 0x016EA5C0ul, 0x00BBBC40ul, "48895C2408574883EC208B0D78D94701"),
            (Steam2026FieldCutsceneCallbackKind.Canm1Or2, 0x00614E3Eu, 0x016EA5E0ul, 0x00BBCF20ul, "48895C2408574883EC208B0D98C64701"),
            (Steam2026FieldCutsceneCallbackKind.BackgroundOn, 0x0061A035u, 0x016EAB00ul, 0x00BD3CD0ul, "48895C24084889742410574883EC208B"),
            (Steam2026FieldCutsceneCallbackKind.Sound, 0x00613A2Du, 0x016EA430ul, 0x00BB72C0ul, "48895C2408574883EC208B0DF8224801"),
            (Steam2026FieldCutsceneCallbackKind.Akao, 0x006137F9u, 0x016EA410ul, 0x00BB6620ul, "48895C2408574883EC208B0D982F4801"),
            (Steam2026FieldCutsceneCallbackKind.Movie, 0x0061A321u, 0x016EAB60ul, 0x00BD4A70ul, "48895C2408574883EC208B0D484B4601")
        };

        Equal(expected.Length, Enum.GetValues<Steam2026FieldCutsceneCallbackKind>().Length,
            "cutscene ingress exposes every catalog opcode handler");
        foreach (var item in expected)
        {
            var metadata = Steam2026FieldCutsceneCallbackCatalog.GetMetadata(item.Item1);
            Equal(item.Item1, metadata.Kind, $"{item.Item1} callback kind");
            Equal(item.Item2, metadata.FunctionMap.LegacyVirtualAddress, $"{item.Item1} legacy VA");
            Equal(item.Item3, metadata.FunctionMap.MappingRecordRva, $"{item.Item1} map-record RVA");
            Equal(item.Item4, metadata.FunctionMap.HostRva, $"{item.Item1} translated host RVA");
            Equal(item.Item5, metadata.FunctionMap.ExpectedPrefixHex, $"{item.Item1} exact translated prefix");
            Equal(
                TranslatedFieldCutsceneHostAbi.TranslatedX86VoidNoArguments,
                metadata.HostAbi,
                $"{item.Item1} translated host ABI");
        }

        var delegateType = typeof(TranslatedFieldCutsceneCallbackOriginal);
        var unmanaged = delegateType.GetCustomAttribute<UnmanagedFunctionPointerAttribute>()
                        ?? throw new InvalidOperationException("WAIT delegate lacks unmanaged ABI metadata.");
        Equal(CallingConvention.Winapi, unmanaged.CallingConvention, "WAIT Windows ABI");
        var invoke = delegateType.GetMethod("Invoke")
                     ?? throw new InvalidOperationException("WAIT delegate lacks Invoke.");
        Equal(typeof(void), invoke.ReturnType, "WAIT delegate return type");
        Equal(0, invoke.GetParameters().Length, "WAIT delegate parameter count");
        AssertPointerFree(typeof(FieldScriptContext), "field script context");
        AssertPointerFree(
            typeof(Steam2026FieldCutsceneIngressSnapshot),
            "field cutscene ingress snapshot");
    }

    private static void ContractAcceptsBothOpcodesForSharedTranslatedHandlers(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = CreateFixture(FieldOpcodeAddressResolver.OpcodeAnimOnceIndex);
        var contract = CreateExactContract(fixture, supportedRuntime);
        Equal(
            true,
            contract.TryValidateCaptureIdentity(
                Steam2026FieldCutsceneCallbackKind.AnimOnceOrHold,
                out var identity),
            "shared animation handler pristine identity");
        Equal(
            FieldOpcodeAddressResolver.OpcodeAnimOnceIndex,
            ReadContext(contract, identity, "ANIM ONCE").Opcode,
            "shared animation handler accepts ANIM ONCE");

        WriteCurrentOpcode(fixture, FieldOpcodeAddressResolver.OpcodeAnimHoldIndex);
        Equal(
            FieldOpcodeAddressResolver.OpcodeAnimHoldIndex,
            ReadContext(contract, identity, "ANIM HOLD").Opcode,
            "shared animation handler accepts ANIM HOLD");

        WriteCurrentOpcode(fixture, FieldOpcodeAddressResolver.OpcodeMessageIndex);
        Equal(
            false,
            contract.TryCaptureContext(identity, out _),
            "shared animation handler rejects MESSAGE");
    }

    private static void ContractCapturesAStableWaitContextAcrossAnActiveHookLease(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = CreateWaitFixture();
        var contract = CreateExactContract(fixture, supportedRuntime);
        Equal(true, contract.HasExactSupportedFingerprint, "WAIT contract exact fingerprint gate");
        Equal(
            true,
            contract.TryValidateCaptureIdentity(
                Steam2026FieldCutsceneCallbackKind.Wait,
                out var identity),
            "WAIT pristine callback identity");
        Equal(
            new FieldScriptContext(116, 0, 0, 160, FieldOpcodeAddressResolver.OpcodeWaitIndex),
            ReadWait(contract, identity),
            "checked WAIT script context");

        fixture.Native.Write(identity.HostAddress, [0xE9]);
        contract.ActivateHookLease(_ => true);
        Equal(
            true,
            contract.IsCurrentCaptureIdentity(identity),
            "WAIT identity remains current while owning hook lease is active");
        Equal(
            new FieldScriptContext(116, 0, 0, 160, FieldOpcodeAddressResolver.OpcodeWaitIndex),
            ReadWait(contract, identity),
            "checked WAIT context survives patched entry prefix");

        contract.RevokeHookLease();
        Equal(
            false,
            contract.TryCaptureContext(identity, out _),
            "WAIT capture rejects a patched prefix without its hook lease");
    }

    private static void ContractCapturesAStableSoundContextAcrossAnActiveHookLease(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = CreateSoundFixture();
        var contract = CreateExactContract(fixture, supportedRuntime);
        Equal(
            true,
            contract.TryValidateCaptureIdentity(
                Steam2026FieldCutsceneCallbackKind.Sound,
                out var identity),
            "SOUND pristine callback identity");
        Equal(
            new FieldScriptContext(
                116,
                0,
                0,
                160,
                FieldOpcodeAddressResolver.OpcodeSoundIndex),
            ReadContext(contract, identity, "SOUND"),
            "checked SOUND script context");

        fixture.Native.Write(identity.HostAddress, [0xE9]);
        contract.ActivateHookLease(_ => true);
        Equal(
            true,
            contract.IsCurrentCaptureIdentity(identity),
            "SOUND identity remains current while owning hook lease is active");
        Equal(
            new FieldScriptContext(
                116,
                0,
                0,
                160,
                FieldOpcodeAddressResolver.OpcodeSoundIndex),
            ReadContext(contract, identity, "SOUND"),
            "checked SOUND context survives patched entry prefix");

        contract.RevokeHookLease();
        Equal(
            false,
            contract.TryCaptureContext(identity, out _),
            "SOUND capture rejects a patched prefix without its hook lease");
    }

    private static void IngressCopiesContextBeforeCallingTheOriginalExactlyOnce(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = CreateWaitFixture();
        var contract = CreateExactContract(fixture, supportedRuntime);
        var queue = new BoundedNativeIngressQueue<Steam2026FieldCutsceneIngressSnapshot>(4);
        var originalCalls = 0;
        using var ingress = new Steam2026FieldCutsceneDetourIngressCoordinator(
            contract,
            Steam2026FieldCutsceneCallbackKind.Wait,
            () =>
            {
                originalCalls++;
                WriteCurrentOpcode(fixture, FieldOpcodeAddressResolver.OpcodeMessageIndex);
            },
            () => Timestamp,
            queue);

        ingress.OnCallback();

        Equal(1, originalCalls, "WAIT original invoked exactly once");
        Equal(true, queue.TryDequeue(out var snapshot), "WAIT snapshot published after original");
        Equal(1L, snapshot.Sequence, "WAIT snapshot sequence");
        Equal(Timestamp, snapshot.TimestampUtc, "WAIT snapshot UTC timestamp");
        Equal(
            new FieldScriptContext(116, 0, 0, 160, FieldOpcodeAddressResolver.OpcodeWaitIndex),
            snapshot.Context,
            "WAIT snapshot preserves pre-original context");
        Equal(false, queue.TryDequeue(out _), "WAIT ingress publishes one snapshot");

        ingress.OnCallback();
        Equal(2, originalCalls, "wrong-opcode callback still invokes original exactly once");
        Equal(false, queue.TryDequeue(out _), "wrong opcode fails closed without publication");
    }

    private static void SoundIngressCopiesContextBeforeCallingTheOriginalExactlyOnce(
        Steam2026FingerprintResult supportedRuntime)
    {
        var fixture = CreateSoundFixture();
        var contract = CreateExactContract(fixture, supportedRuntime);
        var queue = new BoundedNativeIngressQueue<Steam2026FieldCutsceneIngressSnapshot>(4);
        var originalCalls = 0;
        using var ingress = new Steam2026FieldCutsceneDetourIngressCoordinator(
            contract,
            Steam2026FieldCutsceneCallbackKind.Sound,
            () =>
            {
                originalCalls++;
                WriteCurrentOpcode(fixture, FieldOpcodeAddressResolver.OpcodeMessageIndex);
            },
            () => Timestamp,
            queue);

        ingress.OnCallback();

        Equal(1, originalCalls, "SOUND original invoked exactly once");
        Equal(true, queue.TryDequeue(out var snapshot), "SOUND snapshot published after original");
        Equal(1L, snapshot.Sequence, "SOUND snapshot sequence");
        Equal(Timestamp, snapshot.TimestampUtc, "SOUND snapshot UTC timestamp");
        Equal(
            new FieldScriptContext(
                116,
                0,
                0,
                160,
                FieldOpcodeAddressResolver.OpcodeSoundIndex),
            snapshot.Context,
            "SOUND snapshot preserves pre-original context");
        Equal(false, queue.TryDequeue(out _), "SOUND ingress publishes one snapshot");

        ingress.OnCallback();
        Equal(2, originalCalls, "wrong-opcode SOUND callback still invokes original exactly once");
        Equal(false, queue.TryDequeue(out _), "wrong SOUND opcode fails closed without publication");
    }

    private static void IngressContainsOriginalAndQueueFailures(
        Steam2026FingerprintResult supportedRuntime)
    {
        var originalFixture = CreateWaitFixture();
        var originalContract = CreateExactContract(originalFixture, supportedRuntime);
        var queue = new BoundedNativeIngressQueue<Steam2026FieldCutsceneIngressSnapshot>(2);
        var originalCalls = 0;
        using var originalFailure = new Steam2026FieldCutsceneDetourIngressCoordinator(
            originalContract,
            Steam2026FieldCutsceneCallbackKind.Wait,
            () =>
            {
                originalCalls++;
                throw new InvalidOperationException("native wrapper failure");
            },
            () => Timestamp,
            queue);

        originalFailure.OnCallback();
        originalFailure.OnCallback();
        Equal(2, originalCalls, "degraded WAIT ingress keeps every native original callable");
        Equal(true, originalFailure.IsFatallyDegraded, "WAIT original failure permanently degrades ingress");
        Equal(false, queue.TryDequeue(out _), "failed WAIT original publishes nothing");

        var queueFixture = CreateWaitFixture();
        var queueContract = CreateExactContract(queueFixture, supportedRuntime);
        var rejectingQueue = new RejectingIngressQueue();
        var queueOriginalCalls = 0;
        using var queueFailure = new Steam2026FieldCutsceneDetourIngressCoordinator(
            queueContract,
            Steam2026FieldCutsceneCallbackKind.Wait,
            () => queueOriginalCalls++,
            () => Timestamp,
            rejectingQueue);
        queueFailure.OnCallback();
        Equal(1, queueOriginalCalls, "WAIT original runs before rejected publication");
        Equal(1, rejectingQueue.Attempts, "WAIT snapshot gets one nonblocking publication attempt");
        Equal(true, queueFailure.IsFatallyDegraded, "WAIT queue rejection permanently degrades ingress");
    }

    private static void DescriptionSpeechRetriesWithoutLosingTheCue()
    {
        var fixture = CreateWaitFixture();
        var coordinator = new Steam2026FieldCutsceneDescriptionCoordinator(fixture.Direct);
        Equal(true, coordinator.Observe(CreateOpeningSnapshot()), "opening WAIT cue queued");

        var attempts = 0;
        bool TrySpeak(string text)
        {
            attempts++;
            Equal(
                "A train pulls into the station beside a metal platform under green industrial light.",
                text,
                "opening WAIT narration text");
            if (attempts == 1)
            {
                throw new InvalidOperationException("Prism unavailable");
            }

            return attempts >= 3;
        }

        Equal(
            false,
            coordinator.TrySpeakPending(true, () => false, TrySpeak, Timestamp, out _),
            "thrown WAIT narration output remains pending");
        Equal(
            false,
            coordinator.TrySpeakPending(true, () => false, TrySpeak, Timestamp, out _),
            "rejected WAIT narration output remains pending");
        Equal(
            true,
            coordinator.TrySpeakPending(true, () => false, TrySpeak, Timestamp, out var spoken),
            "WAIT narration retries until output accepts it");
        Equal(3, attempts, "WAIT narration output attempt count");
        Equal(116, spoken.FieldId, "spoken WAIT cue field");
        Equal(
            true,
            coordinator.ShouldQueueDialogue(116, Timestamp.AddMilliseconds(1)),
            "successful WAIT narration protects itself from dialogue interruption");
        Equal(
            false,
            coordinator.ShouldQueueDialogue(117, Timestamp.AddMilliseconds(1)),
            "WAIT narration protection remains scoped to its field");
        Equal(
            false,
            coordinator.TrySpeakPending(true, () => false, TrySpeak, Timestamp, out _),
            "accepted WAIT narration dequeues exactly once");
        Equal(3, attempts, "empty WAIT queue does not invoke output");
    }

    private static void DescriptionSpeechDefersForReadableDialogueAndFailsClosed()
    {
        var fixture = CreateWaitFixture();
        var coordinator = new Steam2026FieldCutsceneDescriptionCoordinator(fixture.Direct);
        Equal(true, coordinator.Observe(CreateOpeningSnapshot()), "dialogue-gated WAIT cue queued");
        fixture.Write(
            (uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount,
            [1]);
        var speechCalls = 0;
        Equal(
            false,
            coordinator.TrySpeakPending(
                true,
                () => true,
                _ =>
                {
                    speechCalls++;
                    return true;
                },
                Timestamp,
                out _),
            "readable active dialogue defers WAIT narration");
        Equal(0, speechCalls, "deferred WAIT narration never reaches output");

        fixture.Write(
            (uint)FieldAudibleCueStateReader.AddressActiveFieldMessageCount,
            [0]);
        Equal(
            false,
            coordinator.TrySpeakPending(
                false,
                () => false,
                _ =>
                {
                    speechCalls++;
                    return true;
                },
                Timestamp,
                out _),
            "background WAIT narration remains pending");
        Equal(0, speechCalls, "background WAIT narration never reaches output");
        Equal(
            true,
            coordinator.TrySpeakPending(
                true,
                () => false,
                _ =>
                {
                    speechCalls++;
                    return true;
                },
                Timestamp,
                out _),
            "WAIT narration resumes when foreground and dialogue gates are clear");
        Equal(1, speechCalls, "resumed WAIT narration reaches output once");

        var invalid = new Steam2026FieldCutsceneDescriptionCoordinator(fixture.Direct);
        Equal(
            false,
            invalid.Observe(CreateOpeningSnapshot() with
            {
                Context = CreateOpeningSnapshot().Context with
                {
                    Opcode = FieldOpcodeAddressResolver.OpcodeMessageIndex
                }
            }),
            "non-WAIT snapshot is rejected");
        Equal(
            false,
            invalid.TrySpeakPending(true, () => false, _ => true, Timestamp, out _),
            "rejected non-WAIT snapshot cannot speak");
    }

    private static void DescriptionSpeechAcceptsExactSoundAndRejectsMessage()
    {
        var fixture = CreateSoundFixture();
        var cue = new FieldCutsceneDescriptionCue(
            116,
            0,
            0,
            160,
            "A warning siren echoes through the reactor.",
            FieldOpcodeAddressResolver.OpcodeSoundIndex);
        var coordinator = new Steam2026FieldCutsceneDescriptionCoordinator(
            fixture.Direct,
            [cue]);
        var soundSnapshot = new Steam2026FieldCutsceneIngressSnapshot(
            1,
            Timestamp,
            new FieldScriptContext(
                116,
                0,
                0,
                160,
                FieldOpcodeAddressResolver.OpcodeSoundIndex));

        Equal(true, coordinator.Observe(soundSnapshot), "exact SOUND cue queued");
        Equal(
            true,
            coordinator.TrySpeakPending(
                true,
                () => false,
                text => text == cue.Text,
                Timestamp,
                out var spoken),
            "exact SOUND cue reaches speech");
        Equal(cue, spoken, "spoken SOUND cue preserves exact identity");

        var unsupported = new Steam2026FieldCutsceneDescriptionCoordinator(
            fixture.Direct,
            [cue]);
        Equal(
            false,
            unsupported.Observe(soundSnapshot with
            {
                Context = soundSnapshot.Context with
                {
                    Opcode = FieldOpcodeAddressResolver.OpcodeMessageIndex
                }
            }),
            "MESSAGE snapshot remains outside cutscene ingress");
        Equal(
            false,
            unsupported.Observe(soundSnapshot with
            {
                Context = soundSnapshot.Context with { ByteIndex = 161 }
            }),
            "SOUND snapshot must match the cue's full script context");
    }

    private static void HookSetOwnsCatalogDrivenProvenDetours()
    {
        var fields = typeof(Steam2026FieldCutsceneHookSet)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        Equal(
            1,
            fields.Count(field => field.FieldType == typeof(Dictionary<
                Steam2026FieldCutsceneCallbackKind,
                TranslatedFieldCutsceneCallbackOriginal>)),
            "cutscene hook set retains one delegate per catalog identity");
        Equal(
            1,
            fields.Count(field => field.FieldType == typeof(Dictionary<
                Steam2026FieldCutsceneCallbackKind,
                IHook<TranslatedFieldCutsceneCallbackOriginal>>)),
            "cutscene hook set owns one proven hook per catalog identity");
        Equal(
            1,
            fields.Count(field => field.FieldType == typeof(Dictionary<
                Steam2026FieldCutsceneCallbackKind,
                Steam2026FieldCutsceneDetourIngressCoordinator>)),
            "cutscene hook set owns one ingress coordinator per catalog identity");
    }

    private static FieldObservationFixture CreateWaitFixture() =>
        CreateFixture(FieldOpcodeAddressResolver.OpcodeWaitIndex);

    private static FieldObservationFixture CreateSoundFixture() =>
        CreateFixture(FieldOpcodeAddressResolver.OpcodeSoundIndex);

    private static FieldObservationFixture CreateFixture(int opcode)
    {
        var fixture = FieldObservationFixture.CreatePopulated();
        foreach (var kind in Enum.GetValues<Steam2026FieldCutsceneCallbackKind>())
        {
            var metadata = Steam2026FieldCutsceneCallbackCatalog.GetMetadata(kind);
            fixture.Native.MapRegion(
                FieldObservationFixture.ModuleBase + metadata.FunctionMap.HostRva,
                0x1000,
                FieldObservationFixture.ModuleBase,
                isCommitted: true,
                isExecutable: true);
            fixture.Native.Write(
                FieldObservationFixture.ModuleBase + metadata.FunctionMap.MappingRecordRva,
                BitConverter.GetBytes((ulong)metadata.FunctionMap.LegacyVirtualAddress));
            fixture.Native.Write(
                FieldObservationFixture.ModuleBase
                + metadata.FunctionMap.MappingRecordRva
                + sizeof(ulong),
                BitConverter.GetBytes(
                    FieldObservationFixture.ModuleBase + metadata.FunctionMap.HostRva));
            fixture.Native.Write(
                FieldObservationFixture.ModuleBase + metadata.FunctionMap.HostRva,
                Convert.FromHexString(metadata.FunctionMap.ExpectedPrefixHex));
        }

        fixture.Write((uint)FieldScriptContextReader.AddressCurrentEntityId, [0]);
        fixture.Write((uint)FieldScriptContextReader.AddressCurrentEntityScriptPriority, [0]);
        fixture.Write((uint)FieldScriptContextReader.AddressCurrentEntityScriptId, [0]);
        fixture.Write(
            (uint)FieldScriptContextReader.AddressFieldCurrScriptPosition,
            BitConverter.GetBytes((ushort)0x0120));
        var scriptOffsetTable = FieldObservationFixture.ScriptPointer
                                + 16
                                + FieldScriptContextReader.ScriptOffsetTableHeaderSize
                                + 16;
        fixture.Write(scriptOffsetTable, BitConverter.GetBytes((ushort)0x0080));
        WriteCurrentOpcode(fixture, opcode);
        return fixture;
    }

    private static void WriteCurrentOpcode(FieldObservationFixture fixture, int opcode) =>
        fixture.Write(FieldObservationFixture.ScriptPointer + 0x0120, [checked((byte)opcode)]);

    private static Steam2026FieldCutsceneCallbackContract CreateExactContract(
        FieldObservationFixture fixture,
        Steam2026FingerprintResult supportedRuntime) =>
        new(
            supportedRuntime,
            FieldObservationFixture.ModuleBase,
            ModuleImageSize,
            fixture.Native);

    private static FieldScriptContext ReadWait(
        Steam2026FieldCutsceneCallbackContract contract,
        Steam2026FieldCutsceneCallbackIdentity identity) =>
        ReadContext(contract, identity, "WAIT");

    private static FieldScriptContext ReadContext(
        Steam2026FieldCutsceneCallbackContract contract,
        Steam2026FieldCutsceneCallbackIdentity identity,
        string label)
    {
        Equal(
            true,
            contract.TryCaptureContext(identity, out var context),
            $"checked {label} capture succeeds");
        return context;
    }

    private static Steam2026FieldCutsceneIngressSnapshot CreateOpeningSnapshot() =>
        new(
            1,
            Timestamp,
            new FieldScriptContext(
                116,
                0,
                0,
                160,
                FieldOpcodeAddressResolver.OpcodeWaitIndex));

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void AssertPointerFree(Type type, string label) =>
        AssertPointerFree(type, label, []);

    private static void AssertPointerFree(
        Type type,
        string label,
        HashSet<Type> visited)
    {
        Equal(false, type.IsPointer, $"{label} has no pointer type");
        Equal(false, type == typeof(IntPtr), $"{label} has no native signed pointer");
        Equal(false, type == typeof(UIntPtr), $"{label} has no native unsigned pointer");
        Equal(true, type.IsValueType, $"{label} contains only copied value state");
        if (!visited.Add(type) || type.IsPrimitive || type.IsEnum)
        {
            return;
        }

        foreach (var field in type.GetFields(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            AssertPointerFree(field.FieldType, $"{label}.{field.Name}", visited);
        }
    }

    private sealed class RejectingIngressQueue :
        INativeIngressQueue<Steam2026FieldCutsceneIngressSnapshot>
    {
        public int Attempts { get; private set; }

        public bool TryEnqueue(Steam2026FieldCutsceneIngressSnapshot item)
        {
            Attempts++;
            return false;
        }
    }
}

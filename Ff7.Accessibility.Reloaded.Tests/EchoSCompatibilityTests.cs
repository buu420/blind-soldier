using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.LegacyLayout;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class EchoSCompatibilityTests
{
    internal static void Run()
    {
        ReadsEveryScriptInAFieldAsOneCoherentSnapshot();
        ReadsCoherentLoadedFieldScriptIdentity();
        SelectsOnlyExactSupportedDescriptionVariants();
        ResolvesOnlyFingerprintBoundDisclaimerText();
        QueuesDisclaimerPagesAcrossTheIdentityRace();
        RestoresOnlyTheExactEchoSReactorTimer();
        CorrelatesOnlyExactSuccessfulFfnxVoicePlayback();
        KeepsOpeningProbeAliveAcrossValidatedEchoSDisclaimer();
        RejectsUnvalidatedButAcceptsValidatedDisclaimerFirst();
        ResolvesEchoSOpeningPotionsAsIndependentGuards();
    }

    private static void ReadsCoherentLoadedFieldScriptIdentity()
    {
        const uint scriptPointer = 0x02000000;
        var memory = new ScriptIdentityMemory();
        var script = new byte[64];
        BinaryPrimitives.WriteUInt16LittleEndian(script, 0x0301);
        script[2] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(script.AsSpan(4), 48);
        for (var index = 8; index < 48; index++)
        {
            script[index] = (byte)(index * 3);
        }

        memory.Write((uint)FieldScriptContextReader.AddressCurrentModule, FieldPositionReader.FieldModule);
        memory.WriteUInt16((uint)FieldScriptContextReader.AddressCurrentFieldId, 116);
        memory.WriteUInt32((uint)FieldScriptContextReader.AddressFieldScriptPtr, scriptPointer);
        memory.Write(scriptPointer, script);

        var reader = new LoadedFieldScriptIdentityReader(memory);
        Equal(true, reader.TryRead(out var identity), "loaded script identity read");
        Equal(116, identity.FieldId, "loaded script field");
        Equal(scriptPointer, identity.ScriptPointer, "loaded script pointer");
        Equal(
            Convert.ToHexString(SHA256.HashData(script.AsSpan(0, 48))),
            identity.ScriptPrefixSha256,
            "loaded script prefix fingerprint");

        memory.TearAddress = scriptPointer + 20;
        Equal(false, reader.TryRead(out _), "torn loaded script rejected");
    }

    private static void SelectsOnlyExactSupportedDescriptionVariants()
    {
        var tracker = new EchoSFieldCutsceneDescriptionTracker();
        var echoIdentity = new LoadedFieldScriptIdentity(
            116,
            0x02000000,
            EchoSCompatibilityManifest.GetEchoFingerprint(116)!);
        var echoCue = tracker.Observe(new FieldScriptContext(116, 0, 0, 205, 0x24), echoIdentity);
        Equal(
            FieldCutsceneDescriptionCatalog.CreateOpeningTrainArrival()[0].Text,
            echoCue?.Text,
            "Echo-S cue keeps canonical description text");
        Equal(null, tracker.Observe(new FieldScriptContext(116, 0, 0, 205, 0x24), echoIdentity), "Echo-S cue speaks once");

        tracker.Reset();
        var vanillaIdentity = new LoadedFieldScriptIdentity(
            116,
            0x02000000,
            EchoSCompatibilityManifest.GetVanillaFingerprint(116)!);
        Equal(
            true,
            tracker.Observe(new FieldScriptContext(116, 0, 0, 160, 0x24), vanillaIdentity) is not null,
            "vanilla cue preserved");

        tracker.Reset();
        var unknownIdentity = new LoadedFieldScriptIdentity(116, 0x02000000, new string('0', 64));
        Equal(null, tracker.Observe(new FieldScriptContext(116, 0, 0, 205, 0x24), unknownIdentity), "unknown script rejected");
        Equal(null, tracker.Observe(new FieldScriptContext(116, 0, 0, 206, 0x24), echoIdentity), "nearby Echo-S offset rejected");

        tracker.Reset();
        var alternateUiIdentity = new LoadedFieldScriptIdentity(
            116,
            0x02000000,
            "AC918E9D3752DAAB116A4A384BC9E7D6BA1DD5185CA558E07E1C6ED9F343F5C5");
        Equal(
            true,
            tracker.Observe(new FieldScriptContext(116, 0, 0, 205, 0x24), alternateUiIdentity) is not null,
            "validated Echo-S UI variant uses the exact shared cue map");
    }

    private static void ResolvesOnlyFingerprintBoundDisclaimerText()
    {
        var supported = new LoadedFieldScriptIdentity(
            109,
            0x02000000,
            EchoSCompatibilityManifest.EchoS124DisclaimerFingerprint);
        Equal(true, EchoSCompatibilityManifest.IsSupportedDisclaimer(supported), "supported disclaimer identity");
        Equal("Welcome to Project Echo-S.", EchoSCompatibilityManifest.ResolveDisclaimerText(supported, 1), "disclaimer page one");
        Equal(null, EchoSCompatibilityManifest.ResolveDisclaimerText(supported, 5), "unknown disclaimer page");
        Equal(
            "Welcome to Project Echo-S. Press confirm to continue.",
            EchoSCompatibilityManifest.ResolveDisclaimerSpeechText(supported, 1),
            "disclaimer page one has an actionable prompt");

        var alternateUi = supported with
        {
            ScriptPrefixSha256 = "0EE70B724A9F19F675688EB616BA0A24345CB74C81EDD82A6FB03F56AFB9B6C2"
        };
        Equal(true, EchoSCompatibilityManifest.IsSupportedDisclaimer(alternateUi), "supported UI disclaimer identity");

        var unsupported = supported with { ScriptPrefixSha256 = new string('F', 64) };
        Equal(false, EchoSCompatibilityManifest.IsSupportedDisclaimer(unsupported), "unsupported disclaimer identity");
        Equal(null, EchoSCompatibilityManifest.ResolveDisclaimerText(unsupported, 1), "unsupported disclaimer text rejected");
        Equal(null, EchoSCompatibilityManifest.ResolveDisclaimerSpeechText(unsupported, 1), "unsupported disclaimer speech rejected");
    }

    private static void QueuesDisclaimerPagesAcrossTheIdentityRace()
    {
        var tracker = new EchoSDisclaimerSpeechTracker();
        var supported = new LoadedFieldScriptIdentity(
            109,
            0x02000000,
            EchoSCompatibilityManifest.EchoS124DisclaimerFingerprint);

        Equal(true, tracker.Queue(109, 1), "first disclaimer queued before identity");
        Equal(false, tracker.Queue(109, 1), "duplicate pending disclaimer rejected");
        Equal(false, tracker.Queue(110, 2), "wrong field disclaimer rejected");
        Equal(false, tracker.Queue(109, 5), "out-of-range disclaimer rejected");

        var first = tracker.TryResolve(supported);
        Equal(1, first?.MessageId, "first pending disclaimer resolved after identity");
        Equal(
            "Welcome to Project Echo-S. Press confirm to continue.",
            first?.Text,
            "resolved disclaimer includes confirm instruction");
        tracker.Acknowledge(first!.Value, delivered: false);
        Equal(first, tracker.TryResolve(supported), "failed disclaimer speech remains pending");
        tracker.Acknowledge(first.Value, delivered: true);
        Equal(null, tracker.TryResolve(supported), "delivered disclaimer removed");
        Equal(true, tracker.OwnsVisibleSpeech(supported), "delivered exact page owns visible speech");

        for (var messageId = 2; messageId <= 4; messageId++)
        {
            Equal(true, tracker.Queue(109, messageId), $"disclaimer page {messageId} queued");
            var candidate = tracker.TryResolve(supported);
            Equal(messageId, candidate?.MessageId, $"disclaimer page {messageId} resolved");
            Equal(true, candidate?.Text.EndsWith("Press confirm to continue.", StringComparison.Ordinal), $"page {messageId} prompt");
            tracker.Acknowledge(candidate!.Value, delivered: true);
        }

        var unsupported = supported with { ScriptPrefixSha256 = new string('F', 64) };
        tracker.Reset();
        Equal(true, tracker.Queue(109, 1), "unsupported lifecycle candidate queued without guessing");
        Equal(null, tracker.TryResolve(unsupported), "unsupported lifecycle remains silent");
        Equal(1, tracker.TryResolve(supported)?.MessageId, "candidate survives transient identity mismatch");

        tracker.ObserveLifecycle(new LoadedFieldScriptIdentity(116, 0x03000000, new string('0', 64)));
        Equal(null, tracker.TryResolve(supported), "leaving disclaimer field clears stale pending pages");
        tracker.Reset();
        Equal(true, tracker.Queue(109, 1), "reset permits a new disclaimer lifecycle");
    }

    private static void RestoresOnlyTheExactEchoSReactorTimer()
    {
        var baseIdentity = new LoadedFieldScriptIdentity(
            125,
            0x03000000,
            EchoSCompatibilityManifest.GetEchoFingerprint(125)!);
        var alternateIdentity = baseIdentity with
        {
            ScriptPointer = 0x04000000,
            ScriptPrefixSha256 = "1DF9D7ECC91F519364A25DAC22F56C134BFC7A2850126188081704E6D5EAE972"
        };
        var firstEchoSet = new FieldScriptContext(125, 1, 0, 0x89, FieldOpcodeAddressResolver.OpcodeTimerIndex);
        var alternateEchoSet = new FieldScriptContext(125, 1, 0, 0x91, FieldOpcodeAddressResolver.OpcodeTimerIndex);
        var tracker = new EchoSReactorTimerOverrideTracker();

        Equal(true, tracker.Queue(firstEchoSet), "first Echo-S timer instruction queued");
        var firstDecision = tracker.TryResolve(baseIdentity);
        Equal(600, firstDecision?.Seconds, "Echo-S timer restored to ten minutes");
        Equal(EchoSReactorTimerOverrideTracker.NativeCountdownAddress, firstDecision?.Address, "native countdown destination");
        tracker.Acknowledge(firstDecision!.Value, applied: false);
        Equal(firstDecision, tracker.TryResolve(baseIdentity), "failed timer write retries");
        tracker.Acknowledge(firstDecision.Value, applied: true);
        Equal(null, tracker.TryResolve(baseIdentity), "successful timer write applies once per loaded script");

        tracker.Reset();
        Equal(true, tracker.Queue(alternateEchoSet), "alternate Echo-S timer instruction queued");
        Equal(600, tracker.TryResolve(alternateIdentity)?.Seconds, "alternate UI fingerprint supported");

        tracker.Reset();
        Equal(false, tracker.Queue(firstEchoSet with { ByteIndex = 0x88 }), "nearby timer offset rejected");
        Equal(false, tracker.Queue(firstEchoSet with { ByteIndex = 0x11E }), "vanilla timer offset rejected");
        Equal(false, tracker.Queue(firstEchoSet with { FieldId = 124 }), "wrong timer field rejected");
        Equal(false, tracker.Queue(firstEchoSet with { EntityId = 0 }), "wrong timer entity rejected");
        Equal(false, tracker.Queue(firstEchoSet with { ScriptId = 1 }), "wrong timer script rejected");
        Equal(false, tracker.Queue(firstEchoSet with { Opcode = 0x39 }), "wrong timer opcode rejected");

        Equal(true, tracker.Queue(firstEchoSet), "exact timer candidate re-queued");
        var vanillaIdentity = baseIdentity with
        {
            ScriptPrefixSha256 = EchoSCompatibilityManifest.GetVanillaFingerprint(125)!
        };
        var unknownIdentity = baseIdentity with { ScriptPrefixSha256 = new string('A', 64) };
        Equal(null, tracker.TryResolve(vanillaIdentity), "vanilla field never overridden");
        Equal(null, tracker.TryResolve(unknownIdentity), "unknown field never overridden");
        Equal(600, tracker.TryResolve(baseIdentity)?.Seconds, "candidate survives identity-read race until exact Echo-S identity");
    }

    private static unsafe void CorrelatesOnlyExactSuccessfulFfnxVoicePlayback()
    {
        var queue = new FfnxVoicePlaybackEventQueue(capacity: 1, maxFieldNameBytes: 16);
        var fieldName = stackalloc byte[] { (byte)'m', (byte)'d', (byte)'1', (byte)'s', (byte)'t', (byte)'i', (byte)'n', 0 };
        Equal(true, queue.TryCapture(fieldName, 2, 17, 0, played: true, timestamp: 100), "FFNx voice captured");
        Equal(false, queue.TryCapture(fieldName, 2, 18, 0, played: true, timestamp: 101), "FFNx voice queue bounded");
        Equal(true, queue.TryDequeue(out var voice), "FFNx voice dequeued");
        Equal("md1stin", voice.FieldName, "FFNx voice field name");

        var tracker = new FfnxVoicePlaybackTracker(TimeSpan.FromSeconds(5), timestampFrequency: 1000);
        tracker.ObserveVoice(voice);
        tracker.ObserveMessage(116, 2, 17, timestamp: 120);
        Equal(true, tracker.ShouldSuppressPrism(116, 2, timestamp: 130), "matching played voice owns dialogue");
        Equal(false, tracker.ShouldSuppressPrism(116, 3, timestamp: 130), "different window not suppressed");

        tracker.ObserveMessage(116, 2, 18, timestamp: 140);
        Equal(false, tracker.ShouldSuppressPrism(116, 2, timestamp: 150), "unmatched message remains Prism fallback");
        tracker.ObserveVoice(voice with { DialogId = 18, Played = false, Timestamp = 145 });
        Equal(false, tracker.ShouldSuppressPrism(116, 2, timestamp: 150), "failed voice remains Prism fallback");
        tracker.ObserveVoice(voice with { DialogId = 18, Played = true, Timestamp = 146 });
        Equal(true, tracker.ShouldSuppressPrism(116, 2, timestamp: 150), "late matching voice owns dialogue");
        Equal(false, tracker.ShouldSuppressPrism(116, 2, timestamp: 6000), "stale voice expires");
    }

    private static void KeepsOpeningProbeAliveAcrossValidatedEchoSDisclaimer()
    {
        var lifetime = new OpeningMovieProbeLifetime();
        lifetime.Observe(1, 116, movieDetected: false, movieFileActive: false);
        lifetime.Observe(
            1,
            109,
            movieDetected: false,
            movieFileActive: false,
            isSupportedEchoSDisclaimerField: false);
        Equal(true, lifetime.ShouldProbe, "probe while Echo-S disclaimer identity is transitioning");
        lifetime.Observe(
            1,
            109,
            movieDetected: false,
            movieFileActive: false,
            isSupportedEchoSDisclaimerField: true);
        lifetime.Observe(5, 109, movieDetected: false, movieFileActive: false);
        lifetime.Observe(1, 116, movieDetected: false, movieFileActive: false);
        lifetime.Observe(1, 116, movieDetected: true, movieFileActive: true);

        Equal(true, lifetime.ShouldProbe, "probe during Echo-S opening movie");
        lifetime.Observe(1, 116, movieDetected: true, movieFileActive: false);
        Equal(false, lifetime.ShouldProbe, "probe after Echo-S opening movie closes");
    }

    private static void RejectsUnvalidatedButAcceptsValidatedDisclaimerFirst()
    {
        var unvalidated = new OpeningMovieProbeLifetime();
        unvalidated.Observe(1, 116, movieDetected: false, movieFileActive: false);
        unvalidated.Observe(1, 109, movieDetected: false, movieFileActive: false);
        Equal(true, unvalidated.ShouldProbe, "transient field 109 identity miss receives a bounded grace period");
        for (var attempt = 0; attempt < 64; attempt++)
        {
            unvalidated.Observe(1, 109, movieDetected: false, movieFileActive: false);
        }
        Equal(false, unvalidated.ShouldProbe, "persistently unvalidated field 109 stops probing");

        var validatedDisclaimerFirst = new OpeningMovieProbeLifetime();
        validatedDisclaimerFirst.Observe(
            1,
            109,
            movieDetected: false,
            movieFileActive: false,
            isSupportedEchoSDisclaimerField: true);
        Equal(true, validatedDisclaimerFirst.ShouldProbe, "validated Echo-S disclaimer may precede opening field");
        validatedDisclaimerFirst.Observe(1, 116, movieDetected: false, movieFileActive: false);
        validatedDisclaimerFirst.Observe(1, 116, movieDetected: true, movieFileActive: true);
        Equal(true, validatedDisclaimerFirst.ShouldProbe, "opening movie remains observable after disclaimer-first sequence");
    }

    private static void ResolvesEchoSOpeningPotionsAsIndependentGuards()
    {
        var echoIdentity = new LoadedFieldScriptIdentity(
            116,
            0x02000000,
            "A5860AB734603DD6A62F9DD2CB262EB1E4F38A0A9B6ECFF09171F3CB6CB84D97");
        var vanillaIdentity = new LoadedFieldScriptIdentity(
            116,
            0x02000000,
            "18E7F7E7DD47A52C98255001DA76578D335AF5F4CA238ADA81FCD5EC6FEB6FA2");
        var firstGuard = new FieldNavigationObjectDefinition(
            116,
            9,
            FieldNavigationObjectKind.Item,
            NativeId: 0,
            CollectedBank: 15,
            CollectedAddress: 32,
            CollectedMask: 3);
        var secondGuard = firstGuard with { EntityId = 10 };

        Equal((byte)1, EchoSCompatibilityManifest.ResolveObjectCollectedMask(echoIdentity, firstGuard), "Echo-S first guard potion bit");
        Equal((byte)2, EchoSCompatibilityManifest.ResolveObjectCollectedMask(echoIdentity, secondGuard), "Echo-S second guard potion bit");
        Equal((byte)3, EchoSCompatibilityManifest.ResolveObjectCollectedMask(vanillaIdentity, firstGuard), "vanilla shared potion state remains unchanged");
    }

    private static void ReadsEveryScriptInAFieldAsOneCoherentSnapshot()
    {
        var catalog = new FieldScriptNavigationCatalog(
            @"X:\SteamLibrary\steamapps\common\FINAL FANTASY VII Steam Edition\ff7\workingdir");
        var scripts = catalog.ReadAllScriptOpcodes(116);

        Equal(true, scripts.Count > 1, "opening field script population");
        Equal(
            true,
            scripts.Any(script =>
                script.EntityId == 0 &&
                script.ScriptId == 0 &&
                script.Opcodes.Any(opcode => opcode.ByteIndex == 160 && opcode.Opcode == 0x24)),
            "opening description opcode in aggregate script read");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private sealed class ScriptIdentityMemory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = new();
        private bool torn;

        internal uint? TearAddress { get; set; }

        internal void Write(uint address, byte value) => bytes[address] = value;

        internal void Write(uint address, ReadOnlySpan<byte> value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                bytes[address + (uint)index] = value[index];
            }
        }

        internal void WriteUInt16(uint address, ushort value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            Write(address, buffer);
        }

        internal void WriteUInt32(uint address, uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            Write(address, buffer);
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                var address = virtualAddress + (uint)index;
                if (!bytes.TryGetValue(address, out destination[index]))
                {
                    return false;
                }

                if (!torn && TearAddress == address)
                {
                    bytes[address]++;
                    torn = true;
                }
            }

            return true;
        }
    }
}

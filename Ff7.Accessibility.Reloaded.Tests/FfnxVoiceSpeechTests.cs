using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class FfnxVoiceSpeechTests
{
    public static void Run()
    {
        VoicedDialogueDoesNotNeedARecognizedDescriptionScript();
        UnvoicedDialogueAndChoiceFallbackRemainReadable();
        VoicedDialoguePreferenceAndDrawFallbackAgree();
        VerifiedReleaseProfilesKeepTheirOwnAddresses();
        ParallelWindowsAndFieldTransitionsKeepTheirOwnVoice();
    }

    private static void VoicedDialogueDoesNotNeedARecognizedDescriptionScript()
    {
        // A save loaded directly into field 214 has no prior Echo-S description
        // fingerprint. FFNx's successful playback is sufficient voice evidence.
        var voice = new FfnxVoicePlaybackTracker(TimeSpan.FromSeconds(60), 1000);
        voice.ObserveMessage(214, 0, 2, 100);
        voice.ObserveVoice(new("onna_1", 0, 2, 0, true, 110));
        var speech = Poll(voice, 214, 0);
        Equal(0, speech.Count, "a voiced message must not reach Prism before a recognized script loads");
    }

    private static void UnvoicedDialogueAndChoiceFallbackRemainReadable()
    {
        var voice = new FfnxVoicePlaybackTracker(TimeSpan.FromSeconds(60), 1000);
        voice.ObserveMessage(214, 0, 2, 100);
        Equal(1, Poll(voice, 214, 0).Count, "missing playback evidence preserves dialogue");

        voice.ObserveVoice(new("onna_1", 0, 2, 0, false, 110));
        Equal(1, Poll(voice, 214, 0).Count, "a missing voice file preserves dialogue");

        voice.ObserveVoice(new("onna_1", 0, 2, 0, true, 110));
        Equal(1, Poll(voice, 214, 0, verifiedVoiceHookInstalled: false).Count,
            "an unavailable verified hook preserves the fallback");
        Equal(1, Poll(voice, 214, 0, hasActiveAsk: true).Count,
            "voice ownership does not discard choices and their prompts");
        Equal(1, Poll(voice, 214, 1).Count, "a different visible window remains readable");
        Equal(1, Poll(voice, 218, 0).Count, "a different current field remains readable");

        voice.ObserveVoice(new("onna_1", 0, 2, 1, false, 120));
        Equal(1, Poll(voice, 214, 0).Count, "an unvoiced next page restores dialogue speech");
    }

    private static IReadOnlyList<FieldVisibleWindowSpeechDispatch> Poll(
        FfnxVoicePlaybackTracker voice,
        int fieldId,
        int windowId,
        bool verifiedVoiceHookInstalled = true,
        bool hasActiveAsk = false)
    {
        var windows = new FieldVisibleWindowSpeechCoordinator(TimeSpan.Zero);
        FieldVisibleWindowSnapshot[] page = [new(windowId, 2, "Cloud: Let's go.", 0x700040)];
        bool Suppress(FieldVisibleWindowSnapshot window) => voice.ShouldSuppressPolling(
            fieldId, window.WindowId, 150, verifiedVoiceHookInstalled, hasActiveAsk);
        windows.Observe(page, 1, DateTime.UnixEpoch, Suppress);
        return windows.Observe(page, 1, DateTime.UnixEpoch.AddTicks(1), Suppress);
    }

    private static void VoicedDialoguePreferenceAndDrawFallbackAgree()
    {
        var voice = new FfnxVoicePlaybackTracker(TimeSpan.FromSeconds(60), 1000);
        voice.ObserveMessage(214, 0, 2, 100);
        voice.ObserveVoice(new("onna_1", 0, 2, 0, true, 110));
        Equal(false, voice.ShouldSuppressPolling(214, 0, 150, true, false, readVoicedDialogue: true),
            "the preference restores recorded dialogue to Prism");
        FieldVisibleWindowSnapshot[] windows = [new(0, 2, "Cloud: Let's go.", 0x700040)];
        Equal(true, voice.ShouldSuppressDraw(214, "Let's go.", windows, 150, true, false, false),
            "the raw drawing fallback must not duplicate a verified voice");
        Equal(false, voice.ShouldSuppressDraw(214, "Let's go.", windows, 150, true, false, true),
            "the drawing fallback honors the read-voiced preference");
        Equal(false, voice.ShouldSuppressDraw(214, "A different line.", windows, 150, true, false, false),
            "unmatched drawings remain readable");
        Equal(false, voice.ShouldSuppressDraw(214, "Let's go.", windows, 150, true, true, false),
            "choice prompts remain readable");
        windows = [.. windows, new(1, 2, "Let's go.", 0x700080)];
        Equal(false, voice.ShouldSuppressDraw(214, "Let's go.", windows, 150, true, false, false),
            "ambiguous unvoiced windows preserve text");
    }

    private static void VerifiedReleaseProfilesKeepTheirOwnAddresses()
    {
        const ulong moduleBase = 0x10000000;
        var memory = new VoiceMemory();
        foreach (var (hash, rva) in new[]
        {
            (FfnxPopupStateReader.SupportedModuleSha256, 0x004187E0u),
            (FfnxVoicePlaybackHookTarget.Ffnx1252Sha256, 0x00442C50u),
        })
        {
            memory.Address = (uint)moduleBase + rva;
            Equal(true, FfnxVoicePlaybackHookTarget.TryResolveModule(hash, moduleBase, memory, out var target, out _),
                "the exact reviewed FFNx release resolves");
            Equal((int)memory.Address, target, "each release uses its own PDB-proven RVA");
            memory.Corrupt = true;
            Equal(false, FfnxVoicePlaybackHookTarget.TryResolveModule(hash, moduleBase, memory, out _, out _),
                "a modified prologue cannot be hooked");
            memory.Corrupt = false;
        }
        Equal(false, FfnxVoicePlaybackHookTarget.TryResolveModule(new string('A', 64), moduleBase,
            memory, out _, out _), "an unknown FFNx build cannot be hooked");
        Equal(false, FfnxVoicePlaybackHookTarget.TryResolveModule(FfnxVoicePlaybackHookTarget.Ffnx1252Sha256,
            ulong.MaxValue, memory, out _, out _), "overflowed module addresses are rejected");
    }

    private sealed class VoiceMemory : Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace
    {
        public uint Address;
        public bool Corrupt;
        public bool TryRead(uint address, Span<byte> destination)
        {
            if (address != Address || destination.Length != 9) return false;
            new byte[] { 0x55, 0x8B, 0xEC, 0x81, 0xEC, 0x0C, 0x01, 0x00, 0x00 }.CopyTo(destination);
            if (Corrupt) destination[0] = 0xE9;
            return true;
        }
    }

    private static void ParallelWindowsAndFieldTransitionsKeepTheirOwnVoice()
    {
        var voice = new FfnxVoicePlaybackTracker(TimeSpan.FromSeconds(60), 1000);
        voice.ObserveVoice(new("mrkt3", 0, 2, 0, true, 110) { FieldId = 214 });
        voice.ObserveMessage(214, 0, 2, 100);
        voice.ObserveMessage(214, 1, 3, 120);
        Equal(true, voice.ShouldSuppressPolling(214, 0, 150, true, false),
            "a second window does not lose the first window's playback identity");
        Equal(false, voice.ShouldSuppressPolling(214, 1, 150, true, false),
            "an unvoiced parallel window remains readable");
        voice.ObserveMessage(218, 0, 2, 200);
        Equal(false, voice.ShouldSuppressPolling(218, 0, 220, true, false),
            "the next field cannot reuse another field's voice event");
        voice.ObserveVoice(new("_world", 0, 2, 0, true, 225) { FieldId = 218 });
        Equal(false, voice.ShouldSuppressPolling(218, 0, 230, true, false),
            "a world voice cannot accidentally suppress a later field message");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, actual {actual}.");
        }
    }
}

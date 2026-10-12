using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class CreatureApproachNarrationCoordinatorTests
{
    private const string Stock = "25A95C3A8CEEB6EC00B6E04089276ED80657816BB41ED4FDA883D8F2EC05825B";
    private const string Echo = "D3FE6A91732C0C5C20DCC962A0BACBE1E6EE79B504077C60F40206272306B87C";
    private static readonly DateTime Start = new(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc);
    internal static void Run()
    {
        var tests = new Action[]
        {
            CallbackCaptureNeverReadsNativeStateOrPrefix,
            FirstObservedNativeEpisodeSpeaksOnlyOnceAfterAcceptance,
            EchoActualOffsetMapsToTheCanonicalNativeCue,
            RefusalAndOutputErrorsRetainTheOwedDescription,
            SpeechGuardsCanDeferWithoutMarkingSpoken,
            CallbackAfterTheWorkerTimestampRemainsPendingWithoutRecapture,
            HiddenOrReplacedNativeGeometryInvalidatesPendingInformation,
            UnknownLocalizedAndChangingPrefixesNeverAuthorizeEnglishOffsets,
            ArbitraryC4AndUnmovedSamplesCannotOpenAnEpisode,
            LaterObservedBonesDoNotExtendTheOriginalEpisodeLifetime,
            FirstEpisodeIsPrioritizedBeforeTheSecond,
            SecondEpisodeHasItsOwnAcceptanceAndDedupe,
            ResetRetiresCallbacksAndAllowsAFreshSession,
            ResetDuringAWorkerGuardPreventsOutput,
            ConcurrentResetDuringNativeValidationRetiresOnlyOldWork,
            ConcurrentCallbackUpdatesKeepOnlyTwoEpisodes,
            AResetDuringAcceptedOutputDoesNotCommitOldSessionHistory,
            PrefixGetterFailureDoesNotEscapeOrCreateSpeech,
            AcceptedDescriptionVisibilityUsesFreshGeometryAndLaterBones,
            ResetAndIdentityReplacementRetireAcceptedVisibility
        };
        var failures = new List<Exception>();
        foreach (var test in tests)
        {
            try { test(); Console.WriteLine($"PASS {test.Method.Name}"); }
            catch (Exception error) { failures.Add(new Exception(test.Method.Name, error)); Console.WriteLine($"FAIL {test.Method.Name}: {error.Message}"); }
        }
        if (failures.Count != 0) throw new AggregateException(failures);
    }
    private static void CallbackCaptureNeverReadsNativeStateOrPrefix()
    {
        var reads = 0;
        var c = new FieldCreatureApproachNarrationCoordinator(_ => { reads++; throw new InvalidOperationException(); }, () => { reads++; throw new InvalidOperationException(); });
        c.Capture(Sample(), Start); True(reads == 0, "draw callbacks store immutable observations without memory or prefix reads");
    }
    private static void FirstObservedNativeEpisodeSpeaksOnlyOnceAfterAcceptance()
    {
        var c = Coordinator(); var words = new List<string>();
        for (var i = 0; i < 26; i++) c.Capture(Sample() with { PolygonSet = 0x180000u + (uint)i * 256 }, Start);
        True(Speak(c, Start, words, out var cue), "the first observed native movement must be delivered");
        Equal(new FieldCutsceneDescriptionKey(767, 12, 3, 12), cue.Key, "canonical held OFSTW cue");
        Equal("The huge creature glides closer.", words.Single(), "first episode wording");
        c.Capture(Sample(), Start.AddSeconds(1)); True(!Speak(c, Start.AddSeconds(1), words, out _), "per-bone and later repeated callbacks do not duplicate an accepted description");
    }
    private static void EchoActualOffsetMapsToTheCanonicalNativeCue()
    { var c = Coordinator(() => new(767, Echo)); c.Capture(Sample() with { Context = new(767, 12, 3, 14, 0xC4) }, Start); True(Speak(c, Start, [], out var cue), "reviewed Echo VISI insertion remains observable"); Equal(12, cue.ByteIndex, "Echo maps to stock semantic cue"); }
    private static void RefusalAndOutputErrorsRetainTheOwedDescription()
    {
        var c = Coordinator(); c.Capture(Sample(), Start); var calls = 0;
        True(!c.TrySpeakPending(Start, _ => true, _ => { calls++; return false; }, out _), "refused speech is not accepted");
        True(!c.TrySpeakPending(Start.AddSeconds(1), _ => true, _ => throw new InvalidOperationException("device failure"), out _), "output failure remains pending");
        True(c.TrySpeakPending(Start.AddSeconds(2), _ => true, _ => { calls++; return true; }, out _), "the same owed cue retries after failure"); Equal(2, calls, "accepted/refused output calls");
    }
    private static void SpeechGuardsCanDeferWithoutMarkingSpoken()
    {
        var c = Coordinator(); c.Capture(Sample(), Start); var called = false;
        True(!c.TrySpeakPending(Start, _ => false, _ => { called = true; return true; }, out _), "busy dialogue defers"); True(!called, "output is not invoked while guarded");
        True(!c.TrySpeakPending(Start, _ => throw new InvalidOperationException(), _ => true, out _), "guard failure cannot escape");
        True(Speak(c, Start.AddSeconds(1), [], out _), "the description remains owed after the guard clears");
    }
    private static void CallbackAfterTheWorkerTimestampRemainsPendingWithoutRecapture()
    {
        var reads = 0; var words = new List<string>();
        var c = Coordinator(() => { reads++; return new(767, Stock); }, _ => { reads++; return true; });
        c.Capture(Sample(), Start.AddMilliseconds(1));
        c.Capture(Sample(4), Start);
        True(!Speak(c, Start, words, out _), "a callback published after the worker timestamp defers the first episode");
        True(reads == 0 && words.Count == 0, "a future first episode defers without native reads or out-of-order output");
        True(Speak(c, Start.AddMilliseconds(50), words, out var first), "the visible observation remains owed without another callback");
        Equal(new FieldCutsceneDescriptionKey(767, 12, 3, 12), first.Key, "the first episode keeps its priority");
        True(Speak(c, Start.AddMilliseconds(50), words, out _), "the second episode follows the retained first episode");
        Equal("The huge creature glides closer.|The creature closes in.", string.Join('|', words), "callback timing preserves native episode order");
    }
    private static void HiddenOrReplacedNativeGeometryInvalidatesPendingInformation()
    {
        var visible = true; var c = Coordinator(current: _ => visible); c.Capture(Sample(), Start); visible = false;
        True(!Speak(c, Start.AddSeconds(1), [], out _), "fresh current-reader denial forbids stale visible information");
        visible = true; True(!Speak(c, Start.AddSeconds(2), [], out _), "a stale capture does not become pending again by itself");
        c.Capture(Sample(), Start.AddSeconds(3)); True(Speak(c, Start.AddSeconds(3), [], out _), "freshly observed visible movement can restore the owed episode within its original lifetime");
        c.Reset(); c.Capture(Sample(), Start); c.Capture(Sample(4) with { HrcModel = 0x300000 }, Start.AddSeconds(1));
        var words = new List<string>(); True(Speak(c, Start.AddSeconds(1), words, out _), "replacement establishes a new current actor generation"); Equal("The creature closes in.", words.Single(), "old actor generation was cleared");
    }
    private static void UnknownLocalizedAndChangingPrefixesNeverAuthorizeEnglishOffsets()
    {
        foreach (var hash in new[] { new string('A', 64), "58AA6DEDCE90CF9F9E9692A197801EB04A0BF10AC56FB4F834A8E72CAD6989CA" })
        { var c = Coordinator(() => new(767, hash)); c.Capture(Sample(), Start); True(!Speak(c, Start, [], out _), "unreviewed English description prefix cannot authorize offsets"); }
        LoadedFieldScriptIdentity? identity = new(767, Stock); var reads = 0;
        var changing = Coordinator(() => { reads++; return reads == 1 ? identity : new(767, Echo); }); changing.Capture(Sample(), Start);
        True(!Speak(changing, Start, [], out _), "prefix changed during worker validation");
        var wrongField = Coordinator(() => new(768, Stock)); wrongField.Capture(Sample(), Start); True(!Speak(wrongField, Start, [], out _), "current field does not match observed field");
    }
    private static void ArbitraryC4AndUnmovedSamplesCannotOpenAnEpisode()
    {
        foreach (var sample in new[] { Sample() with { Context = new(767, 12, 3, 15, 0xC4) }, Sample() with { Context = new(767, 3, 3, 12, 0xC4) },
                     Sample() with { Context = new(767, 12, 3, 12, 0xF1) }, Sample() with { CurrentX = 2574, CurrentY = 2672, CurrentZ = -836, Step = 1 }, Sample() with { MovementPhase = 0 } })
        { var c = Coordinator(); c.Capture(sample, Start); True(!Speak(c, Start, [], out _), "only actually observed reviewed movement is pending"); }
    }
    private static void LaterObservedBonesDoNotExtendTheOriginalEpisodeLifetime()
    {
        var c = Coordinator(); c.Capture(Sample(), Start);
        for (var i = 1; i <= 11; i++) c.Capture(Sample() with { PolygonSet = 0x180000u + (uint)i * 256 }, Start.AddSeconds(i));
        True(!Speak(c, Start.AddSeconds(11), [], out _), "the original ten-second episode lifetime is never extended");
        c.Reset(); c.Capture(Sample(), Start.AddSeconds(12)); True(Speak(c, Start.AddSeconds(12), [], out _), "expiration did not mark the episode spoken in a new session");
    }
    private static void FirstEpisodeIsPrioritizedBeforeTheSecond()
    {
        var c = Coordinator(); c.Capture(Sample(4), Start.AddSeconds(1)); c.Capture(Sample(), Start); var words = new List<string>();
        True(Speak(c, Start.AddSeconds(2), words, out _), "first pending action has priority"); True(Speak(c, Start.AddSeconds(3), words, out _), "second pending action follows");
        Equal("The huge creature glides closer.|The creature closes in.", string.Join('|', words), "native episode order");
    }
    private static void SecondEpisodeHasItsOwnAcceptanceAndDedupe()
    {
        var c = Coordinator(); var words = new List<string>(); c.Capture(Sample(), Start); True(Speak(c, Start, words, out _), "first accepted");
        c.Capture(Sample(4), Start.AddSeconds(2)); True(Speak(c, Start.AddSeconds(2), words, out var second), "second native advance accepted independently");
        Equal(new FieldCutsceneDescriptionKey(767, 12, 4, 22), second.Key, "second canonical cue"); c.Capture(Sample(4), Start.AddSeconds(3)); True(!Speak(c, Start.AddSeconds(3), words, out _), "second only speaks once"); Equal(2, words.Count, "both native movements remain audible");
    }
    private static void ResetRetiresCallbacksAndAllowsAFreshSession()
    { var c = Coordinator(); c.Capture(Sample(), Start); c.Reset(); True(!Speak(c, Start, [], out _), "reset retires pending callbacks"); c.Capture(Sample(), Start.AddSeconds(1)); True(Speak(c, Start.AddSeconds(1), [], out _), "fresh session is not suppressed by old dedupe"); }
    private static void ResetDuringAWorkerGuardPreventsOutput()
    {
        var c = Coordinator(); c.Capture(Sample(), Start); var output = false;
        True(!c.TrySpeakPending(Start, _ => { c.Reset(); return true; }, _ => { output = true; return true; }, out _), "reset in guard revokes pending selection"); True(!output, "revoked work never reaches output");
    }
    private static void ConcurrentCallbackUpdatesKeepOnlyTwoEpisodes()
    {
        var c = Coordinator(); Parallel.For(0, 100, i => c.Capture(Sample(i % 2 == 0 ? 3 : 4) with { PolygonSet = 0x180000u + (uint)i * 256 }, Start));
        var words = new List<string>(); True(Speak(c, Start, words, out _), "first bounded slot delivered"); True(Speak(c, Start, words, out _), "second bounded slot delivered"); True(!Speak(c, Start, words, out _), "there is no third per-bone episode"); Equal(2, words.Count, "two native episodes bound callback storage");
    }
    private static void ConcurrentResetDuringNativeValidationRetiresOnlyOldWork()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var c = Coordinator(current: _ => { entered.Set(); return release.Wait(TimeSpan.FromSeconds(5)); }); c.Capture(Sample(), Start);
        var output = false; var worker = Task.Run(() => c.TrySpeakPending(Start, _ => true, _ => { output = true; return true; }, out _));
        try
        {
            True(entered.Wait(TimeSpan.FromSeconds(5)), "worker reached native revalidation"); c.Reset(); c.Capture(Sample(4), Start.AddSeconds(1));
        }
        finally { release.Set(); }
        True(!worker.GetAwaiter().GetResult() && !output, "a concurrent reset revokes selected old work before output");
        var words = new List<string>(); True(Speak(c, Start.AddSeconds(1), words, out _), "old worker does not erase the new session's capture"); Equal("The creature closes in.", words.Single(), "new session remains intact");
    }
    private static void AResetDuringAcceptedOutputDoesNotCommitOldSessionHistory()
    {
        var c = Coordinator(); c.Capture(Sample(), Start); c.TrySpeakPending(Start, _ => true, _ => { c.Reset(); return true; }, out _);
        c.Capture(Sample(), Start.AddSeconds(1)); True(Speak(c, Start.AddSeconds(1), [], out _), "a successful obsolete output never marks the new session heard");
    }
    private static void PrefixGetterFailureDoesNotEscapeOrCreateSpeech()
    { var c = Coordinator(() => throw new InvalidOperationException()); c.Capture(Sample(), Start); True(!Speak(c, Start, [], out _), "unknown prefix on getter failure is unavailable"); }
    private static void AcceptedDescriptionVisibilityUsesFreshGeometryAndLaterBones()
    {
        var visible = true; uint polygon = 0x180000;
        var c = Coordinator(current: sample => visible && sample.PolygonSet == polygon);
        True(!c.IsCurrentDescriptionVisible(), "no accepted description has no audio visibility lease"); c.Capture(Sample(), Start);
        True(!c.IsCurrentDescriptionVisible(), "pending alone does not create an accepted visibility lease"); True(Speak(c, Start, [], out _), "description accepted");
        True(c.IsCurrentDescriptionVisible(), "fresh geometry preserves an accepted recording");
        polygon = 0x180100; c.Capture(Sample() with { PolygonSet = polygon }, Start.AddSeconds(1));
        True(c.IsCurrentDescriptionVisible(), "a later validated bone refreshes accepted visibility without rescheduling speech");
        visible = false; True(!c.IsCurrentDescriptionVisible(), "hidden-after-start stops the owned creature output");
        visible = true; True(!c.IsCurrentDescriptionVisible(), "an invalidated accepted visibility lease does not silently resume");
        True(!Speak(c, Start.AddSeconds(2), [], out _), "visibility monitoring never changes acceptance dedupe");
    }
    private static void ResetAndIdentityReplacementRetireAcceptedVisibility()
    {
        LoadedFieldScriptIdentity? identity = new(767, Stock); var c = Coordinator(() => identity); c.Capture(Sample(), Start);
        True(Speak(c, Start, [], out _) && c.IsCurrentDescriptionVisible(), "accepted native description initially visible"); c.Reset();
        True(!c.IsCurrentDescriptionVisible(), "reset retires the accepted sample"); c.Capture(Sample(), Start.AddSeconds(1)); True(Speak(c, Start.AddSeconds(1), [], out _), "new session accepted");
        identity = new(767, Echo); True(!c.IsCurrentDescriptionVisible(), "changed exact loaded prefix retires the old accepted sample");
    }

    private static FieldCreatureApproachNarrationCoordinator Coordinator(Func<LoadedFieldScriptIdentity?>? identity = null, Func<FieldCreatureApproachSample, bool>? current = null) => new(current ?? (_ => true), identity ?? (() => new(767, Stock)));
    private static FieldCreatureApproachSample Sample(int script = 3) => new(new(767, 12, script, script == 3 ? 12 : 22, 0xC4),
        0x100000, 0x110000, 0x120000, 0x110440, 0x130000, 0x140000, 0x180000, 0x210000,
        8, script == 3 ? 2505 : 476, script == 3 ? 2599 : 439, script == 3 ? -806 : 88,
        script == 3 ? 2574 : 494, script == 3 ? 2672 : 460, script == 3 ? -836 : 92,
        2, (ushort)(script == 3 ? 30 : 20), 1, 0x120C80, 0x0600, 0x300000, 123);
    private static bool Speak(FieldCreatureApproachNarrationCoordinator c, DateTime time, List<string> words, out FieldCutsceneDescriptionCue cue) => c.TrySpeakPending(time, _ => true, text => { words.Add(text); return true; }, out cue);
    private static void True(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual, string message) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{message}: expected {expected}, got {actual}"); }
}

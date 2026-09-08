using System.Buffers.Binary;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Gold Saucer G Bike, which is the same native module as the story chase run
/// in its arcade mode. Root's review: the arcade HUD draws HI-SCORE and SCORE and no
/// party health at all, the support vehicle is real and visible in both modes, and
/// hitting it costs points.
/// </summary>
internal static class GBikeArcadeModeTests
{
    internal static void Run()
    {
        ArcadeModeReadsWithoutStoryPartyHealth();
        ArcadeStatusCarriesTheDisplayedHighScore();
        TheSupportVehicleIsAvoidedInBothModes();
        TheArcadeBannerIsReadFromTheHudTheRendererUses();
        TheBannerIsSpokenWhenItAppearsAndNotPredicted();
        AHudReplacedOrRemodedMidReadIsNotABanner();
        TheModeAnnouncementNeverSwallowsTheBanner();
    }

    /// <summary>
    /// Root's finding: the reader validated and returned party HP in both modes, so
    /// a stale or unreadable story block could silence an otherwise valid arcade
    /// snapshot - and there is nothing on screen for it to describe anyway.
    /// </summary>
    private static void ArcadeModeReadsWithoutStoryPartyHealth()
    {
        var memory = new FakeHighwayMemory { StoryMode = 1, Score = 4200, HighScore = 9100 };

        // Exactly the case that used to fail: the story party block cannot be read.
        memory.PartyHealthReadable = false;
        var reader = new HighwayStateReader(memory);
        Equal(true, reader.TryRead(out var arcade),
            $"an arcade snapshot must read without the story party block; {reader.LastDiagnostic}");
        Equal(false, arcade.IsStoryChase, "story mode 1 is the arcade");
        Equal(0, arcade.PartyHealth.Count, "and draws no party health");
        Equal(4200, arcade.Score, "the displayed score is read");
        Equal(9100, arcade.HighScore, "and so is the displayed high score");

        // Nonsense left in the story block must not matter either.
        memory.PartyHealthReadable = true;
        memory.SetPartyHealth(slot: 0, currentHp: 900, maximumHp: 0);
        Equal(true, reader.TryRead(out var stillArcade),
            $"invalid story party values must not silence the arcade; {reader.LastDiagnostic}");
        Equal(0, stillArcade.PartyHealth.Count, "and are still not reported");

        // The story chase keeps its existing behaviour, including its own validation.
        memory.StoryMode = 0;
        memory.SetPartyHealth(slot: 0, currentHp: 250, maximumHp: 400);
        Equal(true, reader.TryRead(out var story), $"the story chase reads; {reader.LastDiagnostic}");
        Equal(true, story.IsStoryChase, "story mode 0 is the chase");
        Equal(1, story.PartyHealth.Count, "and does report party health");
        Equal(250, story.PartyHealth[0].CurrentHp, "with the current value");

        memory.SetPartyHealth(slot: 0, currentHp: 900, maximumHp: 0);
        Equal(false, reader.TryRead(out _), "an impossible story party value is still refused");

        memory.PartyHealthReadable = false;
        Equal(false, reader.TryRead(out _), "and an unreadable story party block is still refused");
    }

    private static void ArcadeStatusCarriesTheDisplayedHighScore()
    {
        var arcade = State(isStoryChase: false, score: 4200, highScore: 9100);
        var status = HighwayAccessibilityTracker.CreateStatusForTest(arcade);
        Equal(true, status.Contains("Score 4200.", StringComparison.Ordinal),
            $"the arcade status carries the score; got {status}");
        Equal(true, status.Contains("High score 9100.", StringComparison.Ordinal),
            $"and the displayed high score; got {status}");

        // The story chase's HUD is the party display and draws neither HI-SCORE nor
        // an arcade banner, so nothing invents one there.
        var story = State(isStoryChase: true, score: 4200, highScore: 9100);
        var storyStatus = HighwayAccessibilityTracker.CreateStatusForTest(story);
        Equal(false, storyStatus.Contains("High score", StringComparison.Ordinal),
            $"the story chase draws no high score; got {storyStatus}");
    }

    /// <summary>
    /// Root's finding: the truck-collision correction was gated on the story chase,
    /// even though the arcade vehicle is real and hitting it takes fifty points off.
    /// </summary>
    private static void TheSupportVehicleIsAvoidedInBothModes()
    {
        foreach (var isStoryChase in new[] { true, false })
        {
            var tracker = new HighwaySteeringTracker(
                normalCueInterval: TimeSpan.Zero,
                criticalCueInterval: TimeSpan.Zero);
            var road = new HighwayRoadState(0d, 200d);
            var now = new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);

            // Far away: no correction in either mode.
            var clear = tracker.Update(road, new HighwayPoint(400d, 400d), now);
            Equal(HighwaySteeringDirection.None, clear.Direction,
                $"a distant support vehicle needs no correction (story={isStoryChase})");

            // Right on top of Cloud: the correction has to fire in both modes.
            var close = tracker.Update(road, new HighwayPoint(4d, 4d), now.AddMilliseconds(100));
            Equal(true, close.Direction != HighwaySteeringDirection.None,
                $"a support vehicle right in front must be avoided (story={isStoryChase})");
        }

        // And the composer must actually hand the delta over in arcade mode, which is
        // the defect: it previously passed null unless the run was the story chase.
        var arcadeState = State(isStoryChase: false, score: 0, highScore: 0);
        var delta = HighwayAccessibilityComposer.TruckDeltaForTest(arcadeState);
        Equal(true, delta is not null, "the arcade composer supplies the support vehicle delta");
    }

    /// <summary>
    /// The word the arcade HUD paints across the screen. FUN_00659379 switches on the
    /// HUD object's own first field and puts up guaa, huaa or iuaa - READY, GO! and
    /// GOAL - for states 1, 3 and 2 and nothing for anything else.
    /// </summary>
    private static void TheArcadeBannerIsReadFromTheHudTheRendererUses()
    {
        var memory = new FakeHighwayMemory { StoryMode = 1, Score = 4200, HighScore = 9100 };
        var reader = new HighwayStateReader(memory);

        foreach (var (state, expected) in new[]
                 {
                     (0, HighwayBanner.None),
                     (1, HighwayBanner.Ready),
                     (2, HighwayBanner.Goal),
                     (3, HighwayBanner.Go),
                     (4, HighwayBanner.None)
                 })
        {
            memory.BannerState = state;
            Equal(true, reader.TryRead(out var snapshot), $"HUD state {state} reads");
            Equal(expected, snapshot.Banner, $"and puts up {expected}");
        }

        // The story chase goes to a different HUD object entirely, which draws no
        // banner at all and whose first field means something else.
        memory.StoryMode = 0;
        memory.BannerState = 3;
        Equal(true, reader.TryRead(out var story), "the story chase reads");
        Equal(HighwayBanner.None, story.Banner, "and never claims an arcade banner");

        // A HUD that has not been built is not a HUD showing nothing, but losing the
        // bikers and the truck over it would be far worse than not naming a banner.
        memory.StoryMode = 1;
        memory.HudPointer = 0;
        Equal(true, reader.TryRead(out var noHud), "an absent HUD does not lose the whole snapshot");
        Equal(HighwayBanner.Unknown, noHud.Banner, "but what it shows is not known");
        Equal(true, reader.LastDiagnostic.Contains("HUD pointer", StringComparison.Ordinal),
            $"and the reason is said out loud; got {reader.LastDiagnostic}");

        memory.HudPointer = 0x00A00000;
        memory.HudReadable = false;
        Equal(true, reader.TryRead(out var unreadable), "an unreadable HUD pointer likewise");
        Equal(HighwayBanner.Unknown, unreadable.Banner, "reports itself unknown");
    }

    /// <summary>
    /// Neither of these is caught by the module check: both move while the module
    /// stays at 6. FUN_0065076D replaces the pointer at 0x00D8D444 when the HUD is
    /// rebuilt, and FUN_0065950C picks a different HUD object entirely once
    /// 0x00D8596C leaves arcade mode - one whose word at the same offset is not a
    /// banner at all.
    /// </summary>
    private static void AHudReplacedOrRemodedMidReadIsNotABanner()
    {
        var memory = new FakeHighwayMemory { StoryMode = 1, Score = 1500, HighScore = 4500, BannerState = 1 };
        var reader = new HighwayStateReader(memory);
        Equal(true, reader.TryRead(out var settled), $"a settled arcade frame reads; {reader.LastDiagnostic}");
        Equal(HighwayBanner.Ready, settled.Banner, "with the word on screen");

        // The HUD is rebuilt while its own state word is being sampled.
        memory.ReplaceHudAfterReadingState(0x00B00000);
        Equal(true, reader.TryRead(out var replaced), "the frame still reads, so steering data survives");
        Equal(HighwayBanner.Unknown, replaced.Banner,
            "but a state word from a HUD that has been replaced is not the banner on screen");
        Equal(1500, replaced.Score, "and the score is independent of it");

        // The ride leaves arcade mode while its own state word is being sampled.
        memory.SetHudPointer(0x00A00000);
        memory.ChangeModeAfterReadingState(0);
        Equal(true, reader.TryRead(out var restyled), "the frame still reads");
        Equal(HighwayBanner.Unknown, restyled.Banner,
            "but the story chase's HUD holds something else at that offset");

        // And the composer never turns an unknown into a word.
        var tracker = NewTracker();
        var now = new DateTime(2026, 9, 7, 11, 0, 0, DateTimeKind.Utc);
        Equal(null, Spoken(tracker, HighwayBannerState.Unknown, 0, now),
            "an unknown banner is never announced");
        Equal("Ready.", Spoken(tracker, HighwayBannerState.Ready, 0, now.AddMilliseconds(50)),
            "the next one that is actually known still is");
        Equal(null, Spoken(tracker, HighwayBannerState.Unknown, 0, now.AddMilliseconds(100)),
            "and an unknown after it neither speaks");
        Equal(null, Spoken(tracker, HighwayBannerState.Ready, 0, now.AddMilliseconds(150)),
            "nor makes the word it was hiding sound new");
    }

    /// <summary>
    /// The banner is said as it appears, once, and the score beside GOAL is the one
    /// the HUD is showing at that moment. Nothing here predicts a banner from a state
    /// that has not put one up yet.
    /// </summary>
    private static void TheBannerIsSpokenWhenItAppearsAndNotPredicted()
    {
        var tracker = NewTracker();
        var now = new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc);

        Equal(null, Spoken(tracker, HighwayBannerState.None, 0, now),
            "a screen with no banner says nothing");

        Equal("Ready.", Spoken(tracker, HighwayBannerState.Ready, 0, now.AddMilliseconds(50)),
            "READY is said as it comes up");
        for (var hold = 0; hold < 30; hold++)
        {
            Equal(null, Spoken(tracker, HighwayBannerState.Ready, 0, now.AddMilliseconds(60 + hold)),
                "and not repeated while it stays on screen");
        }

        Equal(null, Spoken(tracker, HighwayBannerState.None, 0, now.AddMilliseconds(100)),
            "the banner leaving is not itself an announcement");
        Equal("Go!", Spoken(tracker, HighwayBannerState.Go, 0, now.AddMilliseconds(150)),
            "GO is said as it comes up");

        var goal = Spoken(tracker, HighwayBannerState.Goal, 5600, now.AddMilliseconds(60_000));
        Equal("Goal. Score 5600.", goal, $"and GOAL carries the score on screen with it; got {goal}");

        // A moment where the HUD could not be read must not read as the word leaving
        // and coming back.
        Equal(null, Spoken(tracker, HighwayBannerState.Unknown, 5600, now.AddMilliseconds(60_050)),
            "an unreadable HUD says nothing");
        Equal(null, Spoken(tracker, HighwayBannerState.Goal, 5600, now.AddMilliseconds(60_100)),
            "and the banner it was hiding is not announced again");

        // A status press landing on the same pass as a banner must not swallow it.
        var fresh = NewTracker();
        var combined = fresh.Update(
            Banner(HighwayBannerState.Go, 0), now, statusRequested: true).Speech;
        Equal(true, combined?.Text.StartsWith("Go!", StringComparison.Ordinal) == true,
            $"the banner is kept when a status press lands with it; got {combined?.Text}");
        Equal(true, combined?.Text.Contains("Score 0.", StringComparison.Ordinal) == true,
            "along with the status that was asked for");
    }

    /// <summary>
    /// The steering mode and the banner arrive on the same poll, and both are
    /// one-shot: the mode announces itself on the first poll of every ride and again
    /// on every F8, and the banner exists only while it is on screen.
    ///
    /// The coordinator used to speak the mode and return, after the composer had
    /// already counted the banner as delivered - so a ride acquired while READY was up
    /// never heard READY, and F8 during GO or GOAL lost that word for good. This
    /// drives the same composer, the same mode tracker and the same delivery the
    /// coordinator calls.
    /// </summary>
    private static void TheModeAnnouncementNeverSwallowsTheBanner()
    {
        var now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
        var mode = new HighwayAutoSteeringModeTracker(enabledByDefault: true);
        var composer = NewComposer();

        // First poll of the ride, with READY on screen. The mode always announces
        // itself here, which is exactly when the banner used to be lost.
        var acquisition = mode.Observe(isHighway: true, isForeground: true, toggleRequested: false);
        Equal(true, acquisition.Announcement is not null, "the first poll of a ride announces the mode");

        var ready = composer.Update(
            Banner(HighwayBannerState.Ready, score: 0), roadState: null, now, statusRequested: false);
        Equal(true, ready.Speech is not null, "and the composer has the banner that is on screen");

        var acquired = HighwayAccessibilityComposer.Deliver(acquisition.Announcement, ready.Speech);
        Equal(1, acquired.Count, "both go out together as one utterance");
        Equal(true, acquired[0].Text.Contains(
                HighwayAutoSteeringModeTracker.EnabledAnnouncement, StringComparison.Ordinal),
            $"carrying the steering mode; got {acquired[0].Text}");
        Equal(true, acquired[0].Text.Contains("Ready.", StringComparison.Ordinal),
            $"and the word that was on screen with it; got {acquired[0].Text}");
        Equal(true, acquired[0].Interrupt, "and it supersedes whatever was being said");

        // F8 during GOAL. The banner carries the displayed final score, which is the
        // one thing a player cannot get back once it has gone.
        var goal = composer.Update(
            Banner(HighwayBannerState.Goal, score: 7300), roadState: null, now.AddSeconds(60),
            statusRequested: false);
        var toggled = mode.Observe(isHighway: true, isForeground: true, toggleRequested: true);
        Equal(true, toggled.Announcement is not null, "F8 announces the mode it switched to");

        var both = HighwayAccessibilityComposer.Deliver(toggled.Announcement, goal.Speech);
        Equal(1, both.Count, "the toggle and the banner go out together");
        Equal(true, both[0].Text.Contains(
                HighwayAutoSteeringModeTracker.DisabledAnnouncement, StringComparison.Ordinal),
            $"carrying the new steering mode; got {both[0].Text}");
        Equal(true, both[0].Text.Contains("Goal. Score 7300.", StringComparison.Ordinal),
            $"and the goal with its displayed score; got {both[0].Text}");

        // Either alone is unchanged, and a poll with neither says nothing.
        var modeOnly = HighwayAccessibilityComposer.Deliver(
            HighwayAutoSteeringModeTracker.EnabledAnnouncement, null);
        Equal(1, modeOnly.Count, "a mode change on a quiet poll is still announced");
        Equal(HighwayAutoSteeringModeTracker.EnabledAnnouncement, modeOnly[0].Text, "on its own");

        var speechOnly = HighwayAccessibilityComposer.Deliver(
            null, new HighwaySpeechRequest(HighwaySpeechKind.Warning, "Go!", Interrupt: true));
        Equal(1, speechOnly.Count, "a banner with no mode change is still announced");
        Equal("Go!", speechOnly[0].Text, "on its own");

        Equal(0, HighwayAccessibilityComposer.Deliver(null, null).Count,
            "and a poll with nothing to say stays quiet");
    }

    private static HighwayAccessibilityComposer NewComposer() =>
        new(NewTracker(),
            new HighwaySteeringTracker(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)),
            new HighwayEngagementSteeringTracker(40d, 20d));

    private static HighwayAccessibilityTracker NewTracker() =>
        new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 40d, 20d, 200d, 100d);

    private static string? Spoken(
        HighwayAccessibilityTracker tracker,
        HighwayBannerState banner,
        int score,
        DateTime nowUtc) =>
        tracker.Update(Banner(banner, score), nowUtc, statusRequested: false).Speech?.Text;

    private static HighwayAccessibilityState Banner(HighwayBannerState banner, int score) =>
        State(isStoryChase: false, score: score, highScore: 0) with { Banner = banner };

    private static HighwayAccessibilityState State(bool isStoryChase, int score, int highScore) =>
        new(new HighwayPoint(0d, 0d),
            new HighwayPoint(10d, 60d),
            Array.Empty<HighwayEnemyState>(),
            Array.Empty<HighwayPartyHealth>(),
            score,
            isStoryChase,
            CloudAttackTimer: 0,
            HighScore: highScore);

    private sealed class FakeHighwayMemory : ILegacyAddressSpace
    {
        private readonly byte[] actors = new byte[HighwayStateReader.ActorCount * HighwayStateReader.ActorStride];
        private readonly byte[] party =
            new byte[HighwayStateReader.PartySlotCount * HighwayStateReader.PartyHealthStride];

        public FakeHighwayMemory() => Array.Fill(party, (byte)0xFF);

        private const uint HudObject = 0x00A00000;

        public byte Module { get; set; } = HighwayStateReader.HighwayModule;
        public int StoryMode { get; set; }
        public int Score { get; set; }
        public int HighScore { get; set; }
        public bool PartyHealthReadable { get; set; } = true;

        /// <summary>The HUD object the renderer reaches, or zero if it was never built.</summary>
        public uint HudPointer { get; set; } = HudObject;

        public bool HudReadable { get; set; } = true;

        /// <summary>The HUD's own banner state word, as the renderer switches on it.</summary>
        public int BannerState { get; set; }

        private uint replacementHud;
        private int replacementMode = -1;

        public void SetHudPointer(uint pointer) => HudPointer = pointer;

        /// <summary>The HUD is rebuilt the moment its state word has been sampled.</summary>
        public void ReplaceHudAfterReadingState(uint pointer)
        {
            replacementHud = pointer;
            replacementMode = -1;
        }

        /// <summary>The ride leaves arcade mode the moment its state word has been sampled.</summary>
        public void ChangeModeAfterReadingState(int mode)
        {
            replacementMode = mode;
            replacementHud = 0;
        }

        public void SetPartyHealth(int slot, int currentHp, int maximumHp)
        {
            var offset = slot * HighwayStateReader.PartyHealthStride;
            BinaryPrimitives.WriteUInt16LittleEndian(
                party.AsSpan(offset + HighwayStateReader.PartyMaximumHpOffset), (ushort)maximumHp);
            BinaryPrimitives.WriteUInt16LittleEndian(
                party.AsSpan(offset + HighwayStateReader.PartyCurrentHpOffset), (ushort)currentHp);
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            destination.Clear();
            switch (virtualAddress)
            {
                case (uint)HighwayStateReader.AddressCurrentModule when destination.Length == 1:
                    destination[0] = Module;
                    return true;
                case (uint)HighwayStateReader.AddressStoryMode when destination.Length == 4:
                    BinaryPrimitives.WriteInt32LittleEndian(destination, StoryMode);
                    return true;
                case (uint)HighwayStateReader.AddressScore when destination.Length == 4:
                    BinaryPrimitives.WriteInt32LittleEndian(destination, Score);
                    return true;
                case (uint)HighwayStateReader.AddressHighScore when destination.Length == 4:
                    BinaryPrimitives.WriteInt32LittleEndian(destination, HighScore);
                    return true;
                case (uint)HighwayStateReader.AddressHudPointer when destination.Length == 4:
                    if (!HudReadable)
                    {
                        return false;
                    }

                    BinaryPrimitives.WriteUInt32LittleEndian(destination, HudPointer);
                    return true;
                case HudObject + HighwayStateReader.HudBannerStateOffset when destination.Length == 4:
                    BinaryPrimitives.WriteInt32LittleEndian(destination, BannerState);
                    if (replacementHud != 0)
                    {
                        HudPointer = replacementHud;
                        replacementHud = 0;
                    }

                    if (replacementMode >= 0)
                    {
                        StoryMode = replacementMode;
                        replacementMode = -1;
                    }

                    return true;
                case (uint)HighwayStateReader.AddressActorTable when destination.Length == actors.Length:
                    actors.CopyTo(destination);
                    return true;
                case (uint)HighwayStateReader.AddressPartyHealth when destination.Length == party.Length:
                    if (!PartyHealthReadable)
                    {
                        return false;
                    }

                    party.CopyTo(destination);
                    return true;
                default:
                    return false;
            }
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(
                $"G Bike arcade mode - {message}: expected {expected}, actual {actual}.");
    }
}

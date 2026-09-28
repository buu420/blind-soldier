using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Both 2026-09-27 logs: every Limit break re-activated its "LIMIT BREAK" battle text
/// buffer about fifteen times in two seconds (legacy 18:13:09-18:13:10, buffer 256;
/// x64 14:43:59-14:44:00), and each re-activation was spoken again, talking over "Barret.
/// Limit" and the Limit list. One blink sequence is one message; a message shown again
/// after a real pause is still a new one.
/// </summary>
internal static class BattleMessageFlickerTests
{
    private const short LimitBuffer = 256;
    private const string LimitText = "! LIMIT BREAK";

    internal static void Run()
    {
        ABlinkingBannerIsSpokenOnce();
        TheSenseCoordinatorSpeaksOneBlinkSequenceOnce();
        TheSameMessageAfterAPauseIsSpokenAgain();
        ANewMessageInTheSameBufferIsSpoken();
        WithoutAClockEveryReactivationIsSpokenAsBefore();
        Console.WriteLine("PASS battle message flicker: one LIMIT BREAK per blink sequence; genuine repeats and new text still spoken.");
    }

    private static void ABlinkingBannerIsSpokenOnce()
    {
        var now = new DateTime(2026, 9, 27, 18, 13, 9, DateTimeKind.Utc);
        var tracker = new BattleMessageSpeechTracker(id => id == LimitBuffer ? LimitText : null, () => now);
        var spoken = Blink(tracker, ref now, blinks: 15, interval: TimeSpan.FromMilliseconds(130));
        Equal(1, spoken, "fifteen re-activations of the banner in two seconds are one announcement");
    }

    private static void TheSenseCoordinatorSpeaksOneBlinkSequenceOnce()
    {
        var now = new DateTime(2026, 9, 27, 14, 43, 59, DateTimeKind.Utc);
        var coordinator = new BattleSenseSpeechCoordinator(
            id => id == LimitBuffer ? new BattleRuntimeTextResolution("% LIMIT BREAK", []) : null,
            _ => null,
            _ => null,
            () => now);
        var spoken = 0;
        for (var blink = 0; blink < 15; blink++)
        {
            coordinator.ObserveActiveBuffer(LimitBuffer);
            if (coordinator.Poll() is { Length: > 0 })
            {
                spoken++;
            }

            // The inactive marker resets the coordinator between blinks.
            coordinator.ObserveActiveBuffer(-1);
            now = now.AddMilliseconds(130);
        }

        Equal(1, spoken, "the shared coordinator both hosts use speaks one blink sequence once");
    }

    private static void TheSameMessageAfterAPauseIsSpokenAgain()
    {
        var now = new DateTime(2026, 9, 27, 18, 13, 9, DateTimeKind.Utc);
        var tracker = new BattleMessageSpeechTracker(id => id == LimitBuffer ? LimitText : null, () => now);
        Equal(1, Blink(tracker, ref now, blinks: 15, interval: TimeSpan.FromMilliseconds(130)), "first Limit break");
        now = now.AddSeconds(5);
        Equal(1, Blink(tracker, ref now, blinks: 15, interval: TimeSpan.FromMilliseconds(130)), "the next Limit break is announced");

        tracker.ObserveActiveBuffer(LimitBuffer);
        now = now.AddMilliseconds(100);
        tracker.ObserveActiveBuffer(-1);
        now = now.AddSeconds(1);
        tracker.ObserveActiveBuffer(LimitBuffer);
        Equal(LimitText, tracker.Poll(), "a message shown again after a second's pause is spoken again");
    }

    private static void ANewMessageInTheSameBufferIsSpoken()
    {
        var now = new DateTime(2026, 9, 27, 18, 13, 9, DateTimeKind.Utc);
        var text = LimitText;
        var tracker = new BattleMessageSpeechTracker(id => id == LimitBuffer ? text : null, () => now);
        tracker.ObserveActiveBuffer(LimitBuffer);
        Equal(LimitText, tracker.Poll(), "first message");
        tracker.ObserveActiveBuffer(-1);
        now = now.AddMilliseconds(100);
        text = "% LIMIT BREAK";
        tracker.ObserveActiveBuffer(LimitBuffer);
        Equal("% LIMIT BREAK", tracker.Poll(), "different text reusing the buffer at once is a new message");
    }

    private static void WithoutAClockEveryReactivationIsSpokenAsBefore()
    {
        var tracker = new BattleMessageSpeechTracker(id => id == LimitBuffer ? LimitText : null);
        tracker.ObserveActiveBuffer(LimitBuffer);
        Equal(LimitText, tracker.Poll(), "first activation");
        tracker.ObserveActiveBuffer(-1);
        tracker.ObserveActiveBuffer(LimitBuffer);
        Equal(LimitText, tracker.Poll(), "the clockless tracker keeps its released behaviour");
    }

    private static int Blink(BattleMessageSpeechTracker tracker, ref DateTime now, int blinks, TimeSpan interval)
    {
        var spoken = 0;
        for (var blink = 0; blink < blinks; blink++)
        {
            tracker.ObserveActiveBuffer(LimitBuffer);
            if (tracker.Poll() is { Length: > 0 })
            {
                spoken++;
            }

            tracker.ObserveActiveBuffer(-1);
            now += interval;
        }

        return spoken;
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}

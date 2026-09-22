using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Shinra Mansion safe, field 299 <c>sinin2_1</c>, entity 9 <c>lin1</c>.
///
/// <para>What a sighted player is looking at while the dial is open is one two-digit
/// number and a clock. The number is native numeric window 0: <c>disn</c>, entity 1,
/// script 3 opens it with <c>WSPCL</c> display type 2 and fills it from Bank[6][7] with
/// <c>WNUMB</c>, and the leader's own script 3 rewrites that same window on every frame a
/// direction is held. The clock is a different window - <c>time</c>, entity 2, script 3
/// opens window 3 as display type 1 - and already belongs to the countdown readout.</para>
///
/// <para>Nothing else about the dial is on screen. The target for the current entry is in
/// Bank[6][12], the direction that entry will accept is in Bank[5][9] and the entry index
/// is in Bank[5][11]; none of the three is drawn anywhere and none of them is read here.
/// The readout is handed the window and nothing else, which is what makes that structural
/// rather than a promise.</para>
///
/// <para>The one hard delivery problem is that the number is not a slow reading. The
/// leader's poll loop has no wait in it - byte 331 jumps straight back to byte 108 - so a
/// held direction moves the dial once per frame and rewrites the window once per frame.
/// Speaking every value would queue dozens of stale numbers over a twenty second timer.
/// The readout therefore samples current numbers at a bounded rate while moving,
/// and promptly says the final number when the dial stops.</para>
/// </summary>
internal static class ShinraMansionSafeDialTests
{
    private const int SafeRoom = ShinraMansionSafeDialReadout.FieldId;
    private const int OtherMansionRoom = 300;

    private static readonly DateTime Start = new(2026, 9, 21, 20, 59, 40, DateTimeKind.Utc);

    public static void Run()
    {
        IgnoresEveryFieldButTheSafeRoom();
        SaysNothingUntilTheNativeDialWindowIsOnScreen();
        AnnouncesTheDialWhenTheNativeWindowOpens();
        SpeaksCurrentNumbersWhileTheDialIsTurning();
        SpeaksTheNumberOnceItHasStoppedChanging();
        SpeaksTheCurrentNumberRatherThanAQueuedOne();
        DoesNotRepeatANumberThatHasNotChanged();
        FollowsTheNativeWrapAroundInBothDirections();
        ForgetsTheDialWhenItClosesSoARetryStartsClean();
        ForgetsTheDialWhenTheFieldChanges();
        OwnsOnlyTheNativeDialWindow();
        SuppressesTheCountdownOnlyWhileTheDialIsUp();
        DropsTheClocksThresholdsForAsLongAsTheDialIsUp();
        RejectsValuesOutsideTheNativeDialRange();
        SaysNothingButTheNumbersThatWereOnScreen();
        DescribesTheCurrentNumberForTheRepeatAction();
        UnreadableSampleDoesNotInventACloseOrAReading();
        StaleSpeechCannotBeRetriedAfterTheDialMoves();
        RejectsOtherNumericDisplays();
    }

    /// <summary>
    /// The installed archives, read with the production decoder. This is the check that
    /// keeps the gate honest: the readout believes the dial is up because window 0 is a
    /// display-type-2 numeric window, and that is only true while these opcodes say so.
    /// </summary>
    public static void RunWithInstalledGameData()
    {
        // Skips when there is no archive configured, and fails when there is one and it
        // does not say what this readout is built on. A missing archive is not evidence.
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")))
        {
            Console.WriteLine("safe dial: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        var scripts = Scripts();

        // disn, entity 1: the dial's own window. Display type 2 is the gate the readout
        // gates on, and the value comes from Bank[6][7] with a two digit limit.
        var display = scripts.ReadScriptOpcodes(SafeRoom, 1, 3);
        Equal("disn", display[0].EntityName, "entity 1 is the dial display");
        AssertBytes(display, 4, [0x36, 0x00, 0x02, 0x0A, 0x0C], "WSPCL opens window 0 as display type 2");
        AssertBytes(display, 9, [0x37, 0x60, 0x00, 0x07, 0x00, 0x00, 0x00, 0x02], "WNUMB fills window 0 from Bank[6][7]");
        AssertBytes(display, 27, [0x40, 0x00, 0x17], "the window carries the blank placeholder dialog 23");

        // time, entity 2: a different window and a different display type. The clock is
        // the countdown readout's, and nothing here speaks it.
        var clock = scripts.ReadScriptOpcodes(SafeRoom, 2, 3);
        Equal("time", clock[0].EntityName, "entity 2 is the clock");
        AssertBytes(clock, 14, [0x36, 0x03, 0x01, 0x0A, 0x0A], "WSPCL opens window 3 as the clock display type");

        // The leader runs the dial. All three party scripts are identical copies; cl is
        // the one that runs when Cloud is leading.
        var leader = scripts.ReadScriptOpcodes(SafeRoom, 3, 3);
        Equal("cl", leader[0].EntityName, "entity 3 is the leader's copy of the dial");
        AssertBytes(leader, 30, [0x38, 0x00, 0x00, 0x00, 0x00, 0x14], "STTIM gives the dial twenty seconds");
        AssertBytes(leader, 120, [0x30, 0x00, 0x20, 0x55], "a held Right turns the dial up");
        AssertBytes(leader, 208, [0x30, 0x00, 0x80, 0x55], "a held Left turns the dial down");
        AssertBytes(leader, 296, [0x31, 0x20, 0x02, 0x20], "a fresh OK enters the current number");
        AssertBytes(leader, 158, [0x37, 0x60, 0x00, 0x07, 0x00, 0x00, 0x00, 0x02], "every turn rewrites window 0");

        // The poll loop has no wait in it, which is why the number moves once per frame
        // and why speaking every value would queue.
        AssertBytes(leader, 331, [0x13, 0xDF, 0x00], "the poll loop jumps straight back with no wait");

        // And the display type is put back to zero when the dial closes, so the gate
        // cannot go stale into the question window or the Success and Fail pages.
        AssertBytes(leader, 340, [0x36, 0x00, 0x00, 0x00, 0x00], "closing the dial clears window 0's display type");
        AssertBytes(leader, 345, [0x36, 0x03, 0x00, 0x00, 0x00], "closing the dial clears the clock display type");
    }

    private static void IgnoresEveryFieldButTheSafeRoom()
    {
        var readout = new ShinraMansionSafeDialReadout();
        var cue = readout.Observe(OtherMansionRoom, Dial(36), Start);

        Equal(null, cue.Speech, "another room says nothing");
        Equal(false, cue.IsDialOnScreen, "another room is not the dial");
        Equal(false, readout.OwnsWindow(0), "another room owns no window");
    }

    private static void SaysNothingUntilTheNativeDialWindowIsOnScreen()
    {
        var readout = new ShinraMansionSafeDialReadout();

        // This is the safe's own question window - "Open the safe" - which is window 0
        // as well, but is not a numeric window. Reading it as a dial would speak a
        // number over the choice the player is making.
        var cue = readout.Observe(SafeRoom, default, Start);

        Equal(null, cue.Speech, "no numeric window is not a dial");
        Equal(false, cue.IsDialOnScreen, "no numeric window is not the dial being open");
        Equal(false, readout.OwnsWindow(0), "the question window is left to the dialogue readout");
    }

    private static void AnnouncesTheDialWhenTheNativeWindowOpens()
    {
        var readout = new ShinraMansionSafeDialReadout();
        var cue = readout.Observe(SafeRoom, Dial(0), Start);

        Equal("Safe dial 0.", cue.Speech, "the dial opening is said at once");
        Equal(true, cue.IsDialOnScreen, "the dial is on screen");
    }

    private static void SpeaksCurrentNumbersWhileTheDialIsTurning()
    {
        var readout = new ShinraMansionSafeDialReadout();
        readout.Observe(SafeRoom, Dial(0), Start);
        var spoken = 0;
        var lastSpeechAt = Start;
        for (var step = 1; step <= 40; step++)
        {
            var now = Start.AddMilliseconds(step * 35);
            var cue = readout.Observe(SafeRoom, Dial(step), now);
            if (cue.Speech is null) continue;
            Equal($"{step}.", cue.Speech, "moving feedback is the currently displayed number");
            Equal(true, now - lastSpeechAt >= TimeSpan.FromMilliseconds(250),
                "held movement does not interrupt speech every frame");
            lastSpeechAt = now;
            spoken++;
        }

        Equal(true, spoken >= 4 && spoken <= 6,
            "a held turn provides bounded feedback before the player releases it");
    }

    private static void SpeaksTheNumberOnceItHasStoppedChanging()
    {
        var readout = new ShinraMansionSafeDialReadout();
        readout.Observe(SafeRoom, Dial(0), Start);
        var now = Start;

        for (var step = 1; step <= 36; step++)
        {
            now = now.AddMilliseconds(35);
            readout.Observe(SafeRoom, Dial(step), now);
        }

        // The player has let go. The number is still moving as far as the readout knows
        // until it has held still long enough to be a reading rather than a frame.
        now = now.AddMilliseconds(35);
        Equal(null, readout.Observe(SafeRoom, Dial(36), now).Speech, "one still frame is not a stopped dial");

        now += ShinraMansionSafeDialReadout.SettleDelay;
        Equal("36.", readout.Observe(SafeRoom, Dial(36), now).Speech, "a stopped dial is read out");
    }

    private static void SpeaksTheCurrentNumberRatherThanAQueuedOne()
    {
        var readout = new ShinraMansionSafeDialReadout();
        readout.Observe(SafeRoom, Dial(0), Start);

        var now = Start;
        foreach (var value in new[] { 10, 20, 30, 40, 50 })
        {
            now = now.AddMilliseconds(35);
            Equal(null, readout.Observe(SafeRoom, Dial(value), now).Speech, $"{value} is passed through, not spoken");
        }

        now = now.AddMilliseconds(35);
        readout.Observe(SafeRoom, Dial(59), now);
        now += ShinraMansionSafeDialReadout.SettleDelay + TimeSpan.FromMilliseconds(35);

        Equal("59.", readout.Observe(SafeRoom, Dial(59), now).Speech, "the number said is the one on screen now");
    }

    private static void DoesNotRepeatANumberThatHasNotChanged()
    {
        var readout = new ShinraMansionSafeDialReadout();
        readout.Observe(SafeRoom, Dial(0), Start);
        var now = Start.AddMilliseconds(35);
        readout.Observe(SafeRoom, Dial(5), now);
        now += ShinraMansionSafeDialReadout.SettleDelay + TimeSpan.FromMilliseconds(35);
        Equal("5.", readout.Observe(SafeRoom, Dial(5), now).Speech, "the settled number is said");

        var spoken = 0;
        for (var tick = 1; tick <= 60; tick++)
        {
            if (readout.Observe(SafeRoom, Dial(5), now.AddMilliseconds(tick * 35)).Speech is not null)
            {
                spoken++;
            }
        }

        Equal(0, spoken, "a dial nobody is touching stays quiet");
    }

    private static void FollowsTheNativeWrapAroundInBothDirections()
    {
        // 99 wraps to 0 going up and 0 wraps to 99 going down, in the leader's own
        // script. The readout follows the window rather than doing any arithmetic.
        Equal("0.", Settle(99, 0), "turning up past 99 reads 0");
        Equal("99.", Settle(0, 99), "turning down past 0 reads 99");
    }

    private static void ForgetsTheDialWhenItClosesSoARetryStartsClean()
    {
        var readout = new ShinraMansionSafeDialReadout();
        var now = Start;
        readout.Observe(SafeRoom, Dial(0), now);
        now = now.AddMilliseconds(35);
        readout.Observe(SafeRoom, Dial(36), now);
        now += ShinraMansionSafeDialReadout.SettleDelay + TimeSpan.FromMilliseconds(35);
        Equal("36.", readout.Observe(SafeRoom, Dial(36), now).Speech, "first attempt reads out");

        // Fail closes the window and puts the display type back to zero.
        now = now.AddMilliseconds(35);
        Equal(null, readout.Observe(SafeRoom, default, now).Speech, "closing the dial says nothing");
        Equal(false, readout.OwnsWindow(0), "the closed dial releases window 0 for the Fail page");
        Equal(null, readout.Describe(), "a closed dial has nothing to repeat");

        // The player opens the safe again. The dial is new, not a continuation.
        now = now.AddMilliseconds(35);
        Equal("Safe dial 0.", readout.Observe(SafeRoom, Dial(0), now).Speech, "a retry announces its own dial");
    }

    private static void ForgetsTheDialWhenTheFieldChanges()
    {
        var readout = new ShinraMansionSafeDialReadout();
        readout.Observe(SafeRoom, Dial(0), Start);
        readout.Observe(OtherMansionRoom, default, Start.AddMilliseconds(35));

        Equal(false, readout.OwnsWindow(0), "leaving the room releases the window");
        Equal(null, readout.Describe(), "leaving the room leaves nothing to repeat");
        Equal(
            "Safe dial 0.",
            readout.Observe(SafeRoom, Dial(0), Start.AddMilliseconds(70)).Speech,
            "coming back announces the dial again");
    }

    private static void OwnsOnlyTheNativeDialWindow()
    {
        var readout = new ShinraMansionSafeDialReadout();
        readout.Observe(SafeRoom, Dial(0), Start);

        Equal(true, readout.OwnsWindow(0), "the dial owns its own blank window");
        Equal(false, readout.OwnsWindow(1), "Success and Fail are left to the dialogue readout");
        Equal(false, readout.OwnsWindow(3), "the clock is left to the countdown readout");
    }

    private static void SuppressesTheCountdownOnlyWhileTheDialIsUp()
    {
        var readout = new ShinraMansionSafeDialReadout();
        Equal(false, readout.SuppressesCountdownSpeech, "nothing is suppressed before the dial opens");

        readout.Observe(SafeRoom, Dial(0), Start);
        Equal(true, readout.SuppressesCountdownSpeech, "the dial owns speech while it is up");

        readout.Observe(SafeRoom, default, Start.AddMilliseconds(35));
        Equal(false, readout.SuppressesCountdownSpeech, "the countdown is handed straight back");
    }

    /// <summary>
    /// The clock the dial runs against is window 3, and the countdown readout announces
    /// every one of its last ten seconds with interrupt. On a twenty second timer that
    /// is eleven interruptions, one on top of every reading of the number the player is
    /// turning, which is why the dial takes the channel while it is up.
    /// </summary>
    private static void DropsTheClocksThresholdsForAsLongAsTheDialIsUp()
    {
        var readout = new ShinraMansionSafeDialReadout();
        var countdown = new FieldCountdownSpeechCoordinator();

        countdown.Observe(new FieldCountdownSnapshot(true, 15, 0b1000));
        Equal(false, readout.TrySuppressCountdown(countdown), "nothing is taken before the dial opens");
        Equal(true, countdown.TryGetPending(out var beforeDial), "the clock keeps its own threshold");
        Equal("15 seconds remaining", beforeDial.Speech, "the clock speaks for itself before the dial");
        countdown.Acknowledge(beforeDial);

        readout.Observe(SafeRoom, Dial(0), Start);
        countdown.Observe(new FieldCountdownSnapshot(true, 10, 0b1000));
        Equal(true, readout.TrySuppressCountdown(countdown), "the dial takes the clock's threshold");
        Equal(false, countdown.TryGetPending(out _), "no clock line is left queued behind the dial");

        // Taken rather than held: a threshold that arrived during the dial must not
        // turn up late over the Success or Fail page.
        readout.Observe(SafeRoom, default, Start.AddMilliseconds(35));
        Equal(false, readout.TrySuppressCountdown(countdown), "a closed dial takes nothing");
        Equal(false, countdown.TryGetPending(out _), "the dropped threshold does not arrive late");

        countdown.Observe(new FieldCountdownSnapshot(true, 9, 0b1000));
        Equal(true, countdown.TryGetPending(out var afterDial), "the clock speaks again once the dial is gone");
        Equal("9", afterDial.Speech, "the clock is handed straight back");
    }

    private static void RejectsValuesOutsideTheNativeDialRange()
    {
        var readout = new ShinraMansionSafeDialReadout();

        Equal(null, readout.Observe(SafeRoom, Dial(100), Start).Speech, "a value the dial cannot show is not a reading");
        Equal(false, readout.Observe(SafeRoom, Dial(-1), Start.AddMilliseconds(35)).IsDialOnScreen, "a negative value is not a dial");
        Equal(false, readout.OwnsWindow(0), "an impossible value owns nothing");
    }

    private static void SaysNothingButTheNumbersThatWereOnScreen()
    {
        // Driven through the four numbers this safe actually wants, in the order it
        // wants them, with the direction each entry accepts changing in between. If the
        // readout ever learned any of that, it would have to say something that is not
        // one of these two forms.
        var readout = new ShinraMansionSafeDialReadout();
        var spoken = new List<string>();
        var onScreen = new List<int>();
        var now = Start;

        foreach (var entry in new[] { 36, 10, 59, 97 })
        {
            now = now.AddMilliseconds(35);
            for (var value = 0; value <= entry; value += 7)
            {
                onScreen.Add(value);
                now = now.AddMilliseconds(35);
                Collect(readout.Observe(SafeRoom, Dial(value), now));
            }

            onScreen.Add(entry);
            now = now.AddMilliseconds(35);
            Collect(readout.Observe(SafeRoom, Dial(entry), now));
            now += ShinraMansionSafeDialReadout.SettleDelay + TimeSpan.FromMilliseconds(35);
            Collect(readout.Observe(SafeRoom, Dial(entry), now));
        }

        if (spoken.Count == 0)
        {
            throw new InvalidOperationException("the dial session said nothing at all.");
        }

        foreach (var line in spoken)
        {
            var isNumberOnScreen = onScreen.Any(value =>
                line == $"{value}." || line == $"Safe dial {value}.");
            if (!isNumberOnScreen)
            {
                throw new InvalidOperationException(
                    $"the dial said \"{line}\", which is not a number that was on screen.");
            }
        }

        void Collect(ShinraMansionSafeDialCue cue)
        {
            if (cue.Speech is not null)
            {
                spoken.Add(cue.Speech);
            }
        }
    }

    private static void DescribesTheCurrentNumberForTheRepeatAction()
    {
        var readout = new ShinraMansionSafeDialReadout();
        readout.Observe(SafeRoom, Dial(0), Start);
        Equal("Safe dial 0.", readout.Describe(), "the repeat action answers with the opening number");

        // Mid-turn the repeat action still answers, with whatever is on screen now -
        // it is an answer to a press, not a queued sentence.
        readout.Observe(SafeRoom, Dial(42), Start.AddMilliseconds(35));
        Equal("Safe dial 42.", readout.Describe(), "the repeat action answers with the current number");
    }

    private static void UnreadableSampleDoesNotInventACloseOrAReading()
    {
        var readout = new ShinraMansionSafeDialReadout();
        readout.Observe(SafeRoom, Dial(12), Start);
        var unavailable = readout.ObserveUnavailable();
        Equal(true, unavailable.IsDialOnScreen, "an unreadable frame does not close the safe");
        Equal(null, unavailable.Speech, "an unreadable frame invents no number");
        Equal(null, readout.CurrentNumber, "a previous value is not current evidence");
        Equal(null, readout.Describe(), "an unreadable frame has no numeric description");
        Equal("Safe dial reading unavailable.", readout.DescribeForRepeat(),
            "explicit repeat explains the failed read instead of replaying an old number");
        Equal(false, readout.IsCurrentSpeech("Safe dial 12."), "stale pending speech is invalidated");
        Equal(true, readout.SuppressesCountdownSpeech, "a torn frame does not let the timer interrupt");
        Equal(null, readout.Observe(SafeRoom, Dial(12), Start.AddMilliseconds(500)).Speech,
            "recovery does not invent another opening");
        Equal(12, readout.CurrentNumber, "recovery restores a current reading");
        readout.Observe(SafeRoom, default, Start.AddMilliseconds(550));
        Equal(false, readout.SuppressesCountdownSpeech, "coherent close releases ownership");
        Equal(null, readout.DescribeForRepeat(), "closed safe leaves ordinary repeat alone");
    }

    private static void StaleSpeechCannotBeRetriedAfterTheDialMoves()
    {
        var readout = new ShinraMansionSafeDialReadout();
        var opening = readout.Observe(SafeRoom, Dial(0), Start).Speech!;
        Equal(true, readout.IsCurrentSpeech(opening), "initial pending speech is current");
        readout.Observe(SafeRoom, Dial(7), Start.AddMilliseconds(35));
        Equal(false, readout.IsCurrentSpeech(opening), "initial speech expires as soon as the value changes");
        Equal(true, readout.IsCurrentSpeech("7."), "only the current number can be retried");
        readout.Reset();
        Equal(false, readout.IsCurrentSpeech("7."), "closed safe cannot replay a number");
    }

    private static void RejectsOtherNumericDisplays()
    {
        var readout = new ShinraMansionSafeDialReadout();
        foreach (var digits in new[] { 0, 1, 3, 6 })
            Equal(false, readout.Observe(SafeRoom, new(true, 7, digits), Start).IsDialOnScreen,
                "the safe is specifically the two digit native display");
    }

    private static string? Settle(int before, int after)
    {
        var readout = new ShinraMansionSafeDialReadout();
        var now = Start;
        readout.Observe(SafeRoom, Dial(before), now);
        now = now.AddMilliseconds(35);
        readout.Observe(SafeRoom, Dial(after), now);
        now += ShinraMansionSafeDialReadout.SettleDelay + TimeSpan.FromMilliseconds(35);
        return readout.Observe(SafeRoom, Dial(after), now).Speech;
    }

    /// <summary>The window the native dial draws: open, numeric, two digits.</summary>
    private static FieldActivityNumericWindow Dial(int value) => new(true, value, 2);

    private static FieldScriptNavigationCatalog Scripts() =>
        new(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")
            ?? throw new InvalidOperationException("FF7_ACCESSIBILITY_DATA_ROOT is not set."));

    private static void AssertBytes(
        IReadOnlyList<FieldScriptOpcodeDefinition> opcodes,
        int byteIndex,
        byte[] expected,
        string label)
    {
        var matches = opcodes.Where(entry => entry.ByteIndex == byteIndex).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"{label}: expected exactly one opcode at byte {byteIndex}, found {matches.Length}.");
        }

        var actual = matches[0].Bytes.ToArray();
        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"{label}: expected {Convert.ToHexString(expected)} at byte {byteIndex}, " +
                $"got {Convert.ToHexString(actual)}.");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}

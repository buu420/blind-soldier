using Ff7.Accessibility.Reloaded;

/// <summary>
/// The player's own chocobo race, which the mod used to say nothing about.
///
/// <para>The report was exact: "I could not really tell if I was speeding up or slowing
/// down during the chocobo race, also I didn't know if I was in manual or auto mode".
/// In the recorded race the mod announced the finishing order about sixty-five times in
/// seventy seconds and never once mentioned either.</para>
///
/// <para>Every quantity here is the one the HUD draws from: the automatic/manual word at
/// the player record's +0x60, the speed at +0x04 that translates the chocobo, the stamina
/// pair at +0x68 and +0x6C the gauge is drawn from, and the turbo animation at +0x80 that
/// only runs once a dash has been accepted. None of it is inferred from a key press, and
/// none of it is another racer's.</para>
///
/// <para>The pace cases carry their own clock. Coalescing is a matter of time rather than
/// of samples - a hundred milliseconds apart and a second apart mean different things about
/// the same two speeds - and a test that cannot move the clock cannot check that.</para>
/// </summary>
internal static class ChocoboRaceControlReadoutTests
{
    private static readonly DateTime Start = new(2026, 9, 8, 13, 58, 40, DateTimeKind.Utc);

    internal static void Run()
    {
        TheModeIsSaidOnEntryAndOnEveryChange();
        PaceIsSpokenFromTheChocobosOwnSpeed();
        NothingAboutPaceIsSaidBeforeTheCountdownEnds();
        ADashIsSpokenOnlyWhenTheGameAcceptedIt();
        StaminaIsSpokenInBandsRatherThanEveryFrame();
        AFinishIsSpokenOnceWithThePlace();
        ABettingRaceSaysNothingAboutControl();
        TheStatusKeyRereadsAllOfIt();
        PickingUpAgainAfterASteadyStretchIsWorthSaying();
        AFlutteringPaceIsCoalescedInsteadOfShouted();
        AnUnknownGaugeIsNotAnEmptyOne();
        AFinishPlaceIsCorrectedWhenTheStripCatchesUp();
        RecoveringStaminaSaysWhatTheGaugeReads();
        RunningOutASecondTimeIsSaidASecondTime();
        AChangeOfPaceThatIsUndoneIsNotAnnouncedLate();
    }

    /// <summary>
    /// Scraping a little stamina back and spending it straight away is the shape of the
    /// whole back half of a race. Running out is the moment the player most needs telling,
    /// and a latch that fires once a race tells them the first time and then leaves them
    /// guessing for the rest of it.
    /// </summary>
    private static void RunningOutASecondTimeIsSaidASecondTime()
    {
        var readout = new ChocoboRaceControlReadout();
        _ = readout.Observe(Racing(started: true, stamina: 10000));
        Contains("Stamina empty.", readout.Observe(Racing(started: true, stamina: 0)),
            "the first time it runs out");
        Equal(0, readout.Observe(Racing(started: true, stamina: 0)).Count,
            "and not on every frame it stays there");

        // Five percent. The same band as nothing at all, and not the same news either way.
        Equal(0, readout.Observe(Racing(started: true, stamina: 500)).Count,
            "a scrape of recovery inside the same band is not worth a sentence");
        Contains("Stamina empty.", readout.Observe(Racing(started: true, stamina: 0)),
            "but spending it again is");
    }

    /// <summary>
    /// The other edge of coalescing. A change held back because something was said a moment
    /// ago must not be spoken once the pace has already gone back the other way: the player
    /// would be told the chocobo was slowing down a second after it stopped doing so.
    /// </summary>
    private static void AChangeOfPaceThatIsUndoneIsNotAnnouncedLate()
    {
        var readout = new ChocoboRaceControlReadout();
        _ = readout.Observe(Racing(started: true, speed: 2000), Start);
        Contains("Speeding up.", readout.Observe(Racing(started: true, speed: 2100), At(0.1d)),
            "the first change is heard at once");

        // Down again, which cannot be said yet, and then up again before it could be.
        _ = readout.Observe(Racing(started: true, speed: 2000), At(0.2d));
        for (var sample = 3; sample <= 15; sample++)
        {
            var lines = readout.Observe(Racing(started: true, speed: 2200), At(sample * 0.1d));
            Equal(false, lines.Any(line => line.Contains("Slowing down", StringComparison.Ordinal)),
                $"a dip that was over before it could be spoken is not announced at sample {sample}");
        }
    }

    private static void TheModeIsSaidOnEntryAndOnEveryChange()
    {
        var readout = new ChocoboRaceControlReadout();
        Contains("Automatic control.", readout.Observe(Racing(automatic: true)),
            "the mode has to be said the moment the race opens");
        Equal(0, readout.Observe(Racing(automatic: true)).Count,
            "and not repeated while nothing has changed");
        Contains("Manual control.", readout.Observe(Racing(automatic: false)),
            "a switch to manual is the player's own doing and must be confirmed");
        Contains("Automatic control.", readout.Observe(Racing(automatic: true)),
            "and back again");
    }

    private static void PaceIsSpokenFromTheChocobosOwnSpeed()
    {
        var readout = new ChocoboRaceControlReadout();
        _ = readout.Observe(Racing(started: true, speed: 1000), Start);
        Equal(0, readout.Observe(Racing(started: true, speed: 1040), At(0.1d)).Count,
            "a speed that has barely moved is not a change worth interrupting for");
        Contains("Speeding up.", readout.Observe(Racing(started: true, speed: 1400), At(0.2d)),
            "a real gain in the speed that moves the chocobo is speeding up");
        Equal(0, readout.Observe(Racing(started: true, speed: 1800), At(0.3d)).Count,
            "still accelerating is the same event, not a second sentence");
        Contains("Slowing down.", readout.Observe(Racing(started: true, speed: 900), At(1.3d)),
            "and a real loss is slowing down");
    }

    private static void NothingAboutPaceIsSaidBeforeTheCountdownEnds()
    {
        // 0x00E710F8 only becomes non-zero at frame 60, when the countdown finishes. Before
        // that nothing is moving, so a "slowing down" would be an invention - but the mode
        // and the stamina are already drawn and are worth reading.
        var readout = new ChocoboRaceControlReadout();
        var entry = readout.Observe(Racing(started: false, automatic: false, speed: 2000));
        Contains("Manual control.", entry, "the mode is on screen at the starting line");
        Contains("Stamina 50 percent.", entry, "and so is the gauge");

        var waiting = readout.Observe(Racing(started: false, speed: 0, automatic: false));
        Equal(false, waiting.Any(line => line.Contains("Slowing", StringComparison.Ordinal)),
            "the countdown is not the player slowing down");
        Equal(false, waiting.Any(line => line.Contains("Speeding", StringComparison.Ordinal)),
            "nor speeding up");
    }

    private static void ADashIsSpokenOnlyWhenTheGameAcceptedIt()
    {
        var readout = new ChocoboRaceControlReadout();
        _ = readout.Observe(Racing(started: true));
        Contains("Dashing.", readout.Observe(Racing(started: true, dashing: true)),
            "the turbo animation means the dash was accepted and stamina is going into it");
        Equal(false, readout.Observe(Racing(started: true, dashing: true))
                .Any(line => line.Contains("Dashing", StringComparison.Ordinal)),
            "and it is said once, not on every frame it lasts");
        _ = readout.Observe(Racing(started: true));
        Contains("Dashing.", readout.Observe(Racing(started: true, dashing: true)),
            "a fresh dash is a fresh announcement");
    }

    private static void StaminaIsSpokenInBandsRatherThanEveryFrame()
    {
        var readout = new ChocoboRaceControlReadout();
        _ = readout.Observe(Racing(started: true, stamina: 10000));
        Equal(0, readout.Observe(Racing(started: true, stamina: 9000)).Count,
            "a bar that has dropped a little is not worth a sentence");
        Contains("Stamina 50 percent.", readout.Observe(Racing(started: true, stamina: 5000)),
            "crossing half is");
        Contains("Stamina 5 percent.", readout.Observe(Racing(started: true, stamina: 500)),
            "and the number said is the one the gauge is showing, not the band it fell past");
        Contains("Stamina empty.", readout.Observe(Racing(started: true, stamina: 0)),
            "an empty gauge has to be unmistakable");
    }

    private static void AFinishIsSpokenOnceWithThePlace()
    {
        var readout = new ChocoboRaceControlReadout();
        _ = readout.Observe(Racing(started: true, place: 2));
        Contains("Finished second.", readout.Observe(Racing(started: true, place: 2, finished: true)),
            "the player's own finish is the one result they cannot see");
        Equal(0, readout.Observe(Racing(started: true, place: 2, finished: true)).Count,
            "and it is said once");
    }

    private static void ABettingRaceSaysNothingAboutControl()
    {
        var readout = new ChocoboRaceControlReadout();
        Equal(0, readout.Observe(default).Count,
            "a race the player is only betting on has no control state of theirs to read");
        Equal("You are not riding in this race.", ChocoboRaceControlReadout.Describe(default),
            "and the status key says so plainly rather than inventing one");
    }

    private static void TheStatusKeyRereadsAllOfIt()
    {
        var status = ChocoboRaceControlReadout.Describe(
            Racing(started: true, automatic: false, place: 3, stamina: 2500, dashing: true));
        foreach (var expected in new[] { "Manual control.", "third place.", "Stamina 25 percent.", "Dashing." })
        {
            Equal(true, status.Contains(expected, StringComparison.Ordinal),
                $"the status key must reread '{expected}': {status}");
        }

        var waiting = ChocoboRaceControlReadout.Describe(Racing(started: false));
        Equal(true, waiting.Contains("Waiting for the start.", StringComparison.Ordinal),
            $"and must say when the race has not begun: {waiting}");
    }

    /// <summary>
    /// The first version latched on direction alone: once "speeding up" had been said,
    /// accelerating again was never mentioned until the player had slowed down in between.
    /// Root's harness catches it, and so does a player - hold a steady pace down the back
    /// straight and then open up, and a sighted player watches the chocobo pull away while
    /// a blind one hears nothing at all.
    /// </summary>
    private static void PickingUpAgainAfterASteadyStretchIsWorthSaying()
    {
        var readout = new ChocoboRaceControlReadout();
        _ = readout.Observe(Racing(started: true, speed: 2000), Start);
        Contains("Speeding up.", readout.Observe(Racing(started: true, speed: 2400), At(0.1d)),
            "the first acceleration");

        // Two seconds holding it there. Twenty samples, nothing to say about any of them.
        for (var sample = 1; sample <= 20; sample++)
        {
            Equal(0, readout.Observe(Racing(started: true, speed: 2400), At(0.1d + (sample * 0.1d))).Count,
                $"a steady pace is not news at sample {sample}");
        }

        Contains("Speeding up.", readout.Observe(Racing(started: true, speed: 2800), At(2.3d)),
            "and opening up again after the plateau is news again");
    }

    /// <summary>
    /// The other half of the same problem. A docile chocobo on a rough segment moves the
    /// speed word up and down by a hundred every sample; a bare threshold turns that into
    /// twenty contrary sentences in two seconds, on top of everything else being said.
    /// </summary>
    private static void AFlutteringPaceIsCoalescedInsteadOfShouted()
    {
        var readout = new ChocoboRaceControlReadout();
        _ = readout.Observe(Racing(started: true, speed: 2200), Start);

        var paceLines = 0;
        for (var sample = 1; sample <= 20; sample++)
        {
            paceLines += readout
                .Observe(Racing(started: true, speed: sample % 2 == 1 ? 2300 : 2200), At(sample * 0.1d))
                .Count(line =>
                    line.Contains("Speeding up", StringComparison.Ordinal) ||
                    line.Contains("Slowing down", StringComparison.Ordinal));
        }

        Equal(true, paceLines is > 0 and <= 4,
            $"two seconds of flutter is at most a few sentences, and got {paceLines}");
    }

    /// <summary>
    /// The native gauge is drawn as current over maximum, so a maximum of zero means the
    /// gauge cannot be worked out at all. Calling that empty invents the single most
    /// alarming reading the bar has - and the old code managed to call it both unknown and
    /// empty in the same breath.
    /// </summary>
    private static void AnUnknownGaugeIsNotAnEmptyOne()
    {
        var readout = new ChocoboRaceControlReadout();
        var entry = readout.Observe(
            new ChocoboRaceControlState(true, true, false, 2000, 0, 0, false, false, 0), Start);
        Equal(true, entry.Any(line => line.Contains("Stamina unknown", StringComparison.Ordinal)),
            $"an unreadable gauge is unknown: {string.Join(" | ", entry)}");
        Equal(false, entry.Any(line => line.Contains("empty", StringComparison.OrdinalIgnoreCase)),
            "and never empty");

        for (var sample = 1; sample <= 20; sample++)
        {
            var lines = readout.Observe(
                new ChocoboRaceControlState(true, true, false, 2000, 0, 0, false, false, 0),
                At(sample * 0.1d));
            Equal(false, lines.Any(line => line.Contains("Stamina", StringComparison.Ordinal)),
                "and an unknown gauge never becomes a low-stamina cue either");
        }

        Equal(true,
            ChocoboRaceControlReadout
                .Describe(new ChocoboRaceControlState(true, true, false, 2000, 0, 0, false, false, 0))
                .Contains("Stamina unknown", StringComparison.Ordinal),
            "and the status key says the same thing");
    }

    /// <summary>
    /// The ranking strip refreshes about sixteen frames after a chocobo crosses the line,
    /// so the first sample with the finished flag set can still be carrying the place from
    /// before it. What the strip settles on is what the player is being shown, and a
    /// permanent latch on the first answer leaves them believing a result that is not on
    /// screen.
    /// </summary>
    private static void AFinishPlaceIsCorrectedWhenTheStripCatchesUp()
    {
        var readout = new ChocoboRaceControlReadout();
        _ = readout.Observe(Racing(started: true, place: 1));
        Contains("Finished first.", readout.Observe(Racing(started: true, place: 1, finished: true)),
            "the finish is spoken as soon as it happens");
        Contains("Finished second.", readout.Observe(Racing(started: true, place: 2, finished: true)),
            "and corrected when the strip settles on something else");
        Equal(0, readout.Observe(Racing(started: true, place: 2, finished: true)).Count,
            "then left alone");
    }

    /// <summary>
    /// Stamina climbing back is worth a word, and the word has to be the gauge's own
    /// reading. "Recovered" at seventy-six percent tells the player they are full when a
    /// quarter of the bar is missing.
    /// </summary>
    private static void RecoveringStaminaSaysWhatTheGaugeReads()
    {
        var readout = new ChocoboRaceControlReadout();
        _ = readout.Observe(Racing(started: true, stamina: 10000));
        Contains("Stamina 20 percent.", readout.Observe(Racing(started: true, stamina: 2000)),
            "spending it is news");
        var recovered = readout.Observe(Racing(started: true, stamina: 7600));
        Contains("Stamina 76 percent.", recovered,
            "and so is getting it back - as the number on the gauge");
        Equal(false, recovered.Any(line => line.Contains("recovered", StringComparison.OrdinalIgnoreCase)),
            "never as a claim of full recovery it cannot make");
    }

    private static DateTime At(double seconds) => Start.AddSeconds(seconds);

    private static ChocoboRaceControlState Racing(
        bool started = false,
        bool automatic = true,
        int speed = 0,
        int stamina = 5000,
        bool dashing = false,
        bool finished = false,
        int place = 0) =>
        new(true, started, automatic, speed, stamina, 10000, dashing, finished, place);

    private static void Contains(string expected, IReadOnlyList<string> lines, string message)
    {
        if (!lines.Contains(expected))
        {
            throw new InvalidOperationException(
                $"Chocobo race control: {message}. Expected '{expected}', got " +
                (lines.Count == 0 ? "silence" : string.Join(" | ", lines)) + ".");
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Chocobo race control: {message}. Expected {expected}, actual {actual}.");
        }
    }
}

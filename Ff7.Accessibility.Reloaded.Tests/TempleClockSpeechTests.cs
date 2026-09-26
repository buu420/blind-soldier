using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// What the Temple of the Ancients clock (607 kuro_4) says on its own while the Time
/// Guardian's hands move, replayed through the real readout against a model of the native
/// hands. The player asked for "Clock time, such as Moving, ten thirty.": the short hand gives
/// the hour and the long hand the minutes, its numeral times five, read from the rendered
/// bearings only.
///
/// <para>The hands are modelled from kuro_4's own scripts and the engine's own turn. Every
/// hand turn is one TURNGEN (opcode B4, FUN_00617b0f): its steps operand is how many native
/// updates the turn takes, and FUN_006342c6 moves the angle once per update - linearly
/// through FUN_006430a4 for mode 1, eased through FUN_006430f2 and its cosine table for mode
/// 2 - both reproduced in <see cref="NativeBearing"/>. Move it myself turns the long hand 6
/// updates per numeral (e21 script 4 forward, linear; script 7 back, eased) and carries or
/// borrows the short hand over 10, eased (e22 scripts 4 and 5), when the long hand passes
/// twelve. Spin turns the long hand one numeral per update and the short hand back one
/// numeral per 4, both linear; on OK the long hand slows over 8, 8 and 10 updates, linear,
/// and the short hand over 10 and 10, linear, and a last 10, eased (e22 script 5). The native
/// update is taken as 1/30 s and the time between script steps as a fixed number of updates;
/// the bridge lock timing is simplified as described on <see cref="NativeClock"/>. The host
/// samples every 16, 50 or 100 ms.</para>
/// </summary>
internal static class TempleClockSpeechTests
{
    private const double NativeUpdate = 1.0 / 30;
    private static readonly int[] HourBearings = [128, 106, 86, 64, 42, 22, 0, 234, 214, 192, 170, 150];
    private static readonly int[][] BridgePairs =
        [[93, 2], [78, 12], [79, 0], [89, 66], [72, 60], [73, 54], [85, 48], [74, 42], [75, 36], [81, 14], [76, 10], [77, 6]];
    private static readonly double[] HostIntervals = [0.016, 0.050, 0.100];
    private static readonly DateTime Epoch = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

    internal static void Run()
    {
        OneTapSaysMovingThenTheNewTime();
        HeldForwardKeepsTellingTheTimeAndCarriesTheHour();
        HeldBackwardBorrowsTheHour();
        TheSpinsBackwardShortHandIsReadAsItIsSeen();
        StoppedWaitsForBothHandsToStayStill();
        QuickPressesSayEachTimeTheyStopOn();
        APressSoonAfterSpeechSaysOnlyTheNewTime();
        TheSecondHandSaysNothingOnItsOwn();
        AutomaticSpeechIsOnlyMovementAndTime();
        TheRepeatKeepsTheDetailAndChangesNothing();
        TheRepeatAndTheWaitCheckChangeNothing();
        AnUnreadableClockForgetsItsTime();
        AFirstLookClaimsNothingUntilTheHandsAreWatched();
        AResetDuringASpinIsNotAStop();
        ATornLookDuringASpinIsNotAStop();
        AnEasedTurnIsFollowedToItsEnd();
    }

    // --- the native hands ---------------------------------------------------------------

    /// <summary>
    /// One TURNGEN: a hand moving one numeral forward or back over <paramref name="Steps"/>
    /// native updates, linearly (mode 1) or eased (mode 2).
    /// </summary>
    private readonly record struct Turn(int StartUpdate, bool Long, int FromHour, int ToHour, int Steps, bool Forward, bool Eased = false);

    /// <summary>
    /// The table FUN_00636500 reads (0x00908E32, one 16-bit value every 4 bytes). In the
    /// installed ff7_en.exe every entry is within 1 of 4096 cos(2 pi k / 256); this is that
    /// cosine, so an eased bearing here can differ from the game's by at most a unit.
    /// </summary>
    private static readonly int[] NativeCosine =
        Enumerable.Range(0, 256).Select(k => (int)Math.Round(4096 * Math.Cos(2 * Math.PI * k / 256))).ToArray();

    /// <summary>
    /// The bearing after <paramref name="progress"/> of <paramref name="steps"/> updates, as the
    /// engine computes it. FUN_00617b0f takes the target and, for a turn in a set direction,
    /// moves it a whole turn so that it lies that way from the start (direction 1 is the
    /// falling bearing, forward on this clock). FUN_006430a4 then interpolates linearly with C
    /// division, and FUN_006430f2 eases with the cosine table.
    /// </summary>
    private static int NativeBearing(int from, int to, bool forward, int progress, int steps, bool eased)
    {
        var target = to;
        if (!forward && target < from) target += 256;
        if (forward && target > from) target -= 256;
        var arc = target - from;
        int value;
        if (!eased)
        {
            value = from + arc * progress / steps;
        }
        else
        {
            var fraction = (progress << 12) / steps;
            var index = (sbyte)unchecked((byte)(((fraction + ((fraction >> 31) & 0x1F)) >> 5) - 0x80));
            var scaled = (NativeCosine[index & 0xFF] + 0x1000) * arc;
            value = from + ((scaled + ((scaled >> 31) & 0x1FFF)) >> 13);
        }

        return Mod(value);
    }

    /// <summary>
    /// The clock's hands after a list of turns, one native update at a time, and the bridges
    /// that are open. The bridge model is simplified, and says only what the tests rely on:
    /// in Move it myself a hand's scripts lock and unlock after its TURNGEN has finished
    /// (e21 scripts 4 and 7, e22 script 6), so here a turning hand keeps the pair of the
    /// numeral it left open and opens the new one on the update its turn ends; a spin locks
    /// all twenty-four before it starts (e5 script 10 requests IDdr script 1 first), so here
    /// everything is locked from the spin's first update until each hand's last turn ends.
    /// The exact update on which a script's unlock runs is not modelled. No clock line spoken
    /// on its own depends on the bridges; the repeat's bridge wording is covered with explicit
    /// lock states elsewhere.
    /// </summary>
    private sealed class NativeClock(int longHour, int shortHour)
    {
        private readonly List<Turn> turns = [];
        private readonly int firstLongHour = longHour;
        private readonly int firstShortHour = shortHour;
        private (int From, int To)? lockedAll;
        public int LongHour { get; private set; } = longHour;
        public int ShortHour { get; private set; } = shortHour;
        public int LastUpdate { get; private set; }

        public NativeClock Add(Turn turn)
        {
            turns.Add(turn);
            LastUpdate = Math.Max(LastUpdate, turn.StartUpdate + turn.Steps);
            if (turn.Long) LongHour = turn.ToHour; else ShortHour = turn.ToHour;
            return this;
        }

        /// <summary>A spin: every bridge locked from <paramref name="from"/> until <paramref name="to"/>.</summary>
        public NativeClock LockAll(int from, int to)
        {
            lockedAll = (from, to);
            return this;
        }

        /// <summary>The rendered bearings after <paramref name="update"/> native updates, and the open bridges.</summary>
        public (int Long, int Short, IReadOnlySet<int> OpenHours) At(int update)
        {
            (int Bearing, int OpenHour) Hand(bool isLong, int startHour)
            {
                var bearing = HourBearings[startHour];
                var open = startHour;
                foreach (var turn in turns.Where(t => t.Long == isLong).OrderBy(t => t.StartUpdate))
                {
                    if (update <= turn.StartUpdate) break;
                    var done = Math.Min(update - turn.StartUpdate, turn.Steps);
                    bearing = NativeBearing(HourBearings[turn.FromHour], HourBearings[turn.ToHour], turn.Forward, done, turn.Steps, turn.Eased);
                    open = done < turn.Steps ? turn.FromHour : turn.ToHour;
                }

                return (bearing, open);
            }

            var (longBearing, longOpen) = Hand(true, firstLongHour);
            var (shortBearing, shortOpen) = Hand(false, firstShortHour);
            var openHours = lockedAll is { } spin && update > spin.From && update < spin.To
                ? new HashSet<int>()
                : new HashSet<int> { longOpen, shortOpen };
            return (longBearing, shortBearing, openHours);
        }
    }

    private static int Mod(int value) => ((value % 256) + 256) % 256;

    /// <summary>
    /// Move it myself: OK (forward) or MENU (back) pressed or held, <paramref name="count"/>
    /// numerals, one TURNGEN each - e21 script 4 forward, linear over 6 updates; e21 script 7
    /// back, eased over 6 - with <paramref name="gap"/> updates of script between steps. Passing
    /// twelve first requests the short hand's own eased 10-update turn, e22 script 4 to carry
    /// or script 5 to borrow, which runs alongside the long hand's.
    /// </summary>
    // The first press comes two seconds in, after the clock has been read on entering.
    private static NativeClock Tap(int longHour, int shortHour, int count, bool forward, int firstUpdate = 60, int gap = 2)
    {
        var clock = new NativeClock(longHour, shortHour);
        var update = firstUpdate;
        var l = longHour;
        var s = shortHour;
        for (var i = 0; i < count; i++)
        {
            var next = forward ? (l + 1) % 12 : (l + 11) % 12;
            if (forward && next == 0 || !forward && l == 0)
            {
                var nextShort = forward ? (s + 1) % 12 : (s + 11) % 12;
                clock.Add(new Turn(update, false, s, nextShort, 10, forward, Eased: true));
                s = nextShort;
            }

            clock.Add(new Turn(update, true, l, next, 6, forward, Eased: !forward));
            l = next;
            update += 6 + gap;
        }

        return clock;
    }

    /// <summary>
    /// Spin, from ten ten, for <paramref name="spinUpdates"/> updates then OK: e21 script 10
    /// turns the long hand one numeral per update, linearly; e22 script 7 turns the short hand
    /// one numeral back per 4 updates, linearly. On OK the long hand slows over e21 script 11
    /// twice (8 updates each, linear) and script 6 (10, linear); the short hand over e22 script
    /// 8 twice (10 each, linear) and then script 5 (10, eased). Every bridge is locked throughout.
    /// </summary>
    private static (NativeClock Clock, int LongHour, int ShortHour) Spin(int start, int spinUpdates)
    {
        var clock = new NativeClock(2, 10);
        int l = 2, s = 10;
        for (var i = 0; i < spinUpdates; i++)
        {
            clock.Add(new Turn(start + i, true, l, (l + 1) % 12, 1, true));
            l = (l + 1) % 12;
        }

        for (var i = 0; i < spinUpdates / 4; i++)
        {
            clock.Add(new Turn(start + i * 4, false, s, (s + 11) % 12, 4, false));
            s = (s + 11) % 12;
        }

        var slow = start + spinUpdates;
        foreach (var steps in new[] { 8, 8, 10 })
        {
            clock.Add(new Turn(slow, true, l, (l + 1) % 12, steps, true));
            l = (l + 1) % 12;
            slow += steps;
        }

        slow = start + spinUpdates;
        foreach (var (steps, eased) in new[] { (10, false), (10, false), (10, true) })
        {
            clock.Add(new Turn(slow, false, s, (s + 11) % 12, steps, false, eased));
            s = (s + 11) % 12;
            slow += steps;
        }

        clock.LockAll(start, clock.LastUpdate);
        return (clock, l, s);
    }

    // --- sampling through the readout ---------------------------------------------------

    private static FieldActivityObservation Clock(
        int longBearing, int shortBearing, int secondBearing, IReadOnlySet<int> openHours, int triangle = 26) =>
        new(607, 0, 0, 0, triangle, false, 618, new FieldNavigationControlTransform(0), true,
            [Hand(21, longBearing), Hand(22, shortBearing), Hand(23, secondBearing)],
            new Dictionary<int, FieldActivityWaitState>(), _ => true,
            triangle => !BridgePairs.Where((_, hour) => openHours.Contains(hour)).SelectMany(pair => pair).Contains(triangle), 0);

    private static FieldActivityModelReading Hand(int entityId, int bearing) =>
        new(entityId, FieldActivityReadStatus.Visible, new FieldActivityModelState(true, true, 0, 0, 0, 0, bearing));

    private static int SecondHandAt(int update) => Mod(128 - update / 30 * 4);

    private readonly record struct Spoken(double Time, string Text);

    /// <summary>Samples the clock every <paramref name="interval"/> seconds, as a host tick would.</summary>
    private static List<Spoken> Listen(NativeClock clock, double interval, double seconds, FieldActivityReadout? readout = null,
        Action<FieldActivityReadout, FieldActivityObservation>? between = null)
    {
        readout ??= new FieldActivityReadout();
        var spoken = new List<Spoken>();
        for (var t = 0.0; t <= seconds + 1e-9; t += interval)
        {
            var update = (int)Math.Floor(t / NativeUpdate + 1e-9);
            var (l, s, open) = clock.At(update);
            var observation = Clock(l, s, SecondHandAt(update), open);
            between?.Invoke(readout, observation);
            if (readout.Observe(observation, Epoch.AddSeconds(t)).Speech is { } speech)
            {
                spoken.Add(new Spoken(t, speech));
            }

            between?.Invoke(readout, observation);
        }

        return spoken;
    }

    private static double LastChange(NativeClock clock) => clock.LastUpdate * NativeUpdate;

    private static string Show(IEnumerable<Spoken> lines) =>
        string.Join(" | ", lines.Select(line => $"{line.Time:0.000}s {line.Text}"));

    // --- the cases ----------------------------------------------------------------------

    /// <summary>
    /// One press of OK in Move it myself, ten ten to ten fifteen: the hand is said to be moving
    /// as it goes and the new time once it has come to rest, and nothing else.
    /// </summary>
    private static void OneTapSaysMovingThenTheNewTime()
    {
        foreach (var interval in HostIntervals)
        {
            var clock = Tap(longHour: 2, shortHour: 10, count: 1, forward: true);
            var spoken = Listen(clock, interval, 3);
            Equal("Stopped, ten ten.", spoken.First().Text, $"{interval * 1000}ms: the clock as it stands on entering ({Show(spoken)})");
            Equal(true, spoken.First().Time >= 0.35 - 1e-9,
                $"{interval * 1000}ms: once the hands have been watched still for the settle time ({Show(spoken)})");
            var after = spoken.Skip(1).ToList();
            Equal(true, after.Count is 2, $"{interval * 1000}ms: one moving line and one stopped line ({Show(spoken)})");
            Equal(true, after[0].Text.StartsWith("Moving, ", StringComparison.Ordinal),
                $"{interval * 1000}ms: the turn is said as it starts ({Show(spoken)})");
            // The turn starts on update 60; the first bearing that differs is rendered on update 61.
            Equal(true, after[0].Time <= 61 * NativeUpdate + interval + 1e-9,
                $"{interval * 1000}ms: straight away, at the first sample that sees the hand move ({Show(spoken)})");
            Equal("Stopped, ten fifteen.", after[1].Text, $"{interval * 1000}ms: then the new time ({Show(spoken)})");
            var settled = LastChange(clock) + 0.35;
            Equal(true, after[1].Time >= settled - 1e-9 && after[1].Time <= settled + interval + 1e-9,
                $"{interval * 1000}ms: once both hands have been still for the settle time ({Show(spoken)})");
        }
    }

    /// <summary>
    /// OK held for fourteen numerals: the clock keeps saying what time it shows while it moves,
    /// at a pace a listener can take, and passing twelve carries the short hand from ten to
    /// eleven. The final time is eleven twenty.
    /// </summary>
    private static void HeldForwardKeepsTellingTheTimeAndCarriesTheHour()
    {
        foreach (var interval in HostIntervals)
        {
            var clock = Tap(longHour: 2, shortHour: 10, count: 14, forward: true);
            var spoken = Listen(clock, interval, LastChange(clock) + 1.5);
            var moving = spoken.Where(line => line.Text.StartsWith("Moving, ", StringComparison.Ordinal)).ToList();
            Equal(true, moving.Count >= 3, $"{interval * 1000}ms: the time is told during the movement, not only after it ({Show(spoken)})");
            for (var i = 1; i < moving.Count; i++)
            {
                Equal(true, moving[i].Time - moving[i - 1].Time >= 1.0 - 1e-9,
                    $"{interval * 1000}ms: moving updates are bounded, not one per sample ({Show(spoken)})");
            }

            Equal(true, moving.Any(line => line.Text.Contains("eleven", StringComparison.Ordinal)),
                $"{interval * 1000}ms: the carried hour is heard while the hands are still going ({Show(spoken)})");
            Equal(false, spoken.Any(line => line.Text.StartsWith("Stopped", StringComparison.Ordinal) &&
                                            line.Time > 0.5 && line.Time < LastChange(clock)),
                $"{interval * 1000}ms: never stopped between held steps ({Show(spoken)})");
            Equal("Stopped, eleven twenty.", spoken.Last().Text, $"{interval * 1000}ms: the time it comes to rest on ({Show(spoken)})");
        }
    }

    /// <summary>MENU held for four numerals from ten ten: back past twelve borrows the hour, nine fifty.</summary>
    private static void HeldBackwardBorrowsTheHour()
    {
        foreach (var interval in HostIntervals)
        {
            var clock = Tap(longHour: 2, shortHour: 10, count: 4, forward: false);
            var spoken = Listen(clock, interval, LastChange(clock) + 1.5);
            Equal("Stopped, nine fifty.", spoken.Last().Text, $"{interval * 1000}ms: back through twelve ({Show(spoken)})");
            Equal(1, spoken.Count(line => line.Text.StartsWith("Stopped", StringComparison.Ordinal) && line.Time > 0.5),
                $"{interval * 1000}ms: one stop for one held press ({Show(spoken)})");
        }
    }

    /// <summary>
    /// Spin: the long hand races forward while the short hand walks backwards on its own. The
    /// clock reads what the hands show - a time no real clock passes through - and ends on the
    /// numerals they come to rest on, not on anything predicted.
    /// </summary>
    private static void TheSpinsBackwardShortHandIsReadAsItIsSeen()
    {
        foreach (var interval in HostIntervals)
        {
            // Three seconds of spin from one second in, then OK.
            var (clock, l, s) = Spin(start: 30, spinUpdates: 90);
            var spoken = Listen(clock, interval, LastChange(clock) + 1.5);
            var moving = spoken.Where(line => line.Text.StartsWith("Moving, ", StringComparison.Ordinal)).ToList();
            Equal(true, moving.Count >= 2, $"{interval * 1000}ms: the spin is told as it goes ({Show(spoken)})");
            Equal($"Stopped, {Hour(s)} {Minutes(l)}.", spoken.Last().Text,
                $"{interval * 1000}ms: the numerals the hands came to rest on ({Show(spoken)})");
        }
    }

    /// <summary>
    /// A hand is not stopped because two samples in a row saw the same bearing: at 16 ms the
    /// host sees every native update twice. Nor is a clock stopped while either hand has moved
    /// within the settle time, or while the long hand passes through a numeral mid-spin.
    /// </summary>
    private static void StoppedWaitsForBothHandsToStayStill()
    {
        // A made-up pair of turns, not a native sequence: the long hand reaches twelve in 6
        // updates while the short hand is still going over 20. Not stopped until the short
        // hand is still as well.
        var clock = new NativeClock(11, 10)
            .Add(new Turn(30, false, 10, 11, 20, true))
            .Add(new Turn(30, true, 11, 0, 6, true));
        foreach (var interval in HostIntervals)
        {
            var spoken = Listen(clock, interval, 4);
            var stops = spoken.Where(line => line.Text.StartsWith("Stopped", StringComparison.Ordinal) && line.Time > 0.5).ToList();
            Equal(1, stops.Count, $"{interval * 1000}ms: one stop ({Show(spoken)})");
            Equal("Stopped, eleven o'clock.", stops[0].Text, $"{interval * 1000}ms: twelve on the long hand is o'clock ({Show(spoken)})");
            Equal(true, stops[0].Time >= LastChange(clock) + 0.35 - 1e-9,
                $"{interval * 1000}ms: only after the short hand too has been still ({Show(spoken)})");
        }
    }

    /// <summary>
    /// Pressing OK again and again, each press coming to rest before the next: every time the
    /// hands stop on is said, and no moving line cuts one of them off a moment after it began.
    /// </summary>
    private static void QuickPressesSayEachTimeTheyStopOn()
    {
        foreach (var interval in HostIntervals)
        {
            // 6 updates of turn and 15 of rest: half a second between presses, longer than the
            // settle time, so each press does come to rest.
            var clock = Tap(longHour: 2, shortHour: 10, count: 4, forward: true, gap: 15);
            var spoken = Listen(clock, interval, LastChange(clock) + 1.5).Skip(1).ToList();
            // The first moving line is exact or "about" depending on whether the sample that
            // saw the hand start caught it still within its numeral.
            Equal(true, spoken[0].Text is "Moving, ten ten." or "Moving, about ten ten.",
                $"{interval * 1000}ms: the run starts from rest ({Show(spoken)})");
            Equal("Stopped, ten fifteen.|Stopped, ten twenty.|Stopped, ten twenty-five.|Stopped, ten thirty.",
                string.Join("|", spoken.Skip(1).Select(line => line.Text)),
                $"{interval * 1000}ms: each new time, and no moving line in between ({Show(spoken)})");
        }
    }

    /// <summary>
    /// A press a second after the clock last spoke: the moving reading is not yet due while the
    /// hand turns, and once it is due the hand is only settling on its numeral, so it is not
    /// said at all - it would be cut off by the stop a moment later. The new time is.
    /// </summary>
    private static void APressSoonAfterSpeechSaysOnlyTheNewTime()
    {
        foreach (var interval in HostIntervals)
        {
            var clock = Tap(longHour: 2, shortHour: 10, count: 1, forward: true, firstUpdate: 30);
            var spoken = Listen(clock, interval, 3);
            Equal("Stopped, ten ten.|Stopped, ten fifteen.", string.Join("|", spoken.Select(line => line.Text)),
                $"{interval * 1000}ms: only the time the press came to rest on ({Show(spoken)})");
        }
    }

    /// <summary>The second hand never stops and is not one of the hands the player sets.</summary>
    private static void TheSecondHandSaysNothingOnItsOwn()
    {
        var readout = new FieldActivityReadout();
        var open = new HashSet<int> { 2, 10 };
        Equal(null, readout.Observe(Clock(86, 170, 128, open), Epoch).Speech, "a first look claims nothing");
        Equal("Stopped, ten ten.", readout.Observe(Clock(86, 170, 124, open), Epoch.AddSeconds(0.4)).Speech,
            "the hands watched still, the second hand moving: stopped");
        for (var tick = 1; tick <= 120; tick++)
        {
            Equal(null, readout.Observe(Clock(86, 170, Mod(128 - tick * 4), open), Epoch.AddSeconds(tick * 0.5)).Speech,
                $"a second hand at {Mod(128 - tick * 4)} is not a reason to speak");
        }
    }

    /// <summary>No place, bridge or second hand in what is said on its own; only movement and time.</summary>
    private static void AutomaticSpeechIsOnlyMovementAndTime()
    {
        var clock = Tap(longHour: 2, shortHour: 10, count: 14, forward: true);
        var spoken = Listen(clock, 0.050, LastChange(clock) + 1.5);
        foreach (var line in spoken)
        {
            Equal(true, System.Text.RegularExpressions.Regex.IsMatch(line.Text,
                    @"^(Moving|Stopped), (about )?(twelve|one|two|three|four|five|six|seven|eight|nine|ten|eleven) " +
                    @"(o'clock|oh five|ten|fifteen|twenty|twenty-five|thirty|thirty-five|forty|forty-five|fifty|fifty-five)\.$"),
                $"'{line.Text}' is movement and a time, nothing more");
            Equal(true, line.Text.Split(' ').Length <= 5, $"'{line.Text}' is short");
        }

        // A hand between numerals is not claimed to be on one.
        var mid = new FieldActivityReadout();
        _ = mid.Observe(Clock(86, 170, 128, new HashSet<int> { 2, 10 }), Epoch);
        Equal("Moving, about ten ten.", mid.Observe(Clock(76, 170, 128, new HashSet<int>()), Epoch.AddSeconds(2)).Speech,
            "the long hand just past two reads as about ten past");
    }

    /// <summary>
    /// The repeat action keeps everything a sighted player can see - where the party stands,
    /// all three hands and which bridges are really there - and asking for it, or asking
    /// whether the field waits for a button, changes nothing about what is said next.
    /// </summary>
    private static void TheRepeatKeepsTheDetailAndChangesNothing()
    {
        var open = new HashSet<int> { 6, 10 };
        var readout = new FieldActivityReadout();
        _ = readout.Observe(Clock(0, 170, 64, open), Epoch);
        Equal(
            "Ten thirty. You are by doorway ten. Long hand at six, short hand at ten, second hand at three. " +
            "The bridge to doorway six is open. The bridge to doorway ten is open.",
            readout.Describe(Clock(0, 170, 64, open)),
            "before the hands have been watched long enough, the repeat gives the time alone, neither moving nor stopped");
        _ = readout.Observe(Clock(0, 170, 64, open), Epoch.AddSeconds(0.4));
        Equal(
            "Stopped, ten thirty. You are by doorway ten. Long hand at six, short hand at ten, second hand at three. " +
            "The bridge to doorway six is open. The bridge to doorway ten is open.",
            readout.Describe(Clock(0, 170, 64, open)),
            "the repeat says the time, the place, all three hands and both bridges");
        Equal(false, readout.IsWaitingForInput(Clock(0, 170, 64, open)), "the clock itself never waits on a button");

        // Mid-turn the repeat says the hands are moving and claims no bridge.
        var moving = new FieldActivityReadout();
        _ = moving.Observe(Clock(86, 170, 128, new HashSet<int> { 2, 10 }), Epoch);
        _ = moving.Observe(Clock(76, 170, 128, new HashSet<int>()), Epoch.AddSeconds(0.1));
        Equal(
            "Moving, about ten ten. You are by doorway ten. Long hand between two and three, short hand at ten, second hand at twelve. " +
            "The bridge to doorway ten is not open.",
            moving.Describe(Clock(76, 170, 128, new HashSet<int>())),
            "a turning hand claims no bridge, and the short hand's is really shut while it turns");
    }

    /// <summary>
    /// Asking for the repeat line, or whether the field waits for a button, is only a question:
    /// the same timeline says the same things whether or not they are asked on every tick, and
    /// the repeat asked twice says the same thing twice.
    /// </summary>
    private static void TheRepeatAndTheWaitCheckChangeNothing()
    {
        foreach (var interval in HostIntervals)
        {
            var clock = Tap(longHour: 2, shortHour: 10, count: 5, forward: true, gap: 0);
            var plain = Listen(clock, interval, LastChange(clock) + 1.5);
            var repeats = new List<string>();
            var asked = Listen(clock, interval, LastChange(clock) + 1.5, between: (r, observation) =>
            {
                var first = r.Describe(observation);
                var second = r.Describe(observation);
                _ = r.IsWaitingForInput(observation);
                if (first != second) repeats.Add($"{first} / {second}");
            });
            Equal(Show(plain), Show(asked), $"{interval * 1000}ms: the repeat and the wait check are only questions");
            Equal(0, repeats.Count, $"{interval * 1000}ms: the repeat asked twice says the same thing ({string.Join(" | ", repeats.Take(2))})");
        }
    }

    /// <summary>A clock that could not be read is not a clock showing the last time it did.</summary>
    private static void AnUnreadableClockForgetsItsTime()
    {
        var readout = new FieldActivityReadout();
        var open = new HashSet<int> { 2, 10 };
        _ = readout.Observe(Clock(86, 170, 128, open), Epoch);
        Equal("Stopped, ten ten.", readout.Observe(Clock(86, 170, 128, open), Epoch.AddSeconds(0.4)).Speech, "before");
        _ = readout.Observe(Clock(76, 170, 128, new HashSet<int>()), Epoch.AddSeconds(0.5));
        Equal("Cannot read the clock hands.", readout.Observe(Torn(), Epoch.AddSeconds(0.6)).Speech, "said once");
        Equal(null, readout.Observe(Torn(), Epoch.AddSeconds(0.7)).Speech, "and not again");
        Equal(true, readout.Describe(Torn())?.Contains("ten ten", StringComparison.Ordinal) != true, "the repeat has no stale time");
        var three = new HashSet<int> { 3, 10 };
        Equal(null, readout.Observe(Clock(64, 170, 128, three), Epoch.AddSeconds(0.8)).Speech,
            "one readable look again is only where the hands are, not that they are at rest");
        Equal("Ten fifteen. You are by doorway ten. Long hand at three, short hand at ten, second hand at twelve. " +
              "The bridge to doorway three is open. The bridge to doorway ten is open.",
            readout.Describe(Clock(64, 170, 128, three)), "the repeat gives the time alone meanwhile");
        Equal("Stopped, ten fifteen.", readout.Observe(Clock(64, 170, 128, three), Epoch.AddSeconds(1.2)).Speech,
            "and once watched still, it is read afresh");
    }

    private static FieldActivityObservation Torn() =>
        new(607, 0, 0, 0, 26, false, 618, new FieldNavigationControlTransform(0), true,
            [FieldActivityModelReading.Unreadable(21), Hand(22, 170), Hand(23, 128)],
            new Dictionary<int, FieldActivityWaitState>(), _ => true, _ => true, 0);

    /// <summary>
    /// Entering the room: a single look at the hands proves where they are, not that they are
    /// at rest. Nothing is said until they have been watched for the settle time; then the time
    /// they rest on, or the movement if they change meanwhile.
    /// </summary>
    private static void AFirstLookClaimsNothingUntilTheHandsAreWatched()
    {
        foreach (var interval in HostIntervals)
        {
            var still = new NativeClock(2, 10);
            var spoken = Listen(still, interval, 2);
            Equal("Stopped, ten ten.", string.Join("|", spoken.Select(line => line.Text)), $"{interval * 1000}ms: one stop ({Show(spoken)})");
            Equal(true, spoken[0].Time >= 0.35 - 1e-9 && spoken[0].Time <= 0.35 + interval + 1e-9,
                $"{interval * 1000}ms: said as soon as the hands have been watched still ({Show(spoken)})");

            // Entering while a spin is under way: the long hand is on a numeral at every update,
            // and is never taken for stopped while it goes on changing.
            var (spin, _, _) = Spin(start: 0, spinUpdates: 60);
            var during = Listen(spin, interval, 1.8);
            Equal(false, during.Any(line => line.Text.StartsWith("Stopped", StringComparison.Ordinal)),
                $"{interval * 1000}ms: no stop while the spin runs ({Show(during)})");
            Equal(true, during.Count >= 1 && during[0].Text.StartsWith("Moving, ", StringComparison.Ordinal),
                $"{interval * 1000}ms: the movement is what is said ({Show(during)})");
        }
    }

    /// <summary>
    /// The readout reset in the middle of a spin - the host lost focus, or left and came back -
    /// then looks at a long hand that happens to sit on a numeral. That is not a stop.
    /// </summary>
    private static void AResetDuringASpinIsNotAStop()
    {
        var readout = new FieldActivityReadout();
        var none = new HashSet<int>();
        _ = readout.Observe(Clock(86, 170, 64, none), Epoch);
        _ = readout.Observe(Clock(76, 170, 64, none), Epoch.AddSeconds(0.1));
        readout.Reset();
        Equal(null, readout.Observe(Clock(64, 170, 64, none), Epoch.AddSeconds(0.2)).Speech,
            "an aligned first look after a reset claims neither stopped nor moving");
        var spoken = new List<string>();
        var bearing = 64;
        for (var update = 1; update <= 30; update++)
        {
            bearing = HourBearings[(Array.IndexOf(HourBearings, bearing) + 1) % 12];
            if (readout.Observe(Clock(bearing, 170, 64, none), Epoch.AddSeconds(0.2 + update / 30.0)).Speech is { } line)
            {
                spoken.Add(line);
            }
        }

        Equal(false, spoken.Any(line => line.StartsWith("Stopped", StringComparison.Ordinal)),
            $"the spin going on is never a stop ({string.Join(" | ", spoken)})");
        Equal(true, spoken.Count == 1 && spoken[0].StartsWith("Moving, ", StringComparison.Ordinal),
            $"it is movement, said once ({string.Join(" | ", spoken)})");
    }

    /// <summary>The same with a torn look instead of a reset: the next aligned look is not a stop.</summary>
    private static void ATornLookDuringASpinIsNotAStop()
    {
        foreach (var interval in HostIntervals)
        {
            var (spin, _, _) = Spin(start: 0, spinUpdates: 60);
            var readout = new FieldActivityReadout();
            var spoken = new List<Spoken>();
            for (var t = 0.0; t <= 1.8 + 1e-9; t += interval)
            {
                var update = (int)Math.Floor(t / NativeUpdate + 1e-9);
                var (l, s, open) = spin.At(update);
                // Half a second in, one look at the long hand tears.
                var observation = t is >= 0.5 and < 0.5 + 1e-3 + 0.1 ? Torn() : Clock(l, s, SecondHandAt(update), open);
                if (readout.Observe(observation, Epoch.AddSeconds(t)).Speech is { } speech)
                {
                    spoken.Add(new Spoken(t, speech));
                }
            }

            Equal(true, spoken.Any(line => line.Text == "Cannot read the clock hands."), $"{interval * 1000}ms: the torn look ({Show(spoken)})");
            Equal(false, spoken.Any(line => line.Text.StartsWith("Stopped", StringComparison.Ordinal)),
                $"{interval * 1000}ms: never a stop while the spin goes on ({Show(spoken)})");
            Equal(true, spoken.Last().Text.StartsWith("Moving, ", StringComparison.Ordinal),
                $"{interval * 1000}ms: after the torn look the spin is movement again ({Show(spoken)})");
        }
    }

    /// <summary>
    /// One MENU press, eased over 6 updates (e21 script 7): the eased turn barely moves at first
    /// and last, and the stop still waits for its very last change.
    /// </summary>
    private static void AnEasedTurnIsFollowedToItsEnd()
    {
        var bearings = Enumerable.Range(0, 7).Select(done => NativeBearing(HourBearings[2], HourBearings[1], false, done, 6, true)).ToArray();
        Equal("86,87,90,96,100,104,106", string.Join(",", bearings), "the engine's eased turn from two back to one");
        foreach (var interval in HostIntervals)
        {
            var clock = Tap(longHour: 2, shortHour: 10, count: 1, forward: false);
            var spoken = Listen(clock, interval, 3).Skip(1).ToList();
            Equal(true, spoken.Count == 2 && spoken[0].Text.StartsWith("Moving, ", StringComparison.Ordinal),
                $"{interval * 1000}ms: moving ({Show(spoken)})");
            Equal("Stopped, ten oh five.", spoken[1].Text, $"{interval * 1000}ms: then the time it came back to ({Show(spoken)})");
            Equal(true, spoken[1].Time >= LastChange(clock) + 0.35 - 1e-9,
                $"{interval * 1000}ms: only after the eased turn's last change ({Show(spoken)})");
        }
    }

    private static readonly string[] Numerals =
        ["twelve", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven"];

    private static string Hour(int hour) => Numerals[hour];

    private static string Minutes(int longHour) => longHour switch
    {
        0 => "o'clock",
        1 => "oh five",
        _ => new[] { "", "", "ten", "fifteen", "twenty", "twenty-five", "thirty", "thirty-five", "forty", "forty-five", "fifty", "fifty-five" }[longHour]
    };

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}

using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>What the field activity readout wants delivered this tick.</summary>
/// <param name="Speech">The line to speak, or null when nothing should be said now.</param>
/// <param name="PlayButtonReadyCue">
/// A native script has just stopped on a button. This goes to its own device so a held
/// button cannot cut it off, which is the whole reason it is a sound and not a word.
/// </param>
/// <param name="IsPending">
/// The field is stopped waiting for the player. While this is true the route guidance
/// has nothing useful to say and must not talk over the thing that does.
/// </param>
public readonly record struct FieldActivityCue(
    string? Speech,
    bool PlayButtonReadyCue,
    bool IsPending)
{
    public bool IsEmpty => Speech is null && !PlayButtonReadyCue;
}

/// <summary>
/// Everything the readout is allowed to look at.
/// </summary>
/// <param name="IsPlayerControlled">
/// The player has control. While the field is moving the party itself, naming a button
/// would be inviting a press into a scene that is not listening.
/// </param>
/// <param name="Waits">
/// Entity id to which of that entity's native waits its script is actually sitting in,
/// read per entity through the program counter - never the globally current VM entity,
/// and never "the script is running" on its own, because the scripts these waits live
/// in also contain dialogue and animation.
/// </param>
/// <param name="IsLineEnabled">
/// The native LINON state of a trigger, or null when it could not be read.
/// </param>
/// <param name="IsBoundaryEnabled">
/// The native IDLCK state of a walkmesh triangle, or null when it could not be read.
/// A locked triangle is a wall; the clock's bridges are unlocked one pair at a time.
/// </param>
/// <param name="PillarGate">
/// Bank[5][9], which every pillar's Go script tests before it will jump at all, or -1
/// when it could not be read.
/// </param>
public readonly record struct FieldActivityObservation(
    int FieldId,
    int PlayerX,
    int PlayerY,
    int PlayerZ,
    int PlayerTriangle,
    bool IsPlayerControlled,
    int GameMoment,
    FieldNavigationControlTransform Transform,
    bool IsTransformUsable,
    IReadOnlyList<FieldActivityModelReading> Models,
    IReadOnlyDictionary<int, FieldActivityWaitState> Waits,
    Func<int, bool>? IsLineEnabled,
    Func<int, bool>? IsBoundaryEnabled,
    int PillarGate)
{
    /// <summary>
    /// The native numeric window the cliff fields draw a body temperature in, when one
    /// is actually on screen.
    /// </summary>
    public FieldActivityNumericWindow NumericWindow { get; init; }

    /// <summary>
    /// Bank[5], the field's own temporary bytes, or null when they could not be read.
    /// The wind fields keep their strength, gust gap and safe flag here, and the cliff
    /// keeps the climbing bit that decides whether warming is possible at all.
    /// </summary>
    public Func<int, int>? ReadTemporaryByte { get; init; }
}

/// <summary>
/// Speaks what a sighted player can see during the native activities on the way from
/// the Temple of the Ancients to Icicle Inn, and only what they can see.
///
/// Three rules run through all of it.
///
/// <para><b>A running script is not a pending input.</b> Every one of these waits lives
/// inside a script that also plays animation and dialogue, so "is this entity running
/// script N" is worthless as evidence. What is read instead is the entity's own program
/// counter and the loop the field circles while it waits - 647's director cycles
/// 8-31-284 for its first Confirm and 56-281 for its second, 606's <c>last</c> cycles
/// 165-174 - and the script the counter points into is proved by the bytes of the wait
/// opcode itself before any of it is believed.</para>
///
/// <para><b>A failed read is not an empty room.</b> Every model read says whether it was
/// visible, hidden, or unreadable, and an unreadable one never becomes "the corridor is
/// clear" or "no diggers are out".</para>
///
/// <para><b>Nothing hidden is read.</b> The guard's next doorway in Bank[5][18], the
/// buried target in bonevil2's <c>luna</c>, and the hands' target hours in Bank[4][226]
/// and Bank[4][228] are all left alone. The hands are read from the models, which is
/// what is on screen while they turn.</para>
///
/// The activities and the installed evidence each is read from:
///
/// <list type="bullet">
/// <item><description>
/// <b>606 kuro_3, the rolling corridor.</b> iwa1, iwa2 and iwa3 - entities 26, 27 and
/// 28 - are the boulders. <c>last</c>, entity 16, writes 612 in its script 3 and waits
/// at byte 165 on <c>IFKEY 61440</c>, falling to 174 which jumps back to 165.
/// </description></item>
/// <item><description>
/// <b>607 kuro_4, the clock.</b> <c>long</c> 21, <c>short</c> 22 and <c>second</c> 23
/// are the hands; entity 21's script 10 gives the bearing of each hour - 128 for
/// twelve, 106, 86, 64, 42, 22, 0 for six, 234, 214, 192, 170, 150 - a full turn in 256
/// units. A hand between two of those is turning, and no bridge is claimed for it. When
/// it is on an hour, the bridge is claimed only if the native IDLCK state agrees:
/// entity 8's script 1 locks all twenty-four bridge triangles and each hour's own
/// script unlocks its pair, so an unlocked pair is a bridge that is actually there.
/// The short hand opens the pair for its own hour too (entity 22's script 6), so its
/// bridge is said the same way whenever it stands on a different hour. Where the party
/// stands - the middle, or which doorway's side - is said first, from the walkmesh's
/// own components, because it decides which bridges lead anywhere.
/// The controls are ordinary dialogue in native windows 3, 2 and 4 and are left to the
/// dialogue readout.
/// </description></item>
/// <item><description>
/// <b>610 kuro_7, the chase.</b> <c>keyman</c> 27 places itself in a doorway and turns
/// itself visible, then hides again; only where it visibly is gets said. jump1 and
/// jump2 - entities 16 and 17 - are Confirm crossings at two different heights, 370 and
/// about -10, so height is part of deciding which one the party is standing on.
/// </description></item>
/// <item><description>
/// <b>772 bonevil2, the excavation.</b> The dig is its own field, reached from
/// bonevil's foreman; the five placed diggers are entities 14 to 18, each placed by its
/// own script 5. Entity 6 is the buried target and is never observed. How many of the
/// five are placed is the phase, and it is read from the models rather than from any
/// counter.
/// </description></item>
/// <item><description>
/// <b>646 ancnt1, the pillars.</b> st1 to st5 - entities 12 to 16 - jump on a direction
/// press, and the pair is not the same at every pillar. All five first test Bank[5][9];
/// st1 takes Left to go on but only from 667, and Right to come back but only from 673,
/// so during the first approach it has no way back at all. st2, st3 and st4 take Up or
/// Left on and Down or Right back. st5 takes Left on and Right back.
/// </description></item>
/// <item><description>
/// <b>647 ancnt2, the altar scene.</b> <c>dir</c>, entity 0, stops on a fresh Confirm
/// at bytes 31, 56, 102, 144 and 151, each with its own loop back - 284 to 8, 281 to
/// 56, 279 to 67, 277 to 109 and 275 to 151 - and writes 671 after the first.
/// </description></item>
/// </list>
/// </summary>
public sealed class FieldActivityReadout
{
    private const int DirectionUnitsPerTurn = 256;
    private const int ClockNumerals = 12;

    /// <summary>
    /// How close to an hour's own bearing counts as standing on it. The hours are about
    /// twenty-one units apart, so this is a fifth of a step: near enough that the hand
    /// has arrived, far enough from the midpoint that a turning hand is never mistaken
    /// for a stopped one.
    /// </summary>
    private const int ClockAlignmentTolerance = 4;

    /// <summary>Bearings of the twelve hours, from entity 21's own script 10.</summary>
    private static readonly int[] ClockHourBearings =
        [128, 106, 86, 64, 42, 22, 0, 234, 214, 192, 170, 150];

    /// <summary>
    /// The pair of walkmesh triangles each hour's bridge is made of, from entity 8's
    /// scripts: hour 12 unlocks 93 and 2, hour 1 unlocks 78 and 12, and so on in the
    /// order entity 8's script 1 locks them.
    /// </summary>
    private static readonly int[][] ClockBridgeTriangles =
    [
        [93, 2], [78, 12], [79, 0], [89, 66], [72, 60], [73, 54],
        [85, 48], [74, 42], [75, 36], [81, 14], [76, 10], [77, 6]
    ];

    private static readonly string[] Numerals =
        ["twelve", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven"];

    /// <summary>
    /// The installed walkmesh's own parts of the clock, as the Story rows for doorway six use
    /// them (story-regions/TempleOfTheAncients.ps1). Each bridge is three triangles, and IDdr's
    /// pair locks its inner and outer ones. With all twenty-four shut the middle is one
    /// component of thirty triangles; with the twelve inner ends it is where a party stands
    /// between bridges. With only the inner ends shut each doorway's side is six triangles: its
    /// platform of four and the outer two of its bridge. Every way into the room lands on one
    /// of the sides. The other eighteen triangles are a separate part of the walkmesh that no
    /// way in reaches, and are left unnamed.
    /// </summary>
    private static readonly HashSet<int> ClockMiddleTriangles =
    [
        80, 82, 83, 84, 86, 87, 88, 90, 91, 92, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103,
        104, 105, 106, 107, 126, 127, 128, 129, 130, 131,
        93, 78, 79, 89, 72, 73, 85, 74, 75, 81, 76, 77
    ];

    private static readonly int[][] ClockDoorwaySides =
    [
        [2, 3, 16, 17, 18, 19], [4, 5, 8, 9, 12, 13], [0, 1, 68, 69, 70, 71], [62, 63, 64, 65, 66, 67],
        [56, 57, 58, 59, 60, 61], [50, 51, 52, 53, 54, 55], [44, 45, 46, 47, 48, 49], [38, 39, 40, 41, 42, 43],
        [32, 33, 34, 35, 36, 37], [14, 15, 28, 29, 30, 31], [10, 11, 24, 25, 26, 27], [6, 7, 20, 21, 22, 23]
    ];

    /// <summary>The hour of the doorway whose side a triangle is on, -1 for the middle, -2 for neither.</summary>
    internal static int ClockPlace(int triangle)
    {
        if (ClockMiddleTriangles.Contains(triangle))
        {
            return -1;
        }

        for (var hour = 0; hour < ClockDoorwaySides.Length; hour++)
        {
            if (Array.IndexOf(ClockDoorwaySides[hour], triangle) >= 0)
            {
                return hour;
            }
        }

        return -2;
    }

    /// <summary>The middle's and each doorway's triangles, for tests that check them against the walkmesh.</summary>
    internal static IReadOnlyCollection<int> ClockMiddle => ClockMiddleTriangles;

    internal static IReadOnlyList<int> ClockDoorwaySide(int hour) => ClockDoorwaySides[hour];

    /// <summary>Distances are said in hundreds; a boulder does not move in units.</summary>
    private const int DistanceBucket = 100;

    /// <summary>Far enough away that naming a boulder would be noise, not a warning.</summary>
    private const int HazardRange = 900;

    /// <summary>How far from a trigger line still counts as standing on it.</summary>
    private const int TriggerReach = 56;

    /// <summary>
    /// How far above or below a trigger line the party may be and still be on it. The
    /// chase's two crossings are 380 apart in height, so this only has to be smaller
    /// than that to keep them separate.
    /// </summary>
    private const int TriggerHeightBand = 90;

    /// <summary>
    /// The one cadence every non-pending activity line shares. It is deliberately a
    /// floor on speech rather than a filter on change: a room whose state changes
    /// several times a second must not produce several sentences a second, however
    /// genuinely different each of those states is.
    /// </summary>
    public static readonly TimeSpan NonPendingSpeechInterval = TimeSpan.FromSeconds(2.5);

    /// <summary>A wait the player has not answered is said again this often.</summary>
    public static readonly TimeSpan PendingRepeatInterval = TimeSpan.FromSeconds(8);

    private string? lastKey;
    private string? lastDetailKey;
    private DateTime lastSpokenAt = DateTime.MinValue;
    private int lastLongHandBearing = -1;
    private int lastChairAnimation = -1;

    public void Reset()
    {
        lastKey = null;
        lastDetailKey = null;
        lastSpokenAt = DateTime.MinValue;
        lastLongHandBearing = -1;
        lastChairAnimation = -1;
    }

    public FieldActivityCue Observe(FieldActivityObservation observation) =>
        Observe(observation, DateTime.UtcNow);

    /// <summary>
    /// What to deliver now. Speech is bounded rather than queued: a new situation is
    /// said once, an unanswered wait is said again on a slow beat, and a hazard that is
    /// merely moving is refreshed no faster than a listener can take it in. Nothing here
    /// enqueues a sentence per sample.
    /// </summary>
    public FieldActivityCue Observe(FieldActivityObservation observation, DateTime now)
    {
        var report = Compose(observation);
        if (report is null)
        {
            Reset();
            return default;
        }

        var value = report.Value;
        var isNewSituation = !string.Equals(value.Key, lastKey, StringComparison.Ordinal);

        // A button the field has just stopped on is the one thing that jumps the queue.
        // Nothing else on screen can tell a player the game is waiting for them, and the
        // cue that says so goes to its own device where a held button cannot cut it off.
        if (value.IsPending)
        {
            if (isNewSituation)
            {
                lastKey = value.Key;
                lastDetailKey = value.DetailKey;
                lastSpokenAt = now;
                return new FieldActivityCue(value.Text, true, true);
            }

            if (now - lastSpokenAt < PendingRepeatInterval)
            {
                return new FieldActivityCue(null, false, true);
            }

            lastDetailKey = value.DetailKey;
            lastSpokenAt = now;
            return new FieldActivityCue(value.Text, false, true);
        }

        // A short-lived situation is said the moment it becomes true. The wind is the
        // one that needs this: its openings are brief, and a player told about one after
        // the shared cadence has come round has been told about an opening that has
        // already closed. This is bounded by the situation itself rather than by time -
        // the wind has two states, so it can only speak as often as the field actually
        // changes it - and its repeats fall back to the ordinary cadence below.
        if (value.IsImmediate && isNewSituation)
        {
            lastKey = value.Key;
            lastDetailKey = value.DetailKey;
            lastSpokenAt = now;
            return new FieldActivityCue(value.Text, false, false);
        }

        // Everything else the room is doing shares one cadence, whether or not it has
        // become a different situation. A clock hand sweeping between hours and a
        // boulder crossing a distance boundary both change the situation several times a
        // second, and speaking each one would leave a queue of stale positions playing
        // over the room they are meant to describe. What is said when the cadence comes
        // round is composed from the state at that moment, so it is the newest one
        // rather than the oldest queued one - and the repeat action answers with the
        // current state at any time in between.
        if (now - lastSpokenAt < NonPendingSpeechInterval)
        {
            return new FieldActivityCue(null, false, false);
        }

        if (!isNewSituation &&
            string.Equals(value.DetailKey, lastDetailKey, StringComparison.Ordinal))
        {
            return new FieldActivityCue(null, false, false);
        }

        lastKey = value.Key;
        lastDetailKey = value.DetailKey;
        lastSpokenAt = now;
        return new FieldActivityCue(value.Text, false, false);
    }

    /// <summary>
    /// The current line for this field regardless of what was last said, for the repeat
    /// action. Null when this field has no activity or nothing about it can be read.
    /// </summary>
    public string? Describe(FieldActivityObservation observation) => Compose(observation)?.Text;

    /// <summary>Whether the field is stopped waiting for the player right now.</summary>
    public bool IsWaitingForInput(FieldActivityObservation observation) =>
        Compose(observation)?.IsPending == true;

    private Report? Compose(FieldActivityObservation observation) => observation.FieldId switch
    {
        CorelPursuitFieldId => ComposePursuit(observation),
        CorelBrakingFieldId => ComposeBraking(observation),
        CliffLowerFieldId or CliffMiddleFieldId or CliffUpperFieldId => ComposeCliff(observation),
        WindFirstFieldId or WindSecondFieldId or WindThirdFieldId => ComposeWind(observation),
        WhirlwindStruggleFieldId => ComposeStruggle(observation),
        JunonPressRoomFieldId => ComposeLockedDoor(observation),
        JunonGasChairFieldId => ComposeGasChair(observation),
        JunonCannonFieldId => ComposeCannonConfrontation(observation),
        RollingCorridorFieldId => ComposeRollingCorridor(observation),
        ClockRoomFieldId => ComposeClock(observation),
        ChaseChamberFieldId => ComposeChase(observation),
        ExcavationFieldId => ComposeExcavation(observation),
        PillarApproachFieldId => ComposePillars(observation),
        AltarSceneFieldId => ComposeAltarScene(observation),
        _ => null
    };

    // --- 728, the pursuit ---------------------------------------------------------------
    private Report? ComposePursuit(FieldActivityObservation observation)
    {
        var wait = ReadWait(observation, CorelPursuitCidEntityId);
        if (!wait.IsWaiting)
        {
            return null;
        }

        // The two levers are worked in turn, and which one the handcar will take next is
        // not hidden - it is the lever Cid is not holding. The field keeps that in
        // Bank[5][16], and it is the same thing the animation shows.
        var onLeftLever = observation.ReadTemporaryByte is { } readByte
            ? readByte(CorelPursuitLeverStateAddress) == 0
            : (bool?)null;

        var text = onLeftLever switch
        {
            true => "Working the handcar. Press Up for the left lever.",
            false => "Working the handcar. Press Menu for the right lever.",
            _ => "Working the handcar. Up and Menu work the two levers in turn."
        };

        // The ten-minute countdown is an ordinary native clock window and is already
        // spoken by the countdown readout; repeating it here would be two voices on one
        // timer.
        var key = $"728:{onLeftLever?.ToString() ?? "unknown"}";
        return new Report(key, key, text, IsPending: true);
    }

    // --- 730, the brakes ------------------------------------------------------------------
    private Report? ComposeBraking(FieldActivityObservation observation)
    {
        var wait = ReadWait(observation, CorelBrakingCidEntityId);
        return wait.IsWaiting
            ? new Report(
                $"730:wait:{wait.WaitIndex}",
                $"730:wait:{wait.WaitIndex}",
                "At the brakes. Press Up then Menu, or Down then Cancel.",
                IsPending: true)
            : null;
    }

    // --- 689, 692 and 694, the cliff ------------------------------------------------
    private Report? ComposeCliff(FieldActivityObservation observation)
    {
        var window = observation.NumericWindow;
        if (!window.IsUsable)
        {
            // No numeric window on screen is not a temperature of zero. The climb is
            // the only time the field draws one, and outside it there is nothing to say.
            return null;
        }

        var climbing = observation.ReadTemporaryByte is { } readByte
            ? (readByte(CliffClimbingStateAddress) & CliffClimbingBit) != 0
            : (bool?)null;

        var text = $"Body temperature {window.Value} degrees.";
        var key = $"cliff:{window.Value}:{climbing?.ToString() ?? "unknown"}";
        text += climbing switch
        {
            false => " Press Square to warm up.",
            true => string.Empty,
            _ => string.Empty
        };

        return new Report(key, key, text, IsPending: false);
    }

    // --- 709, 710 and 711, the wind ---------------------------------------------------
    private Report? ComposeWind(FieldActivityObservation observation)
    {
        if (observation.ReadTemporaryByte is not { } readByte)
        {
            return new Report("wind:unreadable", "wind:unreadable", "Cannot read the wind.", IsPending: false);
        }

        // Each of the three ledges keeps its own safe flag, and the field sets it from
        // its own conditions. What is reported is that flag as it stands, never a guess
        // at when it will next change - woa_3's phase is drawn from the field's own
        // random number and predicting it would be inventing information.
        var (safeAddress, safeWhenZero) = observation.FieldId switch
        {
            WindFirstFieldId => (WindFirstSafeAddress, false),
            WindSecondFieldId => (WindSecondSafeAddress, true),
            _ => (WindThirdSafeAddress, false)
        };

        var safeValue = readByte(safeAddress);
        var isOpen = safeWhenZero ? safeValue == 0 : safeValue != 0;

        // Nothing on screen shows the phase counter, the gust spacing or the strength
        // byte; what a player sees is the gust blowing and then dropping away. So the
        // report is the two states the ledge actually has, and never a number behind
        // them or a guess at when the next lull is coming - woa_3's phase comes out of
        // the field's own random number, and predicting it would be inventing something
        // the game has not decided.
        var text = isOpen
            ? "The wind has dropped. Cross now."
            : "The wind is blowing hard.";
        var key = $"wind:{observation.FieldId}:{isOpen}";
        return new Report(key, key, text, IsPending: false, IsImmediate: true);
    }

    // --- 706, holding on ----------------------------------------------------------------
    private Report? ComposeStruggle(FieldActivityObservation observation)
    {
        var wait = ReadWait(observation, WhirlwindStruggleEntityId);
        return wait.IsWaiting
            ? new Report(
                $"706:wait:{wait.WaitIndex}",
                $"706:wait:{wait.WaitIndex}",
                "Hold on. Press any button.",
                IsPending: true)
            : null;
    }

    // --- 401, forcing the door -----------------------------------------------------------
    private Report? ComposeLockedDoor(FieldActivityObservation observation)
    {
        var wait = ReadWait(observation, JunonPressRoomBarretEntityId);
        return wait.IsWaiting
            ? new Report(
                "401:wait",
                "401:wait",
                "Forcing the door. Press a direction or Confirm, again and again.",
                IsPending: true)
            : null;
    }

    // --- 402, the chair -------------------------------------------------------------------
    private Report? ComposeGasChair(FieldActivityObservation observation)
    {
        var wait = ReadWait(observation, JunonGasChairTifaEntityId);
        if (!wait.IsWaiting)
        {
            return null;
        }

        // The legend is the field's own window 3, and this is the same four pairings it
        // draws. Which of them to use, and in what order, is the player's to work out:
        // nothing here reads the progress state or names a next move.
        var text = "Tied to the chair. Menu moves your head, Switch your right arm, " +
            "OK your left arm, Cancel your legs.";

        // What a sighted player watches after each attempt is Tifa's pose and the key
        // above her. Both are read from the models: the pose is the animation the field
        // is actually playing, and the key is either still hanging there or it is not.
        // The key's own record existing is not the same as having it, so only whether it
        // is being drawn is used.
        var pose = Find(observation, JunonGasChairTifaEntityId);
        var key = $"402:chair:{pose.Status}:{pose.Model.AnimationId}";
        if (pose.Status == FieldActivityReadStatus.Visible &&
            lastChairAnimation >= 0 &&
            lastChairAnimation != pose.Model.AnimationId)
        {
            text += " " + DescribeChairMovement(pose.Model.AnimationId);
        }

        if (pose.Status == FieldActivityReadStatus.Visible)
        {
            lastChairAnimation = pose.Model.AnimationId;
        }

        // The key is on the floor, not above her: 402:8's placement puts it at
        // (31,-188) and 402:8's script 4 visibly drags it toward her at (-13,-120).
        // What a sighted player watches is that gap closing, so the gap is what is
        // said - from the two models' own positions, in steps a listener can follow,
        // with no phase byte and no count of how many attempts are left.
        var chairKey = Find(observation, JunonGasChairKeyEntityId);
        switch (chairKey.Status)
        {
            case FieldActivityReadStatus.Visible when pose.Status == FieldActivityReadStatus.Visible:
                var reach = DescribeChairKeyReach(pose.Model, chairKey.Model);
                text += " " + reach.Text;
                key += ":key:" + reach.Step;
                break;
            case FieldActivityReadStatus.Visible:
                text += " The key is on the floor.";
                key += ":key";
                break;
            case FieldActivityReadStatus.Hidden:
                text += " The key is off the floor now.";
                key += ":nokey";
                break;

            // Not the same thing as the key being gone, and saying so is the difference
            // between "you have it" and "I could not look".
            case FieldActivityReadStatus.Unreadable:
                text += " The key cannot be made out.";
                key += ":keyunreadable";
                break;
        }

        return new Report(key, key, text, IsPending: true);
    }

    /// <summary>
    /// What visibly moved, from the animation the field is playing on her model.
    ///
    /// <para>402:3's script 12 is the loop the chair runs: at each stage it tests the
    /// four buttons and plays a different animation for each, and only some of those
    /// animations do anything. The ones named here are the ones whose visible result has
    /// been read off the installed script - 12 and 16 are the legs, and 16 is the one
    /// that calls the key's own script 4 and drags it closer; 19 is the head; 20 moves
    /// head and legs together and takes the key through the key's script 6; 22 and 24
    /// are the head with the right and the left arm.</para>
    ///
    /// <para>Anything else is reported as movement without a name rather than guessed
    /// at, and none of it names a button: which one to try is the player's to work out,
    /// exactly as it is for a sighted player watching the same limbs.</para>
    /// </summary>
    private static string DescribeChairMovement(int animationId) =>
        animationId switch
        {
            12 or 16 => "Your legs moved.",
            19 => "Your head moved.",
            20 => "Your head and legs moved together.",
            22 => "Your head and right arm moved.",
            24 => "Your head and left arm moved.",
            _ => "That moved you."
        };

    /// <summary>
    /// How far the key still is from the character in the chair, in three steps. The
    /// distance itself is not spoken: a sighted player sees a gap, not a number.
    /// </summary>
    private static (int Step, string Text) DescribeChairKeyReach(
        FieldActivityModelState seated,
        FieldActivityModelState chairKey)
    {
        var dx = chairKey.X - seated.X;
        var dy = chairKey.Y - seated.Y;
        var distance = Math.Sqrt(dx * (double)dx + dy * (double)dy);
        return distance switch
        {
            <= 40 => (0, "The key is on the floor near your feet."),
            <= 100 => (1, "The key is on the floor a short way from you."),
            _ => (2, "The key is on the floor, well out of reach.")
        };
    }

    // --- 416, the confrontation --------------------------------------------------------------
    private Report? ComposeCannonConfrontation(FieldActivityObservation observation)
    {
        var wait = ReadWait(observation, JunonCannonTifaEntityId);
        return wait.IsWaiting
            ? new Report(
                "416:wait",
                "416:wait",
                "Hold Confirm.",
                IsPending: true)
            : null;
    }

    // --- 606, the rolling corridor -------------------------------------------------
    private Report? ComposeRollingCorridor(FieldActivityObservation observation)
    {
        var wait = ReadWait(observation, RollingCorridorLastLineEntityId);
        if (wait.IsWaiting)
        {
            return new Report(
                "606:wait",
                "606:wait",
                "The scene is waiting. Hold any direction to go on.",
                IsPending: true);
        }

        var hazards = new List<string>();
        var bearings = new List<string>();
        var unreadable = 0;
        foreach (var entityId in RollingCorridorBoulderEntityIds)
        {
            var reading = Find(observation, entityId);
            if (reading.Status == FieldActivityReadStatus.Unreadable)
            {
                unreadable++;
                continue;
            }

            if (reading.Status != FieldActivityReadStatus.Visible)
            {
                continue;
            }

            var distance = Distance(observation, reading.Model);
            if (distance > HazardRange)
            {
                continue;
            }

            var bearing = DescribeRelative(observation, reading.Model);
            bearings.Add(bearing);
            hazards.Add($"a boulder {bearing}, {Bucket(distance)} away");
        }

        if (hazards.Count > 0)
        {
            var text = "Rolling: " + string.Join("; ", hazards) + ".";
            return new Report(
                "606:rolling:" + string.Join("|", bearings),
                "606:rolling:" + text,
                text,
                IsPending: false);
        }

        if (unreadable > 0)
        {
            return new Report("606:unreadable", "606:unreadable", "Cannot read the corridor.", IsPending: false);
        }

        return new Report("606:clear", "606:clear", "The corridor is clear.", IsPending: false);
    }

    // --- 607, the clock --------------------------------------------------------------
    private Report? ComposeClock(FieldActivityObservation observation)
    {
        var longHand = Find(observation, ClockLongHandEntityId);
        if (longHand.Status == FieldActivityReadStatus.Unreadable)
        {
            return new Report("607:unreadable", "607:unreadable", "Cannot read the clock hands.", IsPending: false);
        }

        if (longHand.Status != FieldActivityReadStatus.Visible)
        {
            return null;
        }

        var bearing = longHand.Model.Direction;
        var wasMoving = lastLongHandBearing >= 0 && lastLongHandBearing != bearing;
        lastLongHandBearing = bearing;

        var parts = new List<string> { "Long hand " + DescribeBearing(bearing, wasMoving) };
        AppendHand(observation, ClockShortHandEntityId, "short hand", parts);
        AppendHand(observation, ClockSecondHandEntityId, "second hand", parts);

        // Which bridges are any use depends on where the party stands, and a sighted player
        // sees that as plainly as the hands: say it first. A triangle that is neither the
        // middle nor a doorway's side is not given a place.
        var place = ClockPlace(observation.PlayerTriangle);
        var where = place switch
        {
            -1 => "You are in the middle of the clock. ",
            >= 0 => $"You are by doorway {Numerals[place]}. ",
            _ => string.Empty
        };
        var text = where + string.Join(", ", parts) + ".";
        var hour = AlignedHour(bearing);
        // The second hand never stops, so it is carried in the words but kept out of
        // the key: a clock that spoke every time its second hand moved would be a clock
        // nothing else could be heard over. Where the party stands is part of the key, so
        // walking from a doorway onto the middle is said, at the same cadence as the hands.
        var key = $"607:{place}:{DescribeBearing(bearing, moving: false)}";
        if (hour >= 0)
        {
            // Only the field's own lock state may say a bridge is there. The hand
            // arriving and the bridge opening are two different things.
            var open = IsBridgeOpen(observation, hour);
            key += $":{open?.ToString() ?? "unknown"}";
            text += DescribeBridge(hour, open);
        }

        // The short hand is a bridge as well. Its own script 6 unlocks the pair for the
        // hour Bank[4][228] names, after entity 8 has locked all twenty-four, in the same
        // way the long hand's scripts unlock the pair for its hour - so on the first
        // visit the short hand at ten is the way in from the corridor, and after the
        // mural it is the short hand at six that joins doorway six to the middle.
        var shortHand = Find(observation, ClockShortHandEntityId);
        if (shortHand.Status == FieldActivityReadStatus.Visible)
        {
            var shortHour = AlignedHour(shortHand.Model.Direction);
            if (shortHour >= 0 && shortHour != hour)
            {
                var shortOpen = IsBridgeOpen(observation, shortHour);
                key += $":short{shortHour}:{shortOpen?.ToString() ?? "unknown"}";
                text += DescribeBridge(shortHour, shortOpen);
            }
        }

        return new Report(key, key, text, IsPending: false);
    }

    private static string DescribeBridge(int hour, bool? open) => open switch
    {
        true => $" The bridge to doorway {Numerals[hour]} is open.",
        false => $" The bridge to doorway {Numerals[hour]} is not open.",
        _ => string.Empty
    };

    private void AppendHand(
        FieldActivityObservation observation,
        int entityId,
        string name,
        ICollection<string> parts)
    {
        var reading = Find(observation, entityId);
        if (reading.Status == FieldActivityReadStatus.Visible)
        {
            parts.Add($"{name} {DescribeBearing(reading.Model.Direction, moving: false)}");
        }
    }

    private static string DescribeBearing(int bearing, bool moving)
    {
        var hour = AlignedHour(bearing);
        if (hour >= 0)
        {
            return $"at {Numerals[hour]}";
        }

        var (before, after) = SurroundingHours(bearing);
        var between = $"between {Numerals[before]} and {Numerals[after]}";
        return moving ? between + ", turning" : between;
    }

    /// <summary>
    /// The hour a hand is standing on, or -1 when it is between two of them. The hours
    /// come from the field's own turn table rather than from dividing the circle, so a
    /// hand is only "at" an hour when it is where the field actually puts it.
    /// </summary>
    internal static int AlignedHour(int bearing)
    {
        for (var hour = 0; hour < ClockHourBearings.Length; hour++)
        {
            if (AngleDistance(bearing, ClockHourBearings[hour]) <= ClockAlignmentTolerance)
            {
                return hour;
            }
        }

        return -1;
    }

    /// <summary>The two hours a turning hand currently lies between, in clock order.</summary>
    internal static (int Before, int After) SurroundingHours(int bearing)
    {
        // The hours run backwards around the byte circle - twelve at 128, one at 106 -
        // so the hour before is the one at the larger bearing.
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var hour = 0; hour < ClockHourBearings.Length; hour++)
        {
            var distance = AngleDistance(bearing, ClockHourBearings[hour]);
            if (distance < bestDistance)
            {
                best = hour;
                bestDistance = distance;
            }
        }

        var next = (best + 1) % ClockNumerals;
        var previous = (best + ClockNumerals - 1) % ClockNumerals;
        return AngleDistance(bearing, ClockHourBearings[next]) <=
               AngleDistance(bearing, ClockHourBearings[previous])
            ? (best, next)
            : (previous, best);
    }

    private static int AngleDistance(int left, int right)
    {
        var difference = Math.Abs(((left - right) % DirectionUnitsPerTurn + DirectionUnitsPerTurn) % DirectionUnitsPerTurn);
        return Math.Min(difference, DirectionUnitsPerTurn - difference);
    }

    private static bool? IsBridgeOpen(FieldActivityObservation observation, int hour)
    {
        if (observation.IsBoundaryEnabled is null || hour < 0 || hour >= ClockBridgeTriangles.Length)
        {
            return null;
        }

        foreach (var triangle in ClockBridgeTriangles[hour])
        {
            // A locked triangle is a wall. The bridge is there when its own pair is not
            // locked, which is what each hour's script does when the hand reaches it.
            if (observation.IsBoundaryEnabled(triangle))
            {
                return false;
            }
        }

        return true;
    }

    // --- 610, the chase ---------------------------------------------------------------
    private Report? ComposeChase(FieldActivityObservation observation)
    {
        foreach (var crossing in ChaseCrossings)
        {
            if (IsStandingOn(observation, crossing))
            {
                return new Report(
                    $"610:crossing:{crossing.EntityId}",
                    $"610:crossing:{crossing.EntityId}",
                    "At a crossing. Press Confirm to jump across.",
                    IsPending: true);
            }
        }

        var guard = Find(observation, ChaseGuardEntityId);
        return guard.Status switch
        {
            FieldActivityReadStatus.Visible => Guard(guard),
            FieldActivityReadStatus.Hidden =>
                new Report("610:hidden", "610:hidden", "No doorway has the guard in it.", IsPending: false),
            _ => new Report("610:unreadable", "610:unreadable", "Cannot see where the guard is.", IsPending: false)
        };

        Report Guard(FieldActivityModelReading reading)
        {
            var bearing = DescribeRelative(observation, reading.Model);
            var text = $"The guard is in the doorway {bearing}, " +
                $"{Bucket(Distance(observation, reading.Model))} away.";
            return new Report($"610:guard:{bearing}", $"610:guard:{text}", text, IsPending: false);
        }
    }

    // --- 772, the excavation ------------------------------------------------------------
    private Report? ComposeExcavation(FieldActivityObservation observation)
    {
        var placed = new List<string>();
        var unreadable = 0;
        foreach (var entityId in ExcavationWorkerEntityIds)
        {
            var reading = Find(observation, entityId);
            switch (reading.Status)
            {
                case FieldActivityReadStatus.Unreadable:
                    unreadable++;
                    break;
                case FieldActivityReadStatus.Visible:
                    placed.Add(
                        $"one {DescribeRelative(observation, reading.Model)} at " +
                        $"{Bucket(Distance(observation, reading.Model))}, " +
                        $"facing {DescribeFacing(observation, reading.Model.Direction)}");
                    break;
            }
        }

        if (unreadable > 0 && placed.Count == 0)
        {
            return new Report("772:unreadable", "772:unreadable", "Cannot read the diggers.", IsPending: false);
        }

        var total = ExcavationWorkerEntityIds.Length;
        var text = placed.Count == 0
            ? $"No diggers placed yet, {total} to place."
            : $"{placed.Count} of {total} diggers placed: " + string.Join("; ", placed) + ".";
        if (unreadable > 0)
        {
            text += $" {unreadable} could not be read.";
        }

        return new Report($"772:{placed.Count}:{unreadable}", $"772:{text}", text, IsPending: false);
    }

    // --- 646, the pillars ----------------------------------------------------------------
    private Report? ComposePillars(FieldActivityObservation observation)
    {
        // Every pillar's Go script gives up before it looks at any key unless this is
        // clear, so nothing is offered while it is not.
        if (observation.PillarGate != 0)
        {
            return null;
        }

        foreach (var pillar in Pillars)
        {
            if (!IsStandingOn(observation, pillar))
            {
                continue;
            }

            var legs = new List<string>();
            if (observation.GameMoment >= pillar.ForwardFromMoment)
            {
                legs.Add($"{pillar.Forward} to jump on");
            }

            if (observation.GameMoment >= pillar.ReverseFromMoment)
            {
                legs.Add($"{pillar.Reverse} to jump back");
            }

            if (legs.Count == 0)
            {
                continue;
            }

            var text = "At a pillar. Press " + string.Join(", ", legs) + ".";
            var key = $"646:{pillar.EntityId}:{legs.Count}";
            return new Report(key, key, text, IsPending: true);
        }

        return null;
    }

    // --- 647, the altar scene ---------------------------------------------------------------
    private Report? ComposeAltarScene(FieldActivityObservation observation)
    {
        var wait = ReadWait(observation, AltarSceneDirectorEntityId);
        if (!wait.IsWaiting)
        {
            return null;
        }

        // The wait index is part of the key even though every one of them says the same
        // words, so moving from one to the next is a new thing to answer rather than a
        // repeat of the last.
        return new Report(
            $"647:wait:{wait.WaitIndex}",
            $"647:wait:{wait.WaitIndex}",
            "The scene is waiting. Press Confirm to go on.",
            IsPending: true);
    }

    // --- shared -------------------------------------------------------------------------------
    private static FieldActivityWaitState ReadWait(FieldActivityObservation observation, int entityId) =>
        observation.Waits is not null && observation.Waits.TryGetValue(entityId, out var state)
            ? state
            : FieldActivityWaitState.Unreadable;

    private static FieldActivityModelReading Find(FieldActivityObservation observation, int entityId)
    {
        if (observation.Models is null)
        {
            return FieldActivityModelReading.Unreadable(entityId);
        }

        foreach (var reading in observation.Models)
        {
            if (reading.EntityId == entityId)
            {
                return reading;
            }
        }

        return FieldActivityModelReading.Unreadable(entityId);
    }

    /// <summary>
    /// Whether the party is actually on a trigger: near its line rather than near a
    /// point, at its height rather than merely above or below it, with the line switched
    /// on and the player in control of where they are standing.
    /// </summary>
    private static bool IsStandingOn(FieldActivityObservation observation, TriggerLeg leg)
    {
        if (!observation.IsPlayerControlled)
        {
            return false;
        }

        if (observation.IsLineEnabled is null || !observation.IsLineEnabled(leg.EntityId))
        {
            return false;
        }

        if (Math.Abs(observation.PlayerZ - leg.Z) > TriggerHeightBand)
        {
            return false;
        }

        return DistanceToSegment(
            observation.PlayerX, observation.PlayerY,
            leg.StartX, leg.StartY, leg.EndX, leg.EndY) <= TriggerReach;
    }

    private static double DistanceToSegment(
        int x, int y, int startX, int startY, int endX, int endY)
    {
        double dx = endX - startX;
        double dy = endY - startY;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= 0d)
        {
            return Math.Sqrt(Math.Pow(x - startX, 2) + Math.Pow(y - startY, 2));
        }

        var t = Math.Clamp((((x - startX) * dx) + ((y - startY) * dy)) / lengthSquared, 0d, 1d);
        var nearestX = startX + (t * dx);
        var nearestY = startY + (t * dy);
        return Math.Sqrt(Math.Pow(x - nearestX, 2) + Math.Pow(y - nearestY, 2));
    }

    private static int Distance(FieldActivityObservation observation, FieldActivityModelState model)
    {
        var dx = (double)(model.X - observation.PlayerX);
        var dy = (double)(model.Y - observation.PlayerY);
        return (int)Math.Round(Math.Sqrt((dx * dx) + (dy * dy)));
    }

    /// <summary>
    /// Distances in hundreds. Saying "two hundred" of something that is rolling is both
    /// what a sighted player perceives and the reason a moving hazard does not produce a
    /// fresh sentence every sample.
    /// </summary>
    private static int Bucket(int distance) =>
        (int)Math.Round(distance / (double)DistanceBucket, MidpointRounding.AwayFromZero) * DistanceBucket;

    private static string DescribeRelative(
        FieldActivityObservation observation,
        FieldActivityModelState model)
    {
        if (!observation.IsTransformUsable)
        {
            return "somewhere in the room";
        }

        var stick = observation.Transform.TransformWorldVector(
            model.X - observation.PlayerX,
            model.Y - observation.PlayerY);
        return DescribeStick(stick.X, stick.Y);
    }

    /// <summary>
    /// Which way a model is turned, in the same terms as where it is. The facing byte is
    /// an angle in the frame the control transform already works in - the transform
    /// subtracts the field's own control byte from <c>atan2(dx, -dy)</c> - so the facing
    /// is turned into a heading vector in that frame and put through the same transform.
    /// Without a usable transform there is no orientation to give and none is guessed.
    /// </summary>
    private static string DescribeFacing(FieldActivityObservation observation, int direction)
    {
        if (!observation.IsTransformUsable)
        {
            return "a direction that cannot be read";
        }

        var angle = direction * 2d * Math.PI / DirectionUnitsPerTurn;
        var stick = observation.Transform.TransformWorldVector(
            (int)Math.Round(Math.Sin(angle) * 1000d),
            (int)Math.Round(-Math.Cos(angle) * 1000d));
        var horizontal = Math.Abs(stick.X);
        var vertical = Math.Abs(stick.Y);
        var dominant = Math.Max(horizontal, vertical);
        if (dominant <= 0.001d)
        {
            return "nowhere in particular";
        }

        var sideways = stick.X < 0 ? "left" : "right";
        var away = stick.Y < 0 ? "away" : "towards you";
        return (horizontal >= dominant * 0.5d, vertical >= dominant * 0.5d) switch
        {
            (true, true) => $"{away} and {sideways}",
            (true, false) => sideways,
            _ => away
        };
    }

    private static string DescribeStick(double x, double y)
    {
        var horizontal = Math.Abs(x);
        var vertical = Math.Abs(y);
        var dominant = Math.Max(horizontal, vertical);
        if (dominant <= 0.001d)
        {
            return "here";
        }

        var useHorizontal = horizontal >= dominant * 0.5d;
        var useVertical = vertical >= dominant * 0.5d;
        var sideways = x < 0 ? "left" : "right";
        var away = y < 0 ? "ahead" : "behind";
        return (useHorizontal, useVertical) switch
        {
            (true, true) => $"{away} and to the {sideways}",
            (true, false) => $"to the {sideways}",
            _ => away
        };
    }

    /// <summary>
    /// One situation, in three parts. <paramref name="Key"/> is what makes it a
    /// different situation at all - the hour a hand is on, which boulders are in the
    /// way, how many diggers are placed - and a change in it is news the moment it
    /// happens. <paramref name="DetailKey"/> is the coarse part of the wording that may
    /// go stale, such as a distance in hundreds, and a change in that is worth saying
    /// again only after the listener has had time. Anything in the text but in neither
    /// key - a second hand, which never stops - is carried when the line is spoken but
    /// never causes it to be spoken.
    /// </summary>
    /// <param name="IsImmediate">
    /// This situation can be over before the shared cadence comes round. A wind that
    /// drops for a moment is the case it exists for: throttling it would mean the only
    /// time the player is told the crossing is open is after it has shut again. Only a
    /// genuine change of situation jumps the queue; repeats still wait their turn.
    /// </param>
    private readonly record struct Report(
        string Key,
        string DetailKey,
        string Text,
        bool IsPending,
        bool IsImmediate = false);

    /// <summary>
    /// A native trigger line and what pressing which way at it does, with the story
    /// counter each leg first becomes possible at.
    /// </summary>
    private readonly record struct TriggerLeg(
        int EntityId,
        int StartX,
        int StartY,
        int EndX,
        int EndY,
        int Z,
        string Forward,
        string Reverse,
        int ForwardFromMoment,
        int ReverseFromMoment);

    /// <summary>
    /// ancnt1's five pillars, from their own <c>LINE</c> opcodes and Go scripts. st1 is
    /// deliberately not symmetrical: its Right is gated on 673 and its Left on 667, so
    /// on the way out there is no way back across it.
    /// </summary>
    private static readonly TriggerLeg[] Pillars =
    [
        new(12, 789, -661, 829, -730, -141, "left", "right", 667, 673),
        new(13, 720, -751, 761, -820, -106, "up or left", "down or right", 0, 0),
        new(14, 651, -760, 611, -835, -61, "up or left", "down or right", 0, 0),
        new(15, 593, -749, 552, -679, -13, "up or left", "down or right", 0, 0),
        new(16, 486, -795, 446, -726, 31, "left", "right", 0, 0)
    ];

    /// <summary>
    /// kuro_7's two Confirm crossings, from their own <c>LINE</c> opcodes. They are at
    /// different heights, which is why height is checked at all.
    /// </summary>
    private static readonly TriggerLeg[] ChaseCrossings =
    [
        new(16, -675, 223, -487, 177, 370, "Confirm", "Confirm", 0, 0),
        new(17, -665, 79, -562, 45, -10, "Confirm", "Confirm", 0, 0)
    ];

    /// <summary>
    /// The three cliff fields that draw a body temperature. gaia_1's ad script 3 makes
    /// numeric window 1 with display type 2 and a two-digit limit; gaia_2 and gaia_31 do
    /// the same. Warming is <c>atatame</c> entity 4 script 3's fresh Square, which only
    /// runs while the climbing bit in Bank[5][13] is clear.
    /// </summary>
    public const int CliffLowerFieldId = 689;
    public const int CliffMiddleFieldId = 692;
    public const int CliffUpperFieldId = 694;
    public const int CliffNumericWindowId = 1;
    public const int CliffClimbingStateAddress = 13;
    public const int CliffClimbingBit = 0x01;

    /// <summary>
    /// The three wind ledges. woa_1's <c>try</c> sets Bank[5][4] from its own phase in
    /// Bank[5][0]; woa_2's crossing runs while Bank[5][18] is zero; woa_3's <c>try</c>
    /// sets Bank[5][7] from wind strength Bank[5][1] and gust gap Bank[5][6].
    /// </summary>
    public const int WindFirstFieldId = 709;
    public const int WindSecondFieldId = 710;
    public const int WindThirdFieldId = 711;
    public const int WindFirstSafeAddress = 4;
    public const int WindSecondSafeAddress = 18;
    public const int WindThirdSafeAddress = 7;
    public const int WindPhaseAddress = 0;
    public const int WindStrengthAddress = 1;
    public const int WindGustGapAddress = 6;

    /// <summary>trnad_51's produce, entity 0, and its three any-button holds.</summary>
    public const int WhirlwindStruggleFieldId = 706;
    public const int WhirlwindStruggleEntityId = 0;

    /// <summary>junbin4's Barret, entity 4, forcing the gas-room door.</summary>
    public const int JunonPressRoomFieldId = 401;
    public const int JunonPressRoomBarretEntityId = 4;

    /// <summary>
    /// junbin5's Tifa, entity 3, tied to the chair, and the key above her, entity 8.
    /// </summary>
    public const int JunonGasChairFieldId = 402;
    public const int JunonGasChairTifaEntityId = 3;
    public const int JunonGasChairKeyEntityId = 8;

    /// <summary>
    /// zcoal_1's Cid, entity 13, working the handcar levers. His Main alternates between
    /// a fresh Up at 418 while Bank[5][16] is zero and a fresh Menu at 461 otherwise,
    /// looping back from 502, so the lever the player is being asked for is the one that
    /// byte is not on. The ten-minute clock beside it is an ordinary native countdown
    /// window and belongs to the countdown readout.
    /// </summary>
    public const int CorelPursuitFieldId = 728;
    public const int CorelPursuitCidEntityId = 13;
    public const int CorelPursuitLeverStateAddress = 16;

    /// <summary>
    /// zcoal_3's Cid, entity 11, at the brakes. His Main stops twice - looping 484 to 542
    /// and again 806 to 868 - and each stop takes either a fresh Up then Menu or a fresh
    /// Down then Cancel. Both pairs are real inputs at both stops.
    /// </summary>
    public const int CorelBrakingFieldId = 730;
    public const int CorelBrakingCidEntityId = 11;

    /// <summary>junone7's Tifa, entity 4, during Scarlet's attack.</summary>
    public const int JunonCannonFieldId = 416;
    public const int JunonCannonTifaEntityId = 4;

    public const int RollingCorridorFieldId = 606;
    public const int ClockRoomFieldId = 607;
    public const int ChaseChamberFieldId = 610;
    public const int ExcavationFieldId = 772;
    public const int PillarApproachFieldId = 646;
    public const int AltarSceneFieldId = 647;

    public const int RollingCorridorLastLineEntityId = 16;
    public const int ClockLongHandEntityId = 21;
    public const int ClockShortHandEntityId = 22;
    public const int ClockSecondHandEntityId = 23;
    public const int ChaseGuardEntityId = 27;
    public const int AltarSceneDirectorEntityId = 0;

    public static readonly int[] RollingCorridorBoulderEntityIds = [26, 27, 28];

    /// <summary>
    /// bonevil2's five placed diggers. Entity 6 is the buried target and is absent on
    /// purpose: it is not something a sighted player can see.
    /// </summary>
    public static readonly int[] ExcavationWorkerEntityIds = [14, 15, 16, 17, 18];

    private const byte HeldKeyOpcode = 0x30;
    private const byte FreshKeyOpcode = 0x31;
    private const int AnyDirectionMask = 61440;
    private const int ConfirmMask = 32;
    private const int AnyButtonMask = 65535;
    private const int UpMask = 4096;
    private const int SwitchMask = 128;

    /// <summary>
    /// The waits each field's readout has to be able to recognise, with the loop the
    /// field circles while it sits in each one.
    /// </summary>
    public static IReadOnlyList<FieldActivityWaitAnchor> WaitAnchors(int fieldId, int entityId) =>
        (fieldId, entityId) switch
        {
            (RollingCorridorFieldId, RollingCorridorLastLineEntityId) =>
                [new FieldActivityWaitAnchor(165, HeldKeyOpcode, AnyDirectionMask, 165, 175)],
            (AltarSceneFieldId, AltarSceneDirectorEntityId) =>
            [
                new FieldActivityWaitAnchor(31, FreshKeyOpcode, ConfirmMask, 8, 34),
                new FieldActivityWaitAnchor(56, FreshKeyOpcode, ConfirmMask, 56, 59),
                new FieldActivityWaitAnchor(102, FreshKeyOpcode, ConfirmMask, 67, 105),
                new FieldActivityWaitAnchor(144, FreshKeyOpcode, ConfirmMask, 109, 147),
                new FieldActivityWaitAnchor(151, FreshKeyOpcode, ConfirmMask, 151, 154)
            ],

            // Three separate holds, each its own loop back onto itself: 8 falls to 29
            // and jumps back to 8, 35 to 56 and back, 62 to 83 and back. The field is
            // frozen and every gateway is disabled throughout, which is exactly why a
            // frozen screen cannot be taken for a scene that needs nothing.
            (WhirlwindStruggleFieldId, WhirlwindStruggleEntityId) =>
            [
                new FieldActivityWaitAnchor(8, FreshKeyOpcode, AnyButtonMask, 8, 31),
                new FieldActivityWaitAnchor(35, FreshKeyOpcode, AnyButtonMask, 35, 58),
                new FieldActivityWaitAnchor(62, FreshKeyOpcode, AnyButtonMask, 62, 85)
            ],

            // One loop, five ways to feed it: Up at 18, Down at 28, Right at 38, Left at
            // 48 and Confirm at 58, each adding to the same count, with 76 jumping back
            // to 18 until it is enough.
            (JunonPressRoomFieldId, JunonPressRoomBarretEntityId) =>
            [
                new FieldActivityWaitAnchor(18, FreshKeyOpcode, UpMask, 18, 78)
            ],

            // The chair polls all four limb buttons in one body and jumps back to the
            // top of it, so anywhere in there is the field waiting on the player. The
            // anchor is the first of those tests, Switch for the right arm, at 48 -
            // the installed bytes, which are three further on than the decompiler
            // prints for this script.
            (JunonGasChairFieldId, JunonGasChairTifaEntityId) =>
            [
                new FieldActivityWaitAnchor(48, HeldKeyOpcode, SwitchMask, 42, 400)
            ],

            // Held Confirm at 146, falling to 197 and jumping back from 200.
            (JunonCannonFieldId, JunonCannonTifaEntityId) =>
            [
                new FieldActivityWaitAnchor(146, HeldKeyOpcode, ConfirmMask, 146, 202)
            ],

            // One alternating loop, 412 to 504: a fresh Up at 418 or a fresh Menu at
            // 461, whichever lever the handcar is waiting for.
            (CorelPursuitFieldId, CorelPursuitCidEntityId) =>
            [
                new FieldActivityWaitAnchor(418, FreshKeyOpcode, UpMask, 412, 504)
            ],

            // Two stops, each its own loop and each taking either pair.
            (CorelBrakingFieldId, CorelBrakingCidEntityId) =>
            [
                new FieldActivityWaitAnchor(484, FreshKeyOpcode, UpMask, 484, 544),
                new FieldActivityWaitAnchor(806, FreshKeyOpcode, UpMask, 806, 870)
            ],
            _ => Array.Empty<FieldActivityWaitAnchor>()
        };

    /// <summary>The entities each field's readout needs a pose for, and nothing else.</summary>
    public static IReadOnlyList<int> ObservedEntities(int fieldId) => fieldId switch
    {
        RollingCorridorFieldId => RollingCorridorBoulderEntityIds,
        ClockRoomFieldId => [ClockLongHandEntityId, ClockShortHandEntityId, ClockSecondHandEntityId],
        ChaseChamberFieldId => [ChaseGuardEntityId],
        ExcavationFieldId => ExcavationWorkerEntityIds,
        JunonGasChairFieldId => [JunonGasChairTifaEntityId, JunonGasChairKeyEntityId],
        _ => Array.Empty<int>()
    };

    /// <summary>The entities whose script position this field's readout depends on.</summary>
    public static IReadOnlyList<int> ObservedWaitEntities(int fieldId) => fieldId switch
    {
        RollingCorridorFieldId => [RollingCorridorLastLineEntityId],
        AltarSceneFieldId => [AltarSceneDirectorEntityId],
        WhirlwindStruggleFieldId => [WhirlwindStruggleEntityId],
        JunonPressRoomFieldId => [JunonPressRoomBarretEntityId],
        JunonGasChairFieldId => [JunonGasChairTifaEntityId],
        JunonCannonFieldId => [JunonCannonTifaEntityId],
        CorelPursuitFieldId => [CorelPursuitCidEntityId],
        CorelBrakingFieldId => [CorelBrakingCidEntityId],
        _ => Array.Empty<int>()
    };

    /// <summary>The numeric window this field's readout needs, or -1 for none.</summary>
    public static int ObservedNumericWindow(int fieldId) =>
        fieldId is CliffLowerFieldId or CliffMiddleFieldId or CliffUpperFieldId
            ? CliffNumericWindowId
            : -1;

    /// <summary>Whether this field's readout needs the field's own temporary bytes.</summary>
    public static bool NeedsTemporaryBank(int fieldId) =>
        fieldId is CliffLowerFieldId or CliffMiddleFieldId or CliffUpperFieldId
            or WindFirstFieldId or WindSecondFieldId or WindThirdFieldId
            or CorelPursuitFieldId;

    /// <summary>The trigger entities whose LINON state this field's readout depends on.</summary>
    public static IReadOnlyList<int> ObservedLineEntities(int fieldId) => fieldId switch
    {
        ChaseChamberFieldId => [ChaseCrossings[0].EntityId, ChaseCrossings[1].EntityId],
        PillarApproachFieldId => [12, 13, 14, 15, 16],
        _ => Array.Empty<int>()
    };

    /// <summary>The walkmesh triangles whose lock state this field's readout depends on.</summary>
    public static IReadOnlyList<int> ObservedBoundaryTriangles(int fieldId) =>
        fieldId == ClockRoomFieldId
            ? ClockBridgeTriangles.SelectMany(pair => pair).ToArray()
            : Array.Empty<int>();

    /// <summary>True when this field has an activity worth reading out at all.</summary>
    public static bool HasActivity(int fieldId) =>
        fieldId is RollingCorridorFieldId or ClockRoomFieldId or ChaseChamberFieldId
            or ExcavationFieldId or PillarApproachFieldId or AltarSceneFieldId
            or CliffLowerFieldId or CliffMiddleFieldId or CliffUpperFieldId
            or WindFirstFieldId or WindSecondFieldId or WindThirdFieldId
            or WhirlwindStruggleFieldId or JunonPressRoomFieldId
            or JunonGasChairFieldId or JunonCannonFieldId
            or CorelPursuitFieldId or CorelBrakingFieldId;
}

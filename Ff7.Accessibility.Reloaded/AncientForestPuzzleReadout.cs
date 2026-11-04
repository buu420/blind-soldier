namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// What a sighted player sees of the Ancient Forest puzzle, said as it changes: what Cloud is
/// carrying, each pitcher plant open, closed or visibly shaking, each Mutant Flytrap open or
/// closed, and the one-off things that happen in plain view - a frog coming out, a released
/// creature landing, the flytrap biting the party.
///
/// <para>Only visible facts are read. A plant's flag is the closed shape its director draws; the
/// shake is the plant's own entity running its shake script
/// (<see cref="AncientForestPuzzleStateReader"/>); creatures are their models. Nothing hidden is
/// said: no countdown, no solution, nothing about where a creature thrown out of sight went. A
/// creature that vanishes is never said to have been eaten; the plant closing says what a sighted
/// player sees.</para>
///
/// <para>A line names what changed since the last one. The whole state is said on the first
/// coherent look, and again after a look that could not be read. Lines replace each other, so
/// nothing queues. Each line interrupts the one before it, so whatever the last line said that has
/// not had time to be heard - it is still being said, or it was held back unsaid - is said again in
/// the next, after what is new. Each subject (what Cloud carries, a plant, a flytrap, a creature,
/// the bite) is said once, at its newest. A state is kept until it has been heard; a one-off event -
/// the bite, a creature landing, set down or appearing - is news only for a short while. What no
/// longer holds is dropped: a plant that has opened is not still closed, the warning about the plant
/// underfoot ends when the leader leaves it, and a creature picked up, out of sight or left behind is
/// no longer where it was seen. While the field moves the party - a jump, a bite, a launch - nothing
/// is said and nothing is forgotten, and a line it kept from being said is offered again when control
/// returns. Afterwards only what really changed is said, together with a bite seen meanwhile. Local
/// numbers match the Objects targets.</para>
/// </summary>
internal sealed class AncientForestPuzzleReadout
{
    /// <summary>A released creature this close to the leader was set down where he stood.</summary>
    private const int FeetRadius = 64;

    /// <summary>How close the leader must be to a jaw's native placement point to be in its mouth.</summary>
    private const int JawRadius = 24;

    /// <summary>
    /// How long a one-off event - the bite, a creature landing, set down or appearing - stays news
    /// while it waits to be said: the same ten seconds the creature-approach narration allows an
    /// episode. Long enough to outlast speech it waits behind, not a long mute.
    /// </summary>
    private static readonly TimeSpan EventLifetime = TimeSpan.FromSeconds(10);

    private int fieldId = -1;
    private Scene? said;
    private readonly HashSet<int> shaking = [];
    private readonly Dictionary<int, bool> visible = [];
    private int pendingRelease = -1;
    private int bitten;
    private int standingOn;
    private int warningPlant;

    // What the current line says, part by part, until it has been heard (see Hear). heardBy is when
    // its words will have had time to be said at the delivery's own slow rate, from when the host
    // said it, and is null while the line has not been said, or was cut off. believedUntil bounds
    // how long a screen reader still saying so is believed; stillSaying is that it said so past
    // heardBy. saidOverSince and isSpeaking are the host's delivery's, asked on this thread only.
    private readonly List<Part> unheard = [];
    private DateTime? heardBy;
    private DateTime? believedUntil;
    private bool stillSaying;
    private Func<DateTime?>? saidOverSince;
    private Func<bool?>? isSpeaking;

    public string? CurrentLine { get; private set; }
    public bool CurrentIsUrgent { get; private set; }

    /// <summary>Give a valid pending fact a gap in routine navigation speech.</summary>
    public bool HasUnheardLine => CurrentLine is not null && unheard.Count != 0;

    internal static bool HasPuzzle(int field) => field is 620 or 622 or 623;

    public void Reset()
    {
        fieldId = -1;
        Forget();
    }

    private void Forget()
    {
        said = null;
        shaking.Clear();
        visible.Clear();
        pendingRelease = -1;
        bitten = 0;
        standingOn = 0;
        warningPlant = 0;
        unheard.Clear();
        Unsaid();
        saidOverSince = null;
        isSpeaking = null;
        CurrentIsUrgent = false;
        CurrentLine = null;
    }

    /// <summary>
    /// The host has just said <paramref name="line"/>. If it is the current line, all of it is heard
    /// once it has been said through (see <see cref="Hear"/>); until then the next line says it again.
    /// <paramref name="saidOverSince"/> is when anything else was first said after it, and
    /// <paramref name="isSpeaking"/> the screen reader's own word, or null when it cannot say.
    /// </summary>
    public void NoteSpoken(string line, DateTime now, Func<DateTime?> saidOverSince, Func<bool?>? isSpeaking)
    {
        if (line != CurrentLine)
        {
            return;
        }

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        heardBy = now + TimeSpan.FromTicks(Math.Max(
            FieldActivityLatestLineDelivery.PerWord.Ticks * words,
            FieldActivityLatestLineDelivery.MinimumProtection.Ticks));
        believedUntil = now + FieldActivityLatestLineDelivery.CeilingFor(words);
        stillSaying = false;
        this.saidOverSince = saidOverSince;
        this.isSpeaking = isSpeaking;
    }

    /// <summary>
    /// Whether the line the host said has been heard, asked before each look so that nothing is let go
    /// on time alone. Anything else said over it before then cut it off - both hosts speak with
    /// interruption - and none of it counts as heard: what of it still holds is offered again, and
    /// waits for that speech like any line. Otherwise it is heard once its words have had time to be
    /// said, or, where the screen reader can tell, once it stops saying it, though no longer than the
    /// delivery believes any line. Speech that starts after that is not this line's, and does not
    /// hold it up.
    /// </summary>
    private void Hear(DateTime now)
    {
        if (heardBy is not { } estimate)
        {
            return;
        }

        var over = saidOverSince?.Invoke();
        if (over is { } cut && (cut < estimate || stillSaying))
        {
            Unsaid();
            CurrentLine = null;
            return;
        }

        if (now < estimate)
        {
            return;
        }

        if (over is null && now < believedUntil && Speaking() == true)
        {
            stillSaying = true;
            return;
        }

        unheard.Clear();
        Unsaid();
        CurrentLine = null;
    }

    private void Unsaid()
    {
        heardBy = null;
        believedUntil = null;
        stillSaying = false;
    }

    private bool? Speaking()
    {
        try
        {
            return isSpeaking?.Invoke();
        }
        catch (Exception)
        {
            // A screen reader that fails to answer cannot say.
            return null;
        }
    }

    public FieldActivityCue Observe(FieldActivityObservation observation, DateTime now)
    {
        if (!HasPuzzle(observation.FieldId))
        {
            Reset();
            return default;
        }

        if (observation.FieldId != fieldId)
        {
            Reset();
            fieldId = observation.FieldId;
        }

        if (ReadFacts(observation) is not { } facts)
        {
            // Nothing is known. An older line is not current either, and the next coherent look
            // starts again from the whole state.
            Forget();
            return Silent;
        }

        Hear(now);
        LatchShaking(facts, observation.AncientForestShakingPlants);
        if (!observation.IsPlayerControlled)
        {
            if (JawAt(observation) is { } flytrap)
            {
                bitten = flytrap;
            }

            CurrentLine = null;
            warningPlant = 0;
            CurrentIsUrgent = false;
            return Silent;
        }

        var scene = SceneOf(facts, observation.AncientForestShakingPlants);
        var under = PlantUnder(observation);
        CurrentIsUrgent = warningPlant != 0 && warningPlant == under &&
            scene.Plants[under - 1] == PlantState.Shaking;
        if (!CurrentIsUrgent) warningPlant = 0;
        if (said is not { } before)
        {
            said = scene;
            bitten = 0;
            standingOn = under;
            Remember(observation);
            warningPlant = under != 0 && scene.Plants[under - 1] == PlantState.Shaking ? under : 0;
            CurrentIsUrgent = warningPlant != 0;
            return Say(WholeParts(scene), scene, observation, now);
        }

        // The plant the party stood on when the field last let go of it, now open with the party
        // off it: it opened under them, and its own jump or launch moved them.
        var openedUnder = standingOn != 0 && standingOn != under &&
            before.Plants[standingOn - 1] is PlantState.Closed or PlantState.Shaking &&
            scene.Plants[standingOn - 1] == PlantState.Open
            ? standingOn
            : 0;
        var arrivedOnPlant = under != 0 && standingOn != under;
        standingOn = under;

        var parts = new List<Part>();
        var newsUntil = now + EventLifetime;
        if (bitten != 0)
        {
            parts.Add(new("bite", BiteLine(bitten)) { NewsUntil = newsUntil });
            bitten = 0;
        }

        parts.AddRange(Outcomes(before, scene, observation, newsUntil));
        Remember(observation);
        parts.AddRange(Changes(before, scene, openedUnder));
        said = scene;
        if (under != 0 && scene.Plants[under - 1] == PlantState.Shaking && (parts.Count > 0 || arrivedOnPlant))
        {
            var subject = Plant(under);
            parts.RemoveAll(part => part.Subject == subject);
            parts.Add(new(subject, $"Pitcher plant {under} shaking under you.") { UnderYou = under });
            warningPlant = under;
        }
        else if (parts.Count > 0) warningPlant = 0;
        CurrentIsUrgent = warningPlant != 0;
        if (parts.Count == 0 && unheard.Count == 0)
        {
            return Silent;
        }

        // The warning about the plant underfoot leads, whatever else it is said with.
        var warning = Plant(warningPlant);
        return Say(parts.OrderBy(part => part.Subject == warning ? 0 : 1).ToList(), scene, observation, now);
    }

    public static string? Describe(FieldActivityObservation observation)
    {
        if (!observation.IsPlayerControlled || ReadFacts(observation) is not { } facts)
        {
            return null;
        }

        var states = facts.Closed
            .Select((closed, index) => MissingVisualState(observation.FieldId, index, closed, observation.AncientForestShakingPlants) ? PlantState.Unavailable
                : observation.AncientForestShakingPlants?.Contains(index + 1) == true ? PlantState.Shaking
                : closed ? PlantState.Closed
                : PlantState.Open)
            .ToArray();
        // Confirm picks up, sets down, throws into a plant and uses the beehive, depending on where
        // Cloud stands; Switch gives up. Only the game's own buttons, and no outcome or solution.
        return Whole(new Scene(observation.FieldId, facts.Carried, states, facts.Flytraps)) +
            " Use your normal Confirm button for pickup, release and throws." +
            " Use your normal Switch button to return to the forest entrance.";
    }

    internal static IReadOnlyList<(int Number, int Address)> Flytraps(int field) => field switch
    {
        620 => [(1, 51), (2, 52)],
        623 => [(1, 61)],
        _ => []
    };

    /// <summary>
    /// The creatures, in the order of their native carried codes (bank 5 byte 9 = 1, 2, ...), by
    /// stock entity id with the Objects label.
    /// </summary>
    private static IReadOnlyList<(int Entity, string Label)> Creatures(int field) => field switch
    {
        620 => [(7, "Insect 1"), (8, "Insect 2"), (9, "Frog 1"), (10, "Frog 2"), (16, "Beehive")],
        622 => [(6, "Insect 1"), (7, "Insect 2"), (8, "Insect 3"), (9, "Frog 1"), (10, "Frog 2"), (11, "Frog 3")],
        623 => [(6, "Insect 1"), (7, "Insect 2"), (8, "Insect 3"), (9, "Insect 4"), (10, "Frog 1"), (16, "Beehive")],
        _ => []
    };

    /// <summary>
    /// Where each jaw's bite places the leader, by flytrap number. That is the XYZI, or the JUMP
    /// into the mouth when a frog launch lands him there.
    /// </summary>
    private static IReadOnlyList<(int Flytrap, int X, int Y, int Z)> Jaws(int field) => field switch
    {
        620 => [(1, -130, -360, -12), (2, 1469, -187, -12)],
        622 => [(1, 441, -914, 13)],
        623 => [(1, 592, -285, 37)],
        _ => []
    };

    /// <summary>
    /// The triangles of each plant's top, west to east: the ones ujp0 polls for the leader
    /// standing on a closed plant.
    /// </summary>
    private static IReadOnlyList<int[]> PlantTops(int field) => field switch
    {
        620 => [[7, 8], [19, 20], [21, 22]],
        622 => [[14, 2], [29, 25], [26, 27]],
        623 => [[0, 1], [2, 3], [34, 35], [44, 45]],
        _ => []
    };

    private static int PlantUnder(FieldActivityObservation observation)
    {
        var tops = PlantTops(observation.FieldId);
        for (var index = 0; index < tops.Count; index++)
        {
            if (tops[index].Contains(observation.PlayerTriangle))
            {
                return index + 1;
            }
        }

        return 0;
    }

    /// <summary>The zone byte every bite and launch script clears before it moves the party.</summary>
    private static int ZoneAddress(int field) => field switch { 620 => 20, 622 => 19, _ => 22 };

    private static FieldActivityCue Silent => new(null, false, false) { ReplacesEarlierLine = true };

    /// <summary>
    /// The line for this look: what is new, then whatever of the current line has not been heard and
    /// still holds, each subject once and at its newest. With nothing new nothing is said, but a line
    /// still waiting to be said loses what no longer holds, and a line retired before it was heard -
    /// cut off by other speech, or by the field's own movement before it was said at all - is offered
    /// again at once, or as soon as control returns.
    /// </summary>
    private FieldActivityCue Say(IReadOnlyList<Part> news, Scene scene, FieldActivityObservation observation, DateTime now)
    {
        var kept = unheard
            .Where(part => news.All(newer => newer.Subject != part.Subject))
            .Select(part => StillHolding(part, scene, observation, now))
            .OfType<Part>()
            .ToList();
        var changed = news.Count > 0 || !kept.SequenceEqual(unheard);
        // Only the field moving the party and other speech cutting the line off retire it and keep
        // what it says; then nothing of it has been heard.
        var retiredUnsaid = CurrentLine is null && heardBy is null;
        unheard.Clear();
        unheard.AddRange(news);
        unheard.AddRange(kept);
        if (unheard.Count == 0)
        {
            CurrentLine = null;
            return Silent;
        }

        if (news.Count == 0 && !retiredUnsaid)
        {
            // Said, or still waiting to be: only what no longer holds changes.
            if (changed && CurrentLine is not null)
            {
                CurrentLine = string.Join(' ', unheard.Select(part => part.Text));
            }

            return Silent;
        }

        Unsaid();
        CurrentLine = string.Join(' ', unheard.Select(part => part.Text));
        return new(CurrentLine, false, false) { ReplacesEarlierLine = true };
    }

    /// <summary>
    /// An unheard part as it is now, or null once it no longer holds. The warning about the plant
    /// underfoot is only the plant's state once the leader is off it; a one-off event is news only
    /// for <see cref="EventLifetime"/>; a creature seen somewhere is gone once it is carried or out of
    /// sight, and one no longer known to be at the leader's feet keeps only its release, as a past event.
    /// </summary>
    private Part? StillHolding(Part part, Scene scene, FieldActivityObservation observation, DateTime now)
    {
        if (part.NewsUntil is { } until && now >= until)
        {
            return null;
        }

        if (part.UnderYou != 0 && part.UnderYou != warningPlant)
        {
            return new(part.Subject, $"Pitcher plant {part.UnderYou} {Word(scene.Plants[part.UnderYou - 1])}.");
        }

        if (part.Creature < 0)
        {
            return part;
        }

        if (part.Creature == Carried(Creatures(fieldId), scene.Carried))
        {
            return null;
        }

        foreach (var model in observation.Models)
        {
            if (model.EntityId == part.Creature)
            {
                // Retain an observed release as a past event, but do not keep claiming
                // its current place once that place is unreadable or the leader has left it.
                return model.Status switch
                {
                    FieldActivityReadStatus.Hidden => null,
                    FieldActivityReadStatus.Visible when part.AtFeet && !IsNear(model, observation) => WithoutCurrentPlace(part),
                    FieldActivityReadStatus.Unreadable => WithoutCurrentPlace(part),
                    _ => part
                };
            }
        }

        return WithoutCurrentPlace(part);
    }

    private Part? WithoutCurrentPlace(Part part) => !part.AtFeet ? part :
        Creatures(fieldId).FirstOrDefault(creature => creature.Entity == part.Creature).Label is { } label
            ? part with { Text = $"{label} landed.", AtFeet = false }
            : null;

    private static Facts? ReadFacts(FieldActivityObservation observation)
    {
        if (!HasPuzzle(observation.FieldId) || observation.ReadTemporaryByte is not { } read)
        {
            return null;
        }

        var carried = read(9);
        if (carried < 0 || carried > Creatures(observation.FieldId).Count)
        {
            return null;
        }

        var first = observation.FieldId switch { 620 => 17, 622 => 16, _ => 18 };
        var closed = new bool[observation.FieldId == 623 ? 4 : 3];
        for (var index = 0; index < closed.Length; index++)
        {
            var state = read(first + index);
            if (state is not (0 or 1))
            {
                return null;
            }

            closed[index] = state == 1;
        }

        var flytraps = Flytraps(observation.FieldId);
        var trapped = new bool[flytraps.Count];
        for (var index = 0; index < flytraps.Count; index++)
        {
            var state = read(flytraps[index].Address);
            if (state is not (0 or 1))
            {
                return null;
            }

            trapped[index] = state == 1;
        }

        return new Facts(carried, closed, trapped);
    }

    /// <summary>
    /// A plant seen shaking stays shaking until it is seen open: its flag clear and its entity known
    /// not to be shaking. The shake is what is on screen. The two native passes leave a frame or two
    /// between them, Echo-S 620's first plant clears its own flag during the second pass, and an
    /// unknown sample says nothing either way.
    /// </summary>
    private void LatchShaking(Facts facts, IReadOnlySet<int>? seen)
    {
        for (var plant = 1; plant <= facts.Closed.Count; plant++)
        {
            if (seen?.Contains(plant) == true)
            {
                shaking.Add(plant);
            }
            else if (seen is not null && !facts.Closed[plant - 1])
            {
                shaking.Remove(plant);
            }
        }
    }

    private Scene SceneOf(Facts facts, IReadOnlySet<int>? seen) => new(
        fieldId,
        facts.Carried,
        facts.Closed.Select((closed, index) => MissingVisualState(fieldId, index, closed, seen) ? PlantState.Unavailable
            : shaking.Contains(index + 1) ? PlantState.Shaking
            : closed ? PlantState.Closed
            : PlantState.Open).ToArray(),
        facts.Flytraps);

    // Echo-S 620 plant 1 clears its control flag during its visible second shake.
    // Without a readable background pose, that zero proves neither open nor shaking.
    private static bool MissingVisualState(int field, int index, bool closed, IReadOnlySet<int>? seen) =>
        field == 620 && index == 0 && !closed && seen is null;

    private static int? JawAt(FieldActivityObservation observation)
    {
        if (observation.ReadTemporaryByte is not { } read || read(ZoneAddress(observation.FieldId)) != 0)
        {
            return null;
        }

        foreach (var (flytrap, x, y, z) in Jaws(observation.FieldId))
        {
            var dx = (long)observation.PlayerX - x;
            var dy = (long)observation.PlayerY - y;
            if (dx * dx + dy * dy <= (long)JawRadius * JawRadius &&
                Math.Abs((long)observation.PlayerZ - z) <= JawRadius)
            {
                return flytrap;
            }
        }

        return null;
    }

    private string BiteLine(int flytrap) => fieldId == 620
        ? $"Mutant Flytrap {flytrap} bit the party."
        : "The Mutant Flytrap bit the party.";

    /// <summary>
    /// What is visibly different about the creatures. A creature just released is described where
    /// it is seen: at the leader's feet, landed elsewhere, or nothing when it is out of sight. Any
    /// other creature that comes into view appeared. An unreadable model says nothing, and a release
    /// waits for its model.
    /// </summary>
    private IEnumerable<Part> Outcomes(Scene before, Scene now, FieldActivityObservation observation, DateTime newsUntil)
    {
        var creatures = Creatures(fieldId);
        var held = Carried(creatures, now.Carried);
        var released = Carried(creatures, before.Carried);
        if (released >= 0 && released != held)
        {
            pendingRelease = released;
        }

        if (pendingRelease == held)
        {
            pendingRelease = -1;
        }

        var lines = new List<Part>();
        foreach (var model in observation.Models)
        {
            if (model.Status == FieldActivityReadStatus.Unreadable ||
                creatures.FirstOrDefault(c => c.Entity == model.EntityId).Label is not { } label)
            {
                continue;
            }

            var isVisible = model.Status == FieldActivityReadStatus.Visible;
            var subject = $"creature {model.EntityId}";
            if (model.EntityId == pendingRelease)
            {
                pendingRelease = -1;
                if (isVisible)
                {
                    var atFeet = IsNear(model, observation);
                    lines.Add(new(subject, atFeet ? $"{label} is at your feet." : $"{label} landed.")
                    {
                        Creature = model.EntityId,
                        AtFeet = atFeet,
                        NewsUntil = newsUntil
                    });
                }

                continue;
            }

            if (isVisible && model.EntityId != held &&
                visible.TryGetValue(model.EntityId, out var wasVisible) && !wasVisible)
            {
                lines.Add(new(subject, $"{label} appeared.") { Creature = model.EntityId, NewsUntil = newsUntil });
            }
        }

        return lines;
    }

    private void Remember(FieldActivityObservation observation)
    {
        foreach (var model in observation.Models)
        {
            if (model.Status != FieldActivityReadStatus.Unreadable)
            {
                visible[model.EntityId] = model.Status == FieldActivityReadStatus.Visible;
            }
        }
    }

    private static int Carried(IReadOnlyList<(int Entity, string Label)> creatures, int code) =>
        code >= 1 && code <= creatures.Count ? creatures[code - 1].Entity : -1;

    private static bool IsNear(FieldActivityModelReading model, FieldActivityObservation observation)
    {
        var dx = (long)model.Model.X - observation.PlayerX;
        var dy = (long)model.Model.Y - observation.PlayerY;
        return dx * dx + dy * dy <= (long)FeetRadius * FeetRadius;
    }

    private static IEnumerable<Part> Changes(Scene before, Scene now, int openedUnder)
    {
        if (before.Carried != now.Carried)
        {
            yield return new("carrying", $"Carrying {CarriedName(now.FieldId, now.Carried)}.");
        }

        for (var index = 0; index < now.Plants.Count; index++)
        {
            if (before.Plants[index] != now.Plants[index])
            {
                yield return new(Plant(index + 1), index + 1 == openedUnder
                    ? $"Pitcher plant {index + 1} opened under you."
                    : $"Pitcher plant {index + 1} {Word(now.Plants[index])}.");
            }
        }

        var flytraps = Flytraps(now.FieldId);
        for (var index = 0; index < flytraps.Count; index++)
        {
            if (before.Flytraps[index] != now.Flytraps[index])
            {
                yield return new($"flytrap {flytraps[index].Number}",
                    $"Mutant Flytrap {flytraps[index].Number} {(now.Flytraps[index] ? "closed" : "open")}.");
            }
        }
    }

    private static string Whole(Scene scene) => string.Join(' ', WholeParts(scene).Select(part => part.Text));

    private static List<Part> WholeParts(Scene scene)
    {
        var parts = new List<Part> { new("carrying", $"Carrying {CarriedName(scene.FieldId, scene.Carried)}.") };
        for (var index = 0; index < scene.Plants.Count; index++)
        {
            parts.Add(new(Plant(index + 1), $"{(index == 0 ? "Pitcher plant" : "Plant")} {index + 1} {Word(scene.Plants[index])}."));
        }

        var flytraps = Flytraps(scene.FieldId);
        for (var index = 0; index < flytraps.Count; index++)
        {
            parts.Add(new($"flytrap {flytraps[index].Number}",
                $"Mutant Flytrap {flytraps[index].Number} {(scene.Flytraps[index] ? "closed" : "open")}."));
        }

        // 622's mouth closes only during its native bite, while the field holds the party; the two
        // beehive fields keep their visible permanent closure in the flags above.
        if (scene.FieldId == 622)
        {
            parts.Add(new("flytrap", "Mutant Flytrap open."));
        }

        return parts;
    }

    private static string Plant(int number) => $"plant {number}";

    private static string CarriedName(int field, int code) =>
        code == 0 ? "nothing" : Creatures(field)[code - 1].Label.ToLowerInvariant();

    private static string Word(PlantState state) => state switch
    {
        PlantState.Open => "open",
        PlantState.Shaking => "shaking",
        PlantState.Unavailable => "state unavailable",
        _ => "closed"
    };

    private enum PlantState { Open, Closed, Shaking, Unavailable }

    private sealed record Facts(int Carried, IReadOnlyList<bool> Closed, IReadOnlyList<bool> Flytraps);

    private sealed record Scene(int FieldId, int Carried, IReadOnlyList<PlantState> Plants, IReadOnlyList<bool> Flytraps);

    /// <summary>
    /// One thing a line says, by its subject: what is carried, a plant, a flytrap, the bite or one
    /// creature. A newer part about the same subject replaces it. <see cref="Creature"/> is the
    /// creature a release or appearance was seen of, and <see cref="UnderYou"/> the plant a warning
    /// is about. A one-off event is news until <see cref="NewsUntil"/>; a state has no such limit
    /// and is kept until it has been heard.
    /// </summary>
    private sealed record Part(string Subject, string Text)
    {
        public int Creature { get; init; } = -1;
        public bool AtFeet { get; init; }
        public int UnderYou { get; init; }
        public DateTime? NewsUntil { get; init; }
    }
}

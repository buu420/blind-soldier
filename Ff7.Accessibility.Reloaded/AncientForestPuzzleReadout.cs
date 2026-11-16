namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Announces short changes in the visible Ancient Forest puzzle. The full room state is
/// available through <see cref="Describe"/>. Local numbers match the Objects targets.
/// A shaking plant under the player takes priority once per continuous exposure.
/// </summary>
internal sealed class AncientForestPuzzleReadout
{
    /// <summary>A released creature this close to the leader was set down where he stood.</summary>
    private const int FeetRadius = 64;

    /// <summary>How close the leader must be to a jaw's native placement point to be in its mouth.</summary>
    private const int JawRadius = 24;

    /// <summary>
    /// How long an observed bite or creature release stays relevant while it waits for speech.
    /// </summary>
    private static readonly TimeSpan EventLifetime = TimeSpan.FromSeconds(10);

    private int fieldId = -1;
    private Scene? previousScene;
    private readonly HashSet<int> shaking = [];
    private readonly Dictionary<int, bool> visible = [];
    private int pendingRelease = -1;
    private int bitten;
    private int standingOn;
    private int warnedPlant;
    private int pendingWarning;

    // Keep only the latest useful facts while the speaker is busy. Once submitted, a cue can
    // protect its playback briefly, but a player action can interrupt it without requeuing it.
    private readonly List<Part> pending = [];
    private DateTime? protectUntil;
    private DateTime protectionCeiling;
    private Func<DateTime?>? saidOverSince;
    private Func<bool?>? isSpeaking;

    public string? CurrentLine { get; private set; }
    public bool CurrentIsUrgent => pendingWarning != 0 && CurrentLine is not null;

    /// <summary>Give a valid cue a gap in routine navigation speech.</summary>
    public bool HasPendingSpeech => CurrentLine is not null || protectUntil is not null;

    internal static bool HasPuzzle(int field) => field is 620 or 622 or 623;

    public void Reset()
    {
        fieldId = -1;
        warnedPlant = 0;
        Forget();
    }

    private void Forget()
    {
        previousScene = null;
        shaking.Clear();
        visible.Clear();
        pendingRelease = -1;
        bitten = 0;
        standingOn = 0;
        pendingWarning = 0;
        pending.Clear();
        EndProtection();
        CurrentLine = null;
    }

    /// <summary>
    /// The speaker accepted this cue. Routine speech can wait for it while it plays.
    /// </summary>
    public void NoteSpoken(string line, DateTime now, Func<DateTime?> saidOverSince, Func<bool?>? isSpeaking)
    {
        if (line != CurrentLine)
        {
            return;
        }

        if (pendingWarning != 0)
        {
            warnedPlant = pendingWarning;
            pendingWarning = 0;
        }
        pending.Clear();
        CurrentLine = null;

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        protectUntil = now + TimeSpan.FromTicks(Math.Max(
            FieldActivityLatestLineDelivery.PerWord.Ticks * words,
            FieldActivityLatestLineDelivery.MinimumProtection.Ticks));
        protectionCeiling = now + FieldActivityLatestLineDelivery.CeilingFor(words);
        this.saidOverSince = saidOverSince;
        this.isSpeaking = isSpeaking;
    }

    private void UpdateProtection(DateTime now)
    {
        if (protectUntil is { } end &&
            (saidOverSince?.Invoke() is not null ||
                (now >= end && (now >= protectionCeiling || Speaking() != true))))
        {
            EndProtection();
        }
    }

    private void EndProtection()
    {
        protectUntil = null;
        saidOverSince = null;
        isSpeaking = null;
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

        var under = PlantUnder(observation);
        if (observation.IsPlayerControlled && observation.PlayerTriangle >= 0 && warnedPlant != under)
        {
            warnedPlant = 0;
        }
        if (ReadFacts(observation) is not { } facts)
        {
            // Start from a fresh baseline when coherent data returns. Keep the submission
            // marker for a warning if the player is still on the same plant.
            Forget();
            return Silent;
        }

        UpdateProtection(now);
        LatchShaking(facts, observation.AncientForestShakingPlants);
        if (!observation.IsPlayerControlled)
        {
            if (JawAt(observation) is { } flytrap)
            {
                bitten = flytrap;
            }

            EndProtection();
            pendingWarning = 0;
            return Publish(null);
        }

        var scene = SceneOf(facts, observation.AncientForestShakingPlants);
        if (warnedPlant != 0 && scene.Plants[warnedPlant - 1] is PlantState.Open or PlantState.Closed)
        {
            warnedPlant = 0;
        }
        pendingWarning = under != 0 && warnedPlant != under && scene.Plants[under - 1] == PlantState.Shaking
            ? under
            : 0;

        var changes = new List<Part>();
        var newsUntil = now + EventLifetime;
        if (bitten != 0)
        {
            changes.Add(new("bite", BiteLine(bitten)) { NewsUntil = newsUntil });
            bitten = 0;
        }

        if (previousScene is { } before)
        {
            var openedUnder = standingOn != 0 && standingOn != under &&
                before.Plants[standingOn - 1] is PlantState.Closed or PlantState.Shaking &&
                scene.Plants[standingOn - 1] == PlantState.Open
                ? standingOn
                : 0;
            var plantOpened = scene.Plants.Where((state, index) =>
                state == PlantState.Open && before.Plants[index] is PlantState.Closed or PlantState.Shaking).Any();
            var outcomes = Outcomes(before, scene, observation, newsUntil, plantOpened);
            changes.AddRange(Changes(before, scene, openedUnder, outcomes.Any(part => part.Subject == "carrying")));
            changes.AddRange(outcomes);
        }
        Remember(observation);
        standingOn = under;
        previousScene = scene;
        return Say(changes, scene, observation, now);
    }

    public static string? Describe(FieldActivityObservation observation)
    {
        if (!observation.IsPlayerControlled || ReadFacts(observation) is not { } facts)
        {
            return null;
        }

        var states = ReadPlantStates(observation.FieldId, facts.Closed, observation.AncientForestShakingPlants);
        // Confirm picks up, sets down, throws into a plant and uses the beehive, depending on where
        // Cloud stands; Switch gives up. Only the game's own buttons, and no outcome or solution.
        return string.Join(' ', StatusParts(new Scene(observation.FieldId, facts.Carried, states, facts.Flytraps))) +
            " Use your normal Confirm button for pickup, release and throws." +
            " Use your normal Switch button to return to the forest entrance.";
    }

    internal static string? DescribeSelectedPlant(
        FieldNavigationTarget target, Func<int, AncientForestPuzzleSnapshot?> readState)
    {
        const string plantLabel = "Pitcher plant ";
        if (target.Category != FieldNavigationCategory.Objects || !HasPuzzle(target.FieldId) ||
            !target.Label.StartsWith(plantLabel, StringComparison.Ordinal))
            return null;

        var suffix = target.Label.AsSpan(plantLabel.Length);
        var comma = suffix.IndexOf(',');
        if (comma < 0 || !int.TryParse(suffix[..comma], out var number) ||
            readState(target.FieldId) is not { } snapshot ||
            ReadClosedPlants(target.FieldId, address => address < snapshot.Bank.Length ? snapshot.Bank[address] : -1) is not { } closed ||
            number < 1 || number > closed.Length)
            return null;

        var state = ReadPlantStates(target.FieldId, closed, snapshot.ShakingPlants)[number - 1];
        return state == PlantState.Unavailable ? null : $"Plant {number} {Word(state)}";
    }

    private static PlantState[] ReadPlantStates(int field, IReadOnlyList<bool> closed, IReadOnlySet<int>? seen) =>
        closed.Select((isClosed, index) => MissingVisualState(field, index, isClosed, seen) ? PlantState.Unavailable
            : seen?.Contains(index + 1) == true ? PlantState.Shaking
            : isClosed ? PlantState.Closed
            : PlantState.Open).ToArray();

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

    /// <summary>While speech is busy, retain up to two current, as yet unsubmitted facts.</summary>
    private FieldActivityCue Say(IReadOnlyList<Part> news, Scene scene, FieldActivityObservation observation, DateTime now)
    {
        var kept = news.Concat(pending).DistinctBy(part => part.Subject)
            .Select(part => StillHolding(part, scene, observation, now))
            .OfType<Part>()
            .Take(2)
            .ToArray();
        pending.Clear();
        pending.AddRange(kept);
        if (pendingWarning != 0)
            return Publish($"Pitcher plant {pendingWarning} shaking under you.");

        return Publish(protectUntil is null && kept.Length > 0
            ? string.Join(' ', kept.Select(part => part.Text))
            : null);
    }

    private FieldActivityCue Publish(string? line)
    {
        var changed = line != CurrentLine;
        CurrentLine = line;
        return changed ? new(line, false, false) { ReplacesEarlierLine = true } : Silent;
    }

    /// <summary>
    /// Retire obsolete states, older events, and stale creature positions before delivery.
    /// </summary>
    private Part? StillHolding(Part part, Scene scene, FieldActivityObservation observation, DateTime now)
    {
        if (part.NewsUntil is { } until && now >= until)
        {
            return null;
        }

        if (part.PlantNumber != 0 && scene.Plants[part.PlantNumber - 1] != part.PlantPose)
        {
            return null;
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

        if (ReadClosedPlants(observation.FieldId, read) is not { } closed)
            return null;

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

    private static bool[]? ReadClosedPlants(int field, Func<int, int> read)
    {
        if (!HasPuzzle(field))
            return null;

        var first = field switch { 620 => 17, 622 => 16, _ => 18 };
        var closed = new bool[field == 623 ? 4 : 3];
        for (var index = 0; index < closed.Length; index++)
        {
            var state = read(first + index);
            if (state is not (0 or 1))
                return null;

            closed[index] = state == 1;
        }

        return closed;
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
    /// Report a visible release and frogs that reappear as plants open.
    /// </summary>
    private List<Part> Outcomes(
        Scene before, Scene now, FieldActivityObservation observation, DateTime newsUntil, bool plantOpened)
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
                    lines.Add(new(held < 0 ? "carrying" : subject, atFeet ? $"{label} is at your feet." : $"{label} landed.")
                    {
                        Creature = model.EntityId,
                        AtFeet = atFeet,
                        NewsUntil = newsUntil
                    });
                }

                continue;
            }

            if (plantOpened && isVisible && model.EntityId != held && label.StartsWith("Frog ", StringComparison.Ordinal) &&
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

    private static IEnumerable<Part> Changes(Scene before, Scene now, int openedUnder, bool describedRelease)
    {
        if (openedUnder != 0)
        {
            yield return new(Plant(openedUnder), $"Pitcher plant {openedUnder} opened under you.")
            {
                PlantNumber = openedUnder,
                PlantPose = PlantState.Open
            };
        }

        for (var index = 0; index < now.Plants.Count; index++)
        {
            var state = now.Plants[index];
            if (index + 1 != openedUnder && before.Plants[index] != state && state is PlantState.Open or PlantState.Closed)
            {
                yield return new(Plant(index + 1), $"Pitcher plant {index + 1} {Word(state)}.")
                {
                    PlantNumber = index + 1,
                    PlantPose = state
                };
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

        if (!describedRelease && before.Carried != now.Carried)
        {
            yield return new("carrying", $"Carrying {CarriedName(now.FieldId, now.Carried)}.");
        }
    }

    private static IEnumerable<string> StatusParts(Scene scene)
    {
        yield return $"Carrying {CarriedName(scene.FieldId, scene.Carried)}.";
        for (var index = 0; index < scene.Plants.Count; index++)
        {
            yield return $"{(index == 0 ? "Pitcher plant" : "Plant")} {index + 1} {Word(scene.Plants[index])}.";
        }

        var flytraps = Flytraps(scene.FieldId);
        for (var index = 0; index < flytraps.Count; index++)
        {
            yield return $"Mutant Flytrap {flytraps[index].Number} {(scene.Flytraps[index] ? "closed" : "open")}.";
        }

        // 622's mouth closes only during its native bite, while the field holds the party; the two
        // beehive fields keep their visible permanent closure in the flags above.
        if (scene.FieldId == 622)
        {
            yield return "Mutant Flytrap open.";
        }
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
    /// One current fact. A newer fact about the same subject replaces it.
    /// </summary>
    private sealed record Part(string Subject, string Text)
    {
        public int Creature { get; init; } = -1;
        public bool AtFeet { get; init; }
        public int PlantNumber { get; init; }
        public PlantState PlantPose { get; init; }
        public DateTime? NewsUntil { get; init; }
    }
}

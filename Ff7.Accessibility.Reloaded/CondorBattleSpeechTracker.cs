namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Turns Fort Condor battle snapshots into the things a sighted player is told.
/// </summary>
/// <remarks>
/// The battle has a banner that names each event as it happens, a stat panel for
/// whatever the cursor is over, a hire list, and a funds counter. None of it is
/// text, so all of it is reconstructed here from state.
///
/// <para>Only edges are announced. A player moving the cursor across open ground
/// is shown nothing new and is told nothing new; what they can see at a glance
/// and a blind player cannot - where they are, how much money is left, what is
/// closing in - is on the status key instead of in a running commentary.</para>
/// </remarks>
public sealed class CondorBattleSpeechTracker
{
    /// <summary>
    /// The banner messages, by the identifier the renderer draws them from. The
    /// wording is the game's own, recovered from <c>emes00.tex</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> Messages =
        new Dictionary<int, string>
        {
            [0] = "Encountered enemy.",
            [1] = "Start combat.",
            [2] = "Halted enemy attack!",
            [3] = "Arrived at the directed position.",
            [7] = "Enemy invasion.",
            [10] = "Enemy destroyed.",
            [12] = "Set units.",
            [13] = "Start the game? Yes. No."
        };

    private const int OutcomeOngoing = 0;
    private const int OutcomeVictory = 1;
    private const int OutcomeInvasion = 2;

    private const int VictoryMessageId = 2;
    private const int InvasionMessageId = 7;

    private readonly Action<string> log;
    private readonly HashSet<int> reportedUnknownTypes = [];

    private bool started;
    private int lastMessageId;
    private int lastOutcome;
    private bool lastSettingMenuOpen;
    private int? lastHighlightedTypeId;
    private int lastUnitUnderCursorSlot;
    private bool lastPlacementLegal;
    private int lastAlliedCount;

    public CondorBattleSpeechTracker(Action<string>? log = null) =>
        this.log = log ?? (_ => { });

    /// <summary>Forgets the battle, so re-entering module 9 announces itself again.</summary>
    public void Reset()
    {
        started = false;
        reportedUnknownTypes.Clear();
    }

    /// <summary>
    /// The lines to speak for this snapshot, in the order they should be heard.
    /// </summary>
    public IReadOnlyList<string> Observe(CondorBattleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        ReportUnknownUnitTypes(snapshot);

        if (!started)
        {
            started = true;
            Remember(snapshot);
            return [DescribeStatus(snapshot)];
        }

        var lines = new List<string>();

        // The banner is the loudest thing on screen and it names events the
        // player cannot otherwise detect, so it goes first.
        if (snapshot.MessageId != lastMessageId &&
            Messages.TryGetValue(snapshot.MessageId, out var message))
        {
            lines.Add(message);
        }

        // The outcome is written by the code that ends the battle. Normally the
        // banner says it too, and saying it twice would be worse than saying it
        // once; this is here so the result is never missed if it does not.
        if (snapshot.Outcome != lastOutcome && snapshot.Outcome != OutcomeOngoing)
        {
            var expectedMessageId = snapshot.Outcome == OutcomeVictory
                ? VictoryMessageId
                : InvasionMessageId;
            if (snapshot.MessageId != expectedMessageId)
            {
                lines.Add(snapshot.Outcome switch
                {
                    OutcomeVictory => Messages[VictoryMessageId],
                    OutcomeInvasion => Messages[InvasionMessageId],
                    _ => $"Battle state {snapshot.Outcome}."
                });
            }
        }

        if (snapshot.SettingMenuOpen && !lastSettingMenuOpen)
        {
            lines.Add($"Setting menu. {snapshot.Gil} gil.");
            if (DescribeHighlightedUnit(snapshot) is { } opened)
            {
                lines.Add(opened);
            }
        }
        else if (snapshot.SettingMenuOpen &&
                 snapshot.HighlightedTypeId != lastHighlightedTypeId &&
                 DescribeHighlightedUnit(snapshot) is { } moved)
        {
            lines.Add(moved);
        }

        // A unit appearing on the player's side after the menu closes is a hire
        // that went through. The game shows the new unit and the funds dropping;
        // this is the same fact.
        if (snapshot.AlliedCount > lastAlliedCount && lastSettingMenuOpen && !snapshot.SettingMenuOpen)
        {
            lines.Add($"Placed. {snapshot.Gil} gil.");
        }

        if (!snapshot.SettingMenuOpen && snapshot.InteractionMode == CondorBattleSnapshot.CursorInteractionMode)
        {
            lines.AddRange(ObserveCursor(snapshot));
        }

        Remember(snapshot);
        return lines;
    }

    /// <summary>
    /// The picture a sighted player takes in at a glance: what is left, what it
    /// costs, where the cursor is, and what is nearest to it.
    /// </summary>
    public string DescribeStatus(CondorBattleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var parts = new List<string>
        {
            $"{snapshot.Gil} gil",
            Pluralize(snapshot.AlliedCount, "unit", "units"),
            Pluralize(snapshot.EnemyCount, "enemy", "enemies")
        };

        if (snapshot.UnitUnderCursor is { } under)
        {
            parts.Add($"cursor on {under.Describe()}");
        }
        else
        {
            parts.Add(snapshot.CursorPlacementLegal ? "can place here" : "cannot place here");
        }

        if (snapshot.NearestEnemy is { } nearest)
        {
            var direction = snapshot.DirectionFromCursor(nearest);
            var distance = snapshot.DistanceFromCursor(nearest);
            parts.Add(direction == "here"
                ? $"nearest {nearest.Describe()} at the cursor"
                : $"nearest {nearest.Describe()}, {distance} {direction}");
        }

        return string.Join(". ", parts) + ".";
    }

    private IEnumerable<string> ObserveCursor(CondorBattleSnapshot snapshot)
    {
        if (snapshot.UnitUnderCursorSlot != lastUnitUnderCursorSlot)
        {
            if (snapshot.UnitUnderCursor is { } unit)
            {
                yield return unit.Describe() + ".";
            }
            else if (lastUnitUnderCursorSlot >= 0)
            {
                // Leaving a unit for open ground. The stat panel clears, so say
                // that it cleared rather than leaving the last unit standing as
                // the player's mental picture of where the cursor is.
                yield return snapshot.CursorPlacementLegal ? "Clear." : "Blocked.";
            }

            yield break;
        }

        // Whether the spot under the cursor can take a unit is drawn into the
        // cursor itself, and it is the difference between a confirm that opens
        // the hire list and one that does nothing at all.
        if (snapshot.UnitUnderCursorSlot < 0 && snapshot.CursorPlacementLegal != lastPlacementLegal)
        {
            yield return snapshot.CursorPlacementLegal ? "Clear." : "Blocked.";
        }
    }

    private string? DescribeHighlightedUnit(CondorBattleSnapshot snapshot)
    {
        if (snapshot.HighlightedTypeId is not { } typeId)
        {
            return null;
        }

        var unit = CondorUnitCatalog.ResolveByRecordIndex(typeId);
        return unit is null
            ? $"Unit type {typeId}."
            : CondorUnitCatalog.DescribeForHire(unit, snapshot.Gil);
    }

    /// <summary>
    /// Records any unit type the catalog cannot name.
    ///
    /// <para>Only the ten hireable types have been tied to the names in
    /// <c>emes01.tex</c>. The enemy roster has not, so an enemy is announced by
    /// side alone. Logging the identifiers that actually turn up is what will
    /// close that gap, and it costs one line per unseen type per battle.</para>
    /// </summary>
    private void ReportUnknownUnitTypes(CondorBattleSnapshot snapshot)
    {
        foreach (var unit in snapshot.Units)
        {
            if (CondorUnitCatalog.ResolveByRecordIndex(unit.TypeId) is not null ||
                !reportedUnknownTypes.Add(unit.TypeId))
            {
                continue;
            }

            log(
                $"Fort Condor: unnamed unit type {unit.TypeId} in slot {unit.Slot} " +
                $"({(unit.IsEnemy ? "enemy" : "allied")}), {unit.CurrentHp} of {unit.MaximumHp} HP, " +
                $"attack {unit.Attack}.");
        }
    }

    private void Remember(CondorBattleSnapshot snapshot)
    {
        lastMessageId = snapshot.MessageId;
        lastOutcome = snapshot.Outcome;
        lastSettingMenuOpen = snapshot.SettingMenuOpen;
        lastHighlightedTypeId = snapshot.HighlightedTypeId;
        lastUnitUnderCursorSlot = snapshot.UnitUnderCursorSlot;
        lastPlacementLegal = snapshot.CursorPlacementLegal;
        lastAlliedCount = snapshot.AlliedCount;
    }

    private static string Pluralize(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";
}

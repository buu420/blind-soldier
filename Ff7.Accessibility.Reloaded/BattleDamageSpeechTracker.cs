namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Speaks what a battle damage popup shows. The engine calculates a whole action
/// before its animation starts, so live HP and MP are already final when the
/// first popup draws; the popup's own number and flags are the only per-hit
/// truth, and they are what a sighted player reads.
/// </summary>
public sealed class BattleDamageSpeechTracker
{
    private readonly object sync = new();
    private string? pending;

    /// <summary>
    /// Kept for existing callers. Popups carry their own value and flags, so no
    /// HP baseline is needed.
    /// </summary>
    public void SeedActors(IReadOnlyList<BattleActorSnapshot> actors)
    {
        ArgumentNullException.ThrowIfNull(actors);
    }

    public void Observe(BattleDamagePopupSnapshot popup, BattleActorSnapshot actor)
    {
        if (!popup.IsValid ||
            popup.TargetActor != actor.ActorIndex ||
            !IsBattleActor(actor.ActorIndex) ||
            string.IsNullOrWhiteSpace(actor.Name))
        {
            return;
        }

        var text = FormatPopup(actor.Name, popup.Value, popup.Flags);
        lock (sync)
        {
            pending = text;
        }
    }

    /// <summary>
    /// Speaks one popup read with its native provenance. A drain recovery names
    /// the drainer as the recipient and the drainer's own targets as the source.
    /// </summary>
    public void ObserveVisibleResult(
        BattleVisibleResultSnapshot result,
        IReadOnlyList<BattleActorSnapshot> actors)
    {
        ArgumentNullException.ThrowIfNull(actors);
        if (!result.IsValid ||
            !IsBattleActor(result.TargetActor) ||
            !TryFindName(actors, result.TargetActor, out var target))
        {
            return;
        }

        var text = result.IsDrainRecovery && result.ShowsNumber
            ? FormatDrain(target, result, SourceNames(actors, result))
            : FormatPopup(target, result.Value, result.Flags);
        lock (sync)
        {
            pending = text;
        }
    }

    public string? Poll()
    {
        lock (sync)
        {
            var result = pending;
            pending = null;
            return result;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            pending = null;
        }
    }

    private static string? FormatPopup(string name, int value, int flags)
    {
        switch (value)
        {
            case BattleVisibleResultSnapshot.MissValue:
                return $"Attack missed {name}.";
            case BattleVisibleResultSnapshot.FullRestoreValue:
                return $"{name}'s HP and MP were fully restored.";
            case < 0:
                // -2 marks an elemental instant death; the status tracker reports
                // the defeat from the lethal result row instead.
                return null;
        }

        var recovery = (flags & BattleVisibleResultSnapshot.RecoveryFlag) != 0;
        var mp = (flags & BattleVisibleResultSnapshot.MpFlag) != 0;
        return (recovery, mp) switch
        {
            (true, true) => $"{name} recovered {value} MP.",
            (true, false) => $"{name} recovered {value} HP.",
            (false, true) => $"{name} lost {value} MP.",
            _ => $"{name} took {value} damage."
        };
    }

    private static string FormatDrain(
        string drainer,
        BattleVisibleResultSnapshot result,
        IReadOnlyList<string> sources)
    {
        var from = sources.Count == 0 ? null : JoinNames(sources);
        if (result.IsRecovery)
        {
            var stat = result.IsMp ? "MP" : "HP";
            return from is null
                ? $"{drainer} drained {result.Value} {stat}."
                : $"{drainer} drained {result.Value} {stat} from {from}.";
        }

        // FUN_005df30b reverses the transfer when the drained target was healed
        // (undead or absorbing): the drainer is hurt by its own drain.
        if (result.IsMp)
        {
            return from is null
                ? $"{drainer} lost {result.Value} MP from the drain."
                : $"{drainer} lost {result.Value} MP draining {from}.";
        }

        return from is null
            ? $"{drainer} took {result.Value} damage from the drain."
            : $"{drainer} took {result.Value} damage draining {from}.";
    }

    private static IReadOnlyList<string> SourceNames(
        IReadOnlyList<BattleActorSnapshot> actors,
        BattleVisibleResultSnapshot result)
    {
        var names = new List<string>();
        foreach (var actor in actors.OrderBy(actor => actor.ActorIndex))
        {
            if (actor.ActorIndex != result.TargetActor &&
                IsBattleActor(actor.ActorIndex) &&
                (result.DrainSourceMask & (1 << actor.ActorIndex)) != 0 &&
                !string.IsNullOrWhiteSpace(actor.Name))
            {
                names.Add(actor.Name);
            }
        }

        return names;
    }

    private static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}"
    };

    private static bool TryFindName(
        IReadOnlyList<BattleActorSnapshot> actors,
        int actorIndex,
        out string name)
    {
        foreach (var actor in actors)
        {
            if (actor.ActorIndex == actorIndex && !string.IsNullOrWhiteSpace(actor.Name))
            {
                name = actor.Name;
                return true;
            }
        }

        name = string.Empty;
        return false;
    }

    private static bool IsBattleActor(int actorIndex) =>
        actorIndex is >= 0 and < 3 or >= 4 and <= 9;
}

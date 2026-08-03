namespace Ff7.Accessibility.Reloaded;

public sealed class BattleTargetSpeechTracker
{
    private readonly object sync = new();
    private string lastTargetKey = string.Empty;
    private string? pending;

    public void Observe(BattleTargetSnapshot snapshot)
    {
        lock (sync)
        {
            pending = null;
            if (!snapshot.IsValid || !snapshot.IsTargeting || string.IsNullOrWhiteSpace(snapshot.Actor.Name))
            {
                lastTargetKey = string.Empty;
                return;
            }

            var selectsWholeRow = snapshot.TargetMode is 5 or 6;
            var key = selectsWholeRow
                ? $"group\u001f{snapshot.TargetMode}\u001f{snapshot.TargetMask}\u001f{snapshot.Actor.IsEnemy}"
                : $"{snapshot.SelectedTarget}\u001f{snapshot.TargetMask}\u001f{snapshot.Actor.Name}";
            if (string.Equals(key, lastTargetKey, StringComparison.Ordinal))
            {
                return;
            }

            lastTargetKey = key;
            pending = selectsWholeRow
                ? snapshot.Actor.IsEnemy ? "All enemies" : "All allies"
                : Format(snapshot.Actor);
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
            lastTargetKey = string.Empty;
            pending = null;
        }
    }

    private static string Format(BattleActorSnapshot actor)
    {
        if (actor.IsEnemy && !actor.InformationVisible)
        {
            return actor.Name;
        }

        return $"{actor.Name}. HP {actor.CurrentHp} of {actor.MaxHp}. MP {actor.CurrentMp} of {actor.MaxMp}";
    }
}

namespace Ff7.Accessibility.Reloaded;

public sealed class BattleEnemyActionSpeechTracker
{
    private readonly object sync = new();
    private string lastEventKey = string.Empty;
    private string? pending;

    public void Observe(
        BattleEnemyActionSnapshot snapshot,
        IReadOnlyList<BattleActorSnapshot> actors)
    {
        lock (sync)
        {
            if (!snapshot.IsValid)
            {
                lastEventKey = string.Empty;
                return;
            }

            var eventKey = $"{snapshot.EventIndex}\u001f{snapshot.AttackerActorIndex}\u001f" +
                $"{snapshot.CommandId}\u001f{snapshot.SceneAttackIndex}\u001f{snapshot.ActionId}";
            if (string.Equals(eventKey, lastEventKey, StringComparison.Ordinal))
            {
                return;
            }

            lastEventKey = eventKey;
            if (!string.IsNullOrWhiteSpace(snapshot.AccessibilityDescription))
            {
                pending = snapshot.AccessibilityDescription.Trim();
                return;
            }

            if (string.IsNullOrWhiteSpace(snapshot.ActionName))
            {
                return;
            }

            var attacker = actors.FirstOrDefault(actor => actor.ActorIndex == snapshot.AttackerActorIndex);
            if (string.IsNullOrWhiteSpace(attacker.Name))
            {
                return;
            }

            pending = $"{attacker.Name} used {snapshot.ActionName}.";
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
            lastEventKey = string.Empty;
            pending = null;
        }
    }
}

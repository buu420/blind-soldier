namespace Ff7.Accessibility.Reloaded;

public sealed class BattleDamageSpeechTracker
{
    private const int HealingPopupFlag = 0x04;
    private readonly object sync = new();
    private readonly Dictionary<int, ActorResources> resourcesByActor = new();
    private string? pending;

    public void SeedActors(IReadOnlyList<BattleActorSnapshot> actors)
    {
        lock (sync)
        {
            foreach (var actor in actors)
            {
                if (IsBattleActor(actor) &&
                    CanUsePublicHp(actor) &&
                    !resourcesByActor.ContainsKey(actor.ActorIndex))
                {
                    resourcesByActor[actor.ActorIndex] = Resources(actor);
                }
            }
        }
    }

    public void Observe(BattleDamagePopupSnapshot popup, BattleActorSnapshot actor)
    {
        if (!popup.IsValid || popup.TargetActor != actor.ActorIndex || !IsBattleActor(actor))
        {
            return;
        }

        lock (sync)
        {
            if (popup.IsMiss)
            {
                if (CanUsePublicHp(actor))
                {
                    resourcesByActor[actor.ActorIndex] = Resources(actor);
                }

                pending = $"Attack missed {actor.Name}.";
                return;
            }

            if (!CanUsePublicHp(actor))
            {
                if ((popup.Flags & HealingPopupFlag) != 0)
                {
                    pending = $"{actor.Name} recovered {popup.Value} HP.";
                }
                else
                {
                    pending = $"{actor.Name} took {popup.Value} damage.";
                }

                return;
            }

            if (!resourcesByActor.TryGetValue(actor.ActorIndex, out var previous))
            {
                resourcesByActor[actor.ActorIndex] = Resources(actor);
                return;
            }

            resourcesByActor[actor.ActorIndex] = Resources(actor);
            var recoveredHp = actor.CurrentHp - previous.Hp;
            var recoveredMp = actor.CurrentMp - previous.Mp;
            if (recoveredHp > 0 || recoveredMp > 0)
            {
                pending = BuildRecoverySpeech(actor.Name, recoveredHp, recoveredMp);
                return;
            }

            if ((popup.Flags & HealingPopupFlag) != 0)
            {
                return;
            }

            if (actor.CurrentHp < previous.Hp)
            {
                pending = $"{actor.Name} took {popup.Value} damage.";
            }
            else if (actor.CurrentMp < previous.Mp)
            {
                pending = $"{actor.Name} lost {previous.Mp - actor.CurrentMp} MP.";
            }
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
            resourcesByActor.Clear();
            pending = null;
        }
    }

    private static bool IsBattleActor(BattleActorSnapshot actor) =>
        actor.ActorIndex is >= 0 and < 3 or >= 4 and <= 9;

    private static bool CanUsePublicHp(BattleActorSnapshot actor) =>
        !actor.IsEnemy || actor.InformationVisible;

    private static ActorResources Resources(BattleActorSnapshot actor) =>
        new(actor.CurrentHp, actor.CurrentMp);

    private static string BuildRecoverySpeech(string name, int recoveredHp, int recoveredMp)
    {
        if (recoveredHp > 0 && recoveredMp > 0)
        {
            return $"{name} recovered {recoveredHp} HP and {recoveredMp} MP.";
        }

        return recoveredHp > 0
            ? $"{name} recovered {recoveredHp} HP."
            : $"{name} recovered {recoveredMp} MP.";
    }

    private readonly record struct ActorResources(int Hp, int Mp);
}

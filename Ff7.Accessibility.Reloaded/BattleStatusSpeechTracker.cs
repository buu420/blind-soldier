namespace Ff7.Accessibility.Reloaded;

public sealed class BattleStatusSpeechTracker
{
    private readonly object sync = new();
    private readonly Dictionary<int, ActorStatusState> states = new();
    private readonly HashSet<int> confirmedDeaths = [];
    private readonly Queue<string> pending = new();

    public void Observe(IReadOnlyList<BattleActorSnapshot> actors)
    {
        lock (sync)
        {
            foreach (var actor in actors)
            {
                if (string.IsNullOrWhiteSpace(actor.Name))
                {
                    continue;
                }

                if (!states.TryGetValue(actor.ActorIndex, out var previous) ||
                    !string.Equals(previous.Name, actor.Name, StringComparison.Ordinal))
                {
                    confirmedDeaths.Remove(actor.ActorIndex);
                    states[actor.ActorIndex] = new ActorStatusState(actor.Name, actor.IsEnemy, actor.StatusMask);
                    EnqueueChanges(actor, 0, actor.StatusMask);
                    continue;
                }

                if (previous.Mask == actor.StatusMask)
                {
                    continue;
                }

                states[actor.ActorIndex] = new ActorStatusState(actor.Name, actor.IsEnemy, actor.StatusMask);
                EnqueueChanges(actor, previous.Mask, actor.StatusMask);
            }
        }
    }

    public void ConfirmDamage(BattleDamagePopupSnapshot popup, BattleActorSnapshot actor)
    {
        const int healingPopupFlag = 0x04;
        if (!popup.IsValid ||
            popup.TargetActor != actor.ActorIndex ||
            popup.IsMiss ||
            (popup.Flags & healingPopupFlag) != 0 ||
            (actor.StatusMask & 1u) == 0)
        {
            return;
        }

        lock (sync)
        {
            if (confirmedDeaths.Add(actor.ActorIndex))
            {
                pending.Enqueue(Format(actor.Name, actor.IsEnemy, 0, gained: true));
            }
        }
    }

    internal void ConfirmVisibleDamageOutcome(
        BattleDamagePopupSnapshot popup,
        BattleActorVisibleCorrelation actor)
    {
        const int healingPopupFlag = 0x04;
        if (!popup.IsValid ||
            popup.TargetActor != actor.ActorIndex ||
            popup.IsMiss ||
            (popup.Flags & healingPopupFlag) != 0 ||
            !actor.IsDefeated ||
            string.IsNullOrWhiteSpace(actor.Name))
        {
            return;
        }

        lock (sync)
        {
            if (confirmedDeaths.Add(actor.ActorIndex))
            {
                pending.Enqueue(Format(actor.Name, actor.IsEnemy, 0, gained: true));
            }
        }
    }

    public string? Poll()
    {
        lock (sync)
        {
            return pending.Count > 0 ? pending.Dequeue() : null;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            states.Clear();
            confirmedDeaths.Clear();
            pending.Clear();
        }
    }

    private void EnqueueChanges(BattleActorSnapshot actor, uint previousMask, uint currentMask)
    {
        var changed = previousMask ^ currentMask;
        for (var bit = 0; bit < 32; bit++)
        {
            var flag = 1u << bit;
            if ((changed & flag) == 0)
            {
                continue;
            }

            if (bit == 0)
            {
                if ((currentMask & flag) == 0 && confirmedDeaths.Remove(actor.ActorIndex))
                {
                    pending.Enqueue(Format(actor.Name, actor.IsEnemy, bit, gained: false));
                }

                continue;
            }

            pending.Enqueue(Format(actor.Name, actor.IsEnemy, bit, (currentMask & flag) != 0));
        }
    }

    private static string Format(string name, bool isEnemy, int bit, bool gained) => (bit, gained) switch
    {
        (0, true) => isEnemy ? $"{name} was defeated." : $"{name} was knocked out.",
        (0, false) => $"{name} was revived.",
        (1, true) => $"{name} is in critical condition.",
        (1, false) => $"{name} is no longer in critical condition.",
        (2, true) => $"{name} fell asleep.",
        (2, false) => $"{name} woke up.",
        (3, true) => $"{name} was poisoned.",
        (3, false) => $"{name}'s poison cleared.",
        (6, true) => $"{name} became confused.",
        (6, false) => $"{name} is no longer confused.",
        (7, true) => $"{name} was silenced.",
        (7, false) => $"{name}'s Silence wore off.",
        (11, true) => $"{name} turned into a frog.",
        (11, false) => $"{name} is no longer a frog.",
        (12, true) => $"{name} was made small.",
        (12, false) => $"{name} returned to normal size.",
        (14, true) => $"{name} was petrified.",
        (14, false) => $"{name} is no longer petrified.",
        (_, true) => $"{name} gained {StatusName(bit)}.",
        _ => $"{name}'s {StatusName(bit)} wore off."
    };

    private static string StatusName(int bit) => bit switch
    {
        0 => "Death",
        1 => "Near Death",
        2 => "Sleep",
        3 => "Poison",
        4 => "Sadness",
        5 => "Fury",
        6 => "Confusion",
        7 => "Silence",
        8 => "Haste",
        9 => "Slow",
        10 => "Stop",
        11 => "Frog",
        12 => "Small",
        13 => "Slow Numb",
        14 => "Petrify",
        15 => "Regen",
        16 => "Barrier",
        17 => "Magic Barrier",
        18 => "Reflect",
        19 => "Dual",
        20 => "Shield",
        21 => "Death Sentence",
        22 => "Manipulate",
        23 => "Berserk",
        24 => "Peerless",
        25 => "Paralysis",
        26 => "Darkness",
        27 => "Dual Drain",
        28 => "Death Force",
        29 => "Resist",
        30 => "Lucky Girl",
        31 => "Imprisoned",
        _ => "Unknown Status"
    };

    private readonly record struct ActorStatusState(string Name, bool IsEnemy, uint Mask);
}

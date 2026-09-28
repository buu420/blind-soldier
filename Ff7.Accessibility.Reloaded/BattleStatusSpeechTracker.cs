namespace Ff7.Accessibility.Reloaded;

public sealed class BattleStatusSpeechTracker
{
    private const uint DeathMask = 1u;

    // FUN_0041c0bb copies the live mask into 0x009A87FC without its top four
    // bits (Death Force, Resist, Lucky Girl, Imprisoned), and the queue's status
    // event copies that into the drawn mask; a hit copies the full word. Those
    // bits therefore stay announced while the live mask still holds them.
    private const uint UncopiedStatusBits = 0xF0000000u;

    private readonly object sync = new();
    private readonly Dictionary<int, ActorStatusState> states = new();
    private readonly Dictionary<int, uint> deferredMasks = new();
    private readonly HashSet<int> confirmedDeaths = [];
    private readonly Queue<string> pending = new();

    /// <summary>
    /// Follows the live status mask. The damage calculation writes it before the
    /// action's animation starts, so hosts should prefer <see cref="ObserveDisplayed"/>.
    /// </summary>
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

    /// <summary>
    /// Follows the status each actor is drawn with, so a change is spoken when
    /// its effect reaches the actor on screen instead of when it is calculated.
    /// The drawn mask passes through the same enemy privacy rule as the live one.
    /// A change on an actor whose damage popup is about to draw waits for that
    /// popup, so its number is heard first; <see cref="ReleaseDeferred"/> or the
    /// next observation without a pending popup releases it.
    /// </summary>
    public void ObserveDisplayed(
        IReadOnlyList<BattleActorSnapshot> actors,
        BattleDisplayedStatusSnapshot displayed)
    {
        ArgumentNullException.ThrowIfNull(actors);
        if (!displayed.IsValid)
        {
            return;
        }

        lock (sync)
        {
            foreach (var actor in actors)
            {
                if (string.IsNullOrWhiteSpace(actor.Name))
                {
                    continue;
                }

                var drawn = (actor with { StatusMask = displayed.MaskFor(actor.ActorIndex) }).StatusMask;
                if (!states.TryGetValue(actor.ActorIndex, out var previous) ||
                    !string.Equals(previous.Name, actor.Name, StringComparison.Ordinal))
                {
                    confirmedDeaths.Remove(actor.ActorIndex);
                    deferredMasks.Remove(actor.ActorIndex);
                    previous = new ActorStatusState(actor.Name, actor.IsEnemy, 0);
                    states[actor.ActorIndex] = previous;
                }

                var effective = drawn | (previous.Mask & actor.StatusMask & UncopiedStatusBits);
                if (displayed.HasPendingPopup(actor.ActorIndex))
                {
                    if (effective == previous.Mask)
                    {
                        deferredMasks.Remove(actor.ActorIndex);
                    }
                    else
                    {
                        deferredMasks[actor.ActorIndex] = effective;
                    }

                    continue;
                }

                deferredMasks.Remove(actor.ActorIndex);
                ApplyDisplayed(actor.ActorIndex, previous, effective);
            }
        }
    }

    /// <summary>Speaks a drawn change that waited for this actor's popup.</summary>
    public void ReleaseDeferred(int actorIndex)
    {
        lock (sync)
        {
            if (deferredMasks.Remove(actorIndex, out var mask) &&
                states.TryGetValue(actorIndex, out var previous))
            {
                ApplyDisplayed(actorIndex, previous, mask);
            }
        }
    }

    /// <summary>
    /// Confirms a defeat on the popup of the hit that caused it: the engine marks
    /// only a target's last lethal result row, so earlier hits of a multi-hit
    /// action do not announce it early.
    /// </summary>
    public void ConfirmVisibleResult(
        BattleVisibleResultSnapshot result,
        BattleActorSnapshot target)
    {
        if (!result.IsLethal ||
            result.TargetActor != target.ActorIndex ||
            string.IsNullOrWhiteSpace(target.Name))
        {
            return;
        }

        lock (sync)
        {
            if (confirmedDeaths.Add(target.ActorIndex))
            {
                pending.Enqueue(Format(target.Name, target.IsEnemy, 0, gained: true));
            }
        }
    }

    public void ConfirmDamage(BattleDamagePopupSnapshot popup, BattleActorSnapshot actor)
    {
        if (!popup.IsValid ||
            popup.TargetActor != actor.ActorIndex ||
            popup.IsMiss ||
            (popup.Flags & BattleVisibleResultSnapshot.RecoveryFlag) != 0 ||
            (actor.StatusMask & DeathMask) == 0)
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
        if (!popup.IsValid ||
            popup.TargetActor != actor.ActorIndex ||
            popup.IsMiss ||
            (popup.Flags & BattleVisibleResultSnapshot.RecoveryFlag) != 0 ||
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
            deferredMasks.Clear();
            confirmedDeaths.Clear();
            pending.Clear();
        }
    }

    private void ApplyDisplayed(int actorIndex, ActorStatusState previous, uint mask)
    {
        if (previous.Mask == mask)
        {
            return;
        }

        states[actorIndex] = previous with { Mask = mask };
        var changed = previous.Mask ^ mask;
        for (var bit = 0; bit < 32; bit++)
        {
            var flag = 1u << bit;
            if ((changed & flag) == 0)
            {
                continue;
            }

            var gained = (mask & flag) != 0;
            if (bit == 0)
            {
                // A drawn defeat is heard once, whether the lethal popup or the
                // drawn mask shows it first; only a heard defeat can be revived.
                if (gained ? confirmedDeaths.Add(actorIndex) : confirmedDeaths.Remove(actorIndex))
                {
                    pending.Enqueue(Format(previous.Name, previous.IsEnemy, 0, gained));
                }

                continue;
            }

            pending.Enqueue(Format(previous.Name, previous.IsEnemy, bit, gained));
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

    private static string StatusName(int bit) => BattleStatusCatalog.Name(bit);

    private readonly record struct ActorStatusState(string Name, bool IsEnemy, uint Mask);
}

namespace Ff7.Accessibility.Reloaded;

public enum BattleStatusHotkeyCommand
{
    SelectParty1,
    SelectParty2,
    SelectParty3,
    Hp,
    Mp,
    Debuffs,
    Buffs,
    Limit
}

public readonly record struct BattleStatusMemberSnapshot(
    BattleActorSnapshot Actor,
    byte LimitGauge);

public sealed class BattleStatusHotkeyController
{
    private static readonly (int VirtualKey, BattleStatusHotkeyCommand Command)[] Bindings =
    [
        ('1', BattleStatusHotkeyCommand.SelectParty1),
        ('2', BattleStatusHotkeyCommand.SelectParty2),
        ('3', BattleStatusHotkeyCommand.SelectParty3),
        ('H', BattleStatusHotkeyCommand.Hp),
        ('M', BattleStatusHotkeyCommand.Mp),
        ('D', BattleStatusHotkeyCommand.Debuffs),
        ('S', BattleStatusHotkeyCommand.Buffs),
        ('L', BattleStatusHotkeyCommand.Limit)
    ];

    public int SelectedPartySlot { get; private set; }

    public static IReadOnlyList<int> VirtualKeys { get; } =
        Bindings.Select(binding => binding.VirtualKey).ToArray();

    public static bool TryMapVirtualKey(
        int virtualKey,
        out BattleStatusHotkeyCommand command)
    {
        foreach (var binding in Bindings)
        {
            if (binding.VirtualKey == virtualKey)
            {
                command = binding.Command;
                return true;
            }
        }

        command = default;
        return false;
    }

    public string? HandleVirtualKey(
        int virtualKey,
        Func<int, BattleStatusMemberSnapshot?> readMember)
    {
        ArgumentNullException.ThrowIfNull(readMember);
        return TryMapVirtualKey(virtualKey, out var command)
            ? Handle(command, readMember)
            : null;
    }

    public string? Poll(
        bool battleActive,
        Func<int, bool> observeRisingEdge,
        Func<int, BattleStatusMemberSnapshot?> readMember,
        bool observeLimitKey = true,
        bool resetSelectionWhenInactive = true)
    {
        ArgumentNullException.ThrowIfNull(observeRisingEdge);
        ArgumentNullException.ThrowIfNull(readMember);
        string? speech = null;
        foreach (var binding in Bindings)
        {
            if (!observeLimitKey && binding.Command == BattleStatusHotkeyCommand.Limit)
            {
                continue;
            }

            var pressed = observeRisingEdge(binding.VirtualKey);
            if (battleActive && pressed && speech is null)
            {
                speech = Handle(binding.Command, readMember);
            }
        }

        if (!battleActive && resetSelectionWhenInactive)
        {
            Reset();
        }

        return speech;
    }

    public string Handle(
        BattleStatusHotkeyCommand command,
        Func<int, BattleStatusMemberSnapshot?> readMember)
    {
        ArgumentNullException.ThrowIfNull(readMember);
        if (command is >= BattleStatusHotkeyCommand.SelectParty1
            and <= BattleStatusHotkeyCommand.SelectParty3)
        {
            var requestedSlot = (int)command - (int)BattleStatusHotkeyCommand.SelectParty1;
            var requestedMember = ReadMember(readMember, requestedSlot);
            if (requestedMember is null)
            {
                return Unavailable(requestedSlot);
            }

            SelectedPartySlot = requestedSlot;
            return $"{requestedMember.Value.Actor.Name} selected.";
        }

        var member = ReadMember(readMember, SelectedPartySlot);
        if (member is null)
        {
            return Unavailable(SelectedPartySlot);
        }

        var actor = member.Value.Actor;
        return command switch
        {
            BattleStatusHotkeyCommand.Hp =>
                $"{actor.Name} HP {actor.CurrentHp} of {actor.MaxHp}.",
            BattleStatusHotkeyCommand.Mp =>
                $"{actor.Name} MP {actor.CurrentMp} of {actor.MaxMp}.",
            BattleStatusHotkeyCommand.Debuffs =>
                FormatStatuses(actor.Name, actor.StatusMask, beneficial: false),
            BattleStatusHotkeyCommand.Buffs =>
                FormatStatuses(actor.Name, actor.StatusMask, beneficial: true),
            BattleStatusHotkeyCommand.Limit =>
                $"{actor.Name} limit {LimitGaugeToPercent(member.Value.LimitGauge)} percent.",
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
    }

    public void Reset() => SelectedPartySlot = 0;

    public static int LimitGaugeToPercent(byte value) =>
        value == byte.MaxValue ? 100 : (value * 100 + 127) / byte.MaxValue;

    private static BattleStatusMemberSnapshot? ReadMember(
        Func<int, BattleStatusMemberSnapshot?> readMember,
        int partySlot)
    {
        var member = readMember(partySlot);
        return member is { } value &&
               !value.Actor.IsEnemy &&
               !string.IsNullOrWhiteSpace(value.Actor.Name)
            ? value
            : null;
    }

    private static string FormatStatuses(string name, uint mask, bool beneficial)
    {
        var statuses = BattleStatusCatalog.ActiveNames(mask, beneficial);
        if (statuses.Count == 0)
        {
            return $"{name} has no {(beneficial ? "buffs" : "debuffs")}.";
        }

        return $"{name} {(beneficial ? "buffs" : "debuffs")}: {string.Join(", ", statuses)}.";
    }

    private static string Unavailable(int partySlot) =>
        $"Party member {partySlot + 1} unavailable.";
}

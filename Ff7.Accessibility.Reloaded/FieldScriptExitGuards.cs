using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Applies the catalog's live exit guards (<see cref="FieldScriptNavigationReadResult.ExitGuards"/>):
/// a script exit's destination is offered only while some way its MAPJUMP runs is open, read from
/// the savemap bytes the field's own tests read. fship_4's jump line MAPJUMPs to 744 only once
/// bank 13[91] bit 7 is set, and until then crossing it does nothing; offering that exit sent the
/// player to a line that goes nowhere.
///
/// <para>The savemap blocks the tests address are contiguous from 0x00DC08DC (0060FA7D: blocks 1,
/// 3, 11, 13 and 15 at 0x00DC08DC, 0x00DC09DC, 0x00DC0ADC, 0x00DC0BDC and 0x00DC0CDC), so a word
/// at a block's last byte runs on into the next block as the native reader's does. A byte that
/// cannot be read decides nothing: the destination stays offered, as it was before guards
/// existed, rather than hidden on a failed read.</para>
/// </summary>
public static class FieldScriptExitGuards
{
    public const int AddressSavemapFieldBanks = 0x00DC08DC;

    public static IReadOnlyList<FieldNavigationTarget> Apply(
        IReadOnlyList<FieldNavigationTarget> exits,
        IReadOnlyDictionary<string, IReadOnlyList<FieldScriptExitGuard>> guards,
        ILegacyAddressSpace memory)
    {
        ArgumentNullException.ThrowIfNull(exits);
        ArgumentNullException.ThrowIfNull(guards);
        ArgumentNullException.ThrowIfNull(memory);
        if (guards.Count == 0 || exits.Count == 0)
        {
            return exits;
        }

        var result = new List<FieldNavigationTarget>(exits.Count);
        foreach (var exit in exits)
        {
            if (!guards.TryGetValue(exit.StableId, out var exitGuards) || exit.DestinationFieldIds is not { Count: > 0 } destinations)
            {
                result.Add(exit);
                continue;
            }

            var open = destinations
                .Where(destination => exitGuards.FirstOrDefault(guard => guard.DestinationFieldId == destination) is not { } guard ||
                                      guard.Alternatives.Any(alternative => alternative.All(test => IsMet(test, memory))))
                .ToArray();
            if (open.Length == destinations.Count)
            {
                result.Add(exit);
            }
            else if (open.Length > 0)
            {
                result.Add(exit with { DestinationFieldIds = open });
            }
        }

        return result;
    }

    /// <summary>Whether the live byte or word meets the test; an unreadable one is not counted against it.</summary>
    public static bool IsMet(FieldScriptGuardTest test, ILegacyAddressSpace memory)
    {
        if (BlockBase(test.Block) is not { } blockBase)
        {
            return true;
        }

        var address = (uint)(blockBase + test.Address);
        if (!memory.TryReadByte(address, out var low))
        {
            return true;
        }

        var value = (int)low;
        if (test.Word)
        {
            if (!memory.TryReadByte(address + 1, out var high))
            {
                return true;
            }

            value |= high << 8;
        }

        return test.IsMetBy(value);
    }

    private static int? BlockBase(int block) => block switch
    {
        1 => AddressSavemapFieldBanks,
        3 => AddressSavemapFieldBanks + 0x100,
        11 => AddressSavemapFieldBanks + 0x200,
        13 => AddressSavemapFieldBanks + 0x300,
        15 => AddressSavemapFieldBanks + 0x400,
        _ => null
    };
}

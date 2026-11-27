namespace LocalizedDescriptionCueAligner;

/// <summary>The outcome of comparing one target field with the English field for navigation.</summary>
internal sealed record NavigationEquivalenceResult(string Status, bool Equivalent, string Evidence = "");

/// <summary>
/// Decides whether a target archive's field may use the English navigation review. Navigation
/// reads the loaded script itself; what the review adds (NPC labels, object and Story rows, exit
/// guards) is keyed by entity and slot. The official localized archives the navigation manifest
/// accepts have the English entity layout, but some of their slots differ by a few instructions
/// (an extra WINDOW, a different IFUB, a WAIT, even a REQ or MAPJUMP).
/// The same standard is applied here: the entity names and slot aliasing must be identical, and
/// every slot must stay mostly the same code (similarity at least <see cref="CueAligner.MinimumSimilarity"/>).
/// "presentation-only" means the only differences are text ids, window geometry, branch distances
/// and mapped entities; "minor-edits" means some slots also insert, remove or change instructions.
/// </summary>
internal static class NavigationEquivalence
{
    private const int SlotCount = 32;

    // Opcodes whose difference can change what navigation finds or decides: MAPJUMP, the LINE
    // family (LINE, LINON, MPJPO, SLINE), model binding (PC, CHAR) and every savemap or temporary
    // bank writer (PLUS! .. SEARCHX except TLKON and RDMSD).
    private static readonly HashSet<byte> NavigationRelevant =
        [0x60, 0xD0, 0xD1, 0xD2, 0xD3, 0xA0, 0xA1,
         .. Enumerable.Range(0x76, 0x9F - 0x76 + 1).Where(op => op is not (0x7E or 0x7F)).Select(op => (byte)op)];

    // A requested slot (3 and up, not Init/Main, Talk or Contact) may differ in a cutscene-only way:
    // it is accepted when neither side touches anything navigation reads.
    private static bool IsNavigationNeutralRequestedSlot(int slot, IReadOnlyList<NativeInstruction>? a, IReadOnlyList<NativeInstruction>? b) =>
        slot >= 3 && (a ?? []).Concat(b ?? []).All(i => !NavigationRelevant.Contains(i.Opcode));

    public static NavigationEquivalenceResult Compare(NativeField english, NativeField target)
    {
        if (english.PrefixSha256 == target.PrefixSha256)
            return new("identical-prefix", true);
        if (!english.EntityNames.SequenceEqual(target.EntityNames, StringComparer.Ordinal))
            return new("entity-layout-differs", false,
                string.Join(',', english.EntityNames) + " | " + string.Join(',', target.EntityNames));
        var map = Enumerable.Range(0, english.EntityCount).ToDictionary(i => i, i => i);

        var edited = new List<string>();
        for (var entity = 0; entity < english.EntityCount; entity++)
        {
            for (var slot = 0; slot < SlotCount; slot++)
            {
                var a = english.Slot(entity, slot);
                var b = target.Slot(entity, slot);
                if (a is null && b is null) continue;
                var neutral = IsNavigationNeutralRequestedSlot(slot, a, b);
                if (a is null || b is null)
                {
                    if (neutral) { edited.Add($"{entity}/{slot}: requested slot only on one side"); continue; }
                    return new("slot-missing", false, $"entity {entity} slot {slot}");
                }

                if (english.SlotAliases(entity, slot) != target.SlotAliases(entity, slot))
                {
                    if (neutral) { edited.Add($"{entity}/{slot}: requested slot aliases differ"); continue; }
                    return new("slot-aliases-differ", false, $"entity {entity} slot {slot}");
                }

                var comparison = new SlotComparison(a, b, map);
                if (comparison.Identical) continue;
                if (comparison.Similarity < CueAligner.MinimumSimilarity)
                {
                    if (neutral) { edited.Add($"{entity}/{slot}: requested cutscene slot rewritten"); continue; }
                    return new("dissimilar-slot", false,
                        $"entity {entity} slot {slot}: lcs={comparison.Alignment.Total}/{a.Count}/{b.Count}");
                }
                var structural = comparison.StructuralEdits.ToArray();
                if (structural.Length > 0 || comparison.BranchInconsistencies.Count > 0)
                    edited.Add($"{entity}/{slot}: " + string.Join(';', structural.Take(2).Select(e => e.ToString())));
            }
        }

        return edited.Count == 0
            ? new("presentation-only", true)
            : new("minor-edits", true, $"{edited.Count} slots: " + string.Join(" | ", edited.Take(3)));
    }
}

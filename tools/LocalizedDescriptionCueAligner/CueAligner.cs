using Ff7.Accessibility.Reloaded;

namespace LocalizedDescriptionCueAligner;

/// <summary>An English native anchor and where one target archive runs the same instruction.</summary>
internal sealed record AnchorResult(
    string Status,
    bool Authorized,
    int? Entity = null,
    int? Script = null,
    int? ByteIndex = null,
    Equivalence Equivalence = Equivalence.None,
    string Evidence = "");

/// <summary>
/// Decides, for one English anchor (field, entity, slot, byte, opcode) and one target archive,
/// whether the target runs exactly the same native instruction, and where. Nothing is inferred
/// from offsets: the answer is a forced alignment of the whole slot, or no answer.
/// </summary>
internal sealed class CueAligner
{
    // A slot whose instructions mostly differ is not "the same script with a few edits".
    public const double MinimumSimilarity = 0.5;
    private readonly Dictionary<(int Entity, int Slot), SlotComparison> comparisons = new();

    public CueAligner(NativeField english, NativeField target)
    {
        English = english;
        Target = target;
        EntityMap = EntityAlignment.Align(english.EntityNames, target.EntityNames);
    }

    public NativeField English { get; }
    public NativeField Target { get; }
    public IReadOnlyDictionary<int, int> EntityMap { get; }
    public bool SamePrefix => English.PrefixSha256 == Target.PrefixSha256;

    public SlotComparison? Compare(int entity, int slot)
    {
        if (comparisons.TryGetValue((entity, slot), out var cached)) return cached;
        if (!EntityMap.TryGetValue(entity, out var mapped) || English.Slot(entity, slot) is not { } a ||
            Target.Slot(mapped, slot) is not { } b) return null;
        return comparisons[(entity, slot)] = new SlotComparison(a, b, EntityMap);
    }

    public AnchorResult Align(int entity, int slot, int byteIndex, int opcode)
    {
        if (SamePrefix) return new("identical-prefix", false, entity, slot, byteIndex, Equivalence.Exact);
        if (!EntityMap.TryGetValue(entity, out var mapped))
            return new("entity-unaligned", false, Evidence: string.Join(',', English.EntityNames) + " | " + string.Join(',', Target.EntityNames));
        if (English.Slot(entity, slot) is null) return new("english-slot-missing", false);
        if (Target.Slot(mapped, slot) is null) return new("target-slot-missing", false, mapped);
        var aliases = English.SlotAliases(entity, slot);
        var targetAliases = Target.SlotAliases(mapped, slot);
        if (aliases != targetAliases)
            return new("slot-aliases-differ", false, mapped, slot, Evidence: $"{aliases} vs {targetAliases}");
        var comparison = Compare(entity, slot)!;
        var index = comparison.IndexOf(byteIndex, opcode);
        if (index < 0) return new("english-anchor-not-an-instruction", false);
        if (comparison.Identical)
            return new("identical-slot", true, mapped, slot, byteIndex, Equivalence.Exact,
                mapped == entity ? "" : $"entity {entity}->{mapped}");
        if (comparison.Forced[index] is not { } j)
            return new("not-forced", false, mapped, slot, Evidence: $"lcs={comparison.Alignment.Total}/{comparison.English.Count}/{comparison.Target.Count}");
        var equivalence = comparison.Alignment.At(index, j);
        var target = comparison.Target[j];
        var evidence = $"lcs={comparison.Alignment.Total}/{comparison.English.Count}/{comparison.Target.Count}";
        // The instruction itself must be the same action: every byte, an entity operand that names
        // the paired entity, or for MPNAM only its room-name text id (the room is the field's).
        var acceptable = equivalence is Equivalence.Exact or Equivalence.MappedEntity ||
                         equivalence == Equivalence.Text && opcode == 0x43;
        if (!acceptable) return new("instruction-differs", false, mapped, slot, target.ByteIndex, equivalence, evidence);
        if (comparison.BranchInconsistencies.Count > 0)
            return new("branch-inconsistent", false, mapped, slot, target.ByteIndex, equivalence,
                evidence + " " + string.Join(';', comparison.BranchInconsistencies));
        if (comparison.Similarity < MinimumSimilarity)
            return new("dissimilar-slot", false, mapped, slot, target.ByteIndex, equivalence, evidence);
        if (opcode == 0xF9 && FilmNumberEvidence(comparison, index) is { } film && film.Length > 0)
            return new("film-number-differs", false, mapped, slot, target.ByteIndex, equivalence, evidence + " " + film);
        var structural = comparison.StructuralEdits.ToArray();
        return new(structural.Length == 0 ? "aligned" : "aligned-edited-slot", true, mapped, slot, target.ByteIndex, equivalence,
            structural.Length == 0 ? evidence : evidence + " edits " + string.Join(';', structural.Select(e => e.ToString())));
    }

    /// <summary>
    /// A MOVIE starts the film the nearest preceding PMVIE of the same slot named. That PMVIE must
    /// be forced to an identical PMVIE; returns the failure, empty when it holds or none precedes.
    /// </summary>
    private static string? FilmNumberEvidence(SlotComparison comparison, int movieIndex)
    {
        for (var k = movieIndex - 1; k >= 0; k--)
        {
            if (comparison.English[k].Opcode != 0xF8) continue;
            if (comparison.Forced[k] is { } j && comparison.Alignment.At(k, j) == Equivalence.Exact) return "";
            return $"PMVIE {comparison.English[k].ByteIndex}:{comparison.English[k].Hex} not forced exact";
        }

        return "";
    }

    /// <summary>Whether a REQ-family cue's requested slot runs equivalent code in the target.</summary>
    public string RequestTargetEvidence(int cueEntity, int slot, int byteIndex, int opcode)
    {
        if (opcode is not (0x01 or 0x02 or 0x03)) return "";
        var instruction = English.Slot(cueEntity, slot)?.FirstOrDefault(i => i.ByteIndex == byteIndex && i.Opcode == opcode);
        if (instruction is null) return "";
        var requestedEntity = instruction.Bytes[1];
        var requestedSlot = instruction.Bytes[2] & 0x1F;
        if (Compare(requestedEntity, requestedSlot) is not { } requested) return $"requested {requestedEntity}/{requestedSlot} unaligned";
        if (requested.Identical) return "";
        var structural = requested.StructuralEdits.ToArray();
        return structural.Length == 0
            ? $"requested {requestedEntity}/{requestedSlot} presentation-only"
            : $"requested {requestedEntity}/{requestedSlot} edits " + string.Join(';', structural.Select(e => e.ToString()));
    }
}

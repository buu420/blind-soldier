namespace LocalizedDescriptionCueAligner;

/// <summary>English entity index to target entity index, from the native 8-byte names in order.</summary>
internal static class EntityAlignment
{
    /// <summary>Only an entity that every maximum name alignment pairs the same way is mapped.</summary>
    public static IReadOnlyDictionary<int, int> Align(IReadOnlyList<string> english, IReadOnlyList<string> target)
    {
        var map = new Dictionary<int, int>();
        if (english.SequenceEqual(target, StringComparer.Ordinal))
        {
            for (var index = 0; index < english.Count; index++) map[index] = index;
            return map;
        }

        var alignment = new ForcedAlignment(english.Count, target.Count,
            (i, j) => string.Equals(english[i], target[j], StringComparison.Ordinal) ? Equivalence.Exact : Equivalence.None);
        for (var index = 0; index < english.Count; index++)
            if (alignment.Forced(index) is { } paired) map[index] = paired;
        return map;
    }
}

/// <summary>One English slot against the target slot of the paired entity.</summary>
internal sealed class SlotComparison
{
    public SlotComparison(IReadOnlyList<NativeInstruction> english, IReadOnlyList<NativeInstruction> target,
        IReadOnlyDictionary<int, int> entityMap)
    {
        English = english;
        Target = target;
        Identical = english.Count == target.Count && english.Zip(target).All(p =>
            p.First.ByteIndex == p.Second.ByteIndex && Instructions.Compare(p.First, p.Second, entityMap) == Equivalence.Exact);
        Alignment = new ForcedAlignment(english, target, entityMap);
        Forced = Enumerable.Range(0, english.Count).Select(Alignment.Forced).ToArray();
        Images = new HashSet<int>(Forced.OfType<int>());
        Similarity = Math.Max(english.Count, target.Count) == 0 ? 1 : Alignment.Total / (double)Math.Max(english.Count, target.Count);
        Edits = Alignment.CanonicalPairs()
            .Where(p => p.English is null || p.Localized is null || Alignment.At(p.English.Value, p.Localized.Value) != Equivalence.Exact)
            .Select(p => new SlotEdit(
                p.English is { } i ? english[i] : null,
                p.Localized is { } j ? target[j] : null,
                p.English is { } a && p.Localized is { } b ? Alignment.At(a, b) : Equivalence.None))
            .ToArray();
        BranchInconsistencies = FindBranchInconsistencies();
    }

    public IReadOnlyList<NativeInstruction> English { get; }
    public IReadOnlyList<NativeInstruction> Target { get; }
    public bool Identical { get; }
    public ForcedAlignment Alignment { get; }
    public int?[] Forced { get; }
    private HashSet<int> Images { get; }
    public double Similarity { get; }
    public IReadOnlyList<SlotEdit> Edits { get; }
    public IReadOnlyList<string> BranchInconsistencies { get; }

    /// <summary>Edits other than text-table ids, window geometry, branch distances and mapped entities.</summary>
    public IEnumerable<SlotEdit> StructuralEdits => Edits.Where(e => e.Equivalence == Equivalence.None);

    public int IndexOf(int byteIndex, int opcode) =>
        English.ToList().FindIndex(i => i.ByteIndex == byteIndex && i.Opcode == opcode);

    /// <summary>
    /// Every forced branch pair must land in the same place: both targets must reach the same next
    /// common (forced) instruction with only listed insertions or removals in between. A jump to
    /// the slot's end must stay at its end.
    /// </summary>
    private IReadOnlyList<string> FindBranchInconsistencies()
    {
        var failures = new List<string>();
        var englishEnd = English.Count == 0 ? 0 : English[^1].ByteIndex + English[^1].Bytes.Length;
        var targetEnd = Target.Count == 0 ? 0 : Target[^1].ByteIndex + Target[^1].Bytes.Length;
        for (var i = 0; i < English.Count; i++)
        {
            if (Forced[i] is not { } j || !Instructions.IsBranch(English[i].Opcode)) continue;
            var englishTarget = Instructions.BranchTarget(English[i])!.Value;
            var localizedTarget = Instructions.BranchTarget(Target[j])!.Value;
            var ti = English.ToList().FindIndex(x => x.ByteIndex == englishTarget);
            var tj = Target.ToList().FindIndex(x => x.ByteIndex == localizedTarget);
            // Both land before the same next common instruction; anything between is a listed edit.
            var consistent = ti < 0 || tj < 0
                ? ti < 0 && tj < 0 && englishTarget == englishEnd && localizedTarget == targetEnd
                : NextImage(ti) == NextLocal(tj);
            if (!consistent)
                failures.Add($"{English[i].ByteIndex}:{English[i].Hex}->{englishTarget} vs {Target[j].ByteIndex}:{Target[j].Hex}->{localizedTarget}");
        }

        return failures;
    }

    private int NextImage(int englishIndex)
    {
        for (var k = englishIndex; k < English.Count; k++)
            if (Forced[k] is { } image) return image;
        return Target.Count;
    }

    private int NextLocal(int targetIndex)
    {
        for (var k = targetIndex; k < Target.Count; k++)
            if (Images.Contains(k)) return k;
        return Target.Count;
    }
}

internal sealed record SlotEdit(NativeInstruction? English, NativeInstruction? Target, Equivalence Equivalence)
{
    public override string ToString() =>
        $"{(English is null ? "-" : $"{English.ByteIndex}:{English.Hex}")}=>{(Target is null ? "-" : $"{Target.ByteIndex}:{Target.Hex}")}" +
        (English is not null && Target is not null ? $"[{Equivalence}]" : "");
}

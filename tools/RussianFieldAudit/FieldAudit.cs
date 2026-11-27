using LocalizedDescriptionCueAligner;

namespace RussianFieldAudit;

/// <summary>
/// One reviewed navigation field: the English disk archive against the Russian overlay, compared
/// section by section, entity by entity and slot by slot with the localized cue aligner's
/// instruction equivalence rules. Nothing here decides by hash alone.
/// </summary>
internal sealed class FieldAudit
{
    // Instructions whose operands decide where the player can go, who stands where, what model an
    // entity shows and which character it is (names from tools/WholeGameStateAudit/Opcodes.cs):
    // MAPJUMP, IDLCK, LINE/LINON/MPJPO/SLINE, PMJMP/PMJMP2, JUMP/AXYZI/LADER, SOLID, TALKR/SLIDR,
    // TLKR2/SLDR2, TLKON, PC/CHAR, VISI, XYZI/XYI/XYZ, MOVE/CMOVE/MOVA/FMOVE, PXYZI, CC, UC, MENU,
    // MENU2, BATTLE/BTLON, STITM/SMTRA (chest and materia pickups).
    private static readonly HashSet<byte> NavigationOpcodes =
    [
        0x60, 0x6D, 0xD0, 0xD1, 0xD2, 0xD3, 0xD8, 0xD9, 0xC0, 0xC1, 0xC2, 0xC7, 0xC5, 0xC6, 0xD6, 0xD7, 0x7E,
        0xA0, 0xA1, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xAD, 0x75, 0xBF, 0x33, 0x49, 0x4A, 0x70, 0x71,
        0x58, 0x5B,
    ];

    // Edits that change only presentation, timing or input locks, never where the party goes, what a
    // line or gateway does, or what an entry script writes and tests: TUTOR, WAIT, NFADE, KAWAI, BGSCR,
    // WCLS, WSIZW, UC, MESSAGE, MPNAM, MENU2, WINDOW, WCLSE, FADE, FADEW, MUSIC, AKAO, DFANM, MSPED,
    // TURNGEN, ANIMX2. Any other inserted, removed or changed instruction makes the English disk an
    // untrusted description of the Russian field for cross-field route planning.
    private static readonly HashSet<byte> RouteInertOpcodes =
    [
        0x21, 0x24, 0x25, 0x28, 0x2D, 0x2E, 0x2F, 0x33, 0x40, 0x43, 0x4A, 0x50, 0x54, 0x6B, 0x6C, 0xF0, 0xF2,
        0xA2, 0xB2, 0xB4, 0xBA,
    ];

    private static readonly string[] RouteMembers = ["Exits", "Transitions", "ExitGuards", "NativeLines"];

    public required int FieldId { get; init; }
    public required string Name { get; init; }
    public required string EnglishPrefix { get; init; }
    public required string RussianPrefix { get; init; }
    public required string EnglishModelSection { get; init; }
    public required string RussianModelSection { get; init; }
    public bool SamePrefix => EnglishPrefix == RussianPrefix;
    public List<string> DifferentSections { get; } = [];
    public bool HeaderSame { get; set; }
    public int EnglishTextOffset { get; set; }
    public int RussianTextOffset { get; set; }
    public int EnglishAkaoCount { get; set; }
    public int RussianAkaoCount { get; set; }
    public bool EntityNamesSame { get; set; }
    public string[] EnglishEntities { get; set; } = [];
    public string[] RussianEntities { get; set; } = [];
    public SortedDictionary<int, int> EntityMap { get; } = new();
    public List<int> UnmappedEnglishEntities { get; } = [];
    public List<int> UnmappedRussianEntities { get; } = [];
    public bool ModelNamesSame { get; set; }
    public bool ModelSectionSame => EnglishModelSection == RussianModelSection;
    public List<string> ModelDifferences { get; } = [];
    public int IdenticalSlots { get; set; }
    public SortedDictionary<string, int> EquivalentEdits { get; } = new(StringComparer.Ordinal);
    public List<string> StructuralEdits { get; } = [];
    public SortedSet<byte> StructuralOpcodes { get; } = [];
    public bool StructuralPresenceChange { get; set; }
    public List<string> BranchInconsistencies { get; } = [];
    public List<string> AliasDifferences { get; } = [];
    public List<string> NavigationOpcodeDifferences { get; } = [];
    public string? EnglishNavigation { get; set; }
    public string? RussianNavigation { get; set; }
    public List<string> NavigationDifferences { get; } = [];
    public List<string> TextIdDifferences { get; } = [];
    public bool WaitPositionsMoved { get; set; }
    public string EnglishNavigationDiagnostic { get; set; } = "";
    public string RussianNavigationDiagnostic { get; set; } = "";
    public bool EnglishNavigationUsable { get; set; }
    public bool RussianNavigationUsable { get; set; }

    /// <summary>Sections other than script, tile map and background (translated art) that differ.</summary>
    public IEnumerable<string> NonScriptNonArtSectionDifferences =>
        DifferentSections.Where(s => s is not "script" and not "tilemap" and not "background");

    /// <summary>
    /// Every check passes with no difference beyond text ids, window geometry, consistent branch
    /// distances, translated art and an unchanged model loader.
    /// </summary>
    public bool ExactSemanticEquivalent =>
        HeaderSame && EntityNamesSame && ModelSectionSame && ModelNamesSame && !NonScriptNonArtSectionDifferences.Any() &&
        StructuralEdits.Count == 0 && BranchInconsistencies.Count == 0 && AliasDifferences.Count == 0 &&
        NavigationOpcodeDifferences.Count == 0 && EnglishNavigationUsable && RussianNavigationUsable &&
        NavigationDifferences.Count == 0;

    /// <summary>
    /// Whether the installed English field still describes this Russian field for routes planned from
    /// another field (FieldCrossFieldApproachResolver reads them from disk): unchanged, window-only, or
    /// same entities, no route output difference and only presentation edits.
    /// </summary>
    public bool EnglishRouteMetadataEquivalent =>
        SamePrefix || ExactSemanticEquivalent ||
        HeaderSame && EntityNamesSame && ModelNamesSame && !NonScriptNonArtSectionDifferences.Any(s => s != "model-loader") &&
        !StructuralPresenceChange && StructuralOpcodes.All(RouteInertOpcodes.Contains) && BranchInconsistencies.Count == 0 &&
        AliasDifferences.Count == 0 && EnglishNavigationUsable && RussianNavigationUsable &&
        !NavigationDifferences.Any(d => RouteMembers.Any(m => d.StartsWith(m + ":", StringComparison.Ordinal)));

    public IEnumerable<string> ReviewReasons()
    {
        if (!HeaderSame) yield return "script-header";
        if (!EntityNamesSame) yield return "entity-names";
        if (!ModelNamesSame) yield return "model-names";
        else if (!ModelSectionSame) yield return "model-loader-bytes";
        foreach (var section in NonScriptNonArtSectionDifferences) yield return "section-" + section;
        if (StructuralEdits.Count > 0) yield return "structural-edits";
        if (BranchInconsistencies.Count > 0) yield return "branch-inconsistency";
        if (AliasDifferences.Count > 0) yield return "slot-aliases";
        if (NavigationOpcodeDifferences.Count > 0) yield return "navigation-opcodes";
        if (!EnglishNavigationUsable || !RussianNavigationUsable) yield return "navigation-unreadable";
        if (NavigationDifferences.Count > 0) yield return "navigation-output";
    }

    public void CompareScripts(NativeField english, NativeField russian)
    {
        EnglishEntities = english.EntityNames;
        RussianEntities = russian.EntityNames;
        EntityNamesSame = english.EntityNames.SequenceEqual(russian.EntityNames, StringComparer.Ordinal);
        foreach (var (key, value) in EntityAlignment.Align(english.EntityNames, russian.EntityNames)) EntityMap[key] = value;
        UnmappedEnglishEntities.AddRange(Enumerable.Range(0, english.EntityCount).Where(e => !EntityMap.ContainsKey(e)));
        var images = EntityMap.Values.ToHashSet();
        UnmappedRussianEntities.AddRange(Enumerable.Range(0, russian.EntityCount).Where(e => !images.Contains(e)));
        StructuralPresenceChange |= UnmappedEnglishEntities.Count > 0 || UnmappedRussianEntities.Count > 0;
        foreach (var e in UnmappedEnglishEntities) StructuralEdits.Add($"english entity {e} {english.EntityNames[e]} has no Russian counterpart");
        foreach (var e in UnmappedRussianEntities) StructuralEdits.Add($"russian entity {e} {russian.EntityNames[e]} has no English counterpart");

        foreach (var (entity, mapped) in EntityMap)
        {
            var englishOps = new List<string>();
            var russianOps = new List<string>();
            for (var slot = 0; slot < 32; slot++)
            {
                var a = english.Slot(entity, slot);
                var b = russian.Slot(mapped, slot);
                var aliasesA = english.SlotAliases(entity, slot);
                var aliasesB = russian.SlotAliases(mapped, slot);
                if (aliasesA != aliasesB)
                    AliasDifferences.Add($"e{entity}->{mapped} s{slot}: {aliasesA} vs {aliasesB}");
                // A slot sharing an earlier slot's pointer runs the same bytes; compare it once.
                var first = int.Parse(aliasesA.Split('|')[0]);
                if (first != slot) continue;
                if (a is null && b is null) continue;
                if (a is null || b is null)
                {
                    StructuralEdits.Add($"e{entity}->{mapped} s{slot}: slot present only in {(a is null ? "Russian" : "English")}");
                    StructuralPresenceChange = true;
                    continue;
                }

                foreach (var op in a.Where(i => NavigationOpcodes.Contains(i.Opcode))) englishOps.Add($"s{slot}:{Remap(op, EntityMap)}");
                foreach (var op in b.Where(i => NavigationOpcodes.Contains(i.Opcode))) russianOps.Add($"s{slot}:{op.Hex}");
                var comparison = new SlotComparison(a, b, EntityMap);
                if (comparison.Identical)
                {
                    IdenticalSlots++;
                    continue;
                }

                foreach (var edit in comparison.Edits)
                {
                    if (edit.English is not null && edit.Target is not null && edit.Equivalence != Equivalence.None)
                    {
                        var key = $"{edit.Equivalence}:{edit.English.Opcode:X2}";
                        EquivalentEdits[key] = EquivalentEdits.GetValueOrDefault(key) + 1;
                    }
                    else
                    {
                        StructuralEdits.Add($"e{entity}->{mapped} s{slot}: {edit}");
                        if (edit.English is not null) StructuralOpcodes.Add(edit.English.Opcode);
                        if (edit.Target is not null) StructuralOpcodes.Add(edit.Target.Opcode);
                    }
                }

                foreach (var failure in comparison.BranchInconsistencies) BranchInconsistencies.Add($"e{entity}->{mapped} s{slot}: {failure}");
            }

            if (!englishOps.SequenceEqual(russianOps, StringComparer.Ordinal))
                NavigationOpcodeDifferences.Add(
                    $"e{entity}->{mapped} {english.EntityNames[entity]}: english=[{string.Join(' ', englishOps.Except(russianOps))}] russian=[{string.Join(' ', russianOps.Except(englishOps))}]");
        }
    }

    /// <summary>The English instruction with its entity operand rewritten to the mapped Russian entity.</summary>
    private static string Remap(NativeInstruction instruction, IReadOnlyDictionary<int, int> map)
    {
        var operand = Instructions.EntityOperand(instruction.Opcode);
        if (operand < 0 || operand >= instruction.Bytes.Length || !map.TryGetValue(instruction.Bytes[operand], out var mapped))
            return instruction.Hex;
        var bytes = instruction.Bytes.ToArray();
        bytes[operand] = (byte)mapped;
        return Convert.ToHexString(bytes);
    }

    public void CompareModels(FieldSections english, FieldSections russian)
    {
        var a = english.ModelRecords();
        var b = russian.ModelRecords();
        if (a is null || b is null)
        {
            ModelNamesSame = false;
            ModelDifferences.Add("model loader unreadable");
            return;
        }

        ModelNamesSame = a.Select(m => m.Name).SequenceEqual(b.Select(m => m.Name), StringComparer.Ordinal);
        if (english.Sections[FieldSections.ModelLoader].AsSpan(0, 6).SequenceEqual(russian.Sections[FieldSections.ModelLoader].AsSpan(0, 6)) is false)
            ModelDifferences.Add("loader header (count/scale) differs");
        for (var index = 0; index < Math.Max(a.Count, b.Count); index++)
        {
            if (index >= a.Count || index >= b.Count)
            {
                ModelDifferences.Add($"model {index} present in only one archive");
                continue;
            }

            if (a[index].Name != b[index].Name) ModelDifferences.Add($"model {index} name {a[index].Name} vs {b[index].Name}");
            if (a[index].TailHex != b[index].TailHex)
                ModelDifferences.Add($"model {index} {a[index].Name} record tail {a[index].TailHex} vs {b[index].TailHex}");
            if (!a[index].Animations.SequenceEqual(b[index].Animations, StringComparer.Ordinal))
                ModelDifferences.Add($"model {index} {a[index].Name} animations [{string.Join(',', a[index].Animations)}] vs [{string.Join(',', b[index].Animations)}]");
        }
    }
}

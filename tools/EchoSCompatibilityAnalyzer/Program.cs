using System.Security.Cryptography;
using Ff7.Accessibility.Reloaded;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine(
        "Usage: EchoSCompatibilityAnalyzer <vanilla-game-root> <echo-s-extracted-root> [output.tsv]");
    return 2;
}

var vanillaRoot = Path.GetFullPath(args[0]);
var echoRoot = Path.GetFullPath(args[1]);
var vanillaScripts = new FieldScriptNavigationCatalog(vanillaRoot);
var echoScripts = new FieldScriptNavigationCatalog(echoRoot);
var vanillaData = new FlevelDataSource(vanillaRoot);
var echoData = new FlevelDataSource(echoRoot);
var echoText = new FlevelFieldTextResolver(echoRoot);
var cues = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions();
var vanillaFieldScripts = new Dictionary<int, IReadOnlyList<FieldScriptDefinition>>();
var echoFieldScripts = new Dictionary<int, IReadOnlyList<FieldScriptDefinition>>();
var output = new List<string>
{
    "field\tentity\tscript\tvanilla_byte\techo_entity\techo_script\techo_byte\topcode\techo_opcode\tstatus\tleft_anchors\tright_anchors\tfield_name\tvanilla_script_sha256\techo_script_sha256\ttext"
};

var mapped = 0;
var reviewed = 0;
var discarded = 0;
var ambiguous = 0;
var missing = 0;
foreach (var cue in cues)
{
    var vanilla = vanillaScripts.ReadScriptOpcodes(cue.FieldId, cue.EntityId, cue.ScriptId);
    var echo = echoScripts.ReadScriptOpcodes(cue.FieldId, cue.EntityId, cue.ScriptId);
    var result = Align(cue, vanilla, echo);
    if (result.Status != "mapped")
    {
        if (!vanillaFieldScripts.TryGetValue(cue.FieldId, out var allVanilla))
        {
            allVanilla = vanillaScripts.ReadAllScriptOpcodes(cue.FieldId);
            vanillaFieldScripts[cue.FieldId] = allVanilla;
        }

        if (!echoFieldScripts.TryGetValue(cue.FieldId, out var allEcho))
        {
            allEcho = echoScripts.ReadAllScriptOpcodes(cue.FieldId);
            echoFieldScripts[cue.FieldId] = allEcho;
        }

        var vanillaDefinition = allVanilla.FirstOrDefault(script =>
            script.EntityId == cue.EntityId && script.ScriptId == cue.ScriptId);
        if (vanillaDefinition.Opcodes is not null)
        {
            result = FindRelocated(cue, vanillaDefinition, allEcho, result);
        }
    }
    result = ApplyReviewedResolution(cue, result);
    switch (result.Status)
    {
        case "mapped": mapped++; break;
        case "reviewed": reviewed++; break;
        case "discarded": discarded++; break;
        case "ambiguous": ambiguous++; break;
        default: missing++; break;
    }

    vanillaData.FieldNames.TryGetValue(cue.FieldId, out var fieldName);
    var vanillaFingerprint = TryReadScriptFingerprint(vanillaData, cue.FieldId) ?? string.Empty;
    var echoFingerprint = TryReadScriptFingerprint(echoData, cue.FieldId) ?? string.Empty;
    output.Add(string.Join(
        '\t',
        cue.FieldId,
        cue.EntityId,
        cue.ScriptId,
        cue.ByteIndex,
        result.EchoEntityId?.ToString() ?? string.Empty,
        result.EchoScriptId?.ToString() ?? string.Empty,
        result.EchoByteIndex?.ToString() ?? string.Empty,
        $"0x{cue.Opcode:X2}",
        result.EchoOpcode is int echoOpcode ? $"0x{echoOpcode:X2}" : string.Empty,
        result.Status,
        result.LeftAnchors,
        result.RightAnchors,
        fieldName ?? string.Empty,
        vanillaFingerprint,
        echoFingerprint,
        cue.Text.Replace('\t', ' ')));
}

foreach (var line in output)
{
    Console.WriteLine(line);
}

Console.Error.WriteLine(
    $"Cues: {cues.Count}; mapped: {mapped}; reviewed: {reviewed}; discarded: {discarded}; " +
    $"ambiguous: {ambiguous}; missing: {missing}.");
Console.Error.WriteLine(
    $"Echo-S field 109 script fingerprint: {TryReadScriptFingerprint(echoData, 109) ?? "<unavailable>"}");
Console.Error.WriteLine("Echo-S field 109 disclaimer messages:");
for (var messageId = 1; messageId <= 4; messageId++)
{
    var message = echoText.ReadMessageById(109, messageId);
    Console.Error.WriteLine($"  {messageId}: {message.Text}");
}

if (args.Length == 3)
{
    File.WriteAllLines(Path.GetFullPath(args[2]), output);
}

return ambiguous == 0 && missing == 0 ? 0 : 1;

static AlignmentResult ApplyReviewedResolution(
    FieldCutsceneDescriptionCue cue,
    AlignmentResult candidate)
{
    // Echo-S replaces vanilla REQ + WAIT with a single REQEW at this exact
    // opening action. The semantic substitution was verified by dumping both
    // complete entity scripts, not by accepting a nearby-offset candidate.
    if (cue.Key == new FieldCutsceneDescriptionKey(116, 0, 0, 204))
    {
        return new AlignmentResult(0, 0, 246, 0x03, "reviewed", 8, 8);
    }

    // Echo-S changes the church-door request from REQ to REQSW at the same
    // entity/script/byte and retains the same target action.
    if (cue.Key == new FieldCutsceneDescriptionKey(182, 1, 1, 20))
    {
        return new AlignmentResult(1, 1, 20, 0x02, "reviewed", 8, 8);
    }

    // The catalog contains adjacent duplicates for this scene. Byte 444 is
    // not an opcode boundary even in vanilla; byte 446 is the valid cue and
    // is independently aligned to Echo-S byte 456.
    if (cue.Key == new FieldCutsceneDescriptionKey(322, 0, 0, 444))
    {
        return candidate with { Status = "discarded" };
    }

    return candidate;
}

static AlignmentResult Align(
    FieldCutsceneDescriptionCue cue,
    IReadOnlyList<FieldScriptOpcodeDefinition> vanilla,
    IReadOnlyList<FieldScriptOpcodeDefinition> echo)
{
    var targetIndex = IndexOf(vanilla, cue.ByteIndex, cue.Opcode);
    if (targetIndex < 0 || echo.Count == 0)
    {
        return AlignmentResult.Missing;
    }

    var mappings = BuildExactLcsMappings(vanilla, echo);
    if (mappings.TryGetValue(targetIndex, out var exactEchoIndex))
    {
        var anchors = CountAnchors(targetIndex, exactEchoIndex, vanilla, echo, mappings);
        return new AlignmentResult(
            echo[exactEchoIndex].EntityId,
            echo[exactEchoIndex].ScriptId,
            echo[exactEchoIndex].ByteIndex,
            echo[exactEchoIndex].Opcode,
            "mapped",
            anchors.Left,
            anchors.Right);
    }

    var candidates = new List<(int Index, int Score, int Left, int Right)>();
    for (var index = 0; index < echo.Count; index++)
    {
        if (echo[index].Opcode != cue.Opcode)
        {
            continue;
        }

        var context = ScoreContext(targetIndex, index, vanilla, echo);
        candidates.Add((index, context.Left + context.Right, context.Left, context.Right));
    }

    if (candidates.Count == 0)
    {
        return AlignmentResult.Missing;
    }

    var ordered = candidates
        .OrderByDescending(candidate => candidate.Score)
        .ThenBy(candidate => Math.Abs(echo[candidate.Index].ByteIndex - cue.ByteIndex))
        .ToArray();
    var best = ordered[0];
    var isUnique = best.Score >= 3 && (ordered.Length == 1 || best.Score > ordered[1].Score);
    return new AlignmentResult(
        echo[best.Index].EntityId,
        echo[best.Index].ScriptId,
        echo[best.Index].ByteIndex,
        echo[best.Index].Opcode,
        isUnique ? "mapped" : "ambiguous",
        best.Left,
        best.Right);
}

static AlignmentResult FindRelocated(
    FieldCutsceneDescriptionCue cue,
    FieldScriptDefinition vanilla,
    IReadOnlyList<FieldScriptDefinition> echoScripts,
    AlignmentResult existing)
{
    var candidates = new List<(AlignmentResult Result, int Score)>();
    foreach (var echo in echoScripts)
    {
        var result = Align(cue, vanilla.Opcodes, echo.Opcodes);
        if (result.EchoByteIndex is null)
        {
            continue;
        }

        var exactLcsLength = ExactLcsLength(vanilla.Opcodes, echo.Opcodes);
        var nameMatch = string.Equals(vanilla.EntityName, echo.EntityName, StringComparison.OrdinalIgnoreCase);
        var score =
            (nameMatch ? 100_000 : 0) +
            (cue.ScriptId == echo.ScriptId ? 10_000 : 0) +
            exactLcsLength * 100 +
            (result.LeftAnchors + result.RightAnchors) * 10 +
            (result.Status == "mapped" ? 1 : 0);
        candidates.Add((result, score));
    }

    if (candidates.Count == 0)
    {
        return existing;
    }

    var ordered = candidates.OrderByDescending(candidate => candidate.Score).ToArray();
    if (ordered.Length > 1 && ordered[0].Score == ordered[1].Score)
    {
        return ordered[0].Result with { Status = "ambiguous" };
    }

    return ordered[0].Result;
}

static int ExactLcsLength(
    IReadOnlyList<FieldScriptOpcodeDefinition> vanilla,
    IReadOnlyList<FieldScriptOpcodeDefinition> echo)
{
    var previous = new ushort[echo.Count + 1];
    var current = new ushort[echo.Count + 1];
    for (var left = 0; left < vanilla.Count; left++)
    {
        Array.Clear(current);
        for (var right = 0; right < echo.Count; right++)
        {
            current[right + 1] = Exact(vanilla[left], echo[right])
                ? (ushort)(previous[right] + 1)
                : Math.Max(previous[right + 1], current[right]);
        }

        (previous, current) = (current, previous);
    }

    return previous[^1];
}

static Dictionary<int, int> BuildExactLcsMappings(
    IReadOnlyList<FieldScriptOpcodeDefinition> vanilla,
    IReadOnlyList<FieldScriptOpcodeDefinition> echo)
{
    var rows = vanilla.Count + 1;
    var columns = echo.Count + 1;
    var lengths = new ushort[rows, columns];
    for (var left = vanilla.Count - 1; left >= 0; left--)
    {
        for (var right = echo.Count - 1; right >= 0; right--)
        {
            lengths[left, right] = Exact(vanilla[left], echo[right])
                ? (ushort)(lengths[left + 1, right + 1] + 1)
                : Math.Max(lengths[left + 1, right], lengths[left, right + 1]);
        }
    }

    var result = new Dictionary<int, int>();
    var i = 0;
    var j = 0;
    while (i < vanilla.Count && j < echo.Count)
    {
        if (Exact(vanilla[i], echo[j]) && lengths[i, j] == lengths[i + 1, j + 1] + 1)
        {
            result[i++] = j++;
        }
        else if (lengths[i + 1, j] >= lengths[i, j + 1])
        {
            i++;
        }
        else
        {
            j++;
        }
    }

    return result;
}

static (int Left, int Right) CountAnchors(
    int vanillaIndex,
    int echoIndex,
    IReadOnlyList<FieldScriptOpcodeDefinition> vanilla,
    IReadOnlyList<FieldScriptOpcodeDefinition> echo,
    IReadOnlyDictionary<int, int> mappings)
{
    var left = 0;
    var right = 0;
    for (var distance = 1; distance <= 8; distance++)
    {
        if (mappings.TryGetValue(vanillaIndex - distance, out var mapped) && mapped < echoIndex)
        {
            left++;
        }

        if (mappings.TryGetValue(vanillaIndex + distance, out mapped) && mapped > echoIndex)
        {
            right++;
        }
    }

    return (left, right);
}

static (int Left, int Right) ScoreContext(
    int vanillaIndex,
    int echoIndex,
    IReadOnlyList<FieldScriptOpcodeDefinition> vanilla,
    IReadOnlyList<FieldScriptOpcodeDefinition> echo)
{
    var left = 0;
    var right = 0;
    for (var distance = 1; distance <= 8; distance++)
    {
        if (vanillaIndex - distance >= 0 && echoIndex - distance >= 0 &&
            Exact(vanilla[vanillaIndex - distance], echo[echoIndex - distance]))
        {
            left++;
        }

        if (vanillaIndex + distance < vanilla.Count && echoIndex + distance < echo.Count &&
            Exact(vanilla[vanillaIndex + distance], echo[echoIndex + distance]))
        {
            right++;
        }
    }

    return (left, right);
}

static bool Exact(FieldScriptOpcodeDefinition left, FieldScriptOpcodeDefinition right) =>
    left.Opcode == right.Opcode && left.Bytes.SequenceEqual(right.Bytes);

static int IndexOf(
    IReadOnlyList<FieldScriptOpcodeDefinition> opcodes,
    int byteIndex,
    int opcode)
{
    for (var index = 0; index < opcodes.Count; index++)
    {
        if (opcodes[index].ByteIndex == byteIndex && opcodes[index].Opcode == opcode)
        {
            return index;
        }
    }

    return -1;
}

static string? TryReadScriptFingerprint(FlevelDataSource source, int fieldId)
{
    if (!source.TryReadField(fieldId, out var encoded))
    {
        return null;
    }

    var field = Ff7LzsDecoder.DecodeFieldFile(encoded);
    if (field.Length < 14)
    {
        return null;
    }

    var sectionOffset = BitConverter.ToInt32(field, 6);
    if (sectionOffset < 0 || sectionOffset > field.Length - sizeof(int))
    {
        return null;
    }

    var sectionLength = BitConverter.ToInt32(field, sectionOffset);
    var dataOffset = sectionOffset + sizeof(int);
    if (sectionLength < 32 || dataOffset > field.Length - sectionLength)
    {
        return null;
    }

    // Runtime field memory begins at the section-one data and does not expose
    // the outer field section length. Hash the structurally bounded script
    // prefix through (but not including) the text table so the offline and
    // live identities are byte-for-byte comparable.
    var textOffset = BitConverter.ToUInt16(field, dataOffset + 4);
    if (textOffset < 32 || textOffset > sectionLength)
    {
        return null;
    }

    return Convert.ToHexString(SHA256.HashData(field.AsSpan(dataOffset, textOffset)));
}

readonly record struct AlignmentResult(
    int? EchoEntityId,
    int? EchoScriptId,
    int? EchoByteIndex,
    int? EchoOpcode,
    string Status,
    int LeftAnchors,
    int RightAnchors)
{
    public static AlignmentResult Missing => new(null, null, null, null, "missing", 0, 0);
}

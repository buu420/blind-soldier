using System.Security.Cryptography;
using System.Text.Json;
using Ff7.Accessibility.Reloaded;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("StoryCoverageAudit <licensed-game-root> <report.json> [checkpoints.json]");
    return 2;
}

var source = new FlevelDataSource(args[0]);
var native = new FieldScriptNavigationCatalog(args[0]);
var story = FieldStoryEventCatalog.CreateAllFields();
var objects = FieldNavigationObjectCatalog.CreateAllFields();
var failures = new List<string>();
var reviews = new List<object>();
var geometryReviews = new List<object>();
var fields = new List<object>();
var checkpoints = new List<object>();
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
var requested = args.Length == 3
    ? JsonSerializer.Deserialize<List<Checkpoint>>(File.ReadAllText(args[2]), jsonOptions)
        ?? throw new InvalidDataException("Empty checkpoint document")
    : [];
if (args.Length == 3 && requested.Count == 0)
    failures.Add("The requested checkpoint document contains no checkpoints.");
if (requested.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != requested.Count)
    failures.Add("Checkpoint identifiers must be unique.");

foreach (var (id, name) in source.FieldNames.OrderBy(p => p.Key))
{
    var definitions = story.Where(d => d.FieldId == id).ToArray();
    var objectRows = objects.Where(d => d.FieldId == id).ToArray();
    if (!source.TryReadField(id, out var encoded))
    {
        if (definitions.Length > 0 || objectRows.Length > 0 || requested.Any(c => c.FieldId == id))
            failures.Add($"Authored field {id} ({name}) cannot be read: {source.Diagnostic}");
        continue;
    }

    var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
    var scripts = native.ReadAllScriptOpcodes(id);
    var navigation = native.ReadField(id);
    var ops = scripts.SelectMany(s => s.Opcodes).ToArray();
    var entities = scripts.Select(s => s.EntityId).ToHashSet();
    var entityNames = scripts.GroupBy(s => s.EntityId).ToDictionary(g => g.Key, g => g.First().EntityName);
    var triangleCount = ReadTriangleCount(bytes);
    foreach (var row in definitions)
    {
        var prefix = $"Story {id} ({name}), {row.Label}";
        if (row.SourceFieldName != name) failures.Add($"{prefix}: field identity mismatch.");
        if (row.MinimumGameMoment >= 0 && row.MaximumGameMoment >= 0 &&
            row.MinimumGameMoment > row.MaximumGameMoment)
            failures.Add($"{prefix}: empty GameMoment interval.");
        if (row.TargetGameMoment >= 0 && row.MinimumGameMoment >= row.TargetGameMoment)
            failures.Add($"{prefix}: target moment is already complete at the minimum moment.");
        if (row.Kind == FieldStoryTargetKind.Model && !entities.Contains(row.EntityId))
            failures.Add($"{prefix}: model entity {row.EntityId} does not exist.");
        var referencedEntity = row.Kind == FieldStoryTargetKind.Model ? row.EntityId : row.RequiredEnabledLineEntityId;
        if (referencedEntity is { } referenced && !string.IsNullOrWhiteSpace(row.SourceEntityName))
        {
            var namedEntities = entityNames.Where(e => string.Equals(e.Value, row.SourceEntityName, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Key).ToArray();
            if (namedEntities.Length > 0 && !namedEntities.Contains(referenced))
                failures.Add($"{prefix}: named entity {row.SourceEntityName} resolves to native entities {string.Join(",", namedEntities)}, not {referenced}.");
        }
        if (row.RequiredEnabledLineEntityId is { } line &&
            !ops.Any(o => o.EntityId == line && o.Opcode is 0xD0 or 0xD3))
            failures.Add($"{prefix}: required LINE entity {line} has no native LINE/SLINE.");
        if (row.RequiredEnabledLineEntityId is { } nativeLine && row.TriggerLine is { } authoredLine)
        {
            var lines = ops.Where(o => o.EntityId == nativeLine && o.Opcode == 0xD0 && o.Bytes.Count == 13)
                .Select(o => ReadLine(o.Bytes.ToArray(), 1)).Distinct().ToArray();
            if (lines.Length > 0 && !lines.Contains(authoredLine))
            {
                failures.Add($"{prefix}: trigger does not match native LINE entity {nativeLine}.");
                geometryReviews.Add(new { fieldId = id, fieldName = name, row.Label,
                    entityId = nativeLine, authoredLine, nativeLines = lines,
                    reason = "Authored trigger differs from native LINE; review whether the difference is intentional." });
            }
        }
        foreach (var triangle in (row.RequiredPlayerTriangles ?? [])
                     .Concat(row.ExcludedPlayerTriangles ?? []).Concat(row.CompletionPlayerTriangles ?? []))
            if (triangle < 0 || triangle >= triangleCount)
                failures.Add($"{prefix}: triangle {triangle} is outside native walkmesh ({triangleCount}).");
        foreach (var condition in new[] { row.RequiredCondition, row.CompletedCondition }
                     .Concat(row.RequiredConditions ?? []))
            if (condition.PartyMemberId is null && condition.Mask != 0 &&
                (condition.Bank is not (1 or 3 or 5 or 11 or 13 or 15) || condition.Address is < 0 or > 255))
                failures.Add($"{prefix}: unsupported condition bank/address {condition.Bank}:{condition.Address}.");
    }

    foreach (var row in objectRows)
    {
        var prefix = $"Object {id} ({name}), entity {row.EntityId}";
        if (row.SourceFieldName is { Length: > 0 } && row.SourceFieldName != name)
            failures.Add($"{prefix}: field identity mismatch.");
        if (row.TargetKind != FieldNavigationObjectTargetKind.Location && !entities.Contains(row.EntityId))
            failures.Add($"{prefix}: native entity does not exist.");
        if (row.TargetKind != FieldNavigationObjectTargetKind.Location &&
            !string.IsNullOrWhiteSpace(row.SourceEntityName))
        {
            var namedEntities = entityNames.Where(e => string.Equals(e.Value, row.SourceEntityName, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Key).ToArray();
            if (namedEntities.Length > 0 && !namedEntities.Contains(row.EntityId))
                failures.Add($"{prefix}: named entity {row.SourceEntityName} resolves to native entities {string.Join(",", namedEntities)}.");
        }
        if (row.TargetKind == FieldNavigationObjectTargetKind.Line &&
            !ops.Any(o => o.EntityId == row.EntityId && o.Opcode is 0xD0 or 0xD3))
            failures.Add($"{prefix}: LINE object has no native LINE/SLINE.");
        if (row.MinimumGameMoment >= 0 && row.MaximumGameMoment >= 0 && row.MinimumGameMoment > row.MaximumGameMoment)
            failures.Add($"{prefix}: empty GameMoment interval.");
        foreach (var (bank, address, mask) in new[] {
                     (row.RequiredBank, row.RequiredAddress, row.RequiredMask),
                     (row.CollectedBank, row.CollectedAddress, row.CollectedMask) })
            if (mask != 0 && (bank is not (1 or 3 or 5 or 11 or 13 or 15) || address is < 0 or > 255))
                failures.Add($"{prefix}: unsupported condition bank/address {bank}:{address}.");
    }

    // An uncovered native reward or menu is a review candidate, not automatically
    // a missing visible object. It may be an automatic scene, a purchase, a reward
    // for an NPC already listed, or an interaction behind a native state gate.
    var knownEntities = objectRows.Select(d => d.EntityId)
        .Concat(navigation.Npcs.Select(n => n.EntityId))
        .Concat(definitions.Where(d => d.Kind == FieldStoryTargetKind.Model).Select(d => d.EntityId))
        .ToHashSet();
    foreach (var script in scripts.Where(s => s.ScriptId == 1))
    {
        var interaction = script.Opcodes.Where(o => o.Opcode is 0x58 or 0x5B or 0x49).ToArray();
        if (interaction.Length == 0 || knownEntities.Contains(script.EntityId)) continue;
        reviews.Add(new { fieldId = id, fieldName = name, script.EntityId, script.EntityName,
            reason = "Native manual-slot reward/menu not represented by an authored object, Story model or discovered NPC; inspect callers and visibility.",
            anchors = interaction.Select(o => new { o.ScriptId, o.ByteIndex, bytes = Convert.ToHexString(o.Bytes.ToArray()) }) });
    }
    fields.Add(new { fieldId = id, fieldName = name,
        nativeSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
        nativeReadable = navigation.IsUsable, triangleCount, storyDefinitions = definitions.Length,
        objectDefinitions = objectRows.Length, discoveredNpcs = navigation.Npcs.Count,
        nativeExits = navigation.Exits.Count, traversals = navigation.Transitions.Count,
        unboundedStoryRows = definitions.Count(d => d.MinimumGameMoment < 0 && d.MaximumGameMoment < 0),
        storyMomentBands = definitions.Select(d => new { d.MinimumGameMoment, d.MaximumGameMoment }).Distinct() });
}

foreach (var definition in story)
    if (!source.FieldNames.ContainsKey(definition.FieldId))
        failures.Add($"Story field {definition.FieldId} does not exist in this archive.");
foreach (var definition in objects)
    if (!source.FieldNames.ContainsKey(definition.FieldId))
        failures.Add($"Object field {definition.FieldId} does not exist in this archive.");

foreach (var checkpoint in requested)
{
    var memory = new AuditMemory(checkpoint);
    var reader = new FieldStoryTargetReader(memory.Int32, memory.Int16, memory.Byte,
        story, entity => checkpoint.EnabledLines.Contains(entity));
    var position = new FieldPositionSnapshot(FieldPositionReader.FieldModule, checkpoint.FieldId,
        0, checkpoint.X, checkpoint.Y, checkpoint.Z, checkpoint.Triangle, 0);
    var actual = reader.ReadTargets(position).ToArray();
    var errors = new List<string>();
    if (checkpoint.ExpectedLabels.Length == 0 && checkpoint.ForbiddenLabels.Length == 0 && !checkpoint.ExpectEmpty)
        errors.Add("Checkpoint must independently name its expected target(s), or explicitly expect an empty state.");
    if (checkpoint.ExpectedLine is not null && checkpoint.ExpectedLabels.Length != 1)
        errors.Add("A checkpoint with ExpectedLine must name exactly one expected target.");
    if (checkpoint.ExpectEmpty && actual.Length != 0) errors.Add("Expected no Story targets in this state.");
    foreach (var label in checkpoint.ExpectedLabels)
        if (!actual.Any(t => t.Label == label)) errors.Add($"Missing expected Story target: {label}");
        else if (checkpoint.ExpectedLine is { } expectedLine &&
                 !actual.Any(t => t.Label == label && t.TriggerLine == expectedLine))
            errors.Add($"Story trigger does not match the independently recorded native line: {label}");
    foreach (var label in checkpoint.ForbiddenLabels)
        if (actual.Any(t => t.Label == label)) errors.Add($"Unexpected Story target: {label}");
    if (!source.FieldNames.ContainsKey(checkpoint.FieldId)) errors.Add("Unknown native field.");
    if (string.IsNullOrWhiteSpace(checkpoint.Evidence)) errors.Add("Checkpoint lacks independent evidence.");
    foreach (var anchor in checkpoint.NativeAnchors)
        if (anchor.Hex.Length < 2 || anchor.Hex.Length % 2 != 0 ||
            !anchor.Hex.All(Uri.IsHexDigit) ||
            !native.ReadAllScriptOpcodes(checkpoint.FieldId).Any(s => s.EntityId == anchor.EntityId &&
                (anchor.ScriptId < 0 || s.ScriptId == anchor.ScriptId) && s.Opcodes.Any(o =>
                    Convert.ToHexString(o.Bytes.ToArray()).StartsWith(anchor.Hex, StringComparison.OrdinalIgnoreCase))))
            errors.Add($"Native evidence changed: entity {anchor.EntityId}, script {anchor.ScriptId}, opcode {anchor.Hex}.");
    var routeWitnesses = new List<object>();
    if (checkpoint.ExpectedDestinationFieldIds.Length > 0 || checkpoint.RouteArrivalFromFieldId is not null)
    {
        if (checkpoint.ExpectedLabels.Length != 1 || checkpoint.ExpectedLine is null)
            errors.Add("A native transition checkpoint must name one target and its independently recorded line.");
        else if (source.TryReadField(checkpoint.FieldId, out var encoded))
        {
            var bytes = Ff7LzsDecoder.DecodeFieldFile(encoded);
            var nav = native.ReadField(checkpoint.FieldId);
            var destinations = NativeGateways(bytes).Where(g => g.Line == checkpoint.ExpectedLine)
                .Select(g => g.Destination)
                .Concat(nav.Exits.Where(e => e.TriggerLine == checkpoint.ExpectedLine)
                    .SelectMany(e => e.DestinationFieldIds ?? []))
                .ToHashSet();
            foreach (var destination in checkpoint.ExpectedDestinationFieldIds)
                if (!destinations.Contains(destination))
                    errors.Add($"Native target no longer leads to expected field {destination}.");
            if (checkpoint.RouteArrivalFromFieldId is { } from)
            {
                const int fieldPointer = 0x02000000;
                int ReadInt(int address) => address == FieldWalkmeshReader.AddressFieldDataPtr ? fieldPointer :
                    address >= fieldPointer && address <= fieldPointer + bytes.Length - 4
                        ? BitConverter.ToInt32(bytes, address - fieldPointer) : 0;
                short ReadShort(int address) => address >= fieldPointer && address <= fieldPointer + bytes.Length - 2
                    ? BitConverter.ToInt16(bytes, address - fieldPointer) : (short)0;
                var meshReader = new FieldWalkmeshReader(ReadInt, ReadShort);
                var mesh = meshReader.Read(position).Walkmesh;
                var entries = new HashSet<(int X, int Y, ushort Triangle)>();
                if (source.TryReadField(from, out var incoming))
                    foreach (var gateway in NativeGateways(Ff7LzsDecoder.DecodeFieldFile(incoming)))
                        if (gateway.Destination == checkpoint.FieldId)
                            entries.Add((gateway.X, gateway.Y, gateway.Triangle));
                foreach (var op in native.ReadAllScriptOpcodes(from).SelectMany(s => s.Opcodes)
                             .Where(o => o.Opcode == 0x60 && o.Bytes.Count == 10))
                {
                    var b = op.Bytes.ToArray();
                    if (BitConverter.ToUInt16(b, 1) == checkpoint.FieldId)
                        entries.Add((BitConverter.ToInt16(b, 3), BitConverter.ToInt16(b, 5), BitConverter.ToUInt16(b, 7)));
                }
                if (checkpoint.RouteArrivalTriangle is { } wantedTriangle)
                    entries.RemoveWhere(e => e.Triangle != wantedTriangle);
                if (entries.Count == 0) errors.Add($"No native arrival from field {from}.");
                if (mesh is null) errors.Add("Native walkmesh unavailable for route replay.");
                foreach (var entry in entries)
                {
                    if (mesh is null || entry.Triangle >= mesh.Triangles.Count)
                    { errors.Add("Native arrival triangle outside walkmesh."); continue; }
                    var start = new FieldPositionSnapshot(FieldPositionReader.FieldModule, checkpoint.FieldId, 0,
                        entry.X, entry.Y, (int)Math.Round(mesh.Triangles[entry.Triangle].GetCentroid().Z), entry.Triangle, 0);
                    var target = reader.ReadTargets(start).Where(t => t.Label == checkpoint.ExpectedLabels[0]).ToArray();
                    var planner = new FieldWalkmeshRoutePlanner(meshReader, transitionProvider: _ => nav.Transitions);
                    var passed = target.Length == 1 && planner.TryBuildRoute(start, target[0], out _);
                    routeWitnesses.Add(new { from, entry.X, entry.Y, entry.Triangle, passed, planner.LastDiagnostic });
                    if (!passed) errors.Add($"No route to the expected active target from native arrival {from}:{entry.Triangle}: {planner.LastDiagnostic}");
                }
            }
        }
        else errors.Add("Native field unavailable for transition replay.");
    }
    failures.AddRange(errors.Select(e => $"Checkpoint {checkpoint.Id}: {e}"));
    checkpoints.Add(new { checkpoint.Id, checkpoint.FieldId, checkpoint.Moment,
        checkpoint.Evidence, passed = errors.Count == 0, errors, routeWitnesses,
        actual = actual.Select(t => new { t.Label, t.TriggerEntityId, t.X, t.Y, t.Z }) });
}

var report = new { schemaVersion = 1, runtime = Environment.Is64BitProcess ? "x64" : "x86",
    runtimeAssembly = typeof(FieldStoryEventCatalog).Assembly.GetName().Name,
    nativeRoot = Path.GetFullPath(args[0]),
    evidenceLevel = "Native catalog inventory, explicit Story state replay, and requested static route witnesses from native arrivals. Does not simulate script execution, moving actors, or a playthrough.",
    storyDefinitions = story.Count, objectDefinitions = objects.Count,
    fields, checkpoints, failures, manualInteractionReviewCandidates = reviews, geometryReviewCandidates = geometryReviews };
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(report, jsonOptions));
Console.WriteLine($"{report.runtime}: {fields.Count} native fields, {story.Count} Story rows, " +
    $"{checkpoints.Count} checkpoints, {failures.Count} failures, {reviews.Count} interactions needing review. Report: {output}");
return failures.Count == 0 ? 0 : 1;

static int ReadTriangleCount(byte[] bytes)
{
    const int sectionOffsetPosition = 6 + 4 * sizeof(int);
    if (bytes.Length < sectionOffsetPosition + 4) return 0;
    var offset = BitConverter.ToInt32(bytes, sectionOffsetPosition);
    return offset >= 0 && offset <= bytes.Length - 8 ? BitConverter.ToInt32(bytes, offset + 4) : 0;
}

static FieldNavigationTriggerLine ReadLine(byte[] bytes, int offset) => new(
    BitConverter.ToInt16(bytes, offset), BitConverter.ToInt16(bytes, offset + 2),
    BitConverter.ToInt16(bytes, offset + 4), BitConverter.ToInt16(bytes, offset + 6),
    BitConverter.ToInt16(bytes, offset + 8), BitConverter.ToInt16(bytes, offset + 10));

static IEnumerable<(FieldNavigationTriggerLine Line, int Destination, int X, int Y, ushort Triangle)> NativeGateways(byte[] bytes)
{
    var section = BitConverter.ToInt32(bytes, 6 + 7 * 4) + 4;
    for (var i = 0; i < 12; i++)
    {
        var at = section + 0x38 + i * 24;
        var destination = BitConverter.ToInt16(bytes, at + 18);
        if (destination >= 0)
            yield return (ReadLine(bytes, at), destination, BitConverter.ToInt16(bytes, at + 12),
                BitConverter.ToInt16(bytes, at + 14), BitConverter.ToUInt16(bytes, at + 16));
    }
}

sealed class Checkpoint
{
    public string Id { get; set; } = "";
    public string Evidence { get; set; } = "";
    public int FieldId { get; set; }
    public int Moment { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public ushort Triangle { get; set; }
    public StateByte[] State { get; set; } = [];
    public Actor[] Actors { get; set; } = [];
    public int[] EnabledLines { get; set; } = [];
    public string[] ExpectedLabels { get; set; } = [];
    public string[] ForbiddenLabels { get; set; } = [];
    public bool ExpectEmpty { get; set; }
    public NativeAnchor[] NativeAnchors { get; set; } = [];
    public FieldNavigationTriggerLine? ExpectedLine { get; set; }
    public int[] ExpectedDestinationFieldIds { get; set; } = [];
    public int? RouteArrivalFromFieldId { get; set; }
    public ushort? RouteArrivalTriangle { get; set; }
}
sealed record StateByte(int Bank, int Address, byte Value);
sealed record Actor(int EntityId, int X, int Y, int Z);
sealed record NativeAnchor(int EntityId, int ScriptId, string Hex);

sealed class AuditMemory
{
    private const int EventTable = 0x03000000;
    private readonly Dictionary<int, byte> state = [];
    private readonly Checkpoint checkpoint;
    public AuditMemory(Checkpoint checkpoint)
    {
        this.checkpoint = checkpoint;
        foreach (var slot in new[] { 9, 10, 11 })
            state[FieldNavigationObjectReader.AddressFieldBankBase + 0x100 + slot] = 0xFF;
        foreach (var item in checkpoint.State)
        {
            if (item.Address is < 0 or > 255)
                throw new InvalidDataException($"Checkpoint bank address outside byte range: {item.Address}");
            var bankBase = item.Bank switch {
                1 => FieldNavigationObjectReader.AddressFieldBankBase,
                3 => FieldNavigationObjectReader.AddressFieldBankBase + 0x100,
                5 => FieldNavigationObjectReader.AddressTemporaryFieldBankBase,
                11 => FieldNavigationObjectReader.AddressFieldBankBase + 0x200,
                13 => FieldNavigationObjectReader.AddressFieldBankBase + 0x300,
                15 => FieldNavigationObjectReader.AddressFieldBankBase + 0x400,
                _ => throw new InvalidDataException($"Unsupported checkpoint bank {item.Bank}") };
            state[bankBase + item.Address] = item.Value;
        }
        state[FieldNavigationObjectReader.AddressFieldBankBase] = (byte)checkpoint.Moment;
        state[FieldNavigationObjectReader.AddressFieldBankBase + 1] = (byte)(checkpoint.Moment >> 8);
    }
    public byte Byte(int address)
    {
        if (address == FieldPositionReader.AddressFieldNumModels) return checked((byte)(checkpoint.Actors.Length + 1));
        if (address >= FieldNavigationObjectReader.AddressFieldModelIdArray &&
            address < FieldNavigationObjectReader.AddressFieldModelIdArray + 255)
        {
            var entity = address - FieldNavigationObjectReader.AddressFieldModelIdArray;
            var index = Array.FindIndex(checkpoint.Actors, a => a.EntityId == entity);
            return index < 0 ? (byte)255 : checked((byte)(index + 1));
        }
        var offset = address - EventTable;
        if (offset >= 0 && offset / FieldNavigationObjectReader.FieldEventDataStride <= checkpoint.Actors.Length &&
            offset % FieldNavigationObjectReader.FieldEventDataStride == FieldNavigationObjectReader.VisibilityOffset) return 1;
        return state.GetValueOrDefault(address);
    }
    public int Int32(int address)
    {
        if (address == FieldNavigationObjectReader.AddressFieldEventDataPtr) return EventTable;
        var offset = address - EventTable;
        var model = offset / FieldNavigationObjectReader.FieldEventDataStride;
        if (offset < 0 || model < 1 || model > checkpoint.Actors.Length) return 0;
        var actor = checkpoint.Actors[model - 1];
        var coordinate = (offset % FieldNavigationObjectReader.FieldEventDataStride) switch {
            FieldNavigationObjectReader.PositionXOffset => actor.X,
            FieldNavigationObjectReader.PositionYOffset => actor.Y,
            FieldNavigationObjectReader.PositionZOffset => actor.Z, _ => 0 };
        return coordinate * FieldNavigationObjectReader.ModelPositionFixedPointScale;
    }
    public short Int16(int address) => address >= EventTable &&
        (address - EventTable) % FieldNavigationObjectReader.FieldEventDataStride is 0x72 or 0x74 ? (short)40 : (short)0;
}

using System.Text.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ff7.Accessibility.Reloaded;
if (args.Length != 2) return 2;
var source = new FlevelDataSource(args[0]);
var catalog = new FieldScriptNavigationCatalog(args[0]);
var wanted = TownInteractionObjectCatalog.Create().Select(d => d.FieldId).ToHashSet();
var entries = new Dictionary<int, HashSet<(int X, int Y, int Triangle)>>();
var entryOrigins = new Dictionary<(int Field, int X, int Y, int Triangle), HashSet<string>>();
var data = new Dictionary<int, byte[]>();
foreach (var f in source.FieldNames.Keys)
{
    if (!source.TryReadField(f, out var encoded)) continue;
    var b = Ff7LzsDecoder.DecodeFieldFile(encoded);
    if (wanted.Contains(f)) data[f] = b;
    var t = BitConverter.ToInt32(b, 6 + 7 * 4) + 4;
    for (var i = 0; i < 12; i++) { var at = t + 0x38 + i * 24; Add(BitConverter.ToInt16(b, at + 18), BitConverter.ToInt16(b, at + 12), BitConverter.ToInt16(b, at + 14), BitConverter.ToUInt16(b, at + 16), $"field {f} gateway {i}"); }
    foreach (var op in catalog.ReadAllScriptOpcodes(f).SelectMany(s => s.Opcodes).Where(o => o.Opcode == 0x60 && o.Bytes.Count == 10))
    {
        var a = op.Bytes.ToArray(); Add(BitConverter.ToUInt16(a, 1), BitConverter.ToInt16(a, 3), BitConverter.ToInt16(a, 5), BitConverter.ToUInt16(a, 7), $"field {f} entity {op.EntityId} script {op.ScriptId}");
    }
}
var puzzleLandingsOnly = new List<string>();
var results = new List<object>(); var failed = new List<string>(); var success = 0; var routes = 0;
foreach (var group in TownInteractionObjectCatalog.Create().GroupBy(d => d.FieldId))
{
    var f = group.Key; var b = data[f]; const int ptr = 0x02000000;
    int Int(int a) => a == FieldWalkmeshReader.AddressFieldDataPtr ? ptr : a >= ptr && a + 4 <= ptr + b.Length ? BitConverter.ToInt32(b, a - ptr) : 0;
    short Short(int a) => a >= ptr && a + 2 <= ptr + b.Length ? BitConverter.ToInt16(b, a - ptr) : (short)0;
    var reader = new FieldWalkmeshReader(Int, Short); var mesh = reader.Read(new(1, f, 0, 0, 0, 0, 0, 0)).Walkmesh!;
    var nav = catalog.ReadField(f);
    // Production resolves polled traversal source heights before planning. Keep that
    // geometry step while explicitly assuming every LINE and possible mover is available.
    var transitions = new FieldScriptNavigationTransitionTracker().ResolveForNavigation(f, nav.Transitions, _ => true, mesh);
    var planner = new FieldWalkmeshRoutePlanner(reader, transitionProvider: _ => transitions);
    foreach (var row in group)
    {
        var x = row.StaticX; var y = row.StaticY; var z = row.StaticZ;
        if (row.TargetKind == FieldNavigationObjectTargetKind.Model)
        {
            var xyz = catalog.ReadAllScriptOpcodes(f).Where(s => s.EntityId == row.EntityId && s.ScriptId == 0)
                .SelectMany(s => s.Opcodes).First(o => o.Opcode == 0xA5).Bytes.ToArray();
            x = BitConverter.ToInt16(xyz, 3); y = BitConverter.ToInt16(xyz, 5); z = BitConverter.ToInt16(xyz, 7);
            // This frog is hidden at its Init location. Script 3 enables it and lands
            // it on triangle 3; its runtime target always reads that live position.
            if (f == 620 && row.EntityId == 9)
            {
                var jump = catalog.ReadAllScriptOpcodes(f).Single(s => s.EntityId == 9 && s.ScriptId == 3).Opcodes.Single(o => o.Opcode == 0xC0).Bytes.ToArray();
                x = BitConverter.ToInt16(jump, 3); y = BitConverter.ToInt16(jump, 5);
                z = (int)Math.Round(mesh.Triangles[BitConverter.ToUInt16(jump, 7)].GetCentroid().Z);
            }
        }
        var radius = row.TargetKind == FieldNavigationObjectTargetKind.Line ? 19 : row.InteractionRadiusOverride ?? 48;
        var target = new FieldNavigationTarget(f, FieldNavigationCategory.Objects, row.Label!, x, y, z, InteractionRadius: radius);
        var attempted = new List<object>(); var passed = 0;
        foreach (var entry in entries.GetValueOrDefault(f) ?? [])
        {
            if (entry.Triangle >= mesh.Triangles.Count) continue;
            var tri = mesh.Triangles[entry.Triangle]; var height = (int)Math.Round(tri.GetCentroid().Z);
            var start = new FieldPositionSnapshot(1, f, 0, entry.X, entry.Y, height, (ushort)entry.Triangle, 0);
            var ok = planner.TryBuildRoute(start, target, out var plan); long d2 = -1;
            if (ok) { long dx = plan.FinalApproach.X - x, dy = plan.FinalApproach.Y - y; d2 = dx * dx + dy * dy; ok = d2 < (long)radius * radius; }
            attempted.Add(new { entry = new[] { entry.X, entry.Y, entry.Triangle }, origins = entryOrigins[(f, entry.X, entry.Y, entry.Triangle)].Order().ToArray(), ok, d2, diagnostic = planner.LastDiagnostic }); routes++;
            if (ok) passed++;
        }
        if (passed == 0 && f is 620 or 623 && row.EntityId == 16)
        {
            // The two hives are on puzzle ledges reached by the game's own player
            // jumps. Check approach after that native landing, without claiming the
            // navigator solves the insect/frog puzzle or can walk up from the entrance.
            var scripts = catalog.ReadAllScriptOpcodes(f);
            var players = scripts.Where(s => s.ScriptId == 0 && s.Opcodes.Any(o => o.Opcode == 0xA0)).Select(s => s.EntityId).ToHashSet();
            foreach (var op in scripts.Where(s => players.Contains(s.EntityId)).SelectMany(s => s.Opcodes)
                    .Where(o => o.Opcode == 0xC0 && o.Bytes.Count == 11 && o.Bytes[1] == 0 && o.Bytes[2] == 0))
            {
                var a = op.Bytes.ToArray(); var px = BitConverter.ToInt16(a, 3); var py = BitConverter.ToInt16(a, 5); var tr = BitConverter.ToUInt16(a, 7);
                if (tr >= mesh.Triangles.Count) continue;
                var pz = (int)Math.Round(mesh.Triangles[tr].GetCentroid().Z);
                var ok = planner.TryBuildRoute(new(1, f, 0, px, py, pz, tr, 0), target, out var plan);
                if (!ok) continue;
                long dx = plan.FinalApproach.X - x, dy = plan.FinalApproach.Y - y;
                if (dx * dx + dy * dy >= (long)radius * radius) continue;
                attempted.Add(new { nativePlayerLanding = new[] { px, py, (int)tr }, entity = op.EntityId, script = op.ScriptId, ok = true });
                passed++; routes++; puzzleLandingsOnly.Add($"{f}/{row.EntityId}: native player jump required before approach"); break;
            }
        }
        if (passed == 0) failed.Add($"{f}/{row.EntityId} {row.Label}: no native entry reaches activation radius"); else success++;
        results.Add(new { f, row.EntityId, row.Label, point = new[] { x, y, z }, radius, passed, attempted });
    }
}
var runtimeAssembly = typeof(FieldScriptNavigationCatalog).Assembly;
File.WriteAllText(args[1], JsonSerializer.Serialize(new {
    runtime = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
    runtimeAssembly = runtimeAssembly.GetName().Name,
    runtimeAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(runtimeAssembly.Location))),
    archive = Path.GetFullPath(args[0]),
    evidence = "Static native walkmesh replay with possible script transitions and production source-height resolution. Assumes every LINE and possible mover is available; does not simulate live IDLCK, actor ownership, gateway state or gameplay input. A successful route proves geometry for that arrival and assumed transitions only, not availability in a live save. puzzleLandingsOnly targets require the native puzzle jump before the tested approach; they are not routes from field entrances.",
    objects = success + failed.Count, uniqueObjects = TownInteractionObjectCatalog.Create().Select(row => (row.FieldId, row.EntityId)).Distinct().Count(), routes, success, puzzleLandingsOnly, failed, results
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Optional-object geometry: {success}/{success + failed.Count} definitions have at least one successful approach; {routes} routes attempted. This does not certify every arrival or live state.");
foreach (var f in failed) Console.WriteLine(f);
return failed.Count == 0 ? 0 : 1;
void Add(int f, int x, int y, int tri, string origin)
{
    if (!wanted.Contains(f)) return;
    if (!entries.TryGetValue(f, out var set)) entries[f] = set = [];
    set.Add((x, y, tri));
    var key = (f, x, y, tri);
    if (!entryOrigins.TryGetValue(key, out var origins)) entryOrigins[key] = origins = [];
    origins.Add(origin);
}

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Ff7.Accessibility.Reloaded;

namespace GuideRouteAudit;

// This is an evidence collector, not a live-state emulator or a pass/fail release gate.
// Positions from separate scripts are independent witnesses. Their visibility, flags,
// controlled actor and all conditional traversals still require native-state review.
internal static class NativeAudit
{
    private sealed record Arrival(int Source, int X, int Y, ushort Triangle, string Origin);
    private sealed record Point(int X, int Y, int Z, string Source);
    private sealed record Candidate(string Id, string Category, string Label, int Entity,
        FieldNavigationTarget? Target, string Evidence, bool ActivationKnown,
        IReadOnlyList<int>? RequiredTriangles = null, IReadOnlyList<int>? ExcludedTriangles = null, bool UsesTalkRange = false,
        FieldNavigationTriggerLine? PlayerSideLine = null, int PlayerSide = 0);

    internal static int Run(string gameRoot, string reportPath)
    {
        var source = new FlevelDataSource(gameRoot);
        var names = Kernel2TextDatabase.TryCreate(gameRoot);
        var catalog = new FieldScriptNavigationCatalog(gameRoot);
        var arrivals = new Dictionary<int, List<Arrival>>();
        var readErrors = new List<object>();
        foreach (var (id, name) in source.FieldNames.OrderBy(p => p.Key))
        {
            if (!source.TryReadField(id, out var encoded))
            { readErrors.Add(new { id, name, source.Diagnostic }); continue; }
            var b = Ff7LzsDecoder.DecodeFieldFile(encoded);
            foreach (var g in Gateways(b))
                AddArrival(g.Destination, new(id, g.X, g.Y, g.Triangle, $"gateway:{g.Index}"));
            foreach (var op in catalog.ReadAllScriptOpcodes(id).SelectMany(s => s.Opcodes)
                         .Where(o => o.Opcode == 0x60 && o.Bytes.Count == 10))
            {
                var a = op.Bytes.ToArray();
                AddArrival(BitConverter.ToUInt16(a, 1), new(id, BitConverter.ToInt16(a, 3),
                    BitConverter.ToInt16(a, 5), BitConverter.ToUInt16(a, 7),
                    $"MAPJUMP:{op.EntityId}:{op.ScriptId}:{op.ByteIndex}"));
            }
        }
        Console.WriteLine($"Collected native arrivals for {arrivals.Count} fields.");
        var objects = FieldNavigationObjectCatalog.CreateAllFields().GroupBy(o => o.FieldId).ToDictionary(g => g.Key, g => g.ToArray());
        var stories = FieldStoryEventCatalog.CreateAllFields().GroupBy(o => o.FieldId).ToDictionary(g => g.Key, g => g.ToArray());
        var fields = new List<object>();
        var counts = new Dictionary<string, int>();
        var targetCount = 0; var attemptCount = 0;
        foreach (var (id, name) in source.FieldNames.OrderBy(p => p.Key))
        {
            if (!source.TryReadField(id, out var encoded)) continue;
            var b = Ff7LzsDecoder.DecodeFieldFile(encoded);
            const int ptr = 0x02000000;
            int Int(int a) => a == FieldWalkmeshReader.AddressFieldDataPtr ? ptr :
                a >= ptr && a + 4 <= ptr + b.Length ? BitConverter.ToInt32(b, a - ptr) : 0;
            short Short(int a) => a >= ptr && a + 2 <= ptr + b.Length ? BitConverter.ToInt16(b, a - ptr) : (short)0;
            var reader = new FieldWalkmeshReader(Int, Short);
            var mesh = reader.Read(new(1, id, 0, 0, 0, 0, 0, 0)).Walkmesh;
            var native = catalog.ReadField(id);
            var scripts = catalog.ReadAllScriptOpcodes(id);
            var radii = new NativeRadii(b, scripts);
            var playerRadius = radii.Player();
            var positions = scripts.SelectMany(s => s.Opcodes)
                .Where(o => o.Opcode == 0xA5 && o.Bytes.Count == 11 && o.Bytes[1] == 0 && o.Bytes[2] == 0)
                .GroupBy(o => o.EntityId).ToDictionary(g => g.Key, g => g
                    .Select(o => { var a = o.Bytes.ToArray(); return new Point(BitConverter.ToInt16(a, 3),
                        BitConverter.ToInt16(a, 5), BitConverter.ToInt16(a, 7), $"XYZI:{o.ScriptId}:{o.ByteIndex}"); })
                    .DistinctBy(p => (p.X, p.Y, p.Z)).ToArray());
            var candidates = new List<Candidate>();
            var index = 0;
            foreach (var o in objects.GetValueOrDefault(id) ?? [])
            {
                var key = $"object:{index++}:{o.EntityId}";
                var label = !string.IsNullOrWhiteSpace(o.Label) ? o.Label : o.Kind switch
                {
                    FieldNavigationObjectKind.Item => names?.ResolveInventoryObjectName(o.NativeId) ?? $"Item:{o.NativeId}",
                    FieldNavigationObjectKind.Materia => (names?.ResolveMateriaName(o.NativeId) ?? $"Materia:{o.NativeId}") + " Materia",
                    FieldNavigationObjectKind.SavePoint => "Save point",
                    _ => $"{o.Kind}:{o.NativeId}"
                };
                var pts = o.TargetKind == FieldNavigationObjectTargetKind.Model
                    ? positions.GetValueOrDefault(o.EntityId) ?? [] : [new Point(o.StaticX, o.StaticY, o.StaticZ, "catalog")];
                if (o.TargetKind == FieldNavigationObjectTargetKind.Line)
                {
                    foreach (var segment in NativeLineWitnesses.Read(scripts, o.EntityId))
                        candidates.Add(new(key + ":" + segment.Evidence, "Objects", label, o.EntityId,
                            segment.Line is { } line ? NativeObjectWitnesses.Target(o, label,
                                o.StaticX, o.StaticY, o.StaticZ, 0, line, playerRadius.Value) : null,
                            segment.Evidence, segment.Line is not null && playerRadius.Exact && playerRadius.Value > 1,
                            o.RequiredPlayerTriangles, o.ExcludedPlayerTriangles, false, o.PlayerSideLine, o.PlayerSide));
                    continue;
                }
                // Model default is the production object's own threshold. Talk and LINE
                // thresholds need a live player radius and cannot be certified here.
                var talk = radii.Read(o.EntityId, true);
                var known = o.UsesTalkInteraction ? playerRadius.Exact && talk.Exact :
                    !o.UsesPlayerCollisionRadius || playerRadius.Exact;
                var radius = o.UsesTalkInteraction ? playerRadius.Value + talk.Value :
                    o.UsesPlayerCollisionRadius ? Math.Max(0, playerRadius.Value - 1) :
                    o.InteractionRadiusOverride ?? FieldNavigationObjectReader.DefaultInteractionRadius;
                AddPoints(key, "Objects", label, o.EntityId, pts,
                    p => NativeObjectWitnesses.Target(o, label, p.X, p.Y, p.Z, radius),
                    known, o.RequiredPlayerTriangles, o.ExcludedPlayerTriangles, o.UsesTalkInteraction,
                    o.PlayerSideLine, o.PlayerSide);
            }
            index = 0;
            foreach (var s in stories.GetValueOrDefault(id) ?? [])
            {
                var key = $"story:{index++}:{s.EntityId}";
                var pts = s.Kind == FieldStoryTargetKind.Model ? positions.GetValueOrDefault(s.EntityId) ?? [] :
                    [new Point(s.X, s.Y, s.Z, "catalog")];
                var modelRadius = radii.Read(s.EntityId, !s.UsesContactRange);
                var known = s.Kind == FieldStoryTargetKind.Model ? playerRadius.Exact && modelRadius.Exact :
                    s.UsesPlayerCollisionRadius ? playerRadius.Exact && playerRadius.Value > 1 :
                    s.TriggerLine is not null ? playerRadius.Exact : true;
                AddPoints(key, "Story", s.Label, s.EntityId, pts,
                    p => NativeStoryWitnesses.Target(s, p.X, p.Y, p.Z, playerRadius.Value, modelRadius.Value), known,
                    s.RequiredPlayerTriangles, s.ExcludedPlayerTriangles, s.Kind == FieldStoryTargetKind.Model && !s.UsesContactRange);
            }
            foreach (var n in NativeNpcWitnesses.Merge(id, native.Npcs))
            {
                var key = $"npc:{n.EntityId}";
                var label = NativeNpcWitnesses.Label(n);
                var modelRadius = radii.Read(n.EntityId, !n.ContactOnly);
                if (n.InteractionLine is { } line && !n.ContactOnly)
                {
                    AddPoints(key + ":line", "Npcs", label, n.EntityId,
                        [new((line.StartX + line.EndX) / 2, (line.StartY + line.EndY) / 2,
                            (line.StartZ + line.EndZ) / 2, $"LINE:{n.InteractionLineEntityId}")],
                        p => NativeNpcWitnesses.Target(n, p.X, p.Y, p.Z, playerRadius.Value, modelRadius.Value, true),
                        playerRadius.Exact && playerRadius.Value > 1);
                }
                // A live Talk can take precedence over a secondary LINE exchange.
                // Retain both possible states, as the production reader does.
                if (n.ContactOnly || n.InteractionLine is null || n.DialogIds.Count > 0 && !n.InteractionLineRunsTalk)
                    AddPoints(key + ":model", "Npcs", label, n.EntityId, positions.GetValueOrDefault(n.EntityId) ?? [],
                        p => NativeNpcWitnesses.Target(n, p.X, p.Y, p.Z, playerRadius.Value, modelRadius.Value, false),
                        playerRadius.Exact && modelRadius.Exact, talk: !n.ContactOnly);
            }
            foreach (var exit in native.Exits)
                candidates.Add(new(exit.StableId, "Exits", exit.Label, exit.TriggerEntityId, exit, "native-script-exit",
                    exit.TriggerLine is null || playerRadius.Exact));
            foreach (var g in Gateways(b).Where(g => source.FieldNames.ContainsKey(g.Destination)))
            {
                var l = g.Line; var key = $"gateway:{id}:{g.Index}:{g.Destination}";
                candidates.Add(new(key, "Exits", $"{g.Destination}:{source.FieldNames[g.Destination]}", -1,
                    new(id, FieldNavigationCategory.Exits, key, (l.StartX + l.EndX) / 2, (l.StartY + l.EndY) / 2,
                        (l.StartZ + l.EndZ) / 2, key, CompletesOnArrival: true,
                        DestinationFieldIds: [g.Destination], TriggerLine: l), "native-gateway", playerRadius.Exact));
            }
            // Coincident transfer coordinates from different source fields are separate
            // witnesses. One source can have a reviewed automatic entry movement while
            // another does not; merging them would hide the unreviewed starting state.
            var localArrivals = (arrivals.GetValueOrDefault(id) ?? [])
                .GroupBy(a => (a.Source, a.X, a.Y, a.Triangle)).ToArray();
            var transitions = mesh is null ? [] : new FieldScriptNavigationTransitionTracker()
                .ResolveForNavigation(id, native.Transitions, _ => true, mesh);
            var planner = new FieldWalkmeshRoutePlanner(reader, transitionProvider: _ => transitions);
            var detours = new FieldCrossFieldApproachResolver(catalog, source, planner, _ => mesh);
            var exits = candidates.Where(c => c.Category == "Exits" && c.Target is not null)
                .Select(c => c.Target!.Value).ToArray();
            var rows = new List<object>();
            foreach (var c in candidates)
            {
                var attempts = new List<object>(); var assessments = new List<ApproachResult>();
                foreach (var ag in localArrivals)
                {
                    var a = ag.First();
                    if (!string.IsNullOrWhiteSpace(c.Target?.ManualNavigationGuidance))
                    {
                        attempts.Add(new { arrival = a, outcome = "native-manual-interaction-guidance", c.Target.Value.ManualNavigationGuidance });
                        continue;
                    }
                    if (ag.All(x => source.FieldNames[x.Source].StartsWith("blackbg", StringComparison.Ordinal) ||
                                    source.FieldNames[x.Source] is "startmap"))
                    {
                        attempts.Add(new { arrival = a, outcome = "debug-arrival-retained-not-playable-witness" }); continue;
                    }
                    var landing = ag.Select(x => ReviewedEntryLandings.For(id, x.Source, x.Triangle, scripts))
                        .FirstOrDefault(x => x is not null);
                    if (landing is not null)
                    {
                        attempts.Add(new { arrival = a, outcome = "native-entry-script-before-control", landing.Evidence });
                        a = new(a.Source, landing.X, landing.Y, landing.Triangle, landing.Evidence);
                    }
                    // These catalog rows intentionally become available only after a
                    // native puzzle/traversal. An entrance outside that range is not a
                    // reachable-state witness; record that rather than inventing one.
                    if (c.RequiredTriangles is { Count: > 0 } required && !required.Contains(a.Triangle) ||
                        c.ExcludedTriangles is { Count: > 0 } excluded && excluded.Contains(a.Triangle) ||
                        c.PlayerSideLine is { } sideLine &&
                        (c.PlayerSide == 0 || FieldNavigationObjectReader.SideOf(sideLine, a.X, a.Y) != c.PlayerSide))
                    {
                        attempts.Add(new { arrival = a, outcome = "catalog-inactive-at-arrival" }); continue;
                    }
                    if (mesh is null || a.Triangle >= mesh.Triangles.Count || c.Target is not { } target)
                    {
                        assessments.Add(new(null, null)); attempts.Add(new { arrival = a, outcome = "missing-native-witness" }); continue;
                    }
                    var height = Height(mesh.Triangles[a.Triangle], a.X, a.Y);
                    var ok = planner.TryBuildRoute(new(1, id, 0, a.X, a.Y, height, a.Triangle, 0), target, out var plan);
                    var diagnostic = planner.LastDiagnostic;
                    // Retain the failed direct route. A static detour is a separate witness:
                    // adjacent field locks, line state and branch-selected arrivals remain
                    // unknown until that field is loaded by the game.
                    var detour = ok ? null : detours.Resolve(new(1, id, 0, a.X, a.Y, height, a.Triangle, 0), target, exits);
                    bool? within = null; var scripted = false; double? distance = null; int[]? end = null;
                    if (ok)
                    {
                        end = [plan.FinalApproach.X, plan.FinalApproach.Y, plan.FinalApproach.Z];
                        scripted = plan.Portals.Any(p => p.TransitionKind is not null);
                        distance = Math.Sqrt(Math.Pow(plan.FinalApproach.X - target.X, 2) + Math.Pow(plan.FinalApproach.Y - target.Y, 2));
                        if (c.ActivationKnown)
                        {
                            within = EndpointEvidence.Reaches(target, plan.FinalApproach.X, plan.FinalApproach.Y,
                                plan.FinalApproach.Z, plan.TargetTriangle, c.UsesTalkRange, playerRadius.Value);
                        }
                    }
                    assessments.Add(new(ok, within, scripted, c.ActivationKnown)); attemptCount++;
                    attempts.Add(new { arrival = a, origins = ag.Select(x => new { x.Source, x.Origin }).ToArray(),
                        connected = ok, withinReach = within, scripted, distance, end, diagnostic,
                        staticOutAndBack = detour });
                }
                var summary = !string.IsNullOrWhiteSpace(c.Target?.ManualNavigationGuidance)
                    ? "manual-interaction" : RouteAssessment.Summarize(assessments);
                counts[summary] = counts.GetValueOrDefault(summary) + 1; targetCount++;
                rows.Add(new { c.Id, c.Category, c.Label, c.Entity, c.Evidence, c.ActivationKnown, c.Target,
                    c.RequiredTriangles, c.ExcludedTriangles, c.PlayerSideLine, c.PlayerSide,
                    summary, attempts });
            }
            fields.Add(new { id, name, playerRadius = playerRadius.Value, playerRadiusExact = playerRadius.Exact, triangles = mesh?.Triangles.Count ?? 0,
                arrivals = localArrivals.Length, scriptTransitions = transitions.Count, targets = rows });
            if (fields.Count % 50 == 0) Console.WriteLine($"{fields.Count} fields; {targetCount} target-position witnesses; {attemptCount} route attempts.");

            void AddPoints(string key, string category, string label, int entity, Point[] pts,
                Func<Point, FieldNavigationTarget> build, bool known,
                IReadOnlyList<int>? required = null, IReadOnlyList<int>? excluded = null, bool talk = false,
                FieldNavigationTriggerLine? sideLine = null, int side = 0)
            {
                if (pts.Length == 0) candidates.Add(new(key, category, label, entity, null,
                    "no-literal-XYZI-position", false, required, excluded, talk, sideLine, side));
                foreach (var p in pts) candidates.Add(new(key, category, label, entity, build(p), p.Source, known, required, excluded, talk, sideLine, side));
            }
        }
        var assembly = typeof(FieldScriptNavigationCatalog).Assembly;
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new {
            runtime = RuntimeInformation.ProcessArchitecture.ToString(), assembly = assembly.GetName().Name,
            assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
            archive = Path.GetFullPath(gameRoot),
            evidence = "Static native geometry only. All possible script traversals and enabled lines assumed available. Separate XYZI positions are independent possible-state witnesses, not a simultaneous live scene. Actor ownership, visibility, flags, IDLCK, gateway enable, body clearance and input are not simulated. Radii follow Ghidra 0060BCFA/0061813D/00618253; varying or bank-backed radii remain unverified. Every native arrival is retained; debug warps are explicitly excluded from playable witnesses. One good arrival cannot hide another failure. Review findings against reachable game states before changing runtime behavior.",
            fieldCount = fields.Count, targetCount, attemptCount, counts, readErrors, fields
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { fieldCount = fields.Count, targetCount, attemptCount, counts, readErrors = readErrors.Count }));
        // The map name table also includes world-map slots and unused field names.
        // Keep every unavailable name in the report; they are not corrupt walkmeshes.
        return fields.Count > 0 ? 0 : 1;

        void AddArrival(int destination, Arrival arrival)
        {
            if (!source.FieldNames.ContainsKey(destination)) return;
            if (!arrivals.TryGetValue(destination, out var rows)) arrivals[destination] = rows = [];
            if (!rows.Contains(arrival)) rows.Add(arrival);
        }
    }

    private static IEnumerable<(int Index, int Destination, int X, int Y, ushort Triangle, FieldNavigationTriggerLine Line)> Gateways(byte[] b)
    {
        var t = BitConverter.ToInt32(b, 6 + 7 * 4) + 4;
        for (var i = 0; i < 12; i++)
        {
            var a = t + 0x38 + i * 24;
            yield return (i, BitConverter.ToInt16(b, a + 18), BitConverter.ToInt16(b, a + 12),
                BitConverter.ToInt16(b, a + 14), BitConverter.ToUInt16(b, a + 16),
                new(BitConverter.ToInt16(b, a), BitConverter.ToInt16(b, a + 2), BitConverter.ToInt16(b, a + 4),
                    BitConverter.ToInt16(b, a + 6), BitConverter.ToInt16(b, a + 8), BitConverter.ToInt16(b, a + 10)));
        }
    }
    private static int Height(FieldWalkmeshTriangle t, int x, int y)
    {
        var a = t.Vertex0; var b = t.Vertex1; var c = t.Vertex2;
        double d = (b.Y - c.Y) * (double)(a.X - c.X) + (c.X - b.X) * (double)(a.Y - c.Y);
        if (Math.Abs(d) < 0.01) return (int)Math.Round(t.GetCentroid().Z);
        double u = ((b.Y - c.Y) * (double)(x - c.X) + (c.X - b.X) * (double)(y - c.Y)) / d;
        double v = ((c.Y - a.Y) * (double)(x - c.X) + (a.X - c.X) * (double)(y - c.Y)) / d;
        return (int)Math.Round(u * a.Z + v * b.Z + (1 - u - v) * c.Z);
    }
}

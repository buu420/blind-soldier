using Ff7.Accessibility.Reloaded;

/// <summary>
/// Sweeps every field for scripted triggers the planner anchors to the wrong
/// storey, and reports what the elevation-aware anchor resolves them to instead.
///
/// Run with <c>--field-transition-anchor-audit</c>. As of 2026-08-20 it finds
/// two affected fields out of the 128 that carry transitions: convil_1 (355),
/// where the save room's phantom ladder left auto-walk oscillating, and kuro_7
/// (610).
/// </summary>
internal static class FieldTransitionAnchorAudit
{
    internal static void Run(
        Func<int, FieldWalkmeshReader> createReader,
        FieldScriptNavigationCatalog catalog)
    {
        AuditEveryField(createReader, catalog);
    }

    private static void RunWalkmesh(Func<int, FieldWalkmeshReader> createReader)
    {
        const int fieldId = 355;
        var result = createReader(fieldId).Read(new FieldPositionSnapshot(1, fieldId, 0, 1088, 345, 8, 12, 0));
        Console.WriteLine($"walkmesh usable={result.IsUsable}: {result.Diagnostic}");
        var walkmesh = result.Walkmesh;
        if (walkmesh is null)
        {
            return;
        }

        Console.WriteLine($"triangles={walkmesh.Triangles.Count}");
        Console.WriteLine();
        Console.WriteLine("=== triangles the player stood on, and their neighbours ===");
        foreach (var index in new[] { 11, 12, 15, 18 })
        {
            DumpTriangle(walkmesh, index);
        }

        Console.WriteLine();
        Console.WriteLine("=== every adjacency spanning more than 192 units of Z ===");
        var steep = 0;
        for (var index = 0; index < walkmesh.Triangles.Count; index++)
        {
            var triangle = walkmesh.Triangles[index];
            for (var edge = 0; edge < 3; edge++)
            {
                var neighbour = triangle.GetAdjacentTriangle(edge);
                if (neighbour < 0 || neighbour >= walkmesh.Triangles.Count || neighbour <= index)
                {
                    continue;
                }

                var span = Math.Abs(CentroidZ(walkmesh.Triangles[neighbour]) - CentroidZ(triangle));
                if (span <= 192)
                {
                    continue;
                }

                steep++;
                Console.WriteLine(
                    $"  {index} (z={CentroidZ(triangle):F0}) <-> {neighbour} " +
                    $"(z={CentroidZ(walkmesh.Triangles[neighbour]):F0})  span={span:F0}");
            }
        }

        Console.WriteLine($"total steep adjacencies: {steep}");
    }

    private static void DumpTriangle(FieldWalkmesh walkmesh, int index)
    {
        if (index < 0 || index >= walkmesh.Triangles.Count)
        {
            return;
        }

        var triangle = walkmesh.Triangles[index];
        Console.WriteLine(
            $"  tri {index}: centroid=({CentroidX(triangle):F0},{CentroidY(triangle):F0},{CentroidZ(triangle):F0})");
        for (var edge = 0; edge < 3; edge++)
        {
            var neighbour = triangle.GetAdjacentTriangle(edge);
            if (neighbour < 0 || neighbour >= walkmesh.Triangles.Count)
            {
                Console.WriteLine($"      edge {edge}: none");
                continue;
            }

            var other = walkmesh.Triangles[neighbour];
            Console.WriteLine(
                $"      edge {edge}: -> {neighbour} centroid=" +
                $"({CentroidX(other):F0},{CentroidY(other):F0},{CentroidZ(other):F0}) " +
                $"dz={CentroidZ(other) - CentroidZ(triangle):F0}");
        }
    }

    private static double CentroidX(FieldWalkmeshTriangle t) => (t.Vertex0.X + t.Vertex1.X + t.Vertex2.X) / 3d;

    private static double CentroidY(FieldWalkmeshTriangle t) => (t.Vertex0.Y + t.Vertex1.Y + t.Vertex2.Y) / 3d;

    private static double CentroidZ(FieldWalkmeshTriangle t) => (t.Vertex0.Z + t.Vertex1.Z + t.Vertex2.Z) / 3d;

    /// <summary>
    /// For every scripted transition in convil_1, report the Z the script
    /// authored its trigger at, and the Z of the walkmesh triangle the planner
    /// anchors that transition to. A large gap means the off-mesh link has been
    /// stapled to the wrong floor of the tower.
    /// </summary>
    private static void DumpTransitionAnchoring(
        Func<int, FieldWalkmeshReader> createReader,
        FieldScriptNavigationCatalog catalog)
    {
        const int fieldId = 355;
        var read = catalog.ReadField(fieldId);
        var result = createReader(fieldId).Read(new FieldPositionSnapshot(1, fieldId, 0, 1088, 345, 8, 12, 0));
        var walkmesh = result.Walkmesh;
        Console.WriteLine($"transitions={read.Transitions.Count}, usable={read.IsUsable}: {read.Diagnostic}");
        if (walkmesh is null)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("=== transition source anchoring ===");
        foreach (var t in read.Transitions)
        {
            var resolved = FieldWalkmeshPathfinder.ResolveTriangleAtElevation(
                walkmesh, t.SourceX, t.SourceY, t.SourceZ, 192d);
            var anchorZ = resolved >= 0 && resolved < walkmesh.Triangles.Count
                ? CentroidZ(walkmesh.Triangles[resolved])
                : double.NaN;
            var gap = Math.Abs(anchorZ - t.SourceZ);
            var flag = gap > 192 ? "   <-- WRONG FLOOR" : string.Empty;
            Console.WriteLine(
                $"  {t.Kind} {t.StableId}" + Environment.NewLine +
                $"      src=({t.SourceX},{t.SourceY},{t.SourceZ}) -> tri {resolved} " +
                $"(centroidZ={anchorZ:F0}) gap={gap:F0}{flag}");
        }
    }

    /// <summary>
    /// Sweeps every field for scripted triggers anchored to the wrong storey.
    /// </summary>
    private static void AuditEveryField(
        Func<int, FieldWalkmeshReader> createReader,
        FieldScriptNavigationCatalog catalog)
    {
        var affectedFields = 0;
        var affectedTransitions = 0;
        var scanned = 0;
        for (var fieldId = 1; fieldId < 800; fieldId++)
        {
            FieldScriptNavigationReadResult read;
            FieldWalkmesh? walkmesh;
            try
            {
                read = catalog.ReadField(fieldId);
                if (!read.IsUsable || read.Transitions.Count == 0)
                {
                    continue;
                }

                walkmesh = createReader(fieldId)
                    .Read(new FieldPositionSnapshot(1, fieldId, 0, 0, 0, 0, 0, 0)).Walkmesh;
            }
            catch (Exception)
            {
                continue;
            }

            if (walkmesh is null || walkmesh.Triangles.Count == 0)
            {
                continue;
            }

            scanned++;
            var reported = false;
            foreach (var t in read.Transitions)
            {
                var oldAnchor = FieldWalkmeshPathfinder.ResolveTriangle(
                    walkmesh, t.SourceX, t.SourceY, t.SourceZ, -1);
                if (oldAnchor < 0 || oldAnchor >= walkmesh.Triangles.Count)
                {
                    continue;
                }

                var gap = Math.Abs(CentroidZ(walkmesh.Triangles[oldAnchor]) - t.SourceZ);
                if (gap <= 192)
                {
                    continue;
                }

                if (!reported)
                {
                    reported = true;
                    affectedFields++;
                    Console.WriteLine($"field {fieldId}: {read.Diagnostic}");
                }

                affectedTransitions++;
                var newAnchor = FieldWalkmeshPathfinder.ResolveTriangleAtElevation(
                    walkmesh, t.SourceX, t.SourceY, t.SourceZ, 192d);
                var newGap = newAnchor >= 0
                    ? Math.Abs(CentroidZ(walkmesh.Triangles[newAnchor]) - t.SourceZ)
                    : double.NaN;
                var planar = double.NaN;
                if (newAnchor >= 0)
                {
                    var a = walkmesh.Triangles[newAnchor];
                    var dx = CentroidX(a) - t.SourceX;
                    var dy = CentroidY(a) - t.SourceY;
                    planar = Math.Sqrt(dx * dx + dy * dy);
                }

                Console.WriteLine(
                    $"    {t.StableId} src=({t.SourceX},{t.SourceY},{t.SourceZ}) " +
                    $"was tri {oldAnchor} gap={gap:F0} -> now tri {newAnchor} gap={newGap:F0} " +
                    $"planarToCentroid={planar:F0}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            $"scanned {scanned} fields with transitions; " +
            $"{affectedFields} fields had wrong-storey anchors, {affectedTransitions} transitions total.");
    }
}

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Applies verified, field-specific presentation to native exits without
/// changing their reachability or treating common destinations as duplicates.
/// </summary>
public sealed class FieldExitPresentationPolicy
{
    private const string KalmWorldMapGatewayA = "gateway:335:9:2";
    private const string KalmWorldMapGatewayB = "gateway:335:10:2";
    private const string ChocoboFarmWorldMapGateway = "gateway:343:6:3";
    private readonly Func<bool?> readKalmCompletion;

    public FieldExitPresentationPolicy(Func<bool?> readKalmCompletion)
    {
        this.readKalmCompletion = readKalmCompletion ?? throw new ArgumentNullException(nameof(readKalmCompletion));
    }

    public IReadOnlyList<FieldNavigationTarget> Apply(IReadOnlyList<FieldNavigationTarget> targets)
    {
        if (targets.Count == 0)
        {
            return targets;
        }

        var hasKalmBoundary = targets.Any(IsKalmWorldMapGateway);
        var kalmComplete = hasKalmBoundary ? TryReadKalmCompletion() : null;
        var visible = new List<FieldNavigationTarget>(targets.Count);
        var addedWorldMapExit = false;
        foreach (var target in targets)
        {
            if (IsChocoboFarmWorldMapGateway(target))
            {
                if (target.StableId == ChocoboFarmWorldMapGateway)
                {
                    visible.Add(target with { Label = "Leave Chocobo Farm for World Map" });
                }

                continue;
            }

            if (!hasKalmBoundary || !IsKalmWorldMapGateway(target))
            {
                visible.Add(target);
                continue;
            }

            if (kalmComplete != true || addedWorldMapExit)
            {
                continue;
            }

            visible.Add(target with { Label = "Leave Kalm for the World Map" });
            addedWorldMapExit = true;
        }

        return MergeContiguousBoundarySegments(visible);
    }


    /// <summary>
    /// One field boundary is often stored as a chain of gateway records rather than a single
    /// one. The Kalm and Chocobo Farm world-map edges above were collapsed by hand for exactly
    /// that reason; there are 46 more chains across the game - both Sector 7 Station doorways,
    /// the Junon airport walkway, the Upper and Lower Junon street ends, every Shinra Building
    /// lobby - and each was announced once per segment. The player heard the same exit two,
    /// three, even six times over with no way to tell the copies apart.
    ///
    /// Segments of one boundary share an endpoint. Genuinely separate doors to the same
    /// destination do not: Kalm's Materia and Weapon store fronts sit 285 units apart and are
    /// correctly announced separately. So contiguity is the test, not a distance guess.
    ///
    /// Only native gateways are chained. They come from a fixed-size table where splitting a
    /// boundary across records is a storage detail; script trigger lines are authored one at a
    /// time and each one means something.
    /// </summary>
    private static IReadOnlyList<FieldNavigationTarget> MergeContiguousBoundarySegments(
        IReadOnlyList<FieldNavigationTarget> targets)
    {
        var boundaries = new List<(int Destination, List<(int X, int Y, int Z)> Endpoints)>();
        var merged = new List<FieldNavigationTarget>(targets.Count);
        foreach (var target in targets)
        {
            if (!IsGatewaySegment(target, out var destination, out var line))
            {
                merged.Add(target);
                continue;
            }

            var ends = new[]
            {
                (line.StartX, line.StartY, line.StartZ),
                (line.EndX, line.EndY, line.EndZ)
            };
            var existing = boundaries.FirstOrDefault(boundary =>
                boundary.Destination == destination &&
                boundary.Endpoints.Any(known => ends.Contains(known)));
            if (existing.Endpoints is not null)
            {
                // Chained onto a boundary already announced. Absorb the endpoints so a third
                // segment joined only to this one still merges.
                existing.Endpoints.AddRange(ends);
                continue;
            }

            boundaries.Add((destination, new List<(int, int, int)>(ends)));
            merged.Add(target);
        }

        return merged.Count == targets.Count ? targets : merged;
    }

    private static bool IsGatewaySegment(
        FieldNavigationTarget target,
        out int destination,
        out FieldNavigationTriggerLine line)
    {
        destination = 0;
        line = default;
        if (target.Category != FieldNavigationCategory.Exits ||
            !target.StableId.StartsWith("gateway:", StringComparison.Ordinal) ||
            target.TriggerLine is not { } triggerLine ||
            target.DestinationFieldIds is not { Count: 1 } destinations)
        {
            return false;
        }

        destination = destinations[0];
        line = triggerLine;
        return true;
    }

    private bool? TryReadKalmCompletion()
    {
        try
        {
            return readKalmCompletion();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsKalmWorldMapGateway(FieldNavigationTarget target) =>
        target.StableId is KalmWorldMapGatewayA or KalmWorldMapGatewayB;

    private static bool IsChocoboFarmWorldMapGateway(FieldNavigationTarget target) =>
        target.StableId is
            "gateway:343:2:3" or
            "gateway:343:3:3" or
            "gateway:343:4:3" or
            "gateway:343:5:3" or
            ChocoboFarmWorldMapGateway or
            "gateway:343:7:3" or
            "gateway:343:8:3";
}

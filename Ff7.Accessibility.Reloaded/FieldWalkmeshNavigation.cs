namespace Ff7.Accessibility.Reloaded;

public readonly record struct FieldWalkmeshVertex(short X, short Y, short Z);

public readonly record struct FieldWalkmeshTriangle(
    int Index,
    FieldWalkmeshVertex Vertex0,
    FieldWalkmeshVertex Vertex1,
    FieldWalkmeshVertex Vertex2,
    short Adjacent0,
    short Adjacent1,
    short Adjacent2)
{
    public short GetAdjacentTriangle(int edgeIndex) => edgeIndex switch
    {
        0 => Adjacent0,
        1 => Adjacent1,
        2 => Adjacent2,
        _ => -1
    };

    public (FieldWalkmeshVertex Start, FieldWalkmeshVertex End) GetEdge(int edgeIndex) => edgeIndex switch
    {
        0 => (Vertex0, Vertex1),
        1 => (Vertex1, Vertex2),
        2 => (Vertex2, Vertex0),
        _ => throw new ArgumentOutOfRangeException(nameof(edgeIndex))
    };

    public (double X, double Y, double Z) GetCentroid() =>
        ((Vertex0.X + Vertex1.X + Vertex2.X) / 3d,
         (Vertex0.Y + Vertex1.Y + Vertex2.Y) / 3d,
         (Vertex0.Z + Vertex1.Z + Vertex2.Z) / 3d);
}

public sealed class FieldWalkmesh
{
    public FieldWalkmesh(IReadOnlyList<FieldWalkmeshTriangle> triangles)
    {
        Triangles = triangles;
    }

    public IReadOnlyList<FieldWalkmeshTriangle> Triangles { get; }
}

public readonly record struct FieldWalkmeshReadResult(
    bool IsUsable,
    FieldWalkmesh? Walkmesh,
    string Diagnostic)
{
    public static FieldWalkmeshReadResult Valid(FieldWalkmesh walkmesh, string diagnostic) =>
        new(true, walkmesh, diagnostic);

    public static FieldWalkmeshReadResult Invalid(string diagnostic) =>
        new(false, null, diagnostic);
}

public sealed class FieldWalkmeshReader
{
    public const int AddressFieldDataPtr = 0x00CFF594;
    public const int SectionOffsetsHeaderOffset = 6;
    public const int WalkmeshSectionIndex = 4;
    public const int VertexSize = 8;
    public const int TriangleSize = VertexSize * 3;
    public const int AccessSize = 6;
    public const int MaximumTriangleCount = 4096;

    private readonly Func<int, int> readInt32;
    private readonly Func<int, short> readInt16;
    private int cachedFieldId = -1;
    private int cachedFieldDataPointer;
    private FieldWalkmeshReadResult cachedResult;

    public FieldWalkmeshReader(Func<int, int> readInt32, Func<int, short> readInt16)
    {
        this.readInt32 = readInt32;
        this.readInt16 = readInt16;
    }

    public FieldWalkmeshReadResult Read(FieldPositionSnapshot position)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            return FieldWalkmeshReadResult.Invalid("not in field module");
        }

        var fieldDataPointer = readInt32(AddressFieldDataPtr);
        if (fieldDataPointer == 0)
        {
            return FieldWalkmeshReadResult.Invalid($"field={position.FieldId}, field data pointer is null");
        }

        if (cachedFieldId == position.FieldId && cachedFieldDataPointer == fieldDataPointer)
        {
            return cachedResult;
        }

        var result = ReadWalkmesh(position.FieldId, fieldDataPointer);
        if (!result.IsUsable)
        {
            cachedFieldId = -1;
            cachedFieldDataPointer = 0;
            cachedResult = default;
            return result;
        }

        cachedFieldId = position.FieldId;
        cachedFieldDataPointer = fieldDataPointer;
        cachedResult = result;
        return result;
    }

    private FieldWalkmeshReadResult ReadWalkmesh(int fieldId, int fieldDataPointer)
    {
        var sectionEntry = fieldDataPointer + SectionOffsetsHeaderOffset + WalkmeshSectionIndex * sizeof(int);
        var sectionOffset = readInt32(sectionEntry);
        var nextSectionOffset = readInt32(sectionEntry + sizeof(int));
        if (sectionOffset < 0 || nextSectionOffset <= sectionOffset + sizeof(int))
        {
            return FieldWalkmeshReadResult.Invalid(
                $"field={fieldId}, invalid walkmesh section offsets {sectionOffset}/{nextSectionOffset}");
        }

        var payloadAddress64 = (long)fieldDataPointer + sectionOffset + sizeof(int);
        if (payloadAddress64 < int.MinValue || payloadAddress64 > int.MaxValue)
        {
            return FieldWalkmeshReadResult.Invalid($"field={fieldId}, walkmesh payload address overflow");
        }

        var payloadAddress = (int)payloadAddress64;
        var triangleCount = readInt32(payloadAddress);
        if (triangleCount <= 0 || triangleCount > MaximumTriangleCount)
        {
            return FieldWalkmeshReadResult.Invalid(
                $"field={fieldId}, invalid walkmesh triangle count {triangleCount}");
        }

        var requiredPayloadBytes = sizeof(int) + (long)triangleCount * (TriangleSize + AccessSize);
        var availablePayloadBytes = (long)nextSectionOffset - sectionOffset - sizeof(int);
        if (requiredPayloadBytes > availablePayloadBytes)
        {
            return FieldWalkmeshReadResult.Invalid(
                $"field={fieldId}, truncated walkmesh payload requires {requiredPayloadBytes}, has {availablePayloadBytes}");
        }

        var trianglesBase = payloadAddress + sizeof(int);
        var accessBase = trianglesBase + triangleCount * TriangleSize;
        var triangles = new FieldWalkmeshTriangle[triangleCount];
        for (var index = 0; index < triangleCount; index++)
        {
            var triangleBase = trianglesBase + index * TriangleSize;
            var accessEntry = accessBase + index * AccessSize;
            triangles[index] = new FieldWalkmeshTriangle(
                index,
                ReadVertex(triangleBase),
                ReadVertex(triangleBase + VertexSize),
                ReadVertex(triangleBase + VertexSize * 2),
                readInt16(accessEntry),
                readInt16(accessEntry + sizeof(short)),
                readInt16(accessEntry + sizeof(short) * 2));
        }

        var walkmesh = new FieldWalkmesh(triangles);
        return FieldWalkmeshReadResult.Valid(
            walkmesh,
            $"field={fieldId}, data=0x{fieldDataPointer:X8}, walkmesh=0x{payloadAddress:X8}, triangles={triangleCount}");
    }

    private FieldWalkmeshVertex ReadVertex(int address) =>
        new(readInt16(address), readInt16(address + sizeof(short)), readInt16(address + sizeof(short) * 2));
}

public readonly record struct FieldNavigationRouteWaypoint(int X, int Y, int Z);

public readonly record struct FieldWalkmeshSegmentTrace(
    bool IsClear,
    int EndTriangle,
    IReadOnlyList<int> TraversedTriangles,
    FieldNavigationRouteWaypoint FurthestPoint,
    string Diagnostic);

public readonly record struct FieldWalkmeshOffMeshLink(
    int FromTriangle,
    int ToTriangle,
    FieldNavigationRouteWaypoint Entry,
    FieldNavigationRouteWaypoint Exit,
    string StableId,
    FieldNavigationTransitionKind? TransitionKind,
    FieldNavigationInput RequiredInput = FieldNavigationInput.None,
    bool RequiresAction = false);

public readonly record struct FieldNavigationRouteStep(
    FieldNavigationRouteWaypoint Waypoint,
    int RequiredPortalIndex,
    bool MustReach = false);

public readonly record struct FieldNavigationRoutePortal(
    int FromTriangle,
    int ToTriangle,
    FieldNavigationRouteWaypoint Left,
    FieldNavigationRouteWaypoint Right,
    FieldNavigationTransitionKind? TransitionKind = null,
    string TransitionId = "",
    FieldNavigationInput RequiredInput = FieldNavigationInput.None,
    FieldNavigationRouteWaypoint? TransitionExit = null,
    bool RequiresAction = false)
{
    public FieldNavigationRouteWaypoint Midpoint => new(
        (Left.X + Right.X) / 2,
        (Left.Y + Right.Y) / 2,
        (Left.Z + Right.Z) / 2);
}

public readonly record struct FieldNavigationRouteAction(
    FieldNavigationTransitionKind Kind,
    string StableId,
    FieldNavigationRouteWaypoint Waypoint,
    FieldNavigationInput RequiredInput,
    FieldNavigationRouteWaypoint Destination = default,
    int DestinationTriangle = -1,
    bool RequiresAction = false,
    int PortalIndex = -1);

public sealed record FieldNavigationRoutePlan(
    int FieldId,
    string TargetId,
    IReadOnlyList<int> TrianglePath,
    IReadOnlyList<FieldNavigationRoutePortal> Portals,
    FieldNavigationRouteWaypoint FinalApproach,
    int TargetTriangle,
    double FinalApproachToTargetDistance = 0d,
    FieldNavigationTriggerLine? TargetTriggerLine = null,
    IReadOnlyList<FieldNavigationRouteStep>? StableWaypointsOverride = null);

public interface IFieldNavigationRoutePlanner
{
    bool TryResolvePlayerTriangle(
        FieldPositionSnapshot position,
        out int triangle);

    bool TryBuildRoute(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        out FieldNavigationRoutePlan plan);

    bool TryGetNextWaypoint(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        out FieldNavigationRouteWaypoint waypoint);

    string LastDiagnostic { get; }
}

public interface IFieldNavigationRouteRefreshPlanner
{
    bool TryBuildRouteFromCurrentTriangle(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        int resolvedTriangle,
        out FieldNavigationRoutePlan plan);
}

/// <summary>
/// Optional status surface for planners that read live native state. A false
/// route result can mean either a coherent blocked path or an unreadable/torn
/// native snapshot; consumers that must retain user input need that distinction.
/// </summary>
public interface IFieldNavigationRouteReadStatus
{
    bool LastReadWasCoherent { get; }
}

public sealed class FieldWalkmeshRoutePlanner :
    IFieldNavigationRoutePlanner,
    IFieldNavigationRouteRefreshPlanner,
    IFieldNavigationCorridorLookaheadPlanner,
    IFieldNavigationRouteReadStatus
{
    private const int LadderPairMaximumEndpointDistance = 192;

    /// <summary>
    /// How far a scripted trigger's authored elevation may sit from the triangle
    /// the planner anchors it to. Matches the separation the corridor lookahead
    /// already treats as "a different storey".
    /// </summary>
    private const double OffMeshLinkMaximumAnchorElevationError = 192d;
    private const int NativeInteractionVerticalRange = 256;
    private const double InteractionApproachInset = 8d;
    private const double InteractionRangeEpsilon = 0.5d;

    private readonly FieldWalkmeshReader reader;
    private readonly FieldBoundaryStateReader? boundaryStateReader;
    private readonly Func<int, IReadOnlyList<FieldScriptNavigationTransition>>? transitionProvider;
    private readonly Func<
        FieldPositionSnapshot,
        FieldNavigationTarget?,
        IReadOnlyList<FieldNavigationDynamicObstacle>>? dynamicObstacleProvider;
    private FieldNavigationTarget? activeTarget;

    public FieldWalkmeshRoutePlanner(
        FieldWalkmeshReader reader,
        FieldBoundaryStateReader? boundaryStateReader = null,
        Func<int, IReadOnlyList<FieldScriptNavigationTransition>>? transitionProvider = null,
        Func<
            FieldPositionSnapshot,
            FieldNavigationTarget?,
            IReadOnlyList<FieldNavigationDynamicObstacle>>? dynamicObstacleProvider = null)
    {
        this.reader = reader;
        this.boundaryStateReader = boundaryStateReader;
        this.transitionProvider = transitionProvider;
        this.dynamicObstacleProvider = dynamicObstacleProvider;
    }

    public string LastDiagnostic { get; private set; } = string.Empty;

    public bool LastReadWasCoherent { get; private set; } = true;

    public bool TryResolvePlayerTriangle(
        FieldPositionSnapshot position,
        out int triangle)
    {
        triangle = -1;
        LastReadWasCoherent = true;
        var result = reader.Read(position);
        if (!result.IsUsable || result.Walkmesh is null)
        {
            LastReadWasCoherent = false;
            LastDiagnostic = result.Diagnostic;
            return false;
        }

        triangle = FieldWalkmeshPathfinder.ResolveTriangle(
            result.Walkmesh,
            position.X,
            position.Y,
            position.Z,
            preferredTriangleIndex: -1);
        LastDiagnostic = triangle >= 0
            ? $"{result.Diagnostic}, nativeTriangle={position.TriangleId}, resolvedTriangle={triangle}"
            : $"{result.Diagnostic}, no geometric triangle at {position.X},{position.Y},{position.Z}";
        return triangle >= 0;
    }

    public bool TryBuildRoute(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        out FieldNavigationRoutePlan plan)
    {
        plan = null!;
        LastReadWasCoherent = true;
        if (position.FieldId != target.FieldId)
        {
            LastDiagnostic = $"field mismatch player={position.FieldId}, target={target.FieldId}";
            return false;
        }

        var result = reader.Read(position);
        if (!result.IsUsable || result.Walkmesh is null)
        {
            LastReadWasCoherent = false;
            LastDiagnostic = result.Diagnostic;
            return false;
        }

        if (!TryReadBoundaryState(position, result.Walkmesh, out var boundaryState, out var boundaryDiagnostic))
        {
            LastReadWasCoherent = false;
            LastDiagnostic = $"{result.Diagnostic}, {boundaryDiagnostic}";
            return false;
        }

        var playerTriangle = FieldWalkmeshPathfinder.ResolveTriangle(
            result.Walkmesh,
            position.X,
            position.Y,
            position.Z,
            preferredTriangleIndex: -1);
        if (playerTriangle < 0)
        {
            LastDiagnostic = $"{result.Diagnostic}, no geometric player triangle at {position.X},{position.Y},{position.Z}";
            return false;
        }

        var scriptedOffMeshLinks = ResolveOffMeshLinks(position.FieldId, result.Walkmesh);
        var targetRouteLinks = ResolveTargetRouteLinks(target);
        IReadOnlyList<FieldWalkmeshOffMeshLink> offMeshLinks =
            targetRouteLinks.Count == 0
                ? scriptedOffMeshLinks
                : [.. scriptedOffMeshLinks, .. targetRouteLinks];
        var finalApproach = new FieldNavigationRouteWaypoint(target.X, target.Y, target.Z);
        var finalApproachToTargetDistance = 0d;
        var usedInteractionApproach = false;
        var usedTriggerLineApproach = false;
        var triggerLineDistance = double.PositiveInfinity;
        var usedRouteDetour = false;
        var routeDetourFailed = false;
        var routeDetourDiagnostic = string.Empty;
        var dynamicModelRouteBlocked = false;
        var usedDynamicModelDetour = false;
        var dynamicModelDiagnostic = string.Empty;
        IReadOnlyList<FieldNavigationRouteDetour> appliedRouteDetours =
            Array.Empty<FieldNavigationRouteDetour>();
        IReadOnlyList<FieldNavigationRouteStep>? stableWaypointsOverride = null;
        IReadOnlyList<int> trianglePath = Array.Empty<int>();
        IReadOnlyList<FieldNavigationRoutePortal> portals = Array.Empty<FieldNavigationRoutePortal>();
        var targetTriangle = -1;
        var found = target.TriggerLine is { } triggerLine &&
                    TryBuildTriggerLineRoute(
                        result.Walkmesh,
                        playerTriangle,
                        position,
                        triggerLine,
                        boundaryState.IsBoundaryEnabled,
                        offMeshLinks,
                        out trianglePath,
                        out portals,
                        out targetTriangle,
                        out finalApproach,
                        out triggerLineDistance);
        usedTriggerLineApproach = found;
        if (!found)
        {
            found = FieldWalkmeshPathfinder.TryBuildRoute(
                result.Walkmesh,
                playerTriangle,
                position.X,
                position.Y,
                position.Z,
                target.X,
                target.Y,
                target.Z,
                isTriangleBlocked: boundaryState.IsBoundaryEnabled,
                offMeshLinks,
                out trianglePath,
                out portals,
                out targetTriangle);
        }

        if (!found &&
            target.InteractionRadius > 0 &&
            TryBuildInteractionRangeRoute(
                result.Walkmesh,
                playerTriangle,
                position,
                target,
                boundaryState.IsBoundaryEnabled,
                offMeshLinks,
                out trianglePath,
                out portals,
                out targetTriangle,
                out finalApproach,
                out finalApproachToTargetDistance))
        {
            found = true;
            usedInteractionApproach = true;
        }

        var routeDetours = ResolveRouteDetours(target);
        if (found && routeDetours.Count != 0)
        {
            if (TryBuildRouteViaDetours(
                    result.Walkmesh,
                    playerTriangle,
                    position,
                    finalApproach,
                    routeDetours,
                    boundaryState.IsBoundaryEnabled,
                    offMeshLinks,
                    out var detourTrianglePath,
                    out var detourPortals,
                    out var detourTargetTriangle,
                    out var detourWaypoints,
                    out appliedRouteDetours,
                    out routeDetourDiagnostic))
            {
                if (appliedRouteDetours.Count != 0)
                {
                    trianglePath = detourTrianglePath;
                    portals = detourPortals;
                    targetTriangle = detourTargetTriangle;
                    stableWaypointsOverride = detourWaypoints;
                    usedRouteDetour = true;
                }
            }
            else
            {
                found = false;
                routeDetourFailed = true;
            }
        }

        if (found && dynamicObstacleProvider is not null)
        {
            var dynamicObstacles = dynamicObstacleProvider(position, target);
            var routeSteps = stableWaypointsOverride ??
                FieldWalkmeshPathfinder.BuildStableWaypoints(
                    position.X,
                    position.Y,
                    position.Z,
                    portals,
                    finalApproach);
            var current = new FieldNavigationRouteWaypoint(
                position.X,
                position.Y,
                position.Z);
            if (dynamicObstacles.Count > 0 &&
                routeSteps.Count > 0 &&
                FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                    current,
                    routeSteps[0].Waypoint,
                    dynamicObstacles))
            {
                var firstActionPortal = portals
                    .Select((portal, index) => (portal, index))
                    .Where(entry => entry.portal.TransitionKind is not null)
                    .Select(entry => entry.index)
                    .DefaultIfEmpty(int.MaxValue)
                    .Min();
                var routeHeading = new FieldNavigationRouteHeading(
                    true,
                    routeSteps[0].Waypoint.X - current.X,
                    routeSteps[0].Waypoint.Y - current.Y,
                    "initial native route heading");
                for (var index = routeSteps.Count - 1; index >= 0; index--)
                {
                    var step = routeSteps[index];
                    if (step.RequiredPortalIndex > firstActionPortal ||
                        !FieldNavigationDynamicObstacleGeometry.IntersectsAny(
                            current,
                            step.Waypoint,
                            dynamicObstacles) ||
                        !FieldNavigationCorridorLookahead.TryResolveDynamicObstacleDetour(
                            result.Walkmesh,
                            playerTriangle,
                            current,
                            step.Waypoint,
                            index,
                            routeHeading,
                            boundaryState.IsBoundaryEnabled,
                            dynamicObstacles,
                            out var recovery))
                    {
                        continue;
                    }

                    stableWaypointsOverride =
                    [
                        new FieldNavigationRouteStep(
                            recovery.Waypoint,
                            RequiredPortalIndex: 0,
                            MustReach: true),
                        .. routeSteps
                    ];
                    usedDynamicModelDetour = true;
                    dynamicModelDiagnostic = recovery.Diagnostic;
                    break;
                }

                if (!usedDynamicModelDetour)
                {
                    found = false;
                    dynamicModelRouteBlocked = true;
                    dynamicModelDiagnostic =
                        $"live model blocks route at {routeSteps[0].Waypoint.X}," +
                        $"{routeSteps[0].Waypoint.Y},{routeSteps[0].Waypoint.Z}";
                }
            }
        }

        LastDiagnostic = found
            ? $"{result.Diagnostic}, {boundaryDiagnostic}, nativeTriangle={position.TriangleId}, resolvedTriangle={playerTriangle}, targetTriangle={targetTriangle}, " +
              $"routeTriangles={trianglePath.Count}, portals={portals.Count}, offMeshLinks={offMeshLinks.Count}" +
              (usedTriggerLineApproach
                  ? $", trigger line approach={finalApproach.X},{finalApproach.Y},{finalApproach.Z}, " +
                    $"native trigger distance={triggerLineDistance:0.0}"
                  : usedInteractionApproach
                  ? $", interaction approach={finalApproach.X},{finalApproach.Y},{finalApproach.Z}, " +
                    $"targetDistance={finalApproachToTargetDistance:0.0}"
                  : string.Empty) +
              (usedRouteDetour
                  ? $", safe detours={string.Join(";", appliedRouteDetours.Select(detour => $"{detour.X},{detour.Y},{detour.Z}"))}"
                  : string.Empty) +
              (usedDynamicModelDetour
                  ? $", {dynamicModelDiagnostic}"
                  : string.Empty)
            : $"{result.Diagnostic}, {boundaryDiagnostic}, no route from resolved triangle {playerTriangle} to {target.Label}" +
              (routeDetourFailed
                  ? $", native hazard detour unavailable{(string.IsNullOrWhiteSpace(routeDetourDiagnostic) ? string.Empty : $": {routeDetourDiagnostic}")}"
                  : string.Empty) +
              (dynamicModelRouteBlocked
                  ? $", {dynamicModelDiagnostic}"
                  : string.Empty);
        if (!found)
        {
            return false;
        }

        plan = new FieldNavigationRoutePlan(
            position.FieldId,
            GetTargetId(target),
            trianglePath,
            portals,
            finalApproach,
            targetTriangle,
            finalApproachToTargetDistance,
            target.TriggerLine,
            stableWaypointsOverride);
        activeTarget = target;
        return true;
    }

    private static IReadOnlyList<FieldWalkmeshOffMeshLink> ResolveTargetRouteLinks(
        FieldNavigationTarget target)
    {
        var secondWallClimbSocket = new FieldNavigationTriggerLine(
            -25,
            997,
            2249,
            -118,
            1071,
            2311);
        if (target.FieldId != 223 ||
            target.Category != FieldNavigationCategory.Story ||
            target.TriggerLine != secondWallClimbSocket)
        {
            return Array.Empty<FieldWalkmeshOffMeshLink>();
        }

        // Installing the first battery stops the propeller and turns it into
        // the visible bridge between these two native walkmesh edges. The
        // background model is not represented in the field's static adjacency
        // table, so preserve that native crossing only for the state-gated
        // second-socket target. It is ordinary walking, not a jump or ladder.
        return
        [
            new FieldWalkmeshOffMeshLink(
                166,
                82,
                new FieldNavigationRouteWaypoint(235, 1155, 1943),
                new FieldNavigationRouteWaypoint(183, 1181, 1879),
                "walkway:223:first-battery-propeller",
                TransitionKind: null)
        ];
    }

    private static IReadOnlyList<FieldNavigationRouteDetour> ResolveRouteDetours(
        FieldNavigationTarget target)
    {
        if (target.RouteDetours is { Count: > 0 })
        {
            return target.RouteDetours;
        }

        return target.RouteDetour is { } routeDetour
            ? [routeDetour]
            : Array.Empty<FieldNavigationRouteDetour>();
    }

    private static bool TryBuildRouteViaDetours(
        FieldWalkmesh walkmesh,
        int playerTriangle,
        FieldPositionSnapshot position,
        FieldNavigationRouteWaypoint finalApproach,
        IReadOnlyList<FieldNavigationRouteDetour> detours,
        Func<int, bool>? isTriangleBlocked,
        IReadOnlyList<FieldWalkmeshOffMeshLink> offMeshLinks,
        out IReadOnlyList<int> combinedTrianglePath,
        out IReadOnlyList<FieldNavigationRoutePortal> combinedPortals,
        out int finalTargetTriangle,
        out IReadOnlyList<FieldNavigationRouteStep> stableWaypoints,
        out IReadOnlyList<FieldNavigationRouteDetour> appliedDetours,
        out string diagnostic)
    {
        combinedTrianglePath = Array.Empty<int>();
        combinedPortals = Array.Empty<FieldNavigationRoutePortal>();
        finalTargetTriangle = -1;
        stableWaypoints = Array.Empty<FieldNavigationRouteStep>();
        appliedDetours = Array.Empty<FieldNavigationRouteDetour>();
        diagnostic = string.Empty;
        var requiredDetours = new List<FieldNavigationRouteDetour>();
        var currentStart = new FieldNavigationRouteWaypoint(position.X, position.Y, position.Z);
        var currentTriangle = playerTriangle;
        foreach (var detour in detours)
        {
            if (!FieldWalkmeshPathfinder.TryBuildRoute(
                    walkmesh,
                    currentTriangle,
                    currentStart.X,
                    currentStart.Y,
                    currentStart.Z,
                    finalApproach.X,
                    finalApproach.Y,
                    finalApproach.Z,
                    isTriangleBlocked,
                    offMeshLinks,
                    out _,
                    out var remainingPortals,
                    out _) ||
                !RouteCrossesBlockedLine(
                    currentStart,
                    remainingPortals,
                    finalApproach,
                    detour.BlockedLine,
                    detour.Clearance))
            {
                continue;
            }

            if (!FieldWalkmeshPathfinder.TryBuildRoute(
                    walkmesh,
                    currentTriangle,
                    currentStart.X,
                    currentStart.Y,
                    currentStart.Z,
                    detour.X,
                    detour.Y,
                    detour.Z,
                    isTriangleBlocked,
                    offMeshLinks,
                    out _,
                    out _,
                    out var detourTriangle))
            {
                diagnostic = $"cannot reach checkpoint {detour.X},{detour.Y},{detour.Z}";
                return false;
            }

            requiredDetours.Add(detour);
            currentStart = new FieldNavigationRouteWaypoint(detour.X, detour.Y, detour.Z);
            currentTriangle = detourTriangle;
        }

        if (requiredDetours.Count == 0)
        {
            return true;
        }

        var trianglePath = new List<int>();
        var portals = new List<FieldNavigationRoutePortal>();
        var steps = new List<FieldNavigationRouteStep>();
        currentStart = new FieldNavigationRouteWaypoint(position.X, position.Y, position.Z);
        currentTriangle = playerTriangle;
        for (var legIndex = 0; legIndex <= requiredDetours.Count; legIndex++)
        {
            var isDetourLeg = legIndex < requiredDetours.Count;
            var legTarget = isDetourLeg
                ? new FieldNavigationRouteWaypoint(
                    requiredDetours[legIndex].X,
                    requiredDetours[legIndex].Y,
                    requiredDetours[legIndex].Z)
                : finalApproach;
            if (!FieldWalkmeshPathfinder.TryBuildRoute(
                    walkmesh,
                    currentTriangle,
                    currentStart.X,
                    currentStart.Y,
                    currentStart.Z,
                    legTarget.X,
                    legTarget.Y,
                    legTarget.Z,
                    isTriangleBlocked,
                    offMeshLinks,
                    out var legTrianglePath,
                    out var legPortals,
                    out var legTargetTriangle))
            {
                diagnostic = $"cannot build leg {currentStart} to {legTarget}";
                return false;
            }

            var triangleStartIndex =
                trianglePath.Count > 0 &&
                legTrianglePath.Count > 0 &&
                trianglePath[^1] == legTrianglePath[0]
                    ? 1
                    : 0;
            for (var index = triangleStartIndex; index < legTrianglePath.Count; index++)
            {
                trianglePath.Add(legTrianglePath[index]);
            }

            var portalOffset = portals.Count;
            portals.AddRange(legPortals);
            var legSteps = FieldWalkmeshPathfinder.BuildStableWaypoints(
                currentStart.X,
                currentStart.Y,
                currentStart.Z,
                legPortals,
                legTarget);
            for (var stepIndex = 0; stepIndex < legSteps.Count; stepIndex++)
            {
                var step = legSteps[stepIndex];
                steps.Add(step with
                {
                    RequiredPortalIndex = step.RequiredPortalIndex + portalOffset,
                    MustReach =
                        step.MustReach ||
                        (isDetourLeg && stepIndex == legSteps.Count - 1)
                });
            }

            currentStart = legTarget;
            currentTriangle = legTargetTriangle;
            finalTargetTriangle = legTargetTriangle;
        }

        foreach (var detour in requiredDetours)
        {
            if (TryFindBlockedRouteSegment(
                    new FieldNavigationRouteWaypoint(position.X, position.Y, position.Z),
                    steps,
                    detour.BlockedLine,
                    detour.Clearance,
                    out var unsafeStart,
                    out var unsafeEnd))
            {
                diagnostic =
                    $"checkpoint route enters catch capsule {detour.BlockedLine} " +
                    $"within clearance {detour.Clearance} on {unsafeStart} to {unsafeEnd}";
                return false;
            }
        }

        combinedTrianglePath = trianglePath;
        combinedPortals = portals;
        stableWaypoints = steps;
        appliedDetours = requiredDetours;
        return true;
    }

    private static bool RouteCrossesBlockedLine(
        FieldNavigationRouteWaypoint routeStart,
        IReadOnlyList<FieldNavigationRoutePortal> portals,
        FieldNavigationRouteWaypoint finalApproach,
        FieldNavigationTriggerLine blockedLine,
        int clearance)
    {
        var steps = FieldWalkmeshPathfinder.BuildStableWaypoints(
            routeStart.X,
            routeStart.Y,
            routeStart.Z,
            portals,
            finalApproach);
        return RouteStepsCrossBlockedLine(routeStart, steps, blockedLine, clearance);
    }

    private static bool RouteStepsCrossBlockedLine(
        FieldNavigationRouteWaypoint routeStart,
        IReadOnlyList<FieldNavigationRouteStep> steps,
        FieldNavigationTriggerLine blockedLine,
        int clearance = 0)
        => TryFindBlockedRouteSegment(
            routeStart,
            steps,
            blockedLine,
            clearance,
            out _,
            out _);

    private static bool TryFindBlockedRouteSegment(
        FieldNavigationRouteWaypoint routeStart,
        IReadOnlyList<FieldNavigationRouteStep> steps,
        FieldNavigationTriggerLine blockedLine,
        int clearance,
        out FieldNavigationRouteWaypoint unsafeStart,
        out FieldNavigationRouteWaypoint unsafeEnd)
    {
        var previous = routeStart;
        foreach (var step in steps)
        {
            if (SegmentsWithinClearance2D(previous, step.Waypoint, blockedLine, clearance))
            {
                unsafeStart = previous;
                unsafeEnd = step.Waypoint;
                return true;
            }

            previous = step.Waypoint;
        }

        unsafeStart = default;
        unsafeEnd = default;
        return false;
    }

    private static bool SegmentsWithinClearance2D(
        FieldNavigationRouteWaypoint routeStart,
        FieldNavigationRouteWaypoint routeEnd,
        FieldNavigationTriggerLine blockedLine,
        int clearance)
    {
        // Legacy FUN_00637ABB treats LINE triggers as capsules: it compares the
        // squared point-to-segment distance with the player event's +0x72 radius.
        // A route can therefore trigger a catch before its center crosses the line.
        if (SegmentsIntersect2D(routeStart, routeEnd, blockedLine))
        {
            return true;
        }

        if (clearance <= 0)
        {
            return false;
        }

        var blockedStart = new FieldNavigationRouteWaypoint(
            blockedLine.StartX,
            blockedLine.StartY,
            blockedLine.StartZ);
        var blockedEnd = new FieldNavigationRouteWaypoint(
            blockedLine.EndX,
            blockedLine.EndY,
            blockedLine.EndZ);
        var distance = Math.Min(
            Math.Min(
                PointSegmentDistance2D(routeStart, blockedStart, blockedEnd),
                PointSegmentDistance2D(routeEnd, blockedStart, blockedEnd)),
            Math.Min(
                PointSegmentDistance2D(blockedStart, routeStart, routeEnd),
                PointSegmentDistance2D(blockedEnd, routeStart, routeEnd)));
        return distance < clearance;
    }

    private static double PointSegmentDistance2D(
        FieldNavigationRouteWaypoint point,
        FieldNavigationRouteWaypoint segmentStart,
        FieldNavigationRouteWaypoint segmentEnd)
    {
        var segmentX = segmentEnd.X - segmentStart.X;
        var segmentY = segmentEnd.Y - segmentStart.Y;
        var lengthSquared = segmentX * (double)segmentX + segmentY * (double)segmentY;
        if (lengthSquared <= 0d)
        {
            return Math.Sqrt(
                Math.Pow(point.X - segmentStart.X, 2d) +
                Math.Pow(point.Y - segmentStart.Y, 2d));
        }

        var amount = Math.Clamp(
            ((point.X - segmentStart.X) * segmentX +
             (point.Y - segmentStart.Y) * segmentY) /
            lengthSquared,
            0d,
            1d);
        var closestX = segmentStart.X + amount * segmentX;
        var closestY = segmentStart.Y + amount * segmentY;
        return Math.Sqrt(
            Math.Pow(point.X - closestX, 2d) +
            Math.Pow(point.Y - closestY, 2d));
    }

    private static bool SegmentsIntersect2D(
        FieldNavigationRouteWaypoint routeStart,
        FieldNavigationRouteWaypoint routeEnd,
        FieldNavigationTriggerLine blockedLine)
    {
        const double epsilon = 0.000001d;
        var routeX = routeEnd.X - routeStart.X;
        var routeY = routeEnd.Y - routeStart.Y;
        var blockedX = blockedLine.EndX - blockedLine.StartX;
        var blockedY = blockedLine.EndY - blockedLine.StartY;
        var offsetX = blockedLine.StartX - routeStart.X;
        var offsetY = blockedLine.StartY - routeStart.Y;
        var denominator = Cross(routeX, routeY, blockedX, blockedY);
        if (Math.Abs(denominator) > epsilon)
        {
            var routeAmount = Cross(offsetX, offsetY, blockedX, blockedY) / denominator;
            var blockedAmount = Cross(offsetX, offsetY, routeX, routeY) / denominator;
            return routeAmount >= -epsilon &&
                   routeAmount <= 1d + epsilon &&
                   blockedAmount >= -epsilon &&
                   blockedAmount <= 1d + epsilon;
        }

        if (Math.Abs(Cross(offsetX, offsetY, routeX, routeY)) > epsilon)
        {
            return false;
        }

        var useX = Math.Abs(routeX) >= Math.Abs(routeY);
        var routeMinimum = useX
            ? Math.Min(routeStart.X, routeEnd.X)
            : Math.Min(routeStart.Y, routeEnd.Y);
        var routeMaximum = useX
            ? Math.Max(routeStart.X, routeEnd.X)
            : Math.Max(routeStart.Y, routeEnd.Y);
        var blockedMinimum = useX
            ? Math.Min(blockedLine.StartX, blockedLine.EndX)
            : Math.Min(blockedLine.StartY, blockedLine.EndY);
        var blockedMaximum = useX
            ? Math.Max(blockedLine.StartX, blockedLine.EndX)
            : Math.Max(blockedLine.StartY, blockedLine.EndY);
        return routeMaximum >= blockedMinimum - epsilon &&
               blockedMaximum >= routeMinimum - epsilon;
    }

    public bool TryBuildRouteFromCurrentTriangle(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        int resolvedTriangle,
        out FieldNavigationRoutePlan plan)
    {
        if (!TryBuildRoute(position, target, out plan))
        {
            return false;
        }

        return plan.TrianglePath.Count > 0 &&
               plan.TrianglePath[0] == resolvedTriangle;
    }

    public bool TryObserveCorridor(
        FieldPositionSnapshot position,
        FieldNavigationRoutePlan plan,
        IReadOnlyList<FieldNavigationRouteStep> stableWaypoints,
        int waypointIndex,
        FieldNavigationRouteAction? nextAction,
        FieldNavigationRouteHeading heading,
        out FieldNavigationCorridorObservation observation)
    {
        observation = default;
        LastReadWasCoherent = true;
        if (position.FieldId != plan.FieldId)
        {
            LastDiagnostic = $"corridor field mismatch player={position.FieldId}, route={plan.FieldId}";
            return false;
        }

        var result = reader.Read(position);
        if (!result.IsUsable || result.Walkmesh is null)
        {
            LastReadWasCoherent = false;
            LastDiagnostic = result.Diagnostic;
            return false;
        }

        if (!TryReadBoundaryState(position, result.Walkmesh, out var boundaryState, out var boundaryDiagnostic))
        {
            LastReadWasCoherent = false;
            LastDiagnostic = $"{result.Diagnostic}, {boundaryDiagnostic}";
            return false;
        }

        var resolvedTriangle = FieldWalkmeshPathfinder.ResolveTriangle(
            result.Walkmesh,
            position.X,
            position.Y,
            position.Z,
            preferredTriangleIndex: -1);
        if (resolvedTriangle < 0)
        {
            LastDiagnostic =
                $"{result.Diagnostic}, no geometric player triangle at {position.X},{position.Y},{position.Z}";
            return false;
        }

        FieldNavigationTarget? matchingTarget = activeTarget is { } target &&
                                                GetTargetId(target) == plan.TargetId
            ? target
            : null;
        var dynamicObstacles = dynamicObstacleProvider?.Invoke(
            position,
            matchingTarget);
        var found = FieldNavigationCorridorLookahead.TryResolve(
            result.Walkmesh,
            resolvedTriangle,
            position,
            plan,
            stableWaypoints,
            waypointIndex,
            nextAction,
            heading,
            boundaryState.IsBoundaryEnabled,
            dynamicObstacles,
            out observation);
        LastDiagnostic = found
            ? $"{result.Diagnostic}, {boundaryDiagnostic}, dynamicModels={dynamicObstacles?.Count ?? 0}, {observation.Diagnostic}"
            : $"{result.Diagnostic}, {boundaryDiagnostic}, corridor observation unavailable";
        return found;
    }

    private static bool TryBuildTriggerLineRoute(
        FieldWalkmesh walkmesh,
        int playerTriangle,
        FieldPositionSnapshot position,
        FieldNavigationTriggerLine triggerLine,
        Func<int, bool>? isTriangleBlocked,
        IReadOnlyList<FieldWalkmeshOffMeshLink> offMeshLinks,
        out IReadOnlyList<int> bestTrianglePath,
        out IReadOnlyList<FieldNavigationRoutePortal> bestPortals,
        out int bestTargetTriangle,
        out FieldNavigationRouteWaypoint bestApproach,
        out double bestTriggerLineDistance)
    {
        bestTrianglePath = Array.Empty<int>();
        bestPortals = Array.Empty<FieldNavigationRoutePortal>();
        bestTargetTriangle = -1;
        bestApproach = default;
        bestTriggerLineDistance = double.PositiveInfinity;
        var bestRouteDistance = double.PositiveInfinity;
        var bestTriggerLineDistanceSquared = double.PositiveInfinity;
        var found = false;

        for (var triangleIndex = 0; triangleIndex < walkmesh.Triangles.Count; triangleIndex++)
        {
            if (isTriangleBlocked?.Invoke(triangleIndex) == true ||
                !TryFindClosestPointOnTriggerLineInTriangle(
                    walkmesh.Triangles[triangleIndex],
                    triggerLine,
                    position.X,
                    position.Y,
                    out var candidate))
            {
                continue;
            }

            var x = (int)Math.Round(candidate.X);
            var y = (int)Math.Round(candidate.Y);
            var z = (int)Math.Round(InterpolateTriangleZ(walkmesh.Triangles[triangleIndex], x, y));
            var approach = new FieldNavigationRouteWaypoint(x, y, z);
            if (CalculateVerticalDistanceToTriggerLine(approach, triggerLine) >=
                NativeInteractionVerticalRange)
            {
                continue;
            }

            if (!FieldWalkmeshPathfinder.TryBuildRoute(
                    walkmesh,
                    playerTriangle,
                    position.X,
                    position.Y,
                    position.Z,
                    approach.X,
                    approach.Y,
                    approach.Z,
                    isTriangleBlocked,
                    offMeshLinks,
                    out var trianglePath,
                    out var portals,
                    out var resolvedTargetTriangle))
            {
                continue;
            }

            var routeDistance = CalculateRouteDistance(position, portals, approach);
            var triggerLineDistanceSquared =
                CalculateSquaredDistanceToTriggerLine(approach, triggerLine);
            if (triggerLineDistanceSquared > bestTriggerLineDistanceSquared + 0.001d ||
                (Math.Abs(triggerLineDistanceSquared - bestTriggerLineDistanceSquared) <= 0.001d &&
                 routeDistance >= bestRouteDistance - 0.001d))
            {
                continue;
            }

            found = true;
            bestRouteDistance = routeDistance;
            bestTriggerLineDistanceSquared = triggerLineDistanceSquared;
            bestTrianglePath = trianglePath;
            bestPortals = portals;
            bestTargetTriangle = resolvedTargetTriangle;
            bestApproach = approach;
        }

        if (found)
        {
            bestTriggerLineDistance = Math.Sqrt(bestTriggerLineDistanceSquared);
        }

        return found;
    }

    private static double CalculateVerticalDistanceToTriggerLine(
        FieldNavigationRouteWaypoint point,
        FieldNavigationTriggerLine line)
    {
        var lineX = (double)line.EndX - line.StartX;
        var lineY = (double)line.EndY - line.StartY;
        var lineLengthSquared = lineX * lineX + lineY * lineY;
        if (lineLengthSquared <= 0.0001d)
        {
            var minimumZ = Math.Min(line.StartZ, line.EndZ);
            var maximumZ = Math.Max(line.StartZ, line.EndZ);
            return point.Z < minimumZ
                ? minimumZ - point.Z
                : point.Z > maximumZ
                    ? point.Z - maximumZ
                    : 0d;
        }

        var amount = Math.Clamp(
            (((double)point.X - line.StartX) * lineX +
             ((double)point.Y - line.StartY) * lineY) /
            lineLengthSquared,
            0d,
            1d);
        var nativeZ = line.StartZ + ((double)line.EndZ - line.StartZ) * amount;
        return Math.Abs(point.Z - nativeZ);
    }

    private static double CalculateSquaredDistanceToTriggerLine(
        FieldNavigationRouteWaypoint point,
        FieldNavigationTriggerLine line)
    {
        var lineX = (double)line.EndX - line.StartX;
        var lineY = (double)line.EndY - line.StartY;
        var lineZ = (double)line.EndZ - line.StartZ;
        var lineLengthSquared = lineX * lineX + lineY * lineY + lineZ * lineZ;
        if (lineLengthSquared <= 0.0001d)
        {
            var pointX = (double)point.X - line.StartX;
            var pointY = (double)point.Y - line.StartY;
            var pointZ = (double)point.Z - line.StartZ;
            return pointX * pointX + pointY * pointY + pointZ * pointZ;
        }

        var amount = Math.Clamp(
            (((double)point.X - line.StartX) * lineX +
             ((double)point.Y - line.StartY) * lineY +
             ((double)point.Z - line.StartZ) * lineZ) /
            lineLengthSquared,
            0d,
            1d);
        var closestX = line.StartX + lineX * amount;
        var closestY = line.StartY + lineY * amount;
        var closestZ = line.StartZ + lineZ * amount;
        var distanceX = point.X - closestX;
        var distanceY = point.Y - closestY;
        var distanceZ = point.Z - closestZ;
        return
            distanceX * distanceX +
            distanceY * distanceY +
            distanceZ * distanceZ;
    }

    private static bool TryFindClosestPointOnTriggerLineInTriangle(
        FieldWalkmeshTriangle triangle,
        FieldNavigationTriggerLine line,
        int playerX,
        int playerY,
        out RouteCoordinate closest)
    {
        closest = default;
        var start = new RouteCoordinate(line.StartX, line.StartY);
        var end = new RouteCoordinate(line.EndX, line.EndY);
        var lineX = end.X - start.X;
        var lineY = end.Y - start.Y;
        var lineLengthSquared = lineX * lineX + lineY * lineY;
        if (lineLengthSquared <= 0.0001d)
        {
            if (!ContainsPoint2D(triangle, start.X, start.Y))
            {
                return false;
            }

            closest = start;
            return true;
        }

        var parameters = new List<double>(8);
        if (ContainsPoint2D(triangle, start.X, start.Y))
        {
            AddUniqueParameter(parameters, 0d);
        }

        if (ContainsPoint2D(triangle, end.X, end.Y))
        {
            AddUniqueParameter(parameters, 1d);
        }

        AddSegmentIntersectionParameters(parameters, start, end, triangle.Vertex0, triangle.Vertex1);
        AddSegmentIntersectionParameters(parameters, start, end, triangle.Vertex1, triangle.Vertex2);
        AddSegmentIntersectionParameters(parameters, start, end, triangle.Vertex2, triangle.Vertex0);
        if (parameters.Count == 0)
        {
            return false;
        }

        var minimum = parameters.Min();
        var maximum = parameters.Max();
        var clippedStart = new RouteCoordinate(
            start.X + lineX * minimum,
            start.Y + lineY * minimum);
        var clippedEnd = new RouteCoordinate(
            start.X + lineX * maximum,
            start.Y + lineY * maximum);
        var clippedX = clippedEnd.X - clippedStart.X;
        var clippedY = clippedEnd.Y - clippedStart.Y;
        var clippedLengthSquared = clippedX * clippedX + clippedY * clippedY;
        var amount = clippedLengthSquared <= 0.0001d
            ? 0d
            : Math.Clamp(
                ((playerX - clippedStart.X) * clippedX +
                 (playerY - clippedStart.Y) * clippedY) /
                clippedLengthSquared,
                0d,
                1d);
        closest = new RouteCoordinate(
            clippedStart.X + clippedX * amount,
            clippedStart.Y + clippedY * amount);
        return true;
    }

    private static void AddSegmentIntersectionParameters(
        ICollection<double> parameters,
        RouteCoordinate lineStart,
        RouteCoordinate lineEnd,
        FieldWalkmeshVertex edgeStart,
        FieldWalkmeshVertex edgeEnd)
    {
        const double epsilon = 0.000001d;
        var lineX = lineEnd.X - lineStart.X;
        var lineY = lineEnd.Y - lineStart.Y;
        var edgeX = edgeEnd.X - edgeStart.X;
        var edgeY = edgeEnd.Y - edgeStart.Y;
        var offsetX = edgeStart.X - lineStart.X;
        var offsetY = edgeStart.Y - lineStart.Y;
        var denominator = Cross(lineX, lineY, edgeX, edgeY);
        if (Math.Abs(denominator) > epsilon)
        {
            var lineAmount = Cross(offsetX, offsetY, edgeX, edgeY) / denominator;
            var edgeAmount = Cross(offsetX, offsetY, lineX, lineY) / denominator;
            if (lineAmount >= -epsilon && lineAmount <= 1d + epsilon &&
                edgeAmount >= -epsilon && edgeAmount <= 1d + epsilon)
            {
                AddUniqueParameter(parameters, Math.Clamp(lineAmount, 0d, 1d));
            }

            return;
        }

        if (Math.Abs(Cross(offsetX, offsetY, lineX, lineY)) > epsilon)
        {
            return;
        }

        var lineLengthSquared = lineX * lineX + lineY * lineY;
        if (lineLengthSquared <= epsilon)
        {
            return;
        }

        var first = (offsetX * lineX + offsetY * lineY) / lineLengthSquared;
        var second =
            ((edgeEnd.X - lineStart.X) * lineX +
             (edgeEnd.Y - lineStart.Y) * lineY) /
            lineLengthSquared;
        var overlapStart = Math.Max(0d, Math.Min(first, second));
        var overlapEnd = Math.Min(1d, Math.Max(first, second));
        if (overlapStart <= overlapEnd + epsilon)
        {
            AddUniqueParameter(parameters, Math.Clamp(overlapStart, 0d, 1d));
            AddUniqueParameter(parameters, Math.Clamp(overlapEnd, 0d, 1d));
        }
    }

    private static void AddUniqueParameter(ICollection<double> parameters, double value)
    {
        if (!parameters.Any(existing => Math.Abs(existing - value) <= 0.000001d))
        {
            parameters.Add(value);
        }
    }

    private static double Cross(double firstX, double firstY, double secondX, double secondY) =>
        firstX * secondY - firstY * secondX;

    public bool TryGetNextWaypoint(
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        out FieldNavigationRouteWaypoint waypoint)
    {
        waypoint = default;
        if (!TryBuildRoute(position, target, out var plan))
        {
            return false;
        }

        var steps = plan.StableWaypointsOverride ??
            FieldWalkmeshPathfinder.BuildStableWaypoints(
                position.X,
                position.Y,
                position.Z,
                plan.Portals,
                plan.FinalApproach);
        waypoint = steps.Count == 0 ? plan.FinalApproach : steps[0].Waypoint;
        return true;
    }

    private bool TryReadBoundaryState(
        FieldPositionSnapshot position,
        FieldWalkmesh walkmesh,
        out FieldBoundaryState state,
        out string diagnostic)
    {
        if (boundaryStateReader is null)
        {
            state = default;
            diagnostic = "dynamic boundaries not configured";
            return true;
        }

        var result = boundaryStateReader.Read(position, walkmesh.Triangles.Count);
        state = result.State;
        var activeBoundaries = result.IsUsable ? result.State.ActiveBoundaryTriangles : Array.Empty<int>();
        diagnostic = result.IsUsable
            ? $"active boundary triangles={(activeBoundaries.Count == 0 ? "none" : string.Join(',', activeBoundaries))}"
            : $"dynamic boundaries unavailable: {result.Diagnostic}";
        return result.IsUsable;
    }

    private static bool TryBuildInteractionRangeRoute(
        FieldWalkmesh walkmesh,
        int playerTriangle,
        FieldPositionSnapshot position,
        FieldNavigationTarget target,
        Func<int, bool>? isTriangleBlocked,
        IReadOnlyList<FieldWalkmeshOffMeshLink> offMeshLinks,
        out IReadOnlyList<int> bestTrianglePath,
        out IReadOnlyList<FieldNavigationRoutePortal> bestPortals,
        out int bestTargetTriangle,
        out FieldNavigationRouteWaypoint bestApproach,
        out double bestApproachToTargetDistance)
    {
        bestTrianglePath = Array.Empty<int>();
        bestPortals = Array.Empty<FieldNavigationRoutePortal>();
        bestTargetTriangle = -1;
        bestApproach = default;
        bestApproachToTargetDistance = 0d;
        var bestRouteDistance = double.PositiveInfinity;
        var found = false;

        for (var triangleIndex = 0;
             triangleIndex < walkmesh.Triangles.Count;
             triangleIndex++)
        {
            if (isTriangleBlocked?.Invoke(triangleIndex) == true ||
                !TryCreateInteractionApproach(
                    walkmesh.Triangles[triangleIndex],
                    target,
                    out var approach,
                    out var approachToTargetDistance))
            {
                continue;
            }

            if (!FieldWalkmeshPathfinder.TryBuildRoute(
                    walkmesh,
                    playerTriangle,
                    position.X,
                    position.Y,
                    position.Z,
                    approach.X,
                    approach.Y,
                    approach.Z,
                    isTriangleBlocked,
                    offMeshLinks,
                    out var trianglePath,
                    out var portals,
                    out var resolvedTargetTriangle) ||
                resolvedTargetTriangle != triangleIndex)
            {
                continue;
            }

            var routeDistance = CalculateRouteDistance(
                position,
                portals,
                approach);
            if (routeDistance > bestRouteDistance + 0.001d ||
                Math.Abs(routeDistance - bestRouteDistance) <= 0.001d &&
                approachToTargetDistance >= bestApproachToTargetDistance)
            {
                continue;
            }

            found = true;
            bestRouteDistance = routeDistance;
            bestTrianglePath = trianglePath;
            bestPortals = portals;
            bestTargetTriangle = resolvedTargetTriangle;
            bestApproach = approach;
            bestApproachToTargetDistance = approachToTargetDistance;
        }

        return found;
    }

    private static bool TryCreateInteractionApproach(
        FieldWalkmeshTriangle triangle,
        FieldNavigationTarget target,
        out FieldNavigationRouteWaypoint approach,
        out double approachToTargetDistance)
    {
        approach = default;
        approachToTargetDistance = 0d;
        var closest = ClosestPointOnTriangle2D(triangle, target.X, target.Y);
        var centroid = triangle.GetCentroid();
        var insetX = centroid.X - closest.X;
        var insetY = centroid.Y - closest.Y;
        var insetLength = Math.Sqrt(insetX * insetX + insetY * insetY);
        if (insetLength > 0.001d)
        {
            var amount = Math.Min(InteractionApproachInset / insetLength, 0.25d);
            closest = new RouteCoordinate(
                closest.X + insetX * amount,
                closest.Y + insetY * amount);
        }

        var x = (int)Math.Round(closest.X);
        var y = (int)Math.Round(closest.Y);
        var z = (int)Math.Round(InterpolateTriangleZ(triangle, x, y));
        var dx = target.X - x;
        var dy = target.Y - y;
        approachToTargetDistance = Math.Sqrt(dx * (double)dx + dy * (double)dy);
        if (approachToTargetDistance >= target.InteractionRadius - InteractionRangeEpsilon ||
            Math.Abs(target.Z - z) >= NativeInteractionVerticalRange)
        {
            approachToTargetDistance = 0d;
            return false;
        }

        approach = new FieldNavigationRouteWaypoint(x, y, z);
        return true;
    }

    private static RouteCoordinate ClosestPointOnTriangle2D(
        FieldWalkmeshTriangle triangle,
        int x,
        int y)
    {
        if (ContainsPoint2D(triangle, x, y))
        {
            return new RouteCoordinate(x, y);
        }

        var first = ClosestPointOnSegment2D(x, y, triangle.Vertex0, triangle.Vertex1);
        var second = ClosestPointOnSegment2D(x, y, triangle.Vertex1, triangle.Vertex2);
        var third = ClosestPointOnSegment2D(x, y, triangle.Vertex2, triangle.Vertex0);
        var best = first;
        var bestDistance = DistanceSquared2D(x, y, first);
        var secondDistance = DistanceSquared2D(x, y, second);
        if (secondDistance < bestDistance)
        {
            best = second;
            bestDistance = secondDistance;
        }

        if (DistanceSquared2D(x, y, third) < bestDistance)
        {
            best = third;
        }

        return best;
    }

    private static RouteCoordinate ClosestPointOnSegment2D(
        int x,
        int y,
        FieldWalkmeshVertex start,
        FieldWalkmeshVertex end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * (double)dx + dy * (double)dy;
        if (lengthSquared <= 0d)
        {
            return new RouteCoordinate(start.X, start.Y);
        }

        var amount = Math.Clamp(
            ((x - start.X) * dx + (y - start.Y) * dy) / lengthSquared,
            0d,
            1d);
        return new RouteCoordinate(
            start.X + dx * amount,
            start.Y + dy * amount);
    }

    private static bool ContainsPoint2D(
        FieldWalkmeshTriangle triangle,
        double x,
        double y)
    {
        var first = SignedArea2D(x, y, triangle.Vertex0, triangle.Vertex1);
        var second = SignedArea2D(x, y, triangle.Vertex1, triangle.Vertex2);
        var third = SignedArea2D(x, y, triangle.Vertex2, triangle.Vertex0);
        var hasNegative = first < 0 || second < 0 || third < 0;
        var hasPositive = first > 0 || second > 0 || third > 0;
        return !(hasNegative && hasPositive);
    }

    private static double SignedArea2D(
        double x,
        double y,
        FieldWalkmeshVertex start,
        FieldWalkmeshVertex end) =>
        (x - end.X) * (start.Y - end.Y) -
        (start.X - end.X) * (y - end.Y);

    private static double InterpolateTriangleZ(
        FieldWalkmeshTriangle triangle,
        int x,
        int y)
    {
        var denominator =
            (triangle.Vertex1.Y - triangle.Vertex2.Y) *
                (double)(triangle.Vertex0.X - triangle.Vertex2.X) +
            (triangle.Vertex2.X - triangle.Vertex1.X) *
                (double)(triangle.Vertex0.Y - triangle.Vertex2.Y);
        if (Math.Abs(denominator) < 0.0001d)
        {
            return triangle.GetCentroid().Z;
        }

        var first =
            ((triangle.Vertex1.Y - triangle.Vertex2.Y) *
                (double)(x - triangle.Vertex2.X) +
             (triangle.Vertex2.X - triangle.Vertex1.X) *
                (double)(y - triangle.Vertex2.Y)) / denominator;
        var second =
            ((triangle.Vertex2.Y - triangle.Vertex0.Y) *
                (double)(x - triangle.Vertex2.X) +
             (triangle.Vertex0.X - triangle.Vertex2.X) *
                (double)(y - triangle.Vertex2.Y)) / denominator;
        var third = 1d - first - second;
        return first * triangle.Vertex0.Z +
            second * triangle.Vertex1.Z +
            third * triangle.Vertex2.Z;
    }

    private static double CalculateRouteDistance(
        FieldPositionSnapshot position,
        IReadOnlyList<FieldNavigationRoutePortal> portals,
        FieldNavigationRouteWaypoint finalApproach)
    {
        var steps = FieldWalkmeshPathfinder.BuildStableWaypoints(
            position.X,
            position.Y,
            position.Z,
            portals,
            finalApproach);
        var previous = new FieldNavigationRouteWaypoint(
            position.X,
            position.Y,
            position.Z);
        var distance = 0d;
        foreach (var step in steps)
        {
            distance += Distance(previous, step.Waypoint);
            previous = step.Waypoint;
        }

        if (steps.Count == 0 || previous != finalApproach)
        {
            distance += Distance(previous, finalApproach);
        }

        return distance;
    }

    private static double Distance(
        FieldNavigationRouteWaypoint first,
        FieldNavigationRouteWaypoint second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        var dz = second.Z - first.Z;
        return Math.Sqrt(
            dx * (double)dx +
            dy * (double)dy +
            dz * (double)dz);
    }

    private static double DistanceSquared2D(
        int x,
        int y,
        RouteCoordinate point)
    {
        var dx = x - point.X;
        var dy = y - point.Y;
        return dx * dx + dy * dy;
    }

    private readonly record struct RouteCoordinate(double X, double Y);

    private IReadOnlyList<FieldWalkmeshOffMeshLink> ResolveOffMeshLinks(int fieldId, FieldWalkmesh walkmesh)
    {
        var transitions = transitionProvider?.Invoke(fieldId);
        if (transitions is null || transitions.Count == 0)
        {
            return Array.Empty<FieldWalkmeshOffMeshLink>();
        }

        var links = new List<FieldWalkmeshOffMeshLink>(transitions.Count);
        foreach (var transition in transitions)
        {
            if (transition.FieldId != fieldId ||
                transition.TargetTriangle < 0 ||
                transition.TargetTriangle >= walkmesh.Triangles.Count)
            {
                continue;
            }

            // A trigger's authored elevation is exact, so it must anchor to a
            // triangle on its own storey or to none at all. See
            // FieldWalkmeshPathfinder.ResolveTriangleAtElevation.
            var sourceTriangle = FieldWalkmeshPathfinder.ResolveTriangleAtElevation(
                walkmesh,
                transition.SourceX,
                transition.SourceY,
                transition.SourceZ,
                OffMeshLinkMaximumAnchorElevationError);
            if (sourceTriangle < 0)
            {
                continue;
            }

            var targetTriangleIndex = transition.TargetTriangle;
            var targetX = transition.TargetX;
            var targetY = transition.TargetY;
            var targetTriangle = walkmesh.Triangles[targetTriangleIndex];
            var targetZ = transition.TargetZ ?? (int)Math.Round(targetTriangle.GetCentroid().Z);
            if (transition.Kind == FieldNavigationTransitionKind.Ladder &&
                TryResolvePairedLadderLanding(
                    transition,
                    transitions,
                    walkmesh,
                    sourceTriangle,
                    out var pairedTriangle,
                    out var pairedLanding))
            {
                targetTriangleIndex = pairedTriangle;
                targetX = pairedLanding.X;
                targetY = pairedLanding.Y;
                targetZ = pairedLanding.Z;
            }

            links.Add(new FieldWalkmeshOffMeshLink(
                sourceTriangle,
                targetTriangleIndex,
                new FieldNavigationRouteWaypoint(transition.SourceX, transition.SourceY, transition.SourceZ),
                new FieldNavigationRouteWaypoint(targetX, targetY, targetZ),
                transition.StableId,
                transition.Kind,
                transition.RequiredInput,
                transition.RequiresAction));
        }

        return links;
    }

    private static bool TryResolvePairedLadderLanding(
        FieldScriptNavigationTransition transition,
        IReadOnlyList<FieldScriptNavigationTransition> transitions,
        FieldWalkmesh walkmesh,
        int sourceTriangle,
        out int targetTriangle,
        out FieldNavigationRouteWaypoint landing)
    {
        targetTriangle = -1;
        landing = default;
        if (transition.RequiredInput == FieldNavigationInput.None)
        {
            return false;
        }

        var maximumDistanceSquared =
            LadderPairMaximumEndpointDistance * (double)LadderPairMaximumEndpointDistance;
        FieldScriptNavigationTransition? bestPair = null;
        var bestPairTriangle = -1;
        var bestScore = double.MaxValue;
        var missingZPairs = new List<(FieldScriptNavigationTransition Transition, int SourceTriangle)>();
        foreach (var candidate in transitions)
        {
            if (candidate.FieldId != transition.FieldId ||
                candidate.Kind != FieldNavigationTransitionKind.Ladder ||
                string.Equals(candidate.StableId, transition.StableId, StringComparison.Ordinal) ||
                !AreOppositeLadderInputs(transition.RequiredInput, candidate.RequiredInput))
            {
                continue;
            }

            // The other end of a ladder is anchored under the same rule as this
            // end: a trigger belongs to its own storey or to nothing. Pairing
            // through the permissive resolver would re-admit the wrong floor by
            // the back door.
            var candidateSourceTriangle = FieldWalkmeshPathfinder.ResolveTriangleAtElevation(
                walkmesh,
                candidate.SourceX,
                candidate.SourceY,
                candidate.SourceZ,
                OffMeshLinkMaximumAnchorElevationError);
            if (candidateSourceTriangle < 0)
            {
                continue;
            }

            double score;
            if (transition.TargetZ is int transitionTargetZ &&
                candidate.TargetZ is int candidateTargetZ)
            {
                var landingDistance = DistanceSquared(
                    transition.TargetX,
                    transition.TargetY,
                    transitionTargetZ,
                    candidate.SourceX,
                    candidate.SourceY,
                    candidate.SourceZ);
                var reverseLandingDistance = DistanceSquared(
                    candidate.TargetX,
                    candidate.TargetY,
                    candidateTargetZ,
                    transition.SourceX,
                    transition.SourceY,
                    transition.SourceZ);
                if (landingDistance > maximumDistanceSquared ||
                    reverseLandingDistance > maximumDistanceSquared)
                {
                    continue;
                }

                score = landingDistance + reverseLandingDistance;
            }
            else
            {
                // Some original field scripts end LADER with a collapsed cleanup
                // JUMP that writes X/Y but omits Z. In that form one cross-endpoint
                // still identifies the opposite entrance exactly, while treating the
                // missing Z as either source height makes the pair look disconnected.
                var landingDistance = DistanceSquared2D(
                    transition.TargetX,
                    transition.TargetY,
                    candidate.SourceX,
                    candidate.SourceY);
                var reverseLandingDistance = DistanceSquared2D(
                    candidate.TargetX,
                    candidate.TargetY,
                    transition.SourceX,
                    transition.SourceY);
                score = Math.Min(landingDistance, reverseLandingDistance);
                if (score > maximumDistanceSquared)
                {
                    continue;
                }

                // A collapsed cleanup JUMP only leaves one trustworthy X/Y
                // anchor. It is safe to infer the other endpoint only when the
                // opposite entrance is on a different disconnected walkmesh
                // island and exactly one candidate satisfies that evidence.
                if (AreWalkmeshTrianglesConnected(walkmesh, sourceTriangle, candidateSourceTriangle))
                {
                    continue;
                }

                missingZPairs.Add((candidate, candidateSourceTriangle));
                continue;
            }

            if (score < bestScore)
            {
                bestPair = candidate;
                bestPairTriangle = candidateSourceTriangle;
                bestScore = score;
            }
        }

        FieldScriptNavigationTransition pair;
        if (bestPair is { } completePair)
        {
            pair = completePair;
            targetTriangle = bestPairTriangle;
        }
        else if (missingZPairs.Count == 1)
        {
            pair = missingZPairs[0].Transition;
            targetTriangle = missingZPairs[0].SourceTriangle;
        }
        else
        {
            return false;
        }

        if (targetTriangle < 0)
        {
            return false;
        }

        landing = new FieldNavigationRouteWaypoint(pair.SourceX, pair.SourceY, pair.SourceZ);
        return true;
    }

    private static bool AreWalkmeshTrianglesConnected(
        FieldWalkmesh walkmesh,
        int firstTriangle,
        int secondTriangle)
    {
        if (firstTriangle == secondTriangle)
        {
            return true;
        }

        var visited = new bool[walkmesh.Triangles.Count];
        var pending = new Queue<int>();
        visited[firstTriangle] = true;
        pending.Enqueue(firstTriangle);
        while (pending.TryDequeue(out var current))
        {
            var triangle = walkmesh.Triangles[current];
            for (var edgeIndex = 0; edgeIndex < 3; edgeIndex++)
            {
                var adjacent = triangle.GetAdjacentTriangle(edgeIndex);
                if (adjacent < 0 || adjacent >= walkmesh.Triangles.Count || visited[adjacent])
                {
                    continue;
                }

                if (adjacent == secondTriangle)
                {
                    return true;
                }

                visited[adjacent] = true;
                pending.Enqueue(adjacent);
            }
        }

        return false;
    }

    private static bool AreOppositeLadderInputs(FieldNavigationInput first, FieldNavigationInput second) =>
        (first, second) is
            (FieldNavigationInput.Up, FieldNavigationInput.Down) or
            (FieldNavigationInput.Down, FieldNavigationInput.Up) or
            (FieldNavigationInput.Left, FieldNavigationInput.Right) or
            (FieldNavigationInput.Right, FieldNavigationInput.Left);

    private static double DistanceSquared(
        int firstX,
        int firstY,
        int firstZ,
        int secondX,
        int secondY,
        int secondZ)
    {
        var dx = secondX - firstX;
        var dy = secondY - firstY;
        var dz = secondZ - firstZ;
        return dx * (double)dx + dy * (double)dy + dz * (double)dz;
    }

    private static double DistanceSquared2D(
        int firstX,
        int firstY,
        int secondX,
        int secondY)
    {
        var dx = secondX - firstX;
        var dy = secondY - firstY;
        return dx * (double)dx + dy * (double)dy;
    }

    private static string GetTargetId(FieldNavigationTarget target) =>
        string.IsNullOrWhiteSpace(target.StableId)
            ? $"{target.FieldId}:{target.Category}:{target.Label}:{target.X}:{target.Y}:{target.Z}"
            : $"{target.FieldId}:{target.StableId}";
}

public static class FieldWalkmeshPathfinder
{
    private const double LegacyPortalInsetUnits = 18d;
    private const double PortalClearanceUnits = 64d;
    private const double PortalInsetFraction = 0.20d;
    private const double PortalClearanceFraction = 1d / 6d;
    private const double SteepApproachMinimumElevationChange = 96d;
    private const double SteepApproachMinimumGrade = 0.5d;

    /// <summary>
    /// How far, in plan view, a trigger may sit from the nearest triangle on its
    /// own storey and still anchor to it. Audited across every field carrying
    /// transitions: the furthest legitimate fallback is 148 units.
    /// </summary>
    private const double MaximumAnchorPlanarFallbackDistanceSquared = 384d * 384d;

    public static bool TryFindNextWaypoint(
        FieldWalkmesh walkmesh,
        int currentTriangleIndex,
        int targetX,
        int targetY,
        int targetZ,
        out FieldNavigationRouteWaypoint waypoint)
    {
        var triangles = walkmesh.Triangles;
        if (currentTriangleIndex < 0 || currentTriangleIndex >= triangles.Count)
        {
            waypoint = default;
            return false;
        }

        var start = triangles[currentTriangleIndex].GetCentroid();
        return TryFindNextWaypoint(
            walkmesh,
            currentTriangleIndex,
            (int)Math.Round(start.X),
            (int)Math.Round(start.Y),
            (int)Math.Round(start.Z),
            targetX,
            targetY,
            targetZ,
            out waypoint);
    }

    public static bool TryBuildRoute(
        FieldWalkmesh walkmesh,
        int currentTriangleIndex,
        int startX,
        int startY,
        int startZ,
        int targetX,
        int targetY,
        int targetZ,
        out IReadOnlyList<int> trianglePath,
        out IReadOnlyList<FieldNavigationRoutePortal> routePortals,
        out int targetTriangleIndex)
    {
        return TryBuildRoute(
            walkmesh,
            currentTriangleIndex,
            startX,
            startY,
            startZ,
            targetX,
            targetY,
            targetZ,
            null,
            out trianglePath,
            out routePortals,
            out targetTriangleIndex);
    }

    public static bool TryBuildRoute(
        FieldWalkmesh walkmesh,
        int currentTriangleIndex,
        int startX,
        int startY,
        int startZ,
        int targetX,
        int targetY,
        int targetZ,
        Func<int, bool>? isTriangleBlocked,
        out IReadOnlyList<int> trianglePath,
        out IReadOnlyList<FieldNavigationRoutePortal> routePortals,
        out int targetTriangleIndex)
    {
        trianglePath = Array.Empty<int>();
        routePortals = Array.Empty<FieldNavigationRoutePortal>();
        targetTriangleIndex = -1;
        if (!TryBuildRouteData(
                walkmesh.Triangles,
                currentTriangleIndex,
                startX,
                startY,
                startZ,
                targetX,
                targetY,
                targetZ,
                isTriangleBlocked,
                out _,
                out targetTriangleIndex,
                out trianglePath,
                out var portals))
        {
            return false;
        }

        var transitionCount = Math.Max(0, trianglePath.Count - 1);
        if (transitionCount == 0)
        {
            return true;
        }

        var converted = new FieldNavigationRoutePortal[transitionCount];
        for (var index = 0; index < transitionCount; index++)
        {
            var portal = portals[index];
            converted[index] = new FieldNavigationRoutePortal(
                trianglePath[index],
                trianglePath[index + 1],
                ToWaypoint(portal.Left),
                ToWaypoint(portal.Right));
        }

        routePortals = converted;
        return true;
    }

    public static bool TryBuildRoute(
        FieldWalkmesh walkmesh,
        int currentTriangleIndex,
        int startX,
        int startY,
        int startZ,
        int targetX,
        int targetY,
        int targetZ,
        Func<int, bool>? isTriangleBlocked,
        IReadOnlyList<FieldWalkmeshOffMeshLink> offMeshLinks,
        out IReadOnlyList<int> trianglePath,
        out IReadOnlyList<FieldNavigationRoutePortal> routePortals,
        out int targetTriangleIndex)
    {
        if (offMeshLinks.Count == 0)
        {
            return TryBuildRoute(
                walkmesh,
                currentTriangleIndex,
                startX,
                startY,
                startZ,
                targetX,
                targetY,
                targetZ,
                isTriangleBlocked,
                out trianglePath,
                out routePortals,
                out targetTriangleIndex);
        }

        var triangles = walkmesh.Triangles;
        var resolvedStartTriangle = FindBestTriangle(triangles, startX, startY, startZ, currentTriangleIndex);
        targetTriangleIndex = FindBestTriangle(triangles, targetX, targetY, targetZ, preferredTriangleIndex: -1);
        trianglePath = Array.Empty<int>();
        routePortals = Array.Empty<FieldNavigationRoutePortal>();
        if (resolvedStartTriangle < 0 ||
            targetTriangleIndex < 0 ||
            isTriangleBlocked?.Invoke(resolvedStartTriangle) == true ||
            isTriangleBlocked?.Invoke(targetTriangleIndex) == true)
        {
            return false;
        }

        if (resolvedStartTriangle == targetTriangleIndex)
        {
            trianglePath = [resolvedStartTriangle];
            return true;
        }

        if (!TryFindTrianglePath(
                triangles,
                resolvedStartTriangle,
                targetTriangleIndex,
                isTriangleBlocked,
                offMeshLinks,
                out trianglePath,
                out var usedLinkIndices))
        {
            return false;
        }

        var converted = new FieldNavigationRoutePortal[Math.Max(0, trianglePath.Count - 1)];
        for (var index = 0; index < converted.Length; index++)
        {
            var linkIndex = usedLinkIndices[index];
            if (linkIndex >= 0)
            {
                var entry = offMeshLinks[linkIndex].Entry;
                converted[index] = new FieldNavigationRoutePortal(
                    trianglePath[index],
                    trianglePath[index + 1],
                    entry,
                    entry,
                    offMeshLinks[linkIndex].TransitionKind,
                    offMeshLinks[linkIndex].StableId,
                    offMeshLinks[linkIndex].RequiredInput,
                    TransitionExit: offMeshLinks[linkIndex].Exit,
                    RequiresAction: offMeshLinks[linkIndex].RequiresAction);
                continue;
            }

            if (!TryCreatePortal(
                    triangles[trianglePath[index]],
                    triangles[trianglePath[index + 1]],
                    out var left,
                    out var right))
            {
                trianglePath = Array.Empty<int>();
                routePortals = Array.Empty<FieldNavigationRoutePortal>();
                return false;
            }

            converted[index] = new FieldNavigationRoutePortal(
                trianglePath[index],
                trianglePath[index + 1],
                ToWaypoint(left),
                ToWaypoint(right));
        }

        routePortals = converted;
        return true;
    }

    public static bool TryFindNextWaypoint(
        FieldWalkmesh walkmesh,
        int currentTriangleIndex,
        int startX,
        int startY,
        int startZ,
        int targetX,
        int targetY,
        int targetZ,
        out FieldNavigationRouteWaypoint waypoint)
    {
        return TryFindNextWaypoint(
            walkmesh,
            currentTriangleIndex,
            startX,
            startY,
            startZ,
            targetX,
            targetY,
            targetZ,
            null,
            out waypoint);
    }

    public static bool TryFindNextWaypoint(
        FieldWalkmesh walkmesh,
        int currentTriangleIndex,
        int startX,
        int startY,
        int startZ,
        int targetX,
        int targetY,
        int targetZ,
        Func<int, bool>? isTriangleBlocked,
        out FieldNavigationRouteWaypoint waypoint)
    {
        waypoint = default;
        var triangles = walkmesh.Triangles;
        if (!TryBuildRouteData(
                triangles,
                currentTriangleIndex,
                startX,
                startY,
                startZ,
                targetX,
                targetY,
                targetZ,
                isTriangleBlocked,
                out _,
                out _,
                out _,
                out var portals))
        {
            return false;
        }

        if (portals.Count == 0)
        {
            waypoint = new FieldNavigationRouteWaypoint(targetX, targetY, targetZ);
            return true;
        }

        var nextPoint = FindNextFunnelCorner(new RoutePoint(startX, startY, startZ), portals);
        waypoint = new FieldNavigationRouteWaypoint(
            (int)Math.Round(nextPoint.X),
            (int)Math.Round(nextPoint.Y),
            (int)Math.Round(nextPoint.Z));
        return true;
    }

    public static FieldNavigationRouteWaypoint FindNextWaypoint(
        int startX,
        int startY,
        int startZ,
        IReadOnlyList<FieldNavigationRoutePortal> portals,
        int portalIndex,
        FieldNavigationRouteWaypoint finalApproach)
    {
        var remaining = new List<Portal>(Math.Max(1, portals.Count - portalIndex + 1));
        for (var index = Math.Clamp(portalIndex, 0, portals.Count); index < portals.Count; index++)
        {
            var portal = portals[index];
            remaining.Add(new Portal(
                new RoutePoint(portal.Left.X, portal.Left.Y, portal.Left.Z),
                new RoutePoint(portal.Right.X, portal.Right.Y, portal.Right.Z)));
        }

        var target = new RoutePoint(finalApproach.X, finalApproach.Y, finalApproach.Z);
        remaining.Add(new Portal(target, target));
        var next = FindNextFunnelCorner(new RoutePoint(startX, startY, startZ), remaining);
        return ToWaypoint(next);
    }

    public static IReadOnlyList<FieldNavigationRouteStep> BuildStableWaypoints(
        int startX,
        int startY,
        int startZ,
        IReadOnlyList<FieldNavigationRoutePortal> portals,
        FieldNavigationRouteWaypoint finalApproach)
    {
        var target = new RoutePoint(finalApproach.X, finalApproach.Y, finalApproach.Z);
        if (portals.Count == 0)
        {
            return [new FieldNavigationRouteStep(finalApproach, 0)];
        }

        var funnelPortals = new List<Portal>(portals.Count + 1);
        foreach (var portal in portals)
        {
            funnelPortals.Add(new Portal(
                new RoutePoint(portal.Left.X, portal.Left.Y, portal.Left.Z),
                new RoutePoint(portal.Right.X, portal.Right.Y, portal.Right.Z)));
        }

        funnelPortals.Add(new Portal(target, target));
        var steps = new List<FieldNavigationRouteStep>();
        var apex = new RoutePoint(startX, startY, startZ);
        var firstPortalIndex = 0;
        while (firstPortalIndex < funnelPortals.Count)
        {
            var remaining = funnelPortals.GetRange(
                firstPortalIndex,
                funnelPortals.Count - firstPortalIndex);
            var corner = FindNextFunnelCornerWithIndex(apex, remaining);
            var absolutePortalIndex = firstPortalIndex + corner.PortalIndex;
            if (absolutePortalIndex >= funnelPortals.Count - 1)
            {
                AddRouteStep(steps, finalApproach, portals.Count);
                break;
            }

            AddRouteStep(
                steps,
                ToWaypoint(corner.Point),
                Math.Min(portals.Count, absolutePortalIndex + 1));
            apex = corner.Point;
            firstPortalIndex = absolutePortalIndex + 1;
        }

        if (steps.Count == 0 || steps[^1].Waypoint != finalApproach)
        {
            AddRouteStep(steps, finalApproach, portals.Count);
        }

        MarkSteepApproachesAsRequired(steps);
        return steps;
    }

    private static void MarkSteepApproachesAsRequired(
        IList<FieldNavigationRouteStep> steps)
    {
        for (var index = 0; index < steps.Count - 1; index++)
        {
            var approach = steps[index].Waypoint;
            var destinationStep = steps[index + 1];
            var destination = destinationStep.Waypoint;
            var elevationChange = destination.Z - approach.Z;
            if (elevationChange < SteepApproachMinimumElevationChange)
            {
                continue;
            }

            var deltaX = destination.X - approach.X;
            var deltaY = destination.Y - approach.Y;
            var horizontalDistance = Math.Sqrt(
                deltaX * (double)deltaX +
                deltaY * (double)deltaY);
            var grade = elevationChange / Math.Max(1d, horizontalDistance);
            if (grade < SteepApproachMinimumGrade)
            {
                continue;
            }

            // The funnel is planar, while FFVII keeps movement on the current
            // native triangle. Preserve the last same-layer corner before a
            // steep continuation even when the next corner crosses several
            // portals; corridor lookahead must not aim through scenery at the
            // elevated corner and silently skip the ramp or stair entrance.
            steps[index] = steps[index] with { MustReach = true };
        }
    }

    private static void AddRouteStep(
        ICollection<FieldNavigationRouteStep> steps,
        FieldNavigationRouteWaypoint waypoint,
        int requiredPortalIndex)
    {
        if (steps.Count != 0 && steps.Last().Waypoint == waypoint)
        {
            return;
        }

        steps.Add(new FieldNavigationRouteStep(waypoint, requiredPortalIndex));
    }

    private static bool TryBuildRouteData(
        IReadOnlyList<FieldWalkmeshTriangle> triangles,
        int currentTriangleIndex,
        int startX,
        int startY,
        int startZ,
        int targetX,
        int targetY,
        int targetZ,
        Func<int, bool>? isTriangleBlocked,
        out int resolvedStartTriangle,
        out int targetTriangleIndex,
        out IReadOnlyList<int> trianglePath,
        out IReadOnlyList<Portal> portals)
    {
        resolvedStartTriangle = FindBestTriangle(triangles, startX, startY, startZ, currentTriangleIndex);
        targetTriangleIndex = FindBestTriangle(triangles, targetX, targetY, targetZ, preferredTriangleIndex: -1);
        trianglePath = Array.Empty<int>();
        portals = Array.Empty<Portal>();
        if (resolvedStartTriangle < 0 ||
            targetTriangleIndex < 0 ||
            isTriangleBlocked?.Invoke(resolvedStartTriangle) == true ||
            isTriangleBlocked?.Invoke(targetTriangleIndex) == true)
        {
            return false;
        }

        if (targetTriangleIndex == resolvedStartTriangle)
        {
            trianglePath = new[] { resolvedStartTriangle };
            return true;
        }

        if (!TryFindTrianglePath(
                triangles,
                resolvedStartTriangle,
                targetTriangleIndex,
                isTriangleBlocked,
                out trianglePath))
        {
            return false;
        }

        return TryBuildPortals(triangles, trianglePath, targetX, targetY, targetZ, out portals);
    }

    public static int ResolveTriangle(
        FieldWalkmesh walkmesh,
        int x,
        int y,
        int z,
        int preferredTriangleIndex) =>
        FindBestTriangle(walkmesh.Triangles, x, y, z, preferredTriangleIndex);

    /// <summary>
    /// Resolves a triangle for a point whose elevation is known to be reliable,
    /// refusing to answer with one from a different storey.
    /// </summary>
    /// <remarks>
    /// <see cref="FindBestTriangle"/> weights the elevation error heavily but
    /// never rejects on it, so it always returns the best planar match no matter
    /// how far away vertically that match is. In a field built as a tower that
    /// silently staples a trigger to the floor underneath it. Fort Condor is the
    /// case in point: convil_1's ladder trigger <c>ladder:355:12</c> is authored
    /// at (1080, 270, 671), no triangle at that height covers those coordinates,
    /// and the save room's triangle 11 does - 653 units below. The planner then
    /// believed a ladder led up out of the save room floor, steered the player to
    /// exactly the point beneath it, and left them oscillating there with 663
    /// units of the route still to travel and no horizontal move that could make
    /// progress. Two of that field's fifteen transitions land that way.
    /// </remarks>
    public static int ResolveTriangleAtElevation(
        FieldWalkmesh walkmesh,
        int x,
        int y,
        int z,
        double maximumElevationError)
    {
        var triangles = walkmesh.Triangles;
        var bestIndex = -1;
        var bestScore = double.MaxValue;
        for (var index = 0; index < triangles.Count; index++)
        {
            var triangle = triangles[index];
            if (!ContainsPoint(triangle, x, y))
            {
                continue;
            }

            var zDifference = z - InterpolateZ(triangle, x, y);
            if (Math.Abs(zDifference) > maximumElevationError)
            {
                continue;
            }

            var centroid = triangle.GetCentroid();
            var dx = x - centroid.X;
            var dy = y - centroid.Y;
            var score = zDifference * zDifference * 64d + (dx * dx + dy * dy) * 0.001d;
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = index;
            }
        }

        if (bestIndex >= 0)
        {
            return bestIndex;
        }

        // Nothing at the right height covers the point. A trigger at the top of a
        // ladder can sit just off the edge of the platform it belongs to, so fall
        // back to the nearest triangle that is at least on the correct storey -
        // never to one on a different one, and never to one clear across the
        // field. Every fallback the all-field audit exercises lands within 148
        // units, so the ceiling below leaves well over double that in margin
        // while still ruling out a malformed trigger reaching a distant platform.
        bestScore = double.MaxValue;
        for (var index = 0; index < triangles.Count; index++)
        {
            var triangle = triangles[index];
            var zDifference = z - triangle.GetCentroid().Z;
            if (Math.Abs(zDifference) > maximumElevationError)
            {
                continue;
            }

            var planarDistanceSquared = DistanceSquaredToTriangle2D(triangle, x, y);
            if (planarDistanceSquared > MaximumAnchorPlanarFallbackDistanceSquared)
            {
                continue;
            }

            var score = planarDistanceSquared + zDifference * zDifference * 64d;
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    public static FieldWalkmeshSegmentTrace TraceWalkableSegment(
        FieldWalkmesh walkmesh,
        int startTriangleIndex,
        FieldNavigationRouteWaypoint start,
        FieldNavigationRouteWaypoint end,
        Func<int, bool>? isTriangleBlocked = null)
    {
        const double intersectionEpsilon = 0.000001d;
        var triangles = walkmesh.Triangles;
        var resolvedStartTriangle = FindBestTriangle(
            triangles,
            start.X,
            start.Y,
            start.Z,
            startTriangleIndex);
        var targetTriangle = FindBestTriangle(
            triangles,
            end.X,
            end.Y,
            end.Z,
            preferredTriangleIndex: -1);
        if (resolvedStartTriangle < 0 ||
            resolvedStartTriangle >= triangles.Count ||
            !ContainsPoint(triangles[resolvedStartTriangle], start.X, start.Y))
        {
            return SegmentTraceFailure(
                -1,
                Array.Empty<int>(),
                start,
                "start is outside the walkmesh");
        }

        if (targetTriangle < 0 ||
            targetTriangle >= triangles.Count ||
            !ContainsPoint(triangles[targetTriangle], end.X, end.Y))
        {
            return SegmentTraceFailure(
                resolvedStartTriangle,
                [resolvedStartTriangle],
                start,
                "end is outside the walkmesh");
        }

        if (isTriangleBlocked?.Invoke(resolvedStartTriangle) == true)
        {
            return SegmentTraceFailure(
                resolvedStartTriangle,
                [resolvedStartTriangle],
                start,
                $"start triangle {resolvedStartTriangle} is blocked");
        }

        var traversed = new List<int> { resolvedStartTriangle };
        var currentTriangle = resolvedStartTriangle;
        var currentAmount = 0d;
        var directionX = end.X - start.X;
        var directionY = end.Y - start.Y;
        var directionZ = end.Z - start.Z;
        if (Math.Abs(directionX) < intersectionEpsilon &&
            Math.Abs(directionY) < intersectionEpsilon)
        {
            return currentTriangle == targetTriangle
                ? SegmentTraceSuccess(currentTriangle, traversed, end)
                : SegmentTraceFailure(
                    currentTriangle,
                    traversed,
                    start,
                    $"vertical segment cannot reach disconnected triangle {targetTriangle}");
        }

        var maximumTransitions = Math.Max(1, triangles.Count + 1);
        Span<int> crossingEdges = stackalloc int[3];
        for (var transition = 0; transition < maximumTransitions; transition++)
        {
            if (currentTriangle == targetTriangle)
            {
                return SegmentTraceSuccess(currentTriangle, traversed, end);
            }

            var triangle = triangles[currentTriangle];
            var nextAmount = double.MaxValue;
            var crossingEdgeCount = 0;
            for (var edgeIndex = 0; edgeIndex < 3; edgeIndex++)
            {
                var edge = triangle.GetEdge(edgeIndex);
                if (!TryIntersectSegments2D(
                        start.X,
                        start.Y,
                        directionX,
                        directionY,
                        edge.Start.X,
                        edge.Start.Y,
                        edge.End.X - edge.Start.X,
                        edge.End.Y - edge.Start.Y,
                        out var segmentAmount,
                        out _))
                {
                    continue;
                }

                if (segmentAmount <= currentAmount + intersectionEpsilon ||
                    segmentAmount > 1d + intersectionEpsilon)
                {
                    continue;
                }

                if (segmentAmount < nextAmount - intersectionEpsilon)
                {
                    nextAmount = segmentAmount;
                    crossingEdgeCount = 0;
                    crossingEdges[crossingEdgeCount++] = edgeIndex;
                }
                else if (Math.Abs(segmentAmount - nextAmount) <= intersectionEpsilon)
                {
                    crossingEdges[crossingEdgeCount++] = edgeIndex;
                }
            }

            if (crossingEdgeCount == 0 || nextAmount == double.MaxValue)
            {
                return SegmentTraceFailure(
                    currentTriangle,
                    traversed,
                    InterpolateSegmentPoint(start, directionX, directionY, directionZ, currentAmount),
                    $"segment cannot reach target triangle {targetTriangle} from triangle {currentTriangle}");
            }

            nextAmount = Math.Clamp(nextAmount, 0d, 1d);
            var crossingX = start.X + directionX * nextAmount;
            var crossingY = start.Y + directionY * nextAmount;
            var crossingPoint = InterpolateSegmentPoint(
                start,
                directionX,
                directionY,
                directionZ,
                nextAmount);
            var probeAmount = Math.Min(1d, nextAmount + 0.00001d);
            var probeX = start.X + directionX * probeAmount;
            var probeY = start.Y + directionY * probeAmount;
            var selectedAdjacent = -1;
            var blockedAdjacent = -1;
            for (var crossingIndex = 0; crossingIndex < crossingEdgeCount; crossingIndex++)
            {
                var edgeIndex = crossingEdges[crossingIndex];
                var adjacent = triangle.GetAdjacentTriangle(edgeIndex);
                if (adjacent < 0 || adjacent >= triangles.Count)
                {
                    continue;
                }

                if (!IsWithinInsetPortal(triangle.GetEdge(edgeIndex), crossingX, crossingY))
                {
                    continue;
                }

                if (isTriangleBlocked?.Invoke(adjacent) == true)
                {
                    blockedAdjacent = adjacent;
                    continue;
                }

                if (!ContainsPoint(triangles[adjacent], probeX, probeY))
                {
                    continue;
                }

                selectedAdjacent = adjacent;
                break;
            }

            if (selectedAdjacent < 0)
            {
                var diagnostic = blockedAdjacent >= 0
                    ? $"triangle {blockedAdjacent} is blocked"
                    : $"walkmesh edge blocks segment at {crossingPoint.X},{crossingPoint.Y}";
                return SegmentTraceFailure(
                    currentTriangle,
                    traversed,
                    crossingPoint,
                    diagnostic);
            }

            if (traversed.Contains(selectedAdjacent))
            {
                return SegmentTraceFailure(
                    currentTriangle,
                    traversed,
                    crossingPoint,
                    $"segment looped from triangle {currentTriangle} to {selectedAdjacent}");
            }

            currentTriangle = selectedAdjacent;
            traversed.Add(currentTriangle);
            currentAmount = nextAmount;
        }

        return SegmentTraceFailure(
            currentTriangle,
            traversed,
            InterpolateSegmentPoint(start, directionX, directionY, directionZ, currentAmount),
            "segment exceeded walkmesh transition limit");
    }

    private static int FindBestTriangle(
        IReadOnlyList<FieldWalkmeshTriangle> triangles,
        int x,
        int y,
        int z,
        int preferredTriangleIndex)
    {
        var bestIndex = -1;
        var bestScore = double.MaxValue;
        for (var index = 0; index < triangles.Count; index++)
        {
            var triangle = triangles[index];
            if (!ContainsPoint(triangle, x, y))
            {
                continue;
            }

            var centroid = triangle.GetCentroid();
            var zDifference = z - InterpolateZ(triangle, x, y);
            var dx = x - centroid.X;
            var dy = y - centroid.Y;
            var score = zDifference * zDifference * 64d + (dx * dx + dy * dy) * 0.001d;
            if (index == preferredTriangleIndex)
            {
                score -= 0.0001d;
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = index;
            }
        }

        if (bestIndex >= 0)
        {
            return bestIndex;
        }

        for (var index = 0; index < triangles.Count; index++)
        {
            var triangle = triangles[index];
            var centroid = triangle.GetCentroid();
            var zDifference = z - centroid.Z;
            var score = DistanceSquaredToTriangle2D(triangle, x, y) + zDifference * zDifference * 64d;
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private static bool ContainsPoint(FieldWalkmeshTriangle triangle, int x, int y)
    {
        var d1 = SignedArea(x, y, triangle.Vertex0, triangle.Vertex1);
        var d2 = SignedArea(x, y, triangle.Vertex1, triangle.Vertex2);
        var d3 = SignedArea(x, y, triangle.Vertex2, triangle.Vertex0);
        var hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPositive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNegative && hasPositive);
    }

    private static bool ContainsPoint(FieldWalkmeshTriangle triangle, double x, double y)
    {
        var d1 = SignedArea(x, y, triangle.Vertex0, triangle.Vertex1);
        var d2 = SignedArea(x, y, triangle.Vertex1, triangle.Vertex2);
        var d3 = SignedArea(x, y, triangle.Vertex2, triangle.Vertex0);
        var hasNegative = d1 < -0.000001d || d2 < -0.000001d || d3 < -0.000001d;
        var hasPositive = d1 > 0.000001d || d2 > 0.000001d || d3 > 0.000001d;
        return !(hasNegative && hasPositive);
    }

    private static long SignedArea(int x, int y, FieldWalkmeshVertex start, FieldWalkmeshVertex end) =>
        ((long)x - end.X) * (start.Y - end.Y) - ((long)start.X - end.X) * (y - end.Y);

    private static double SignedArea(double x, double y, FieldWalkmeshVertex start, FieldWalkmeshVertex end) =>
        (x - end.X) * (start.Y - end.Y) - (start.X - end.X) * (y - end.Y);

    private static double InterpolateZ(FieldWalkmeshTriangle triangle, int x, int y)
    {
        var denominator =
            (triangle.Vertex1.Y - triangle.Vertex2.Y) * (double)(triangle.Vertex0.X - triangle.Vertex2.X) +
            (triangle.Vertex2.X - triangle.Vertex1.X) * (double)(triangle.Vertex0.Y - triangle.Vertex2.Y);
        if (Math.Abs(denominator) < 0.0001d)
        {
            return triangle.GetCentroid().Z;
        }

        var first =
            ((triangle.Vertex1.Y - triangle.Vertex2.Y) * (double)(x - triangle.Vertex2.X) +
             (triangle.Vertex2.X - triangle.Vertex1.X) * (double)(y - triangle.Vertex2.Y)) / denominator;
        var second =
            ((triangle.Vertex2.Y - triangle.Vertex0.Y) * (double)(x - triangle.Vertex2.X) +
             (triangle.Vertex0.X - triangle.Vertex2.X) * (double)(y - triangle.Vertex2.Y)) / denominator;
        var third = 1d - first - second;
        return first * triangle.Vertex0.Z + second * triangle.Vertex1.Z + third * triangle.Vertex2.Z;
    }

    private static double DistanceSquaredToTriangle2D(FieldWalkmeshTriangle triangle, int x, int y)
    {
        if (ContainsPoint(triangle, x, y))
        {
            return 0d;
        }

        return Math.Min(
            DistanceSquaredToSegment(x, y, triangle.Vertex0, triangle.Vertex1),
            Math.Min(
                DistanceSquaredToSegment(x, y, triangle.Vertex1, triangle.Vertex2),
                DistanceSquaredToSegment(x, y, triangle.Vertex2, triangle.Vertex0)));
    }

    private static double DistanceSquaredToSegment(
        int x,
        int y,
        FieldWalkmeshVertex start,
        FieldWalkmeshVertex end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = dx * (double)dx + dy * (double)dy;
        if (lengthSquared <= 0d)
        {
            var pointDx = x - start.X;
            var pointDy = y - start.Y;
            return pointDx * (double)pointDx + pointDy * (double)pointDy;
        }

        var amount = Math.Clamp(((x - start.X) * dx + (y - start.Y) * dy) / lengthSquared, 0d, 1d);
        var closestX = start.X + dx * amount;
        var closestY = start.Y + dy * amount;
        var distanceX = x - closestX;
        var distanceY = y - closestY;
        return distanceX * distanceX + distanceY * distanceY;
    }

    private static bool TryIntersectSegments2D(
        double startX,
        double startY,
        double directionX,
        double directionY,
        double edgeStartX,
        double edgeStartY,
        double edgeDirectionX,
        double edgeDirectionY,
        out double segmentAmount,
        out double edgeAmount)
    {
        var denominator = Cross2D(directionX, directionY, edgeDirectionX, edgeDirectionY);
        if (Math.Abs(denominator) < 0.0000001d)
        {
            segmentAmount = 0d;
            edgeAmount = 0d;
            return false;
        }

        var offsetX = edgeStartX - startX;
        var offsetY = edgeStartY - startY;
        segmentAmount = Cross2D(offsetX, offsetY, edgeDirectionX, edgeDirectionY) / denominator;
        edgeAmount = Cross2D(offsetX, offsetY, directionX, directionY) / denominator;
        return segmentAmount >= -0.000001d &&
            segmentAmount <= 1.000001d &&
            edgeAmount >= -0.000001d &&
            edgeAmount <= 1.000001d;
    }

    private static bool IsWithinInsetPortal(
        (FieldWalkmeshVertex Start, FieldWalkmeshVertex End) edge,
        double x,
        double y)
    {
        var first = new RoutePoint(edge.Start.X, edge.Start.Y, edge.Start.Z);
        var second = new RoutePoint(edge.End.X, edge.End.Y, edge.End.Z);
        InsetPortal(ref first, ref second);
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0.000001d)
        {
            return false;
        }

        var amount = ((x - first.X) * dx + (y - first.Y) * dy) / lengthSquared;
        if (amount < -0.000001d || amount > 1.000001d)
        {
            return false;
        }

        var closestX = first.X + dx * amount;
        var closestY = first.Y + dy * amount;
        var distanceX = x - closestX;
        var distanceY = y - closestY;
        return distanceX * distanceX + distanceY * distanceY <= 0.25d;
    }

    private static FieldNavigationRouteWaypoint InterpolateSegmentPoint(
        FieldNavigationRouteWaypoint start,
        double directionX,
        double directionY,
        double directionZ,
        double amount) =>
        new(
            (int)Math.Round(start.X + directionX * amount),
            (int)Math.Round(start.Y + directionY * amount),
            (int)Math.Round(start.Z + directionZ * amount));

    private static FieldWalkmeshSegmentTrace SegmentTraceSuccess(
        int endTriangle,
        IReadOnlyList<int> traversed,
        FieldNavigationRouteWaypoint end) =>
        new(
            true,
            endTriangle,
            traversed.ToArray(),
            end,
            $"clear through {traversed.Count} triangle(s)");

    private static FieldWalkmeshSegmentTrace SegmentTraceFailure(
        int endTriangle,
        IReadOnlyList<int> traversed,
        FieldNavigationRouteWaypoint furthestPoint,
        string diagnostic) =>
        new(false, endTriangle, traversed.ToArray(), furthestPoint, diagnostic);

    private static double Cross2D(double firstX, double firstY, double secondX, double secondY) =>
        firstX * secondY - firstY * secondX;

    private static bool TryFindTrianglePath(
        IReadOnlyList<FieldWalkmeshTriangle> triangles,
        int startIndex,
        int targetIndex,
        Func<int, bool>? isTriangleBlocked,
        out IReadOnlyList<int> path)
    {
        path = Array.Empty<int>();
        var cameFrom = Enumerable.Repeat(-1, triangles.Count).ToArray();
        var scores = Enumerable.Repeat(double.PositiveInfinity, triangles.Count).ToArray();
        var closed = new bool[triangles.Count];
        var frontier = new PriorityQueue<int, double>();
        scores[startIndex] = 0d;
        frontier.Enqueue(startIndex, Heuristic(triangles[startIndex], triangles[targetIndex]));

        while (frontier.TryDequeue(out var current, out _))
        {
            if (closed[current])
            {
                continue;
            }

            if (current == targetIndex)
            {
                break;
            }

            closed[current] = true;
            for (var edge = 0; edge < 3; edge++)
            {
                var neighbor = triangles[current].GetAdjacentTriangle(edge);
                if (neighbor < 0 ||
                    neighbor >= triangles.Count ||
                    closed[neighbor] ||
                    isTriangleBlocked?.Invoke(neighbor) == true)
                {
                    continue;
                }

                var tentativeScore = scores[current] + Heuristic(triangles[current], triangles[neighbor]);
                if (tentativeScore >= scores[neighbor])
                {
                    continue;
                }

                cameFrom[neighbor] = current;
                scores[neighbor] = tentativeScore;
                frontier.Enqueue(neighbor, tentativeScore + Heuristic(triangles[neighbor], triangles[targetIndex]));
            }
        }

        if (cameFrom[targetIndex] < 0)
        {
            return false;
        }

        var reversePath = new List<int> { targetIndex };
        var step = targetIndex;
        while (step != startIndex)
        {
            step = cameFrom[step];
            if (step < 0)
            {
                return false;
            }

            reversePath.Add(step);
        }

        reversePath.Reverse();
        path = reversePath;
        return true;
    }

    private static bool TryFindTrianglePath(
        IReadOnlyList<FieldWalkmeshTriangle> triangles,
        int startIndex,
        int targetIndex,
        Func<int, bool>? isTriangleBlocked,
        IReadOnlyList<FieldWalkmeshOffMeshLink> offMeshLinks,
        out IReadOnlyList<int> path,
        out IReadOnlyList<int> usedLinkIndices)
    {
        path = Array.Empty<int>();
        usedLinkIndices = Array.Empty<int>();
        var cameFrom = Enumerable.Repeat(-1, triangles.Count).ToArray();
        var cameViaLink = Enumerable.Repeat(-1, triangles.Count).ToArray();
        var scores = Enumerable.Repeat(double.PositiveInfinity, triangles.Count).ToArray();
        var closed = new bool[triangles.Count];
        var frontier = new PriorityQueue<int, double>();
        scores[startIndex] = 0d;
        frontier.Enqueue(startIndex, Heuristic(triangles[startIndex], triangles[targetIndex]));

        void Relax(int current, int neighbor, double edgeCost, int linkIndex)
        {
            if (neighbor < 0 ||
                neighbor >= triangles.Count ||
                closed[neighbor] ||
                isTriangleBlocked?.Invoke(neighbor) == true)
            {
                return;
            }

            var tentativeScore = scores[current] + Math.Max(1d, edgeCost);
            if (tentativeScore >= scores[neighbor])
            {
                return;
            }

            cameFrom[neighbor] = current;
            cameViaLink[neighbor] = linkIndex;
            scores[neighbor] = tentativeScore;
            frontier.Enqueue(neighbor, tentativeScore + Heuristic(triangles[neighbor], triangles[targetIndex]));
        }

        while (frontier.TryDequeue(out var current, out _))
        {
            if (closed[current])
            {
                continue;
            }

            if (current == targetIndex)
            {
                break;
            }

            closed[current] = true;
            for (var edge = 0; edge < 3; edge++)
            {
                var neighbor = triangles[current].GetAdjacentTriangle(edge);
                if (neighbor >= 0 && neighbor < triangles.Count)
                {
                    Relax(current, neighbor, Heuristic(triangles[current], triangles[neighbor]), linkIndex: -1);
                }
            }

            for (var linkIndex = 0; linkIndex < offMeshLinks.Count; linkIndex++)
            {
                var link = offMeshLinks[linkIndex];
                if (link.FromTriangle != current)
                {
                    continue;
                }

                var from = triangles[current].GetCentroid();
                var to = triangles[link.ToTriangle].GetCentroid();
                var edgeCost = Distance(from.X, from.Y, from.Z, link.Entry.X, link.Entry.Y, link.Entry.Z) +
                    Distance(link.Entry.X, link.Entry.Y, link.Entry.Z, link.Exit.X, link.Exit.Y, link.Exit.Z) +
                    Distance(link.Exit.X, link.Exit.Y, link.Exit.Z, to.X, to.Y, to.Z);
                Relax(current, link.ToTriangle, edgeCost, linkIndex);
            }
        }

        if (cameFrom[targetIndex] < 0)
        {
            return false;
        }

        var reversePath = new List<int> { targetIndex };
        var reverseLinks = new List<int>();
        var step = targetIndex;
        while (step != startIndex)
        {
            reverseLinks.Add(cameViaLink[step]);
            step = cameFrom[step];
            if (step < 0)
            {
                return false;
            }

            reversePath.Add(step);
        }

        reversePath.Reverse();
        reverseLinks.Reverse();
        path = reversePath;
        usedLinkIndices = reverseLinks;
        return true;
    }

    private static double Distance(
        double firstX,
        double firstY,
        double firstZ,
        double secondX,
        double secondY,
        double secondZ)
    {
        var dx = secondX - firstX;
        var dy = secondY - firstY;
        var dz = secondZ - firstZ;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static bool TryCreatePortal(
        FieldWalkmeshTriangle current,
        FieldWalkmeshTriangle next,
        out RoutePoint left,
        out RoutePoint right)
    {
        var edgeIndex = FindEdgeToTriangle(current, next.Index);
        if (edgeIndex < 0)
        {
            left = default;
            right = default;
            return false;
        }

        var edge = current.GetEdge(edgeIndex);
        var first = new RoutePoint(edge.Start.X, edge.Start.Y, edge.Start.Z);
        var second = new RoutePoint(edge.End.X, edge.End.Y, edge.End.Z);
        InsetPortal(ref first, ref second);

        var fromCentroid = current.GetCentroid();
        var toCentroid = next.GetCentroid();
        var midpointX = (first.X + second.X) / 2d;
        var midpointY = (first.Y + second.Y) / 2d;
        var travelX = toCentroid.X - fromCentroid.X;
        var travelY = toCentroid.Y - fromCentroid.Y;
        var firstSide = travelX * (first.Y - midpointY) - travelY * (first.X - midpointX);
        if (firstSide >= 0d)
        {
            left = first;
            right = second;
        }
        else
        {
            left = second;
            right = first;
        }

        return true;
    }

    private static bool TryBuildPortals(
        IReadOnlyList<FieldWalkmeshTriangle> triangles,
        IReadOnlyList<int> path,
        int targetX,
        int targetY,
        int targetZ,
        out IReadOnlyList<Portal> portals)
    {
        var result = new List<Portal>(path.Count);
        for (var pathIndex = 0; pathIndex < path.Count - 1; pathIndex++)
        {
            var current = triangles[path[pathIndex]];
            var next = triangles[path[pathIndex + 1]];
            var edgeIndex = FindEdgeToTriangle(current, next.Index);
            if (edgeIndex < 0)
            {
                portals = Array.Empty<Portal>();
                return false;
            }

            var edge = current.GetEdge(edgeIndex);
            var first = new RoutePoint(edge.Start.X, edge.Start.Y, edge.Start.Z);
            var second = new RoutePoint(edge.End.X, edge.End.Y, edge.End.Z);
            InsetPortal(ref first, ref second);

            var fromCentroid = current.GetCentroid();
            var toCentroid = next.GetCentroid();
            var midpointX = (first.X + second.X) / 2d;
            var midpointY = (first.Y + second.Y) / 2d;
            var travelX = toCentroid.X - fromCentroid.X;
            var travelY = toCentroid.Y - fromCentroid.Y;
            var firstSide = travelX * (first.Y - midpointY) - travelY * (first.X - midpointX);
            result.Add(firstSide >= 0d
                ? new Portal(first, second)
                : new Portal(second, first));
        }

        var target = new RoutePoint(targetX, targetY, targetZ);
        result.Add(new Portal(target, target));
        portals = result;
        return true;
    }

    private static void InsetPortal(ref RoutePoint first, ref RoutePoint second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        var dz = second.Z - first.Z;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0.001d)
        {
            return;
        }

        var legacyInset = Math.Min(LegacyPortalInsetUnits, length * PortalInsetFraction);
        var clearanceInset = Math.Min(PortalClearanceUnits, length * PortalClearanceFraction);
        var inset = Math.Max(legacyInset, clearanceInset);
        var amount = inset / length;
        var originalFirst = first;
        first = new RoutePoint(
            first.X + dx * amount,
            first.Y + dy * amount,
            first.Z + dz * amount);
        second = new RoutePoint(
            second.X + (originalFirst.X - second.X) * amount,
            second.Y + (originalFirst.Y - second.Y) * amount,
            second.Z + (originalFirst.Z - second.Z) * amount);
    }

    private static RoutePoint FindNextFunnelCorner(RoutePoint start, IReadOnlyList<Portal> portals)
        => FindNextFunnelCornerWithIndex(start, portals).Point;

    private static FunnelCorner FindNextFunnelCornerWithIndex(RoutePoint start, IReadOnlyList<Portal> portals)
    {
        var apex = start;
        var left = start;
        var right = start;
        var leftIndex = -1;
        var rightIndex = -1;

        for (var index = 0; index < portals.Count; index++)
        {
            var nextLeft = portals[index].Left;
            var nextRight = portals[index].Right;

            if (TwiceSignedArea(apex, right, nextRight) <= 0d)
            {
                if (SamePoint(apex, right) || TwiceSignedArea(apex, left, nextRight) > 0d)
                {
                    right = nextRight;
                    rightIndex = index;
                }
                else
                {
                    return new FunnelCorner(left, leftIndex);
                }
            }

            if (TwiceSignedArea(apex, left, nextLeft) >= 0d)
            {
                if (SamePoint(apex, left) || TwiceSignedArea(apex, right, nextLeft) < 0d)
                {
                    left = nextLeft;
                    leftIndex = index;
                }
                else
                {
                    return new FunnelCorner(right, rightIndex);
                }
            }
        }

        return new FunnelCorner(portals[^1].Left, portals.Count - 1);
    }

    private static double TwiceSignedArea(RoutePoint first, RoutePoint second, RoutePoint third) =>
        (third.X - first.X) * (second.Y - first.Y) -
        (second.X - first.X) * (third.Y - first.Y);

    private static bool SamePoint(RoutePoint first, RoutePoint second) =>
        Math.Abs(first.X - second.X) < 0.001d && Math.Abs(first.Y - second.Y) < 0.001d;

    private static int FindEdgeToTriangle(FieldWalkmeshTriangle triangle, int adjacentTriangle)
    {
        for (var edge = 0; edge < 3; edge++)
        {
            if (triangle.GetAdjacentTriangle(edge) == adjacentTriangle)
            {
                return edge;
            }
        }

        return -1;
    }

    private static double Heuristic(FieldWalkmeshTriangle from, FieldWalkmeshTriangle to)
    {
        var a = from.GetCentroid();
        var b = to.GetCentroid();
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static FieldNavigationRouteWaypoint ToWaypoint(RoutePoint point) =>
        new((int)Math.Round(point.X), (int)Math.Round(point.Y), (int)Math.Round(point.Z));

    private readonly record struct RoutePoint(double X, double Y, double Z);

    private readonly record struct Portal(RoutePoint Left, RoutePoint Right);

    private readonly record struct FunnelCorner(RoutePoint Point, int PortalIndex);
}

using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Reloaded.Tests;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

/// <summary>
/// The reported missing mansion exit, reproduced through the checked wrapper the Steam
/// 2026 runtime actually plans with.
///
/// <para><see cref="ReachableFieldExitTargetProvider"/> keeps a door the game is holding
/// shut only when its planner implements
/// <see cref="IFieldNavigationNativeBoundaryStatus"/>. The legacy host plans with
/// <c>FieldWalkmeshRoutePlanner</c>, which does; the Steam 2026 host wraps that in
/// <c>Steam2026FailClosedFieldRoutePlanner</c>, which did not - so the same field, the same
/// locks and the same position produced two exits on x86 and one on x64. The 2026-09-22
/// capture has field 300 reporting <c>nativeExits=2, reachableExits=1</c> in 256 of 268
/// samples.</para>
///
/// <para>Both paths are driven here from one fixture so the comparison is the same state,
/// not two similar ones.</para>
/// </summary>
internal static class Steam2026MansionSecretDoorExitTests
{
    public static void Run(Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        ArgumentNullException.ThrowIfNull(createWalkmeshReader);
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_DATA_ROOT")))
        {
            Console.WriteLine("mansion secret door: FF7_ACCESSIBILITY_DATA_ROOT is unset, native cases not run");
            return;
        }

        BothRuntimesKeepTheShutDoorSelectable(createWalkmeshReader);
        TheCheckedWrapperReportsTheNativeBoundaryItsInnerPlannerFound(createWalkmeshReader);
        AReadFailureNeverReportsANativeBoundary(createWalkmeshReader);
        APreparedRouteReplaysTheBoundaryStatusItWasPreparedWith(createWalkmeshReader);
    }

    /// <summary>The defect itself: same field, same locks, same position, both paths.</summary>
    private static void BothRuntimesKeepTheShutDoorSelectable(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var legacy = Exits(createWalkmeshReader, wrapInCheckedPlanner: false);
        Equal(2, legacy.Count, "legacy x86 keeps both of field 300's doors with the panel shut");
        Equal(
            true,
            legacy.Any(exit => exit.DestinationFieldIds?.Contains(301) == true),
            "and the shut door to the staircase is the one it keeps");

        var steam = Exits(createWalkmeshReader, wrapInCheckedPlanner: true);
        Equal(2, steam.Count, "Steam 2026 keeps both of field 300's doors with the panel shut");
        Equal(
            true,
            steam.Any(exit => exit.DestinationFieldIds?.Contains(301) == true),
            "and it keeps the same shut door the legacy host does");

        Equal(
            legacy.Select(exit => exit.StableId).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            steam.Select(exit => exit.StableId).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            "both runtimes publish the same doors from the same state");
    }

    private static void TheCheckedWrapperReportsTheNativeBoundaryItsInnerPlannerFound(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var (planner, start) = Planner(createWalkmeshReader, wrapInCheckedPlanner: true);
        var status = Status(planner);

        Equal(false, planner.TryBuildRoute(start, MansionSecretDoorExitFixture.ToStaircase(), out _),
            "the shut panel has no route through it");
        Equal(true, status.LastFailureWasNativeBoundary,
            "and the wrapper reports that the failure was the game's own lock");
        Equal(
            true,
            status.LastBlockingBoundaryTriangles.Count > 0,
            "the blocking triangles come through as well");

        Equal(true, planner.TryBuildRoute(start, MansionSecretDoorExitFixture.ToHall(), out _),
            "the open door still routes");
        Equal(false, status.LastFailureWasNativeBoundary,
            "and a route that succeeded is not reported as a lock");
    }

    /// <summary>
    /// Fail closed: a read the wrapper could not trust must never be dressed up as a door
    /// the game is holding shut, or an unreadable frame would keep an exit alive forever.
    /// </summary>
    private static void AReadFailureNeverReportsANativeBoundary(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var (inner, start) = Planner(createWalkmeshReader, wrapInCheckedPlanner: false);
        var throwing = new ThrowOnDemandPlanner(inner);
        var planner = new Steam2026FailClosedFieldRoutePlanner(throwing);
        var status = Status(planner);
        var shut = MansionSecretDoorExitFixture.ToStaircase();

        Equal(false, planner.TryBuildRoute(start, shut, out _), "the shut panel has no route through it");
        Equal(true, status.LastFailureWasNativeBoundary, "which is a native boundary");

        // Now the native read tears, exactly as a torn frame does in the host.
        throwing.Throws = true;
        Equal(false, planner.TryBuildRoute(start, shut, out _), "a torn read cannot build a route");

        Equal(false, status.LastFailureWasNativeBoundary,
            "an unreadable frame is not evidence of a lock");
        Equal(0, status.LastBlockingBoundaryTriangles.Count,
            "and it names no blocking triangles");

        var provider = new ReachableFieldExitTargetProvider(
            _ => MansionSecretDoorExitFixture.BothDoors(),
            planner);
        Equal(0, provider.ReadTargets(start).Count,
            "and a torn frame publishes nothing rather than inventing doors");
    }

    /// <summary>An inner planner whose native reads can be made to tear on demand.</summary>
    private sealed class ThrowOnDemandPlanner(IFieldNavigationRoutePlanner inner)
        : IFieldNavigationRoutePlanner, IFieldNavigationNativeBoundaryStatus
    {
        internal bool Throws { get; set; }

        public string LastDiagnostic => inner.LastDiagnostic;

        // Forwarded so the only thing this decorator changes is whether the read tears.
        public bool LastFailureWasNativeBoundary =>
            inner is IFieldNavigationNativeBoundaryStatus { LastFailureWasNativeBoundary: true };

        public IReadOnlyList<int> LastBlockingBoundaryTriangles =>
            inner is IFieldNavigationNativeBoundaryStatus status
                ? status.LastBlockingBoundaryTriangles
                : Array.Empty<int>();

        public bool WouldRouteIfBoundaryReleased(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            int releasedTriangle)
        {
            Guard();
            return inner is IFieldNavigationNativeBoundaryStatus status &&
                status.WouldRouteIfBoundaryReleased(position, target, releasedTriangle);
        }

        public bool TryResolvePlayerTriangle(FieldPositionSnapshot position, out int triangle)
        {
            Guard();
            return inner.TryResolvePlayerTriangle(position, out triangle);
        }

        public bool TryBuildRoute(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRoutePlan plan)
        {
            Guard();
            return inner.TryBuildRoute(position, target, out plan);
        }

        public bool TryGetNextWaypoint(
            FieldPositionSnapshot position,
            FieldNavigationTarget target,
            out FieldNavigationRouteWaypoint waypoint)
        {
            Guard();
            return inner.TryGetNextWaypoint(position, target, out waypoint);
        }

        private void Guard()
        {
            if (Throws)
            {
                throw new InvalidOperationException("torn native read");
            }
        }
    }

    /// <summary>
    /// The prepared-route path replays a cached build result. It has to replay the
    /// boundary status that went with it, or an action route would lose the distinction
    /// the whole fix is about.
    /// </summary>
    private static void APreparedRouteReplaysTheBoundaryStatusItWasPreparedWith(
        Func<int, FieldWalkmeshReader> createWalkmeshReader)
    {
        var (inner, start) = Planner(createWalkmeshReader, wrapInCheckedPlanner: false);
        var checkedPlanner = new Steam2026FailClosedFieldRoutePlanner(inner);
        IFieldNavigationRoutePlanner planner = checkedPlanner;
        var status = Status(planner);
        var shut = MansionSecretDoorExitFixture.ToStaircase();

        checkedPlanner.BeginObservation();
        var preflight = checkedPlanner.PrepareActionRoute(start, shut);
        Equal(true, preflight.IsCoherent, "the preflight read the field coherently");
        Equal(false, preflight.RouteAvailable, "and found no route through the shut panel");

        Equal(true, inner.TryBuildRoute(start, MansionSecretDoorExitFixture.ToHall(), out _),
            "a later inner query can overwrite its last boundary status");
        Equal(false, Status(inner).LastFailureWasNativeBoundary,
            "the inner planner now describes the open hall route");

        Equal(false, planner.TryBuildRoute(start, shut, out _), "the replay agrees there is no route");
        Equal(true, status.LastFailureWasNativeBoundary,
            "and the replay still knows the panel is what stopped it");

        checkedPlanner.CompletePreparedActionRoute();
    }

    /// <summary>
    /// The seam the fix is about. Asserted rather than cast so this file compiles both
    /// before and after, and the failure reads as the defect instead of as a build error.
    /// </summary>
    private static IFieldNavigationNativeBoundaryStatus Status(IFieldNavigationRoutePlanner planner)
    {
        if (planner is IFieldNavigationNativeBoundaryStatus status)
        {
            return status;
        }

        throw new InvalidOperationException(
            $"{planner.GetType().Name} must expose IFieldNavigationNativeBoundaryStatus, or a door the " +
            "game is holding shut is indistinguishable from a door that is not there.");
    }

    private static IReadOnlyList<FieldNavigationTarget> Exits(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        bool wrapInCheckedPlanner)
    {
        var (planner, start) = Planner(createWalkmeshReader, wrapInCheckedPlanner);
        var provider = new ReachableFieldExitTargetProvider(
            _ => MansionSecretDoorExitFixture.BothDoors(),
            planner);
        return provider.ReadTargets(start);
    }

    private static (IFieldNavigationRoutePlanner Planner, FieldPositionSnapshot Start) Planner(
        Func<int, FieldWalkmeshReader> createWalkmeshReader,
        bool wrapInCheckedPlanner)
    {
        var reader = createWalkmeshReader(MansionSecretDoorExitFixture.Field);
        var mesh = reader
            .Read(new FieldPositionSnapshot(
                FieldPositionReader.FieldModule, MansionSecretDoorExitFixture.Field, 0, 0, 0, 0, 0, 0))
            .Walkmesh
            ?? throw new InvalidOperationException("the installed field 300 walkmesh must be readable.");

        IFieldNavigationRoutePlanner planner = new FieldWalkmeshRoutePlanner(
            reader,
            MansionSecretDoorExitFixture.LockedPanel());
        if (wrapInCheckedPlanner)
        {
            planner = new Steam2026FailClosedFieldRoutePlanner(planner);
        }

        return (planner, MansionSecretDoorExitFixture.Entry(mesh));
    }

    private static void Equal(string[] expected, string[] actual, string label)
    {
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"{label}: expected [{string.Join(',', expected)}], got [{string.Join(',', actual)}].");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}

namespace GuideRouteAudit;

internal static class AssessmentTests
{
    internal static void Run()
    {
        // A ledge reached from its return doorway must not mask a failed main entrance.
        Check("mixed-arrivals", "partial-geometry", [new(true, true), new(false, null)]);
        // Having a path to the nearest floor is insufficient for talking or collection.
        Check("outside-activation", "unreachable", [new(true, false)]);
        // A second native state with no resolved model position stays unverified.
        Check("unknown-position", "unverified", [new(true, true), new(null, null)]);
        Check("no-arrivals", "no-native-arrivals", []);
        // Script-enabled jumps are explicitly weaker evidence than ordinary walking.
        Check("conditional-transition", "script-assisted-geometry", [new(true, true, true)]);
        Check("ordinary-geometry", "geometry-from-all-arrivals", [new(true, true), new(true, true)]);
        // A bank-backed range can be larger than the known literal envelope. Failure
        // with that envelope cannot establish that the live interaction is unreachable.
        Check("unknown-range-failed-path", "unverified", [new(false, null, false, false)]);
        Console.WriteLine("Guide route assessment tests passed (7 cases).");
    }

    private static void Check(string name, string expected, ApproachResult[] approaches)
    {
        var actual = RouteAssessment.Summarize(approaches);
        if (actual != expected)
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
    }
}

namespace GuideRouteAudit;

internal readonly record struct ApproachResult(bool? Connected, bool? WithinReach, bool UsesScriptTransitions = false, bool ActivationKnown = true);

internal static class RouteAssessment
{
    internal static string Summarize(IReadOnlyList<ApproachResult> approaches)
    {
        if (approaches.Count == 0) return "no-native-arrivals";
        if (approaches.Any(row => !row.ActivationKnown || row.Connected is null ||
                row.Connected == true && row.WithinReach is null)) return "unverified";
        var reached = approaches.Count(row => row.Connected == true && row.WithinReach == true);
        if (reached == 0) return "unreachable";
        if (reached != approaches.Count) return "partial-geometry";
        return approaches.Any(row => row.UsesScriptTransitions)
            ? "script-assisted-geometry" : "geometry-from-all-arrivals";
    }
}

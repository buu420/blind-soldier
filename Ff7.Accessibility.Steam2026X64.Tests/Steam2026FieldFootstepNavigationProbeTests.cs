using System.Text.Json;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal static class Steam2026FieldFootstepNavigationProbeTests
{
    internal static void Run()
    {
        WritesCompactFootstepsAgainstOneStableRoute();
        NormalizesControllerTargetAliasesToCommittedRouteIdentity();
        EmitsANewRouteWhenBoundaryStateChanges();
        LabelsCachedNavigationAndRejectsForeignOrExpiredState();
        ResetDropsPendingCorrelationButKeepsStrideStatistics();
        JsonlWriterAppendsAcrossSessions();
    }

    private static void WritesCompactFootstepsAgainstOneStableRoute()
    {
        var writer = new RecordingProbeLineWriter();
        var now = new DateTime(2026, 7, 23, 17, 0, 0, DateTimeKind.Utc);
        using var probe = CreateProbe(writer, now);
        var position = Position(0);

        _ = probe.ObserveMovement(
            position,
            now,
            isHostForeground: true,
            hasControl: true,
            FieldFootstepCadence.Walk,
            footstepTriggered: true);
        var distance = probe.ObserveMovement(
            position with { X = 60 },
            now.AddMilliseconds(100),
            isHostForeground: true,
            hasControl: true,
            FieldFootstepCadence.Walk,
            footstepTriggered: true);
        probe.PublishFootstep(Footstep(1, now.AddMilliseconds(100), position with { X = 60 }, distance));
        probe.PublishNavigation(Navigation(1, now.AddMilliseconds(100), position with { X = 60 }));
        probe.CommitCycle(1, now.AddMilliseconds(100));

        var secondDistance = probe.ObserveMovement(
            position with { X = 120 },
            now.AddMilliseconds(200),
            isHostForeground: true,
            hasControl: true,
            FieldFootstepCadence.Walk,
            footstepTriggered: true);
        probe.PublishFootstep(Footstep(2, now.AddMilliseconds(200), position with { X = 120 }, secondDistance));
        probe.PublishNavigation(Navigation(2, now.AddMilliseconds(200), position with { X = 120 }));
        probe.CommitCycle(2, now.AddMilliseconds(200));

        Equal(1, writer.Documents("route").Count, "stable topology should be emitted once");
        var footsteps = writer.Documents("footstep");
        Equal(2, footsteps.Count, "each tracker trigger should produce one footstep record");
        var routeSignature = writer.Documents("route")[0].RootElement.GetProperty("signature").GetString();
        Equal(
            routeSignature,
            footsteps[0].RootElement.GetProperty("navigation").GetProperty("routeSignature").GetString(),
            "first footstep route reference");
        Equal(
            routeSignature,
            footsteps[1].RootElement.GetProperty("navigation").GetProperty("routeSignature").GetString(),
            "second footstep route reference");
        Equal(
            true,
            footsteps[0].RootElement.GetProperty("navigation").GetProperty("sameCycle").GetBoolean(),
            "same worker cycle should be explicit");
    }

    private static void NormalizesControllerTargetAliasesToCommittedRouteIdentity()
    {
        var writer = new RecordingProbeLineWriter();
        var now = new DateTime(2026, 7, 23, 17, 2, 0, DateTimeKind.Utc);
        using var probe = CreateProbe(writer, now);
        var position = Position(0);
        const string committedTargetId = "120:story:120:biggs";

        probe.PublishFootstep(Footstep(1, now, position, default));
        probe.PublishNavigation(
            Navigation(
                1,
                now,
                position,
                controllerTargetId: "story:120:biggs",
                routeTargetId: committedTargetId));
        probe.CommitCycle(1, now);

        probe.PublishFootstep(Footstep(2, now.AddMilliseconds(100), position with { X = 40 }, default));
        probe.PublishNavigation(
            Navigation(
                2,
                now.AddMilliseconds(100),
                position with { X = 40 },
                controllerTargetId: committedTargetId,
                routeTargetId: committedTargetId));
        probe.CommitCycle(2, now.AddMilliseconds(100));

        var routes = writer.Documents("route");
        Equal(1, routes.Count, "controller target aliases must not duplicate unchanged route topology");
        Equal(
            committedTargetId,
            routes[0].RootElement.GetProperty("target").GetProperty("id").GetString(),
            "route record should use the committed route identity");
        foreach (var footstep in writer.Documents("footstep"))
        {
            Equal(
                committedTargetId,
                footstep.RootElement.GetProperty("navigation").GetProperty("targetId").GetString(),
                "footstep correlation should use the committed route identity");
        }
    }

    private static void EmitsANewRouteWhenBoundaryStateChanges()
    {
        var writer = new RecordingProbeLineWriter();
        var now = new DateTime(2026, 7, 23, 17, 5, 0, DateTimeKind.Utc);
        using var probe = CreateProbe(writer, now);
        var position = Position(0);

        probe.PublishFootstep(Footstep(1, now, position, default));
        probe.PublishNavigation(Navigation(1, now, position, boundaryFingerprint: "none"));
        probe.CommitCycle(1, now);
        probe.PublishFootstep(Footstep(2, now.AddMilliseconds(100), position with { X = 40 }, default));
        probe.PublishNavigation(
            Navigation(
                2,
                now.AddMilliseconds(100),
                position with { X = 40 },
                boundaryFingerprint: "39"));
        probe.CommitCycle(2, now.AddMilliseconds(100));

        var routes = writer.Documents("route");
        Equal(2, routes.Count, "boundary change should publish a new route signature");
        NotEqual(
            routes[0].RootElement.GetProperty("signature").GetString(),
            routes[1].RootElement.GetProperty("signature").GetString(),
            "boundary change should alter deterministic signature");
    }

    private static void LabelsCachedNavigationAndRejectsForeignOrExpiredState()
    {
        var writer = new RecordingProbeLineWriter();
        var now = new DateTime(2026, 7, 23, 17, 10, 0, DateTimeKind.Utc);
        using var probe = CreateProbe(writer, now);
        var position = Position(0);

        probe.PublishNavigation(Navigation(1, now, position));
        probe.PublishFootstep(Footstep(2, now.AddMilliseconds(100), position with { X = 20 }, default));
        probe.CommitCycle(2, now.AddMilliseconds(100));

        var cached = writer.Documents("footstep")[0].RootElement.GetProperty("navigation");
        Equal(false, cached.GetProperty("sameCycle").GetBoolean(), "cached route must not claim simultaneity");
        Equal(100L, cached.GetProperty("ageMs").GetInt64(), "cached route age");
        Equal("coherent", cached.GetProperty("state").GetString(), "same-owner cached route state");

        probe.PublishNavigation(Navigation(3, now.AddMilliseconds(200), position with { FieldId = 121 }));
        probe.PublishFootstep(Footstep(3, now.AddMilliseconds(200), position with { X = 40 }, default));
        probe.CommitCycle(3, now.AddMilliseconds(200));
        var foreign = writer.Documents("footstep")[1].RootElement.GetProperty("navigation");
        Equal("unavailable", foreign.GetProperty("state").GetString(), "foreign field route rejected");

        probe.PublishNavigation(Navigation(4, now.AddMilliseconds(300), position));
        probe.PublishFootstep(Footstep(5, now.AddMilliseconds(700), position with { X = 60 }, default));
        probe.CommitCycle(5, now.AddMilliseconds(700));
        var expired = writer.Documents("footstep")[2].RootElement.GetProperty("navigation");
        Equal("unavailable", expired.GetProperty("state").GetString(), "expired route rejected");
    }

    private static void ResetDropsPendingCorrelationButKeepsStrideStatistics()
    {
        var writer = new RecordingProbeLineWriter();
        var now = new DateTime(2026, 7, 23, 17, 15, 0, DateTimeKind.Utc);
        using var probe = CreateProbe(writer, now);
        var position = Position(0);

        _ = probe.ObserveMovement(position, now, true, true, FieldFootstepCadence.Walk, true);
        var accepted = probe.ObserveMovement(
            position with { X = 80 },
            now.AddMilliseconds(100),
            true,
            true,
            FieldFootstepCadence.Walk,
            true);
        probe.PublishFootstep(Footstep(1, now.AddMilliseconds(100), position with { X = 80 }, accepted));
        probe.ResetCorrelation();
        probe.CommitCycle(1, now.AddMilliseconds(100));

        Equal(0, writer.Documents("footstep").Count, "reset should discard pending footstep");
        Equal(1, probe.GetFieldSummary(120).Walk.SampleCount, "reset should retain session calibration");
    }

    private static void JsonlWriterAppendsAcrossSessions()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ff7-x64-footstep-probe-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "probe.jsonl");
        try
        {
            using (var writer = new Steam2026JsonlProbeLineWriter(path, _ => { }))
            {
                Equal(true, writer.TryEnqueue("{\"kind\":\"first\"}"), "first append accepted");
            }

            using (var writer = new Steam2026JsonlProbeLineWriter(path, _ => { }))
            {
                Equal(true, writer.TryEnqueue("{\"kind\":\"second\"}"), "second append accepted");
            }

            var lines = File.ReadAllLines(path);
            Equal(2, lines.Length, "writer should append rather than truncate");
            Equal("{\"kind\":\"first\"}", lines[0], "first session line preserved");
            Equal("{\"kind\":\"second\"}", lines[1], "second session line appended");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Steam2026FieldFootstepNavigationProbe CreateProbe(
        ISteam2026ProbeLineWriter writer,
        DateTime now) =>
        new(
            new FieldFootstepDistanceProbe(reportEverySamples: 1),
            writer,
            "test-fingerprint",
            now,
            TimeSpan.FromMilliseconds(250),
            _ => { });

    private static Steam2026FootstepProbeSample Footstep(
        long cycle,
        DateTime observedAt,
        FieldPositionSnapshot position,
        FieldFootstepDistanceProbeObservation distance) =>
        new(
            cycle,
            observedAt,
            position,
            HasControl: true,
            FieldFootstepCadence.Walk,
            distance,
            TrackName: "nmkin_1_41_159",
            SoundId: 5052,
            FileName: "5052.ogg",
            Steam2026FootstepMappingScope.Triangle,
            Source: "Cosmo nmkin_1_41_159/5052",
            PlaybackSucceeded: true);

    private static Steam2026NavigationProbeSnapshot Navigation(
        long cycle,
        DateTime observedAt,
        FieldPositionSnapshot position,
        string boundaryFingerprint = "none",
        string controllerTargetId = "story:120:biggs",
        string routeTargetId = "story:120:biggs")
    {
        var guidance = new FieldNavigationRouteGuidance(
            new FieldNavigationRouteWaypoint(300, 400, 0),
            PortalIndex: 0,
            PortalCount: 2,
            RemainingDistance: 500,
            Replanned: false,
            NextAction: null,
            Diagnostic: "route retained");
        var route = new FieldNavigationRouteProbeSnapshot(
            position.FieldId,
            routeTargetId,
            9,
            [4, 7, 9],
            [
                new FieldNavigationRoutePortal(
                    4,
                    7,
                    new FieldNavigationRouteWaypoint(200, 250, 0),
                    new FieldNavigationRouteWaypoint(220, 270, 0)),
                new FieldNavigationRoutePortal(
                    7,
                    9,
                    new FieldNavigationRouteWaypoint(350, 400, 0),
                    new FieldNavigationRouteWaypoint(370, 420, 0))
            ],
            [
                new FieldNavigationRouteStep(new FieldNavigationRouteWaypoint(300, 400, 0), 1),
                new FieldNavigationRouteStep(new FieldNavigationRouteWaypoint(500, 600, 0), 2)
            ],
            PortalIndex: 0,
            WaypointIndex: 0,
            ResolvedTriangle: 4,
            guidance);
        var controller = new FieldNavigationControllerProbeSnapshot(
            BeaconEnabled: true,
            position.FieldId,
            FieldNavigationCategory.Story,
            controllerTargetId,
            "Talk to Biggs",
            500,
            600,
            0,
            route,
            "route retained");
        return new Steam2026NavigationProbeSnapshot(
            cycle,
            observedAt,
            position,
            Steam2026NavigationProbeAvailability.Coherent,
            ResolvedTriangle: 4,
            WalkmeshTriangleCount: 60,
            boundaryFingerprint,
            boundaryFingerprint == "none" ? [] : [39],
            controller,
            RoutePlannerDiagnostic: "route retained",
            StateDiagnostic: "coherent");
    }

    private static FieldPositionSnapshot Position(int x) =>
        new(FieldPositionReader.FieldModule, 120, 0, x, 0, 0, 41, 0);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected={expected}, actual={actual}");
        }
    }

    private static void NotEqual<T>(T first, T second, string label)
    {
        if (EqualityComparer<T>.Default.Equals(first, second))
        {
            throw new InvalidOperationException($"{label}: both values were {first}");
        }
    }

    private sealed class RecordingProbeLineWriter : ISteam2026ProbeLineWriter
    {
        private readonly List<string> lines = [];

        public bool TryEnqueue(string jsonLine)
        {
            lines.Add(jsonLine);
            return true;
        }

        public List<JsonDocument> Documents(string kind) =>
            lines
                .Select(line => JsonDocument.Parse(line))
                .Where(document =>
                    document.RootElement.TryGetProperty("kind", out var value) &&
                    string.Equals(value.GetString(), kind, StringComparison.Ordinal))
                .ToList();

        public void Dispose()
        {
        }
    }
}

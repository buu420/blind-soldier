using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Runtime.Abstractions;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using System.Text.Json;

internal static class Steam2026FieldFootstepCoordinatorTests
{
    internal static void Run()
    {
        PlaysMappedFootstepsOnlyForCoherentMovement();
        LeavesUnmappedCosmoSurfacesSilent();
        PublishesControlledMappedAndUnmappedProbeSamples();
    }

    private static void PlaysMappedFootstepsOnlyForCoherentMovement()
    {
        var config = new AccessibilityConfig
        {
            EnableFieldFootstepFeedback = true,
            FieldFootstepScanIntervalMs = 30,
            FieldNavigationSpeechDistanceUnitsPerCount = 1
        };
        var playback = new RecordingFootstepPlayback();
        using var coordinator = new Steam2026FieldFootstepCoordinator(
            config,
            new FieldFootstepTracker(TimeSpan.Zero, TimeSpan.Zero, 300),
            position => new Steam2026FootstepSelection(
                @"C:\mod\Assets\footsteps\cosmo\5000.ogg",
                $"field={position.FieldId},triangle={position.TriangleId}"),
            playback,
            _ => { });

        var start = new DateTime(2026, 7, 20, 20, 0, 0, DateTimeKind.Utc);
        coordinator.Observe(Frame(Field(0, hasControl: true)), isHostForeground: true, start);
        coordinator.Observe(Frame(Field(100, hasControl: true)), isHostForeground: true, start.AddSeconds(1));
        coordinator.Observe(Frame(Field(100, hasControl: true)), isHostForeground: true, start.AddSeconds(2));

        Equal(1, playback.Paths.Count, "one moving footstep");
        Equal(@"C:\mod\Assets\footsteps\cosmo\5000.ogg", playback.Paths[0], "mapped footstep path");

        coordinator.Observe(Frame(Field(200, hasControl: false)), isHostForeground: true, start.AddSeconds(3));
        Equal(2, playback.Paths.Count, "scripted-control movement still produces a real step");

        coordinator.Observe(Frame(RuntimeDomainUpdate<FieldFrameObservation>.Unchanged), isHostForeground: true, start.AddSeconds(4));
        coordinator.Observe(Frame(Field(300, hasControl: true)), isHostForeground: true, start.AddSeconds(5));
        Equal(2, playback.Paths.Count, "failed read resets without a recovery step");

        coordinator.Observe(Frame(Field(400, hasControl: true)), isHostForeground: false, start.AddSeconds(6));
        coordinator.Observe(Frame(Field(500, hasControl: true)), isHostForeground: true, start.AddSeconds(7));
        Equal(2, playback.Paths.Count, "foreground loss resets cadence");
    }

    private static void LeavesUnmappedCosmoSurfacesSilent()
    {
        var config = new AccessibilityConfig
        {
            EnableFieldFootstepFeedback = true,
            FieldFootstepScanIntervalMs = 30,
            FieldNavigationSpeechDistanceUnitsPerCount = 1
        };
        var playback = new RecordingFootstepPlayback();
        using var coordinator = new Steam2026FieldFootstepCoordinator(
            config,
            new FieldFootstepTracker(TimeSpan.Zero, TimeSpan.Zero, 300),
            _ => null,
            playback,
            _ => { });

        var start = new DateTime(2026, 7, 20, 20, 0, 0, DateTimeKind.Utc);
        coordinator.Observe(Frame(Field(0, hasControl: true)), isHostForeground: true, start);
        coordinator.Observe(Frame(Field(100, hasControl: true)), isHostForeground: true, start.AddSeconds(1));

        Equal(0, playback.Paths.Count, "unmapped Cosmo surface stays silent");
    }

    private static void PublishesControlledMappedAndUnmappedProbeSamples()
    {
        var now = new DateTime(2026, 7, 23, 18, 0, 0, DateTimeKind.Utc);
        var mappedWriter = new RecordingProbeLineWriter();
        using var mappedProbe = CreateProbe(mappedWriter, now);
        var mappedPlayback = new RecordingFootstepPlayback();
        using (var coordinator = new Steam2026FieldFootstepCoordinator(
                   CreateConfig(),
                   new FieldFootstepTracker(TimeSpan.Zero, TimeSpan.Zero, 300),
                   _ => new Steam2026FootstepSelection(
                       @"C:\mod\Assets\footsteps\cosmo\5052.ogg",
                       "Cosmo nmkin_1_41_159/5052",
                       "nmkin_1_41_159",
                       5052,
                       Steam2026FootstepMappingScope.Triangle),
                   mappedPlayback,
                   _ => { },
                   mappedProbe))
        {
            coordinator.Observe(Frame(Field(0, hasControl: true)), true, now, workerCycle: 1);
            coordinator.Observe(
                Frame(Field(100, hasControl: true)),
                true,
                now.AddMilliseconds(100),
                workerCycle: 2);
            mappedProbe.CommitCycle(2, now.AddMilliseconds(100));
        }

        var mapped = mappedWriter.Documents("footstep").Single().RootElement;
        Equal("triangle", mapped.GetProperty("surface").GetProperty("scope").GetString(), "mapped scope");
        Equal(5052, mapped.GetProperty("surface").GetProperty("soundId").GetInt32(), "mapped sound id");
        Equal(true, mapped.GetProperty("surface").GetProperty("playbackSucceeded").GetBoolean(), "mapped playback result");

        var unmappedWriter = new RecordingProbeLineWriter();
        using var unmappedProbe = CreateProbe(unmappedWriter, now);
        using (var coordinator = new Steam2026FieldFootstepCoordinator(
                   CreateConfig(),
                   new FieldFootstepTracker(TimeSpan.Zero, TimeSpan.Zero, 300),
                   _ => null,
                   new RecordingFootstepPlayback(),
                   _ => { },
                   unmappedProbe))
        {
            coordinator.Observe(Frame(Field(0, hasControl: true)), true, now, workerCycle: 3);
            coordinator.Observe(
                Frame(Field(100, hasControl: true)),
                true,
                now.AddMilliseconds(100),
                workerCycle: 4);
            unmappedProbe.CommitCycle(4, now.AddMilliseconds(100));
        }

        var unmapped = unmappedWriter.Documents("footstep").Single().RootElement;
        Equal("unmapped", unmapped.GetProperty("surface").GetProperty("scope").GetString(), "unmapped scope");
        Equal(false, unmapped.GetProperty("surface").GetProperty("playbackSucceeded").GetBoolean(), "unmapped playback result");

        var scriptedWriter = new RecordingProbeLineWriter();
        using var scriptedProbe = CreateProbe(scriptedWriter, now);
        using (var coordinator = new Steam2026FieldFootstepCoordinator(
                   CreateConfig(),
                   new FieldFootstepTracker(TimeSpan.Zero, TimeSpan.Zero, 300),
                   _ => new Steam2026FootstepSelection(@"C:\mod\step.ogg", "configured"),
                   new RecordingFootstepPlayback(),
                   _ => { },
                   scriptedProbe))
        {
            coordinator.Observe(Frame(Field(0, hasControl: true)), true, now, workerCycle: 5);
            coordinator.Observe(
                Frame(Field(100, hasControl: false)),
                true,
                now.AddMilliseconds(100),
                workerCycle: 6);
            scriptedProbe.CommitCycle(6, now.AddMilliseconds(100));
        }

        Equal(0, scriptedWriter.Documents("footstep").Count, "script-controlled movement stays out of probe");
    }

    private static AccessibilityConfig CreateConfig() =>
        new()
        {
            EnableFieldFootstepFeedback = true,
            FieldFootstepScanIntervalMs = 30,
            FieldNavigationSpeechDistanceUnitsPerCount = 1
        };

    private static Steam2026FieldFootstepNavigationProbe CreateProbe(
        ISteam2026ProbeLineWriter writer,
        DateTime now) =>
        new(
            new FieldFootstepDistanceProbe(reportEverySamples: 1),
            writer,
            "footstep-coordinator-test",
            now,
            TimeSpan.FromMilliseconds(250),
            _ => { });

    private static FieldFrameObservation Field(int x, bool hasControl) =>
        new(116, 1, x, 0, 0, 9, hasControl, 0, 0, 10);

    private static RuntimeFrameObservation Frame(FieldFrameObservation field) =>
        Frame(RuntimeDomainUpdate<FieldFrameObservation>.Present(field));

    private static RuntimeFrameObservation Frame(RuntimeDomainUpdate<FieldFrameObservation> field) =>
        new(
            DateTime.UtcNow,
            new GameLifecycleObservation(true, false, 1, 1),
            RuntimeDomainUpdate<MenuFrameObservation>.Unchanged,
            RuntimeDomainUpdate<DialoguePageObservation>.Unchanged,
            field,
            RuntimeDomainUpdate<BattleFrameObservation>.Unchanged,
            RuntimeDomainUpdate<NavigationWorldObservation>.Unchanged);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private sealed class RecordingFootstepPlayback : ISteam2026FootstepPlayback
    {
        internal List<string> Paths { get; } = [];

        public bool Play(string path, string reason)
        {
            Paths.Add(path);
            return true;
        }

        public void Dispose() { }
    }

    private sealed class RecordingProbeLineWriter : ISteam2026ProbeLineWriter
    {
        private readonly List<string> lines = [];

        public bool TryEnqueue(string jsonLine)
        {
            lines.Add(jsonLine);
            return true;
        }

        internal List<JsonDocument> Documents(string kind) =>
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

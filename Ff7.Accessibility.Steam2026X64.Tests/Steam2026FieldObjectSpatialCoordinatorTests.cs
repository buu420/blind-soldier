using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Field;
using System.Runtime.CompilerServices;

internal static class Steam2026FieldObjectSpatialCoordinatorTests
{
    [ModuleInitializer]
    internal static void RunAtModuleLoad() => Run();

    internal static void Run()
    {
        PlaysAuthoritativeObjectTargetsWithSpatialGainAndPulseCadence();
        StopsAndReprimesAcrossForegroundSuppressionAndModuleLoss();
        TransientReadLossSuspendsObservationButPreservesPlaybackCadenceAndFieldIdentity();
        IgnoresNonObjectAndForeignFieldTargets();
        UsesOnlyTheThreeExactObjectCueAssets();
    }

    private static void PlaysAuthoritativeObjectTargetsWithSpatialGainAndPulseCadence()
    {
        var config = Config();
        var playback = new RecordingObjectCuePlayback();
        using var coordinator = new Steam2026FieldObjectSpatialCoordinator(
            config,
            new FieldObjectProximityCueTracker(10, 110, 0, TimeSpan.FromSeconds(1)),
            playback,
            _ => { });
        var position = Position();
        var targets = new[]
        {
            Target(FieldObjectCueKind.Materia, "materia", 10, 0),
            Target(FieldObjectCueKind.Chest, "chest", 60, 0),
            Target(FieldObjectCueKind.Item, "item", 85, 0)
        };
        var now = new DateTime(2026, 7, 20, 21, 0, 0, DateTimeKind.Utc);

        coordinator.Observe(
            position,
            new FieldNavigationControlTransform(0),
            targets,
            isHostForeground: true,
            isSuppressed: false,
            isReadCoherent: true,
            now);

        Equal(3, playback.Calls.Count, "all three authoritative object kinds play");
        Equal(FieldObjectCueKind.Materia, playback.Calls[0].Kind, "materia kind");
        Near(1f, playback.Calls[0].Gain, "inner-range gain");
        Equal(FieldObjectCueKind.Chest, playback.Calls[1].Kind, "chest kind");
        Near(0.5f, playback.Calls[1].Gain, "mid-range gain");
        Equal(FieldObjectCueKind.Item, playback.Calls[2].Kind, "item kind");
        Near(0.25f, playback.Calls[2].Gain, "outer-range gain");
        Equal("materia", playback.Calls[0].Cue.TargetLabel, "authoritative target label");
        Equal(10d, playback.Calls[0].Cue.DistanceUnits, "spatialized distance");

        coordinator.Observe(
            position,
            new FieldNavigationControlTransform(0),
            targets,
            isHostForeground: true,
            isSuppressed: false,
            isReadCoherent: true,
            now.AddMilliseconds(999));
        Equal(3, playback.Calls.Count, "pulse interval suppresses duplicate cues");

        coordinator.Observe(
            position,
            new FieldNavigationControlTransform(0),
            targets,
            isHostForeground: true,
            isSuppressed: false,
            isReadCoherent: true,
            now.AddSeconds(1));
        Equal(6, playback.Calls.Count, "pulse resumes at the configured interval");
    }

    private static void StopsAndReprimesAcrossForegroundSuppressionAndModuleLoss()
    {
        var playback = new RecordingObjectCuePlayback();
        using var coordinator = new Steam2026FieldObjectSpatialCoordinator(
            Config(),
            new FieldObjectProximityCueTracker(10, 110, 0, TimeSpan.FromMinutes(1)),
            playback,
            _ => { });
        var position = Position();
        var targets = new[] { Target(FieldObjectCueKind.Item, "item", 10, 0) };
        var now = new DateTime(2026, 7, 20, 21, 10, 0, DateTimeKind.Utc);

        ObserveValid(coordinator, position, targets, now);
        Equal(1, playback.Calls.Count, "initial cue");

        coordinator.Observe(position, default, targets, false, false, true, now.AddSeconds(1));
        Equal(1, playback.StopAllCount, "foreground loss stops active cues");
        ObserveValid(coordinator, position, targets, now.AddSeconds(2));
        Equal(2, playback.Calls.Count, "foreground recovery reprimes tracker");

        coordinator.Observe(position, default, targets, true, true, true, now.AddSeconds(3));
        Equal(2, playback.StopAllCount, "suppression stops active cues");
        ObserveValid(coordinator, position, targets, now.AddSeconds(4));
        Equal(3, playback.Calls.Count, "suppression recovery reprimes tracker");

        coordinator.Observe(
            position with { CurrentModule = 0 },
            default,
            targets,
            true,
            false,
            true,
            now.AddSeconds(5));
        Equal(3, playback.StopAllCount, "module loss stops active cues");
        ObserveValid(coordinator, position, targets, now.AddSeconds(6));
        Equal(4, playback.Calls.Count, "module recovery reprimes tracker");
    }

    private static void TransientReadLossSuspendsObservationButPreservesPlaybackCadenceAndFieldIdentity()
    {
        var playback = new RecordingObjectCuePlayback();
        var logs = new List<string>();
        using var coordinator = new Steam2026FieldObjectSpatialCoordinator(
            Config(),
            new FieldObjectProximityCueTracker(10, 110, 0, TimeSpan.FromMinutes(1)),
            playback,
            logs.Add);
        var position = Position();
        var targets = new[] { Target(FieldObjectCueKind.Item, "item", 10, 0) };
        var now = new DateTime(2026, 7, 20, 21, 15, 0, DateTimeKind.Utc);

        ObserveValid(coordinator, position, targets, now);
        Equal(1, playback.Calls.Count, "initial object cue");

        coordinator.Observe(
            position,
            default,
            targets,
            isHostForeground: true,
            isSuppressed: false,
            isReadCoherent: false,
            now.AddMilliseconds(100),
            "state changed between checked reads");
        Equal(0, playback.StopAllCount, "transient read loss leaves one-shot playback active");

        coordinator.Observe(
            position,
            default,
            targets,
            isHostForeground: true,
            isSuppressed: false,
            isReadCoherent: false,
            now.AddMilliseconds(150),
            "state changed between checked reads");
        Equal(0, playback.StopAllCount, "continued read loss leaves one-shot playback active");
        Equal(
            1,
            logs.Count(message => message.Contains(
                "cue observation suspended for transient read failure",
                StringComparison.Ordinal)),
            "continued read loss logs one suspension transition");

        ObserveValid(coordinator, position, targets, now.AddMilliseconds(200));
        Equal(1, playback.Calls.Count, "same-field recovery preserves pulse cadence");

        var nextFieldPosition = position with { FieldId = 117 };
        var nextFieldTargets = new[]
        {
            Target(FieldObjectCueKind.Item, "next field item", 10, 0) with { FieldId = 117 }
        };
        ObserveValid(coordinator, nextFieldPosition, nextFieldTargets, now.AddMilliseconds(250));
        Equal(2, playback.Calls.Count, "real field transition reprimes tracker");
        Equal(1, playback.StopAllCount, "real field transition stops active playback");
        Equal(
            true,
            logs.Any(message => message.Contains("from=116, to=117", StringComparison.Ordinal)),
            "transient read loss preserves active field identity");
    }

    private static void IgnoresNonObjectAndForeignFieldTargets()
    {
        var playback = new RecordingObjectCuePlayback();
        using var coordinator = new Steam2026FieldObjectSpatialCoordinator(
            Config(),
            new FieldObjectProximityCueTracker(10, 110, 0, TimeSpan.Zero),
            playback,
            _ => { });
        var targets = new[]
        {
            Target(FieldObjectCueKind.None, "save point", 10, 0),
            Target(FieldObjectCueKind.Item, "foreign item", 10, 0) with { FieldId = 117 }
        };

        ObserveValid(
            coordinator,
            Position(),
            targets,
            new DateTime(2026, 7, 20, 21, 20, 0, DateTimeKind.Utc));

        Equal(0, playback.Calls.Count, "non-cue and foreign-field targets stay silent");
    }

    private static void UsesOnlyTheThreeExactObjectCueAssets()
    {
        Equal(
            "object_materia_190_pitch70.wav",
            Steam2026FieldObjectSpatialPlayback.ResolveSoundFileName(FieldObjectCueKind.Materia),
            "materia asset");
        Equal(
            "object_chest_253_pitch70.wav",
            Steam2026FieldObjectSpatialPlayback.ResolveSoundFileName(FieldObjectCueKind.Chest),
            "chest asset");
        Equal(
            "object_item_357_pitch70.wav",
            Steam2026FieldObjectSpatialPlayback.ResolveSoundFileName(FieldObjectCueKind.Item),
            "item asset");
        Equal(
            null,
            Steam2026FieldObjectSpatialPlayback.ResolveSoundFileName(FieldObjectCueKind.None),
            "unsupported kinds have no fallback asset");
    }

    private static AccessibilityConfig Config() => new()
    {
        EnableFieldObjectProximityCues = true,
        FieldObjectCueInnerRangeUnits = 10,
        FieldObjectCueOuterRangeUnits = 110,
        FieldObjectCueClusterRadiusUnits = 0,
        FieldObjectCueIntervalMs = 1000,
        FieldObjectCueVolumePercent = 100
    };

    private static FieldPositionSnapshot Position() =>
        new(FieldPositionReader.FieldModule, 116, 0, 0, 0, 0, 0, 0);

    private static FieldNavigationTarget Target(
        FieldObjectCueKind kind,
        string label,
        int x,
        int y) =>
        new(116, FieldNavigationCategory.Objects, label, x, y, 0, label, kind);

    private static void ObserveValid(
        Steam2026FieldObjectSpatialCoordinator coordinator,
        FieldPositionSnapshot position,
        IReadOnlyList<FieldNavigationTarget> targets,
        DateTime now) =>
        coordinator.Observe(
            position,
            new FieldNavigationControlTransform(0),
            targets,
            isHostForeground: true,
            isSuppressed: false,
            isReadCoherent: true,
            now);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected={expected}, actual={actual}");
        }
    }

    private static void Near(float expected, float actual, string label)
    {
        if (Math.Abs(expected - actual) > 0.0001f)
        {
            throw new InvalidOperationException($"{label}: expected={expected}, actual={actual}");
        }
    }

    private sealed class RecordingObjectCuePlayback : ISteam2026FieldObjectSpatialPlayback
    {
        internal List<PlaybackCall> Calls { get; } = new();
        internal int StopAllCount { get; private set; }

        public bool Play(FieldObjectCueKind kind, NavigationBeaconCue cue, float gain)
        {
            Calls.Add(new PlaybackCall(kind, cue, gain));
            return true;
        }

        public void StopAll() => StopAllCount++;

        public void Dispose()
        {
        }
    }

    private readonly record struct PlaybackCall(
        FieldObjectCueKind Kind,
        NavigationBeaconCue Cue,
        float Gain);
}

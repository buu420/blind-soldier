using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Field;

internal enum Steam2026FootstepMappingScope
{
    Triangle,
    Field,
    ConfiguredFallback,
    Unmapped
}

internal readonly record struct Steam2026FootstepProbeSample(
    long WorkerCycle,
    DateTime ObservedAtUtc,
    FieldPositionSnapshot Position,
    bool HasControl,
    FieldFootstepCadence Cadence,
    FieldFootstepDistanceProbeObservation Distance,
    string TrackName,
    int SoundId,
    string FileName,
    Steam2026FootstepMappingScope MappingScope,
    string Source,
    bool PlaybackSucceeded);

internal enum Steam2026NavigationProbeAvailability
{
    Coherent,
    Disabled,
    Suppressed,
    Incoherent,
    Unavailable,
    Faulted
}

internal sealed record Steam2026NavigationProbeSnapshot(
    long WorkerCycle,
    DateTime ObservedAtUtc,
    FieldPositionSnapshot Position,
    Steam2026NavigationProbeAvailability Availability,
    int ResolvedTriangle,
    int WalkmeshTriangleCount,
    string BoundaryFingerprint,
    IReadOnlyList<int> ActiveBoundaryTriangles,
    FieldNavigationControllerProbeSnapshot Controller,
    string RoutePlannerDiagnostic,
    string StateDiagnostic);

internal interface ISteam2026ProbeLineWriter : IDisposable
{
    bool TryEnqueue(string jsonLine);
}

internal sealed class Steam2026JsonlProbeLineWriter : ISteam2026ProbeLineWriter
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

    private readonly string path;
    private readonly Action<string> log;
    private readonly Channel<string> channel;
    private readonly Task writerTask;
    private int disposed;
    private int faulted;
    private int faultLogged;

    internal Steam2026JsonlProbeLineWriter(string path, Action<string> log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        channel = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        writerTask = Task.Run(WriteLoopAsync);
    }

    public bool TryEnqueue(string jsonLine)
    {
        ArgumentNullException.ThrowIfNull(jsonLine);
        return Volatile.Read(ref disposed) == 0 &&
               Volatile.Read(ref faulted) == 0 &&
               channel.Writer.TryWrite(jsonLine);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        channel.Writer.TryComplete();
        try
        {
            writerTask.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LogFaultOnce(ex);
        }
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 16 * 1024);
            var lastFlush = DateTime.UtcNow;
            await foreach (var line in channel.Reader.ReadAllAsync())
            {
                await writer.WriteLineAsync(line);
                var now = DateTime.UtcNow;
                if (now - lastFlush >= FlushInterval)
                {
                    await writer.FlushAsync();
                    lastFlush = now;
                }
            }

            await writer.FlushAsync();
        }
        catch (Exception ex)
        {
            Volatile.Write(ref faulted, 1);
            LogFaultOnce(ex);
        }
    }

    private void LogFaultOnce(Exception ex)
    {
        if (Interlocked.Exchange(ref faultLogged, 1) != 0)
        {
            return;
        }

        log(
            $"Native Steam 2026 footstep/navigation probe writer disabled: " +
            $"{ex.GetType().Name}: {ex.Message}");
    }
}

internal sealed class Steam2026FieldFootstepNavigationProbe : IDisposable
{
    internal const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly FieldFootstepDistanceProbe distanceProbe;
    private readonly ISteam2026ProbeLineWriter writer;
    private readonly TimeSpan maximumNavigationAge;
    private readonly Action<string> log;
    private readonly Queue<Steam2026FootstepProbeSample> pendingFootsteps = new();
    private Steam2026NavigationProbeSnapshot? latestNavigation;
    private string lastRouteSignature = string.Empty;
    private int enqueueFailureLogged;
    private int disposed;

    internal Steam2026FieldFootstepNavigationProbe(
        FieldFootstepDistanceProbe distanceProbe,
        ISteam2026ProbeLineWriter writer,
        string runtimeFingerprint,
        DateTime sessionStartUtc,
        TimeSpan maximumNavigationAge,
        Action<string> log)
    {
        this.distanceProbe = distanceProbe ?? throw new ArgumentNullException(nameof(distanceProbe));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeFingerprint);
        this.maximumNavigationAge = maximumNavigationAge > TimeSpan.Zero
            ? maximumNavigationAge
            : TimeSpan.FromMilliseconds(250);
        this.log = log ?? throw new ArgumentNullException(nameof(log));

        WriteRecord(
            new
            {
                schemaVersion = SchemaVersion,
                kind = "session",
                startedAtUtc = sessionStartUtc,
                runtimeFingerprint
            });
    }

    internal bool HasPendingFootstep => pendingFootsteps.Count != 0;

    internal FieldFootstepDistanceProbeObservation ObserveMovement(
        FieldPositionSnapshot position,
        DateTime nowUtc,
        bool isHostForeground,
        bool hasControl,
        FieldFootstepCadence cadence,
        bool footstepTriggered) =>
        distanceProbe.ObserveControlled(
            position,
            nowUtc,
            isHostForeground,
            hasControl,
            cadence,
            footstepTriggered);

    internal FieldFootstepDistanceProbeSummary GetFieldSummary(int fieldId) =>
        distanceProbe.GetFieldSummary(fieldId);

    internal void PublishFootstep(Steam2026FootstepProbeSample sample)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (!sample.HasControl || !FieldPositionReader.IsUsable(sample.Position))
        {
            return;
        }

        pendingFootsteps.Enqueue(sample);
    }

    internal void PublishNavigation(Steam2026NavigationProbeSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        latestNavigation = snapshot;
    }

    internal void CommitCycle(long workerCycle, DateTime nowUtc)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        while (pendingFootsteps.TryPeek(out var pending) &&
               pending.WorkerCycle <= workerCycle)
        {
            pendingFootsteps.Dequeue();
            CommitFootstep(pending, nowUtc);
        }
    }

    internal void ResetCorrelation()
    {
        pendingFootsteps.Clear();
        latestNavigation = null;
        lastRouteSignature = string.Empty;
        distanceProbe.ResetCurrentStride();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        pendingFootsteps.Clear();
        writer.Dispose();
    }

    private void CommitFootstep(
        Steam2026FootstepProbeSample footstep,
        DateTime nowUtc)
    {
        var navigation = ResolveNavigation(footstep);
        string? routeSignature = null;
        if (navigation is { Controller.BeaconEnabled: true, Controller.Route: not null } coherent)
        {
            routeSignature = CreateRouteSignature(coherent);
            if (!string.Equals(lastRouteSignature, routeSignature, StringComparison.Ordinal))
            {
                WriteRoute(coherent, routeSignature);
                lastRouteSignature = routeSignature;
            }
        }

        var navigationPayload = CreateNavigationPayload(
            footstep,
            navigation,
            routeSignature);
        var distance = footstep.Distance;
        WriteRecord(
            new
            {
                schemaVersion = SchemaVersion,
                kind = "footstep",
                workerCycle = footstep.WorkerCycle,
                observedAtUtc = footstep.ObservedAtUtc,
                committedAtUtc = nowUtc,
                fieldId = footstep.Position.FieldId,
                modelIndex = footstep.Position.ModelIndex,
                position = new
                {
                    x = footstep.Position.X,
                    y = footstep.Position.Y,
                    z = footstep.Position.Z
                },
                nativeTriangle = footstep.Position.TriangleId,
                cadence = footstep.Cadence,
                strideDistanceUnits = distance.AcceptedSample
                    ? distance.SampleDistanceUnits
                    : (double?)null,
                strideReport = distance.Report,
                surface = new
                {
                    scope = footstep.MappingScope,
                    footstep.TrackName,
                    footstep.SoundId,
                    footstep.FileName,
                    footstep.Source,
                    footstep.PlaybackSucceeded
                },
                navigation = navigationPayload
            });
    }

    private Steam2026NavigationProbeSnapshot? ResolveNavigation(
        Steam2026FootstepProbeSample footstep)
    {
        if (latestNavigation is not { } navigation ||
            navigation.Position.FieldId != footstep.Position.FieldId ||
            navigation.Position.ModelIndex != footstep.Position.ModelIndex)
        {
            return null;
        }

        var age = footstep.ObservedAtUtc - navigation.ObservedAtUtc;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return age <= maximumNavigationAge ? navigation : null;
    }

    private static object CreateNavigationPayload(
        Steam2026FootstepProbeSample footstep,
        Steam2026NavigationProbeSnapshot? navigation,
        string? routeSignature)
    {
        if (navigation is not { } snapshot)
        {
            return new
            {
                state = "unavailable",
                sameCycle = false,
                ageMs = (long?)null,
                routeSignature = (string?)null
            };
        }

        var age = footstep.ObservedAtUtc - snapshot.ObservedAtUtc;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        var route = snapshot.Controller.Route;
        return new
        {
            state = ToState(snapshot.Availability),
            sameCycle = snapshot.WorkerCycle == footstep.WorkerCycle,
            ageMs = (long)Math.Round(age.TotalMilliseconds),
            routeSignature,
            resolvedTriangle = snapshot.ResolvedTriangle,
            walkmeshTriangleCount = snapshot.WalkmeshTriangleCount,
            boundaryFingerprint = snapshot.BoundaryFingerprint,
            beaconEnabled = snapshot.Controller.BeaconEnabled,
            category = snapshot.Controller.Category,
            targetId = route?.TargetId ?? snapshot.Controller.TargetId,
            currentPortal = route?.PortalIndex,
            currentWaypoint = route?.Guidance.Waypoint,
            remainingDistance = route?.Guidance.RemainingDistance,
            replanned = route?.Guidance.Replanned
        };
    }

    private void WriteRoute(
        Steam2026NavigationProbeSnapshot navigation,
        string signature)
    {
        var controller = navigation.Controller;
        var route = controller.Route!;
        WriteRecord(
            new
            {
                schemaVersion = SchemaVersion,
                kind = "route",
                observedAtUtc = navigation.ObservedAtUtc,
                workerCycle = navigation.WorkerCycle,
                signature,
                state = ToState(navigation.Availability),
                fieldId = navigation.Position.FieldId,
                modelIndex = navigation.Position.ModelIndex,
                nativeTriangle = navigation.Position.TriangleId,
                navigation.ResolvedTriangle,
                navigation.WalkmeshTriangleCount,
                target = new
                {
                    category = controller.Category,
                    id = route.TargetId,
                    label = controller.TargetLabel,
                    x = controller.TargetX,
                    y = controller.TargetY,
                    z = controller.TargetZ,
                    triangle = route.TargetTriangle
                },
                route.TrianglePath,
                portals = route.Portals.Select(
                    portal => new
                    {
                        portal.FromTriangle,
                        portal.ToTriangle,
                        portal.Left,
                        portal.Right,
                        portal.TransitionKind,
                        portal.TransitionId,
                        portal.RequiredInput,
                        portal.TransitionExit,
                        portal.RequiresAction
                    }),
                stableWaypoints = route.StableWaypoints.Select(
                    step => new
                    {
                        step.Waypoint,
                        step.RequiredPortalIndex
                    }),
                current = new
                {
                    route.PortalIndex,
                    route.WaypointIndex,
                    route.ResolvedTriangle,
                    route.Guidance.Waypoint,
                    route.Guidance.RemainingDistance,
                    route.Guidance.Replanned,
                    route.Guidance.NextAction
                },
                boundary = new
                {
                    fingerprint = navigation.BoundaryFingerprint,
                    activeTriangles = navigation.ActiveBoundaryTriangles
                },
                diagnostics = new
                {
                    controller.Diagnostic,
                    route = route.Guidance.Diagnostic,
                    planner = navigation.RoutePlannerDiagnostic,
                    state = navigation.StateDiagnostic
                }
            });
    }

    private static string CreateRouteSignature(
        Steam2026NavigationProbeSnapshot navigation)
    {
        var controller = navigation.Controller;
        var route = controller.Route!;
        var value = new StringBuilder()
            .Append(route.FieldId).Append('|')
            .Append(route.TargetId).Append('|')
            .Append(route.TargetTriangle).Append('|')
            .AppendJoin(',', route.TrianglePath).Append('|');
        foreach (var portal in route.Portals)
        {
            value
                .Append(portal.FromTriangle).Append('>')
                .Append(portal.ToTriangle).Append(':')
                .Append(portal.Left.X).Append(',')
                .Append(portal.Left.Y).Append(',')
                .Append(portal.Left.Z).Append(':')
                .Append(portal.Right.X).Append(',')
                .Append(portal.Right.Y).Append(',')
                .Append(portal.Right.Z).Append(';');
        }

        value.Append('|');
        foreach (var step in route.StableWaypoints)
        {
            value
                .Append(step.Waypoint.X).Append(',')
                .Append(step.Waypoint.Y).Append(',')
                .Append(step.Waypoint.Z).Append('@')
                .Append(step.RequiredPortalIndex).Append(';');
        }

        value.Append('|').Append(navigation.BoundaryFingerprint);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }

    private void WriteRecord(object record)
    {
        var json = JsonSerializer.Serialize(record, JsonOptions);
        if (writer.TryEnqueue(json) ||
            Interlocked.Exchange(ref enqueueFailureLogged, 1) != 0)
        {
            return;
        }

        log("Native Steam 2026 footstep/navigation probe stopped accepting records.");
    }

    private static string ToState(Steam2026NavigationProbeAvailability availability) =>
        availability.ToString().ToLowerInvariant();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

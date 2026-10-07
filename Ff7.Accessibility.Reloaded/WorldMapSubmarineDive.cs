using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

internal sealed record WorldMapSubmarineDiveDestination(string TargetId, string Label)
{
    internal Func<WorldMapStateSnapshot, bool> CanDiveAt { get; init; } = static _ => true;
    internal int? NativeModelId { get; init; }
}

/// <summary>wm2.ev allocation and collection gates, in world script bank 0.</summary>
internal readonly record struct WorldMapSubmarineAvailability(bool RedWreckPresent, bool KeyPresent, bool MateriaCollected)
{
    // FUN_00763C35 assigns bank0 to DC08DC; FUN_00764B8C indexes a bit by n >> 3.
    internal const uint WreckFlagsAddress = 0x00DC08DC + (6787 >> 3);
    internal const uint CollectionFlagsAddress = 0x00DC08DC + (6800 >> 3);

    internal static WorldMapSubmarineAvailability? Read(ILegacyAddressSpace memory)
    {
        if (!memory.TryReadByte(WreckFlagsAddress, out var firstWreck) ||
            !memory.TryReadByte(CollectionFlagsAddress, out var firstCollection) ||
            !memory.TryReadByte(WreckFlagsAddress, out var secondWreck) ||
            !memory.TryReadByte(CollectionFlagsAddress, out var secondCollection) ||
            firstWreck != secondWreck || firstCollection != secondCollection) return null;
        return new((firstWreck & (1 << (6787 & 7))) != 0,
            (firstCollection & (1 << (6801 & 7))) == 0,
            (firstCollection & (1 << (6800 & 7))) != 0);
    }
}

public sealed partial class WorldMapTargetCatalog
{
    internal WorldMapTargetCatalog? UnderwaterCatalog { get; set; }
    internal Func<WorldMapSubmarineAvailability?> ReadSubmarineAvailability { get; set; } = static () => null;
    private readonly Dictionary<int, IReadOnlySet<int>> diveAreas = new();
    private readonly Dictionary<(int Surface, int Underwater), IReadOnlySet<int>> checkedDiveAreas = new();

    private IReadOnlyList<WorldMapNavigationTarget> ReadSurfaceSubmarineTargets(
        WorldMapNavigationCategory category, WorldMapStateSnapshot state)
    {
        if (!triangleResolver.TryResolveReachableComponent(state, out var component)) return [];
        if (!diveAreas.TryGetValue(component, out var areas))
        {
            areas = map.Triangles.Where(t => t.TerrainId == 3 &&
                triangleResolver.GetComponentId(13, 0, t.Id) == component).Select(t => t.Id).ToHashSet();
            diveAreas.Add(component, areas);
        }
        if (areas.Count == 0) return [];
        WorldMapNavigationTarget? Row(string label, string id, WorldMapSubmarineDiveDestination destination,
            int underwaterComponent = -1)
        {
            bool SafeEntry(WorldMapStateSnapshot s)
            {
                if (UnderwaterCatalog is not { } underwater) return destination.TargetId.Length == 0;
                return WorldMapRoutePlanner.HasSubmarineFootprint(underwater.map, s.X, s.Z) &&
                    WorldMapBroncoLanding.TryFindSurface(underwater.map, s.X, s.Z, out var floor) &&
                    (underwaterComponent < 0 || underwater.triangleResolver.GetComponentId(13, 2, floor.Id) == underwaterComponent);
            }
            var usable = areas;
            if (UnderwaterCatalog is not null)
            {
                if (!checkedDiveAreas.TryGetValue((component, underwaterComponent), out usable))
                {
                    usable = areas.Where(id => SafeEntry(state with
                        { X = map.Triangles[id].Centroid.X, Z = map.Triangles[id].Centroid.Z })).ToHashSet();
                    checkedDiveAreas.Add((component, underwaterComponent), usable);
                }
            }
            if (usable.Count == 0) return null;
            var triangle = usable.Select(id => map.Triangles[id])
                .MinBy(t => WrappedDistanceSquared(map, state.X, state.Z, t.Centroid.X, t.Centroid.Z))!;
            return new(category, category == WorldMapNavigationCategory.Story ? WorldMapTargetKind.Story :
                category == WorldMapNavigationCategory.Events ? WorldMapTargetKind.Event : WorldMapTargetKind.Location,
                label, triangle.Centroid.X, triangle.Centroid.Y, triangle.Centroid.Z, triangle.Id,
                triangle.RegionId & 31, id, usable) { SubmarineDiveDestination = destination with { CanDiveAt = SafeEntry } };
        }
        if (category == WorldMapNavigationCategory.Events)
            return Row("Dive underwater", "world-submarine:dive", new(string.Empty, "Underwater")) is { } dive ? [dive] : [];
        if (UnderwaterCatalog is null) return [];
        var availability = ReadSubmarineAvailability();
        return UnderwaterCatalog.Locations
            .Where(t => t.SubmarineSurfacingPoint is not null || t.NativeUnderwaterArrival?.ModelId switch
            {
                17 => true,
                26 => availability?.KeyPresent == true,
                28 => availability?.RedWreckPresent == true,
                _ => false
            })
            .Where(t => category != WorldMapNavigationCategory.Story ||
                t.NativeUnderwaterArrival?.ModelId == 26 && state.GameMoment == 1396 ||
                t.NativeUnderwaterArrival?.ModelId == 28 && availability?.MateriaCollected == false)
            .Select(t => Row(category == WorldMapNavigationCategory.Story && t.NativeUnderwaterArrival?.ModelId == 28
                ? "Huge Materia, red submarine wreck" : t.Label,
                $"world-submarine:dive:{t.StableId}", new(t.StableId, t.Label) { NativeModelId = t.NativeUnderwaterArrival?.ModelId },
                UnderwaterCatalog.triangleResolver.GetComponentId(13, 2, t.TriangleId)))
            .Where(t => t is not null).Select(t => t!).ToArray();
    }
}

/// <summary>
/// Keeps a chosen fixed underwater destination across the game's own map0-to-map2 reload.
/// The only automatic action is a bounded, configured Cancel press on native Sea terrain.
/// </summary>
public sealed class WorldMapSubmarineJourney
{
    private enum Phase { Sailing, Ready, Neutral, Pressing, AwaitingMap }
    private WorldMapNavigationController? owner;
    private WorldMapSubmarineDiveDestination? destination;
    private Phase phase;
    private int surfaceProgress;
    private DateTime neutralSince;
    private DateTime deliveredAt;
    private DateTime unavailableSince;
    private string? lastAnnouncement;
    private bool autoWalk;
    private bool diveAccepted;
    internal Func<WorldMapSubmarineDiveState?> ReadNativeDiveState { get; set; } = static () => null;
    internal Func<WorldMapSubmarineAvailability?> ReadSubmarineAvailability { get; set; } = static () => null;
    internal bool RequestsDive { get; private set; }
    internal bool IsAtDivePoint => destination is not null && phase != Phase.Sailing;
    internal bool IsHoldingDestination => destination is not null;

    public static WorldMapSubmarineJourney Attach(IEnumerable<WorldMapRuntimeContext> contexts, ILegacyAddressSpace memory)
    {
        var loaded = contexts.ToArray();
        var journey = new WorldMapSubmarineJourney();
        var controls = new WorldMapDialogueReader(memory);
        journey.ReadNativeDiveState = () => controls.TryReadSubmarineDiveState(out var state) ? state : null;
        journey.ReadSubmarineAvailability = () => WorldMapSubmarineAvailability.Read(memory);
        var underwater = loaded.FirstOrDefault(c => c.Map.WorldMapType == 2)?.Catalog;
        foreach (var context in loaded)
        {
            context.Navigation.SubmarineJourney = journey;
            context.Catalog.UnderwaterCatalog = underwater;
            context.Catalog.ReadSubmarineAvailability = () => WorldMapSubmarineAvailability.Read(memory);
        }
        return journey;
    }

    internal void Start(WorldMapNavigationController controller, WorldMapSubmarineDiveDestination goal, WorldMapStateSnapshot state)
    {
        if (ReferenceEquals(owner, controller) && destination?.TargetId == goal.TargetId && destination?.Label == goal.Label) return;
        Cancel();
        owner = controller;
        destination = goal;
        surfaceProgress = state.WorldProgress;
        phase = Phase.Sailing;
    }

    internal void NoteAutoWalk(bool enabled) { autoWalk = enabled; if (!enabled) Pause(); }
    internal void Cancel(WorldMapNavigationController? controller = null)
    {
        if (controller is not null && !ReferenceEquals(owner, controller)) return;
        owner = null; destination = null; RequestsDive = false; autoWalk = false;
        deliveredAt = neutralSince = unavailableSince = default;
        lastAnnouncement = null; diveAccepted = false; phase = Phase.Sailing;
    }

    internal void Pause()
    {
        RequestsDive = false;
        // Once delivered, never re-press Cancel: that could surface again after the reload.
        if (deliveredAt != default) phase = Phase.AwaitingMap;
        else if (phase is Phase.Neutral or Phase.Pressing) { phase = Phase.Ready; neutralSince = default; }
    }

    internal void NoteDelivered(DateTime now)
    {
        if (RequestsDive && deliveredAt == default) deliveredAt = now;
    }

    /// <summary>Called before the host scan throttle, so a pulse releases even on a slow scan.</summary>
    public bool ObserveNativeTransition(DateTime now)
    {
        if (destination is null || deliveredAt == default) return false;
        if (ReadNativeDiveState()?.DiveAccepted == true) diveAccepted = true;
        if (phase != Phase.Pressing || now - deliveredAt < TimeSpan.FromMilliseconds(150)) return false;
        phase = Phase.AwaitingMap; RequestsDive = false; return true;
    }

    internal WorldMapNavigationOutput? ObserveSurface(WorldMapNavigationController controller,
        WorldMapStateSnapshot state, bool atDivePoint, DateTime now, bool automaticWalkActive, uint? nativeInput)
    {
        RequestsDive = false;
        if (!ReferenceEquals(owner, controller) || destination is null) return null;
        if (state.PlayerModelId != 13 || state.WorldMapType != 0 || state.WorldProgress != surfaceProgress)
            return Fail("Submarine route changed. Navigation off.");
        if (deliveredAt != default)
        {
            if (ReadNativeDiveState()?.DiveAccepted == true) diveAccepted = true;
            if (now < deliveredAt || !diveAccepted && now - deliveredAt > TimeSpan.FromSeconds(8))
                return Fail("The game did not confirm the dive. Auto walk stopped. Choose the underwater destination again.");
            if (phase == Phase.Pressing && (nativeInput is { } mask && (mask & 0x40) != 0 ||
                now - deliveredAt >= TimeSpan.FromMilliseconds(150))) phase = Phase.AwaitingMap;
            RequestsDive = phase == Phase.Pressing;
            return new(null);
        }
        if (!atDivePoint) { phase = Phase.Sailing; neutralSince = default; return null; }
        if (phase == Phase.Sailing) phase = Phase.Ready;
        if (!automaticWalkActive || !autoWalk)
            return Announce($"At diveable sea for {destination.Label}. Press Cancel to dive. Navigation stays on.");
        if (!state.HasNativeControlMode || state.NativeCameraMode is not (0 or 1 or 2) ||
            state.NativeFrameMultiplier is < 1 or > 4 || nativeInput is null)
            return Fail("Cannot confirm the submarine dive controls. Auto walk stopped. Navigation off.");
        if (ReadNativeDiveState() is not { } nativeState)
            return Fail("Cannot confirm the game's dive state. Auto walk stopped. Navigation off.");
        if (!nativeState.CanRequestDive)
        {
            neutralSince = default; phase = Phase.Ready;
            return Announce("Waiting for the game to allow diving.");
        }
        if ((nativeInput.Value & 0x40) != 0)
        {
            neutralSince = default;
            phase = Phase.Ready;
            return Announce("Release Cancel so the submarine can dive.");
        }
        if (neutralSince == default) { neutralSince = now; phase = Phase.Neutral; }
        if (now - neutralSince >= TimeSpan.FromMilliseconds(100))
        {
            phase = Phase.Pressing;
            RequestsDive = true;
        }
        return Announce($"Diving for {destination.Label}.");
    }

    internal bool TryReadUnderwaterGoal(WorldMapStateSnapshot state, DateTime now,
        out WorldMapSubmarineDiveDestination goal, out bool automatic)
    {
        goal = null!; automatic = false;
        if (destination is null || state.WorldMapType != 2 || state.PlayerModelId != 13) return false;
        if (deliveredAt != default && now < deliveredAt)
        { Cancel(); return false; }
        goal = destination; automatic = autoWalk; return true;
    }

    internal WorldMapNavigationOutput WaitForUnderwaterTarget(DateTime now)
    {
        if (destination is not { } goal) return new(null);
        if (ReadSubmarineAvailability() is { } flags &&
            (goal.NativeModelId == 26 && !flags.KeyPresent || goal.NativeModelId == 28 && !flags.RedWreckPresent))
            return Fail($"Underwater. {goal.Label} is no longer available. Navigation off.");
        if (unavailableSince == default) unavailableSince = now;
        else if (now < unavailableSince || now - unavailableSince > TimeSpan.FromSeconds(8))
            return Fail($"Cannot confirm {goal.Label} after diving. Navigation off. Choose the destination again.");
        return Announce($"Underwater. Waiting to confirm {goal.Label}.");
    }

    private WorldMapNavigationOutput Fail(string text) { Cancel(); return new(text, StopAutoWalk: true); }
    private WorldMapNavigationOutput Announce(string text)
    {
        var speech = lastAnnouncement == text ? null : text;
        lastAnnouncement = text;
        return new(speech);
    }
}

public sealed partial class WorldMapNavigationController
{
    public WorldMapSubmarineJourney? SubmarineJourney { get; set; }
    public bool AutomaticInputRequestsSubmarineDive { get; private set; }
    public void NoteSubmarineDiveDelivered(DateTime now) => SubmarineJourney?.NoteDelivered(now);

    private WorldMapNavigationOutput? TryContinueUnderwaterJourney(WorldMapStateSnapshot state, DateTime now)
    {
        if (SubmarineJourney is not { } journey ||
            !journey.TryReadUnderwaterGoal(state, now, out var goal, out var automatic)) return null;
        if (goal.TargetId.Length == 0)
        {
            journey.Cancel();
            return new("Underwater. Choose a destination in Locations.", StopAutoWalk: true);
        }
        var target = targetProvider(state, WorldMapNavigationCategory.Locations)
            .FirstOrDefault(t => t.StableId == goal.TargetId);
        if (target is null)
            return journey.WaitForUnderwaterTarget(now);
        journey.Cancel();
        var output = StartNavigation(target, state, now, announceOn: false);
        return output is { } started ? started with
        { Speech = $"Underwater. Continuing to {goal.Label}. {started.Speech}", StartAutoWalk = automatic && beaconEnabled } : null;
    }
}

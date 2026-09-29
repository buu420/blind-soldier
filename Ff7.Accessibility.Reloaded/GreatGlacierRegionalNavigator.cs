using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>What the next leg of a regional treasure route is.</summary>
public enum GreatGlacierLegKind
{
    /// <summary>One of this screen's native exits.</summary>
    Exit,

    /// <summary>The goal itself, on this screen: the treasure, the hot spring or Snow.</summary>
    Goal,

    /// <summary>The start of the ice-floe crossing on this shore; the player solves the floes.</summary>
    IceFloeCrossing,

    /// <summary>A snowfield exit.</summary>
    SnowfieldLocation
}

public sealed record GreatGlacierFieldLeg(
    GreatGlacierLegKind Kind,
    FieldNavigationTarget Target,
    double EstimatedTicks,
    string Diagnostic);

public sealed record GreatGlacierWorldLeg(
    WorldMapNavigationTarget Target,
    double EstimatedTicks,
    string Diagnostic);

/// <summary>
/// One Great Glacier treasure the player asked to be taken to, kept across every screen,
/// passage reload, snowfield crossing, battle, menu and the Glacier Map until it is picked
/// up, the player cancels or chooses something else, or the party leaves the region. The
/// field and world-map controllers each walk only the leg in front of them and ask for the
/// next one from where the party actually is.
/// </summary>
public sealed class GreatGlacierObjective
{
    internal GreatGlacierObjective(GreatGlacierTreasure treasure, string label)
    {
        Treasure = treasure;
        Label = label;
    }

    public GreatGlacierTreasure Treasure { get; }

    public string Label { get; }

    /// <summary>The player asked to be walked, not just told the way.</summary>
    public bool AutoWalk { get; internal set; }

    /// <summary>Held until the player chooses it again (after a collapse, or a native step skipped).</summary>
    public bool Paused { get; internal set; }

    /// <summary>The native step the last leg was planned for.</summary>
    public GreatGlacierGoalPoint? Goal { get; internal set; }
}

/// <summary>
/// The regional treasure list and routes of the Great Glacier, shared by the field and the
/// world-map navigation of one runtime. Both runtimes build it the same way; only how memory
/// is read differs.
///
/// <para>The route table (<see cref="GreatGlacierRegionGraph"/>) is built once from the
/// installed data on a background task, never on the polling thread. Until it is ready the
/// list is still offered - it needs only the native collection flags - and a route asked for
/// meanwhile waits for it.</para>
/// </summary>
public sealed class GreatGlacierRegionalNavigator
{
    public const string RowIdPrefix = "glacier-item:";

    /// <summary>Far enough from any field position that a regional row is never the nearest selection.</summary>
    private const int RowOffset = 1_000_000;

    private readonly Func<int, byte?> readBank1;
    private readonly Func<int, string?> resolveItemName;
    private readonly Func<int, string?> resolveMateriaName;
    private readonly Action<string>? log;
    private readonly IReadOnlyList<FieldNavigationObjectDefinition> objectDefinitions;
    private readonly object sync = new();
    private string? fieldRoot;
    private Ff7GameLanguageContext? fieldLanguage;
    private (WorldMapData Map, IReadOnlyList<WorldMapNavigationTarget> Locations, IReadOnlySet<int> Entrances, string ArchivePath)? snowfield;
    private Task? buildTask;
    private GreatGlacierRegionGraph? graph;
    private string? buildFailure;

    // The field the party was last seen on, for telling a real arrival from a resample.
    private int lastFieldId = -1;
    private int nodeState = -1;
    private int lastX;
    private int lastY;
    private DateTime pendingStateSince;
    private bool sawPositionGap;

    public GreatGlacierRegionalNavigator(
        Func<int, byte?> readBank1,
        Func<int, string?>? resolveItemName = null,
        Func<int, string?>? resolveMateriaName = null,
        Action<string>? log = null,
        IReadOnlyList<FieldNavigationObjectDefinition>? objectDefinitions = null)
    {
        this.readBank1 = readBank1 ?? throw new ArgumentNullException(nameof(readBank1));
        this.resolveItemName = resolveItemName ?? (_ => null);
        this.resolveMateriaName = resolveMateriaName ?? (_ => null);
        this.log = log;
        this.objectDefinitions = objectDefinitions ?? FieldNavigationObjectCatalog.CreateAllFields();
    }

    /// <summary>The live bank 1 byte 184 on a passage screen, or null.</summary>
    public Func<int?> ReadCorridorState { get; set; } = static () => null;

    /// <summary>The live ice-floe state in hyou5_2, or null.</summary>
    public Func<GreatGlacierIceFloeState?> ReadIceFloes { get; set; } = static () => null;

    public GreatGlacierObjective? Objective { get; private set; }

    public GreatGlacierRegionGraph? Graph
    {
        get
        {
            lock (sync)
            {
                return graph;
            }
        }
    }

    public string? BuildFailure
    {
        get
        {
            lock (sync)
            {
                return buildFailure;
            }
        }
    }

    public bool IsBuilding
    {
        get
        {
            lock (sync)
            {
                return buildTask is { IsCompleted: false };
            }
        }
    }

    /// <summary>The installed field data the route table is read from.</summary>
    public void AttachFieldData(string gameRootDirectory, Ff7GameLanguageContext? language)
    {
        lock (sync)
        {
            fieldRoot = gameRootDirectory;
            fieldLanguage = language;
        }

        TryStartBuild();
    }

    /// <summary>The snowfield's WM3.MAP, its native exits, and the world archive with wm3.ev and field.tbl.</summary>
    public void AttachSnowfield(
        WorldMapData map,
        IReadOnlyList<WorldMapNavigationTarget> locations,
        IReadOnlySet<int> entranceTriangles,
        string worldArchivePath)
    {
        if (map.WorldMapType != GreatGlacierRegion.SnowfieldWorldMapType)
        {
            return;
        }

        lock (sync)
        {
            snowfield = (map, locations, entranceTriangles, worldArchivePath);
        }

        TryStartBuild();
    }

    /// <summary>Uses an already built table (tests, or a host that built it itself).</summary>
    public void UseGraph(GreatGlacierRegionGraph value)
    {
        lock (sync)
        {
            graph = value;
            buildFailure = null;
        }
    }

    /// <summary>Waits for a build already started; for tests and diagnostics only.</summary>
    public bool WaitForGraph(TimeSpan timeout)
    {
        Task? task;
        lock (sync)
        {
            task = buildTask;
        }

        return task is null ? Graph is not null : task.Wait(timeout) && Graph is not null;
    }

    private void TryStartBuild()
    {
        lock (sync)
        {
            if (buildTask is not null || graph is not null || fieldRoot is null || snowfield is null)
            {
                return;
            }

            var root = fieldRoot;
            var language = fieldLanguage;
            var world = snowfield.Value;
            buildTask = Task.Run(() =>
            {
                try
                {
                    var built = BuildGraph(root, language, world.Map, world.Locations, world.Entrances, world.ArchivePath, objectDefinitions);
                    lock (sync)
                    {
                        graph = built;
                    }

                    log?.Invoke(
                        $"Great Glacier regional routes ready: nodes={built.Nodes.Count}, edges={built.Edges.Count}, " +
                        $"built in {built.BuildTime.TotalMilliseconds:0} ms off the polling thread.");
                }
                catch (Exception ex)
                {
                    lock (sync)
                    {
                        buildFailure = ex.Message;
                    }

                    log?.Invoke($"Great Glacier regional routes unavailable: {ex.GetType().Name}: {ex.Message}");
                }
            });
        }
    }

    /// <summary>Builds the table from an installed game root, as the background task does.</summary>
    public static GreatGlacierRegionGraph BuildGraph(
        string gameRootDirectory,
        Ff7GameLanguageContext? language,
        WorldMapData snowfieldMap,
        IReadOnlyList<WorldMapNavigationTarget> snowfieldLocations,
        IReadOnlySet<int> entranceTriangles,
        string worldArchivePath,
        IReadOnlyList<FieldNavigationObjectDefinition>? objectDefinitions = null)
    {
        // Its own readers: nothing the polling thread holds is touched from here.
        var catalog = language is { } context
            ? new FieldScriptNavigationCatalog(gameRootDirectory, context)
            : new FieldScriptNavigationCatalog(gameRootDirectory);
        var flevel = language is { } flevelContext
            ? new FlevelDataSource(gameRootDirectory, flevelContext)
            : new FlevelDataSource(gameRootDirectory);
        var archive = new LgpArchiveReader(worldArchivePath);
        if (!archive.TryReadFile("field.tbl", out var fieldTable) || !archive.TryReadFile("wm3.ev", out var events))
        {
            throw new InvalidDataException($"field.tbl or wm3.ev is missing from {worldArchivePath}.");
        }

        return GreatGlacierRegionGraph.Build(new GreatGlacierRegionInputs(
            catalog,
            field => flevel.TryReadField(field, out var encoded) ? Ff7LzsDecoder.DecodeFieldFile(encoded) : null,
            snowfieldMap,
            snowfieldLocations,
            entranceTriangles,
            fieldTable,
            events,
            objectDefinitions ?? FieldNavigationObjectCatalog.CreateAllFields()));
    }

    // ---------------------------------------------------------------- objective lifecycle

    public bool HasObjective => Objective is not null;

    public bool IsRowId(string stableId) => stableId.StartsWith(RowIdPrefix, StringComparison.Ordinal);

    public static bool TryParseRow(string stableId, out GreatGlacierTreasure treasure)
    {
        treasure = default;
        return stableId.StartsWith(RowIdPrefix, StringComparison.Ordinal) &&
               Enum.TryParse(stableId[RowIdPrefix.Length..], out treasure);
    }

    public static string RowId(GreatGlacierTreasure treasure) => $"{RowIdPrefix}{treasure}";

    public GreatGlacierObjective Start(GreatGlacierTreasure treasure, bool autoWalk)
    {
        Objective = new GreatGlacierObjective(treasure, NameOf(treasure)) { AutoWalk = autoWalk };
        log?.Invoke($"Great Glacier regional route started: {treasure}, autoWalk={autoWalk}.");
        return Objective;
    }

    public void Cancel(string reason)
    {
        if (Objective is { } objective)
        {
            log?.Invoke($"Great Glacier regional route to {objective.Treasure} ended: {reason}.");
        }

        Objective = null;
    }

    public void Pause(string reason)
    {
        if (Objective is not { } objective)
        {
            return;
        }

        objective.Paused = true;
        objective.AutoWalk = false;
        log?.Invoke($"Great Glacier regional route to {objective.Treasure} paused: {reason}.");
    }

    /// <summary>Chosen again: an objective that was paused carries on from where the party is.</summary>
    public bool Resume(bool autoWalk)
    {
        if (Objective is not { Paused: true } objective)
        {
            return false;
        }

        objective.Paused = false;
        objective.AutoWalk = autoWalk;
        return true;
    }

    public void NoteAutoWalk(bool walking)
    {
        if (Objective is { } objective)
        {
            objective.AutoWalk = walking;
        }
    }

    /// <summary>
    /// Every host calls this with the native module each frame. The title screen is where a
    /// save is loaded or a new game begun, and whatever was asked for before it belongs to a
    /// game that is no longer running.
    /// </summary>
    public void ObserveModule(int module)
    {
        if (module == TitleMenuCursorReader.TitleModule)
        {
            if (Objective is not null)
            {
                Cancel("title screen (load or new game)");
            }

            ForgetPosition();
            if (domain != 0)
            {
                // Whatever is loaded next is another game: every leg planned before is retired.
                WorldEntries++;
                FieldEntries++;
            }

            domain = 0;
            return;
        }

        // Only the two navigation domains count: a battle, the results or a menu is an
        // excursion that returns to the same domain, and its legs are suspended, not retired.
        var next = module switch
        {
            FieldPositionReader.FieldModule => FieldPositionReader.FieldModule,
            WorldMapStateReader.WorldModule => WorldMapStateReader.WorldModule,
            _ => domain
        };
        if (next == domain)
        {
            return;
        }

        if (next == WorldMapStateReader.WorldModule)
        {
            WorldEntries++;
        }
        else if (next == FieldPositionReader.FieldModule)
        {
            FieldEntries++;
        }

        domain = next;
    }

    private int domain;

    /// <summary>
    /// How many times the game has moved from the field to the world map. A field leg planned
    /// before the latest move belongs to a screen the party has left: the field controller
    /// retires it, whatever became of the treasure it served.
    /// </summary>
    public int WorldEntries { get; private set; }

    /// <summary>How many times the game has moved from the world map to a field (see <see cref="WorldEntries"/>).</summary>
    public int FieldEntries { get; private set; }

    /// <summary>
    /// The native collection bytes the regional rows depend on, so a cached list is never kept
    /// across a change of them (a pickup, or another save loaded).
    /// </summary>
    public int CollectionSignature() =>
        (readBank1(37) ?? 0x100) | ((readBank1(GreatGlacierRegion.QuestFlagAddress) ?? 0x100) << 9);

    /// <summary>
    /// The host could not load an input the route table needs (WM3.MAP, the world archive or
    /// the location metadata): routes asked for say so once instead of waiting for ever.
    /// </summary>
    public void MarkUnavailable(string reason)
    {
        lock (sync)
        {
            if (graph is null && buildFailure is null)
            {
                buildFailure = reason;
            }
        }

        log?.Invoke($"Great Glacier regional routes unavailable: {reason}");
    }

    /// <summary>
    /// How long a route may wait for the table before it gives up. The table normally builds in
    /// one or two seconds after the hosts attach their data at start-up.
    /// </summary>
    public static readonly TimeSpan PreparationLimit = TimeSpan.FromSeconds(30);

    public void ForgetPosition()
    {
        lastFieldId = -1;
        nodeState = -1;
        pendingStateSince = default;
        sawPositionGap = false;
    }

    /// <summary>
    /// Records where the party is and says whether it has just been put somewhere by the game:
    /// a new field, or a passage screen reloaded with a new byte 184 (every same-screen passage
    /// jump writes one, then MAPJUMPs into the same field, which the engine loads afresh). The
    /// byte is written before the jump, so a changed byte alone is not yet an arrival: the
    /// reload shows as the party being placed somewhere else, a gap in the position reads, or
    /// the new byte simply holding. A resample of the same place is never an arrival.
    /// </summary>
    public GreatGlacierArrival ObserveField(int fieldId, int? corridorState, int x, int y, DateTime now)
    {
        var state = GreatGlacierRegion.IsPassage(fieldId) ? corridorState ?? -1 : 0;
        var arrival = GreatGlacierArrival.None;
        if (lastFieldId < 0)
        {
            arrival = GreatGlacierArrival.FirstSample;
            nodeState = state;
            pendingStateSince = default;
        }
        else if (lastFieldId != fieldId)
        {
            // No exit of a glacier screen leads into the cabin; Cloud's own script does, when
            // the exhaustion counter (bank 4 word 6) passes 544 and he collapses.
            arrival = GreatGlacierRegion.IsGlacierScreen(lastFieldId) &&
                      fieldId is GreatGlacierRegion.CabinFrontRoom or GreatGlacierRegion.CabinBackRoom
                ? GreatGlacierArrival.CollapsedToCabin
                : GreatGlacierArrival.NewField;
            nodeState = state;
            pendingStateSince = default;
        }
        else if (state >= 0 && nodeState < 0)
        {
            nodeState = state;
        }
        else if (state >= 0 && state != nodeState)
        {
            var placed = Math.Abs(x - lastX) + Math.Abs(y - lastY) > 64;
            if (placed || sawPositionGap || (pendingStateSince != default && now - pendingStateSince >= SameFieldReloadSettle))
            {
                arrival = GreatGlacierArrival.SameFieldReload;
                nodeState = state;
                pendingStateSince = default;
            }
            else if (pendingStateSince == default)
            {
                pendingStateSince = now;
            }
        }
        else
        {
            pendingStateSince = default;
        }

        lastFieldId = fieldId;
        lastX = x;
        lastY = y;
        sawPositionGap = false;
        return arrival;
    }

    /// <summary>How long a new byte 184 may hold on the same screen before it counts as the reload.</summary>
    public static readonly TimeSpan SameFieldReloadSettle = TimeSpan.FromSeconds(2);

    /// <summary>The field's position could not be read: a load is under way.</summary>
    public void NotePositionGap() => sawPositionGap = true;

    /// <summary>The world map is between two fields: the next field is a new one.</summary>
    public void ObserveWorldMap() => lastFieldId = 0;

    public GreatGlacierGoalPoint? CurrentGoal(GreatGlacierTreasure treasure, out bool readable) =>
        GreatGlacierRegion.CurrentGoal(treasure, readBank1, out readable);

    // ---------------------------------------------------------------- names and rows

    public string NameOf(GreatGlacierTreasure treasure)
    {
        var definition = GreatGlacierRegion.Definition(treasure);
        var native = objectDefinitions.FirstOrDefault(candidate =>
            candidate.FieldId == definition.FieldId && candidate.EntityId == definition.EntityId);
        var name = native.Kind switch
        {
            FieldNavigationObjectKind.Item when native.FieldId != 0 => resolveItemName(native.NativeId),
            FieldNavigationObjectKind.Materia when native.FieldId != 0 => resolveMateriaName(native.NativeId) is { Length: > 0 } materia
                ? $"{materia} Materia"
                : null,
            _ => null
        };
        return string.IsNullOrWhiteSpace(name) ? definition.FallbackName : name;
    }

    /// <summary>
    /// The regional part of a row's label: where it is and, for Alexander, the native steps
    /// still in front of it. A remote row never says the treasure is here.
    /// </summary>
    public string RowLabel(GreatGlacierTreasure treasure, GreatGlacierGoalPoint goal) =>
        goal.Stage switch
        {
            GreatGlacierGoalStage.HotSpring => $"{NameOf(treasure)}, elsewhere in the Great Glacier; first the hot spring, then Snow",
            GreatGlacierGoalStage.Snow => $"{NameOf(treasure)}, elsewhere in the Great Glacier; first Snow",
            _ => $"{NameOf(treasure)}, elsewhere in the Great Glacier"
        };

    /// <summary>
    /// This screen's Objects with a row for every Great Glacier treasure not yet picked up
    /// that is not already standing here as a live object. Nothing is added outside the
    /// region or while a flag cannot be read.
    /// </summary>
    public IReadOnlyList<FieldNavigationTarget> AppendFieldRows(
        FieldPositionSnapshot position,
        IReadOnlyList<FieldNavigationTarget> localObjects)
    {
        if (!FieldPositionReader.IsUsable(position) || !GreatGlacierRegion.IsRegionField(position.FieldId))
        {
            return localObjects;
        }

        List<FieldNavigationTarget>? rows = null;
        foreach (var definition in GreatGlacierRegion.Treasures)
        {
            if (CurrentGoal(definition.Treasure, out var readable) is not { } goal || !readable)
            {
                continue;
            }

            var localPrefix = $"object:{definition.FieldId}:{definition.EntityId}:";
            if (position.FieldId == definition.FieldId &&
                localObjects.Any(target => target.StableId.StartsWith(localPrefix, StringComparison.Ordinal)))
            {
                // Here and visible: its own live row is the one to use.
                continue;
            }

            rows ??= new List<FieldNavigationTarget>(localObjects);
            rows.Add(new FieldNavigationTarget(
                position.FieldId,
                FieldNavigationCategory.Objects,
                RowLabel(definition.Treasure, goal),
                position.X + RowOffset,
                position.Y + RowOffset,
                position.Z,
                RowId(definition.Treasure)));
        }

        return rows ?? localObjects;
    }

    // ---------------------------------------------------------------- field legs

    /// <summary>
    /// The last <see cref="PlanFieldLeg"/> found the goal's own screen and state but not the goal
    /// itself on offer: a wait, not a route.
    /// </summary>
    public bool LastPlanAwaitsGoal { get; private set; }

    /// <summary>
    /// The leg to walk now from where the party stands in a region field: the goal itself
    /// when it is here and routable, the crossing start when the best way on is over the
    /// lake, or the native exit whose walk plus the best route on from where it leads is the
    /// least estimated walking. Every candidate is validated on the live walkmesh and locks by
    /// <paramref name="liveLength"/>; nothing the live planner cannot route is chosen.
    /// </summary>
    public GreatGlacierFieldLeg? PlanFieldLeg(
        FieldPositionSnapshot position,
        GreatGlacierGoalPoint goal,
        int? corridorState,
        IReadOnlyList<FieldNavigationTarget> liveExits,
        IReadOnlyList<FieldNavigationTarget> liveObjects,
        IReadOnlyList<FieldNavigationTarget> liveNpcs,
        Func<FieldNavigationTarget, double?> liveLength,
        out string failure)
    {
        failure = string.Empty;
        LastPlanAwaitsGoal = false;
        if (Graph is not { } table)
        {
            failure = BuildFailure is { } error ? $"the Glacier route table could not be built ({error})" : "the Glacier route table is still being prepared";
            return null;
        }

        var field = position.FieldId;
        var state = GreatGlacierRegion.IsPassage(field) ? corridorState ?? -1 : 0;
        if (state < 0)
        {
            failure = "this passage screen's native state could not be read";
            return null;
        }

        var candidates = new List<GreatGlacierFieldLeg>();
        var notes = new List<string>();

        // The goal itself, when it stands on this screen now.
        if (field == goal.FieldId && (goal.RequiredCorridorState is not { } required || required == state))
        {
            var goalTarget = goal.Stage == GreatGlacierGoalStage.Snow
                ? liveNpcs.FirstOrDefault(target => target.StableId == $"npc:{goal.FieldId}:{goal.EntityId}")
                : liveObjects.FirstOrDefault(target => target.StableId.StartsWith($"object:{goal.FieldId}:{goal.EntityId}:", StringComparison.Ordinal));
            if (goalTarget.StableId is { Length: > 0 })
            {
                if (liveLength(goalTarget) is { } length)
                {
                    candidates.Add(new GreatGlacierFieldLeg(GreatGlacierLegKind.Goal, goalTarget,
                        GreatGlacierMovementCost.Field(length), $"goal {goal.Key} {length:0} units"));
                }
                else
                {
                    notes.Add($"goal {goal.Key} not routable here");
                }
            }
            else
            {
                // The goal belongs to this screen and this passage state, but the game is not
                // offering it (yet): its model not placed, Snow not yet standing. No exit gets
                // any nearer - every way out only leads back into this room - so the leg waits
                // for it, within the caller's bounded grace, instead of circling out and in.
                LastPlanAwaitsGoal = true;
                failure = goal.Stage switch
                {
                    GreatGlacierGoalStage.Snow => "Snow is not here to talk to",
                    GreatGlacierGoalStage.HotSpring => "the hot spring is not offered on this screen now",
                    _ => "it is not showing on this screen now"
                };
                return null;
            }
        }

        var onLake = field == GreatGlacierRegion.LakeField;
        var northShore = GreatGlacierRegionGraph.IsNorthShore(position.Y);
        foreach (var exit in liveExits)
        {
            if (onLake && exit.TriggerLine is { } line &&
                GreatGlacierRegionGraph.IsNorthShore((line.StartY + line.EndY) / 2) != northShore)
            {
                continue;
            }

            if (table.ExitDestination(field, state, exit) is not { } next)
            {
                continue;
            }

            var onward = table.CostToGoal(goal, next);
            if (double.IsPositiveInfinity(onward))
            {
                continue;
            }

            if (liveLength(exit) is not { } length)
            {
                notes.Add($"{exit.StableId} not routable");
                continue;
            }

            candidates.Add(new GreatGlacierFieldLeg(GreatGlacierLegKind.Exit, exit,
                GreatGlacierMovementCost.Field(length) + GreatGlacierMovementCost.ScreenChangeTieBreak + onward,
                $"{exit.StableId} -> {next.Key}: walk {length:0} + onward {onward:0}"));
        }

        if (onLake)
        {
            var startEntity = northShore ? GreatGlacierRegion.NorthShoreCrossingEntity : GreatGlacierRegion.SouthShoreCrossingEntity;
            var landing = northShore ? GreatGlacierRegion.SouthShoreLanding : GreatGlacierRegion.NorthShoreLanding;
            var crossing = liveObjects.FirstOrDefault(target =>
                target.StableId.StartsWith($"object:{field}:{startEntity}:", StringComparison.Ordinal));
            if (crossing.StableId is { Length: > 0 } &&
                table.FindNode(field, landing.X, landing.Y, landing.Triangle, 0) is { } far &&
                !double.IsPositiveInfinity(table.CostToGoal(goal, far)) &&
                liveLength(crossing) is { } walk)
            {
                candidates.Add(new GreatGlacierFieldLeg(GreatGlacierLegKind.IceFloeCrossing, crossing,
                    GreatGlacierMovementCost.Field(walk) + GreatGlacierMovementCost.IceFloeCrossingTicks + table.CostToGoal(goal, far),
                    $"ice floes from the {(northShore ? "north" : "south")} shore: walk {walk:0}"));
            }
        }

        if (candidates.Count == 0)
        {
            failure = notes.Count == 0
                ? "no native way on from here reaches it"
                : $"no native way on from here reaches it ({string.Join("; ", notes.Take(4))})";
            return null;
        }

        return candidates.OrderBy(candidate => candidate.EstimatedTicks).ThenBy(candidate => candidate.Target.StableId, StringComparer.Ordinal).First();
    }

    // ---------------------------------------------------------------- snowfield legs

    /// <summary>
    /// The snowfield exit to walk to now: the one whose walk from here plus the best route on
    /// from where it enters is least. Each walk is the live world planner's own route, which
    /// already keeps clear of every other native entrance.
    /// </summary>
    public GreatGlacierWorldLeg? PlanWorldLeg(
        GreatGlacierGoalPoint goal,
        IReadOnlyList<WorldMapNavigationTarget> locations,
        Func<WorldMapNavigationTarget, double?> liveLength,
        out string failure)
    {
        failure = string.Empty;
        if (Graph is not { } table)
        {
            failure = BuildFailure is { } error ? $"the Glacier route table could not be built ({error})" : "the Glacier route table is still being prepared";
            return null;
        }

        GreatGlacierWorldLeg? best = null;
        foreach (var location in locations)
        {
            if (GreatGlacierRegionGraph.SnowfieldLocationId(location) is not { } id ||
                table.FindWorldNode(id) is not { } entry)
            {
                continue;
            }

            // Where the exit enters: the one edge out of its own entry node that is its own trigger.
            var enters = table.Edges.FirstOrDefault(edge => edge.From == entry.Index && edge.Kind == GreatGlacierRegionGraph.EdgeKind.Snowfield &&
                                                            edge.ExitId == location.StableId);
            if (enters is null)
            {
                continue;
            }

            var onward = table.CostToGoal(goal, table.Nodes[enters.To]);
            if (double.IsPositiveInfinity(onward) || liveLength(location) is not { } length)
            {
                continue;
            }

            var ticks = GreatGlacierMovementCost.World(length) + GreatGlacierMovementCost.ScreenChangeTieBreak + onward;
            if (best is null || ticks < best.EstimatedTicks)
            {
                best = new GreatGlacierWorldLeg(location, ticks, $"{location.StableId}: walk {length:0} + onward {onward:0}");
            }
        }

        if (best is null)
        {
            failure = "no snowfield exit leads toward it from here";
        }

        return best;
    }

    /// <summary>
    /// The snowfield's Objects: one row per treasure not yet picked up, each routed to the
    /// snowfield exit its best route leaves by, and labelled with that exit.
    /// </summary>
    public IReadOnlyList<WorldMapNavigationTarget> ReadWorldRows(
        WorldMapStateSnapshot state,
        IReadOnlyList<WorldMapNavigationTarget> locations,
        Func<WorldMapNavigationTarget, double?> liveLength)
    {
        if (state.WorldMapType != GreatGlacierRegion.SnowfieldWorldMapType)
        {
            return [];
        }

        var lengths = new Dictionary<string, double?>(StringComparer.Ordinal);
        double? Length(WorldMapNavigationTarget location) =>
            lengths.TryGetValue(location.StableId, out var cached) ? cached : lengths[location.StableId] = liveLength(location);
        var rows = new List<WorldMapNavigationTarget>();
        foreach (var definition in GreatGlacierRegion.Treasures)
        {
            if (CurrentGoal(definition.Treasure, out var readable) is not { } goal || !readable)
            {
                continue;
            }

            var label = RowLabel(definition.Treasure, goal);
            var leg = PlanWorldLeg(goal, locations, Length, out _);
            rows.Add(leg is null
                ? new WorldMapNavigationTarget(WorldMapNavigationCategory.Objects, WorldMapTargetKind.Location, label,
                    state.X, state.Y, state.Z, -1, state.RegionId, RowId(definition.Treasure), new HashSet<int>())
                : leg.Target with
                {
                    Category = WorldMapNavigationCategory.Objects,
                    Label = $"{label}. First to {leg.Target.Label}",
                    StableId = RowId(definition.Treasure)
                });
        }

        return rows;
    }
}

public enum GreatGlacierArrival
{
    None,
    FirstSample,
    NewField,
    SameFieldReload,
    CollapsedToCabin
}

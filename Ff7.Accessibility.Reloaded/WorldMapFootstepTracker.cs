namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Converts accepted native world-player movement into a stable footstep
/// cadence.  It deliberately watches the post-collision position rather than
/// input state, so walking into a wall cannot create phantom footsteps.
/// </summary>
public sealed class WorldMapFootstepTracker
{
    public static readonly TimeSpan DefaultWalkingInterval = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan DefaultChocoboInterval = TimeSpan.FromMilliseconds(500);

    private const int CaughtChocoboModelId = 4;
    private const int AlternateRiddenChocoboModelId = 19;
    private const double MaximumContinuousDelta = WorldMapDataLoader.MeshSize / 2d;

    private readonly int wrapWidth;
    private readonly int wrapHeight;
    private readonly TimeSpan walkingInterval;
    private readonly TimeSpan chocoboInterval;
    private WorldMapStateSnapshot? previous;
    private DateTime lastFootstepAt = DateTime.MinValue;

    public WorldMapFootstepTracker(
        int wrapWidth,
        int wrapHeight,
        TimeSpan? walkingInterval = null,
        TimeSpan? chocoboInterval = null)
    {
        this.wrapWidth = Math.Max(1, wrapWidth);
        this.wrapHeight = Math.Max(1, wrapHeight);
        this.walkingInterval = Normalize(walkingInterval ?? DefaultWalkingInterval);
        this.chocoboInterval = Normalize(chocoboInterval ?? DefaultChocoboInterval);
    }

    public string LastDiagnostic { get; private set; } = "uninitialized";

    public void Reset()
    {
        previous = null;
        lastFootstepAt = DateTime.MinValue;
        LastDiagnostic = "reset";
    }

    public bool Observe(WorldMapStateSnapshot state, DateTime now)
    {
        if (state.CurrentModule != WorldMapStateReader.WorldModule || !IsFootstepModel(state.PlayerModelId))
        {
            Reset();
            LastDiagnostic = state.CurrentModule != WorldMapStateReader.WorldModule
                ? "not world map"
                : $"model {state.PlayerModelId} has no ground footsteps";
            return false;
        }

        if (previous is null ||
            previous.Value.WorldMapType != state.WorldMapType ||
            previous.Value.PlayerModelId != state.PlayerModelId)
        {
            previous = state;
            lastFootstepAt = now;
            LastDiagnostic = "primed";
            return false;
        }

        var prior = previous.Value;
        previous = state;
        var dx = WorldMapTargetCatalog.WrappedDelta(prior.X, state.X, wrapWidth);
        var dz = WorldMapTargetCatalog.WrappedDelta(prior.Z, state.Z, wrapHeight);
        var dy = state.Y - prior.Y;
        var distance = Math.Sqrt(dx * (double)dx + dy * (double)dy + dz * (double)dz);
        if (distance <= 0.001d)
        {
            LastDiagnostic = "stationary or blocked";
            return false;
        }

        if (!double.IsFinite(distance) || distance > MaximumContinuousDelta)
        {
            lastFootstepAt = now;
            LastDiagnostic = $"discontinuous movement {distance:0.0}";
            return false;
        }

        var interval = IsRiddenChocoboModel(state.PlayerModelId) ? chocoboInterval : walkingInterval;
        if (lastFootstepAt != DateTime.MinValue && now - lastFootstepAt < interval)
        {
            LastDiagnostic = $"moving; cadence wait {(interval - (now - lastFootstepAt)).TotalMilliseconds:0} ms";
            return false;
        }

        lastFootstepAt = now;
        LastDiagnostic = $"accepted movement {distance:0.0}; model={state.PlayerModelId}; terrain={state.TerrainId}";
        return true;
    }

    private static bool IsFootstepModel(int modelId) =>
        modelId is 0 or 1 or 2 || IsRiddenChocoboModel(modelId);

    private static bool IsRiddenChocoboModel(int modelId) =>
        modelId is CaughtChocoboModelId or AlternateRiddenChocoboModelId;

    private static TimeSpan Normalize(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
